using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Screens.Core.Transitions
{
    /// <summary>
    /// 전환 애니메이션 ScriptableObject 베이스. 에셋으로 만들어 재사용/재참조 가능.
    /// 구체 클래스는 PlayAsync만 구현하면 됨.
    /// </summary>
    public abstract class TransitionAnimationObject : ScriptableObject, ITransitionAnimation
    {
        /// <summary>전환 지속 시간 (초). 구체 클래스에서 SerializeField로 노출.</summary>
        public abstract float Duration { get; }

        /// <summary>대상 RectTransform — PlayAsync에서 사용.</summary>
        protected RectTransform Target { get; private set; }

        /// <summary>파트너 RectTransform — Page/Modal push 시 이전 화면. Sheet은 null.</summary>
        protected RectTransform Partner { get; private set; }

        public void Setup(RectTransform rectTransform)
        {
            Target = rectTransform;
        }

        public void SetPartner(RectTransform partnerRectTransform)
        {
            Partner = partnerRectTransform;
        }

        public abstract UniTask PlayAsync(IProgress<float> progress = null, CancellationToken ct = default);
    }

    /// <summary>전환 방향 — Fade/Scale 등 공통.</summary>
    public enum TransitionDirection
    {
        /// <summary>화면 진입 (alpha 0→1, scale 0.8→1 등).</summary>
        Enter,
        /// <summary>화면 퇴장 (alpha 1→0, scale 1→0.8 등).</summary>
        Exit
    }
}
