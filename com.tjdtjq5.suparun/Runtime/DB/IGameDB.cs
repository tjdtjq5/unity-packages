using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 게임 DB 인터페이스.
    /// 프로덕션: DapperGameDB (PostgreSQL), 개발: LocalGameDB (메모리).
    /// [Service] 클래스에 생성자 주입으로 제공.
    /// </summary>
    public interface IGameDB
    {
        UniTask<T> Get<T>(object primaryKey, CancellationToken ct = default);
        UniTask<List<T>> GetAll<T>(CancellationToken ct = default);
        UniTask Save<T>(T entity, CancellationToken ct = default);
        UniTask Delete<T>(object primaryKey, CancellationToken ct = default);
        UniTask<List<T>> Query<T>(QueryOptions options, CancellationToken ct = default);
        UniTask<int> Count<T>(QueryOptions options, CancellationToken ct = default);
        UniTask SaveAll<T>(List<T> entities, CancellationToken ct = default);
        UniTask DeleteAll<T>(QueryOptions options, CancellationToken ct = default);
        UniTask Transaction(Func<IGameDB, UniTask> action, CancellationToken ct = default);
    }
}
