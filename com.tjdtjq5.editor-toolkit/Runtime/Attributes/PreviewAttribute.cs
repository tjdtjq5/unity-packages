using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// Object 필드 아래에 썸네일 미리보기를 표시.
    ///
    /// 사용법:
    ///   [Preview]
    ///   public Sprite enemySprite;
    ///
    ///   [Preview(64)]
    ///   public GameObject prefab;
    /// </summary>
    public class PreviewAttribute : PropertyAttribute
    {
        public int Size { get; }

        public PreviewAttribute()
        {
            Size = 48;
        }

        public PreviewAttribute(int size)
        {
            Size = size;
        }
    }
}
