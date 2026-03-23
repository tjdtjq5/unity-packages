using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tjdtjq5.GameServer
{
    /// <summary>
    /// 게임 DB 인터페이스.
    /// 프로덕션: DapperGameDB (PostgreSQL), 개발: LocalGameDB (메모리).
    /// [Service] 클래스에 생성자 주입으로 제공.
    /// </summary>
    public interface IGameDB
    {
        Task<T> Get<T>(object primaryKey);
        Task<List<T>> GetAll<T>();
        Task Save<T>(T entity);
        Task Delete<T>(object primaryKey);
        Task<List<T>> Query<T>(QueryOptions options);
        Task Transaction(Func<IGameDB, Task> action);
    }
}
