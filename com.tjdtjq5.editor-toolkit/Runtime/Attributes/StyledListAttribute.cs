using System;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit
{
    /// <summary>
    /// List/Array 필드에 색상 헤더 + 리오더 + 행 삭제 버튼이 있는 스타일 리스트.
    ///
    /// 사용법:
    ///   [StyledList]
    ///   public List&lt;MyData&gt; items;
    ///
    ///   [StyledList("적 풀", 0.9f, 0.4f, 0.4f)]
    ///   public List&lt;EnemyEntry&gt; enemies;
    ///
    ///   [StyledList("클립", pageSize: 10)]
    ///   public List&lt;AnimClip&gt; clips;
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class StyledListAttribute : PropertyAttribute
    {
        public string Title { get; }
        public Color Color { get; }
        public int PageSize { get; }
        public bool ShowIndex { get; }
        public bool Searchable { get; }

        public StyledListAttribute()
        {
            Title = null; Color = Color.white; PageSize = 0; ShowIndex = true; Searchable = false;
        }

        public StyledListAttribute(string title)
        {
            Title = title; Color = Color.white; PageSize = 0; ShowIndex = true; Searchable = false;
        }

        public StyledListAttribute(string title, float r, float g, float b)
        {
            Title = title; Color = new Color(r, g, b); PageSize = 0; ShowIndex = true; Searchable = false;
        }

        public StyledListAttribute(string title, float r, float g, float b, int pageSize)
        {
            Title = title; Color = new Color(r, g, b); PageSize = pageSize; ShowIndex = true; Searchable = false;
        }

        public StyledListAttribute(string title, float r, float g, float b, bool searchable)
        {
            Title = title; Color = new Color(r, g, b); PageSize = 0; ShowIndex = true; Searchable = searchable;
        }

        public StyledListAttribute(string title, float r, float g, float b, int pageSize, bool searchable)
        {
            Title = title; Color = new Color(r, g, b); PageSize = pageSize; ShowIndex = true; Searchable = searchable;
        }
    }
}
