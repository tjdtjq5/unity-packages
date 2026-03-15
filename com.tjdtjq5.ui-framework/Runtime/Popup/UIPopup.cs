using Tjdtjq5.EditorToolkit;
using UnityEngine;
using VContainer;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// 팝업 스택 모드.
    /// </summary>
    public enum PopupStackMode
    {
        /// <summary>팝업끼리 겹쳐도 OK (기본)</summary>
        Overlay,
        /// <summary>새 팝업 열리면 이전 팝업 숨김, 닫으면 복원</summary>
        Exclusive,
    }

    /// <summary>
    /// 팝업 UI 베이스 클래스.
    /// VContainer [Inject]로 UIManager 자동 주입.
    /// UITransition 연동, 생명주기 훅, UIManager 스택/큐 자동 관리.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    [RequireComponent(typeof(UITransition))]
    public abstract class UIPopup : MonoBehaviour
    {
        [SectionHeader("UIPopup", 0.4f, 0.75f, 0.95f)]
        [SerializeField] private bool _pauseGame;
        [SerializeField] private bool _closeOnBack = true;
        [SerializeField] private PopupStackMode _stackMode = PopupStackMode.Overlay;

        public bool PauseGame => _pauseGame;
        public bool CloseOnBack => _closeOnBack;
        public PopupStackMode StackMode => _stackMode;
        public bool IsOpen { get; private set; }

        /// <summary>스택에서 숨겨진 상태 (IsOpen이지만 보이지 않음)</summary>
        internal bool IsHiddenByStack { get; private set; }

        protected UITransition Transition { get; private set; }
        protected CanvasGroup CanvasGroup { get; private set; }
        protected UIManager UIManager { get; private set; }

        [Inject]
        public void Construct(UIManager uiManager)
        {
            UIManager = uiManager;
        }

        protected virtual void Awake()
        {
            CanvasGroup = GetComponent<CanvasGroup>();
            Transition = GetComponent<UITransition>();
        }

        /// <summary>
        /// 팝업을 엽니다. UIManager에 등록하고 UITransition을 실행합니다.
        /// </summary>
        public void Show()
        {
            if (IsOpen) return;
            IsOpen = true;
            IsHiddenByStack = false;

            CanvasGroup ??= GetComponent<CanvasGroup>();
            Transition ??= GetComponent<UITransition>();

            if (Transition != null)
                Transition.OnCloseComplete -= HandleCloseComplete;

            UIManager?.OnPopupOpen(this);
            OnShowStart();

            if (Transition != null)
            {
                Transition.OnOpenComplete += HandleOpenComplete;
                Transition.Open();
            }
            else
            {
                gameObject.SetActive(true);
                CanvasGroup.alpha = 1f;
                CanvasGroup.interactable = true;
                CanvasGroup.blocksRaycasts = true;
                HandleOpenComplete();
            }
        }

        /// <summary>
        /// 팝업을 닫습니다. IsOpen을 즉시 false로 설정하여 연속 Show 호출 가능.
        /// </summary>
        public void Hide()
        {
            if (!IsOpen) return;
            IsOpen = false;
            IsHiddenByStack = false;

            UIManager?.OnPopupClose(this);
            OnHideStart();

            if (Transition != null)
            {
                Transition.OnCloseComplete += HandleCloseComplete;
                Transition.Close();
            }
            else
            {
                gameObject.SetActive(false);
                HandleCloseComplete();
            }
        }

        /// <summary>
        /// 즉시 닫기 (애니메이션 없이). CloseAll 등에서 사용.
        /// </summary>
        public void HideImmediate()
        {
            if (!IsOpen) return;
            IsOpen = false;
            IsHiddenByStack = false;

            UIManager?.OnPopupClose(this);
            OnHideStart();

            if (Transition != null)
                Transition.SetCloseImmediate();
            else
                gameObject.SetActive(false);

            OnHideComplete();
        }

        /// <summary>
        /// 스택에서 숨기기 (Exclusive 모드). IsOpen 유지, 화면만 숨김.
        /// </summary>
        internal void HideForStack()
        {
            IsHiddenByStack = true;

            if (Transition != null)
                Transition.SetCloseImmediate();
            else
            {
                if (CanvasGroup != null)
                {
                    CanvasGroup.alpha = 0f;
                    CanvasGroup.interactable = false;
                    CanvasGroup.blocksRaycasts = false;
                }
            }
        }

        /// <summary>
        /// 스택에서 복원 (Exclusive 모드).
        /// </summary>
        internal void RestoreFromStack()
        {
            IsHiddenByStack = false;

            if (Transition != null)
                Transition.Open();
            else
            {
                gameObject.SetActive(true);
                if (CanvasGroup != null)
                {
                    CanvasGroup.alpha = 1f;
                    CanvasGroup.interactable = true;
                    CanvasGroup.blocksRaycasts = true;
                }
            }
        }

        private void HandleOpenComplete()
        {
            if (Transition != null)
                Transition.OnOpenComplete -= HandleOpenComplete;

            OnShowComplete();
        }

        private void HandleCloseComplete()
        {
            if (Transition != null)
                Transition.OnCloseComplete -= HandleCloseComplete;

            OnHideComplete();
        }

        // ── 생명주기 훅 ──

        /// <summary>Show() 호출 직후 (데이터 세팅용)</summary>
        protected virtual void OnShowStart() { }

        /// <summary>열기 애니메이션 완료 후</summary>
        protected virtual void OnShowComplete() { }

        /// <summary>Hide() 호출 직후</summary>
        protected virtual void OnHideStart() { }

        /// <summary>닫기 완료 + SetActive(false) 후</summary>
        protected virtual void OnHideComplete() { }
    }
}
