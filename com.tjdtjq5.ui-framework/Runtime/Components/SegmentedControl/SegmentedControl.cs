using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework.Components
{
    /// <summary>
    /// iOS 스타일 토글 버튼 묶음. 자식 Selectable(Button/Toggle 등)을 segment로 사용.
    /// 한 번에 하나만 선택. allowSwitchingOff 시 다시 클릭으로 해제 가능.
    ///
    /// 단순화 적용:
    /// - Sprite cutting (8-slice 잘라내기) 제거 — 디자이너가 좌/우 sprite 직접 준비
    /// - ColorTint transition만 지원 (SpriteSwap/Animation 제거)
    ///
    /// Origin: Unity-UI-Extensions SegmentedControl (David Gileadi, BSD-3).
    /// 우리 namespace + 단순화.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SegmentedControl : MonoBehaviour
    {
        [Serializable] public class SegmentSelectedEvent : UnityEvent<int> { }

        [SerializeField] private bool _allowSwitchingOff;
        [SerializeField] private int _selectedSegmentIndex = -1;
        [SerializeField] private SegmentSelectedEvent _onValueChanged = new();

        Selectable[] _segments;
        Selectable _selectedSegment;

        public Selectable[] Segments
        {
            get
            {
                if (_segments == null || _segments.Length == 0) _segments = ResolveChildSegments();
                return _segments;
            }
        }

        public bool AllowSwitchingOff
        {
            get => _allowSwitchingOff;
            set => _allowSwitchingOff = value;
        }

        public int SelectedSegmentIndex
        {
            get => Array.IndexOf(Segments, _selectedSegment);
            set
            {
                value = Mathf.Clamp(value, -1, Segments.Length - 1);
                if (_selectedSegmentIndex == value) return;

                _selectedSegmentIndex = value;
                ChangeSelectedSegment(value);
            }
        }

        public SegmentSelectedEvent OnValueChanged
        {
            get => _onValueChanged;
            set => _onValueChanged = value;
        }

        internal Selectable SelectedSegment => _selectedSegment;

        void Start()
        {
            ResolveAndApply();
        }

        void OnEnable()
        {
            ResolveAndApply();
        }

        void ResolveAndApply()
        {
            _segments = ResolveChildSegments();
            if (_selectedSegmentIndex >= 0 && _selectedSegmentIndex < _segments.Length)
                ChangeSelectedSegment(_selectedSegmentIndex);
        }

        Selectable[] ResolveChildSegments()
        {
            var buttons = GetComponentsInChildren<Selectable>(includeInactive: true);
            for (int i = 0; i < buttons.Length; i++)
            {
                var seg = buttons[i].GetComponent<Segment>();
                if (seg != null)
                {
                    seg.Index = i;
                    seg.SegmentedControl = this;
                }
            }
            return buttons;
        }

        internal void NotifySelectionChanged(Selectable target, bool isSelected)
        {
            if (isSelected)
            {
                if (_selectedSegment == target)
                {
                    if (_allowSwitchingOff) DeselectInternal(invokeEvent: true);
                    return;
                }

                // 이전 선택 해제
                if (_selectedSegment != null)
                {
                    var prev = _selectedSegment.GetComponent<Segment>();
                    if (prev != null) prev.ApplyVisualState(false);
                }

                _selectedSegment = target;
                var seg = target.GetComponent<Segment>();
                if (seg != null) seg.ApplyVisualState(true);

                _selectedSegmentIndex = SelectedSegmentIndex;
                _onValueChanged?.Invoke(_selectedSegmentIndex);
            }
            else if (_selectedSegment == target)
            {
                DeselectInternal(invokeEvent: true);
            }
        }

        void ChangeSelectedSegment(int index)
        {
            if (_selectedSegment != null)
            {
                var prev = _selectedSegment.GetComponent<Segment>();
                if (prev != null) prev.ApplyVisualState(false);
            }

            if (index < 0 || index >= _segments.Length)
            {
                _selectedSegment = null;
                _onValueChanged?.Invoke(-1);
                return;
            }

            _selectedSegment = _segments[index];
            var seg = _selectedSegment.GetComponent<Segment>();
            if (seg != null) seg.ApplyVisualState(true);

            _onValueChanged?.Invoke(index);
        }

        void DeselectInternal(bool invokeEvent)
        {
            if (_selectedSegment != null)
            {
                var seg = _selectedSegment.GetComponent<Segment>();
                if (seg != null) seg.ApplyVisualState(false);
            }
            _selectedSegment = null;
            _selectedSegmentIndex = -1;
            if (invokeEvent) _onValueChanged?.Invoke(-1);
        }
    }
}
