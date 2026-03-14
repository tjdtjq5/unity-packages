using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    public enum HelpBoxType { Info, Warning, Error }

    /// <summary>
    /// 필드 위에 Info/Warning/Error 메시지 박스를 표시.
    ///
    /// 사용법:
    ///   [HelpBox("0보다 커야 합니다", HelpBoxType.Warning)]
    ///   public float speed;
    ///
    ///   [HelpBox("실험적 기능")]
    ///   public bool experimental;
    /// </summary>
    public class HelpBoxAttribute : PropertyAttribute
    {
        public string Message { get; }
        public HelpBoxType Type { get; }

        public HelpBoxAttribute(string message)
        {
            Message = message;
            Type = HelpBoxType.Info;
        }

        public HelpBoxAttribute(string message, HelpBoxType type)
        {
            Message = message;
            Type = type;
        }
    }
}
