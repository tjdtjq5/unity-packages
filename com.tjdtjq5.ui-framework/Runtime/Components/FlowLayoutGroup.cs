using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework.Components
{
    /// <summary>
    /// 가변폭 자동 wrap LayoutGroup. 자식들이 한 줄에 안 들어가면 다음 줄로 넘어감.
    /// GridLayoutGroup(고정 cell)과 달리 자식별 다른 크기 허용. 태그/뱃지/칩 표시에 적합.
    ///
    /// Origin: Unity-UI-Extensions FlowLayoutGroup (Simie / Sharkbomb / Vicente Russo / Ramon Molossi).
    /// 우리 namespace로 이식, BSD-3 라이선스 표기.
    /// </summary>
    public class FlowLayoutGroup : LayoutGroup
    {
        public enum Axis { Horizontal = 0, Vertical = 1 }

        public float SpacingX = 0f;
        public float SpacingY = 0f;
        public bool ExpandHorizontalSpacing = false;
        public bool ChildForceExpandWidth = false;
        public bool ChildForceExpandHeight = false;
        public bool InvertOrder = false;

        [SerializeField] protected Axis _startAxis = Axis.Horizontal;

        public Axis StartAxis
        {
            get => _startAxis;
            set => SetProperty(ref _startAxis, value);
        }

        float _layoutHeight;
        float _layoutWidth;

        readonly List<RectTransform> _itemList = new();

        public override void CalculateLayoutInputHorizontal()
        {
            if (_startAxis == Axis.Horizontal)
            {
                base.CalculateLayoutInputHorizontal();
                var minWidth = GetGreatestMinimumChildWidth() + padding.left + padding.right;
                SetLayoutInputForAxis(minWidth, -1, -1, 0);
            }
            else
            {
                _layoutWidth = SetLayout(0, true);
            }
        }

        public override void CalculateLayoutInputVertical()
        {
            if (_startAxis == Axis.Horizontal)
                _layoutHeight = SetLayout(1, true);
            else
            {
                base.CalculateLayoutInputHorizontal();
                var minHeight = GetGreatestMinimumChildHeight() + padding.bottom + padding.top;
                SetLayoutInputForAxis(minHeight, -1, -1, 1);
            }
        }

        public override void SetLayoutHorizontal() => SetLayout(0, false);
        public override void SetLayoutVertical() => SetLayout(1, false);

        protected bool IsCenterAlign => childAlignment == TextAnchor.LowerCenter || childAlignment == TextAnchor.MiddleCenter || childAlignment == TextAnchor.UpperCenter;
        protected bool IsRightAlign => childAlignment == TextAnchor.LowerRight || childAlignment == TextAnchor.MiddleRight || childAlignment == TextAnchor.UpperRight;
        protected bool IsMiddleAlign => childAlignment == TextAnchor.MiddleLeft || childAlignment == TextAnchor.MiddleRight || childAlignment == TextAnchor.MiddleCenter;
        protected bool IsLowerAlign => childAlignment == TextAnchor.LowerLeft || childAlignment == TextAnchor.LowerRight || childAlignment == TextAnchor.LowerCenter;

        public float SetLayout(int axis, bool layoutInput)
        {
            var groupHeight = rectTransform.rect.height;
            var groupWidth = rectTransform.rect.width;

            float spacingBetweenBars, spacingBetweenElements, offset, counterOffset, groupSize, workingSize;

            if (_startAxis == Axis.Horizontal)
            {
                groupSize = groupHeight;
                workingSize = groupWidth - padding.left - padding.right;
                offset = IsLowerAlign ? padding.bottom : padding.top;
                counterOffset = IsLowerAlign ? padding.top : padding.bottom;
                spacingBetweenBars = SpacingY;
                spacingBetweenElements = SpacingX;
            }
            else
            {
                groupSize = groupWidth;
                workingSize = groupHeight - padding.top - padding.bottom;
                offset = IsRightAlign ? padding.right : padding.left;
                counterOffset = IsRightAlign ? padding.left : padding.right;
                spacingBetweenBars = SpacingX;
                spacingBetweenElements = SpacingY;
            }

            float currentBarSize = 0f;
            float currentBarSpace = 0f;

            for (int i = 0; i < rectChildren.Count; i++)
            {
                int index = i;
                if (InvertOrder)
                {
                    if (_startAxis == Axis.Horizontal && IsLowerAlign) index = rectChildren.Count - 1 - i;
                    else if (_startAxis == Axis.Vertical && IsRightAlign) index = rectChildren.Count - 1 - i;
                }

                var child = rectChildren[index];
                float childSize, childOtherSize;

                if (_startAxis == Axis.Horizontal)
                {
                    childSize = Mathf.Min(LayoutUtility.GetPreferredSize(child, 0), workingSize);
                    childOtherSize = LayoutUtility.GetPreferredSize(child, 1);
                }
                else
                {
                    childSize = Mathf.Min(LayoutUtility.GetPreferredSize(child, 1), workingSize);
                    childOtherSize = LayoutUtility.GetPreferredSize(child, 0);
                }

                if (currentBarSize + childSize > workingSize)
                {
                    currentBarSize -= spacingBetweenElements;

                    if (!layoutInput)
                    {
                        if (_startAxis == Axis.Horizontal)
                        {
                            float newOffset = CalculateRowVerticalOffset(groupSize, offset, currentBarSpace);
                            LayoutRow(_itemList, currentBarSize, currentBarSpace, workingSize, padding.left, newOffset, axis);
                        }
                        else
                        {
                            float newOffset = CalculateColHorizontalOffset(groupSize, offset, currentBarSpace);
                            LayoutCol(_itemList, currentBarSpace, currentBarSize, workingSize, newOffset, padding.top, axis);
                        }
                    }

                    _itemList.Clear();
                    offset += currentBarSpace + spacingBetweenBars;
                    currentBarSpace = 0;
                    currentBarSize = 0;
                }

                currentBarSize += childSize;
                _itemList.Add(child);
                currentBarSpace = childOtherSize > currentBarSpace ? childOtherSize : currentBarSpace;
                currentBarSize += spacingBetweenElements;
            }

            // 마지막 줄 처리
            if (!layoutInput)
            {
                currentBarSize -= spacingBetweenElements;
                if (_startAxis == Axis.Horizontal)
                {
                    float newOffset = CalculateRowVerticalOffset(groupHeight, offset, currentBarSpace);
                    LayoutRow(_itemList, currentBarSize, currentBarSpace, workingSize, padding.left, newOffset, axis);
                }
                else
                {
                    float newOffset = CalculateColHorizontalOffset(groupWidth, offset, currentBarSpace);
                    LayoutCol(_itemList, currentBarSpace, currentBarSize, workingSize, newOffset, padding.top, axis);
                }
            }

            _itemList.Clear();
            offset += currentBarSpace + counterOffset;

            if (layoutInput)
                SetLayoutInputForAxis(offset, offset, -1, axis);

            return offset;
        }

        float CalculateRowVerticalOffset(float groupHeight, float yOffset, float currentRowHeight)
        {
            if (IsLowerAlign) return groupHeight - yOffset - currentRowHeight;
            if (IsMiddleAlign) return groupHeight * 0.5f - _layoutHeight * 0.5f + yOffset;
            return yOffset;
        }

        float CalculateColHorizontalOffset(float groupWidth, float xOffset, float currentColWidth)
        {
            if (IsRightAlign) return groupWidth - xOffset - currentColWidth;
            if (IsCenterAlign) return groupWidth * 0.5f - _layoutWidth * 0.5f + xOffset;
            return xOffset;
        }

        protected void LayoutRow(IList<RectTransform> contents, float rowWidth, float rowHeight, float maxWidth, float xOffset, float yOffset, int axis)
        {
            float xPos = xOffset;

            if (!ChildForceExpandWidth && IsCenterAlign) xPos += (maxWidth - rowWidth) * 0.5f;
            else if (!ChildForceExpandWidth && IsRightAlign) xPos += maxWidth - rowWidth;

            float extraWidth = 0f;
            float extraSpacing = 0f;

            if (ChildForceExpandWidth) extraWidth = (maxWidth - rowWidth) / contents.Count;
            else if (ExpandHorizontalSpacing && contents.Count > 1)
            {
                extraSpacing = (maxWidth - rowWidth) / (contents.Count - 1);
                if (IsCenterAlign) xPos -= extraSpacing * 0.5f * (contents.Count - 1);
                else if (IsRightAlign) xPos -= extraSpacing * (contents.Count - 1);
            }

            for (int j = 0; j < contents.Count; j++)
            {
                int index = IsLowerAlign ? contents.Count - 1 - j : j;
                var rowChild = contents[index];

                float w = LayoutUtility.GetPreferredSize(rowChild, 0) + extraWidth;
                float h = LayoutUtility.GetPreferredSize(rowChild, 1);
                if (ChildForceExpandHeight) h = rowHeight;
                w = Mathf.Min(w, maxWidth);

                float yPos = yOffset;
                if (IsMiddleAlign) yPos += (rowHeight - h) * 0.5f;
                else if (IsLowerAlign) yPos += rowHeight - h;

                if (ExpandHorizontalSpacing && j > 0) xPos += extraSpacing;

                if (axis == 0) SetChildAlongAxis(rowChild, 0, xPos, w);
                else SetChildAlongAxis(rowChild, 1, yPos, h);

                if (j < contents.Count - 1) xPos += w + SpacingX;
            }
        }

        protected void LayoutCol(IList<RectTransform> contents, float colWidth, float colHeight, float maxHeight, float xOffset, float yOffset, int axis)
        {
            float yPos = yOffset;

            if (!ChildForceExpandHeight && IsMiddleAlign) yPos += (maxHeight - colHeight) * 0.5f;
            else if (!ChildForceExpandHeight && IsLowerAlign) yPos += maxHeight - colHeight;

            float extraHeight = 0f;
            float extraSpacing = 0f;

            if (ChildForceExpandHeight) extraHeight = (maxHeight - colHeight) / contents.Count;
            else if (ExpandHorizontalSpacing && contents.Count > 1)
            {
                extraSpacing = (maxHeight - colHeight) / (contents.Count - 1);
                if (IsMiddleAlign) yPos -= extraSpacing * 0.5f * (contents.Count - 1);
                else if (IsLowerAlign) yPos -= extraSpacing * (contents.Count - 1);
            }

            for (int j = 0; j < contents.Count; j++)
            {
                int index = IsRightAlign ? contents.Count - 1 - j : j;
                var rowChild = contents[index];

                float w = LayoutUtility.GetPreferredSize(rowChild, 0);
                float h = LayoutUtility.GetPreferredSize(rowChild, 1) + extraHeight;
                if (ChildForceExpandWidth) w = colWidth;
                h = Mathf.Min(h, maxHeight);

                float xPos = xOffset;
                if (IsCenterAlign) xPos += (colWidth - w) * 0.5f;
                else if (IsRightAlign) xPos += colWidth - w;

                if (ExpandHorizontalSpacing && j > 0) yPos += extraSpacing;

                if (axis == 0) SetChildAlongAxis(rowChild, 0, xPos, w);
                else SetChildAlongAxis(rowChild, 1, yPos, h);

                if (j < contents.Count - 1) yPos += h + SpacingY;
            }
        }

        public float GetGreatestMinimumChildWidth()
        {
            float max = 0f;
            for (int i = 0; i < rectChildren.Count; i++)
                max = Mathf.Max(LayoutUtility.GetMinWidth(rectChildren[i]), max);
            return max;
        }

        public float GetGreatestMinimumChildHeight()
        {
            float max = 0f;
            for (int i = 0; i < rectChildren.Count; i++)
                max = Mathf.Max(LayoutUtility.GetMinHeight(rectChildren[i]), max);
            return max;
        }

        protected override void OnDisable()
        {
            m_Tracker.Clear();
            LayoutRebuilder.MarkLayoutForRebuild(rectTransform);
        }
    }
}
