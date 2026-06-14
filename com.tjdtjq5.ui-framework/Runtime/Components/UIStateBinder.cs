using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using LitMotion;
using LitMotion.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Tjdtjq5.UIFramework
{
    /// <summary>
    /// string 키 기반 상태 UI 바인딩.
    /// 상태 전환 시 현재 상태의 Exit 바인딩 → 새 상태의 Enter 바인딩 순서로 적용.
    /// </summary>
    public class UIStateBinder : MonoBehaviour
    {
        [SerializeField]
        private StateBinding[] _bindings;

        private Dictionary<string, StateBinding> _bindingMap;
        private string _currentState;
        private bool _animatorReady;
        private string _pendingAnimatorState;

        // LitMotion scale 트윈 핸들 (상태 전환 시 이전 트윈 취소용)
        private readonly List<MotionHandle> _activeHandles = new();

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_bindingMap != null)
            {
                return;
            }

            _bindingMap = new Dictionary<string, StateBinding>();

            if (_bindings == null)
            {
                return;
            }

            foreach (StateBinding binding in _bindings)
            {
                if (!string.IsNullOrEmpty(binding.stateName))
                {
                    _bindingMap[binding.stateName] = binding;
                }
            }
        }

        private void Start()
        {
            // Animator는 첫 Update() 이후 내부 StateMachine이 초기화되므로
            // 1프레임 지연 후 준비 완료 처리
            DelayedAnimatorReadyAsync(this.destroyCancellationToken).Forget();
        }

        private async UniTask DelayedAnimatorReadyAsync(System.Threading.CancellationToken ct)
        {
            await UniTask.Yield(cancellationToken: ct); // 1프레임 대기 (Animator 초기화 완료)
            _animatorReady = true;

            if (_currentState == null)
            {
                SetDefaultState();
            }
            else if (_pendingAnimatorState != null)
            {
                // Animator 준비 전에 SetState가 호출된 경우 Animator 바인딩만 재적용
                if (_bindingMap.TryGetValue(_pendingAnimatorState, out StateBinding binding))
                    ApplyAnimatorBindings(binding.animatorBindings);
                _pendingAnimatorState = null;
            }
        }

        /// <summary>
        /// enum 기반 상태 전환 편의 메서드.
        /// </summary>
        public void SetState<TEnum>(TEnum state) where TEnum : Enum
        {
            SetState(state.ToString());
        }

        /// <summary>
        /// string 키 기반 상태 전환.
        /// 현재 상태 Exit → 새 상태 Enter.
        /// </summary>
        public void SetState(string stateName)
        {
            EnsureInitialized();
            if (stateName == _currentState)
            {
                return;
            }

            if (!_bindingMap.TryGetValue(stateName, out StateBinding binding))
            {
                // 미등록 상태 요청 시 현재 상태를 Exit하여 이전 시각 잔존 방지 (풀링 재사용 안전)
                if (_currentState != null)
                {
                    ApplyExitBinding();
                    _currentState = null;
                }

                Debug.LogWarning($"[UIStateBinder] '{stateName}' not found on {gameObject.name}. " +
                                 $"Available: [{string.Join(", ", _bindingMap.Keys)}]");
                return;
            }

            // 첫 상태 전환 시 모든 Exit 바인딩 합산 적용 (Default 상태 초기화)
            if (_currentState == null)
                ApplyAllExitBindings();
            else
                ApplyExitBinding();

            _currentState = stateName;
            // 새 상태의 Enter 바인딩 적용
            ApplyEnterBinding(binding);
        }

        public string CurrentState => _currentState;

        /// <summary>
        /// 현재 상태를 해제하고 Initial State로 복원.
        /// </summary>
        public void ResetToInitial()
        {
            EnsureInitialized();
            if (_bindings == null || _bindings.Length == 0) return;
            ApplyExitBinding();
            _currentState = _bindings[0].stateName;
            ApplyEnterBinding(_bindings[0]);
        }

        // ═══════════════════════════════════════════════════════════════
        // Exit 바인딩 — 현재 상태를 빠져나갈 때
        // ═══════════════════════════════════════════════════════════════

        private void ApplyExitBinding()
        {
            if (string.IsNullOrEmpty(_currentState)) return;
            if (!_bindingMap.TryGetValue(_currentState, out StateBinding current)) return;
            ApplyExitBindingFor(current);
        }

        private void ApplyExitBindingFor(StateBinding binding)
        {
            BindingFeatures f = binding.features;

            if ((f & BindingFeatures.Objects) != 0)
            {
                ApplyGameObjects(binding.exitActivateObjects, true);
                ApplyGameObjects(binding.exitDeactivateObjects, false);
            }

            if ((f & BindingFeatures.Visual) != 0)
                ApplyVisualBindings(binding.exitVisualBindings);

            if ((f & BindingFeatures.Text) != 0)
                ApplyTextBindings(binding.exitTextBindings);

            if ((f & BindingFeatures.Sprite) != 0)
                ApplySpriteBindings(binding.exitSpriteBindings);

            if ((f & BindingFeatures.Alpha) != 0)
                ApplyAlphaBindings(binding.exitAlphaBindings);

            if ((f & BindingFeatures.Animator) != 0)
            {
                if (binding.exitAnimatorBindings != null && binding.exitAnimatorBindings.Length > 0)
                    ApplyAnimatorBindings(binding.exitAnimatorBindings);
                else
                    RevertAnimatorBindings(binding.animatorBindings);
            }

            if ((f & BindingFeatures.Event) != 0)
                FireEvents(binding.onExitEvents);

            if ((f & BindingFeatures.Tween) != 0)
                ApplyScaleTween(binding.exitTargetScale, binding.tweenConfig);
        }

        /// <summary>
        /// 모든 상태의 Exit 바인딩을 합산 적용하여 Default 상태로 전환.
        /// 어떤 상태도 활성화되지 않은 초기 상태를 만든다.
        /// </summary>
        public void SetDefaultState()
        {
            EnsureInitialized();
            _currentState = null;
            ApplyAllExitBindings();
        }

        private void ApplyAllExitBindings()
        {
            if (_bindings == null) return;
            foreach (StateBinding binding in _bindings)
            {
                if (binding != null)
                    ApplyExitBindingFor(binding);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Enter 바인딩 — 새 상태에 진입할 때
        // ═══════════════════════════════════════════════════════════════

        private void ApplyEnterBinding(StateBinding binding)
        {
            BindingFeatures f = binding.features;

            // Objects Enter
            if ((f & BindingFeatures.Objects) != 0)
            {
                ApplyGameObjects(binding.activateObjects, true);
                ApplyGameObjects(binding.deactivateObjects, false);
            }

            // Visual Enter
            if ((f & BindingFeatures.Visual) != 0)
            {
                if (binding.visualBindings != null && binding.visualBindings.Length > 0)
                {
                    ApplyVisualBindings(binding.visualBindings);
                }
                else if (binding.targetImage != null)
                {
                    binding.targetImage.color = binding.imageColor;
                }
            }

            // Text Enter
            if ((f & BindingFeatures.Text) != 0)
            {
                ApplyTextBindings(binding.textBindings);
            }

            // Sprite Enter
            if ((f & BindingFeatures.Sprite) != 0)
            {
                ApplySpriteBindings(binding.spriteBindings);
            }

            // Alpha Enter
            if ((f & BindingFeatures.Alpha) != 0)
            {
                ApplyAlphaBindings(binding.alphaBindings);
            }

            // Animator Enter
            if ((f & BindingFeatures.Animator) != 0)
            {
                if (_animatorReady)
                {
                    ApplyAnimatorBindings(binding.animatorBindings);
                }
                else
                {
                    // Animator 미초기화 상태 — 준비 완료 후 재적용
                    _pendingAnimatorState = binding.stateName;
                }
            }

            // Event Enter
            if ((f & BindingFeatures.Event) != 0)
            {
                if (binding.onEnterEvents != null && binding.onEnterEvents.Length > 0)
                {
                    FireEvents(binding.onEnterEvents);
                }
                else
                {
                    binding.onEnter?.Invoke();
                }
            }

            // Tween Enter (LitMotion scale)
            if ((f & BindingFeatures.Tween) != 0)
            {
                ApplyScaleTween(binding.targetScale, binding.tweenConfig);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 공통 적용 헬퍼
        // ═══════════════════════════════════════════════════════════════

        private static void ApplyGameObjects(GameObject[] objects, bool active)
        {
            if (objects == null) return;
            foreach (GameObject go in objects)
            {
                if (go != null)
                    go.SetActive(active);
            }
        }

        private static void ApplyVisualBindings(VisualBinding[] bindings)
        {
            if (bindings == null) return;
            foreach (VisualBinding vb in bindings)
            {
                if (vb != null && vb.target != null)
                    vb.target.color = vb.color;
            }
        }

        private static void ApplyTextBindings(TextBinding[] bindings)
        {
            if (bindings == null) return;
            foreach (TextBinding tb in bindings)
            {
                if (tb != null && tb.target != null)
                    tb.target.text = tb.text;
            }
        }

        private static void ApplySpriteBindings(SpriteBinding[] bindings)
        {
            if (bindings == null) return;
            foreach (SpriteBinding sb in bindings)
            {
                if (sb != null && sb.target != null)
                    sb.target.sprite = sb.sprite;
            }
        }

        private static void ApplyAlphaBindings(AlphaBinding[] bindings)
        {
            if (bindings == null) return;
            foreach (AlphaBinding ab in bindings)
            {
                if (ab != null && ab.target != null)
                    ab.target.alpha = ab.alpha;
            }
        }

        private static void ApplyAnimatorBindings(AnimatorBinding[] bindings)
        {
            if (bindings == null) return;
            foreach (AnimatorBinding ab in bindings)
            {
                if (ab == null || ab.animator == null) continue;

                // Legacy: trigger 필드에 값이 있고 paramName이 비었으면 마이그레이션
                string name = !string.IsNullOrEmpty(ab.paramName) ? ab.paramName : ab.trigger;
                if (string.IsNullOrEmpty(name)) continue;

                switch (ab.paramType)
                {
                    case AnimatorParamType.Trigger:
                        ab.animator.SetTrigger(name);
                        break;
                    case AnimatorParamType.Bool:
                        ab.animator.SetBool(name, ab.boolValue);
                        break;
                    case AnimatorParamType.Play:
                        ab.animator.Play(name, -1, 0f);
                        ab.animator.Update(0f);
                        break;
                }
            }
        }

        /// <summary>
        /// Enter 바인딩의 역방향 적용: Bool은 반전, Trigger는 ResetTrigger, Play는 무시.
        /// exitAnimatorBindings가 비었을 때 자동 Exit 용도.
        /// </summary>
        private static void RevertAnimatorBindings(AnimatorBinding[] bindings)
        {
            if (bindings == null) return;
            foreach (AnimatorBinding ab in bindings)
            {
                if (ab == null || ab.animator == null) continue;

                string name = !string.IsNullOrEmpty(ab.paramName) ? ab.paramName : ab.trigger;
                if (string.IsNullOrEmpty(name)) continue;

                switch (ab.paramType)
                {
                    case AnimatorParamType.Trigger:
                        ab.animator.ResetTrigger(name);
                        break;
                    case AnimatorParamType.Bool:
                        ab.animator.SetBool(name, !ab.boolValue);
                        break;
                    // Play는 역방향 없음 — exitAnimatorBindings에서 별도 Play로 처리
                }
            }
        }

        private static void FireEvents(UnityEvent[] events)
        {
            if (events == null) return;
            foreach (UnityEvent evt in events)
            {
                evt?.Invoke();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Tween (LitMotion scale)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// localScale을 targetScale로 LitMotion 트윈. 진행 중이던 트윈은 취소 후 재시작.
        /// </summary>
        private void ApplyScaleTween(Vector3 targetScale, TweenConfig cfg)
        {
            if (cfg == null) return;

            KillActiveHandles();

            MotionHandle handle = LMotion.Create(transform.localScale, targetScale, cfg.duration)
                .WithEase(cfg.ease)
                .WithDelay(cfg.delay)
                .WithScheduler(cfg.useUnscaledTime
                    ? MotionScheduler.UpdateIgnoreTimeScale
                    : MotionScheduler.Update)
                .BindToLocalScale(transform);
            _activeHandles.Add(handle);
        }

        private void KillActiveHandles()
        {
            foreach (MotionHandle handle in _activeHandles)
                handle.TryCancel();
            _activeHandles.Clear();
        }

        private void OnDisable()
        {
            KillActiveHandles();
        }

        // ═══════════════════════════════════════════════════════════════
        // 타입 정의
        // ═══════════════════════════════════════════════════════════════

        [Flags]
        public enum BindingFeatures
        {
            Objects = 1 << 0,
            Animator = 1 << 1,
            Visual = 1 << 2,
            Event = 1 << 3,
            Text = 1 << 4,
            Sprite = 1 << 5,
            Alpha = 1 << 6,
            Tween = 1 << 7,
        }

        public enum AnimatorParamType
        {
            Trigger,
            Bool,
            Play,
        }

        [Serializable]
        public class AnimatorBinding
        {
            public Animator animator;
            public string paramName;
            public AnimatorParamType paramType = AnimatorParamType.Trigger;
            public bool boolValue = true;

            // Legacy
            [HideInInspector]
            [FormerlySerializedAs("trigger")]
            public string trigger;
        }

        [Serializable]
        public class VisualBinding
        {
            [FormerlySerializedAs("targetImage")]
            public Graphic target;

            public Color color = Color.white;
        }

        [Serializable]
        public class TextBinding
        {
            public TMP_Text target;
            public string text;
        }

        [Serializable]
        public class SpriteBinding
        {
            public Image target;
            public Sprite sprite;
        }

        [Serializable]
        public class AlphaBinding
        {
            public CanvasGroup target;
            [Range(0f, 1f)]
            public float alpha = 1f;
        }

        /// <summary>
        /// LitMotion scale 트윈 설정.
        /// </summary>
        [Serializable]
        public class TweenConfig
        {
            public float duration = 0.2f;
            public Ease ease = Ease.OutQuad;
            public float delay;
            public bool useUnscaledTime = true;
        }

        [Serializable]
        public class StateBinding
        {
            public string stateName;
            public BindingFeatures features;

            // ── Objects ──
            public GameObject[] activateObjects;
            public GameObject[] deactivateObjects;
            public GameObject[] exitActivateObjects;
            public GameObject[] exitDeactivateObjects;

            // ── Animator ──
            public AnimatorBinding[] animatorBindings;
            public AnimatorBinding[] exitAnimatorBindings;

            // ── Visual ──
            public VisualBinding[] visualBindings;
            public VisualBinding[] exitVisualBindings;

            // ── Event ──
            public UnityEvent[] onEnterEvents;
            public UnityEvent[] onExitEvents;

            // ── Text ──
            public TextBinding[] textBindings;
            public TextBinding[] exitTextBindings;

            // ── Sprite ──
            public SpriteBinding[] spriteBindings;
            public SpriteBinding[] exitSpriteBindings;

            // ── Alpha ──
            public AlphaBinding[] alphaBindings;
            public AlphaBinding[] exitAlphaBindings;

            // ── Tween (LitMotion scale) ──
            public TweenConfig tweenConfig;
            public Vector3 targetScale = Vector3.one;
            public Vector3 exitTargetScale = Vector3.one;

            // Legacy 단일 필드 (하위 호환 — 마이그레이션 후 미사용)
            [HideInInspector]
            public Animator animator;

            [HideInInspector]
            public string animatorTrigger;

            [HideInInspector]
            public Image targetImage;

            [HideInInspector]
            public Color imageColor = Color.white;

            [HideInInspector]
            public UnityEvent onEnter;
        }
    }
}
