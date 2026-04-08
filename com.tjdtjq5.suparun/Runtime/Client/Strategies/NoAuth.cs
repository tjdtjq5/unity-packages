namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 인증 헤더를 추가하지 않는 전략.
    /// 사용처: 테스트, 공용 endpoint, 또는 명시적으로 인증 없이 호출하고 싶을 때.
    /// </summary>
    public class NoAuth : IAuthStrategy
    {
        public void Apply(HttpTransportRequest request) { }
    }
}
