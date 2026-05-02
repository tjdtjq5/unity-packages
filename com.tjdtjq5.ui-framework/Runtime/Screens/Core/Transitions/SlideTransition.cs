using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;

namespace Tjdtjq5.UIFramework.Screens.Core.Transitions
{
    /// <summary>슬라이드 방향.</summary>
    public enum SlideDirection
    {
        /// <summary>좌측 → 우측 진입 / 우측 → 좌측 퇴장 (Enter/Exit 조합).</summary>
        FromLeft,
        FromRight,
        FromTop,
        FromBottom
    }

    /// <summary>
    /// 화면 슬라이드 전환. anchoredPosition 기준.
    /// Sheet/Page의 좌우 슬라이드, Modal의 위에서 내려옴 등에 사용.
    /// </summary>
    [CreateAssetMenu(
        fileName = "SlideTransition",
        menuName = "Tjdtjq5/UIFramework/Transition/Slide")]
    public sealed class SlideTransition : TransitionAnimationObject
    {
        [SerializeField] private float _duration = 0.3f;
        [SerializeField] private Ease _ease = Ease.OutCubic;
        [SerializeField] private TransitionDirection _direction = TransitionDirection.Enter;
        [SerializeField] private SlideDirection _slideFrom = SlideDirection.FromRight;

        public override float Duration => _duration;

        public override async UniTask PlayAsync(IProgress<float> progress = null, CancellationToken ct = default)
        {
            if (Target == null) return;

            // Target의 부모 RectTransform 크기 기준으로 화면 밖 위치 계산
            var parent = Target.parent as RectTransform;
            var size = parent != null ? parent.rect.size : new Vector2(Screen.width, Screen.height);

            Vector2 offscreen = _slideFrom switch
            {
                SlideDirection.FromLeft => new Vector2(-size.x, 0f),
                SlideDirection.FromRight => new Vector2(size.x, 0f),
                SlideDirection.FromTop => new Vector2(0f, size.y),
                SlideDirection.FromBottom => new Vector2(0f, -size.y),
                _ => Vector2.zero
            };

            var enter = _direction == TransitionDirection.Enter;
            var (from, to) = enter ? (offscreen, Vector2.zero) : (Vector2.zero, offscreen);

            Target.anchoredPosition = from;

            if (_duration <= 0f)
            {
                Target.anchoredPosition = to;
                progress?.Report(1f);
                return;
            }

            var handle = LMotion.Create(from, to, _duration)
                .WithEase(_ease)
                .WithScheduler(MotionScheduler.UpdateIgnoreTimeScale)
                .Bind(p =>
                {
                    Target.anchoredPosition = p;
                    progress?.Report(Vector2.Distance(p, from) / Mathf.Max(0.0001f, Vector2.Distance(to, from)));
                });

            await handle.ToValueTask(CancelBehavior.Cancel, true, ct);
        }
    }
}
