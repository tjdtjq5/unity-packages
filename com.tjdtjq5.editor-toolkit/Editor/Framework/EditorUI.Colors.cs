#if UNITY_EDITOR
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor
{
    /// <summary>에디터 UI 유틸리티. 색상 상수.</summary>
    public static partial class EditorUI
    {
        // ── 배경 ──
        public static readonly Color BG_WINDOW  = new(0.18f, 0.18f, 0.20f);
        public static readonly Color BG_SECTION = new(0.14f, 0.14f, 0.16f);
        public static readonly Color BG_CARD    = new(0.12f, 0.12f, 0.14f);
        public static readonly Color BG_HEADER  = new(0.11f, 0.11f, 0.13f);

        // ── 시맨틱 ──
        public static readonly Color COL_SUCCESS = new(0.30f, 0.80f, 0.40f);
        public static readonly Color COL_WARN    = new(0.95f, 0.75f, 0.20f);
        public static readonly Color COL_ERROR   = new(0.95f, 0.30f, 0.30f);
        public static readonly Color COL_INFO    = new(0.40f, 0.70f, 0.95f);
        public static readonly Color COL_MUTED   = new(0.45f, 0.45f, 0.50f);
        public static readonly Color COL_LINK    = new(0.35f, 0.65f, 0.95f);

        // ── 알림 타입 ──
        public enum NotificationType { Error, Success, Info }
    }
}
#endif
