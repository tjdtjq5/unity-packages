using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Tjdtjq5.AddrX;
using Tjdtjq5.UIFramework.Screens.Core;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using AddrXApi = Tjdtjq5.AddrX.AddrX;

namespace Tjdtjq5.UIFramework.Screens.Sheet
{
    /// <summary>
    /// Sheet 컨테이너. 탭형 UI 관리 (history 없음, 단일 active sheet).
    ///
    /// 흐름: RegisterAsync(addressableKey) → ShowAsync(sheetId) → HideAsync().
    /// 등록된 Sheet은 Container 파괴 시까지 재사용 (재로드 안 함).
    ///
    /// 의존성: AddrX (에셋 로드), UniTask (async), R3 (이벤트), VContainer (선택적 DI).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SheetContainer : MonoBehaviour, IScreenContainer
    {
        readonly Dictionary<int, Sheet> _sheets = new();
        readonly Dictionary<int, SafeHandle<GameObject>> _handles = new();
        readonly SheetEvents _events = new();

        CanvasGroup _canvasGroup;
        IObjectResolver _resolver;

        int _activeSheetId = -1;
        int _nextId;
        bool _isInTransition;

        /// <summary>VContainer 사용 시 IObjectResolver 자동 주입. DI 미사용 시 null 안전.</summary>
        [Inject]
        public void Construct(IObjectResolver resolver)
        {
            _resolver = resolver;
        }

        /// <summary>R3 Observable로 lifecycle 관찰.</summary>
        public SheetEvents Events => _events;

        /// <summary>등록된 모든 Sheet (sheetId → instance).</summary>
        public IReadOnlyDictionary<int, Sheet> Sheets => _sheets;

        /// <summary>현재 활성 Sheet ID. 없으면 -1.</summary>
        public int ActiveSheetId => _activeSheetId;

        public bool IsInTransition => _isInTransition;

        public bool Interactable
        {
            get => _canvasGroup.interactable;
            set => _canvasGroup.interactable = value;
        }

        void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        async void OnDestroy()
        {
            // Cleanup 호출 후 핸들 해제
            foreach (var (_, sheet) in _sheets)
            {
                if (sheet != null) await sheet.BeforeReleaseAsync();
            }

            foreach (var (_, handle) in _handles)
            {
                handle?.Dispose();
            }

            _sheets.Clear();
            _handles.Clear();
            _events.Dispose();
        }

        /// <summary>
        /// Addressable 프리팹을 로드해 Sheet 인스턴스화 + 컨테이너에 등록.
        /// </summary>
        /// <param name="addressableKey">프리팹의 Addressable 키.</param>
        /// <param name="ct">취소 토큰.</param>
        /// <returns>등록된 sheet의 ID. ShowAsync에 전달.</returns>
        public async UniTask<int> RegisterAsync(string addressableKey, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(addressableKey))
                throw new ArgumentNullException(nameof(addressableKey));

            var handle = await AddrXApi.InstantiateAsync(addressableKey, transform);
            ct.ThrowIfCancellationRequested();

            var go = handle.Value;
            var sheet = go.GetComponent<Sheet>();
            if (sheet == null)
            {
                handle.Dispose();
                throw new InvalidOperationException(
                    $"[SheetContainer] '{addressableKey}' 프리팹에 Sheet 컴포넌트가 없습니다.");
            }

            // VContainer DI 주입 (선택적)
            _resolver?.InjectGameObject(go);

            var id = _nextId++;
            _sheets[id] = sheet;
            _handles[id] = handle;

            await sheet.AfterLoadAsync((RectTransform)transform);

            return id;
        }

        /// <summary>등록 해제 + GameObject 파괴.</summary>
        public async UniTask UnregisterAsync(int sheetId)
        {
            if (!_sheets.TryGetValue(sheetId, out var sheet)) return;
            if (_activeSheetId == sheetId) _activeSheetId = -1;

            if (sheet != null) await sheet.BeforeReleaseAsync();

            if (_handles.TryGetValue(sheetId, out var handle))
            {
                handle.Dispose();
                _handles.Remove(sheetId);
            }

            _sheets.Remove(sheetId);
        }

        /// <summary>지정한 Sheet 표시. 이전 active sheet은 자동 숨김.</summary>
        public async UniTask ShowAsync(int sheetId, bool playAnimation = true, CancellationToken ct = default)
        {
            if (_isInTransition)
                throw new InvalidOperationException("[SheetContainer] 이미 전환 중입니다.");

            if (!_sheets.TryGetValue(sheetId, out var enterSheet))
                throw new KeyNotFoundException($"[SheetContainer] Sheet ID '{sheetId}'를 찾을 수 없습니다.");

            if (sheetId == _activeSheetId) return;

            _isInTransition = true;
            try
            {
                Sheet exitSheet = null;
                if (_activeSheetId >= 0)
                    _sheets.TryGetValue(_activeSheetId, out exitSheet);

                // BeforeEnter / BeforeExit
                await enterSheet.BeforeEnterAsync();
                _events.NotifyWillEnter(enterSheet);

                if (exitSheet != null)
                {
                    await exitSheet.BeforeExitAsync();
                    _events.NotifyWillExit(exitSheet);
                }

                // 병렬 transition (enter + exit 동시)
                var enterTask = enterSheet.EnterAsync(playAnimation, ct);
                var exitTask = exitSheet != null
                    ? exitSheet.ExitAsync(playAnimation, ct)
                    : UniTask.CompletedTask;

                await UniTask.WhenAll(enterTask, exitTask);

                // After
                if (exitSheet != null)
                {
                    exitSheet.AfterExit();
                    _events.NotifyDidExit(exitSheet);
                }
                enterSheet.AfterEnter();
                _events.NotifyDidEnter(enterSheet);

                _activeSheetId = sheetId;
            }
            finally
            {
                _isInTransition = false;
            }
        }

        /// <summary>현재 활성 Sheet 숨김. active sheet이 없으면 무시.</summary>
        public async UniTask HideAsync(bool playAnimation = true, CancellationToken ct = default)
        {
            if (_isInTransition)
                throw new InvalidOperationException("[SheetContainer] 이미 전환 중입니다.");

            if (_activeSheetId < 0) return;
            if (!_sheets.TryGetValue(_activeSheetId, out var sheet)) return;

            _isInTransition = true;
            try
            {
                await sheet.BeforeExitAsync();
                _events.NotifyWillExit(sheet);

                await sheet.ExitAsync(playAnimation, ct);

                sheet.AfterExit();
                _events.NotifyDidExit(sheet);

                _activeSheetId = -1;
            }
            finally
            {
                _isInTransition = false;
            }
        }
    }
}
