using System;
using UnityEngine;

namespace Tjdtjq5.AddrX
{
    /// <summary>
    /// AddrX가 생성한 인스턴스에 부착하는 내부 태그. 원본 key와 인스턴스 핸들을 보관해
    /// <c>AddrX.Destroy(go)</c>가 GO만으로 해당 핸들을 찾아 Dispose할 수 있게 한다.
    /// 풀 반환 후 재사용(Pop) 시 새 핸들로 덮어쓴다.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class AddrXInstanceTag : MonoBehaviour
    {
        internal object Key;
        internal IDisposable Handle;
    }
}
