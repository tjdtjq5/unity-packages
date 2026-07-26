using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 메인 툴바의 SupaRun 드롭다운.
    ///
    /// 목적 둘:
    ///   1. **현재 편집 환경을 항상 보이게 한다.** 라벨이 곧 표시다 — dev 인 줄 알고 prod 를 건드리는
    ///      사고는 "지금 어디인지 모르는 상태"에서 나온다. 대시보드를 열어야만 알 수 있으면 늦다.
    ///   2. 자주 쓰는 동작(배포·스키마 반영·어드민)을 대시보드를 열지 않고 실행한다.
    ///
    /// Unity 6.3 의 <c>MainToolbarElement</c> 를 쓴다. 같은 프로젝트의 Quantum 이 씬 선택
    /// 드롭다운을 같은 API 로 얹고 있어 검증된 경로다.
    /// </summary>
    public static class SupaRunToolbar
    {
        const string ToolbarPath = "Tools/SupaRun/Environment Bar";
        const string ToolbarMenuRoot = "Tools/SupaRun";
        const int ToolbarPriority = 900;

        /// <summary>
        /// 툴바 요소는 **등록만으로는 보이지 않는다.** 표시 여부가 사용자 설정에 저장되고
        /// `MainToolbarElementAttribute` 에는 그걸 제어하는 항목이 없다(Quantum 도 같은 상태다).
        /// 그래서 이 패키지를 처음 쓰는 프로젝트에서 **한 번만** 켜 준다.
        ///
        /// 한 번뿐인 이유: 사용자가 나중에 끄면 그 선택을 존중해야 한다. 매번 켜면 도구가 아니라 훼방이다.
        ///
        /// ⚠ `MainToolbar.ShowAll` 은 **Unity 내부 API**다. 버전이 바뀌면 사라질 수 있으므로
        /// 리플렉션으로 부르고 실패는 조용히 넘긴다 — 툴바가 안 보이는 것은 불편이지 고장이 아니다.
        /// 우클릭 메뉴로는 언제든 켤 수 있다.
        /// </summary>
        [InitializeOnLoadMethod]
        static void ShowOnceOnFirstLoad()
        {
            var key = EditorPrefUtils.ProjectPrefix + "ToolbarAutoShown";
            if (EditorPrefs.GetBool(key, false)) return;
            // 성공 여부와 무관하게 먼저 찍는다 — 실패하는 환경에서 컴파일마다 두드리지 않기 위해서다.
            EditorPrefs.SetBool(key, true);

            // 로드 직후에는 툴바가 아직 구성되지 않았을 수 있다. 한 프레임 물러난다.
            EditorApplication.delayCall += () =>
            {
                try
                {
                    var t = typeof(EditorWindow).Assembly.GetType("UnityEditor.Toolbars.MainToolbar");
                    var m = t?.GetMethod("ShowAll",
                        BindingFlags.NonPublic | BindingFlags.Static,
                        null, new[] { typeof(string) }, null);
                    m?.Invoke(null, new object[] { ToolbarMenuRoot });
                }
                catch
                {
                    /* 내부 API — 없어졌으면 그냥 둔다. 우클릭 메뉴로 켤 수 있다. */
                }
            };
        }

        [MainToolbarElement(ToolbarPath, defaultDockPosition = MainToolbarDockPosition.Right,
            menuPriority = ToolbarPriority)]
        public static MainToolbarElement CreateDropdown()
        {
            var settings = SupaRunSettings.Instance;
            var env = settings?.EditorEnvironment;
            var label = string.IsNullOrEmpty(env) ? "SupaRun" : $"SupaRun: {env}";

            // FindTexture 를 쓰는 이유: IconContent 는 없는 이름에 "Unable to load icon" 경고를 뿌린다.
            // 내장 아이콘 이름은 Unity 버전마다 사라지기도 해서, 조용히 null 을 받는 쪽이 낫다.
            var icon = EditorGUIUtility.FindTexture(
                           EditorGUIUtility.isProSkin ? "d_CloudConnect" : "CloudConnect")
                       ?? EditorGUIUtility.FindTexture("d_Package Manager");

            // 라이브를 편집 중이면 툴팁으로도 한 번 더 말한다. 라벨 색까지는 이 API 로 못 바꾼다.
            var tip = IsProduction(env)
                ? $"⚠ 편집 환경이 '{env}' 입니다 — 컴파일 시 스키마가 라이브에 반영됩니다"
                : $"편집 환경: {env}";

            return new MainToolbarDropdown(new MainToolbarContent(label, icon, tip), ShowMenu);
        }

        static void ShowMenu(Rect rect)
        {
            var settings = SupaRunSettings.Instance;
            var menu = new GenericMenu();

            // ── 환경 전환 ──
            var envs = settings.Environments;
            if (envs.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("환경 없음 — 대시보드에서 추가"));
            }
            else
            {
                foreach (var e in envs)
                {
                    var name = e.name;   // 클로저가 루프 변수를 잡지 않게 복사
                    menu.AddItem(
                        new GUIContent($"환경/{name}"),
                        settings.EditorEnvironment == name,
                        () => SwitchEnvironment(name));
                }
            }

            menu.AddSeparator("");

            // ── 자주 쓰는 동작 ──
            menu.AddItem(new GUIContent("어드민 열기"), false, SupaRunDashboard.OpenAdmin);
            menu.AddItem(new GUIContent("대시보드 열기"), false, SupaRunDashboard.Open);

            menu.AddSeparator("");

            var envName = settings.EditorEnvironment;
            menu.AddItem(new GUIContent($"스키마 반영 ({envName})"), false,
                () => SchemaAutoSync.SyncNow().Forget());
            menu.AddItem(new GUIContent("Id 상수 생성"), false, GenerateIds);

            menu.AddSeparator("");

            // 배포는 몇 분이 걸리고 진행 로그를 봐야 한다 — 여기서는 버튼까지 데려다주기만 한다.
            menu.AddItem(new GUIContent("서버 배포…"), false, ConfirmDeploy);

            menu.DropDown(rect);
        }

        /// <summary>
        /// 편집 환경 전환. **라이브로 보이는 이름으로 바꿀 때만** 확인을 받는다 —
        /// 매번 물으면 확인창이 무의미해지고, 정작 위험한 전환에서도 습관적으로 넘기게 된다.
        /// </summary>
        static void SwitchEnvironment(string name)
        {
            var settings = SupaRunSettings.Instance;
            if (settings.EditorEnvironment == name) return;

            if (IsProduction(name) &&
                !EditorUtility.DisplayDialog("편집 환경 전환",
                    $"편집 환경을 '{name}' 으로 바꿉니다.\n\n" +
                    "이 상태로 컴파일하면 스키마가 그 환경에 반영되고, 어드민·에디터 플레이도 그곳을 가리킵니다.\n" +
                    "라이브라면 되돌리는 것을 잊지 마세요.",
                    "바꾼다", "취소"))
                return;

            settings.EditorEnvironment = name;
            Debug.Log($"[SupaRun] 편집 환경 → {name}");

            // 라벨은 툴바가 다시 그릴 때 갱신된다.
            EditorApplication.QueuePlayerLoopUpdate();
        }

        /// <summary>Id 상수 생성. 결과를 알려주지 않으면 눌렀는지도 알 수 없다.</summary>
        static void GenerateIds()
        {
            try
            {
                var r = IdConstantGenerator.Generate();
                Debug.Log($"[SupaRun] Id 상수 생성 — {r}");
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Id 상수 생성 실패", ex.Message, "확인");
            }
        }

        static void ConfirmDeploy()
        {
            var settings = SupaRunSettings.Instance;
            if (!settings.IsDeployConfigured)
            {
                EditorUtility.DisplayDialog("서버 배포",
                    "GitHub/GCP 설정이 필요합니다. 대시보드 > Settings 에서 먼저 설정하세요.", "확인");
                SupaRunDashboard.OpenSettingsWindow();
                return;
            }

            // 환경마다 서비스가 다르므로 어디에 쏘는지 이름으로 확인시킨다.
            if (!EditorUtility.DisplayDialog("서버 배포",
                $"환경 '{settings.EditorEnvironment}' 의 Cloud Run 서비스에 배포합니다.\n" +
                $"서비스: {settings.gcpServiceName}\n\n" +
                "Deploy 탭으로 이동합니다. 거기서 배포를 시작하세요.",
                "이동", "취소"))
                return;

            SupaRunDashboard.OpenDeployTab();
        }

        static bool IsProduction(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var n = name.ToLowerInvariant();
            return n.Contains("prod") || n.Contains("live") || n.Contains("release");
        }
    }
}
