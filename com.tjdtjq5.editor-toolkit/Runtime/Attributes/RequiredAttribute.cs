using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// null 또는 빈 값이면 Inspector에 빨간 경고 표시.
    ///
    /// 사용법:
    ///   [Required]
    ///   public GameObject prefab;
    ///
    ///   [Required("프리팹을 지정하세요")]
    ///   public EnemyDataSO data;
    /// </summary>
    public class RequiredAttribute : PropertyAttribute
    {
        public string Message { get; }

        public RequiredAttribute()
        {
            Message = "이 필드는 필수입니다";
        }

        public RequiredAttribute(string message)
        {
            Message = message;
        }
    }
}
