using System.Reflection;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 메인 툴바의 SupaRun 드롭다운.
    ///
    /// 목적 둘뿐이다:
    ///   1. **현재 편집 환경을 항상 보이게 한다.** 라벨이 곧 표시다 — dev 인 줄 알고 prod 를 건드리는
    ///      사고는 "지금 어디인지 모르는 상태"에서 나온다. 어드민을 열어야만 알 수 있으면 늦다.
    ///   2. 환경 전환과 배포 진입. 그 밖의 동작은 여기 없다 — 어드민은 Ctrl+Shift+D 로 열리고,
    ///      스키마 반영은 자동(컴파일)이거나 배포에 포함이라 사람이 누를 버튼이 아니게 됐다.
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
                menu.AddDisabledItem(new GUIContent("환경 없음 — 어드민에서 추가"));
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

            // 배포는 몇 분이 걸리고 진행 로그를 봐야 한다 — 여기서는 버튼까지 데려다주기만 한다.
            menu.AddItem(new GUIContent("서버 배포…"), false, OpenDeploy);

            menu.DropDown(rect);
        }

        /// <summary>
        /// 편집 환경 전환. 확인창은 없다 — 고르는 행위가 곧 의도다(어드민 전환 입장과 같은 결정).
        /// prod 위험은 확인창 대신 구조가 막는다: 자동 스키마 반영이 환경별이라
        /// prod 를 편집 중이어도 컴파일이 스키마를 밀지 않는다.
        /// </summary>
        static void SwitchEnvironment(string name)
        {
            var settings = SupaRunSettings.Instance;
            if (settings.EditorEnvironment == name) return;

            settings.EditorEnvironment = name;
            Debug.Log($"[SupaRun] 편집 환경 → {name}");

            // 라벨은 툴바가 다시 그릴 때 갱신된다.
            EditorApplication.QueuePlayerLoopUpdate();
        }

        /// <summary>
        /// 배포 화면까지 데려다준다. **확인은 여기서 받지 않는다** — 어드민이 대상과 진행을
        /// 보여주는 자리이고, 같은 것을 두 번 확인시키면 두 번째가 형식이 된다.
        ///
        /// 설정이 덜 된 경우만 다른 화면으로 보낸다. 배포 화면에 도착해서야 "설정이 필요합니다" 를
        /// 읽는 것보다, 채워야 할 곳으로 바로 가는 편이 빠르다.
        /// </summary>
        static void OpenDeploy()
        {
            var settings = SupaRunSettings.Instance;
            SupaRunAdmin.Open(settings.IsDeployConfigured ? "ops" : "settings");
        }

        static bool IsProduction(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var n = name.ToLowerInvariant();
            return n.Contains("prod") || n.Contains("live") || n.Contains("release");
        }
    }
}
