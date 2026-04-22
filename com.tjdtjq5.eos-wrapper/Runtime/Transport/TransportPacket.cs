using Unity.Networking.Transport;

namespace Tjdtjq5.EOS.Transport
{
    /// <summary>
    /// MonoBehaviour(EOSTransportPoller) ↔ Job 사이에서
    /// NativeQueue로 패킷을 주고받기 위한 unmanaged 구조체.
    /// </summary>
    internal unsafe struct TransportPacket
    {
        public const int MaxPayloadSize = 1170; // EOS P2P MTU (1170 bytes)

        public NetworkEndpoint Endpoint;
        public int DataLength;
        public fixed byte Data[MaxPayloadSize];
    }
}
