using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>빌드 시 Config JSON 생성 + Android 딥링크 자동 설정.</summary>
    public class SupaRunBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string ResourcesDir = "Assets/Resources";
        const string ConfigPath = "Assets/Resources/SupaRunConfig.json";
        const string ManifestDir = "Assets/Plugins/Android";
        const string ManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";
        const string BuildProxyDir = "Assets/SupaRun/Generated";
        const string BuildProxyPath = "Assets/SupaRun/Generated/ServerAPI_Build.g.cs";

        // Activity 모드 (androidApplicationEntry 비트 플래그)
        const string ActivityClass = "com.unity3d.player.UnityPlayerActivity";
        const string GameActivityClass = "com.unity3d.player.UnityPlayerGameActivity";
        const string ActivityTheme = "@style/UnityThemeSelector";
        const string GameActivityTheme = "@style/BaseUnityGameActivityTheme";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var settings = SupaRunSettings.Instance;

            // 1. Config JSON
            var config = new SupaRunRuntimeConfig
            {
                supabaseUrl = settings.supabaseUrl,
                supabaseAnonKey = SupaRunSettings.SupabaseAnonKey,
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
            Debug.Log($"[SupaRun:Build] Config 생성: {ConfigPath}");

            // 서버 URL 미설정 경고 (빌드에서는 항상 서버 호출이므로 필수)
            if (string.IsNullOrEmpty(settings.cloudRunUrl))
            {
                Debug.LogWarning(
                    "[SupaRun:Build] cloudRunUrl이 설정되지 않았습니다. " +
                    "빌드에서 서버 API 호출이 실패합니다. 배포를 먼저 진행하세요.");
            }

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
                Debug.Log($"[SupaRun:Build] Config 삭제: {ConfigPath}");
            }

            // BuildProxy 정리 (존재하면)
            if (File.Exists(BuildProxyPath))
                AssetDatabase.DeleteAsset(BuildProxyPath);
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
                    Debug.Log("[SupaRun:Build] AndroidManifest 딥링크 이미 있음");
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
                    Debug.LogWarning($"[SupaRun:Build] {activityClass}를 찾을 수 없습니다. AndroidManifest에 딥링크를 수동으로 추가하세요.");
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
            Debug.Log($"[SupaRun:Build] AndroidManifest 딥링크 추가 ({(isGameActivity ? "GameActivity" : "Activity")}): {bundleId}://auth");
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

        // ── Phase 2: 빌드용 HTTP 전용 프록시 생성 ──

        /// <summary>
        /// 에디터에서 SG가 생성한 ServerAPI 타입을 리플렉션으로 스캔하여
        /// HTTP 전용 프록시를 생성. 빌드에서 Service 코드가 제외되어도 API 호출 가능.
        /// </summary>
        static void GenerateBuildProxy()
        {
            // Assembly-CSharp에서 ServerAPI 타입 찾기
            Type serverApiType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.GetName().Name.Contains("Assembly-CSharp")) continue;
                serverApiType = asm.GetType("Tjdtjq5.SupaRun.ServerAPI");
                if (serverApiType != null) break;
            }

            if (serverApiType == null)
            {
                Debug.Log("[SupaRun:Build] ServerAPI 타입 없음 — Service가 없거나 SG 미적용. BuildProxy 스킵.");
                return;
            }

            var nestedTypes = serverApiType.GetNestedTypes(BindingFlags.Public | BindingFlags.Static);
            if (nestedTypes.Length == 0)
            {
                Debug.Log("[SupaRun:Build] ServerAPI에 서비스 없음. BuildProxy 스킵.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated for build — 빌드 시 자동 생성, 빌드 후 자동 삭제>");
            sb.AppendLine("#if !UNITY_EDITOR");
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using Tjdtjq5.SupaRun;");
            sb.AppendLine("");
            sb.AppendLine("namespace Tjdtjq5.SupaRun");
            sb.AppendLine("{");
            sb.AppendLine("    public static partial class ServerAPI");
            sb.AppendLine("    {");

            foreach (var nested in nestedTypes)
            {
                sb.AppendLine($"        public static class {nested.Name}");
                sb.AppendLine("        {");

                var methods = nested.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
                foreach (var method in methods)
                {
                    EmitHttpOnlyMethod(sb, nested.Name, method);
                }

                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine("#endif");

            if (!Directory.Exists(BuildProxyDir))
                Directory.CreateDirectory(BuildProxyDir);

            File.WriteAllText(BuildProxyPath, sb.ToString());
            AssetDatabase.ImportAsset(BuildProxyPath);
            Debug.Log($"[SupaRun:Build] BuildProxy 생성: {nestedTypes.Length}개 서비스 → {BuildProxyPath}");
        }

        static void EmitHttpOnlyMethod(StringBuilder sb, string svcName, MethodInfo method)
        {
            var returnType = method.ReturnType;
            var innerType = ExtractResponseInner(returnType);
            bool hasReturn = innerType != null;

            string respType = hasReturn
                ? $"ServerResponse<{FormatType(innerType)}>"
                : "ServerResponse";

            var ps = method.GetParameters();
            var paramDecl = string.Join(", ", ps.Select(p => $"{FormatType(p.ParameterType)} {p.Name}"));
            var anonObj = ps.Length > 0
                ? $"new {{ {string.Join(", ", ps.Select(p => p.Name))} }}"
                : "null";

            string endpoint = $"{svcName}/{method.Name}";

            sb.AppendLine($"            public static async Task<{respType}> {method.Name}({paramDecl})");
            sb.AppendLine("            {");
            sb.AppendLine("                await Tjdtjq5.SupaRun.SupaRun.WaitForAuth();");

            if (hasReturn)
                sb.AppendLine($"                return await Tjdtjq5.SupaRun.SupaRun.Client.PostAsync<{FormatType(innerType)}>(\"api/{endpoint}\", {anonObj});");
            else
                sb.AppendLine($"                return await Tjdtjq5.SupaRun.SupaRun.Client.PostAsync(\"api/{endpoint}\", {anonObj});");

            sb.AppendLine("            }");
        }

        /// <summary>Task&lt;ServerResponse&lt;T&gt;&gt; → T. Task&lt;ServerResponse&gt; → null.</summary>
        static Type ExtractResponseInner(Type returnType)
        {
            if (returnType == null || !returnType.IsGenericType) return null;
            var taskInner = returnType.GetGenericArguments()[0]; // ServerResponse<T> 또는 ServerResponse
            if (!taskInner.IsGenericType) return null;
            return taskInner.GetGenericArguments()[0]; // T
        }

        /// <summary>Type을 C# 소스 코드 문자열로 변환.</summary>
        static string FormatType(Type type)
        {
            if (type == null) return "object";
            if (type == typeof(string)) return "string";
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(long)) return "long";
            if (type == typeof(double)) return "double";
            if (type == typeof(void)) return "void";

            if (type.IsGenericType)
            {
                var genericDef = type.GetGenericTypeDefinition();
                var args = type.GetGenericArguments();

                if (genericDef == typeof(List<>))
                    return $"System.Collections.Generic.List<{FormatType(args[0])}>";
                if (genericDef == typeof(Task<>))
                    return $"System.Threading.Tasks.Task<{FormatType(args[0])}>";

                // 기타 제네릭
                var baseName = type.Name.Substring(0, type.Name.IndexOf('`'));
                return $"{baseName}<{string.Join(", ", args.Select(FormatType))}>";
            }

            return type.Name;
        }
    }
}
