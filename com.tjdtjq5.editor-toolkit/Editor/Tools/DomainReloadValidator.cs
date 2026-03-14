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
    /// </summary>
    static class DomainReloadValidator
    {
        // 검사 대상 어셈블리 (프로젝트 코드만)
        const string TargetAssembly = "Assembly-CSharp";

        // 무시할 네임스페이스 접두사
        static readonly string[] IgnoreNamespaces =
        {
            "GameCreator", "DG.Tweening", "Zenject", "VContainer",
            "TMPro", "Spine", "UnityEngine", "UnityEditor",
            "NinjutsuGames", "ChocDino",
        };

        [DidReloadScripts]
        static void OnScriptsReloaded()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == TargetAssembly);
            if (assembly == null) return;

            var issues = new List<string>();
            var types = GetProjectTypes(assembly);

            foreach (var type in types)
            {
                CheckStaticFields(type, issues);
                CheckStaticEvents(type, issues);
            }

            foreach (var issue in issues)
                Debug.LogWarning($"[DomainReloadValidator] {issue}");
        }

        static IEnumerable<Type> GetProjectTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes()
                    .Where(t => t.Namespace != null
                        && !IgnoreNamespaces.Any(ns => t.Namespace.StartsWith(ns)));
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null
                    && t.Namespace != null
                    && !IgnoreNamespaces.Any(ns => t.Namespace.StartsWith(ns)));
            }
        }

        // --- 검사 1: static 필드가 있는데 RuntimeInitializeOnLoadMethod 리셋이 없는 타입 ---

        static void CheckStaticFields(Type type, List<string> issues)
        {
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            var staticFields = type.GetFields(flags)
                .Where(f => !f.IsLiteral          // const 제외
                    && !f.IsInitOnly               // readonly 제외
                    && !f.FieldType.IsEnum          // enum 제외
                    && !IsCompilerGenerated(f))     // backing field 등 제외
                .ToList();

            var staticProps = type.GetProperties(flags)
                .Where(p => p.GetSetMethod(true) != null
                    && p.GetSetMethod(true).IsStatic
                    && !IsCompilerGenerated(p))
                .ToList();

            if (staticFields.Count == 0 && staticProps.Count == 0) return;

            // RuntimeInitializeOnLoadMethod가 있는지 확인
            if (HasRuntimeInitReset(type)) return;

            // static class (유틸리티)에서 캐시용 Dictionary/HashSet은 허용
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
}
