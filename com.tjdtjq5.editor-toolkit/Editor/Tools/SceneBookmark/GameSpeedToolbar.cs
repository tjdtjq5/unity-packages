using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Tjdtjq5.EditorToolkit.Editor.Tools
{
    /// <summary>
    /// 메인 툴바 Play 버튼 오른쪽에 게임 속도 슬라이더를 삽입한다.
    /// Play 모드에서만 활성, 더블클릭으로 1x 리셋, 마우스 휠로 미세 조절.
    /// </summary>
    [InitializeOnLoad]
    static class GameSpeedToolbar
    {
        const string ContainerId = "GameSpeedContainer";
        const int MaxPollFrames = 100;
        const float MinSpeed = 0.1f;
        const float MaxSpeed = 5f;
        const float DefaultSpeed = 1f;
        const float WheelStep = 0.1f;

        // 색상
        static readonly Color BgColor = new(0.18f, 0.18f, 0.18f, 1f);
        static readonly Color SliderTrackColor = new(0.30f, 0.30f, 0.30f, 1f);
        static readonly Color SliderFillColor = new(0.35f, 0.6f, 0.85f, 0.8f);
        static readonly Color TextColor = new(0.78f, 0.78f, 0.78f, 1f);
        static readonly Color DisabledTextColor = new(0.45f, 0.45f, 0.45f, 1f);

        static int _pollCount;
        static bool _injected;
        static Label _speedLabel;
        static Slider _slider;
        static VisualElement _container;

        static GameSpeedToolbar()
        {
            _injected = false;
            _pollCount = 0;

            EditorApplication.delayCall += TryInject;
            EditorApplication.update += PollInject;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        // --- 주입 ---

        static void PollInject()
        {
            if (_injected || _pollCount >= MaxPollFrames)
            {
                EditorApplication.update -= PollInject;
                return;
            }
            _pollCount++;
            TryInject();
        }

        static void TryInject()
        {
            if (_injected) return;

            var toolbarRoot = ToolbarHelper.GetToolbarRoot();
            if (toolbarRoot == null) return;

            var existing = toolbarRoot.Q(ContainerId);
            existing?.RemoveFromHierarchy();

            var playZone = ToolbarHelper.FindPlayZone(toolbarRoot);
            if (playZone == null) return;

            _container = BuildUI();
            var parent = playZone.parent;
            var playIndex = parent.IndexOf(playZone);
            // Play 영역 오른쪽에 삽입
            parent.Insert(playIndex + 1, _container);

            UpdateVisibility();

            _injected = true;
            EditorApplication.update -= PollInject;
        }

        // --- UI 빌드 ---

        static VisualElement BuildUI()
        {
            var container = new VisualElement
            {
                name = ContainerId,
                style =
                {
                    flexDirection = FlexDirection.Row,
                    alignItems = Align.Center,
                    marginLeft = 6,
                    marginRight = 2,
                    paddingLeft = 4,
                    paddingRight = 4,
                    paddingTop = 2,
                    paddingBottom = 2,
                    backgroundColor = BgColor,
                    borderTopLeftRadius = 3,
                    borderTopRightRadius = 3,
                    borderBottomLeftRadius = 3,
                    borderBottomRightRadius = 3,
                }
            };

            // 느림 아이콘
            var slowLabel = new Label("\u00bb")
            {
                tooltip = "게임 속도 (느림)",
                style =
                {
                    color = TextColor,
                    fontSize = 10,
                    marginRight = 2,
                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };
            slowLabel.pickingMode = PickingMode.Ignore;
            container.Add(slowLabel);

            // 슬라이더
            _slider = new Slider(MinSpeed, MaxSpeed)
            {
                value = DefaultSpeed,
                style =
                {
                    width = 80,
                    marginLeft = 0,
                    marginRight = 0,
                }
            };

            // 슬라이더 트랙/드래거 스타일링
            _slider.RegisterCallback<GeometryChangedEvent>(_ => StyleSlider(_slider));

            _slider.RegisterValueChangedCallback(evt =>
            {
                if (EditorApplication.isPlaying)
                    Time.timeScale = evt.newValue;
                UpdateSpeedLabel();
            });

            container.Add(_slider);

            // 빠름 아이콘
            var fastLabel = new Label("\u00bb\u00bb")
            {
                tooltip = "게임 속도 (빠름)",
                style =
                {
                    color = TextColor,
                    fontSize = 10,
                    marginLeft = 2,
                    unityTextAlign = TextAnchor.MiddleCenter,
                }
            };
            fastLabel.pickingMode = PickingMode.Ignore;
            container.Add(fastLabel);

            // 속도 텍스트
            _speedLabel = new Label("x1.0")
            {
                style =
                {
                    color = TextColor,
                    fontSize = 11,
                    unityFontStyleAndWeight = FontStyle.Bold,
                    unityTextAlign = TextAnchor.MiddleCenter,
                    width = 32,
                    marginLeft = 2,
                }
            };
            container.Add(_speedLabel);

            // 마우스 휠로 미세 조절
            container.RegisterCallback<WheelEvent>(evt =>
            {
                if (!EditorApplication.isPlaying) return;
                float delta = evt.delta.y > 0 ? -WheelStep : WheelStep;
                _slider.value = Mathf.Clamp(_slider.value + delta, MinSpeed, MaxSpeed);
                evt.StopPropagation();
            });

            // 더블클릭으로 1x 리셋
            container.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.clickCount == 2 && EditorApplication.isPlaying)
                {
                    _slider.value = DefaultSpeed;
                    evt.StopPropagation();
                }
            });

            return container;
        }

        static void StyleSlider(Slider slider)
        {
            // 트랙
            var tracker = slider.Q("unity-tracker");
            if (tracker != null)
            {
                tracker.style.backgroundColor = SliderTrackColor;
                tracker.style.borderTopLeftRadius = 2;
                tracker.style.borderTopRightRadius = 2;
                tracker.style.borderBottomLeftRadius = 2;
                tracker.style.borderBottomRightRadius = 2;
            }

            // 드래거 (채움)
            var dragger = slider.Q("unity-dragger");
            if (dragger != null)
            {
                dragger.style.backgroundColor = SliderFillColor;
                dragger.style.borderTopLeftRadius = 2;
                dragger.style.borderTopRightRadius = 2;
                dragger.style.borderBottomLeftRadius = 2;
                dragger.style.borderBottomRightRadius = 2;
            }
        }

        // --- 외부 API (단축키 연동) ---

        /// <summary>속도를 절대값으로 설정하고 슬라이더/TimeScale 동기화.</summary>
        public static void SetSpeed(float speed)
        {
            speed = Mathf.Clamp(speed, MinSpeed, MaxSpeed);
            if (_slider != null) _slider.value = speed;
            if (EditorApplication.isPlaying) Time.timeScale = speed;
            UpdateSpeedLabel();
        }

        /// <summary>현재 속도에 delta를 더한다 (음수로 감속).</summary>
        public static void AdjustSpeed(float delta)
        {
            float current = _slider?.value ?? DefaultSpeed;
            SetSpeed(current + delta);
        }

        // --- 상태 관리 ---

        static void UpdateSpeedLabel()
        {
            if (_speedLabel == null) return;
            _speedLabel.text = $"x{_slider.value:F1}";
        }

        static void UpdateVisibility()
        {
            if (_container == null) return;

            bool playing = EditorApplication.isPlaying;
            _container.SetEnabled(playing);
            _container.style.opacity = playing ? 1f : 0.4f;

            if (_speedLabel != null)
                _speedLabel.style.color = playing ? TextColor : DisabledTextColor;
        }

        // --- 이벤트 ---

        static void OnPlayModeChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    // Play 진입 시 슬라이더 값 적용
                    Time.timeScale = _slider?.value ?? DefaultSpeed;
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    // Play 종료 시 TimeScale 복원
                    Time.timeScale = DefaultSpeed;
                    break;
            }

            // 약간의 딜레이 후 UI 갱신 (Play 모드 전환 완료 대기)
            EditorApplication.delayCall += UpdateVisibility;
        }
    }
}
