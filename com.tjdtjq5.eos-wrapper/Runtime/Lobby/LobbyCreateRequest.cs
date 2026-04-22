using System.Collections.Generic;

namespace Tjdtjq5.EOS.Lobby
{
    /// <summary>
    /// EOSLobbyService.CreateAsync 에 전달하는 요청 POCO.
    /// 게임 고유 규약(attribute 의미 등)은 호출자가 채운다.
    /// </summary>
    public sealed class LobbyCreateRequest
    {
        /// <summary>EOS 표준 BucketId(서버 사이드 1차 필터). 필수.</summary>
        public string BucketId { get; set; }

        /// <summary>최대 멤버 수(본인 포함).</summary>
        public uint MaxMembers { get; set; } = 2;

        /// <summary>초기에 세팅할 문자열 attribute.</summary>
        public Dictionary<string, string> StringAttributes { get; } = new();

        /// <summary>초기에 세팅할 Int64 attribute.</summary>
        public Dictionary<string, long> Int64Attributes { get; } = new();

        /// <summary>Presence(친구 UI) 노출 여부.</summary>
        public bool PresenceEnabled { get; set; } = false;

        /// <summary>호스트 마이그레이션 비활성화 여부(2인 co-op은 호스트 이탈 시 세션 종료).</summary>
        public bool DisableHostMigration { get; set; } = true;
    }
}
