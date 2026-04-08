namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 인증 세션(액세스 토큰, 리프레시 토큰, 유저 ID 등) 저장소 추상화.
    /// SupaRunAuth가 정적 SecureStorage에 직접 의존하지 않도록 분리한 인터페이스.
    ///
    /// 구현체:
    /// - <see cref="SecureSessionStorage"/> : 플랫폼 보안 저장소 (iOS Keychain / Android KeyStore / Editor PlayerPrefs)
    /// - <see cref="MemorySessionStorage"/> : 메모리 Dictionary (테스트 / 임시 세션)
    ///
    /// 인터페이스는 KV(Key-Value) API. 실제로는 SupaRunAuth 내부의 4개 키
    /// (AccessToken, RefreshToken, UserId, IsGuest)만 저장한다.
    /// </summary>
    public interface ISessionStorage
    {
        /// <summary>문자열 저장. value가 빈 문자열/null이면 키 삭제와 동일 의미.</summary>
        void Set(string key, string value);

        /// <summary>문자열 읽기. 키가 없으면 defaultValue 반환.</summary>
        string Get(string key, string defaultValue = "");

        /// <summary>정수 저장.</summary>
        void SetInt(string key, int value);

        /// <summary>정수 읽기. 키가 없거나 파싱 실패 시 defaultValue 반환.</summary>
        int GetInt(string key, int defaultValue = 0);

        /// <summary>키 삭제.</summary>
        void Delete(string key);

        /// <summary>플랫폼 저장소에 동기 플러시 (예: PlayerPrefs.Save). 메모리 구현체는 no-op.</summary>
        void Save();
    }
}
