#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(HelpBoxAttribute))]
    public class HelpBoxDrawer : DecoratorDrawer
    {
        public override float GetHeight()
        {
            var attr = (HelpBoxAttribute)attribute;
            float textHeight = EditorStyles.helpBox.CalcHeight(
                new GUIContent(attr.Message), EditorGUIUtility.currentViewWidth - 40);
            return Mathf.Max(38f, textHeight + 10f) + 4f;
        }

        public override void OnGUI(Rect position)
        {
            var attr = (HelpBoxAttribute)attribute;
            var msgType = attr.Type switch
            {
                HelpBoxType.Warning => MessageType.Warning,
                HelpBoxType.Error => MessageType.Error,
                _ => MessageType.Info
            };
            var boxRect = new Rect(position.x, position.y, position.width, GetHeight() - 4f);
            EditorGUI.HelpBox(boxRect, attr.Message, msgType);
        }
    }
}
#endif
