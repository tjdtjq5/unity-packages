using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// [Icon("atlas")] 로 선언된 아틀라스 키를 런타임에 조회한다 (1회 캐시).
    /// 소스 제너레이터를 건드리지 않고 클라이언트에 아틀라스 키를 노출하기 위한 경량 헬퍼.
    ///
    /// 사용 예: IconAtlas.Of&lt;SkillConfig&gt;("icon_key") → "Common/Skill"
    /// 로더가 SpriteAtlasProvider.GetSpriteAsync(atlasKey, config.icon_key) 형태로 쓸 수 있다.
    /// </summary>
    public static class IconAtlas
    {
        private static readonly ConcurrentDictionary<(Type, string), string> _cache = new();

        /// <summary>type의 fieldName 필드/프로퍼티에 붙은 [Icon]의 아틀라스 키. 없으면 null.</summary>
        public static string Of(Type type, string fieldName)
        {
            return _cache.GetOrAdd((type, fieldName), key =>
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
                var member = (MemberInfo)key.Item1.GetField(key.Item2, flags)
                          ?? key.Item1.GetProperty(key.Item2, flags);
                return member?.GetCustomAttribute<IconAttribute>()?.AtlasKey;
            });
        }

        /// <summary>제네릭 편의 오버로드.</summary>
        public static string Of<T>(string fieldName) => Of(typeof(T), fieldName);
    }
}
