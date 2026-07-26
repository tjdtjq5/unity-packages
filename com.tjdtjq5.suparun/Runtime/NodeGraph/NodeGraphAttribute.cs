using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 이 string 컬럼이 <see cref="Node{TCtx}"/> 그래프를 담고 있음을 표시한다.
    /// 어드민은 이 컬럼 셀을 텍스트가 아니라 노드 캔버스로 연다.
    ///
    /// ContextType 이 그래프 종류를 가른다 — `Node&lt;SkillCtx&gt;` 파생만 이 컬럼의 팔레트에 뜬다.
    /// 역할(Entry/Action/Flow/Pure)은 상속이 가르므로 두 축이 서로를 방해하지 않는다.
    ///
    /// <code>
    /// [SpecData("InGame")]
    /// public class SkillData
    /// {
    ///     [PrimaryKey] public string id;
    ///     [NodeGraph(typeof(SkillCtx))] public string effect_graph;
    /// }
    /// </code>
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class NodeGraphAttribute : Attribute
    {
        /// <summary>그래프 종류를 가르는 컨텍스트 타입 (`Node&lt;TCtx&gt;` 의 TCtx).</summary>
        public Type ContextType { get; }

        public NodeGraphAttribute(Type contextType) => ContextType = contextType;
    }
}
