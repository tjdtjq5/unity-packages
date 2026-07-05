using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// string 컬럼이 SpriteAtlas 안의 sprite 이름임을 선언한다.
    /// 어드민 툴은 이 아틀라스의 sprite 목록을 썸네일 드롭다운으로 렌더한다.
    /// 값 자체는 여전히 sprite 이름 string (DB 컬럼 text, 마이그레이션 영향 없음).
    /// 클라이언트는 IconAtlas.Of&lt;T&gt;(fieldName)로 아틀라스 키를 얻을 수 있다.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class IconAttribute : Attribute
    {
        /// <summary>SpriteAtlas의 Addressables 키 (예: "Common/FieldOrb").</summary>
        public string AtlasKey { get; }

        public IconAttribute(string atlasKey)
        {
            AtlasKey = atlasKey;
        }
    }
}
