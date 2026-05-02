using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Screens.Core.Transitions
{
    /// <summary>
    /// 스케일 전환 (Enter: fromScale → 1, Exit: 1 → fromScale) + alpha 페이드.
    /// Modal/Popup 류에 적합.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ScaleTransition",
        menuName = "Tjdtjq5/UIFramework/Transition/Scale")]
    public sealed class ScaleTransition : TransitionAnimationObject
    {
        [SerializeField] private float _duration = 0.3f;
        [SerializeField] private Ease _ease = Ease.OutBack;
        [SerializeField] private TransitionDirection _direction = TransitionDirection.Enter;
        [SerializeField] private float _fromScale = 0.8f;
        [SerializeField] private bool _withFade = true;

        public override float Duration => _duration;

        public override async UniTask PlayAsync(IProgress<float> progress = null, CancellationToken ct = default)
        {
            if (Target == null) return;

            CanvasGroup canvasGroup = null;
            if (_withFade)
            {
                canvasGroup = Target.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = Target.gameObject.AddComponent<CanvasGroup>();
            }

            var enter = _direction == TransitionDirection.Enter;
            var (scaleFrom, scaleTo) = enter
                ? (Vector3.one * _fromScale, Vector3.one)
                : (Vector3.one, Vector3.one * _fromScale);
            var (alphaFrom, alphaTo) = enter ? (0f, 1f) : (1f, 0f);

            Target.localScale = scaleFrom;
            if (canvasGroup != null) canvasGroup.alpha = alphaFrom;

            if (_duration <= 0f)
            {
                Target.localScale = scaleTo;
                if (canvasGroup != null) canvasGroup.alpha = alphaTo;
                progress?.Report(1f);
                return;
            }

            var scaleHandle = LMotion.Create(scaleFrom, scaleTo, _duration)
                .WithEase(_ease)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(s =>
                {
                    Target.localScale = s;
                    progress?.Report(Mathf.InverseLerp(scaleFrom.x, scaleTo.x, s.x));
                });

            MotionHandle alphaHandle = default;
            if (canvasGroup != null)
            {
                alphaHandle = LMotion.Create(alphaFrom, alphaTo, _duration)
                    .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                    .BindToAlpha(canvasGroup);
            }

            try
            {
                await scaleHandle.ToValueTask(CancelBehavior.Cancel, true, ct);
            }
            finally
            {
                alphaHandle.TryCancel();
            }
        }
    }
}
