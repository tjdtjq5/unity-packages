#nullable enable
using NUnit.Framework;

namespace Tjdtjq5.SupaRun.Tests
{
    class SessionStorageTests
    {
        MemorySessionStorage _storage = null!;

        [SetUp]
        public void SetUp()
        {
            _storage = new MemorySessionStorage();
        }

        [Test]
        public void Set_Get_RoundTrip()
        {
            _storage.Set("key1", "value1");
            Assert.AreEqual("value1", _storage.Get("key1"));
        }

        [Test]
        public void Get_Missing_Key_Returns_Default()
        {
            Assert.AreEqual("fallback", _storage.Get("missing", "fallback"));
        }

        [Test]
        public void SetInt_GetInt_RoundTrip()
        {
            _storage.SetInt("count", 42);
            Assert.AreEqual(42, _storage.GetInt("count"));
        }

        [Test]
        public void GetInt_Missing_Key_Returns_Default()
        {
            Assert.AreEqual(99, _storage.GetInt("missing", 99));
        }

        [Test]
        public void Delete_Removes_Key()
        {
            _storage.Set("key1", "value1");
            _storage.Delete("key1");
            Assert.AreEqual("default", _storage.Get("key1", "default"));
        }

        [Test]
        public void Set_Null_Removes_Key()
        {
            _storage.Set("key1", "value1");
            _storage.Set("key1", null);
            Assert.AreEqual("default", _storage.Get("key1", "default"));
        }

        [Test]
        public void Save_Does_Not_Throw()
        {
            _storage.Set("key1", "value1");
            Assert.DoesNotThrow(() => _storage.Save());
        }
    }
}
