using NUnit.Framework;
using Tjdtjq5.SupaRun.Editor;

namespace Tjdtjq5.SupaRun.Tests
{
    /// <summary>SettingsView에서 추출한 AuthConfigParser(손수 JSON 파싱 통합) 검증.</summary>
    class AuthConfigParserTests
    {
        const string Json = @"{
            ""google_enabled"": true,
            ""google_client_id"": ""abc123"",
            ""apple_enabled"": false,
            ""discord_enabled"": true,
            ""discord_client_id"": """",
            ""kakao_enabled"": true,
            ""kakao_client_id"": null
        }";

        [TestCase("google_enabled", true)]
        [TestCase("apple_enabled", false)]
        [TestCase("missing_enabled", false)]
        public void IsFieldTrue_Works(string field, bool expected)
            => Assert.AreEqual(expected, AuthConfigParser.IsFieldTrue(Json, field));

        [Test]
        public void IsFieldTrue_NullOrEmpty_Json_Is_False()
        {
            Assert.IsFalse(AuthConfigParser.IsFieldTrue(null, "google_enabled"));
            Assert.IsFalse(AuthConfigParser.IsFieldTrue("", "google_enabled"));
        }

        [TestCase("google_client_id", AuthConfigParser.FieldState.Set)]
        [TestCase("discord_client_id", AuthConfigParser.FieldState.Empty)]   // ""
        [TestCase("kakao_client_id", AuthConfigParser.FieldState.Empty)]     // null
        [TestCase("missing_client_id", AuthConfigParser.FieldState.Missing)] // 키 없음
        public void GetStringFieldState_Works(string field, AuthConfigParser.FieldState expected)
            => Assert.AreEqual(expected, AuthConfigParser.GetStringFieldState(Json, field));

        [Test]
        public void GetStringFieldState_Empty_Json_Is_Missing()
            => Assert.AreEqual(AuthConfigParser.FieldState.Missing,
                AuthConfigParser.GetStringFieldState("", "google_client_id"));
    }
}
