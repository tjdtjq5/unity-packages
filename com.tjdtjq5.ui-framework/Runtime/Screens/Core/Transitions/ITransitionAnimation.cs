using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Screens.Core.Transitions
{
    /// <summary>
    /// Page/Modal/Sheet 전환 애니메이션 추상화.
    /// 구현체는 ScriptableObject (TransitionAnimationObject) 또는 POCO로 작성.
    /// </summary>
    public interface ITransitionAnimation
    {
        /// <summary>전환 지속 시간 (초). 0이면 즉시 완료.</summary>
        float Duration { get; }

        /// <summary>대상 RectTransform 셋업. PlayAsync 직전 호출됨.</summary>
        void Setup(RectTransform rectTransform);

        /// <summary>
        /// 파트너 RectTransform 셋업 (선택).
        /// Page/Modal에서 push 시 함께 전환되는 이전 화면을 알기 위함. Sheet은 무시.
        /// </summary>
        void SetPartner(RectTransform partnerRectTransform);

        /// <summary>
        /// 전환 실행. progress는 0~1 진행률을 보고하고, ct cancel 시 중단.
        /// </summary>
        UniTask PlayAsync(IProgress<float> progress = null, CancellationToken ct = default);
    }
}
