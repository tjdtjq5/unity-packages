#if UNITY_EDITOR
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Setup.Steps
{
    /// <summary>Step 0/6 — Welcome splash. 큰 타이틀 + 가운데 [시작하기] 버튼.</summary>
    public sealed class Step0WelcomeStep : ISetupStep
    {
        public string Title => "시작";
        public bool IsCompleted => true;
        public bool IsRequired => false;

        // GUIStyle은 EditorStyles 의존이라 OnDraw 안에서 lazy init.
        static GUIStyle _titleStyle;
        static GUIStyle _subStyle;
        static GUIStyle _buttonStyle;
        static GUIStyle _hintStyle;
        static readonly Color Accent = new(0.20f, 0.65f, 1f);

        static void EnsureStyles()
        {
            if (_titleStyle != null) return;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 36,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            _titleStyle.normal.textColor = Accent;

            _subStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            _subStyle.normal.textColor = new Color(0.78f, 0.78f, 0.78f);

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                fixedHeight = 52,
                fixedWidth = 260,
                alignment = TextAnchor.MiddleCenter,
            };

            _hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            _hintStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
        }

        public void OnEnter(SetupContext ctx) { }

        public void OnDraw(SetupContext ctx)
        {
            EnsureStyles();

            // 위쪽 spacing — 타이틀이 화면 중앙 위쪽에 위치
            GUILayout.Space(80);

            // 큰 타이틀
            GUILayout.Label("Codemagic", _titleStyle);

            GUILayout.Space(10);

            // 부제 (한 줄)
            GUILayout.Label(
                "Unity 프로젝트를 Codemagic에서 그냥 돌아가게.",
                _subStyle);

            GUILayout.Space(60);

            // 가운데 큰 [시작하기 →] 버튼
            EditorUI.BeginCenterRow();
            var prevBg = GUI.backgroundColor;
            GUI.backgroundColor = Accent;
            if (GUILayout.Button("시작하기  →", _buttonStyle))
                ctx.RequestNext();
            GUI.backgroundColor = prevBg;
            EditorUI.EndCenterRow();

            GUILayout.Space(16);

            // 부가 안내 — 예상 소요
            GUILayout.Label("예상 소요  ·  5분", _hintStyle);

            GUILayout.Space(48);

            // 하단 안내 — 시크릿 안전성
            GUILayout.Label(
                "시크릿(토큰 / 비밀번호 / .ulf)은 EditorPrefs에 저장되며 git에 노출되지 않습니다.",
                _hintStyle);
        }

        public void OnLeave(SetupContext ctx) { }
    }
}
#endif
