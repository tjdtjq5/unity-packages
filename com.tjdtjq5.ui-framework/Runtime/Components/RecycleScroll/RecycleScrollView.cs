using System.Collections.Generic;
using DG.Tweening;
using Tjdtjq5.EditorToolkit;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework
{
    public enum ScrollDirection { Vertical, Horizontal }
    public enum SnapMode { None, Cell, Page }

    /// <summary>
    /// 무한 재사용 스크롤. 화면에 보이는 셀 + 버퍼만 유지하고 나머지는 재활용.
    /// </summary>
    [RequireComponent(typeof(ScrollRect))]
    public class RecycleScrollView : MonoBehaviour, IBeginDragHandler, IEndDragHandler
    {
        [SectionHeader("RecycleScrollView", 0.3f, 0.8f, 0.6f)]
        [SerializeField] private GameObject _cellPrefab;
        [SerializeField] private ScrollDirection _direction = ScrollDirection.Vertical;

        [BoxGroup("Grid")]
        [SerializeField] private int _gridCount = 1;

        [BoxGroup("Spacing")]
        [SerializeField] private float _spacing;
        [BoxGroup("Spacing")]
        [SerializeField] private RectOffset _padding;

        [BoxGroup("Snap")]
        [SerializeField] private SnapMode _snapMode = SnapMode.None;
        [BoxGroup("Snap")]
        [SerializeField] private float _snapDuration = 0.2f;
        [BoxGroup("Snap")]
        [SerializeField] private Ease _snapEase = Ease.OutCubic;

        [BoxGroup("Cell Size")]
        [SerializeField] private bool _autoDetectCellSize = true;
        [BoxGroup("Cell Size")]
        [SerializeField] private Vector2 _cellSize = new(100f, 100f);

        private ScrollRect _scrollRect;
        private RectTransform _viewport;
        private RectTransform _content;

        private int _totalCount;
        private int _totalRows;
        private Vector2 _actualCellSize;
        private float _rowHeight;   // 세로 모드: 행 높이, 가로 모드: 열 폭
        private int _visibleRows;   // 화면에 보이는 줄 수
        private int _bufferRows = 2;
        private int _poolRows;      // visibleRows + bufferRows

        private readonly List<RectTransform> _pool = new();
        private readonly List<IScrollCell> _cellComponents = new();
        private readonly List<int> _cellIndices = new();
        private int _headRow;       // 풀에서 가장 위(왼쪽)에 있는 줄 인덱스

        private bool _initialized;
        private bool _isDragging;
        private Tweener _snapTween;

        private void Awake()
        {
            _scrollRect = GetComponent<ScrollRect>();
            _viewport = _scrollRect.viewport != null
                ? _scrollRect.viewport
                : _scrollRect.GetComponent<RectTransform>();
            _content = _scrollRect.content;
        }

        /// <summary>
        /// 스크롤을 초기화하고 셀을 표시한다.
        /// </summary>
        public void Init(int totalCount)
        {
            _totalCount = Mathf.Max(0, totalCount);
            _totalRows = Mathf.CeilToInt((float)_totalCount / Mathf.Max(1, _gridCount));
            DetectCellSize();
            SetupScrollRect();
            CalculateMetrics();
            BuildPool();
            UpdateContentSize();
            SetContentPosition(0f);
            RefreshVisibleCells();
            _initialized = true;

            _scrollRect.onValueChanged.RemoveListener(OnScroll);
            _scrollRect.onValueChanged.AddListener(OnScroll);
        }

        /// <summary>
        /// 데이터 개수를 변경하고 현재 위치를 유지하며 갱신한다.
        /// </summary>
        public void UpdateCount(int totalCount)
        {
            _totalCount = Mathf.Max(0, totalCount);
            _totalRows = Mathf.CeilToInt((float)_totalCount / Mathf.Max(1, _gridCount));
            UpdateContentSize();
            RefreshVisibleCells();
        }

        /// <summary>
        /// 특정 인덱스의 셀이 보이도록 스크롤한다.
        /// </summary>
        public void ScrollTo(int index)
        {
            if (!_initialized || _totalCount == 0) return;
            index = Mathf.Clamp(index, 0, _totalCount - 1);

            int targetRow = index / _gridCount;
            float pos = targetRow * _rowHeight + GetPaddingStart();
            float maxScroll = GetContentLength() - GetViewportLength();
            pos = Mathf.Clamp(pos, 0f, Mathf.Max(0f, maxScroll));

            KillSnap();
            SetContentPosition(pos);
            RefreshVisibleCells();
        }

        /// <summary>
        /// 현재 보이는 모든 셀을 갱신한다.
        /// </summary>
        public void RefreshAll()
        {
            if (!_initialized) return;
            RefreshVisibleCells();
        }

        /// <summary>
        /// 특정 인덱스의 셀만 갱신한다. 화면에 없으면 무시.
        /// </summary>
        public void RefreshCell(int index)
        {
            if (!_initialized) return;
            for (int i = 0; i < _cellIndices.Count; i++)
            {
                if (_cellIndices[i] == index)
                {
                    _cellComponents[i].OnUpdateCell(index);
                    return;
                }
            }
        }

        // ── Setup ──

        private void DetectCellSize()
        {
            if (_autoDetectCellSize && _cellPrefab != null)
            {
                var rt = _cellPrefab.GetComponent<RectTransform>();
                if (rt != null)
                    _cellSize = rt.sizeDelta;
            }
            _actualCellSize = _cellSize;
        }

        private void SetupScrollRect()
        {
            _scrollRect.horizontal = _direction == ScrollDirection.Horizontal;
            _scrollRect.vertical = _direction == ScrollDirection.Vertical;

            if (_content == null)
            {
                var go = new GameObject("Content", typeof(RectTransform));
                go.transform.SetParent(_scrollRect.transform, false);
                _content = go.GetComponent<RectTransform>();
                _scrollRect.content = _content;
            }

            // Content 앵커 설정
            if (_direction == ScrollDirection.Vertical)
            {
                _content.anchorMin = new Vector2(0f, 1f);
                _content.anchorMax = Vector2.one;
                _content.pivot = new Vector2(0.5f, 1f);
            }
            else
            {
                _content.anchorMin = Vector2.zero;
                _content.anchorMax = new Vector2(0f, 1f);
                _content.pivot = new Vector2(0f, 0.5f);
            }
            _content.anchoredPosition = Vector2.zero;
        }

        private void CalculateMetrics()
        {
            _gridCount = Mathf.Max(1, _gridCount);

            _rowHeight = (_direction == ScrollDirection.Vertical)
                ? _actualCellSize.y + _spacing
                : _actualCellSize.x + _spacing;

            float viewLen = GetViewportLength();
            _visibleRows = Mathf.CeilToInt(viewLen / _rowHeight) + 1;
            _poolRows = _visibleRows + _bufferRows;
        }

        private void BuildPool()
        {
            // 기존 풀 정리
            foreach (var rt in _pool)
            {
                if (rt != null)
                    Destroy(rt.gameObject);
            }
            _pool.Clear();
            _cellComponents.Clear();
            _cellIndices.Clear();

            int poolSize = _poolRows * _gridCount;
            for (int i = 0; i < poolSize; i++)
            {
                var go = Instantiate(_cellPrefab, _content);
                var rt = go.GetComponent<RectTransform>();
                var cell = go.GetComponent<IScrollCell>();

                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.sizeDelta = _actualCellSize;

                _pool.Add(rt);
                _cellComponents.Add(cell);
                _cellIndices.Add(-1);
                go.SetActive(false);
            }
            _headRow = 0;
        }

        private void UpdateContentSize()
        {
            float length = _totalRows * _rowHeight - _spacing + GetPaddingStart() + GetPaddingEnd();
            length = Mathf.Max(0f, length);

            if (_direction == ScrollDirection.Vertical)
                _content.sizeDelta = new Vector2(0f, length);
            else
                _content.sizeDelta = new Vector2(length, 0f);
        }

        // ── Scroll Handling ──

        private void OnScroll(Vector2 _)
        {
            if (!_initialized) return;
            RefreshVisibleCells();
        }

        private void RefreshVisibleCells()
        {
            float scrollPos = GetScrollPosition();
            int firstVisibleRow = Mathf.FloorToInt(Mathf.Max(0f, scrollPos - GetPaddingStart()) / _rowHeight);

            // 풀의 head를 조정하여 보이는 영역 커버
            int targetHead = Mathf.Max(0, firstVisibleRow - 1);
            targetHead = Mathf.Min(targetHead, Mathf.Max(0, _totalRows - _poolRows));
            _headRow = targetHead;

            for (int p = 0; p < _pool.Count; p++)
            {
                int rowInPool = p / _gridCount;
                int colInPool = p % _gridCount;
                int dataRow = _headRow + rowInPool;
                int dataIndex = dataRow * _gridCount + colInPool;

                var rt = _pool[p];
                var cell = _cellComponents[p];

                if (dataIndex < 0 || dataIndex >= _totalCount || dataRow >= _totalRows)
                {
                    if (rt.gameObject.activeSelf)
                    {
                        if (_cellIndices[p] >= 0)
                            cell?.OnRecycled();
                        rt.gameObject.SetActive(false);
                        _cellIndices[p] = -1;
                    }
                    continue;
                }

                // 위치 계산
                PositionCell(rt, dataRow, colInPool);

                if (!rt.gameObject.activeSelf)
                    rt.gameObject.SetActive(true);

                if (_cellIndices[p] != dataIndex)
                {
                    if (_cellIndices[p] >= 0)
                        cell?.OnRecycled();
                    _cellIndices[p] = dataIndex;
                    cell?.OnUpdateCell(dataIndex);
                }
            }
        }

        private void PositionCell(RectTransform rt, int row, int col)
        {
            if (_direction == ScrollDirection.Vertical)
            {
                float x = GetPaddingLeft() + col * (_actualCellSize.x + _spacing);
                float y = -(GetPaddingStart() + row * _rowHeight);
                rt.anchoredPosition = new Vector2(x, y);
            }
            else
            {
                float x = GetPaddingStart() + row * _rowHeight;
                float y = -(GetPaddingTop() + col * (_actualCellSize.y + _spacing));
                rt.anchoredPosition = new Vector2(x, y);
            }
        }

        // ── Snap ──

        public void OnBeginDrag(PointerEventData eventData) => _isDragging = true;

        public void OnEndDrag(PointerEventData eventData)
        {
            _isDragging = false;
            if (_snapMode != SnapMode.None)
                PerformSnap();
        }

        private void LateUpdate()
        {
            if (!_initialized || _isDragging || _snapMode == SnapMode.None) return;
            if (_snapTween != null && _snapTween.IsActive()) return;

            // Inertia 감속 중 → 속도가 충분히 줄면 스냅
            float vel = _scrollRect.velocity.sqrMagnitude;
            if (vel > 0.01f && vel < 100f)
                PerformSnap();
        }

        private void PerformSnap()
        {
            KillSnap();

            float scrollPos = GetScrollPosition();
            float snapUnit = _snapMode == SnapMode.Page
                ? _visibleRows * _rowHeight
                : _rowHeight;

            float snapped = Mathf.Round(scrollPos / snapUnit) * snapUnit;
            float maxScroll = GetContentLength() - GetViewportLength();
            snapped = Mathf.Clamp(snapped, 0f, Mathf.Max(0f, maxScroll));

            _scrollRect.velocity = Vector2.zero;
            _scrollRect.StopMovement();

            float current = GetScrollPosition();
            _snapTween = DOTween.To(
                () => current,
                v =>
                {
                    current = v;
                    SetContentPosition(v);
                    RefreshVisibleCells();
                },
                snapped,
                _snapDuration
            ).SetEase(_snapEase).SetUpdate(true);
        }

        private void KillSnap()
        {
            _snapTween?.Kill();
            _snapTween = null;
        }

        // ── Helpers ──

        private float GetScrollPosition()
        {
            return _direction == ScrollDirection.Vertical
                ? -_content.anchoredPosition.y
                : -_content.anchoredPosition.x;
        }

        private void SetContentPosition(float pos)
        {
            if (_direction == ScrollDirection.Vertical)
                _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, -pos);
            else
                _content.anchoredPosition = new Vector2(-pos, _content.anchoredPosition.y);
        }

        private float GetViewportLength()
        {
            var rect = _viewport.rect;
            return _direction == ScrollDirection.Vertical ? rect.height : rect.width;
        }

        private float GetContentLength()
        {
            return _direction == ScrollDirection.Vertical
                ? _content.sizeDelta.y
                : _content.sizeDelta.x;
        }

        private float GetPaddingStart()
        {
            if (_padding == null) return 0f;
            return _direction == ScrollDirection.Vertical ? _padding.top : _padding.left;
        }

        private float GetPaddingEnd()
        {
            if (_padding == null) return 0f;
            return _direction == ScrollDirection.Vertical ? _padding.bottom : _padding.right;
        }

        private float GetPaddingLeft()
        {
            return _padding != null ? _padding.left : 0f;
        }

        private float GetPaddingTop()
        {
            return _padding != null ? _padding.top : 0f;
        }

        private void OnDestroy()
        {
            KillSnap();
            if (_scrollRect != null)
                _scrollRect.onValueChanged.RemoveListener(OnScroll);
        }
    }
}
