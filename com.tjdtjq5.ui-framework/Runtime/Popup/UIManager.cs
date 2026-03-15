using System.Collections.Generic;
using UnityEngine;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// 팝업 스택 관리 + 순차 큐 + Time.timeScale 중앙 관리 + Back 버튼 처리.
    /// </summary>
    [AddComponentMenu("UI/UI Manager")]
    public class UIManager : MonoBehaviour
    {
        private readonly List<UIPopup> _stack = new();
        private readonly Queue<UIPopup> _pendingQueue = new();
        private int _pauseCount;

        /// <summary>현재 열린 팝업 수</summary>
        public int OpenCount => _stack.Count;

        /// <summary>큐에 대기 중인 팝업 수</summary>
        public int QueueCount => _pendingQueue.Count;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && _stack.Count > 0)
            {
                var top = _stack[^1];
                if (top.CloseOnBack)
                    top.Hide();
            }
        }

        /// <summary>
        /// 팝업이 열릴 때 호출. 스택에 추가하고 Exclusive 모드면 이전 팝업 숨김.
        /// </summary>
        public void OnPopupOpen(UIPopup popup)
        {
            // Exclusive 모드: 이전 팝업 숨기기
            if (popup.StackMode == PopupStackMode.Exclusive && _stack.Count > 0)
            {
                var prev = _stack[^1];
                if (prev.IsOpen && !prev.IsHiddenByStack)
                    prev.HideForStack();
            }

            _stack.Add(popup);

            if (popup.PauseGame)
            {
                _pauseCount++;
                if (_pauseCount > 0)
                    Time.timeScale = 0f;
            }
        }

        /// <summary>
        /// 팝업이 닫힐 때 호출. 스택에서 제거하고 이전 팝업 복원 / 큐 다음 팝업 Show.
        /// </summary>
        public void OnPopupClose(UIPopup popup)
        {
            _stack.Remove(popup);

            if (popup.PauseGame)
            {
                _pauseCount--;
                if (_pauseCount <= 0)
                {
                    _pauseCount = 0;
                    Time.timeScale = 1f;
                }
            }

            // Exclusive 모드: 이전 팝업 복원
            if (popup.StackMode == PopupStackMode.Exclusive && _stack.Count > 0)
            {
                var prev = _stack[^1];
                if (prev.IsOpen && prev.IsHiddenByStack)
                    prev.RestoreFromStack();
            }

            // 큐: 스택이 비면 다음 팝업 Show
            if (_stack.Count == 0 && _pendingQueue.Count > 0)
            {
                var next = _pendingQueue.Dequeue();
                next.Show();
            }
        }

        /// <summary>
        /// 팝업을 큐에 추가. 현재 열린 팝업이 없으면 즉시 Show, 있으면 대기.
        /// A 닫히면 → B Show → B 닫히면 → C Show 순서.
        /// </summary>
        public void Enqueue(UIPopup popup)
        {
            if (_stack.Count == 0 && _pendingQueue.Count == 0)
                popup.Show();
            else
                _pendingQueue.Enqueue(popup);
        }

        /// <summary>
        /// 큐에 대기 중인 팝업 모두 제거 (현재 열린 팝업은 영향 없음).
        /// </summary>
        public void ClearQueue()
        {
            _pendingQueue.Clear();
        }

        /// <summary>
        /// 모든 열린 팝업을 즉시 닫고 큐도 비움.
        /// </summary>
        public void CloseAll()
        {
            _pendingQueue.Clear();
            for (int i = _stack.Count - 1; i >= 0; i--)
                _stack[i].HideImmediate();
        }
    }
}
