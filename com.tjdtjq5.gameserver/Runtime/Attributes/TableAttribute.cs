using System;

namespace Tjdtjq5.GameServer
{
    /// <summary>이 클래스를 DB 테이블로 생성한다. 클라이언트 읽기 전용.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class TableAttribute : Attribute { }
}
