using System;
using UnityEngine;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// Unity 인스펙터에서 이 문자열 필드를 [SpecData] 타입(configType)의 PK 값들로 채운
    /// 검색 드롭다운으로 렌더한다. SpecDataIdDrawer가 처리.
    ///
    /// 예: [SpecDataId(typeof(EnemyConfig))] public string enemyId;
    /// 값은 여전히 문자열(id) — 저장 형식 그대로, 잘못 입력(오타)만 막는다.
    ///
    /// PK 값은 IdConstantGenerator가 만든 SpecDataIdIndex에서 읽는다 (모든 SpecData 테이블 포함,
    /// [SkipIdConstants] 테이블도). 인덱스가 없으면 텍스트 필드 + 경고로 폴백
    /// (대시보드 > Deploy > Generate Id Constants로 생성).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class SpecDataIdAttribute : PropertyAttribute
    {
        public Type ConfigType { get; }

        public SpecDataIdAttribute(Type configType)
        {
            ConfigType = configType;
        }
    }
}
