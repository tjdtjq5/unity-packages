using System.Collections.Generic;

namespace Tjdtjq5.EOS.Lobby
{
    /// <summary>
    /// EOSLobbyService.SearchAsync 에 전달하는 필터.
    /// BucketId는 EOS 서버 측 1차 필터, Attribute/AvailableSlot 는 2차 필터.
    /// </summary>
    public sealed class LobbySearchCriteria
    {
        public string BucketId { get; set; }

        /// <summary>attribute key == value 일치 필터(서버 사이드).</summary>
        public Dictionary<string, string> RequireStringEquals { get; } = new();

        public uint MaxResults { get; set; } = 10;

        /// <summary>true면 AvailableSlots &gt; 0 조건 클라이언트 필터 적용.</summary>
        public bool RequireAvailableSlot { get; set; } = true;
    }
}
