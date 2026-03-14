#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>Enum 스키마 관리 UI 섹션</summary>
    class RemoteConfigEnumEditor
    {
        static readonly Color COL_MUTED = new(0.45f, 0.45f, 0.50f);
        static readonly Color COL_SUCCESS = new(0.30f, 0.80f, 0.40f);
        static readonly Color COL_ERROR = new(0.95f, 0.30f, 0.30f);
        static readonly Color COL_INFO = new(0.40f, 0.70f, 0.95f);
        static readonly Color BG_CARD = new(0.14f, 0.14f, 0.18f);

        SchemaData _schema;
        string _schemaFilePath;
        string _newSchemaName = "";
        readonly Dictionary<string, string> _newOptionInputs = new();

        public RemoteConfigEnumEditor(SchemaData schema, string filePath)
        {
            _schema = schema;
            _schemaFilePath = filePath;
        }

        public void UpdateRefs(SchemaData schema, string filePath)
        {
            _schema = schema;
            _schemaFilePath = filePath;
        }

        /// <summary>UI 그리기. true 반환 시 스키마 변경됨.</summary>
        public bool Draw()
        {
            if (_schema == null) return false;

            bool changed = false;
            var enumNames = _schema.EnumDefs.Keys.ToList();

            for (int i = 0; i < enumNames.Count; i++)
            {
                string name = enumNames[i];
                if (!_schema.EnumDefs.ContainsKey(name)) continue;

                if (DrawEnumDef(name, _schema.EnumDefs[name]))
                    changed = true;

                GUILayout.Space(4);
            }

            // 새 스키마 생성
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("새 스키마:", GUILayout.Width(65));
            _newSchemaName = EditorGUILayout.TextField(_newSchemaName);
            GUI.enabled = !string.IsNullOrWhiteSpace(_newSchemaName) &&
                          !_schema.EnumDefs.ContainsKey(_newSchemaName.Trim());
            if (GUILayout.Button("생성", GUILayout.Width(40), GUILayout.Height(18)))
            {
                string trimmed = _newSchemaName.Trim();
                _schema.EnumDefs[trimmed] = new List<string>();
                _newSchemaName = "";
                changed = true;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (changed) Save();
            return changed;
        }

        bool DrawEnumDef(string name, List<string> options)
        {
            bool changed = false;

            // 헤더: 이름 + 삭제
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(name, new GUIStyle(EditorStyles.boldLabel)
                { normal = { textColor = COL_INFO } });
            EditorGUILayout.LabelField($"({options.Count}개)", new GUIStyle(EditorStyles.miniLabel)
                { normal = { textColor = COL_MUTED } }, GUILayout.Width(40));

            if (GUILayout.Button("삭제", EditorStyles.miniButton, GUILayout.Width(36), GUILayout.Height(16)))
            {
                if (EditorUtility.DisplayDialog("Enum 스키마 삭제",
                    $"'{name}' 스키마를 삭제하시겠습니까?\n이 enum을 사용 중인 키는 STRING으로 전환됩니다.",
                    "삭제", "취소"))
                {
                    _schema.EnumDefs.Remove(name);
                    // enumMap에서 이 스키마 참조 제거
                    var keysToRemove = _schema.EnumMap.Where(kv => kv.Value == name).Select(kv => kv.Key).ToList();
                    foreach (var k in keysToRemove) _schema.EnumMap.Remove(k);
                    return true;
                }
            }
            EditorGUILayout.EndHorizontal();

            // 옵션 목록 (인라인 편집)
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(16);
            EditorGUILayout.BeginVertical();

            for (int i = 0; i < options.Count; i++)
            {
                EditorGUILayout.BeginHorizontal(GUILayout.Height(16));
                string edited = EditorGUILayout.TextField(options[i], GUILayout.MinWidth(60));
                if (edited != options[i]) { options[i] = edited; changed = true; }

                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(18), GUILayout.Height(14)))
                {
                    options.RemoveAt(i);
                    changed = true;
                    i--;
                }
                EditorGUILayout.EndHorizontal();
            }

            // 새 옵션 추가
            if (!_newOptionInputs.ContainsKey(name)) _newOptionInputs[name] = "";

            EditorGUILayout.BeginHorizontal(GUILayout.Height(16));
            _newOptionInputs[name] = EditorGUILayout.TextField(_newOptionInputs[name], GUILayout.MinWidth(60));
            GUI.enabled = !string.IsNullOrWhiteSpace(_newOptionInputs[name]);
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(20), GUILayout.Height(14)))
            {
                options.Add(_newOptionInputs[name].Trim());
                _newOptionInputs[name] = "";
                changed = true;
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            return changed;
        }

        void Save()
        {
            if (!string.IsNullOrEmpty(_schemaFilePath))
                RemoteConfigSchema.Save(_schemaFilePath, _schema);
        }
    }
}
#endif
