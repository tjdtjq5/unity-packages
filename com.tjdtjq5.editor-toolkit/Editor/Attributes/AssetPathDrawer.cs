#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(AssetPathAttribute))]
    public class AssetPathDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var attr = (AssetPathAttribute)attribute;

            EditorGUI.BeginProperty(position, label, property);

            // 라벨
            position = EditorGUI.PrefixLabel(position, label);

            // 경로 텍스트 (읽기 전용)
            float pickerWidth = 60f;
            var pathRect = new Rect(position.x, position.y, position.width - pickerWidth - 4, position.height);
            var pickerRect = new Rect(position.x + position.width - pickerWidth, position.y, pickerWidth, position.height);

            var prev = GUI.enabled;
            GUI.enabled = false;
            EditorGUI.TextField(pathRect, property.stringValue);
            GUI.enabled = prev;

            // 현재 경로에서 에셋 로드 (미리보기용)
            Object currentAsset = null;
            if (!string.IsNullOrEmpty(property.stringValue))
                currentAsset = AssetDatabase.LoadAssetAtPath(property.stringValue, attr.AssetType);

            // 오브젝트 피커
            EditorGUI.BeginChangeCheck();
            var newAsset = EditorGUI.ObjectField(pickerRect, currentAsset, attr.AssetType, false);
            if (EditorGUI.EndChangeCheck())
            {
                property.stringValue = newAsset != null
                    ? AssetDatabase.GetAssetPath(newAsset)
                    : string.Empty;
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
            => EditorGUIUtility.singleLineHeight;
    }
}
#endif
