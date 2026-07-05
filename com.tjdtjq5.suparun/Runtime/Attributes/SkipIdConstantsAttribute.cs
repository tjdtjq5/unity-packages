using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 이 [SpecData] 테이블을 IdConstantGenerator 대상에서 제외한다.
    /// enum↔pk 페어링을 손으로 관리하는 브리지 테이블(예: PlayerStatConfig ↔ StatType/StatIds/StatIdMapping)에 붙인다.
    /// 클라 전용 마커 — DeployManager.StripForServer가 서버 빌드에서 제거한다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class SkipIdConstantsAttribute : Attribute { }
}
