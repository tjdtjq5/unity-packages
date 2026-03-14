using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// Inspector에서 필드를 읽기 전용으로 표시.
    /// Unity.Collections.ReadOnly와 이름 충돌 방지를 위해 ReadOnlyField로 명명.
    /// </summary>
    public class ReadOnlyFieldAttribute : PropertyAttribute { }
}
