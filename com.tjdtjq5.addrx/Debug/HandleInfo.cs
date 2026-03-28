#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using UnityEngine;

namespace Tjdtjq5.AddrX.Debug
{
    /// <summary>추적 중인 핸들의 정보.</summary>
    public readonly struct HandleInfo
    {
        public readonly int Id;
        public readonly string Address;
        public readonly Type AssetType;
        public readonly float CreatedAt;
        public readonly string StackTrace;

        public HandleInfo(int id, string address, Type assetType, float createdAt, string stackTrace)
        {
            Id = id;
            Address = address;
            AssetType = assetType;
            CreatedAt = createdAt;
            StackTrace = stackTrace;
        }

        /// <summary>생성 후 경과 시간(초).</summary>
        public float Age => Time.realtimeSinceStartup - CreatedAt;
    }
}
#endif
