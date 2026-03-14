#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    [CustomPropertyDrawer(typeof(SeparatorAttribute))]
    public class SeparatorDrawer : DecoratorDrawer
    {
        public override float GetHeight()
        {
            return ((SeparatorAttribute)attribute).Thickness + 8f;
        }

        public override void OnGUI(Rect position)
        {
            var attr = (SeparatorAttribute)attribute;
            float y = position.y + 4f;
            EditorGUI.DrawRect(new Rect(position.x, y, position.width, attr.Thickness), attr.Color);
        }
    }
}
#endif
