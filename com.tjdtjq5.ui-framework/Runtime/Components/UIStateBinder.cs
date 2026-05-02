using System;
using System.Collections.Generic;
using LitMotion;
using LitMotion.Extensions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// string 키 기반 상태 UI 바인딩.
    /// 상태 전환 시 Exclusive 오브젝트 자동 전환, 오브젝트 활성화/비활성화,
    /// 다중 Animator 파라미터, 다중 Visual(Graphic) 타겟, Tween 전환,
    /// 이벤트(onEnter/onExit) 호출.
    /// </summary>
    public class UIStateBinder : MonoBehaviour
    {
        [SerializeField] private string _initialState;
        [SerializeField] private GameObject[] _exclusivePool;
        [SerializeField] private StateBinding[] _bindings;

        private Dictionary<string, StateBinding> _bindingMap;
        private string _currentState;
        private readonly List<MotionHandle> _activeHandles = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { }

        private void Awake()
        {
            // Domain Reload 비활성화 시 인스턴스 상태 리셋
            _bindingMap = null;
            _currentState = null;
            _activeHandles.Clear();
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_bindingMap != null) return;

            _bindingMap = new Dictionary<string, StateBinding>();
            if (_bindings == null) return;

            foreach (var binding in _bindings)
            {
                if (!string.IsNullOrEmpty(binding.stateName))
                    _bindingMap[binding.stateName] = binding;
            }
        }

        private void Start()
        {
            if (!string.IsNullOrEmpty(_initialState))
                SetState(_initialState);
        }

        /// <summary>
        /// enum 기반 상태 전환 편의 메서드. Enum.ToString() boxing 회피를 위해 캐시 사용.
        /// </summary>
        public void SetState<TEnum>(TEnum state) where TEnum : Enum
        {
            SetState(EnumNameCache<TEnum>.GetName(state));
        }

        /// <summary>
        /// string 키 기반 상태 전환.
        /// </summary>
        public void SetState(string stateName)
        {
            EnsureInitialized();
            if (stateName == _currentState) return;

            if (!_bindingMap.TryGetValue(stateName, out var binding))
            {
                Debug.LogWarning($"[UIStateBinder] '{stateName}' 상태를 찾을 수 없습니다: {gameObject.name}", this);
                return;
            }

            // onExit (이전 상태)
            if (_currentState != null && _bindingMap.TryGetValue(_currentState, out var prev))
            {
                if ((prev.features & BindingFeatures.Event) != 0)
                    prev.onExit?.Invoke();
            }

            _currentState = stateName;
            KillActiveHandles();
            ApplyBinding(binding);
        }

        public string CurrentState => _currentState;

        /// <summary>
        /// Exclusive Pool에 등록된 오브젝트 목록 (Editor에서 참조).
        /// </summary>
        public GameObject[] ExclusivePool => _exclusivePool;

        private void ApplyBinding(StateBinding binding)
        {
            var f = binding.features;
            bool useTween = (f & BindingFeatures.Tween) != 0 && binding.tweenConfig != null;

            if ((f & BindingFeatures.Exclusive) != 0) ApplyExclusive(binding);
            if ((f & BindingFeatures.Objects) != 0) ApplyObjects(binding);
            if ((f & BindingFeatures.Animator) != 0) ApplyAnimator(binding);
            if ((f & BindingFeatures.Visual) != 0) ApplyVisual(binding, useTween);
            if (useTween && binding.targetScale != Vector3.one) ApplyScaleTween(binding);
            if ((f & BindingFeatures.Event) != 0) ApplyEvent(binding, useTween);
        }

        private void ApplyExclusive(StateBinding binding)
        {
            // pool 전체 끄고 → exclusiveShow만 켬 (항상 즉시)
            if (_exclusivePool == null) return;

            foreach (var go in _exclusivePool)
                if (go != null) go.SetActive(false);

            if (binding.exclusiveShow != null)
                foreach (var go in binding.exclusiveShow)
                    if (go != null) go.SetActive(true);
        }

        private static void ApplyObjects(StateBinding binding)
        {
            if (binding.activateObjects != null)
                foreach (var go in binding.activateObjects)
                    if (go != null) go.SetActive(true);

            if (binding.deactivateObjects != null)
                foreach (var go in binding.deactivateObjects)
                    if (go != null) go.SetActive(false);
        }

        private static void ApplyAnimator(StateBinding binding)
        {
            if (binding.animatorTargets is { Length: > 0 })
            {
                foreach (var at in binding.animatorTargets)
                    SetAnimatorParameter(at);
                return;
            }

            if (binding.animator != null && !string.IsNullOrEmpty(binding.animatorTrigger))
                binding.animator.SetTrigger(binding.animatorTrigger);
        }

        private static void SetAnimatorParameter(AnimatorTarget at)
        {
            if (at.animator == null || string.IsNullOrEmpty(at.parameterName)) return;

            switch (at.paramType)
            {
                case AnimatorParamType.Trigger:
                    at.animator.SetTrigger(at.parameterName);
                    break;
                case AnimatorParamType.Bool:
                    at.animator.SetBool(at.parameterName, at.floatValue > 0.5f);
                    break;
                case AnimatorParamType.Int:
                    at.animator.SetInteger(at.parameterName, (int)at.floatValue);
                    break;
                case AnimatorParamType.Float:
                    at.animator.SetFloat(at.parameterName, at.floatValue);
                    break;
            }
        }

        private void ApplyVisual(StateBinding binding, bool useTween)
        {
            if (useTween) ApplyVisualTween(binding);
            else ApplyVisualImmediate(binding);
        }

        private void ApplyScaleTween(StateBinding binding)
        {
            var cfg = binding.tweenConfig;
            var handle = LMotion.Create(transform.localScale, binding.targetScale, cfg.duration)
                .WithEase(cfg.ease)
                .WithDelay(cfg.delay)
                .WithScheduler(GetScheduler(cfg.useUnscaledTime))
                .BindToLocalScale(transform);
            _activeHandles.Add(handle);
        }

        // Tween 시: 완료 시점에 onEnter 호출 (모든 트윈이 동일 cfg 사용 가정).
        // 즉시 모드: 바로 호출.
        private void ApplyEvent(StateBinding binding, bool useTween)
        {
            if (!useTween || _activeHandles.Count == 0)
            {
                binding.onEnter?.Invoke();
                return;
            }

            var cfg = binding.tweenConfig;
            var callbackHandle = LMotion.Create(0f, 1f, cfg.duration)
                .WithDelay(cfg.delay)
                .WithScheduler(GetScheduler(cfg.useUnscaledTime))
                .WithOnComplete(() => binding.onEnter?.Invoke())
                .Bind(_ => { });
            _activeHandles.Add(callbackHandle);
        }

        private void ApplyVisualImmediate(StateBinding binding)
        {
            if (binding.visualTargets is { Length: > 0 })
            {
                foreach (var vt in binding.visualTargets)
                {
                    if (vt.target == null) continue;

                    var c = vt.color;
                    if (vt.alpha >= 0f) c.a = vt.alpha;
                    vt.target.color = c;

                    if (vt.sprite != null && vt.target is Image img)
                        img.sprite = vt.sprite;
                }
            }
            else if (binding.targetImage != null)
            {
                binding.targetImage.color = binding.imageColor;
            }
        }

        private void ApplyVisualTween(StateBinding binding)
        {
            var cfg = binding.tweenConfig;
            var scheduler = GetScheduler(cfg.useUnscaledTime);

            if (binding.visualTargets is { Length: > 0 })
            {
                foreach (var vt in binding.visualTargets)
                {
                    if (vt.target == null) continue;

                    // Sprite는 즉시 교체 (Tween 불가)
                    if (vt.sprite != null && vt.target is Image img)
                        img.sprite = vt.sprite;

                    var c = vt.color;
                    if (vt.alpha >= 0f) c.a = vt.alpha;

                    var handle = LMotion.Create(vt.target.color, c, cfg.duration)
                        .WithEase(cfg.ease)
                        .WithDelay(cfg.delay)
                        .WithScheduler(scheduler)
                        .BindToColor(vt.target);
                    _activeHandles.Add(handle);
                }
            }
            else if (binding.targetImage != null)
            {
                var handle = LMotion.Create(binding.targetImage.color, binding.imageColor, cfg.duration)
                    .WithEase(cfg.ease)
                    .WithDelay(cfg.delay)
                    .WithScheduler(scheduler)
                    .BindToColor(binding.targetImage);
                _activeHandles.Add(handle);
            }
        }

        private static IMotionScheduler GetScheduler(bool useUnscaledTime)
        {
            return useUnscaledTime
                ? MotionScheduler.UpdateIgnoreTimeScale
                : MotionScheduler.Update;
        }

        private void KillActiveHandles()
        {
            foreach (var handle in _activeHandles)
                handle.TryCancel();
            _activeHandles.Clear();
        }

        private void OnDisable()
        {
            KillActiveHandles();
        }

        // ─── Enums ─────────────────────────────────────────

        [Flags]
        public enum BindingFeatures
        {
            Objects   = 1 << 0,
            Animator  = 1 << 1,
            Visual    = 1 << 2,
            Event     = 1 << 3,
            Exclusive = 1 << 4,
            Tween     = 1 << 5,
        }

        public enum AnimatorParamType
        {
            Trigger,
            Bool,
            Int,
            Float,
        }

        // ─── Serializable Classes ──────────────────────────

        [Serializable]
        public class TweenConfig
        {
            public float duration = 0.2f;
            public Ease ease = Ease.OutQuad;
            public float delay;
            public bool useUnscaledTime = true;
        }

        [Serializable]
        public class VisualTarget
        {
            public Graphic target;
            public Color color = Color.white;
            public Sprite sprite;
            public float alpha = -1f;
        }

        [Serializable]
        public class AnimatorTarget
        {
            public Animator animator;
            public string parameterName;
            public AnimatorParamType paramType = AnimatorParamType.Trigger;
            public float floatValue;
        }

        /// <summary>
        /// Enum.ToString() GC alloc 회피용 캐시.
        /// </summary>
        private static class EnumNameCache<TEnum> where TEnum : Enum
        {
            private static readonly Dictionary<TEnum, string> Cache = new();

            public static string GetName(TEnum value)
            {
                if (!Cache.TryGetValue(value, out var name))
                {
                    name = value.ToString();
                    Cache[value] = name;
                }
                return name;
            }
        }

        [Serializable]
        public class StateBinding
        {
            public string stateName;
            public BindingFeatures features = BindingFeatures.Objects;

            // Exclusive
            public GameObject[] exclusiveShow;

            // Objects (기존 호환)
            public GameObject[] activateObjects;
            public GameObject[] deactivateObjects;

            // Animator — 새 배열 우선, 비어있으면 레거시 사용
            public AnimatorTarget[] animatorTargets;
            public Animator animator;
            public string animatorTrigger;

            // Visual — 새 배열 우선, 비어있으면 레거시 사용
            public VisualTarget[] visualTargets;
            public Image targetImage;
            public Color imageColor = Color.white;

            // Tween
            public TweenConfig tweenConfig;
            public Vector3 targetScale = Vector3.one;

            // Event
            public UnityEvent onEnter;
            public UnityEvent onExit;
        }
    }
}
