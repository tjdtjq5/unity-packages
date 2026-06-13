#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>
    /// LocalGameDB 직렬화 검증. 시스템 표준 Newtonsoft로 통일됨 — JsonUtility가 못 다루던
    /// property/Dictionary가 보존되어 Realtime/REST/서버(모두 Newtonsoft) 경로와 일치해야 한다.
    /// </summary>
    class LocalGameDbTests
    {
        class Item
        {
            [PrimaryKey] public string id = "";
            public int score;                          // 필드 — 두 직렬화 모두 처리
            public int Bonus { get; set; }             // property — JsonUtility는 무시(손실), Newtonsoft는 보존
            public Dictionary<string, int> meta = new(); // Dictionary — JsonUtility 불가, Newtonsoft 가능
        }

        [Test]
        public async Task Save_Get_Roundtrips_Property_And_Dictionary()
        {
            var db = new LocalGameDB();
            await db.Save(new Item
            {
                id = "a",
                score = 7,
                Bonus = 42,
                meta = new Dictionary<string, int> { ["x"] = 1, ["y"] = 2 },
            });

            var loaded = await db.Get<Item>("a");

            Assert.IsNotNull(loaded);
            Assert.AreEqual(7, loaded.score);
            Assert.AreEqual(42, loaded.Bonus);          // JsonUtility였다면 0 (property 무시)
            Assert.AreEqual(2, loaded.meta.Count);      // JsonUtility였다면 비어 있음
            Assert.AreEqual(1, loaded.meta["x"]);
        }

        [Test]
        public async Task GetAll_Returns_All_Saved()
        {
            var db = new LocalGameDB();
            await db.Save(new Item { id = "a", score = 1 });
            await db.Save(new Item { id = "b", score = 2 });

            var all = await db.GetAll<Item>();

            Assert.AreEqual(2, all.Count);
        }
    }
}
