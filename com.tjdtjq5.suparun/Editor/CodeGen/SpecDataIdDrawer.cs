using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// [SpecDataId(typeof(XxxConfig))] string 필드를 XxxConfig의 PK 값들로 채운 검색 드롭다운으로 렌더.
    /// PK 값은 IdConstantGenerator가 만든 SpecDataIdIndex.ByConfig[config 이름]에서 읽는다.
    /// 인덱스가 없거나 해당 config가 없으면 텍스트 필드 + 경고로 폴백(loud).
    /// </summary>
    [CustomPropertyDrawer(typeof(SpecDataIdAttribute))]
    public class SpecDataIdDrawer : PropertyDrawer
    {
        static readonly HashSet<Type> _warned = new();

        // SpecDataIdIndex.ByConfig — 도메인 리로드까지 캐시. Generate 후 리로드로 자동 갱신.
        static Dictionary<string, string[]> _index;
        static bool _indexLoaded;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var attr = (SpecDataIdAttribute)attribute;
            var ids = GetIds(attr.ConfigType);

            var fieldRect = EditorGUI.PrefixLabel(position, label);

            if (ids == null)
            {
                if (attr.ConfigType != null && _warned.Add(attr.ConfigType))
                    Debug.LogWarning($"[SupaRun] {attr.ConfigType.Name}의 PK 인덱스를 못 찾았습니다. " +
                        "SupaRun 대시보드 > Deploy > Generate Id Constants를 실행하세요.");
                property.stringValue = EditorGUI.TextField(fieldRect, property.stringValue);
                return;
            }

            var current = string.IsNullOrEmpty(property.stringValue) ? "(none)" : property.stringValue;
            if (GUI.Button(fieldRect, current, EditorStyles.popup))
            {
                // AdvancedDropdown 콜백은 다음 프레임에 비동기 호출 → 지금 property는 stale.
                // 콜백에서 target + path로 새 SerializedObject를 다시 얻어 써야 반영된다.
                var targets = property.serializedObject.targetObjects;
                var path = property.propertyPath;
                var picker = new IdSearchDropdown(new AdvancedDropdownState(), ids, picked =>
                {
                    foreach (var t in targets)
                    {
                        if (t == null) continue;
                        var so = new SerializedObject(t);
                        var p = so.FindProperty(path);
                        if (p == null) continue;
                        p.stringValue = picked;
                        so.ApplyModifiedProperties();
                    }
                    // 비동기 콜백이라 인스펙터가 자동 갱신 안 될 수 있음 → 강제 repaint
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                });
                picker.Show(fieldRect);
            }
        }

        // config 이름으로 SpecDataIdIndex에서 PK 값 조회. 인덱스/키 없으면 null.
        static string[] GetIds(Type configType)
        {
            if (configType == null) return null;
            if (!_indexLoaded) { _index = LoadIndex(); _indexLoaded = true; }
            if (_index != null && _index.TryGetValue(configType.Name, out var ids)) return ids;
            return null;
        }

        static Dictionary<string, string[]> LoadIndex()
        {
            var t = FindTypeByName("SpecDataIdIndex");
            var f = t?.GetField("ByConfig", BindingFlags.Public | BindingFlags.Static);
            return f?.GetValue(null) as Dictionary<string, string[]>;
        }

        static Type FindTypeByName(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }
                // static class = abstract + sealed
                var t = types.FirstOrDefault(x => x.Name == name && x.IsClass && x.IsAbstract && x.IsSealed);
                if (t != null) return t;
            }
            return null;
        }
    }

    /// <summary>검색창이 딸린 문자열 선택 드롭다운 (AdvancedDropdown 기본 제공). item.name으로 매핑.</summary>
    class IdSearchDropdown : AdvancedDropdown
    {
        const string NoneLabel = "(none)";
        readonly string[] _items;
        readonly Action<string> _onPick;

        public IdSearchDropdown(AdvancedDropdownState state, string[] items, Action<string> onPick) : base(state)
        {
            _items = items;
            _onPick = onPick;
            minimumSize = new Vector2(220, 320);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Id");
            root.AddChild(new AdvancedDropdownItem(NoneLabel));
            foreach (var s in _items.OrderBy(x => x, StringComparer.Ordinal))
                root.AddChild(new AdvancedDropdownItem(s));
            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            _onPick?.Invoke(item.name == NoneLabel ? "" : item.name);
        }
    }
}
