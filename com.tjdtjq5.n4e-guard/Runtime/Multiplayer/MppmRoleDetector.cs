using Unity.NetCode;
#if UNITY_EDITOR
using Unity.Multiplayer.PlayMode;
#endif

namespace N4EGuard.Multiplayer
{
    /// <summary>
    /// Unity Multiplayer Play Mode(MPPM) 프로세스 역할 판별 유틸.
    ///
    /// 원칙:
    ///   - <b>읽기 전용</b>. EditorPrefs/MultiplayerPlayModePreferences를 수정하지 않는다.
    ///     사용자가 PlayMode Tools Window에서 직접 바꾼 값은 그대로 존중됨.
    ///   - 빌드(non-editor)에서는 MPPM 개념이 없으므로 <see cref="IsMainEditor"/> = true 고정.
    ///
    /// 배경:
    ///   MPPM 2.x의 기본 PlayType(ClientAndServer)으로 VP를 띄우면 VP에도 ServerWorld가 생성되어
    ///   Netcode 공식 <c>DefaultDriverBuilder.ClientUseSocketDriver</c>가 IPC를 택하게 된다.
    ///   그러나 VP는 자체 서버를 listen하지 않으므로 loopback IPC 연결이 실패한다.
    ///   해결: VP 프로세스 감지 시 Client Driver를 무조건 Socket으로 강제.
    /// </summary>
    public static class MppmRoleDetector
    {
        /// <summary>
        /// 현재 프로세스가 Main Editor(호스트 역할 가능)인지 여부.
        /// 빌드에서는 항상 true.
        /// </summary>
        public static bool IsMainEditor
        {
            get
            {
#if UNITY_EDITOR
                return CurrentPlayer.IsMainEditor;
#else
                return true;
#endif
            }
        }

        /// <summary>
        /// 현재 프로세스가 MPPM Virtual Player(리모트 클라이언트 역할)인지 여부.
        /// 빌드에서는 항상 false.
        /// </summary>
        public static bool IsVirtualPlayer => !IsMainEditor;

        /// <summary>
        /// Netcode Client Driver가 Socket Transport를 써야 하는지 판정.
        /// VP 프로세스면 무조건 true, 아니면 Netcode 공식 <see cref="DefaultDriverBuilder.ClientUseSocketDriver"/> 결과 위임.
        ///
        /// 사용처 예: <c>INetworkStreamDriverConstructor.CreateClientDriver</c>에서 IPC/Socket 분기.
        /// </summary>
        public static bool ShouldClientUseSocket(NetDebug netDebug)
        {
            if (IsVirtualPlayer) return true;
            return DefaultDriverBuilder.ClientUseSocketDriver(netDebug);
        }
    }
}
