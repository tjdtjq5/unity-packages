using System.Collections.Generic;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using UnityEngine;

namespace Tjdtjq5.EOS.Transport
{
    /// <summary>
    /// EOS P2P 연결에 필요한 managed 상태를 보관하는 컨텍스트.
    /// EOSP2PNetworkInterface(struct)에서 인덱스로 참조한다.
    /// </summary>
    internal sealed class EOSContext
    {
        public P2PInterface P2P;
        public ProductUserId LocalUserId;
        public string SocketName;
        public EOSTransportPoller Poller;
    }

    /// <summary>
    /// EOSContext를 인덱스 기반으로 관리하는 static 저장소.
    /// unmanaged struct에서 managed 객체를 참조하기 위한 브릿지.
    /// </summary>
    internal static class EOSContextStore
    {
        // 인덱스 0은 예약 (struct 기본값 0 = 미초기화 상태)
        static readonly List<EOSContext> s_Contexts = new() { null };
        static readonly Queue<int> s_FreeIndices = new();

        public static int Alloc(EOSContext ctx)
        {
            if (s_FreeIndices.Count > 0)
            {
                int idx = s_FreeIndices.Dequeue();
                s_Contexts[idx] = ctx;
                return idx;
            }

            s_Contexts.Add(ctx);
            return s_Contexts.Count - 1;
        }

        public static EOSContext Get(int index)
        {
            if (index < 0 || index >= s_Contexts.Count || s_Contexts[index] == null)
            {
                Debug.LogError("[EOS Transport] Invalid context index");
                return null;
            }

            return s_Contexts[index];
        }

        public static void Free(int index)
        {
            if (index >= 0 && index < s_Contexts.Count)
            {
                s_Contexts[index] = null;
                s_FreeIndices.Enqueue(index);
            }
        }
    }
}
