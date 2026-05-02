using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Screens.Core.Transitions
{
    /// <summary>
    /// CanvasGroup.alpha 페이드 전환 (Enter: 0→1, Exit: 1→0).
    /// Target에 CanvasGroup이 없으면 자동 추가.
    /// </summary>
    [CreateAssetMenu(
        fileName = "FadeTransition",
        menuName = "Tjdtjq5/UIFramework/Transition/Fade")]
    public sealed class FadeTransition : TransitionAnimationObject
    {
        [SerializeField] private float _duration = 0.3f;
        [SerializeField] private Ease _ease = Ease.OutQuad;
        [SerializeField] private TransitionDirection _direction = TransitionDirection.Enter;

        public override float Duration => _duration;

        public override async UniTask PlayAsync(IProgress<float> progress = null, CancellationToken ct = default)
        {
            if (Target == null) return;

            var canvasGroup = Target.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = Target.gameObject.AddComponent<CanvasGroup>();

            var (from, to) = _direction == TransitionDirection.Enter ? (0f, 1f) : (1f, 0f);
            canvasGroup.alpha = from;

            if (_duration <= 0f)
            {
                canvasGroup.alpha = to;
                progress?.Report(1f);
                return;
            }

            var handle = LMotion.Create(from, to, _duration)
                .WithEase(_ease)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(a =>
                {
                    canvasGroup.alpha = a;
                    progress?.Report(Mathf.Abs(a - from) / Mathf.Max(0.0001f, Mathf.Abs(to - from)));
                });

            await handle.ToValueTask(CancelBehavior.Cancel, true, ct);
        }
    }
}
