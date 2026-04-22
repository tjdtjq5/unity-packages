using System.Collections.Generic;
using Epic.OnlineServices;

namespace Tjdtjq5.EOS.Lobby
{
    /// <summary>
    /// EOS 로비의 스냅샷. native handle(LobbyDetails)은 노출하지 않고
    /// 호출자가 실제로 사용하는 필드만 복사한다.
    /// </summary>
    public sealed class LobbyInfo
    {
        public string LobbyId { get; }
        public ProductUserId OwnerUserId { get; }
        public uint MaxMembers { get; }
        public uint AvailableSlots { get; }
        public string BucketId { get; }
        public IReadOnlyDictionary<string, string> StringAttributes { get; }
        public IReadOnlyDictionary<string, long> Int64Attributes { get; }
        public IReadOnlyList<ProductUserId> Members { get; }

        public uint CurrentMembers => MaxMembers - AvailableSlots;

        public LobbyInfo(
            string lobbyId,
            ProductUserId ownerUserId,
            uint maxMembers,
            uint availableSlots,
            string bucketId,
            IReadOnlyDictionary<string, string> stringAttributes,
            IReadOnlyDictionary<string, long> int64Attributes,
            IReadOnlyList<ProductUserId> members)
        {
            LobbyId = lobbyId;
            OwnerUserId = ownerUserId;
            MaxMembers = maxMembers;
            AvailableSlots = availableSlots;
            BucketId = bucketId;
            StringAttributes = stringAttributes;
            Int64Attributes = int64Attributes;
            Members = members;
        }

        public bool TryGetString(string key, out string value) =>
            StringAttributes.TryGetValue(key, out value);

        public bool TryGetInt64(string key, out long value) =>
            Int64Attributes.TryGetValue(key, out value);

        public bool IsOwnedBy(ProductUserId user) =>
            user != null && OwnerUserId != null && OwnerUserId.ToString() == user.ToString();
    }
}
