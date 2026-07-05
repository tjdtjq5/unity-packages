using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// IdConstantGenerator의 출력 위치·네임스페이스를 프로젝트가 선언한다 (assembly 단위).
    /// 미선언 시 기본값(Assets/Generated/SupaRunIds, global namespace)을 쓴다.
    ///
    /// 예:
    ///   [assembly: SupaRunIdsConfig(
    ///       OutputDir = "Assets/_Project/2_Scripts/_Core/_Generated",
    ///       Namespace = "SurvivorsDuo.Core")]
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public class SupaRunIdsConfigAttribute : Attribute
    {
        /// <summary>생성 .g.cs를 쓸 프로젝트 상대 경로 (Assets/...).</summary>
        public string OutputDir { get; set; }

        /// <summary>생성 클래스의 네임스페이스. 빈 값이면 global.</summary>
        public string Namespace { get; set; }
    }
}
