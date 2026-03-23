using System.IO;
using System.Xml;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Tjdtjq5.GameServer.Editor
{
    /// <summary>빌드 시 Config JSON 생성 + Android 딥링크 자동 설정.</summary>
    public class GameServerBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string ResourcesDir = "Assets/Resources";
        const string ConfigPath = "Assets/Resources/GameServerConfig.json";
        const string ManifestDir = "Assets/Plugins/Android";
        const string ManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";

        // Activity 모드 (androidApplicationEntry 비트 플래그)
        const string ActivityClass = "com.unity3d.player.UnityPlayerActivity";
        const string GameActivityClass = "com.unity3d.player.UnityPlayerGameActivity";
        const string ActivityTheme = "@style/UnityThemeSelector";
        const string GameActivityTheme = "@style/BaseUnityGameActivityTheme";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = GameServerSettings.Instance;

            // 1. Config JSON
            var config = new GameServerRuntimeConfig
            {
                supabaseUrl = settings.supabaseUrl,
                supabaseAnonKey = GameServerSettings.SupabaseAnonKey,
                cloudRunUrl = settings.cloudRunUrl
            };

            if (!Directory.Exists(ResourcesDir))
            {
                Directory.CreateDirectory(ResourcesDir);
                AssetDatabase.Refresh();
            }

            var json = JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(ConfigPath, json);
            AssetDatabase.ImportAsset(ConfigPath);
            Debug.Log($"[GameServer:Build] Config 생성: {ConfigPath}");

            // 2. Android 딥링크 (소셜 로그인 활성화 시)
            if (report.summary.platform == BuildTarget.Android &&
                settings.enabledAuthProviders != null &&
                settings.enabledAuthProviders.Count > 1) // Guest 외에 소셜이 있으면
            {
                EnsureAndroidDeepLink();
            }
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (File.Exists(ConfigPath))
            {
                AssetDatabase.DeleteAsset(ConfigPath);
                Debug.Log($"[GameServer:Build] Config 삭제: {ConfigPath}");
            }
        }

        /// <summary>현재 PlayerSettings에서 GameActivity 모드인지 확인.</summary>
        static bool IsGameActivityMode()
        {
            // androidApplicationEntry: 1=Activity, 2=GameActivity, 3=둘다
            // 비트 플래그: 2번 비트가 켜져있으면 GameActivity
            var so = new SerializedObject(
                UnityEditor.Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
            var prop = so.FindProperty("androidApplicationEntry");
            if (prop != null)
            {
                int value = prop.intValue;
                return (value & 2) != 0; // GameActivity 비트 체크
            }
            // 프로퍼티 못 찾으면 Activity 모드 기본
            return false;
        }

        static string GetActivityClassName() =>
            IsGameActivityMode() ? GameActivityClass : ActivityClass;

        static string GetActivityTheme() =>
            IsGameActivityMode() ? GameActivityTheme : ActivityTheme;

        static void EnsureAndroidDeepLink()
        {
            var bundleId = PlayerSettings.applicationIdentifier.ToLower();
            var activityClass = GetActivityClassName();
            var activityTheme = GetActivityTheme();
            bool isGameActivity = IsGameActivityMode();

            if (!Directory.Exists(ManifestDir))
                Directory.CreateDirectory(ManifestDir);

            if (File.Exists(ManifestPath))
            {
                // 기존 매니페스트에 딥링크 있는지 확인
                var content = File.ReadAllText(ManifestPath);
                if (content.Contains($"android:scheme=\"{bundleId}\""))
                {
                    Debug.Log("[GameServer:Build] AndroidManifest 딥링크 이미 있음");
                    return;
                }

                // 기존 매니페스트에 딥링크 추가
                var doc = new XmlDocument();
                doc.Load(ManifestPath);
                var nsm = CreateNsManager(doc);

                // GameActivity와 Activity 모두 검색
                var activity = doc.SelectSingleNode(
                    $"//activity[@android:name='{activityClass}']", nsm);

                // 현재 모드의 클래스를 못 찾으면 다른 쪽도 시도
                if (activity == null)
                {
                    var fallbackClass = isGameActivity ? ActivityClass : GameActivityClass;
                    activity = doc.SelectSingleNode(
                        $"//activity[@android:name='{fallbackClass}']", nsm);
                }

                if (activity == null)
                {
                    Debug.LogWarning($"[GameServer:Build] {activityClass}를 찾을 수 없습니다. AndroidManifest에 딥링크를 수동으로 추가하세요.");
                    return;
                }

                AddDeepLinkIntentFilter(doc, activity, bundleId);
                doc.Save(ManifestPath);
            }
            else
            {
                // 새 매니페스트 생성
                var libNameMeta = isGameActivity
                    ? "\n            <meta-data android:name=\"android.app.lib_name\" android:value=\"game\" />"
                    : "";

                var manifest = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<manifest xmlns:android=""http://schemas.android.com/apk/res/android""
          package=""com.unity3d.player"">
    <application>
        <activity
            android:name=""{activityClass}""
            android:theme=""{activityTheme}""
            android:launchMode=""singleTask""
            android:configChanges=""fontScale|keyboard|keyboardHidden|locale|mnc|mcc|navigation|orientation|screenLayout|screenSize|smallestScreenSize|uiMode|touchscreen"">
            <intent-filter>
                <action android:name=""android.intent.action.MAIN"" />
                <category android:name=""android.intent.category.LAUNCHER"" />
            </intent-filter>
            <meta-data android:name=""unityplayer.UnityActivity"" android:value=""true"" />{libNameMeta}
            <intent-filter>
                <action android:name=""android.intent.action.VIEW"" />
                <category android:name=""android.intent.category.DEFAULT"" />
                <category android:name=""android.intent.category.BROWSABLE"" />
                <data android:scheme=""{bundleId}"" android:host=""auth"" />
            </intent-filter>
        </activity>
    </application>
</manifest>";
                File.WriteAllText(ManifestPath, manifest);
            }

            AssetDatabase.ImportAsset(ManifestPath);
            Debug.Log($"[GameServer:Build] AndroidManifest 딥링크 추가 ({(isGameActivity ? "GameActivity" : "Activity")}): {bundleId}://auth");
        }

        static void AddDeepLinkIntentFilter(XmlDocument doc, XmlNode activity, string bundleId)
        {
            var ns = "http://schemas.android.com/apk/res/android";
            var intentFilter = doc.CreateElement("intent-filter");

            var action = doc.CreateElement("action");
            action.SetAttribute("name", ns, "android.intent.action.VIEW");
            intentFilter.AppendChild(action);

            var catDefault = doc.CreateElement("category");
            catDefault.SetAttribute("name", ns, "android.intent.category.DEFAULT");
            intentFilter.AppendChild(catDefault);

            var catBrowsable = doc.CreateElement("category");
            catBrowsable.SetAttribute("name", ns, "android.intent.category.BROWSABLE");
            intentFilter.AppendChild(catBrowsable);

            var data = doc.CreateElement("data");
            data.SetAttribute("scheme", ns, bundleId);
            data.SetAttribute("host", ns, "auth");
            intentFilter.AppendChild(data);

            activity.AppendChild(intentFilter);
        }

        static XmlNamespaceManager CreateNsManager(XmlDocument doc)
        {
            var nsm = new XmlNamespaceManager(doc.NameTable);
            nsm.AddNamespace("android", "http://schemas.android.com/apk/res/android");
            return nsm;
        }
    }
}
