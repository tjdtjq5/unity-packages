#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor.Tools
{
    /// <summary>
    /// Domain Reload + Scene Reload 비활성화 시 문제가 될 수 있는 패턴을 자동 감지.
    /// 스크립트 컴파일 후 자동 실행, 문제 있을 때만 Console Warning 출력.
    /// 무시 목록은 EditorPrefs에 저장되어 에디터 윈도우에서 관리 가능.
    /// </summary>
    static class DomainReloadValidator
    {
        // 검사 대상 어셈블리 (프로젝트 코드만)
        const string TargetAssembly = "Assembly-CSharp";

        // EditorPrefs 키
        const string KEY_IGNORE_NS = "DomainReloadValidator_IgnoreNamespaces";
        const string KEY_IGNORE_TYPES = "DomainReloadValidator_IgnoreTypes";
        const string KEY_ENABLED = "DomainReloadValidator_Enabled";

        // 기본 무시 네임스페이스 (서드파티)
        static readonly string[] DefaultIgnoreNamespaces =
        {
            "GameCreator", "DG.Tweening", "Zenject", "VContainer",
            "TMPro", "Spine", "UnityEngine", "UnityEditor",
            "NinjutsuGames", "ChocDino", "LayerLab", "SuperScrollView",
        };

        /// <summary>검증 활성화 여부</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(KEY_ENABLED, true);
            set => EditorPrefs.SetBool(KEY_ENABLED, value);
        }

        /// <summary>무시할 네임스페이스 목록 (EditorPrefs 저장)</summary>
        public static List<string> GetIgnoreNamespaces()
        {
            string saved = EditorPrefs.GetString(KEY_IGNORE_NS, "");
            if (string.IsNullOrEmpty(saved))
                return new List<string>(DefaultIgnoreNamespaces);
            return saved.Split('|').Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        public static void SetIgnoreNamespaces(List<string> list)
        {
            EditorPrefs.SetString(KEY_IGNORE_NS, string.Join("|", list));
        }

        /// <summary>무시할 타입 전체 이름 목록 (EditorPrefs 저장)</summary>
        public static List<string> GetIgnoreTypes()
        {
            string saved = EditorPrefs.GetString(KEY_IGNORE_TYPES, "");
            if (string.IsNullOrEmpty(saved)) return new List<string>();
            return saved.Split('|').Where(s => !string.IsNullOrEmpty(s)).ToList();
        }

        public static void SetIgnoreTypes(List<string> list)
        {
            EditorPrefs.SetString(KEY_IGNORE_TYPES, string.Join("|", list));
        }

        /// <summary>기본값으로 리셋</summary>
        public static void ResetToDefaults()
        {
            EditorPrefs.DeleteKey(KEY_IGNORE_NS);
            EditorPrefs.DeleteKey(KEY_IGNORE_TYPES);
        }

        [DidReloadScripts]
        static void OnScriptsReloaded()
        {
            if (!Enabled) return;

            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == TargetAssembly);
            if (assembly == null) return;

            var ignoreNs = GetIgnoreNamespaces();
            var ignoreTypes = new HashSet<string>(GetIgnoreTypes());

            var issues = new List<string>();
            var types = GetProjectTypes(assembly, ignoreNs, ignoreTypes);

            foreach (var type in types)
            {
                CheckStaticFields(type, issues);
                CheckStaticEvents(type, issues);
            }

            foreach (var issue in issues)
                Debug.LogWarning($"[DomainReloadValidator] {issue}");
        }

        static IEnumerable<Type> GetProjectTypes(Assembly assembly, List<string> ignoreNs, HashSet<string> ignoreTypes)
        {
            try
            {
                return assembly.GetTypes()
                    .Where(t => t.Namespace != null
                        && !ignoreNs.Any(ns => t.Namespace.StartsWith(ns))
                        && !ignoreTypes.Contains(t.FullName)
                        && !IsBurstGenerated(t));
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null
                    && t.Namespace != null
                    && !ignoreNs.Any(ns => t.Namespace.StartsWith(ns))
                    && !ignoreTypes.Contains(t.FullName)
                    && !IsBurstGenerated(t));
            }
        }

        // --- 검사 1: static 필드가 있는데 RuntimeInitializeOnLoadMethod 리셋이 없는 타입 ---

        static void CheckStaticFields(Type type, List<string> issues)
        {
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            var staticFields = type.GetFields(flags)
                .Where(f => !f.IsLiteral
                    && !f.IsInitOnly
                    && !f.FieldType.IsEnum
                    && !IsCompilerGenerated(f))
                .ToList();

            var staticProps = type.GetProperties(flags)
                .Where(p => p.GetSetMethod(true) != null
                    && p.GetSetMethod(true).IsStatic
                    && !IsCompilerGenerated(p))
                .ToList();

            if (staticFields.Count == 0 && staticProps.Count == 0) return;
            if (HasRuntimeInitReset(type)) return;
            if (type.IsAbstract && type.IsSealed && AllFieldsAreCache(staticFields)) return;

            var fieldNames = staticFields.Select(f => f.Name)
                .Concat(staticProps.Select(p => p.Name));
            issues.Add($"{type.FullName}: static 멤버 [{string.Join(", ", fieldNames)}] " +
                "발견 — [RuntimeInitializeOnLoadMethod] 리셋 없음");
        }

        // --- 검사 2: static event 리셋 누락 ---

        static void CheckStaticEvents(Type type, List<string> issues)
        {
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            var staticEvents = type.GetEvents(flags)
                .Where(e => !IsCompilerGenerated(e))
                .ToList();

            if (staticEvents.Count == 0) return;
            if (HasRuntimeInitReset(type)) return;

            var eventNames = staticEvents.Select(e => e.Name);
            issues.Add($"{type.FullName}: static event [{string.Join(", ", eventNames)}] " +
                "발견 — [RuntimeInitializeOnLoadMethod] 리셋 없음");
        }

        // --- 헬퍼 ---

        static bool HasRuntimeInitReset(Type type)
        {
            var methods = type.GetMethods(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return methods.Any(m =>
                m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() != null);
        }

        static bool IsBurstGenerated(Type type)
        {
            string name = type.Name;
            return name.Contains("$BurstDirectCall") || name.Contains("__codegen__");
        }

        static bool IsCompilerGenerated(MemberInfo member)
        {
            return member.Name.StartsWith("<")
                || member.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false).Length > 0;
        }

        static bool AllFieldsAreCache(List<FieldInfo> fields)
        {
            return fields.All(f =>
                f.FieldType.IsGenericType &&
                (f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>)
                || f.FieldType.GetGenericTypeDefinition() == typeof(HashSet<>)
                || f.FieldType.GetGenericTypeDefinition() == typeof(List<>)));
        }
    }

    /// <summary>
    /// DomainReloadValidator 설정 윈도우.
    /// Tools > Domain Reload Validator Settings
    /// </summary>
    class DomainReloadValidatorWindow : EditorWindow
    {
        Vector2 _scrollNs, _scrollTypes;
        string _newNs = "";
        string _newType = "";
        List<string> _ignoreNs;
        List<string> _ignoreTypes;

        [MenuItem("Tools/Domain Reload Validator Settings")]
        static void Open()
        {
            var wnd = GetWindow<DomainReloadValidatorWindow>();
            wnd.titleContent = new GUIContent("Domain Reload Validator");
            wnd.minSize = new Vector2(400, 300);
        }

        void OnEnable()
        {
            _ignoreNs = DomainReloadValidator.GetIgnoreNamespaces();
            _ignoreTypes = DomainReloadValidator.GetIgnoreTypes();
        }

        void OnGUI()
        {
            // 활성화 토글
            EditorGUILayout.BeginHorizontal();
            bool enabled = EditorGUILayout.ToggleLeft("검증 활성화", DomainReloadValidator.Enabled,
                EditorStyles.boldLabel);
            if (enabled != DomainReloadValidator.Enabled)
                DomainReloadValidator.Enabled = enabled;

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("기본값 복원", GUILayout.Width(80)))
            {
                DomainReloadValidator.ResetToDefaults();
                _ignoreNs = DomainReloadValidator.GetIgnoreNamespaces();
                _ignoreTypes = DomainReloadValidator.GetIgnoreTypes();
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(8);

            // 무시 네임스페이스
            EditorGUILayout.LabelField("무시할 네임스페이스", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("이 네임스페이스로 시작하는 타입은 검사하지 않습니다.", MessageType.Info);

            _scrollNs = EditorGUILayout.BeginScrollView(_scrollNs, GUILayout.MaxHeight(150));
            for (int i = 0; i < _ignoreNs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(_ignoreNs[i]);
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    _ignoreNs.RemoveAt(i);
                    DomainReloadValidator.SetIgnoreNamespaces(_ignoreNs);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            _newNs = EditorGUILayout.TextField(_newNs);
            if (GUILayout.Button("+", GUILayout.Width(22)) && !string.IsNullOrWhiteSpace(_newNs))
            {
                _ignoreNs.Add(_newNs.Trim());
                DomainReloadValidator.SetIgnoreNamespaces(_ignoreNs);
                _newNs = "";
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(12);

            // 무시 타입
            EditorGUILayout.LabelField("무시할 타입 (전체 이름)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("특정 타입만 개별적으로 무시합니다. 예: MyGame.PlayerManager", MessageType.Info);

            _scrollTypes = EditorGUILayout.BeginScrollView(_scrollTypes, GUILayout.MaxHeight(150));
            for (int i = 0; i < _ignoreTypes.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(_ignoreTypes[i]);
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    _ignoreTypes.RemoveAt(i);
                    DomainReloadValidator.SetIgnoreTypes(_ignoreTypes);
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            _newType = EditorGUILayout.TextField(_newType);
            if (GUILayout.Button("+", GUILayout.Width(22)) && !string.IsNullOrWhiteSpace(_newType))
            {
                _ignoreTypes.Add(_newType.Trim());
                DomainReloadValidator.SetIgnoreTypes(_ignoreTypes);
                _newType = "";
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
