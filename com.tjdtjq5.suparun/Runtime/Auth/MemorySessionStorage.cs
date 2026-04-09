#nullable enable
using System.Collections.Generic;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 메모리 기반 세션 저장소. 프로세스 종료 시 모든 데이터가 사라진다.
    /// 주 용도:
    /// - 단위 테스트 (mock 대신 가벼운 실제 구현체)
    /// - 임시 세션 (게스트로 잠깐만 사용 후 종료)
    /// - 보안상 디스크에 토큰을 남기지 않으려는 케이스
    /// </summary>
    public class MemorySessionStorage : ISessionStorage
    {
        readonly Dictionary<string, string> _store = new();

        public void Set(string key, string? value)
        {
            if (string.IsNullOrEmpty(value))
                _store.Remove(key);
            else
                _store[key] = value;
        }

        public string? Get(string key, string? defaultValue = "")
            => _store.TryGetValue(key, out var v) ? v : defaultValue;

        public void SetInt(string key, int value)
            => Set(key, value.ToString());

        public int GetInt(string key, int defaultValue = 0)
        {
            var s = Get(key, null);
            return s != null && int.TryParse(s, out var v) ? v : defaultValue;
        }

        public void Delete(string key)
            => _store.Remove(key);

        public void Save() { /* no-op for memory */ }
    }
}
