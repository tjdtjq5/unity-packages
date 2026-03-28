using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tjdtjq5.AddrX
{
    /// <summary>GameObject 파괴 시 바인딩된 핸들을 자동 Dispose하는 컴포넌트.</summary>
    [DisallowMultipleComponent]
    internal sealed class HandleReleaser : MonoBehaviour
    {
        readonly List<IDisposable> _handles = new();

        internal static void Bind(GameObject go, IDisposable handle)
        {
            if (!go.TryGetComponent<HandleReleaser>(out var releaser))
                releaser = go.AddComponent<HandleReleaser>();
            releaser._handles.Add(handle);
        }

        void OnDestroy()
        {
            for (int i = _handles.Count - 1; i >= 0; i--)
            {
                try
                {
                    _handles[i].Dispose();
                }
                catch (Exception e)
                {
                    AddrXLog.Error("HandleReleaser", $"핸들 해제 중 예외: {e.Message}");
                }
            }

            _handles.Clear();
        }
    }
}
