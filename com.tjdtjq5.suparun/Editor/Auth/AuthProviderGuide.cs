namespace Tjdtjq5.SupaRun.Editor
{
    public struct GuideStep
    {
        public string description;
        public (string label, string url)[] links;
    }

    public struct GuideInfo
    {
        public string displayName;
        public GuideStep[] guideSteps;
        public bool requiresSDK;
        public string sdkName;
    }

    public static class AuthProviderGuide
    {
        public static readonly string[] AvailableProviders =
            { "Guest", "Google", "Apple", "Facebook", "Discord", "Twitter", "Kakao", "Twitch", "Spotify", "Slack", "GitHub", "GPGS", "GameCenter" };

        // ── Supabase OAuth 공통 2-step ──

        static GuideStep[] OAuthSteps(string providerLower) => new[]
        {
            new GuideStep
            {
                description = "Supabase 공식 가이드를 따라 설정하세요.",
                links = new[] { ("공식 설정 가이드", $"https://supabase.com/docs/guides/auth/social-login/auth-{providerLower}") }
            },
            new GuideStep
            {
                description = "게임용 추가 설정 (Supabase Auth 설정 페이지에서):\n" +
                    "1. nonce 검사 건너뛰기 - 켜기 (모바일 게임 필수)\n" +
                    "2. 이메일 없는 사용자 허용 - 켜기",
                links = new[] { ("Supabase Auth 설정", "https://supabase.com/dashboard/project/{PROJECT_ID}/auth/providers") }
            }
        };

        public static GuideInfo Get(string provider) => provider switch
        {
            "Guest" => new GuideInfo
            {
                displayName = "Guest",
                guideSteps = new[]
                {
                    new GuideStep {
                        description = "Supabase에서 Anonymous Sign-in 활성화:\n" +
                            "Auth > Settings > User Signups > Anonymous Sign-ins 켜기",
                        links = new[] { ("Auth Settings", "https://supabase.com/dashboard/project/{PROJECT_ID}/settings/auth") }
                    }
                }
            },

            // ── Supabase OAuth (공식 문서 위임) ──

            "Google" => new GuideInfo { displayName = "Google", guideSteps = OAuthSteps("google") },
            "Apple" => new GuideInfo { displayName = "Apple", guideSteps = OAuthSteps("apple") },
            "Facebook" => new GuideInfo { displayName = "Facebook", guideSteps = OAuthSteps("facebook") },
            "Discord" => new GuideInfo { displayName = "Discord", guideSteps = OAuthSteps("discord") },
            "Twitter" => new GuideInfo { displayName = "Twitter (X)", guideSteps = OAuthSteps("twitter") },
            "Kakao" => new GuideInfo { displayName = "Kakao", guideSteps = OAuthSteps("kakao") },
            "Twitch" => new GuideInfo { displayName = "Twitch", guideSteps = OAuthSteps("twitch") },
            "Spotify" => new GuideInfo { displayName = "Spotify", guideSteps = OAuthSteps("spotify") },
            "Slack" => new GuideInfo { displayName = "Slack", guideSteps = OAuthSteps("slack") },
            "GitHub" => new GuideInfo { displayName = "GitHub", guideSteps = OAuthSteps("github") },

            // ── GPGS (네이티브 SDK — 별도 가이드) ──

            "GPGS" => new GuideInfo
            {
                displayName = "Google Play Games",
                requiresSDK = true,
                sdkName = "Google Play Games SDK",
                guideSteps = new[]
                {
                    new GuideStep {
                        description = "Play Console에서 아래 작업을 완료하세요:\n" +
                            "1. Play Games 서비스 > 설정 및 관리에서 활성화\n" +
                            "2. 사용자 인증 정보에 웹 서버 타입 OAuth Client 등록",
                        links = new[] { ("Play Console", "https://play.google.com/console") } },
                    new GuideStep {
                        description = "Google Cloud Console에서 아래 작업을 완료하세요:\n" +
                            "사용자 인증 정보 > OAuth Client ID 생성 (유형: 웹 애플리케이션)",
                        links = new[] { ("Google Cloud Console", "https://console.cloud.google.com/apis/credentials") } },
                    new GuideStep {
                        description = "GPGS 플러그인 설치 + Unity Setup:\n" +
                            "1. GitHub Releases에서 .unitypackage 다운로드 후 Import\n" +
                            "2. Window > Google Play Games > Setup\n" +
                            "3. Web Client ID 입력\n" +
                            "4. Resources Definition 입력:\n" +
                            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                            "<resources><string name=\"app_id\">OAuth숫자ID</string></resources>",
                        links = new[] { ("GPGS Unity Plugin", "https://github.com/playgameservices/play-games-plugin-for-unity") } },
                    new GuideStep {
                        description = "Supabase에서 연결:\n" +
                            "1. Auth > Providers > Google 활성화\n" +
                            "2. Client ID / Secret 입력\n" +
                            "3. nonce 검사 건너뛰기 - 켜기\n" +
                            "4. 이메일 없는 사용자 허용 - 켜기\n" +
                            "(GPGS는 Google OAuth 기반)",
                        links = new[] { ("Supabase Auth 설정", "https://supabase.com/dashboard/project/{PROJECT_ID}/auth/providers") } },
                }
            },

            // ── Game Center (네이티브 — iOS 전용) ──

            "GameCenter" => new GuideInfo
            {
                displayName = "Game Center",
                guideSteps = new[]
                {
                    new GuideStep {
                        description = "iOS 전용 (빌드 타겟이 iOS여야 동작)\n" +
                            "1. App Store Connect에서 Game Center 활성화\n" +
                            "2. Xcode에서 Game Center Capability 추가",
                        links = new[]
                        {
                            ("App Store Connect", "https://appstoreconnect.apple.com"),
                            ("Game Center 문서", "https://developer.apple.com/game-center/")
                        } },
                    new GuideStep {
                        description = "게임용 추가 설정 (Supabase Auth 설정 페이지에서):\n" +
                            "1. nonce 검사 건너뛰기 - 켜기\n" +
                            "2. 이메일 없는 사용자 허용 - 켜기",
                        links = new[] { ("Supabase Auth 설정", "https://supabase.com/dashboard/project/{PROJECT_ID}/auth/providers") } },
                }
            },

            _ => new GuideInfo { displayName = provider, guideSteps = System.Array.Empty<GuideStep>() }
        };

        /// <summary>Provider key → Supabase Management API 필드 접두사. null이면 API 미지원.</summary>
        public static string GetApiFieldPrefix(string provider) => provider switch
        {
            "Guest"     => "external_anonymous_users",  // _enabled만 사용
            "Google"    => "external_google",
            "Apple"     => "external_apple",
            "Facebook"  => "external_facebook",
            "Discord"   => "external_discord",
            "Twitter"   => "external_twitter",
            "Kakao"     => "external_kakao",
            "Twitch"    => "external_twitch",
            "Spotify"   => "external_spotify",
            "Slack"     => "external_slack_oidc",
            "GitHub"    => "external_github",
            _ => null  // GPGS, GameCenter은 Supabase provider가 아님 (서버 경유)
        };

        /// <summary>이 Provider가 Client ID/Secret 입력이 필요한지.</summary>
        public static bool RequiresClientCredentials(string provider) => provider switch
        {
            "Guest" => false,
            "GPGS" => false,       // Google provider 경유
            "GameCenter" => false, // 서버 경유
            _ => true
        };

        public static bool IsSDKInstalled(string provider)
        {
            if (provider == "GPGS")
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "Google.Play.Games")
                        return true;
                }
                return false;
            }
            if (provider == "GameCenter")
            {
                #if UNITY_IOS
                return true;
                #else
                return false;
                #endif
            }
            return true;
        }
    }
}
