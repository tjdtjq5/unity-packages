#if UNITY_EDITOR
using System;
using Tjdtjq5.Codemagic.Editor.Codemagic;
using Tjdtjq5.Codemagic.Editor.Settings;
using Tjdtjq5.EditorToolkit.Editor;

namespace Tjdtjq5.Codemagic.Editor.Setup
{
    /// <summary>SetupWizard의 Step들이 공유하는 Service/State 컨테이너.</summary>
    /// <remarks>
    /// 시크릿(token, password)은 SecretStore(EditorPrefs)에서 직접 읽고,
    /// per-user 메타는 State, 팀 공유 메타는 Settings에서 읽는다.
    /// </remarks>
    public sealed class SetupContext
    {
        // Welcome의 [시작하기] 같은 콘텐츠 내 버튼이 다음 step으로 진행시키는 콜백.
        // Wizard 생성자에서 BindWizard로 등록.
        Action _onNextRequested;
        /// <summary>Layer 2 — per-user 비-시크릿 (Library/codemagic-setup.json).</summary>
        public LocalUserState State { get; set; }

        /// <summary>Layer 3 — 팀 공유 (Assets/Editor/CodemagicProjectSettings.asset).</summary>
        public CodemagicProjectSettings Settings => CodemagicProjectSettings.Instance;

        /// <summary>Codemagic REST 클라이언트. Step 2에서 토큰 입력되면 RebuildApi 호출 후 사용 가능.</summary>
        public CodemagicApiClient Api { get; private set; }

        // 토스트 알림 — SetupWizard 상단의 DrawNotificationBar에 표시.
        // string은 ref로 넘기는 게 표준 패턴이라 public 필드로 둔다 (property X).
        // [Copy]/[X] 버튼은 EditorUI.DrawNotificationBar가 자동 제공.
        public string Notification = "";
        public EditorUI.NotificationType NotificationType = EditorUI.NotificationType.Info;

        public SetupContext()
        {
            State = LocalUserStateStore.Load();
            RebuildApi();
        }

        /// <summary>SecretStore.CodemagicToken으로 ApiClient 재생성. 토큰 비어있으면 null.</summary>
        public void RebuildApi()
        {
            var token = SecretStore.CodemagicToken;
            Api = string.IsNullOrEmpty(token) ? null : new CodemagicApiClient(token);
        }

        /// <summary>State를 Library JSON에 저장.</summary>
        public void SaveState() => LocalUserStateStore.Save(State);

        /// <summary>Wizard가 Step → Wizard 진행 콜백을 등록. SetupWizard 생성자에서 1회 호출.</summary>
        public void BindWizard(Action onNextRequested) => _onNextRequested = onNextRequested;

        /// <summary>현재 Step이 다음 Step으로 진행을 요청. Welcome의 [시작하기] 등에서 호출.</summary>
        public void RequestNext() => _onNextRequested?.Invoke();

        /// <summary>토스트 알림 표시. 윈도우 상단에 Copy/X 버튼과 함께 노출. 다음 호출 시 덮어쓰기.</summary>
        public void ShowNotification(string message, EditorUI.NotificationType type = EditorUI.NotificationType.Info)
        {
            Notification = message ?? "";
            NotificationType = type;
        }

        /// <summary>토스트 닫기. 사용자 X 버튼 효과를 코드에서 트리거할 때.</summary>
        public void ClearNotification() => Notification = "";
    }
}
#endif
