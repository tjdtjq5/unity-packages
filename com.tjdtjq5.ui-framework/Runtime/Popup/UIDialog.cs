using System;
using Tjdtjq5.EditorToolkit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// 확인 / 확인+취소 메시지 다이얼로그.
    /// UIPopup 상속으로 UITransition, UIManager 스택 자동 지원.
    ///
    /// 사용:
    ///   _dialog.ShowConfirm("알림", "저장되었습니다");
    ///   _dialog.ShowChoice("경고", "삭제하시겠습니까?", OnDelete);
    /// </summary>
    public class UIDialog : UIPopup
    {
        [SectionHeader("Dialog Text", 0.4f, 0.92f, 0.92f)]
        [Required] [SerializeField] private TMP_Text _titleText;
        [Required] [SerializeField] private TMP_Text _messageText;

        [SectionHeader("Buttons", 0.4f, 0.75f, 0.4f)]
        [Required] [SerializeField] private Button _confirmButton;
        [SerializeField] private Button _cancelButton;
        [BoxGroup("Labels")]
        [SerializeField] private TMP_Text _confirmLabel;
        [BoxGroup("Labels")]
        [SerializeField] private TMP_Text _cancelLabel;

        private Action _onConfirm;
        private Action _onCancel;

        protected override void Awake()
        {
            base.Awake();

            _confirmButton.onClick.AddListener(OnConfirmClicked);
            if (_cancelButton != null)
                _cancelButton.onClick.AddListener(OnCancelClicked);
        }

        /// <summary>
        /// 확인 버튼만 있는 다이얼로그.
        /// </summary>
        public void ShowConfirm(string title, string message,
            Action onConfirm = null, string confirmText = "확인")
        {
            SetupDialog(title, message, onConfirm, null, confirmText, null);

            if (_cancelButton != null)
                _cancelButton.gameObject.SetActive(false);

            Show();
        }

        /// <summary>
        /// 확인 + 취소 버튼 다이얼로그.
        /// </summary>
        public void ShowChoice(string title, string message,
            Action onConfirm, Action onCancel = null,
            string confirmText = "확인", string cancelText = "취소")
        {
            SetupDialog(title, message, onConfirm, onCancel, confirmText, cancelText);

            if (_cancelButton != null)
                _cancelButton.gameObject.SetActive(true);

            Show();
        }

        private void SetupDialog(string title, string message,
            Action onConfirm, Action onCancel,
            string confirmText, string cancelText)
        {
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            if (_titleText != null) _titleText.text = title;
            if (_messageText != null) _messageText.text = message;
            if (_confirmLabel != null) _confirmLabel.text = confirmText;
            if (_cancelLabel != null && cancelText != null) _cancelLabel.text = cancelText;
        }

        private void OnConfirmClicked()
        {
            _onConfirm?.Invoke();
            Hide();
        }

        private void OnCancelClicked()
        {
            _onCancel?.Invoke();
            Hide();
        }

        protected override void OnHideComplete()
        {
            base.OnHideComplete();
            _onConfirm = null;
            _onCancel = null;
        }
    }
}
