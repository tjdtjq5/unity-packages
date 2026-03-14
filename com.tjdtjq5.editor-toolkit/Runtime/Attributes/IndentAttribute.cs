using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// Inspector에서 들여쓰기 레벨을 조정.
    /// [Indent] 또는 [Indent(2)]
    /// </summary>
    public class IndentAttribute : PropertyAttribute
    {
        public int Level { get; }
        public IndentAttribute() { Level = 1; }
        public IndentAttribute(int level) { Level = level; }
    }
}
