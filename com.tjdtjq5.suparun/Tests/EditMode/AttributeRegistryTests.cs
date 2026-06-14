#nullable enable
using System.Reflection;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>
    /// AttributeRegistry가 직접 GetCustomAttribute와 byte-동일한 분류를 반환하는지 검증.
    /// LocalGameDB(런타임 검증)와 ServerCodeGenerator(마이그레이션, 단위테스트 불가)가 이 동등성에 의존하므로 핵심 안전망.
    /// </summary>
    class AttributeRegistryTests
    {
        class Sample
        {
            [PrimaryKey] public string id = "";
            [NotNull] public string name = "";
            [Unique] public string email = "";
            [MaxLength(10)] public string code = "";
            [Default(5)] public int level;
            [CreatedAt] public long createdAt;
            [UpdatedAt] public long updatedAt;
            public int plain;
            [NotNull, Unique, MaxLength(20)] public string combo = "";  // 다중 속성 필드
        }

        [Test]
        public void Registry_Matches_Direct_Reflection()
        {
            var info = AttributeRegistry.Get(typeof(Sample));
            foreach (var f in typeof(Sample).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.AreEqual(f.GetCustomAttribute<PrimaryKeyAttribute>() != null, info.IsPrimaryKey(f), $"PK {f.Name}");
                Assert.AreEqual(f.GetCustomAttribute<NotNullAttribute>() != null, info.IsNotNull(f), $"NotNull {f.Name}");
                Assert.AreEqual(f.GetCustomAttribute<UniqueAttribute>() != null, info.IsUnique(f), $"Unique {f.Name}");
                Assert.AreEqual(f.GetCustomAttribute<MaxLengthAttribute>()?.Length, info.GetMaxLength(f), $"MaxLen {f.Name}");
                Assert.AreEqual(f.GetCustomAttribute<DefaultAttribute>() != null, info.HasDefault(f), $"HasDefault {f.Name}");
            }
        }

        [Test]
        public void Classifies_Lists_Correctly()
        {
            var info = AttributeRegistry.Get(typeof(Sample));
            Assert.AreEqual("id", info.PrimaryKey!.Name);
            CollectionAssert.AreEquivalent(new[] { "name", "combo" }, System.Array.ConvertAll(info.NotNull, f => f.Name));
            CollectionAssert.AreEquivalent(new[] { "email", "combo" }, System.Array.ConvertAll(info.Unique, f => f.Name));
            Assert.AreEqual(2, info.MaxLength.Length);   // code(10), combo(20)
            Assert.AreEqual(1, info.Default.Length);     // level(5)
            Assert.AreEqual(1, info.CreatedAt.Length);
            Assert.AreEqual(1, info.UpdatedAt.Length);
        }

        [Test]
        public void Get_Is_Cached()
        {
            var a = AttributeRegistry.Get(typeof(Sample));
            var b = AttributeRegistry.Get(typeof(Sample));
            Assert.AreSame(a, b);
        }
    }
}
