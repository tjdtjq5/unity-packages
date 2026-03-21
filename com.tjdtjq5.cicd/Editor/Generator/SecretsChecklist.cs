#if UNITY_EDITOR
using System.Collections.Generic;

namespace Tjdtjq5.CICD.Editor
{
    public struct SecretEntry
    {
        public string Name;
        public string Description;
        public string HowToGet;
        /// <summary>null이면 수동 입력 필요, 값이 있으면 자동 생성 가능</summary>
        public string AutoValue;
    }

    /// <summary>현재 설정 기반으로 필요한 GitHub Secrets 목록 생성</summary>
    public static class SecretsChecklist
    {
        public static List<SecretEntry> GetRequired(BuildAutomationSettings settings)
        {
            var list = new List<SecretEntry>();

            // ── Unity 라이선스 ──

            list.Add(new SecretEntry
            {
                Name = "UNITY_LICENSE",
                Description = "Unity 라이선스 파일 (.ulf) 내용",
                HowToGet = "Step 2에서 선택한 .ulf 파일",
                AutoValue = BuildAutomationSettings.UlfContent
            });
            list.Add(new SecretEntry
            {
                Name = "UNITY_EMAIL",
                Description = "Unity 계정 이메일",
                HowToGet = "Step 2에서 입력한 Unity 이메일",
                AutoValue = BuildAutomationSettings.UnityEmail
            });
            list.Add(new SecretEntry
            {
                Name = "UNITY_PASSWORD",
                Description = "Unity 계정 비밀번호",
                HowToGet = "Step 2에서 입력한 Unity 비밀번호",
                AutoValue = BuildAutomationSettings.UnityPassword
            });

            // ── Android ──

            if (settings.enableAndroid)
            {
                list.Add(new SecretEntry
                {
                    Name = "ANDROID_KEYSTORE_BASE64",
                    Description = "Keystore 파일의 base64 인코딩",
                    HowToGet = "[base64 변환] 버튼으로 자동 생성",
                    AutoValue = KeystoreHelper.ToBase64(settings.keystorePath)
                });
                list.Add(new SecretEntry
                {
                    Name = "ANDROID_KEYSTORE_PASS",
                    Description = "Keystore 비밀번호",
                    HowToGet = "Keystore 생성 시 설정한 비밀번호",
                    AutoValue = BuildAutomationSettings.KeystorePass
                });
                list.Add(new SecretEntry
                {
                    Name = "ANDROID_KEY_ALIAS",
                    Description = "Key Alias 이름",
                    HowToGet = "Keystore의 Key Alias",
                    AutoValue = settings.keyAlias
                });
                list.Add(new SecretEntry
                {
                    Name = "ANDROID_KEY_PASS",
                    Description = "Key 비밀번호",
                    HowToGet = "Key Alias의 비밀번호",
                    AutoValue = BuildAutomationSettings.KeyPass
                });
            }

            // ── Google Play ──

            if (settings.deployGooglePlay)
            {
                list.Add(new SecretEntry
                {
                    Name = "GOOGLE_PLAY_SERVICE_ACCOUNT_JSON",
                    Description = "Google Play Service Account JSON 키",
                    HowToGet = "Google Cloud Console → IAM → 서비스 계정 → JSON 키 생성"
                });
                list.Add(new SecretEntry
                {
                    Name = "ANDROID_PACKAGE_NAME",
                    Description = "Android 패키지 이름 (예: com.company.game)",
                    HowToGet = "Unity → Player Settings → Package Name",
                    AutoValue = UnityEditor.PlayerSettings.applicationIdentifier
                });
            }

            // ── App Store ──

            if (settings.deployAppStore)
            {
                list.Add(new SecretEntry
                {
                    Name = "APP_STORE_CONNECT_API_KEY",
                    Description = "App Store Connect API 키 (.p8) 내용",
                    HowToGet = "App Store Connect → 사용자 및 액세스 → 키 → .p8 다운로드"
                });
                list.Add(new SecretEntry
                {
                    Name = "APP_STORE_CONNECT_KEY_ID",
                    Description = "API 키 ID",
                    HowToGet = "App Store Connect → 키 목록에서 Key ID 확인"
                });
                list.Add(new SecretEntry
                {
                    Name = "APP_STORE_CONNECT_ISSUER_ID",
                    Description = "Issuer ID",
                    HowToGet = "App Store Connect → 키 페이지 상단의 Issuer ID"
                });
            }

            // ── Steam ──

            if (settings.deploySteam)
            {
                list.Add(new SecretEntry
                {
                    Name = "STEAM_USERNAME",
                    Description = "Steam 빌드 업로드 계정 이름",
                    HowToGet = "Steamworks에서 빌드 업로드 권한이 있는 계정"
                });
                list.Add(new SecretEntry
                {
                    Name = "STEAM_CONFIG_VDF",
                    Description = "SteamCMD config.vdf (base64)",
                    HowToGet = "SteamCMD 로그인 후 config/config.vdf 파일을 base64로 변환"
                });
                if (string.IsNullOrEmpty(settings.steamAppId))
                {
                    list.Add(new SecretEntry
                    {
                        Name = "STEAM_APP_ID",
                        Description = "Steam App ID",
                        HowToGet = "Steamworks 대시보드에서 확인"
                    });
                }
            }

            // ── 웹훅 ──

            if (settings.notifyChannel != NotifyChannel.None)
            {
                string channelName = settings.notifyChannel switch
                {
                    NotifyChannel.Discord => "DISCORD",
                    NotifyChannel.Slack => "SLACK",
                    _ => "CUSTOM"
                };
                list.Add(new SecretEntry
                {
                    Name = $"{channelName}_WEBHOOK",
                    Description = $"{settings.notifyChannel} 웹훅 URL",
                    HowToGet = $"{settings.notifyChannel} 서버 설정 → 웹훅 → URL 복사",
                    AutoValue = settings.webhookUrl
                });
            }

            return list;
        }

    }
}
#endif
