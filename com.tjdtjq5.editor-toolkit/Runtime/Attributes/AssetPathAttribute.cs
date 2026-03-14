using System;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// string 필드에 에셋 경로 표시 + 오브젝트 피커로 선택.
    /// [AssetPath(typeof(Sprite))] public string spritePath;
    /// [AssetPath] public string anyPath;
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
    public class AssetPathAttribute : PropertyAttribute
    {
        public Type AssetType { get; }

        public AssetPathAttribute() { AssetType = typeof(UnityEngine.Object); }
        public AssetPathAttribute(Type assetType) { AssetType = assetType; }
    }
}
