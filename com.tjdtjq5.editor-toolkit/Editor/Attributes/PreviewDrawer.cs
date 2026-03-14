#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(PreviewAttribute))]
    public class PreviewDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference)
            {
                EditorGUI.PropertyField(position, property, label, true);
                return;
            }

            var attr = (PreviewAttribute)attribute;
            float fieldHeight = EditorGUIUtility.singleLineHeight;

            // 오브젝트 필드
            var fieldRect = new Rect(position.x, position.y, position.width, fieldHeight);
            EditorGUI.PropertyField(fieldRect, property, label);

            // 미리보기
            var obj = property.objectReferenceValue;
            if (obj == null) return;

            Texture2D preview = null;

            if (obj is Sprite sprite)
                preview = AssetPreview.GetAssetPreview(sprite);
            else if (obj is Texture2D tex)
                preview = tex;
            else
                preview = AssetPreview.GetAssetPreview(obj);

            if (preview == null) return;

            int size = attr.Size;
            var previewRect = new Rect(
                position.x + EditorGUIUtility.labelWidth + 2,
                position.y + fieldHeight + 2,
                size, size);

            // 배경
            EditorGUI.DrawRect(previewRect, new Color(0.10f, 0.10f, 0.13f));
            GUI.DrawTexture(previewRect, preview, ScaleMode.ScaleToFit);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;

            if (property.propertyType == SerializedPropertyType.ObjectReference
                && property.objectReferenceValue != null)
            {
                var attr = (PreviewAttribute)attribute;
                height += attr.Size + 4f;
            }

            return height;
        }
    }
}
#endif
