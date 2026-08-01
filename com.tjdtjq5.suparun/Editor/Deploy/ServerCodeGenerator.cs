using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Tjdtjq5.SupaRun.Editor
{
    public static class ServerCodeGenerator
    {
        /// <summary>
        /// 컴파일 직후 반영할 SQL 만 생성한다 (ADR-0004 — SchemaAutoSync 용).
        ///
        /// **[SpecData] 만 다룬다.** [UserData] 는 게임이 Cloud Run 을 거쳐 읽고 쓰므로,
        /// DB 컬럼만 먼저 만들어 봐야 서버는 그 필드를 모른다([Service] 가 옛 클래스로
        /// 역직렬화해 버린다). 어중간한 상태를 만들지 않도록 **배포 때 함께** 반영한다
        /// — 서버가 시작하며 Migrations 를 자동 실행하므로 그것으로 충분하다.
        /// 같은 이유로 table_types 메타도 여기서 제외한다(컬럼 없는 필드를 어드민이 조회하면 에러).
        ///
        /// <see cref="Generate"/> 를 쓰지 않는 이유: 그쪽은 어드민 컨트롤러를 만들면서
        /// SpriteAtlas 를 열어 PNG 를 base64 로 굽고 Addressables 전체를 스캔한다.
        /// **컴파일할 때마다** 그걸 돌리면 에디터가 멈춘다.
        ///
        /// 반환 순서는 서버(Program.cs)의 실행 순서와 같아야 한다 —
        /// 호출부에서 파일명 기준으로 정렬한다.
        /// </summary>
        public static List<GeneratedFile> GenerateSchemaSql(Type[] specTypes)
        {
            return new List<GeneratedFile>
            {
                GenerateSupaRunCoreMigration(),
                GenerateSecretMigration(),
                GenerateEnvMigration(),
                GenerateSnapshotMigration(),
                GenerateVersionMigration(),
                GenerateReleaseMigration(),
                GeneratePlayersMigration(),
                GenerateSegmentsMigration(),
                GenerateConfigMetaMigration(specTypes),
                GenerateTypeCatalogMigration(specTypes),
                GenerateAdminUserMigration(),
                GenerateAdminAuditMigration(),
                // 서버 로그도 시스템 표다. 여기 없어서 **배포할 때만** 만들어졌는데,
                // 그 사이 RLS 를 켜는 변경이 반영될 길이 없었다(어드민 로그 화면이 이 표를 읽는다).
                // 파일명이 `_` 로 시작하지 않아 정렬상 core 뒤에 오므로 is_admin() 은 이미 있다.
                new GeneratedFile("Generated/Migrations/server_logs.sql", GenerateServerLogsMigration()),
            }
            .Concat(specTypes.Select(GenerateMigration))
            .ToList();
        }

        public static List<GeneratedFile> Generate(
            Type[] tableTypes, Type[] specTypes, Type[] logicTypes,
            SupaRunSettings settings)
        {
            var files = new List<GeneratedFile>();

            // 어트리뷰트 스텁 (서버에서 컴파일용)
            files.Add(GenerateAttributeStubs());

            // QueryFilter + QueryOptions (서버용)
            files.Add(GenerateQueryStubs());

            // IGameDB 인터페이스
            files.Add(GenerateIGameDB());

            // DapperGameDB
            files.Add(GenerateDapperGameDB());

            // SupaRun 코어 — 메타 테이블 + is_admin() + 감사 트리거 함수.
            // 파일명이 `_` 로 시작해 다른 마이그레이션보다 먼저 실행된다 (ADR-0004).
            files.Add(GenerateSupaRunCoreMigration());
            files.Add(GenerateSecretMigration());
            files.Add(GenerateEnvMigration());
            files.Add(GenerateSnapshotMigration());
            files.Add(GenerateVersionMigration());
            files.Add(GenerateReleaseMigration());
            files.Add(GeneratePlayersMigration());
            files.Add(GenerateSegmentsMigration());
            files.Add(GenerateConfigMetaMigration(specTypes));
            files.Add(GenerateTypeCatalogMigration(specTypes));
            files.Add(GenerateTableMetaMigration(tableTypes));

            // 서버 로그 시스템
            files.Add(new GeneratedFile("Generated/Migrations/server_logs.sql", GenerateServerLogsMigration()));
            files.Add(GenerateServerLogModel());
            files.Add(GenerateServerLogger());

            // [UserData] → Controller + Migration
            foreach (var type in tableTypes)
            {
                files.Add(GenerateReadController(type, "table"));
                files.Add(GenerateMigration(type));
            }

            // [SpecData] → Controller + Migration
            foreach (var type in specTypes)
            {
                files.Add(GenerateReadController(type, "config"));
                files.Add(GenerateMigration(type));
            }

            // [Service] → Controller + Request DTOs
            foreach (var type in logicTypes)
            {
                files.Add(GenerateLogicController(type));
                files.AddRange(GenerateRequestDTOs(type));
            }

            // CS 액션 계층 (③ 트랙) — 게이트 + 시스템 액션 + 어드민 버튼 메타
            files.Add(GenerateCsGate());
            files.Add(GenerateCsSystemController(tableTypes));
            files.Add(GenerateCsActionsMetaMigration(logicTypes));

            // [Cron] → CronController (HTTP 엔드포인트는 그대로 필요)
            var cronMethods = ScanCronMethods(logicTypes);
            if (cronMethods.Count > 0)
                files.Add(GenerateCronController(cronMethods));

            // 관리자/감사로그 — **테이블만** 만든다 (ADR-0004).
            // 어드민 웹은 Supabase 에 직접 붙으므로 AdminController / AdminTableController 가 필요 없다.
            // AdminUser 모델은 남긴다 — Program.cs 의 미들웨어가 첫 가입자 자동 등록에 쓴다.
            // 감사 로그는 suparun_audit() 트리거가 남기므로 서버 모델이 필요 없다.
            files.Add(GenerateAdminUserModel());
            files.Add(GenerateAdminUserMigration());
            files.Add(GenerateAdminAuditMigration());

            return files;
        }

        // ── 어트리뷰트 스텁 ──

        static GeneratedFile GenerateAttributeStubs()
        {
            var sb = new StringBuilder();
            sb.AppendLine("// Auto-generated attribute stubs for server compilation");
            sb.AppendLine("namespace Tjdtjq5.SupaRun");
            sb.AppendLine("{");

            string[] attrs = { "UserData", "SpecData", "Service", "API", "Cron", "CsAction",
                "PrimaryKey", "ForeignKey", "Index", "Unique", "NotNull", "Default",
                "MaxLength", "Hidden", "Json", "RenamedFrom", "CreatedAt", "UpdatedAt",
                "Public", "Private" };

            foreach (var a in attrs)
            {
                if (a == "CsAction")
                    sb.AppendLine($"    [System.AttributeUsage(System.AttributeTargets.Method)] public class {a}Attribute : System.Attribute {{ public string Label; public bool SeniorOnly; public bool Dangerous; public {a}Attribute(string label = null) {{ Label = label; }} }}");
                else if (a == "ForeignKey")
                    sb.AppendLine($"    [System.AttributeUsage(System.AttributeTargets.All)] public class {a}Attribute : System.Attribute {{ public {a}Attribute(System.Type t) {{}} }}");
                else if (a == "Default")
                    sb.AppendLine($"    [System.AttributeUsage(System.AttributeTargets.All)] public class {a}Attribute : System.Attribute {{ public {a}Attribute(object v) {{}} }}");
                else if (a == "MaxLength")
                    sb.AppendLine($"    [System.AttributeUsage(System.AttributeTargets.All)] public class {a}Attribute : System.Attribute {{ public {a}Attribute(int n) {{}} }}");
                else if (a == "RenamedFrom")
                    sb.AppendLine($"    [System.AttributeUsage(System.AttributeTargets.All)] public class {a}Attribute : System.Attribute {{ public {a}Attribute(string s) {{}} }}");
                else if (a == "Cron")
                    sb.AppendLine($"    [System.AttributeUsage(System.AttributeTargets.Method)] public class {a}Attribute : System.Attribute {{ public string Expression; public string TimeZone; public string Description; public {a}Attribute(string expression, string timeZone = \"Etc/UTC\", string description = null) {{ Expression = expression; TimeZone = timeZone; Description = description; }} }}");
                else if (a == "UserData" || a == "SpecData")
                    sb.AppendLine($"    [System.AttributeUsage(System.AttributeTargets.Class)] public class {a}Attribute : System.Attribute {{ public string Group {{ get; }} public {a}Attribute() {{}} public {a}Attribute(string group) => Group = group; }}");
                else if (a == "Json")
                    sb.AppendLine($"    [System.AttributeUsage(System.AttributeTargets.Field)] public class {a}Attribute : System.Attribute {{ public System.Type TargetType {{ get; }} public {a}Attribute() {{}} public {a}Attribute(System.Type targetType) => TargetType = targetType; }}");
                else
                    sb.AppendLine($"    [System.AttributeUsage(System.AttributeTargets.All)] public class {a}Attribute : System.Attribute {{ }}");
            }

            sb.AppendLine("}");

            return new GeneratedFile("Generated/Attributes.cs", sb.ToString());
        }

        // ── IGameDB ──

        static GeneratedFile GenerateQueryStubs()
        {
            return new GeneratedFile("Generated/QueryFilter.cs",
@"using System.Collections.Generic;

public class QueryFilter
{
    public string Column;
    public string Operator;
    public object Value;
    public QueryFilter(string column, string op, object value)
    {
        Column = column;
        Operator = op;
        Value = value;
    }
}

public class QueryOptions
{
    public List<QueryFilter> Filters = new();
    public string OrderBy;
    public bool OrderDesc;
    public int Limit = 1000;
    public int Offset;

    public QueryOptions Eq(string column, object value) { Filters.Add(new QueryFilter(column, ""="", value)); return this; }
    public QueryOptions Gt(string column, object value) { Filters.Add(new QueryFilter(column, "">"", value)); return this; }
    public QueryOptions Lt(string column, object value) { Filters.Add(new QueryFilter(column, ""<"", value)); return this; }
    public QueryOptions Gte(string column, object value) { Filters.Add(new QueryFilter(column, "">="", value)); return this; }
    public QueryOptions Lte(string column, object value) { Filters.Add(new QueryFilter(column, ""<="", value)); return this; }
    public QueryOptions Like(string column, string value) { Filters.Add(new QueryFilter(column, ""like"", value)); return this; }
    public QueryOptions OrderByAsc(string column) { OrderBy = column; OrderDesc = false; return this; }
    public QueryOptions OrderByDesc(string column) { OrderBy = column; OrderDesc = true; return this; }
    public QueryOptions SetLimit(int limit) { Limit = limit; return this; }
    public QueryOptions SetOffset(int offset) { Offset = offset; return this; }
}");
        }

        static GeneratedFile GenerateIGameDB()
        {
            return new GeneratedFile("Generated/IGameDB.cs",
@"using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public interface IGameDB
{
    Task<T> Get<T>(object primaryKey);
    Task<List<T>> GetAll<T>();
    Task Save<T>(T entity);
    Task Delete<T>(object primaryKey);
    Task<List<T>> Query<T>(QueryOptions options);
    Task<int> Count<T>(QueryOptions options);
    Task SaveAll<T>(List<T> entities);
    Task DeleteAll<T>(QueryOptions options);
    Task Transaction(Func<IGameDB, Task> action);
}");
        }

        // ── DapperGameDB ──

        static GeneratedFile GenerateDapperGameDB()
        {
            return new GeneratedFile("Generated/DapperGameDB.cs",
@"using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Npgsql;
using Dapper;

public class DapperGameDB : IGameDB
{
    readonly string _cs;
    readonly IDbConnection _sharedConn;
    readonly IDbTransaction _tx;

    public DapperGameDB(string connectionString) { _cs = connectionString; }
    DapperGameDB(IDbConnection conn, IDbTransaction tx) { _sharedConn = conn; _tx = tx; }

    // 트랜잭션 모드: sharedConn 재사용 (dispose 금지)
    // 일반 모드: 새 커넥션 생성 (using으로 dispose)
    bool IsTransaction => _sharedConn != null;
    IDbConnection GetConn() => _sharedConn ?? new NpgsqlConnection(_cs);

    // Reflection 캐시 — Cloud Run 동시 request race 방지 (Dictionary는 thread-safe X → IndexOutOfRange/corrupted state 발생 가능).
    // GetOrAdd의 valueFactory는 동시 호출 시 여러 번 실행될 수 있으나 reflection은 idempotent라 결과 동일 — 안전.
    static readonly ConcurrentDictionary<Type, System.Reflection.FieldInfo[]> _fieldCache = new();
    static System.Reflection.FieldInfo[] CachedFields(Type type)
        => _fieldCache.GetOrAdd(type, t => t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));

    static string ToSnakeCase(string name)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
            sb.Append(char.ToLower(name[i]));
        }
        return sb.ToString();
    }

    // lowercase 컬럼 → camelCase 필드 매핑용 SELECT 컬럼 생성
    static string SelectCols<T>()
    {
        var q = (char)34; // double quote
        var fields = CachedFields(typeof(T));
        return string.Join("", "", fields.Select(f =>
        {
            var lower = f.Name.ToLower();
            return lower == f.Name ? lower : $""{lower} as {q}{f.Name}{q}"";
        }));
    }

    // WHERE 절 + 파라미터 빌드 (공통)
    static (string where, DynamicParameters param) BuildWhere(QueryOptions options)
    {
        var param = new DynamicParameters();
        if (options?.Filters?.Count > 0)
        {
            var parts = new List<string>();
            for (int i = 0; i < options.Filters.Count; i++)
            {
                var f = options.Filters[i];
                var pn = $""p{i}"";
                var op = f.Operator == ""like"" ? ""ILIKE"" : f.Operator;
                var val = f.Operator == ""like"" ? $""%{f.Value}%"" : f.Value;
                parts.Add($""{f.Column.ToLower()} {op} @{pn}"");
                param.Add(pn, val);
            }
            return ("" WHERE "" + string.Join("" AND "", parts), param);
        }
        return ("""", param);
    }

    public async Task<T> Get<T>(object primaryKey)
    {
        var c = GetConn();
        try
        {
            var table = ToSnakeCase(typeof(T).Name);
            var cols = SelectCols<T>();
            return await c.QueryFirstOrDefaultAsync<T>($""SELECT {cols} FROM {table} WHERE id = @id"", new { id = primaryKey }, _tx);
        }
        finally { if (!IsTransaction) c.Dispose(); }
    }

    public async Task<List<T>> GetAll<T>()
    {
        var c = GetConn();
        try
        {
            var table = ToSnakeCase(typeof(T).Name);
            var cols = SelectCols<T>();
            return (await c.QueryAsync<T>($""SELECT {cols} FROM {table}"", transaction: _tx)).ToList();
        }
        finally { if (!IsTransaction) c.Dispose(); }
    }

    public async Task Save<T>(T entity)
    {
        var c = GetConn();
        try
        {
            var type = typeof(T);
            var fields = CachedFields(type);
            var names = string.Join("", "", fields.Select(f => f.Name.ToLower()));
            var values = string.Join("", "", fields.Select(f => ""@"" + f.Name));
            var updates = string.Join("", "", fields.Where(f => f.Name != ""id"").Select(f => $""{f.Name.ToLower()} = @{f.Name}""));
            var table = ToSnakeCase(type.Name);
            var sql = $""INSERT INTO {table} ({names}) VALUES ({values}) ON CONFLICT (id) DO UPDATE SET {updates}"";
            var param = new DynamicParameters();
            foreach (var f in fields)
                param.Add(f.Name, f.GetValue(entity));
            await c.ExecuteAsync(sql, param, _tx);
        }
        finally { if (!IsTransaction) c.Dispose(); }
    }

    public async Task Delete<T>(object primaryKey)
    {
        var c = GetConn();
        try
        {
            var table = ToSnakeCase(typeof(T).Name);
            await c.ExecuteAsync($""DELETE FROM {table} WHERE id = @id"", new { id = primaryKey }, _tx);
        }
        finally { if (!IsTransaction) c.Dispose(); }
    }

    public async Task<List<T>> Query<T>(QueryOptions options)
    {
        var c = GetConn();
        try
        {
            var table = ToSnakeCase(typeof(T).Name);
            var cols = SelectCols<T>();
            var (where, param) = BuildWhere(options);
            var sql = $""SELECT {cols} FROM {table}{where}"";

            if (!string.IsNullOrEmpty(options?.OrderBy))
                sql += $"" ORDER BY {options.OrderBy.ToLower()}"" + (options.OrderDesc ? "" DESC"" : "" ASC"");

            sql += $"" LIMIT {options?.Limit ?? 1000}"";
            if (options?.Offset > 0) sql += $"" OFFSET {options.Offset}"";

            return (await c.QueryAsync<T>(sql, param, _tx)).ToList();
        }
        finally { if (!IsTransaction) c.Dispose(); }
    }

    public async Task<int> Count<T>(QueryOptions options)
    {
        var c = GetConn();
        try
        {
            var table = ToSnakeCase(typeof(T).Name);
            var (where, param) = BuildWhere(options);
            return await c.ExecuteScalarAsync<int>($""SELECT COUNT(*) FROM {table}{where}"", param, _tx);
        }
        finally { if (!IsTransaction) c.Dispose(); }
    }

    public async Task SaveAll<T>(List<T> entities)
    {
        if (entities == null || entities.Count == 0) return;
        var c = GetConn();
        try
        {
            if (!IsTransaction) ((NpgsqlConnection)c).Open();

            var type = typeof(T);
            var fields = CachedFields(type);
            var names = string.Join("", "", fields.Select(f => f.Name.ToLower()));
            var updates = string.Join("", "", fields.Where(f => f.Name != ""id"").Select(f => $""{f.Name.ToLower()} = EXCLUDED.{f.Name.ToLower()}""));
            var table = ToSnakeCase(type.Name);

            var valueClauses = new List<string>();
            var param = new DynamicParameters();
            for (int i = 0; i < entities.Count; i++)
            {
                var vals = string.Join("", "", fields.Select(f => $""@{f.Name}_{i}""));
                valueClauses.Add($""({vals})"");
                foreach (var f in fields)
                    param.Add($""{f.Name}_{i}"", f.GetValue(entities[i]));
            }

            var sql = $""INSERT INTO {table} ({names}) VALUES {string.Join("", "", valueClauses)} ON CONFLICT (id) DO UPDATE SET {updates}"";
            await c.ExecuteAsync(sql, param, _tx);
        }
        finally { if (!IsTransaction) c.Dispose(); }
    }

    public async Task DeleteAll<T>(QueryOptions options)
    {
        var c = GetConn();
        try
        {
            var table = ToSnakeCase(typeof(T).Name);
            var (where, param) = BuildWhere(options);
            await c.ExecuteAsync($""DELETE FROM {table}{where}"", param, _tx);
        }
        finally { if (!IsTransaction) c.Dispose(); }
    }

    public async Task Transaction(Func<IGameDB, Task> action)
    {
        if (_sharedConn != null)
        {
            // 이미 트랜잭션 안이면 그대로 실행
            await action(this);
            return;
        }
        using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();
        try
        {
            var txDb = new DapperGameDB(conn, tx);
            await action(txDb);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}");
        }

        static string ToSnakeCase(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLower(name[i]));
            }
            return sb.ToString();
        }

        // ── 읽기 Controller ──

        static GeneratedFile GenerateReadController(Type type, string category)
        {
            var name = type.Name;
            var sb = new StringBuilder();
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Microsoft.AspNetCore.Authorization;");
            sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
            sb.AppendLine("");
            sb.AppendLine($"[ApiController]");
            sb.AppendLine($"[Route(\"api/{ToSnakeCase(name)}\")]");
            sb.AppendLine("[Authorize]");
            sb.AppendLine($"public class {name}Controller : ControllerBase");
            sb.AppendLine("{");
            sb.AppendLine("    readonly IGameDB _db;");
            sb.AppendLine($"    public {name}Controller(IGameDB db) => _db = db;");
            sb.AppendLine("");
            sb.AppendLine("    [HttpGet(\"{id}\")]");
            sb.AppendLine($"    public async Task<IActionResult> Get(string id)");
            sb.AppendLine("    {");
            sb.AppendLine($"        var result = await _db.Get<{name}>(id);");
            sb.AppendLine("        return result != null ? Ok(result) : NotFound();");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    [HttpGet]");
            sb.AppendLine($"    public async Task<IActionResult> GetAll()");
            sb.AppendLine("    {");
            sb.AppendLine($"        var result = await _db.GetAll<{name}>();");
            sb.AppendLine("        return Ok(result);");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return new GeneratedFile($"Generated/Controllers/{name}Controller.cs", sb.ToString());
        }

        // ── [Service] Controller ──

        static GeneratedFile GenerateLogicController(Type type)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System.Diagnostics;");
            sb.AppendLine("using System.Text.Json;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using Microsoft.AspNetCore.Authorization;");
            sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
            sb.AppendLine("");

            // 생성자 파라미터 분석
            var ctor = type.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();
            var ctorParams = ctor?.GetParameters() ?? Array.Empty<ParameterInfo>();

            // CS 액션(#38)이 하나라도 있으면 롤 게이트·감사용 NpgsqlConnection 을 함께 받는다
            // (admin_user_role/admin_audit_log 는 IGameDB 의 타입 CRUD 밖 — raw SQL 이 필요하다).
            var hasCsActions = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(m => !m.IsSpecialName && m.GetCustomAttribute<CsActionAttribute>() != null);

            sb.AppendLine("[ApiController]");
            sb.AppendLine($"[Route(\"api/{ToSnakeCase(type.Name)}\")]");
            sb.AppendLine($"public class {type.Name}Controller : ControllerBase");
            sb.AppendLine("{");

            // IGameDB는 항상 주입 (서비스 + ServerLogger 양쪽에서 사용)
            sb.AppendLine("    readonly IGameDB _db;");
            if (hasCsActions)
            {
                sb.AppendLine("    readonly Npgsql.NpgsqlConnection _conn;");
                sb.AppendLine($"    public {type.Name}Controller(IGameDB db, Npgsql.NpgsqlConnection conn) {{ _db = db; _conn = conn; }}");
            }
            else
            {
                sb.AppendLine($"    public {type.Name}Controller(IGameDB db) => _db = db;");
            }

            // 서비스 인스턴스 생성 코드 구축
            var svcCtorLines = new List<string>();
            var svcCtorArgs = new List<string>();
            foreach (var p in ctorParams)
            {
                if (p.ParameterType.Name == "IGameDB")
                {
                    svcCtorArgs.Add("_db");
                }
                else
                {
                    // 의존 서비스를 IGameDB로 직접 생성
                    var depCtor = p.ParameterType.GetConstructors()
                        .OrderByDescending(c => c.GetParameters().Length)
                        .FirstOrDefault();
                    var depParams = depCtor?.GetParameters() ?? Array.Empty<ParameterInfo>();
                    var depArgs = string.Join(", ", depParams.Select(dp =>
                        dp.ParameterType.Name == "IGameDB" ? "_db" : $"new {dp.ParameterType.Name}(_db)"));
                    var varName = $"__{p.Name}";
                    svcCtorLines.Add($"        var {varName} = new {p.ParameterType.Name}({depArgs});");
                    svcCtorArgs.Add(varName);
                }
            }

            // [API]/[CsAction] 어트리뷰트가 붙은 메서드만 엔드포인트로 생성
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName &&
                    (m.GetCustomAttribute<APIAttribute>() != null ||
                     m.GetCustomAttribute<CsActionAttribute>() != null));

            var svcPrefix = type.Name.Replace("Service", "");

            foreach (var m in methods)
            {
                var cs = m.GetCustomAttribute<CsActionAttribute>();
                var reqName = $"{svcPrefix}_{m.Name}Request";
                var paramList = m.GetParameters().Any()
                    ? $"[FromBody] {reqName} req"
                    : "";
                var args = string.Join(", ", m.GetParameters().Select(p => $"req.{p.Name}"));

                // 접근 제어. CS 액션은 [Authorize] + 본문 롤 게이트 2겹이다 — JWT 롤 클레임에
                // 기대지 않는 이유: 롤의 진실은 admin_user_role 표라 회수가 즉시 반영돼야 한다.
                var authAttr = cs != null
                    ? "[Authorize]"
                    : m.GetCustomAttribute<PublicAttribute>() != null
                        ? "[AllowAnonymous]"
                        : m.GetCustomAttribute<PrivateAttribute>() != null
                            ? "[Authorize(Roles = \"admin\")]"
                            : "[Authorize]";

                var endpointName = $"{type.Name}/{m.Name}";
                var hasReqBody = m.GetParameters().Any();

                sb.AppendLine("");
                sb.AppendLine($"    {authAttr}");
                sb.AppendLine($"    [HttpPost(\"{m.Name}\")]");
                sb.AppendLine($"    public async Task<IActionResult> {m.Name}({paramList})");
                sb.AppendLine("    {");
                sb.AppendLine("        var sw = Stopwatch.StartNew();");
                if (hasReqBody)
                    sb.AppendLine("        var reqJson = JsonSerializer.Serialize(req);");
                sb.AppendLine("        try");
                sb.AppendLine("        {");
                if (cs != null)
                {
                    // 롤 게이트 — 실행 전. 감사 — 실행 성공 후(실패한 시도는 500 경로의 서버 로그가 남긴다).
                    sb.AppendLine("            var __sub = User.FindFirst(\"sub\")?.Value ?? \"\";");
                    sb.AppendLine($"            if (!await CsGate.Allowed(_conn, __sub, seniorOnly: {(cs.SeniorOnly ? "true" : "false")}))");
                    sb.AppendLine("                return StatusCode(403, new { error = \"CS 롤이 필요합니다.\" });");
                }
                foreach (var line in svcCtorLines)
                    sb.AppendLine("    " + line);
                var svcArgsStr = string.Join(", ", svcCtorArgs);
                sb.AppendLine(ctorParams.Length > 0
                    ? $"            var service = new {type.Name}({svcArgsStr});"
                    : $"            var service = new {type.Name}();");

                var isTask = typeof(System.Threading.Tasks.Task).IsAssignableFrom(m.ReturnType);
                var hasResult = m.ReturnType.IsGenericType && m.ReturnType != typeof(System.Threading.Tasks.Task);
                var isVoidReturn = m.ReturnType == typeof(void) || m.ReturnType == typeof(System.Threading.Tasks.Task);

                // CS 감사의 대상 플레이어 — 관례상 첫 playerId/userId 파라미터.
                // 감사 실패는 삼키고 stderr 로 남긴다 — 서비스는 이미 자기 트랜잭션을 커밋했다.
                // 여기서 500 을 내면 성공한 액션이 실패로 보이고, Mono HttpClient 재전송과
                // 겹치면 이중 실행(재화 이중 지급)이 실위험이다(리뷰 실측 지적).
                var csTarget = m.GetParameters().FirstOrDefault(p => p.Name == "playerId" || p.Name == "userId");
                var csAudit = cs == null ? null :
                    $"            try {{ await CsGate.Audit(_conn, __sub, \"cs:{m.Name}\", " +
                    $"{(csTarget != null ? $"req.{csTarget.Name}" : "null")}, {(hasReqBody ? "reqJson" : "null")}); }}\n" +
                    $"            catch (System.Exception __auditEx) {{ System.Console.Error.WriteLine(\"[CsGate] 감사 기록 실패({m.Name}): \" + __auditEx.Message); }}";

                if (hasResult)
                {
                    sb.AppendLine(isTask
                        ? $"            var result = await service.{m.Name}({args});"
                        : $"            var result = service.{m.Name}({args});");
                    if (csAudit != null) sb.AppendLine(csAudit);
                    sb.AppendLine("            return Ok(result);");
                }
                else if (isVoidReturn)
                {
                    sb.AppendLine(isTask
                        ? $"            await service.{m.Name}({args});"
                        : $"            service.{m.Name}({args});");
                    if (csAudit != null) sb.AppendLine(csAudit);
                    sb.AppendLine("            return Ok();");
                }
                else
                {
                    // 동기 + 값 반환 (long, string 등)
                    sb.AppendLine($"            var result = service.{m.Name}({args});");
                    if (csAudit != null) sb.AppendLine(csAudit);
                    sb.AppendLine("            return Ok(result);");
                }
                sb.AppendLine("        }");
                sb.AppendLine("        catch (System.Exception ex)");
                sb.AppendLine("        {");
                sb.AppendLine("            sw.Stop();");
                sb.AppendLine($"            await ServerLogger.LogError(_db, ex.Message,");
                sb.AppendLine($"                stack: ex.StackTrace,");
                sb.AppendLine($"                endpoint: \"{endpointName}\",");
                sb.AppendLine($"                serviceName: \"{type.Name}\",");
                sb.AppendLine($"                statusCode: 500,");
                sb.AppendLine(hasReqBody
                    ? $"                requestBody: reqJson,"
                    : $"                requestBody: null,");
                sb.AppendLine($"                durationMs: (int)sw.ElapsedMilliseconds);");
                sb.AppendLine("            return StatusCode(500, new { error = ex.Message });");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }

            sb.AppendLine("}");

            return new GeneratedFile($"Generated/Controllers/{type.Name}Controller.cs", sb.ToString());
        }

        // ── Request DTO ──

        static List<GeneratedFile> GenerateRequestDTOs(Type type)
        {
            var files = new List<GeneratedFile>();
            var svcName = type.Name.Replace("Service", "");

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetParameters().Length > 0 &&
                    (m.GetCustomAttribute<APIAttribute>() != null ||
                     m.GetCustomAttribute<CsActionAttribute>() != null));

            foreach (var m in methods)
            {
                var dtoName = $"{svcName}_{m.Name}Request";
                var sb = new StringBuilder();
                sb.AppendLine($"public class {dtoName}");
                sb.AppendLine("{");
                foreach (var p in m.GetParameters())
                    sb.AppendLine($"    public {ToCSharpType(p.ParameterType)} {p.Name} {{ get; set; }}");
                sb.AppendLine("}");

                files.Add(new GeneratedFile($"Generated/Models/{dtoName}.cs", sb.ToString()));
            }

            return files;
        }

        // ── Migration SQL ──

        /// <summary>[UserData]/[SpecData] 전체의 마이그레이션 SQL을 하나로 합쳐 반환.</summary>
        public static string GenerateMigrationSql()
        {
            var types = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.Contains("Assembly-CSharp")) continue;
                foreach (var type in assembly.GetTypes())
                {
                    if (type.GetCustomAttribute<UserDataAttribute>() != null ||
                        type.GetCustomAttribute<SpecDataAttribute>() != null)
                        types.Add(type);
                }
            }

            var sb = new StringBuilder();

            // 시스템 테이블: server_logs
            sb.AppendLine(GenerateServerLogsMigration());

            // 유저 정의 테이블
            foreach (var type in types)
            {
                var file = GenerateMigration(type);
                sb.AppendLine(file.Content);
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        // ── ServerLog 모델 ──

        static GeneratedFile GenerateServerLogModel()
        {
            return new GeneratedFile("Generated/ServerLog.cs",
@"public class ServerLog
{
    public string id;
    public string level;
    public string message;
    public string stack;
    public string endpoint;
    public string player_id;
    public string service_name;
    public int status_code;
    public string request_body;
    public int duration_ms;
    public long createdat;
}");
        }

        // ── ServerLogger 헬퍼 ──

        static GeneratedFile GenerateServerLogger()
        {
            return new GeneratedFile("Generated/ServerLogger.cs",
@"using System;
using System.Threading.Tasks;

public static class ServerLogger
{
    public static async Task LogError(IGameDB db, string message, string stack = null,
        string endpoint = null, string playerId = null, string serviceName = null,
        int statusCode = 500, string requestBody = null, int durationMs = 0)
    {
        try
        {
            await db.Save(new ServerLog
            {
                id = Guid.NewGuid().ToString(),
                level = ""error"",
                message = message,
                stack = stack,
                endpoint = endpoint,
                player_id = playerId,
                service_name = serviceName,
                status_code = statusCode,
                request_body = requestBody,
                duration_ms = durationMs,
                createdat = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
        catch { }
    }

    public static async Task LogWarn(IGameDB db, string message,
        string endpoint = null, string playerId = null, string serviceName = null,
        string requestBody = null)
    {
        try
        {
            await db.Save(new ServerLog
            {
                id = Guid.NewGuid().ToString(),
                level = ""warn"",
                message = message,
                endpoint = endpoint,
                player_id = playerId,
                service_name = serviceName,
                request_body = requestBody,
                createdat = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
        catch { }
    }
}");
        }

        // ── server_logs 마이그레이션 ──

        static string GenerateServerLogsMigration()
        {
            return @"-- ══ 서버 로그 (자동 생성) ══════════════════════════════════════════════
-- 서버가 service_role 로 쓰고, 어드민이 관리자 자격으로 읽는다.
CREATE TABLE IF NOT EXISTS server_log (
    id TEXT PRIMARY KEY,
    level TEXT NOT NULL,
    message TEXT NOT NULL,
    stack TEXT,
    endpoint TEXT,
    player_id TEXT,
    service_name TEXT,
    status_code INTEGER,
    request_body TEXT,
    duration_ms INTEGER,
    createdat BIGINT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_server_log_level_createdat ON server_log (level, createdat DESC);
CREATE INDEX IF NOT EXISTS idx_server_log_createdat ON server_log (createdat DESC);

-- ⚠ 이 표는 오랫동안 RLS 가 꺼져 있었다. 그동안 **anon key 만 있으면 누구나 읽혔고**,
-- 그 키는 게임 빌드에 들어간다 — request_body·player_id·스택트레이스가 그대로 노출된다.
-- 읽는 쪽이 Unity 대시보드(anon key 사용)였기 때문에 생긴 구멍이라, 그 화면을 어드민으로
-- 옮기면서 함께 닫는다. 어드민은 로그인한 관리자로 읽으므로 아래 정책을 지난다.
--
-- 서버는 service_role 로 쓰므로 RLS 를 타지 않는다 — 쓰기 정책이 없어도 기록은 그대로 남는다.
ALTER TABLE server_log ENABLE ROW LEVEL SECURITY;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'server_log' AND policyname = 'admin_read') THEN
    CREATE POLICY admin_read ON server_log FOR SELECT USING (is_admin());
  END IF;
END $$;

-- 열람은 롤 보유자 전체에 연다 (#24 — game-viewer 도 로그를 본다). admin_read 는
-- game-admin 의 중복 통로로 남는다 — 정책은 OR 결합이라 해가 없다.
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'server_log' AND policyname = 'operator_read') THEN
    CREATE POLICY operator_read ON server_log FOR SELECT USING (suparun_is_operator());
  END IF;
END $$;
";
        }

        static GeneratedFile GenerateMigration(Type type)
        {
            var sb = new StringBuilder();
            var tableName = ToSnakeCase(type.Name);
            bool isConfig = type.GetCustomAttribute<SpecDataAttribute>() != null;

            sb.AppendLine($"CREATE TABLE IF NOT EXISTS {tableName} (");

            var info = AttributeRegistry.Get(type);
            var fields = info.Fields;
            var lines = new List<string>();

            foreach (var f in fields)
            {
                var col = f.Name.ToLower();
                var sqlType = GetSqlType(f, info);
                var constraints = GetConstraints(f, info);
                lines.Add($"    {col} {sqlType}{constraints}");
            }

            // [SpecData] 타입은 sort_order 컬럼 자동 추가 (어드민 드래그 정렬용)
            if (isConfig && !fields.Any(f => f.Name == "sort_order"))
                lines.Add("    sort_order INTEGER NOT NULL DEFAULT 0");

            sb.AppendLine(string.Join(",\n", lines));
            sb.AppendLine(");");

            // 기존 테이블에 새 컬럼 추가 — DO 블록으로 감싸서 개별 에러 무시
            // DEFAULT 절 포함 → 새 컬럼 추가 시 기존 row가 자동으로 default 값 채움 (PG 11+)
            sb.AppendLine();
            sb.AppendLine($"DO $$ BEGIN");
            foreach (var f in fields)
            {
                var col = f.Name.ToLower();
                var sqlType = GetSqlType(f, info);
                var defClause = GetDefaultClause(f, info);
                sb.AppendLine($"  ALTER TABLE {tableName} ADD COLUMN IF NOT EXISTS {col} {sqlType}{defClause};");
            }
            if (isConfig && !fields.Any(f => f.Name == "sort_order"))
                sb.AppendLine($"  ALTER TABLE {tableName} ADD COLUMN IF NOT EXISTS sort_order INTEGER NOT NULL DEFAULT 0;");
            sb.AppendLine($"END $$;");

            // 기존 NULL row를 default 값으로 채움 (멱등 — NULL 없으면 0건 영향).
            // PG 11+의 ADD COLUMN DEFAULT는 자동으로 기존 row를 채우지만,
            // 그 이전에 default 없이 추가된 컬럼이 NULL로 남아있는 경우를 위한 안전망.
            var nullFixLines = new List<string>();
            foreach (var f in fields)
            {
                var col = f.Name.ToLower();
                var literal = GetDefaultLiteral(f, info);
                if (literal == null) continue;
                nullFixLines.Add($"  UPDATE {tableName} SET {col} = {literal} WHERE {col} IS NULL;");
            }
            if (nullFixLines.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"DO $$ BEGIN");
                foreach (var line in nullFixLines) sb.AppendLine(line);
                sb.AppendLine($"END $$;");
            }

            // [SpecData] 타입은 공개 읽기 RLS 정책 + sort_order 인덱스 + backfill 추가
            if (isConfig)
            {
                sb.AppendLine();
                sb.AppendLine($"ALTER TABLE {tableName} ENABLE ROW LEVEL SECURITY;");
                sb.AppendLine($"DO $$ BEGIN");
                sb.AppendLine($"  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = '{tableName}' AND policyname = 'public_read') THEN");
                sb.AppendLine($"    CREATE POLICY public_read ON {tableName} FOR SELECT USING (true);");
                sb.AppendLine($"  END IF;");
                sb.AppendLine($"END $$;");

                // sort_order 정렬 인덱스
                sb.AppendLine();
                sb.AppendLine($"CREATE INDEX IF NOT EXISTS idx_{tableName}_sort ON {tableName}(sort_order);");

                // sort_order backfill — 모두 0이면 ctid(입력 순)로 부여 (멱등 — 1회만 동작)
                sb.AppendLine($"DO $$ BEGIN");
                sb.AppendLine($"  IF (SELECT COUNT(*) FROM {tableName} WHERE sort_order != 0) = 0 THEN");
                sb.AppendLine($"    WITH ordered AS (SELECT ctid, ROW_NUMBER() OVER (ORDER BY ctid) - 1 AS rn FROM {tableName})");
                sb.AppendLine($"    UPDATE {tableName} SET sort_order = ordered.rn FROM ordered WHERE {tableName}.ctid = ordered.ctid;");
                sb.AppendLine($"  END IF;");
                sb.AppendLine($"END $$;");

                // 어드민이 Supabase 에 직접 쓰므로 쓰기 정책이 필요하다 (ADR-0004).
                // 지금까지는 서버가 service_role 로 RLS 를 우회해 썼기에 정책이 없었다.
                //
                // 승격 전용 잠금(#50)이 식에 끼면서 DROP+CREATE 로 바꿨다 — IF NOT EXISTS 로는
                // 기존 DB 가 옛 식(is_admin 만)에 머문다. prod(이름 규약)에서는 game-admin 이라도
                // 직접 쓰기가 거부된다 — 데이터는 dev 에서 만들어 업로드→diff→게시 경로로만.
                sb.AppendLine();
                sb.AppendLine($"DROP POLICY IF EXISTS admin_write ON {tableName};");
                sb.AppendLine($"CREATE POLICY admin_write ON {tableName} FOR ALL " +
                              "USING (is_admin() AND NOT suparun_is_promote_only()) " +
                              "WITH CHECK (is_admin() AND NOT suparun_is_promote_only());");

                // 변경 이력은 트리거로 남긴다 — 클라이언트가 건너뛸 수 없다.
                // [SpecData] 에만 단다. [UserData] 에 달면 게임 플레이마다 로그가 쌓여 폭발한다.
                AppendAuditTrigger(sb, tableName, info);
            }
            else
            {
                // [UserData] — **관리자만**. 게임은 anon 으로 직접 붙지 않는다.
                //
                // 게임의 읽기·쓰기는 Cloud Run 이 service_role 로 처리하므로 RLS 를 타지 않는다.
                // 그래서 anon 에게 열어 줄 이유가 없고, 여기 필요한 것은 **어드민이 볼 수 있게** 하는 것뿐이다.
                // (게임 클라이언트도 같은 anon key 를 쓰므로, 여기서 연 문은 모든 플레이어에게도 열린다.)
                //
                // "본인 행만 읽기" 정책은 게임이 UserData 를 직접 읽을 때만 의미가 있는데,
                // 그러려면 저장도 직접 해야 하고([Service] 가 새 필드를 버리므로) 그건 게임 아키텍처
                // 변경이다. ADR-0004 결정 20 참조.
                sb.AppendLine();
                sb.AppendLine($"ALTER TABLE {tableName} ENABLE ROW LEVEL SECURITY;");
                AppendPolicy(sb, tableName, "admin_all", "FOR ALL", "is_admin()", "is_admin()");
                // 조회(View 화면)는 롤 보유자 전체 — 쓰기는 위 admin_all(=game-admin)만 (#24)
                AppendPolicy(sb, tableName, "operator_read", "FOR SELECT", "suparun_is_operator()");
            }

            return new GeneratedFile($"Generated/Migrations/{tableName}.sql", sb.ToString());
        }

        /// <summary>RLS 정책을 멱등하게 추가한다 (CREATE POLICY 에는 IF NOT EXISTS 가 없다).</summary>
        static void AppendPolicy(StringBuilder sb, string tableName, string policyName,
            string forClause, string usingExpr, string checkExpr = null)
        {
            sb.AppendLine($"DO $$ BEGIN");
            sb.AppendLine($"  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = '{tableName}' AND policyname = '{policyName}') THEN");
            var check = checkExpr != null ? $" WITH CHECK ({checkExpr})" : "";
            sb.AppendLine($"    CREATE POLICY {policyName} ON {tableName} {forClause} USING ({usingExpr}){check};");
            sb.AppendLine($"  END IF;");
            sb.AppendLine($"END $$;");
        }

        /// <summary>
        /// 변경 이력 트리거. PK 컬럼명을 인자로 넘겨 공용 함수 하나를 재사용한다.
        /// DROP + CREATE 로 멱등 — 컬럼이 바뀌어도 최신 정의로 덮인다.
        /// </summary>
        static void AppendAuditTrigger(StringBuilder sb, string tableName, TypeAttributeInfo info)
        {
            var pk = info.PrimaryKey?.Name.ToLower() ?? "id";
            sb.AppendLine();
            sb.AppendLine($"DROP TRIGGER IF EXISTS audit_{tableName} ON {tableName};");
            sb.AppendLine($"CREATE TRIGGER audit_{tableName}");
            sb.AppendLine($"  AFTER INSERT OR UPDATE OR DELETE ON {tableName}");
            sb.AppendLine($"  FOR EACH ROW EXECUTE FUNCTION suparun_audit('{pk}');");
        }

        static string ToCSharpType(Type t)
        {
            if (t == typeof(string)) return "string";
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "long";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(void)) return "void";
            return t.Name;
        }

        static string GetSqlType(FieldInfo f, TypeAttributeInfo info)
        {
            var maxLen = info.GetMaxLength(f);
            if (f.FieldType == typeof(string))
                return maxLen != null ? $"VARCHAR({maxLen.Value})" : "TEXT";
            if (f.FieldType == typeof(int)) return "INTEGER";
            if (f.FieldType == typeof(long)) return "BIGINT";
            if (f.FieldType == typeof(float)) return "REAL";
            if (f.FieldType == typeof(double)) return "DOUBLE PRECISION";
            if (f.FieldType == typeof(bool)) return "BOOLEAN";
            return "TEXT";
        }

        static string GetConstraints(FieldInfo f, TypeAttributeInfo info)
        {
            var parts = new List<string>();
            if (info.IsPrimaryKey(f)) parts.Add(" PRIMARY KEY");
            if (info.IsNotNull(f)) parts.Add(" NOT NULL");
            if (info.IsUnique(f)) parts.Add(" UNIQUE");
            var defClause = GetDefaultClause(f, info);
            if (!string.IsNullOrEmpty(defClause)) parts.Add(defClause);
            return string.Join("", parts);
        }

        /// <summary>
        /// 컬럼 DEFAULT 절 생성. [Default] attribute 우선, 없으면 C# field initializer 사용.
        /// 예: public float lateral_extend = 1f; → " DEFAULT 1"
        /// string은 NULL 허용 (skip). 알 수 없는 타입도 skip.
        /// </summary>
        static string GetDefaultClause(FieldInfo f, TypeAttributeInfo info)
        {
            var literal = GetDefaultLiteral(f, info);
            return literal != null ? $" DEFAULT {literal}" : string.Empty;
        }

        /// <summary>SQL DEFAULT 리터럴만 반환 (절 prefix 없이). 없으면 null.</summary>
        static string GetDefaultLiteral(FieldInfo f, TypeAttributeInfo info)
        {
            // [Default] attribute가 있으면 그 값(null이어도) 우선 — 원본과 동일하게 initializer fallback 안 함.
            if (info.HasDefault(f)) return info.GetDefault(f)?.ToString();
            return GetSqlDefaultFromInitializer(f);
        }

        static string GetSqlDefaultFromInitializer(FieldInfo f)
        {
            var t = f.FieldType;
            if (t == typeof(string)) return null; // nullable string은 default 안 적용

            object instance;
            try { instance = Activator.CreateInstance(f.DeclaringType); }
            catch { return null; } // parameterless ctor 없음 → skip

            var v = f.GetValue(instance);
            if (v == null) return null;

            if (t == typeof(bool)) return (bool)v ? "TRUE" : "FALSE";
            if (t == typeof(int) || t == typeof(long) || t == typeof(short))
                return v.ToString();
            if (t == typeof(float) || t == typeof(double))
                return Convert.ToDouble(v).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (t.IsEnum) return $"'{v}'";
            return null; // 알 수 없는 타입 (List/object 등) skip
        }

        // ── Cron ──

        struct CronMethodInfo
        {
            public Type ServiceType;
            public MethodInfo Method;
            public string Expression;
            public string TimeZone;
            public string Description;
        }

        static List<CronMethodInfo> ScanCronMethods(Type[] logicTypes)
        {
            var result = new List<CronMethodInfo>();
            foreach (var type in logicTypes)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var m in methods)
                {
                    var cron = m.GetCustomAttribute<CronAttribute>();
                    if (cron == null) continue;
                    if (m.GetParameters().Length > 0)
                    {
                        UnityEngine.Debug.LogError(
                            $"[SupaRun] [Cron] 메서드는 파라미터를 가질 수 없습니다: {type.Name}.{m.Name} — 스킵됨");
                        continue;
                    }
                    result.Add(new CronMethodInfo
                    {
                        ServiceType = type,
                        Method = m,
                        Expression = ResolveCronAlias(cron.Expression),
                        TimeZone = cron.TimeZone ?? "Etc/UTC",
                        Description = cron.Description ?? ""
                    });
                }
            }
            return result;
        }

        static string ResolveCronAlias(string expr)
        {
            var normalized = expr.Trim().ToLower();
            return normalized switch
            {
                "@daily" => "0 0 * * *",
                "@weekly" => "0 0 * * MON",
                "@hourly" => "0 * * * *",
                "@midnight" => "0 0 * * *",
                _ when normalized.StartsWith("@every ") => ParseEvery(normalized),
                _ => expr
            };
        }

        static string ParseEvery(string normalized)
        {
            // @every 30m → */30 * * * *
            // @every 2h → 0 */2 * * *
            var part = normalized.Substring(7).Trim();
            if (part.EndsWith("m") && int.TryParse(part.TrimEnd('m'), out var min))
                return $"*/{min} * * * *";
            if (part.EndsWith("h") && int.TryParse(part.TrimEnd('h'), out var hr))
                return $"0 */{hr} * * *";
            return normalized; // 파싱 실패 시 원본 반환
        }

        static GeneratedFile GenerateCronController(List<CronMethodInfo> cronMethods)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Diagnostics;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
            sb.AppendLine("");
            sb.AppendLine("[ApiController]");
            sb.AppendLine("[Route(\"api/cron\")]");
            sb.AppendLine("public class CronController : ControllerBase");
            sb.AppendLine("{");
            sb.AppendLine("    readonly IGameDB _db;");
            sb.AppendLine("    public CronController(IGameDB db) => _db = db;");
            sb.AppendLine("");
            sb.AppendLine("    bool ValidateCronSecret()");
            sb.AppendLine("    {");
            sb.AppendLine("        var expected = Environment.GetEnvironmentVariable(\"CRON_SECRET\");");
            sb.AppendLine("        if (string.IsNullOrEmpty(expected)) return true;");
            sb.AppendLine("        var actual = Request.Headers[\"X-Cron-Secret\"].FirstOrDefault();");
            sb.AppendLine("        return actual == expected;");
            sb.AppendLine("    }");

            foreach (var cm in cronMethods)
            {
                var svcType = cm.ServiceType;
                var m = cm.Method;
                var endpointName = $"cron/{svcType.Name}/{m.Name}";

                // 서비스 생성자 분석 (GenerateLogicController와 동일)
                var ctor = svcType.GetConstructors()
                    .OrderByDescending(c => c.GetParameters().Length)
                    .FirstOrDefault();
                var ctorParams = ctor?.GetParameters() ?? Array.Empty<ParameterInfo>();

                var svcCtorLines = new List<string>();
                var svcCtorArgs = new List<string>();
                foreach (var p in ctorParams)
                {
                    if (p.ParameterType.Name == "IGameDB")
                    {
                        svcCtorArgs.Add("_db");
                    }
                    else
                    {
                        var depCtor = p.ParameterType.GetConstructors()
                            .OrderByDescending(c => c.GetParameters().Length)
                            .FirstOrDefault();
                        var depParams = depCtor?.GetParameters() ?? Array.Empty<ParameterInfo>();
                        var depArgs = string.Join(", ", depParams.Select(dp =>
                            dp.ParameterType.Name == "IGameDB" ? "_db" : $"new {dp.ParameterType.Name}(_db)"));
                        var varName = $"__{p.Name}";
                        svcCtorLines.Add($"            var {varName} = new {p.ParameterType.Name}({depArgs});");
                        svcCtorArgs.Add(varName);
                    }
                }
                var svcArgsStr = string.Join(", ", svcCtorArgs);

                var isTask = typeof(System.Threading.Tasks.Task).IsAssignableFrom(m.ReturnType);

                sb.AppendLine("");
                sb.AppendLine($"    [HttpPost(\"{svcType.Name}/{m.Name}\")]");
                sb.AppendLine($"    public async Task<IActionResult> {svcType.Name}_{m.Name}()");
                sb.AppendLine("    {");
                sb.AppendLine("        if (!ValidateCronSecret()) return Unauthorized();");
                sb.AppendLine("        var sw = Stopwatch.StartNew();");
                sb.AppendLine("        try");
                sb.AppendLine("        {");
                foreach (var line in svcCtorLines)
                    sb.AppendLine("    " + line);
                sb.AppendLine(ctorParams.Length > 0
                    ? $"            var service = new {svcType.Name}({svcArgsStr});"
                    : $"            var service = new {svcType.Name}();");
                sb.AppendLine(isTask
                    ? $"            await service.{m.Name}();"
                    : $"            service.{m.Name}();");
                sb.AppendLine("            return Ok();");
                sb.AppendLine("        }");
                sb.AppendLine("        catch (Exception ex)");
                sb.AppendLine("        {");
                sb.AppendLine("            sw.Stop();");
                sb.AppendLine($"            await ServerLogger.LogError(_db, ex.Message,");
                sb.AppendLine($"                stack: ex.StackTrace,");
                sb.AppendLine($"                endpoint: \"{endpointName}\",");
                sb.AppendLine($"                serviceName: \"{svcType.Name}\",");
                sb.AppendLine($"                statusCode: 500,");
                sb.AppendLine($"                durationMs: (int)sw.ElapsedMilliseconds);");
                sb.AppendLine("            return StatusCode(500, new { error = ex.Message });");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }

            sb.AppendLine("}");
            return new GeneratedFile("Generated/Controllers/CronController.cs", sb.ToString());
        }

        /// <summary>pg_cron 확장 활성화 SQL.</summary>
        public static string GenerateCronExtensionsSql_PgCron()
        {
            return "CREATE EXTENSION IF NOT EXISTS pg_cron WITH SCHEMA pg_catalog;";
        }

        /// <summary>pg_net 확장 활성화 SQL.</summary>
        public static string GenerateCronExtensionsSql_PgNet()
        {
            return "CREATE EXTENSION IF NOT EXISTS pg_net WITH SCHEMA extensions;";
        }

        /// <summary>gs_ 접두사 잡 전부 삭제 SQL.</summary>
        public static string GenerateCronCleanupSql()
        {
            return "DO $cleanup$ BEGIN PERFORM cron.unschedule(jobname) FROM cron.job WHERE jobname LIKE 'gs_%'; EXCEPTION WHEN OTHERS THEN NULL; END $cleanup$;";
        }

        /// <summary>pg_cron 잡 등록 SQL 목록. [Cron] 메서드가 없으면 null 반환. 각 잡 = 1개 SQL.</summary>
        public static List<string> GenerateCronScheduleSqls(Type[] logicTypes, string cloudRunUrl, string cronSecret)
        {
            var cronMethods = ScanCronMethods(logicTypes);
            if (cronMethods.Count == 0) return null;

            // SQL 인젝션 방어: hex-only 검증
            if (!System.Text.RegularExpressions.Regex.IsMatch(cronSecret ?? "", "^[a-f0-9]+$"))
                cronSecret = Guid.NewGuid().ToString("N");

            var sqls = new List<string>();
            foreach (var cm in cronMethods)
            {
                var jobName = $"gs_{cm.ServiceType.Name}_{cm.Method.Name}".ToLower();
                var endpoint = $"/api/cron/{cm.ServiceType.Name}/{cm.Method.Name}";

                // '' 이스케이프 사용 (Management API JSON 호환)
                var command = $"SELECT net.http_post(url := ''{cloudRunUrl}{endpoint}'', headers := ''{{\"X-Cron-Secret\": \"{cronSecret}\"}}''::jsonb)";
                sqls.Add($"SELECT cron.schedule('{jobName}', '{cm.Expression}', '{command}');");
            }

            return sqls;
        }

        // ── SupaRun 코어 마이그레이션 (ADR-0004) ──

        /// <summary>
        /// 메타 테이블 + 모든 RLS 정책이 쓰는 공통 함수.
        ///
        /// 파일명이 `_` 로 시작하는 이유: 서버는 Migrations 폴더를 이름순으로 실행하는데
        /// (`Program.cs` — `Directory.GetFiles(...).OrderBy(f => f)`), 다른 파일의 정책이
        /// is_admin() 을 참조하므로 **반드시 먼저** 실행돼야 한다. ASCII 에서 `_`(0x5F)는
        /// 소문자(0x61~)보다 앞이라 항상 첫 번째가 된다.
        ///
        /// 두 함수 모두 plpgsql 인 이유: LANGUAGE sql 은 생성 시점에 본문의 참조 객체를 검증한다.
        /// 이 파일이 admin_user / admin_audit_log 보다 먼저 실행되므로 그때는 두 테이블이 없다.
        /// plpgsql 은 본문을 실행 시점에 해석하므로 순서에 걸리지 않는다.
        /// </summary>
        static GeneratedFile GenerateSupaRunCoreMigration()
        {
            return new GeneratedFile("Generated/Migrations/_suparun_core.sql",
@"-- ══ SupaRun 코어 (자동 생성) ══════════════════════════════════════════
-- 어드민이 서버를 거치지 않고 Supabase 에 직접 붙기 위한 토대. ADR-0004 참조.

-- ── 어드민 메타데이터 ──
-- 어드민이 표를 그리려면 컬럼 목록/enum/FK/조건이 필요한데, 그건 C# attribute 에만 있다.
-- 서버 코드에 _typesJson 으로 박아 넣던 것을 여기로 옮겨 재배포 없이 갱신한다.
CREATE TABLE IF NOT EXISTS suparun_meta (
    key        TEXT PRIMARY KEY,
    value      JSONB NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE suparun_meta ENABLE ROW LEVEL SECURITY;
-- 스키마 정보일 뿐 비밀이 아니다. 어드민이 로그인 전에도 읽을 수 있어야 화면이 뜬다.
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'suparun_meta' AND policyname = 'public_read') THEN
    CREATE POLICY public_read ON suparun_meta FOR SELECT USING (true);
  END IF;
END $$;
-- 쓰기 정책은 없다. Unity 가 Management API(service_role)로만 갱신한다.

-- ── 관리자 판별 — 빌트인 4롤 (ADR-0009, #24) ──
-- game-admin / game-viewer / cs-senior / cs-agent. 한 유저가 복수 롤을 갖고 권한은 합집합.
-- 매핑은 admin_user_role 표다(admin_users.sql — 이 파일보다 뒤에 실행되지만 plpgsql 이라
-- 생성 시점엔 안 걸린다).
-- SECURITY DEFINER 필수 — admin_user_role 자신에도 RLS 가 걸려 있어서, 없으면
-- 함수가 자기 참조에서 막혀 **항상 false** 가 된다.
-- search_path 고정: 없으면 호출자가 스키마를 바꿔치기해 가짜 표로 우회할 수 있다.
CREATE OR REPLACE FUNCTION suparun_has_role(p_role text) RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER STABLE SET search_path = public
AS $$
BEGIN
    RETURN EXISTS (
        SELECT 1 FROM admin_user_role
        WHERE user_id = auth.uid()::text AND role = p_role
    );
END $$;

-- **쓰기 게이트.** 기존 정책 전부가 이 이름을 참조하므로 정의 교체만으로 모든 쓰기가
-- game-admin 전용이 된다 — 정책은 함수를 OID 로 참조해서 본문 교체가 즉시 전파된다.
-- game-viewer 의 쓰기는 여기서 거부된다 (#24 게이트의 RLS 겹).
CREATE OR REPLACE FUNCTION is_admin() RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER STABLE SET search_path = public
AS $$
BEGIN
    RETURN suparun_has_role('game-admin');
END $$;

-- **열람 게이트.** 롤이 하나라도 있으면 어드민 화면(감사·서버 로그·환경 현황·테이블 조회)을
-- 읽을 수 있다. 롤이 없는 로그인(승인 대기)은 여기서도 걸린다.
CREATE OR REPLACE FUNCTION suparun_is_operator() RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER STABLE SET search_path = public
AS $$
BEGIN
    RETURN EXISTS (
        SELECT 1 FROM admin_user_role WHERE user_id = auth.uid()::text
    );
END $$;

-- **승격 전용 환경인가** (ADR-0010 결정 7, #50). 이름 규약(prod 포함)으로 판정한다 —
-- 어드민 타이틀바 경고색과 같은 규약이라 화면과 정책이 같은 말을 한다.
-- 참이면 config 데이터의 PostgREST 직접 쓰기가 정책에서 거부된다(관리표는 제외).
-- 게시(suparun_version_publish)는 SECURITY DEFINER 라 이 잠금과 무관하게 동작한다 —
-- 경로가 하나면 사고도 하나다.
CREATE OR REPLACE FUNCTION suparun_is_promote_only() RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER STABLE SET search_path = public
AS $$
BEGIN
    RETURN coalesce((SELECT value FROM suparun_env WHERE key = 'name'), '') ~* 'prod';
END $$;

-- ── 변경 이력 자동 기록 ──
-- 어드민이 Supabase 에 직접 쓰게 되면서, 기록을 클라이언트에 맡기면 건너뛸 수 있어
-- 감사가 무의미해진다. 트리거로 내리면 **어떤 경로로 고쳐도** 남는다.
-- SECURITY DEFINER: 호출자에게 admin_audit_log INSERT 권한이 없어도 기록돼야 한다.
-- TG_ARGV[0] 로 PK 컬럼명을 받는다 — 테이블마다 PK 이름이 달라 함수 하나를 공용하려면 필요하다.
CREATE OR REPLACE FUNCTION suparun_audit() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
DECLARE
    v_before JSONB;
    v_after  JSONB;
    v_pk     TEXT := TG_ARGV[0];
    v_row_id TEXT;
BEGIN
    IF TG_OP = 'DELETE' THEN
        v_before := to_jsonb(OLD); v_after := NULL;          v_row_id := v_before ->> v_pk;
    ELSIF TG_OP = 'UPDATE' THEN
        v_before := to_jsonb(OLD); v_after := to_jsonb(NEW); v_row_id := v_after  ->> v_pk;
    ELSE
        v_before := NULL;          v_after := to_jsonb(NEW); v_row_id := v_after  ->> v_pk;
    END IF;

    INSERT INTO admin_audit_log
        (id, admin_id, config_type, row_id, action, before_json, after_json, created_at)
    VALUES
        (gen_random_uuid()::text,
         coalesce(auth.uid()::text, 'server'),   -- service_role 로 쓰면 auth.uid() 가 NULL 이다
         TG_TABLE_NAME, v_row_id, lower(TG_OP),
         v_before::text, v_after::text,
         (extract(epoch from now()) * 1000)::bigint);
    RETURN NULL;   -- AFTER 트리거라 반환값은 무시된다
END $$;

-- ══ 정책 관리 (ADR-0004 결정 15~19) ═══════════════════════════════
-- RLS 정책은 코드에도 화면에도 안 나타나고 DB 를 직접 캐물어야 보인다. 그래서 조용히 어긋난다
-- (실제로 FOR ALL USING(true) 정책 8개가 아무도 모르게 있었다).
-- 어드민에서 상시 보이게 하고, 프리셋으로만 바꾸게 한다.

-- 관리 대상인가 — suparun_meta 에 등록된 테이블만 건드릴 수 있다.
-- 이 화이트리스트가 없으면 admin_user 의 정책까지 지울 수 있다.
CREATE OR REPLACE FUNCTION suparun_is_managed(p_table text) RETURNS boolean
LANGUAGE sql SECURITY DEFINER STABLE SET search_path = public
AS $$
  SELECT EXISTS (
    SELECT 1 FROM suparun_meta m, jsonb_array_elements(m.value) e
    WHERE m.key IN ('config_types', 'table_types') AND e->>'tableName' = p_table
  );
$$;

-- 현재 정책 상태. 프리셋으로 되돌려 읽되, 우리가 만든 조합과 다르면 'custom' 이다.
-- unsafe = 쓰기가 무조건 허용된 상태(USING true 인 ALL/INSERT/UPDATE/DELETE) — 화면에서 빨갛게 띄운다.
CREATE OR REPLACE FUNCTION suparun_policies()
RETURNS TABLE(table_name text, preset text, unsafe boolean, detail text)
LANGUAGE plpgsql SECURITY DEFINER STABLE SET search_path = public
AS $$
DECLARE
    r        record;
    v_names  text[];
    v_unsafe boolean;
    v_detail text;
    v_preset text;
BEGIN
    IF NOT is_admin() THEN
        RAISE EXCEPTION '관리자만 조회할 수 있습니다';
    END IF;

    FOR r IN
        SELECT DISTINCT e->>'tableName' AS tbl
        FROM suparun_meta m, jsonb_array_elements(m.value) e
        WHERE m.key IN ('config_types', 'table_types')
        ORDER BY 1
    LOOP
        SELECT array_agg(p.policyname ORDER BY p.policyname),
               bool_or(p.cmd <> 'SELECT' AND coalesce(p.qual, 'true') = 'true'),
               string_agg(p.policyname || '(' || p.cmd || ': ' || coalesce(p.qual, '-') || ')', ', ' ORDER BY p.policyname)
          INTO v_names, v_unsafe, v_detail
          FROM pg_policies p
         WHERE p.schemaname = 'public' AND p.tablename = r.tbl;

        v_names  := coalesce(v_names, ARRAY[]::text[]);
        v_unsafe := coalesce(v_unsafe, false);

        -- 이름 조합으로 프리셋을 되짚는다. 조건식까지 우리 것과 같아야 하므로 unsafe 면 custom 이다.
        -- 'admin' 은 두 형태를 인정한다 — operator_read(#24) 가 스키마 반영으로 추가되기
        -- 전의 표([admin_all]만)가 잠깐 custom 으로 보이면 화면이 거짓 경고를 띄운다.
        IF v_unsafe THEN
            v_preset := 'custom';
        ELSIF v_names = ARRAY['admin_write', 'public_read'] THEN
            v_preset := 'public';
        ELSIF v_names = ARRAY['admin_all'] OR v_names = ARRAY['admin_all', 'operator_read'] THEN
            v_preset := 'admin';
        ELSIF array_length(v_names, 1) IS NULL THEN
            v_preset := 'locked';
        ELSE
            v_preset := 'custom';
        END IF;

        table_name := r.tbl; preset := v_preset; unsafe := v_unsafe; detail := coalesce(v_detail, '(정책 없음)');
        RETURN NEXT;
    END LOOP;
END $$;

-- 프리셋 적용. **테이블명과 프리셋 이름만** 받는다 — 조건식을 문자열로 받으면
-- 그 자체가 임의 SQL 실행 통로가 된다. 식별자는 format('%I') 로만 조립한다.
CREATE OR REPLACE FUNCTION suparun_set_policy(p_table text, p_preset text) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
BEGIN
    IF NOT is_admin() THEN
        RAISE EXCEPTION '관리자만 정책을 바꿀 수 있습니다';
    END IF;
    IF NOT suparun_is_managed(p_table) THEN
        RAISE EXCEPTION 'SupaRun 이 관리하는 테이블이 아닙니다: %', p_table;
    END IF;

    -- SupaRun 이 만든 이름만 걷어낸다. 손으로 추가한 특수 정책은 남긴다.
    EXECUTE format('DROP POLICY IF EXISTS public_read   ON %I', p_table);
    EXECUTE format('DROP POLICY IF EXISTS admin_write   ON %I', p_table);
    EXECUTE format('DROP POLICY IF EXISTS owner_read    ON %I', p_table);
    EXECUTE format('DROP POLICY IF EXISTS admin_all     ON %I', p_table);
    EXECUTE format('DROP POLICY IF EXISTS operator_read ON %I', p_table);
    EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', p_table);

    IF p_preset = 'public' THEN
        EXECUTE format('CREATE POLICY public_read ON %I FOR SELECT USING (true)', p_table);
        -- 승격 전용 환경(#50)에서는 game-admin 이라도 config 직접 쓰기가 거부된다
        EXECUTE format('CREATE POLICY admin_write ON %I FOR ALL USING (is_admin() AND NOT suparun_is_promote_only()) WITH CHECK (is_admin() AND NOT suparun_is_promote_only())', p_table);

    ELSIF p_preset = 'admin' THEN
        EXECUTE format('CREATE POLICY admin_all ON %I FOR ALL USING (is_admin()) WITH CHECK (is_admin())', p_table);
        -- 조회는 롤 보유자 전체, 쓰기는 game-admin (#24)
        EXECUTE format('CREATE POLICY operator_read ON %I FOR SELECT USING (suparun_is_operator())', p_table);

    ELSIF p_preset = 'locked' THEN
        NULL;   -- 정책 없음 = 서버(service_role)만 접근

    ELSE
        RAISE EXCEPTION '알 수 없는 프리셋: %', p_preset;
    END IF;

    -- DDL 은 데이터 트리거가 잡지 못한다. 보안 설정 변경이라 이력이 없으면 곤란하므로 직접 남긴다.
    INSERT INTO admin_audit_log
        (id, admin_id, config_type, row_id, action, before_json, after_json, created_at)
    VALUES
        (gen_random_uuid()::text, coalesce(auth.uid()::text, 'server'),
         p_table, NULL, 'policy', NULL, p_preset,
         (extract(epoch from now()) * 1000)::bigint);
END $$;
");
        }

        /// <summary>
        /// 스냅샷 / 복원 — 어드민에서 [SpecData] 를 특정 시점으로 되돌린다.
        ///
        /// 데이터를 밖으로 꺼내지 않고 **Postgres 스키마 안에서만** 옮긴다. 크기와 무관하게 빠르고
        /// plpgsql 함수가 단일 트랜잭션이라 원자적이다 — 도중에 실패하면 반쪽 스키마가 남지 않는다.
        ///
        /// 브라우저는 DDL 을 실행할 수 없으므로 RPC 로 낸다. `suparun_set_policy` 와 같은 이유이자
        /// 같은 방어 구조다: SECURITY DEFINER + search_path 고정 + is_admin() + 화이트리스트 +
        /// 식별자는 quote_ident 로만 조립(조건식 문자열은 받지 않는다).
        ///
        /// **범위는 [SpecData] 뿐이다.** [UserData] 를 되돌리는 건 플레이어 진행을 지우는 일이라
        /// 성격이 완전히 다르다. 화이트리스트를 config_types 로 못박아 이 함수로는 아예 닿지 못하게 한다.
        ///
        /// 파일명은 `_suparun_core.sql`(테이블·is_admin 이 거기서 생김) 다음이어야 한다.
        /// `core` &lt; `snapshot` 이라 이름순으로 자연히 뒤가 된다.
        /// </summary>
        /// <summary>
        /// 팀이 공유해야 하는 비밀 보관소.
        ///
        /// 왜 필요한가: PAT·DB 비밀번호·GitHub 토큰이 `ProjectSettings/SupaRunProjectSettings.json` 에
        /// 그대로 담겨 git 에 올라가 있었다. gitignore 로 빼면 이번엔 팀원이 설정을 못 받는다 —
        /// **공유와 보안이 정면으로 부딪히는 자리**였다. 로그인 뒤에서 읽게 하면 둘 다 된다.
        ///
        /// 정책은 `admin_all` 하나뿐이다. `public_read` 를 절대 붙이지 않는다 —
        /// anon 이 읽으면 계정 마스터키가 인터넷에 공개된 것과 같다.
        /// `suparun_set_policy` 로도 못 건드린다: 그쪽은 `suparun_is_managed()` 화이트리스트
        /// (config_types/table_types 에 등록된 표)로 막혀 있고 이 표는 거기 없다.
        ///
        /// 파일명은 `_suparun_core.sql`(is_admin 이 거기서 생김) 다음이어야 한다.
        /// `core` &lt; `secret` 이라 이름순으로 자연히 뒤가 된다.
        /// </summary>
        static GeneratedFile GenerateSecretMigration()
        {
            return new GeneratedFile("Generated/Migrations/_suparun_secret.sql",
@"-- ══ 공유 비밀 (자동 생성) ══════════════════════════════════════════════
-- git 에 올릴 수 없지만 팀이 공유해야 하는 값들.

CREATE TABLE IF NOT EXISTS suparun_secret (
    key        TEXT PRIMARY KEY,
    value      TEXT NOT NULL,
    updated_at BIGINT NOT NULL,
    updated_by TEXT
);

ALTER TABLE suparun_secret ENABLE ROW LEVEL SECURITY;

-- 값을 **읽는 경로를 아예 없앤다.**
-- 예전 정책은 admin_all(FOR ALL) 이라 SELECT 까지 열려 있었고, 관리자로 로그인하면 브라우저에서
-- PostgREST 로 PAT 가 그대로 읽혔다 — 관리자 계정 하나가 곧 Supabase 계정 전체였다.
-- SELECT 정책을 두지 않으면 그 경로가 닫힌다. Unity 는 PAT(service_role)로 읽으므로 영향이 없다.
--
-- DROP 을 명시하는 이유: 아래 CREATE 들이 IF NOT EXISTS 라서, 옛 admin_all 이 남아 있으면
-- 새 정책만 추가되고 읽기는 계속 열린 채로 **조용히** 지나간다.
DROP POLICY IF EXISTS admin_all ON suparun_secret;

DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'suparun_secret' AND policyname = 'admin_insert') THEN
    CREATE POLICY admin_insert ON suparun_secret FOR INSERT WITH CHECK (is_admin());
  END IF;
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'suparun_secret' AND policyname = 'admin_update') THEN
    CREATE POLICY admin_update ON suparun_secret FOR UPDATE USING (is_admin()) WITH CHECK (is_admin());
  END IF;
END $$;

-- 목록은 **값을 뺀 채로만** 준다. 어드민 화면에 필요한 것은 '무엇이 언제 누구에 의해 채워졌나'
-- 뿐이고 값은 필요 없다. SECURITY DEFINER + 첫 줄 is_admin() 은 ADR-0004 결정 18 과 같은 형태다.
CREATE OR REPLACE FUNCTION suparun_secret_list()
RETURNS TABLE(key TEXT, updated_at BIGINT, updated_by TEXT)
LANGUAGE plpgsql SECURITY DEFINER STABLE SET search_path = public
AS $$
BEGIN
    IF NOT is_admin() THEN
        RAISE EXCEPTION '관리자만 조회할 수 있습니다';
    END IF;
    RETURN QUERY SELECT s.key, s.updated_at, s.updated_by FROM suparun_secret s ORDER BY s.key;
END $$;

-- 값 자체는 감사 로그에 남기지 않는다. 로그를 읽을 수 있으면 비밀도 읽히기 때문이다.
-- 누가 언제 바꿨는지는 updated_at/updated_by 로 충분하다.
");}

        /// <summary>
        /// 이 환경의 설정. **어드민이 쓰고 Unity 가 읽는다.**
        ///
        /// `suparun_meta` 와 나누는 이유: 그쪽은 `public_read` 라 anon key 만 있으면 누구나 읽는다.
        /// anon key 는 게임 빌드에서 추출되므로, 거기에 GCP 프로젝트·GitHub 레포를 두면 사실상 공개다.
        /// </summary>
        static GeneratedFile GenerateEnvMigration()
        {
            return new GeneratedFile("Generated/Migrations/_suparun_env.sql",
@"-- ══ 환경 설정 (자동 생성) ══════════════════════════════════════════════
-- 이 환경(= 이 Supabase 프로젝트)의 설정. 어드민이 쓰고 Unity 가 읽는다.
-- 환경마다 자기 것만 담는다 — '환경 공통' 설정을 두지 않기로 했기 때문이다.

CREATE TABLE IF NOT EXISTS suparun_env (
    key        TEXT PRIMARY KEY,
    value      TEXT NOT NULL,
    updated_at BIGINT NOT NULL,
    updated_by TEXT
);

ALTER TABLE suparun_env ENABLE ROW LEVEL SECURITY;

-- ⚠ 쓰기 정책을 **반드시** 함께 만든다.
-- suparun_meta 는 public_read(SELECT) 만 있고 쓰기 정책이 없어서, 어드민의 저장이 RLS 에 막혀
-- 조용히 실패해 왔다(실측 확인). RLS 거부는 로그를 남기지 않아 화면의 토스트 말고는 흔적이 없다.
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'suparun_env' AND policyname = 'admin_all') THEN
    CREATE POLICY admin_all ON suparun_env FOR ALL USING (is_admin()) WITH CHECK (is_admin());
  END IF;
END $$;

-- 환경 카드(현황)는 롤 보유자 전체가 본다 (#24) — 쓰기는 위 admin_all(=game-admin)만.
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'suparun_env' AND policyname = 'operator_read') THEN
    CREATE POLICY operator_read ON suparun_env FOR SELECT USING (suparun_is_operator());
  END IF;
END $$;
");}

        static GeneratedFile GenerateSnapshotMigration()
        {
            return new GeneratedFile("Generated/Migrations/_suparun_snapshot.sql",
@"-- ══ 스냅샷 / 복원 (자동 생성) ══════════════════════════════════════════
-- [SpecData] 전체를 한 시점으로 찍고 되돌린다. 본체는 snap_* 스키마, 메타는 아래 표.

-- 목록·코멘트·핀은 평범한 표다. 조회와 수정이 PostgREST 로 그냥 되므로 RPC 를 늘리지 않는다.
CREATE TABLE IF NOT EXISTS suparun_snapshot (
    schema_name     TEXT PRIMARY KEY,
    label           TEXT NOT NULL,
    comment         TEXT,
    created_by      TEXT NOT NULL,
    created_at      BIGINT NOT NULL,
    -- 출처. 불변이다 — 리스트에서 [auto] 배지로만 쓴다.
    created_by_auto BOOLEAN NOT NULL DEFAULT false,
    -- 보관 여부. 가변이다 — 핀 토글이 이것만 바꾸고, 자동 정리는 이게 false 인 것만 지운다.
    -- 출처와 나누는 이유: '자동으로 찍혔지만 남겨둘 것' 이 표현돼야 한다.
    pinned          BOOLEAN NOT NULL DEFAULT true
);

-- 개발 중 잠깐 있었던 컬럼. CREATE TABLE IF NOT EXISTS 는 이미 있는 표를 손대지 않으므로
-- 여기서 걷어내지 않으면 영원히 남는다. 다른 프로젝트에는 애초에 없어 no-op 이다.
ALTER TABLE suparun_snapshot DROP COLUMN IF EXISTS schema_json;

ALTER TABLE suparun_snapshot ENABLE ROW LEVEL SECURITY;
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'suparun_snapshot' AND policyname = 'admin_all') THEN
    CREATE POLICY admin_all ON suparun_snapshot FOR ALL USING (is_admin()) WITH CHECK (is_admin());
  END IF;
END $$;

-- 스냅샷 목록은 롤 보유자 전체가 본다 (#24) — 저장·복원 RPC 는 is_admin(=game-admin) 가드.
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'suparun_snapshot' AND policyname = 'operator_read') THEN
    CREATE POLICY operator_read ON suparun_snapshot FOR SELECT USING (suparun_is_operator());
  END IF;
END $$;

-- 자동본을 몇 개까지 남길지. 넘치면 오래된 것부터 지운다(핀이 꽂힌 것은 제외).
CREATE OR REPLACE FUNCTION suparun_snapshot_keep_count() RETURNS int
LANGUAGE sql IMMUTABLE AS $$ SELECT 5 $$;

-- ── 대상 테이블 ──
-- [SpecData] 만이다. config_types 만 보므로 [UserData] 는 이 경로로 닿을 수 없다.
CREATE OR REPLACE FUNCTION suparun_snapshot_tables() RETURNS SETOF text
LANGUAGE sql SECURITY DEFINER STABLE SET search_path = public
AS $$
  SELECT DISTINCT e->>'tableName'
  FROM suparun_meta m, jsonb_array_elements(m.value) e
  WHERE m.key = 'config_types'
    AND to_regclass('public.' || quote_ident(e->>'tableName')) IS NOT NULL
  ORDER BY 1;
$$;

-- ── 찍기 ──
-- 반환값은 만들어진 스키마명이다. 자동본일 때만 정리까지 함께 돈다.
CREATE OR REPLACE FUNCTION suparun_snapshot_create(
    p_label text, p_comment text DEFAULT NULL, p_auto boolean DEFAULT false)
RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
DECLARE
    v_slug   text;
    v_schema text;
    v_tbl    text;
    v_n      int := 0;
BEGIN
    IF NOT is_admin() THEN
        RAISE EXCEPTION '관리자만 스냅샷을 만들 수 있습니다';
    END IF;

    -- 라벨은 표시용이자 복원 확인 타이핑 대상이다. 식별자로도 쓰므로 안전한 꼴로 접는다.
    v_slug := regexp_replace(lower(coalesce(nullif(trim(p_label), ''), 'snap')), '[^a-z0-9]+', '_', 'g');
    v_slug := trim(both '_' from left(v_slug, 24));
    IF v_slug = '' THEN v_slug := 'snap'; END IF;

    -- 초까지 넣어 같은 분에 두 번 찍어도 부딪히지 않게 한다.
    v_schema := 'snap_' || v_slug || '_' || to_char(now(), 'YYYYMMDD_HH24MISS');
    IF to_regnamespace(quote_ident(v_schema)) IS NOT NULL THEN
        RAISE EXCEPTION '같은 이름의 스냅샷이 이미 있습니다: %', v_schema;
    END IF;

    EXECUTE format('CREATE SCHEMA %I', v_schema);

    FOR v_tbl IN SELECT * FROM suparun_snapshot_tables() LOOP
        EXECUTE format('CREATE TABLE %I.%I AS SELECT * FROM public.%I', v_schema, v_tbl, v_tbl);
        v_n := v_n + 1;
    END LOOP;

    IF v_n = 0 THEN
        EXECUTE format('DROP SCHEMA %I CASCADE', v_schema);
        RAISE EXCEPTION '찍을 [SpecData] 테이블이 없습니다';
    END IF;

    INSERT INTO suparun_snapshot
        (schema_name, label, comment, created_by, created_at, created_by_auto, pinned)
    VALUES
        (v_schema, coalesce(nullif(trim(p_label), ''), v_slug), nullif(trim(p_comment), ''),
         coalesce(auth.uid()::text, 'server'), (extract(epoch from now()) * 1000)::bigint,
         p_auto, NOT p_auto);

    INSERT INTO admin_audit_log
        (id, admin_id, config_type, row_id, action, before_json, after_json, created_at)
    VALUES
        (gen_random_uuid()::text, coalesce(auth.uid()::text, 'server'),
         'suparun_snapshot', v_schema, 'snapshot', NULL, v_schema,
         (extract(epoch from now()) * 1000)::bigint);

    -- 사람이 찍은 것은 건드리지 않는다. 자동본이 쌓일 때만 정리한다.
    IF p_auto THEN PERFORM suparun_snapshot_prune(); END IF;

    RETURN v_schema;
END $$;

-- ── 자동본 정리 ──
-- 핀이 꽂힌 것은 자동본이라도 남는다.
CREATE OR REPLACE FUNCTION suparun_snapshot_prune() RETURNS int
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
DECLARE
    v_row record;
    v_n   int := 0;
BEGIN
    FOR v_row IN
        SELECT schema_name FROM suparun_snapshot
        WHERE NOT pinned
        ORDER BY created_at DESC
        OFFSET suparun_snapshot_keep_count()
    LOOP
        EXECUTE format('DROP SCHEMA IF EXISTS %I CASCADE', v_row.schema_name);
        DELETE FROM suparun_snapshot WHERE schema_name = v_row.schema_name;
        v_n := v_n + 1;
    END LOOP;
    RETURN v_n;
END $$;

-- ── 지우기 ──
CREATE OR REPLACE FUNCTION suparun_snapshot_delete(p_schema text) RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
BEGIN
    IF NOT is_admin() THEN
        RAISE EXCEPTION '관리자만 스냅샷을 지울 수 있습니다';
    END IF;
    -- 우리 표에 있는 것만 지운다. 없으면 남의 스키마일 수 있다.
    IF NOT EXISTS (SELECT 1 FROM suparun_snapshot WHERE schema_name = p_schema) THEN
        RAISE EXCEPTION '없는 스냅샷입니다: %', p_schema;
    END IF;

    EXECUTE format('DROP SCHEMA IF EXISTS %I CASCADE', p_schema);
    DELETE FROM suparun_snapshot WHERE schema_name = p_schema;

    INSERT INTO admin_audit_log
        (id, admin_id, config_type, row_id, action, before_json, after_json, created_at)
    VALUES
        (gen_random_uuid()::text, coalesce(auth.uid()::text, 'server'),
         'suparun_snapshot', p_schema, 'snapshot_delete', p_schema, NULL,
         (extract(epoch from now()) * 1000)::bigint);
END $$;

-- ── 차이 ──
-- 리스트 배지와 복원 확인 화면이 같은 함수를 쓴다.
-- is_missing = 스냅샷에 없는 테이블(찍은 뒤 새로 생겼다) → 복원해도 그대로 남는다.
--
-- ⚠ 반환 컬럼 이름이 information_schema.columns 의 컬럼(table_name/table_schema/column_name)과
-- 겹치면 본문의 조회가 'column reference is ambiguous' 로 죽는다. 그래서 tbl_/_cols 로 접두·접미를 뒀다.
--
-- DROP 을 먼저 하는 이유: CREATE OR REPLACE 는 **반환 타입 구성이 바뀌면 거부**한다
-- ('cannot change return type of existing function'). 반환 컬럼 이름을 한 번 고친 적이 있어
-- 그때 실제로 막혔다. 이 함수는 반환 구조가 앞으로도 바뀔 수 있으므로 DROP 을 붙여 둔다.
DROP FUNCTION IF EXISTS suparun_snapshot_diff(text);
CREATE OR REPLACE FUNCTION suparun_snapshot_diff(p_schema text)
RETURNS TABLE(tbl_name text, cur_rows bigint, snap_rows bigint,
              added_cols text[], removed_cols text[], is_missing boolean)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
DECLARE
    v_tbl  text;
    v_snap text[];
    v_cur  text[];
BEGIN
    IF NOT is_admin() THEN
        RAISE EXCEPTION '관리자만 볼 수 있습니다';
    END IF;

    FOR v_tbl IN SELECT * FROM suparun_snapshot_tables() LOOP
        tbl_name := v_tbl;
        is_missing := to_regclass(quote_ident(p_schema) || '.' || quote_ident(v_tbl)) IS NULL;

        EXECUTE format('SELECT count(*) FROM public.%I', v_tbl) INTO cur_rows;
        IF is_missing THEN
            snap_rows := NULL;
            added_cols := NULL; removed_cols := NULL;
        ELSE
            EXECUTE format('SELECT count(*) FROM %I.%I', p_schema, v_tbl) INTO snap_rows;

            SELECT array_agg(c.column_name ORDER BY c.column_name) INTO v_cur
              FROM information_schema.columns c
             WHERE c.table_schema = 'public' AND c.table_name = v_tbl;
            SELECT array_agg(c.column_name ORDER BY c.column_name) INTO v_snap
              FROM information_schema.columns c
             WHERE c.table_schema = p_schema AND c.table_name = v_tbl;

            -- added = 찍은 뒤 생긴 컬럼(복원 시 기본값으로 남는다)
            -- removed = 그 사이 사라진 컬럼(스냅샷 값은 버려진다)
            SELECT array(SELECT unnest(coalesce(v_cur, '{}')) EXCEPT SELECT unnest(coalesce(v_snap, '{}'))) INTO added_cols;
            SELECT array(SELECT unnest(coalesce(v_snap, '{}')) EXCEPT SELECT unnest(coalesce(v_cur, '{}'))) INTO removed_cols;
        END IF;

        RETURN NEXT;
    END LOOP;
END $$;

-- ── 되돌리기 ──
-- 지금 상태를 자동본으로 한 장 찍고 나서 되돌린다. 잘못 눌러도 돌아올 자리가 있어야 한다.
-- 컬럼은 **양쪽에 다 있는 것만** 옮긴다 — SELECT * 로 하면 컬럼이 하나만 늘어도 복원이 실패한다.
-- SupaRun 의 FK 는 DB 제약이 아니라 어드민 메타라, 테이블 순서를 신경 쓸 필요가 없다.
CREATE OR REPLACE FUNCTION suparun_snapshot_restore(p_schema text) RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
DECLARE
    v_tbl    text;
    v_cols   text;
    v_backup text;
BEGIN
    IF NOT is_admin() THEN
        RAISE EXCEPTION '관리자만 복원할 수 있습니다';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM suparun_snapshot WHERE schema_name = p_schema) THEN
        RAISE EXCEPTION '없는 스냅샷입니다: %', p_schema;
    END IF;

    v_backup := suparun_snapshot_create('auto', '복원 직전 자동 저장: ' || p_schema, true);

    FOR v_tbl IN SELECT * FROM suparun_snapshot_tables() LOOP
        -- 스냅샷에 없던 테이블은 건너뛴다. 지우면 그 시점 이후 만든 것이 통째로 날아간다.
        CONTINUE WHEN to_regclass(quote_ident(p_schema) || '.' || quote_ident(v_tbl)) IS NULL;

        SELECT string_agg(quote_ident(c.column_name), ', ' ORDER BY c.column_name)
          INTO v_cols
          FROM information_schema.columns c
         WHERE c.table_schema = 'public' AND c.table_name = v_tbl
           AND EXISTS (SELECT 1 FROM information_schema.columns s
                        WHERE s.table_schema = p_schema AND s.table_name = v_tbl
                          AND s.column_name = c.column_name);

        CONTINUE WHEN v_cols IS NULL;   -- 겹치는 컬럼이 하나도 없다

        EXECUTE format('TRUNCATE public.%I', v_tbl);
        EXECUTE format('INSERT INTO public.%I (%s) SELECT %s FROM %I.%I',
                       v_tbl, v_cols, v_cols, p_schema, v_tbl);
    END LOOP;

    INSERT INTO admin_audit_log
        (id, admin_id, config_type, row_id, action, before_json, after_json, created_at)
    VALUES
        (gen_random_uuid()::text, coalesce(auth.uid()::text, 'server'),
         'suparun_snapshot', p_schema, 'snapshot_restore', v_backup, p_schema,
         (extract(epoch from now()) * 1000)::bigint);

    -- 되돌아올 자리를 화면에 알려 준다.
    RETURN v_backup;
END $$;
");
        }

        /// <summary>
        /// config 버전·게시 (ADR-0010, #30~#34).
        ///
        /// 버전의 실체는 **미게시 스냅샷**이다 — 별도 저장소를 만들지 않고 suparun_snapshot 에
        /// 버전 메타(is_version·content_hash·git_sha·게시 기록)를 얹는다. 게시는 기존 복원기
        /// (suparun_snapshot_restore — 자동 백업 포함)를 재사용하고, 활성 버전 스탬프는
        /// suparun_meta(public_read)에 둬 클라가 세션 협상에서 anon 으로 읽는다 (#35).
        ///
        /// 파일명은 `_suparun_snapshot.sql`(restore·tables 가 거기서 생김) 다음이어야 한다.
        /// `snapshot` &lt; `version` 이라 이름순으로 자연히 뒤가 된다.
        /// </summary>
        static GeneratedFile GenerateVersionMigration()
        {
            return new GeneratedFile("Generated/Migrations/_suparun_version.sql",
@"-- ══ config 버전·게시 (자동 생성, ADR-0010) ═══════════════════════════
-- 업로드 = 미게시 버전 스냅샷 생성(라이브 무영향), 게시 = 복원기로 public 에 반영.
-- 버전 ID 는 내용 해시(동일 내용 재업로드 = 같은 버전), 재현 좌표로 git SHA 를 같이 둔다.

ALTER TABLE suparun_snapshot ADD COLUMN IF NOT EXISTS is_version   BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE suparun_snapshot ADD COLUMN IF NOT EXISTS content_hash TEXT;
ALTER TABLE suparun_snapshot ADD COLUMN IF NOT EXISTS git_sha      TEXT;
ALTER TABLE suparun_snapshot ADD COLUMN IF NOT EXISTS published_at BIGINT;
ALTER TABLE suparun_snapshot ADD COLUMN IF NOT EXISTS published_by TEXT;

-- ── 업로드 ──
-- 페이로드는 세션 변수(suparun.upload_payload)로 받는다 — 인자로 받으면 SQL 크기가 두 배가 된다.
-- 테이블 구조의 기준은 **대상(public)** 이다: jsonb_populate_recordset 이 public 타입으로 펼치므로
-- 원본에만 있는 컬럼은 무시되고 대상에만 있는 컬럼은 **NULL** 이 된다(LIKE 는 DEFAULT 를
-- 안 옮기고 populate 는 명시 NULL 을 넣는다 — NOT NULL 신설 컬럼이면 업로드가 죽는 게 정직하다).
CREATE OR REPLACE FUNCTION suparun_version_upload(
    p_label text, p_content_hash text, p_git_sha text DEFAULT NULL)
RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
DECLARE
    v_schema  text;
    v_tbl     text;
    v_n       int := 0;
    v_payload jsonb;
BEGIN
    IF NOT is_admin() THEN
        RAISE EXCEPTION '관리자만 업로드할 수 있습니다';
    END IF;
    IF p_content_hash IS NULL OR length(p_content_hash) < 12 THEN
        RAISE EXCEPTION '내용 해시가 필요합니다';
    END IF;

    -- 동일 내용 재업로드 = 같은 버전. 새로 만들지 않고 기존 좌표를 돌려준다.
    SELECT schema_name INTO v_schema
      FROM suparun_snapshot WHERE is_version AND content_hash = p_content_hash;
    IF v_schema IS NOT NULL THEN
        RETURN v_schema;
    END IF;

    v_payload := current_setting('suparun.upload_payload', true)::jsonb;
    IF v_payload IS NULL THEN
        RAISE EXCEPTION '업로드 페이로드가 없습니다 (suparun.upload_payload)';
    END IF;

    v_schema := 'ver_' || left(p_content_hash, 12);
    -- 표에는 없는데 스키마만 남은 고아(중단된 업로드)는 걷어내고 새로 만든다.
    IF to_regnamespace(quote_ident(v_schema)) IS NOT NULL THEN
        EXECUTE format('DROP SCHEMA %I CASCADE', v_schema);
    END IF;
    EXECUTE format('CREATE SCHEMA %I', v_schema);

    FOR v_tbl IN SELECT * FROM suparun_snapshot_tables() LOOP
        -- CREATE TABLE AS 는 유틸리티 문이라 USING 파라미터($1)를 못 받는다 — 생성과 주입을 나눈다.
        EXECUTE format('CREATE TABLE %I.%I (LIKE public.%I)', v_schema, v_tbl, v_tbl);
        EXECUTE format(
            'INSERT INTO %I.%I SELECT * FROM jsonb_populate_recordset(null::public.%I, coalesce($1 -> %L, ''[]''::jsonb))',
            v_schema, v_tbl, v_tbl, v_tbl) USING v_payload;
        v_n := v_n + 1;
    END LOOP;

    IF v_n = 0 THEN
        EXECUTE format('DROP SCHEMA %I CASCADE', v_schema);
        RAISE EXCEPTION '담을 [SpecData] 테이블이 없습니다';
    END IF;

    INSERT INTO suparun_snapshot
        (schema_name, label, comment, created_by, created_at, created_by_auto, pinned,
         is_version, content_hash, git_sha)
    VALUES
        (v_schema, coalesce(nullif(trim(p_label), ''), left(p_content_hash, 12)), NULL,
         coalesce(auth.uid()::text, 'server'), (extract(epoch from now()) * 1000)::bigint,
         false, true, true, p_content_hash, nullif(trim(p_git_sha), ''));

    -- 행위자는 'server' 고정 — 업로드는 에디터(PAT) 경로뿐이고, RLS 통과용으로 빌린
    -- 관리자 신원(set_config claims)이 감사에 남으면 그 사람이 한 일처럼 보인다.
    INSERT INTO admin_audit_log
        (id, admin_id, config_type, row_id, action, before_json, after_json, created_at)
    VALUES
        (gen_random_uuid()::text, 'server',
         'suparun_config_version', v_schema, 'upload', NULL, p_content_hash,
         (extract(epoch from now()) * 1000)::bigint);

    RETURN v_schema;
END $$;

-- ── 게시 ──
-- 복원기 재사용: 자동 백업을 먼저 찍고 public 을 TRUNCATE+복사한다. 활성 스탬프는
-- suparun_meta(public_read) — 클라 세션 협상(#35)이 anon 으로 읽는 유일한 창구다.
-- 롤백(#34)도 이 함수다: 과거 버전을 다시 게시하면 된다. 이력은 감사 로그가 담는다.
CREATE OR REPLACE FUNCTION suparun_version_publish(p_schema text) RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
DECLARE
    v_row    suparun_snapshot%ROWTYPE;
    v_prev   text;
    v_backup text;
    v_now    bigint := (extract(epoch from now()) * 1000)::bigint;
BEGIN
    IF NOT is_admin() THEN
        RAISE EXCEPTION '관리자만 게시할 수 있습니다';
    END IF;
    SELECT * INTO v_row FROM suparun_snapshot WHERE schema_name = p_schema AND is_version;
    IF v_row.schema_name IS NULL THEN
        RAISE EXCEPTION '없는 버전입니다: %', p_schema;
    END IF;

    v_prev := (SELECT value ->> 'content_hash' FROM suparun_meta WHERE key = 'active_config_version');

    v_backup := suparun_snapshot_restore(p_schema);

    INSERT INTO suparun_meta (key, value, updated_at)
    VALUES ('active_config_version', jsonb_build_object(
                'content_hash', v_row.content_hash,
                'schema_name',  p_schema,
                'git_sha',      v_row.git_sha,
                'published_at', v_now,
                'published_by', coalesce(auth.uid()::text, 'server')), now())
    ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = EXCLUDED.updated_at;

    UPDATE suparun_snapshot SET published_at = v_now,
           published_by = coalesce(auth.uid()::text, 'server')
     WHERE schema_name = p_schema;

    INSERT INTO admin_audit_log
        (id, admin_id, config_type, row_id, action, before_json, after_json, created_at)
    VALUES
        (gen_random_uuid()::text, coalesce(auth.uid()::text, 'server'),
         'suparun_config_version', p_schema, 'publish', v_prev, v_row.content_hash, v_now);

    RETURN v_backup;
END $$;

-- ── 좌표 검증 ──
-- diff 의 양쪽 좌표는 'public'(활성본) 또는 우리 표에 있는 스냅샷 스키마만 허용한다.
-- SECURITY DEFINER 함수가 임의 스키마를 읽는 통로가 되면 안 된다.
CREATE OR REPLACE FUNCTION suparun_version_coord(p text) RETURNS text
LANGUAGE plpgsql SECURITY DEFINER STABLE SET search_path = public
AS $$
BEGIN
    IF p = 'public' THEN RETURN 'public'; END IF;
    IF EXISTS (SELECT 1 FROM suparun_snapshot WHERE schema_name = p) THEN
        RETURN quote_ident(p);
    END IF;
    RAISE EXCEPTION '알 수 없는 좌표입니다: %', p;
END $$;

-- ── diff: 테이블 단위 (#32) ──
-- 행 짝은 id(PK — [SpecData] 관례)로 맞춘다. 열람이라 operator 면 된다.
DROP FUNCTION IF EXISTS suparun_version_diff_tables(text, text);
CREATE FUNCTION suparun_version_diff_tables(p_base text, p_new text)
RETURNS TABLE(tbl_name text, added int, removed int, modified int,
              base_missing boolean, new_missing boolean)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
DECLARE
    v_tbl  text;
    v_b    text;
    v_n    text;
BEGIN
    IF NOT suparun_is_operator() THEN
        RAISE EXCEPTION '롤 보유자만 볼 수 있습니다';
    END IF;
    v_b := suparun_version_coord(p_base);
    v_n := suparun_version_coord(p_new);

    FOR v_tbl IN SELECT * FROM suparun_snapshot_tables() LOOP
        tbl_name := v_tbl;
        base_missing := to_regclass(v_b || '.' || quote_ident(v_tbl)) IS NULL;
        new_missing  := to_regclass(v_n || '.' || quote_ident(v_tbl)) IS NULL;
        added := 0; removed := 0; modified := 0;

        IF base_missing AND new_missing THEN
            NULL;
        ELSIF base_missing THEN
            EXECUTE format('SELECT count(*) FROM %s.%I', v_n, v_tbl) INTO added;
        ELSIF new_missing THEN
            EXECUTE format('SELECT count(*) FROM %s.%I', v_b, v_tbl) INTO removed;
        ELSE
            EXECUTE format(
                'SELECT count(*) FILTER (WHERE b.id IS NULL),
                        count(*) FILTER (WHERE n.id IS NULL),
                        count(*) FILTER (WHERE b.id IS NOT NULL AND n.id IS NOT NULL)
                   FROM %s.%I b FULL OUTER JOIN %s.%I n ON b.id = n.id
                  WHERE to_jsonb(b) IS DISTINCT FROM to_jsonb(n)',
                v_b, v_tbl, v_n, v_tbl)
            INTO added, removed, modified;
        END IF;

        RETURN NEXT;
    END LOOP;
END $$;

-- ── diff: 행 단위 (#33) ──
-- (릴리스 매니페스트는 _suparun_release.sql — 별 함수 의존이 없어 파일이 나뉘어도 안전하다)
DROP FUNCTION IF EXISTS suparun_version_diff_rows(text, text, text);
CREATE FUNCTION suparun_version_diff_rows(p_base text, p_new text, p_table text)
RETURNS TABLE(row_id text, status text, before_json text, after_json text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
DECLARE
    v_b text;
    v_n text;
    v_bm boolean;
    v_nm boolean;
BEGIN
    IF NOT suparun_is_operator() THEN
        RAISE EXCEPTION '롤 보유자만 볼 수 있습니다';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM suparun_snapshot_tables() t WHERE t = p_table) THEN
        RAISE EXCEPTION '대상 테이블이 아닙니다: %', p_table;
    END IF;
    v_b := suparun_version_coord(p_base);
    v_n := suparun_version_coord(p_new);
    v_bm := to_regclass(v_b || '.' || quote_ident(p_table)) IS NULL;
    v_nm := to_regclass(v_n || '.' || quote_ident(p_table)) IS NULL;

    IF v_bm AND v_nm THEN RETURN; END IF;

    IF v_bm THEN
        RETURN QUERY EXECUTE format(
            'SELECT n.id::text, ''added''::text, NULL::text, to_jsonb(n)::text FROM %s.%I n ORDER BY n.id',
            v_n, p_table);
    ELSIF v_nm THEN
        RETURN QUERY EXECUTE format(
            'SELECT b.id::text, ''removed''::text, to_jsonb(b)::text, NULL::text FROM %s.%I b ORDER BY b.id',
            v_b, p_table);
    ELSE
        RETURN QUERY EXECUTE format(
            'SELECT coalesce(b.id::text, n.id::text),
                    CASE WHEN b.id IS NULL THEN ''added''
                         WHEN n.id IS NULL THEN ''removed''
                         ELSE ''modified'' END,
                    to_jsonb(b)::text, to_jsonb(n)::text
               FROM %s.%I b FULL OUTER JOIN %s.%I n ON b.id = n.id
              WHERE to_jsonb(b) IS DISTINCT FROM to_jsonb(n)
              ORDER BY 1',
            v_b, p_table, v_n, p_table);
    END IF;
END $$;
");
        }

        /// <summary>
        /// 릴리스 매니페스트 (ADR-0010 결정 5·6, #51).
        ///
        /// 릴리스 = 무엇이 함께 나갔는가의 기록이다: logic version(클라 호환 게이트), git SHA,
        /// config 버전 해시, Cloud Run 리비전 태그, 메모, 게시 시각/행위자. 승격 오케스트레이션
        /// (ReleaseOrchestrator)이 **순차 실행 + 단계별 기록**으로 이 표를 채운다 — 교차 시스템
        /// 원자성은 주장하지 않는다(additive-only 스키마 전제).
        /// </summary>
        static GeneratedFile GenerateReleaseMigration()
        {
            return new GeneratedFile("Generated/Migrations/_suparun_release.sql",
@"-- ══ 릴리스 매니페스트 (자동 생성, ADR-0010) ═══════════════════════════
-- 한 릴리스로 무엇이 함께 나갔는가. 오케스트레이션의 단계별 성공/실패가 steps 에 쌓인다.

CREATE TABLE IF NOT EXISTS suparun_release (
    id            TEXT PRIMARY KEY,
    logic_version INT NOT NULL,
    logic_min     INT NOT NULL DEFAULT 1,
    git_sha       TEXT,
    content_hash  TEXT,
    revision_tag  TEXT,
    memo          TEXT,
    status        TEXT NOT NULL DEFAULT 'running',
    steps         JSONB NOT NULL DEFAULT '[]',
    published_at  BIGINT,
    published_by  TEXT,
    created_at    BIGINT NOT NULL,
    created_by    TEXT
);

ALTER TABLE suparun_release ENABLE ROW LEVEL SECURITY;

-- 열람만 연다(롤 보유자 전체). 쓰기 정책은 **없다** — 생성·갱신은 오케스트레이션의
-- PAT(Management API, RLS 미적용) 경로뿐이라 브라우저 쓰기 표면을 열 이유가 없다.
-- DROP+CREATE 인 이유: IF NOT EXISTS 로 만들면 식을 바꾸는 날 기존 DB 가 조용히 옛 식에 남는다.
DROP POLICY IF EXISTS operator_read ON suparun_release;
CREATE POLICY operator_read ON suparun_release FOR SELECT USING (suparun_is_operator());
DROP POLICY IF EXISTS admin_write ON suparun_release;

-- 릴리스도 감사에 남는다 — 이력의 이력이지만, 누가 릴리스 행을 고쳤는가는 다른 질문이다.
DROP TRIGGER IF EXISTS audit_suparun_release ON suparun_release;
CREATE TRIGGER audit_suparun_release
  AFTER INSERT OR UPDATE OR DELETE ON suparun_release
  FOR EACH ROW EXECUTE FUNCTION suparun_audit('id');
");
        }

        /// <summary>
        /// 플레이어 운영 계층 (③ 트랙, #36~#40) — 밴·개발자 표 + 플레이어 검색 RPC.
        /// auth.users 는 PostgREST 로 닿을 수 없어(스키마 밖) SECURITY DEFINER RPC 가 유일한 창이다 —
        /// 문은 suparun_is_operator() 로 잠근다. 밴·개발자 표의 쓰기 정책은 없다: 쓰기는 서버
        /// (직접 Postgres 연결, RLS 미적용)의 CS 액션 경로뿐이라 브라우저 표면을 열 이유가 없다.
        /// </summary>
        static GeneratedFile GeneratePlayersMigration()
        {
            return new GeneratedFile("Generated/Migrations/_suparun_players.sql",
@"-- ══ 플레이어 운영 계층 (자동 생성, ③ 트랙) ═══════════════════════════
-- 밴·개발자 지정은 게임 데이터가 아니라 **운영 상태**라 [UserData] 가 아닌 시스템 표다.

CREATE TABLE IF NOT EXISTS suparun_ban (
    user_id      TEXT PRIMARY KEY,
    reason       TEXT,
    banned_until BIGINT NOT NULL DEFAULT 0,  -- 0 = 영구
    created_at   BIGINT NOT NULL,
    created_by   TEXT
);
ALTER TABLE suparun_ban ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS operator_read ON suparun_ban;
CREATE POLICY operator_read ON suparun_ban FOR SELECT USING (suparun_is_operator());
DROP TRIGGER IF EXISTS audit_suparun_ban ON suparun_ban;
CREATE TRIGGER audit_suparun_ban
  AFTER INSERT OR UPDATE OR DELETE ON suparun_ban
  FOR EACH ROW EXECUTE FUNCTION suparun_audit('user_id');

CREATE TABLE IF NOT EXISTS suparun_developer (
    user_id    TEXT PRIMARY KEY,
    note       TEXT,
    created_at BIGINT NOT NULL,
    created_by TEXT
);
ALTER TABLE suparun_developer ENABLE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS operator_read ON suparun_developer;
CREATE POLICY operator_read ON suparun_developer FOR SELECT USING (suparun_is_operator());
DROP TRIGGER IF EXISTS audit_suparun_developer ON suparun_developer;
CREATE TRIGGER audit_suparun_developer
  AFTER INSERT OR UPDATE OR DELETE ON suparun_developer
  FOR EACH ROW EXECUTE FUNCTION suparun_audit('user_id');

-- ── 플레이어 검색 (#36) ──
-- 빈 질의 = 최근 로그인 순(Recently Active). 질의는 id 정확/접두 + email·이름 부분 일치.
DROP FUNCTION IF EXISTS suparun_player_search(text, int);
CREATE FUNCTION suparun_player_search(p_query text DEFAULT NULL, p_limit int DEFAULT 50)
RETURNS TABLE(id text, email text, name text, created_at bigint, last_sign_in_at bigint,
              banned boolean, ban_reason text, banned_until bigint, is_developer boolean)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
BEGIN
    IF NOT suparun_is_operator() THEN
        RAISE EXCEPTION '롤 보유자만 조회할 수 있습니다';
    END IF;
    RETURN QUERY
    SELECT u.id::text, u.email::text,
           COALESCE(u.raw_user_meta_data->>'name', '')::text,
           (extract(epoch from u.created_at) * 1000)::bigint,
           (extract(epoch from u.last_sign_in_at) * 1000)::bigint,
           (b.user_id IS NOT NULL AND (b.banned_until = 0
             OR b.banned_until > (extract(epoch from now()) * 1000)::bigint)),
           b.reason, b.banned_until,
           (d.user_id IS NOT NULL)
      FROM auth.users u
      LEFT JOIN suparun_ban b ON b.user_id = u.id::text
      LEFT JOIN suparun_developer d ON d.user_id = u.id::text
     WHERE p_query IS NULL OR p_query = ''
        OR u.id::text ILIKE p_query || '%'
        OR u.email ILIKE '%' || p_query || '%'
        OR (u.raw_user_meta_data->>'name') ILIKE '%' || p_query || '%'
     ORDER BY u.last_sign_in_at DESC NULLS LAST
     LIMIT LEAST(GREATEST(COALESCE(p_limit, 50), 1), 200);
END $$;

-- ── 플레이어 단건 (#37 상세) ── 없는 ID 는 0행 — 화면이 그 사실로 안내를 그린다.
DROP FUNCTION IF EXISTS suparun_player_get(text);
CREATE FUNCTION suparun_player_get(p_id text)
RETURNS TABLE(id text, email text, name text, created_at bigint, last_sign_in_at bigint,
              banned boolean, ban_reason text, banned_until bigint, is_developer boolean)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
BEGIN
    IF NOT suparun_is_operator() THEN
        RAISE EXCEPTION '롤 보유자만 조회할 수 있습니다';
    END IF;
    RETURN QUERY
    SELECT u.id::text, u.email::text,
           COALESCE(u.raw_user_meta_data->>'name', '')::text,
           (extract(epoch from u.created_at) * 1000)::bigint,
           (extract(epoch from u.last_sign_in_at) * 1000)::bigint,
           (b.user_id IS NOT NULL AND (b.banned_until = 0
             OR b.banned_until > (extract(epoch from now()) * 1000)::bigint)),
           b.reason, b.banned_until,
           (d.user_id IS NOT NULL)
      FROM auth.users u
      LEFT JOIN suparun_ban b ON b.user_id = u.id::text
      LEFT JOIN suparun_developer d ON d.user_id = u.id::text
     WHERE u.id::text = p_id;
END $$;
");
        }

        /// <summary>
        /// 타입 메타데이터를 suparun_meta 에 밀어 넣는다 (ADR-0004 결정 6·7).
        ///
        /// 마이그레이션 파일로 두는 이유: 서버 배포 시에도 최신 메타가 반영된다.
        /// 컴파일 훅(<see cref="SchemaAutoSync"/>)은 **같은 SQL** 을 Management API 로 실행해
        /// 배포 없이 갱신한다 — 두 경로가 같은 결과를 내야 하므로 생성 지점을 하나로 둔다.
        ///
        /// 여기에는 **리플렉션만으로 만들어지는 가벼운 것**만 넣는다. 아이콘/컴포넌트 맵은
        /// SpriteAtlas 를 굽고 Addressables 를 훑어야 해서 <see cref="BuildAdminAssetsMetaSql"/> 로 뺐다.
        ///
        /// 파일명은 `_suparun_core.sql` 다음으로 실행돼야 한다(테이블이 거기서 생성됨).
        /// `core` &lt; `meta` 라 이름순으로 자연히 뒤가 된다.
        /// </summary>
        public static GeneratedFile GenerateConfigMetaMigration(Type[] specTypes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- [SpecData] 타입 메타 (자동 생성) — ADR-0004");
            sb.AppendLine("-- 어드민이 이 행을 읽어 표를 그린다. 서버 _typesJson 을 대체한다.");
            sb.AppendLine("-- 컴파일 직후 반영된다 — 게임도 어드민도 DB 를 직접 보므로 서버가 낄 자리가 없다.");
            sb.AppendLine();
            AppendMetaUpsert(sb, "config_types", BuildConfigMetadataJson(specTypes));
            return new GeneratedFile("Generated/Migrations/_suparun_meta_config.sql", sb.ToString());
        }

        /// <summary>
        /// 타입 카탈로그. `[SpecData]` 메타와 같이 **컴파일 직후** 반영된다.
        ///
        /// 노드·다형 클래스를 고치는 것도 어드민 표를 고치는 것과 성질이 같다 —
        /// 쓰는 쪽이 서버가 아니라 사람(어드민)이라 배포를 기다릴 이유가 없다.
        /// 둘 다 안 쓰는 프로젝트에서는 빈 객체가 실린다.
        /// </summary>
        public static GeneratedFile GenerateTypeCatalogMigration(Type[] specTypes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- 타입 카탈로그 (자동 생성) — ADR-0002 / ADR-0005");
            sb.AppendLine("-- [NodeGraph] 캔버스의 팔레트와 [Polymorphic] 셀의 타입 목록을 이 행에서 읽는다.");
            sb.AppendLine();
            AppendMetaUpsert(sb, "type_catalog", BuildTypeCatalogJson(specTypes));
            return new GeneratedFile("Generated/Migrations/_suparun_meta_types.sql", sb.ToString());
        }

        /// <summary>
        /// [UserData] 타입 메타. **배포 때만** 반영한다.
        ///
        /// 게임이 Cloud Run 을 거치므로 서버가 최신이어야 새 필드가 동작한다.
        /// 메타만 먼저 갱신하면 어드민이 DB 에 없는 컬럼을 조회해 에러가 난다.
        /// 파일명이 `_suparun_meta_config.sql` 뒤에 오도록 `_table` 접미사를 쓴다.
        /// </summary>
        public static GeneratedFile GenerateTableMetaMigration(Type[] tableTypes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("-- [UserData] 타입 메타 (자동 생성) — ADR-0004");
            sb.AppendLine("-- 서버 재배포와 함께 반영된다. 컴파일 훅은 이 파일을 건드리지 않는다.");
            sb.AppendLine();
            AppendMetaUpsert(sb, "table_types", BuildTableMetadataJson(tableTypes));
            return new GeneratedFile("Generated/Migrations/_suparun_meta_table.sql", sb.ToString());
        }

        /// <summary>
        /// 아이콘 썸네일 + 어드레서블 주소 맵 UPSERT SQL (ADR-0004).
        ///
        /// **무겁다** — SpriteAtlas 를 열어 PNG 를 base64 로 굽고 Addressables 전체를 훑는다.
        /// 그래서 컴파일 훅이 아니라 **어드민 페이지를 여는 시점**에만 실행한다.
        /// 아이콘이 실제로 필요해지는 순간이고, 스프라이트를 안 건드린 컴파일에서 낭비하지 않는다.
        /// </summary>
        public static string BuildAdminAssetsMetaSql(Type[] specTypes)
        {
            var sb = new StringBuilder();
            AppendMetaUpsert(sb, "icons", BuildIconsJson(specTypes));
            AppendMetaUpsert(sb, "components", BuildComponentsJson(specTypes));
            return sb.ToString();
        }

        /// <summary>
        /// JSON 을 dollar-quoting 으로 감싸 UPSERT 한다.
        /// 작은따옴표 이스케이프를 신경 쓰지 않아도 되고, 메타 JSON 에 `$suparun$` 가 나올 일은 없다.
        /// </summary>
        static void AppendMetaUpsert(StringBuilder sb, string key, string json)
        {
            sb.AppendLine($"INSERT INTO suparun_meta (key, value, updated_at)");
            sb.AppendLine($"VALUES ('{key}', $suparun${json}$suparun$::jsonb, now())");
            sb.AppendLine($"ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now();");
            sb.AppendLine();
        }

        // ── admin_audit_log 마이그레이션 ──

        static GeneratedFile GenerateAdminAuditMigration()
        {
            return new GeneratedFile("Generated/Migrations/admin_audit_log.sql",
@"CREATE TABLE IF NOT EXISTS admin_audit_log (
    id TEXT PRIMARY KEY,
    admin_id TEXT NOT NULL,
    config_type TEXT NOT NULL,
    row_id TEXT,
    action TEXT NOT NULL,
    before_json TEXT,
    after_json TEXT,
    created_at BIGINT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_admin_audit_config ON admin_audit_log (config_type, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_admin_audit_admin ON admin_audit_log (admin_id, created_at DESC);

-- 이력 자체는 관리자만 읽는다. 쓰기 정책은 두지 않는다 —
-- 기록은 SECURITY DEFINER 트리거만 하고, 사람이 직접 고칠 수 있으면 감사가 아니다.
ALTER TABLE admin_audit_log ENABLE ROW LEVEL SECURITY;
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'admin_audit_log' AND policyname = 'admin_read') THEN
    CREATE POLICY admin_read ON admin_audit_log FOR SELECT USING (is_admin());
  END IF;
END $$;

-- 열람은 롤 보유자 전체 (#24 — game-viewer 도 이력을 본다). 쓰기 정책은 여전히 없다.
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'admin_audit_log' AND policyname = 'operator_read') THEN
    CREATE POLICY operator_read ON admin_audit_log FOR SELECT USING (suparun_is_operator());
  END IF;
END $$;

-- ── 열람 자기기록 (#27, ADR-0008: 변경=트리거, 열람=자기기록) ──
-- SELECT 는 트리거가 없어 열람은 화면이 스스로 기록해야 한다. 쓰기 정책을 여는 대신
-- action='viewed' 만 허용하는 좁은 문(RPC)을 둔다 — 임의 감사행 위조(사람이 고칠 수
-- 있으면 감사가 아니다)는 여전히 불가능하다. 행위자는 인자가 아니라 auth.uid() 다.
-- action 은 좁은 허용 목록이다 — 'viewed' 에 'gdpr_export'(#41: 민감 정보 접근도 열람의
-- 일종)만 더한다. 임의 action 을 열면 감사행 위조 문이 된다.
-- 시그니처가 바뀌므로 옛 2-인자 함수를 지운다 — CREATE OR REPLACE 는 인자가 다르면
-- **오버로드를 하나 더 만들 뿐**이라, 안 지우면 옛 함수가 조용히 남아 호출을 가로챈다.
DROP FUNCTION IF EXISTS suparun_audit_viewed(text, text);
CREATE OR REPLACE FUNCTION suparun_audit_viewed(p_config_type text, p_row_id text DEFAULT NULL, p_action text DEFAULT 'viewed')
RETURNS void
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
BEGIN
    IF NOT suparun_is_operator() THEN
        RAISE EXCEPTION '롤 보유자만 기록할 수 있습니다';
    END IF;
    IF p_action NOT IN ('viewed', 'gdpr_export') THEN
        RAISE EXCEPTION '허용되지 않는 action 입니다: %', p_action;
    END IF;
    INSERT INTO admin_audit_log
        (id, admin_id, config_type, row_id, action, before_json, after_json, created_at)
    VALUES
        (gen_random_uuid()::text, auth.uid()::text, p_config_type, p_row_id, p_action,
         NULL, NULL, (extract(epoch from now()) * 1000)::bigint);
END $$;
");}

        // ── AdminUser 모델 ──

        static GeneratedFile GenerateAdminUserModel()
        {
            return new GeneratedFile("Generated/AdminUser.cs",
@"public class AdminUser
{
    public string id;           // row ID (GUID)
    public string user_id;      // Supabase Auth UUID
    public string email;
    public string memo;
    public long created_at;
    public string created_by;
    // 롤은 여기 없다 — admin_user_role 매핑 테이블(복수 롤, 합집합)이다 (ADR-0009, #24)
}");
        }

        // ── admin_users 마이그레이션 ──

        static GeneratedFile GenerateAdminUserMigration()
        {
            return new GeneratedFile("Generated/Migrations/admin_users.sql",
@"CREATE TABLE IF NOT EXISTS admin_user (
    id TEXT PRIMARY KEY,
    user_id TEXT,
    email TEXT,
    memo TEXT,
    created_at BIGINT NOT NULL,
    created_by TEXT
);

-- 어느 프로바이더로 들어온 계정인가. 같은 이메일이라도 프로바이더가 다르면 Supabase 에서
-- **다른 사용자**이고 승인도 따로 받아야 한다. 이 값이 없으면 목록에 같은 이메일이 두 줄
-- 떠 있는데 왜 그런지 알 수 없다.
ALTER TABLE admin_user ADD COLUMN IF NOT EXISTS provider TEXT;

-- 이메일에는 유니크를 걸지 않는다. 같은 이메일을 쓰는 Google 계정과 GitHub 계정은
-- Supabase 에서 **서로 다른 사용자**다. 유니크를 걸면 두 번째 프로바이더로 로그인할 때
-- 등록이 조용히 실패해 승인 대기 목록에 뜨지도 않는다. 신원은 user_id 다.
DROP INDEX IF EXISTS idx_admin_user_email;
CREATE UNIQUE INDEX IF NOT EXISTS idx_admin_user_uid ON admin_user (user_id) WHERE user_id IS NOT NULL;

-- ── 롤 매핑 (ADR-0009 결정 4, #24) ──
-- 빌트인 4롤: game-admin / game-viewer / cs-senior / cs-agent. 한 유저가 복수 롤을
-- 가질 수 있고 권한은 합집합이다. 판정은 suparun_has_role() 계열(_suparun_core.sql).
-- 일반 복합 PK 라 admin_user.user_id(부분 유니크 인덱스)와 달리 ON CONFLICT 를 쓸 수 있다.
CREATE TABLE IF NOT EXISTS admin_user_role (
    user_id TEXT NOT NULL,
    role TEXT NOT NULL,
    granted_at BIGINT NOT NULL,
    granted_by TEXT,
    PRIMARY KEY (user_id, role)
);

-- 단일 role 컬럼 → 매핑 마이그레이션. 'admin' 은 game-admin 이 되고 'pending' 은
-- 무롤(=승인 대기)이 된다. 컬럼이 남아 있을 때 한 번만 돌고, 지운 뒤에는 통째로 건너뛴다.
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM information_schema.columns
             WHERE table_schema = 'public' AND table_name = 'admin_user' AND column_name = 'role') THEN
    INSERT INTO admin_user_role (user_id, role, granted_at, granted_by)
    SELECT user_id, 'game-admin', (extract(epoch from now()) * 1000)::bigint, 'migration'
      FROM admin_user WHERE role = 'admin' AND user_id IS NOT NULL
    ON CONFLICT (user_id, role) DO NOTHING;
    ALTER TABLE admin_user DROP COLUMN role;
  END IF;
END $$;

-- admin_user: 명단 관리는 game-admin(is_admin) 전용. User Roles 화면(#24)이 이 표를 그린다.
ALTER TABLE admin_user ENABLE ROW LEVEL SECURITY;
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'admin_user' AND policyname = 'admin_all') THEN
    CREATE POLICY admin_all ON admin_user FOR ALL USING (is_admin()) WITH CHECK (is_admin());
  END IF;
END $$;

-- 읽기는 롤 보유자 전체 (#25) — 감사 로그의 행위자(uid)를 이메일로 읽으려면 명단이 필요하다.
-- 행위자 식별 없는 감사는 무의미하고, 감사를 열람할 수 있는 사람에게 명단 이메일은 비밀이 아니다.
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'admin_user' AND policyname = 'operator_read') THEN
    CREATE POLICY operator_read ON admin_user FOR SELECT USING (suparun_is_operator());
  END IF;
END $$;

-- 본인 행은 읽을 수 있게 한다 — 어드민 페이지가 ""내가 승인 대기인지"" 를 보여주려면 필요하다.
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'admin_user' AND policyname = 'self_read') THEN
    CREATE POLICY self_read ON admin_user FOR SELECT USING (user_id = auth.uid()::text);
  END IF;
END $$;

-- 본인 행 등록 — 로그인한 사람이 자기 신원을 대기 명단에 올린다(App 이 로그인 직후 수행).
-- 이 행이 있어야 User Roles 화면에 대기자가 보인다. 롤은 admin_user_role 에만 있으므로
-- 이 정책으로 self-grant 는 불가능하다.
-- email 도 JWT 의 것과 같아야 한다 — 가입이 열려 있어 아무 계정이나 남의 이메일로 행을
-- 위조하면, game-admin 이 User Roles 에서 **이메일을 보고** 롤을 주는 순간 뚫린다.
-- DROP 후 CREATE: 조건이 바뀌어도 기존 DB 에 반영되게 한다(정책 교체는 순간이라 무해).
DROP POLICY IF EXISTS self_insert ON admin_user;
CREATE POLICY self_insert ON admin_user FOR INSERT
  WITH CHECK (user_id = auth.uid()::text AND email = (auth.jwt() ->> 'email'));

-- admin_user_role: 읽기 = 롤 보유자 + 본인(무롤이어도 자기 상태는 본다), 쓰기 = game-admin.
ALTER TABLE admin_user_role ENABLE ROW LEVEL SECURITY;
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'admin_user_role' AND policyname = 'operator_read') THEN
    CREATE POLICY operator_read ON admin_user_role FOR SELECT
      USING (suparun_is_operator() OR user_id = auth.uid()::text);
  END IF;
END $$;
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'admin_user_role' AND policyname = 'admin_write') THEN
    CREATE POLICY admin_write ON admin_user_role FOR ALL USING (is_admin()) WITH CHECK (is_admin());
  END IF;
END $$;

-- 롤 부여/회수도 감사에 남긴다 — 권한 변경이야말로 ""누가"" 가 중요한 이력이다.
DROP TRIGGER IF EXISTS audit_admin_user_role ON admin_user_role;
CREATE TRIGGER audit_admin_user_role
  AFTER INSERT OR UPDATE OR DELETE ON admin_user_role
  FOR EACH ROW EXECUTE FUNCTION suparun_audit('user_id');
");}

        // ── 공통 필드 메타데이터 생성 ──

        /// <summary>
        /// MemberInfo(Field/Property) 하나의 어드민 메타데이터 JSON을 생성한다.
        ///
        /// <paramref name="defaultValue"/> 는 카탈로그 항목(노드·다형 타입)에서만 넘어온다 —
        /// 코드에 적은 필드 초기값을 어드민이 새 값 만들 때 쓰라고 실어 보낸다.
        /// 표 컬럼은 DB 의 DEFAULT 가 그 역할을 하므로 넘기지 않는다.
        /// </summary>
        static string BuildMemberJson(MemberInfo member, Type memberType, object defaultValue = null)
        {
            var parts = new List<string>();

            // NodeValue<T> — "상수 또는 PureNode 출력" 이라 표시 타입은 T 를 따른다.
            var valueType = memberType;
            bool isNodeValue = memberType.IsGenericType
                && memberType.GetGenericTypeDefinition() == typeof(NodeValue<>);
            if (isNodeValue) valueType = memberType.GetGenericArguments()[0];

            parts.Add($"\"name\":\"{member.Name}\"");
            parts.Add($"\"type\":\"{GetJsType(valueType)}\"");
            if (isNodeValue) parts.Add("\"isNodeValue\":true");

            if (member.GetCustomAttribute<PrimaryKeyAttribute>() != null)
                parts.Add("\"isPrimaryKey\":true");
            if (member.GetCustomAttribute<NotNullAttribute>() != null)
                parts.Add("\"isRequired\":true");

            // NodeGraph — 이 컬럼은 텍스트가 아니라 노드 캔버스로 연다.
            // 값은 type_catalog 의 그룹 키(컨텍스트 타입명)다.
            var nodeGraph = member.GetCustomAttribute<NodeGraphAttribute>();
            if (nodeGraph?.ContextType != null)
                parts.Add($"\"nodeGraph\":\"{nodeGraph.ContextType.Name}\"");

            // Polymorphic — 타입 드롭다운 + 그 타입의 필드 폼으로 연다.
            // 값은 type_catalog 의 그룹 키(base 타입명)다.
            var polymorphic = member.GetCustomAttribute<PolymorphicAttribute>();
            if (polymorphic?.BaseType != null)
                parts.Add($"\"polymorphic\":\"{polymorphic.BaseType.Name}\"");

            // JSON 필드 판정 + jsonSchema 생성
            var jsonAttr = member.GetCustomAttribute<JsonAttribute>();
            var nameLower = member.Name.ToLower();
            if (memberType == typeof(string) &&
                (jsonAttr != null || nameLower == "rewards" || nameLower == "metadata" || nameLower.EndsWith("json")))
            {
                parts.Add("\"isJson\":true");
                if (jsonAttr?.TargetType != null)
                {
                    var schema = BuildJsonSchemaJson(jsonAttr.TargetType);
                    if (schema != null) parts.Add($"\"jsonSchema\":{schema}");
                }
            }

            // EnumType → 드롭다운
            var enumAttr = member.GetCustomAttribute<EnumTypeAttribute>();
            if (enumAttr != null && enumAttr.EnumType.IsEnum)
            {
                var names = Enum.GetNames(enumAttr.EnumType);
                parts.Add("\"isEnum\":true");
                parts.Add($"\"enumValues\":[{string.Join(",", names.Select(n => $"\"{n}\""))}]");
            }

            // ForeignKey — ReferenceType이 List<T>면 리스트 FK (TEXT 컬럼에 JSON 배열, 어드민 리스트 에디터)
            var fk = member.GetCustomAttribute<ForeignKeyAttribute>();
            if (fk != null)
            {
                var refType = fk.ReferenceType;
                if (refType.IsGenericType && refType.GetGenericTypeDefinition() == typeof(List<>))
                    parts.Add($"\"foreignKeyList\":\"{refType.GetGenericArguments()[0].Name}\"");
                else
                    parts.Add($"\"foreignKey\":\"{refType.Name}\"");
            }

            // Icon → SpriteAtlas sprite 이름 드롭다운 (썸네일). 값은 여전히 string.
            var iconAttr = member.GetCustomAttribute<IconAttribute>();
            if (iconAttr != null)
                parts.Add($"\"iconAtlas\":\"{iconAttr.AtlasKey}\"");

            // Component → 루트에 T를 가진 어드레서블 주소 검색 드롭다운. 값은 어드레서블 주소 string.
            var compAttr = member.GetCustomAttribute<ComponentAttribute>();
            if (compAttr?.ComponentType != null)
                parts.Add($"\"componentType\":\"{EscapeJson(compAttr.ComponentType.FullName)}\"");

            // VisibleIf / HiddenIf
            var visibleIf = member.GetCustomAttribute<VisibleIfAttribute>();
            if (visibleIf != null)
            {
                var vJson = $"\"field\":\"{visibleIf.ConditionField}\"";
                if (visibleIf.CompareValues.Length > 0)
                    vJson += $",\"values\":[{string.Join(",", visibleIf.CompareValues.Select(v => $"\"{v}\""))}]";
                parts.Add($"\"visibleIf\":{{{vJson}}}");
            }
            var hiddenIf = member.GetCustomAttribute<HiddenIfAttribute>();
            if (hiddenIf != null)
            {
                var hJson = $"\"field\":\"{hiddenIf.ConditionField}\"";
                if (hiddenIf.CompareValues.Length > 0)
                    hJson += $",\"values\":[{string.Join(",", hiddenIf.CompareValues.Select(v => $"\"{v}\""))}]";
                parts.Add($"\"hiddenIf\":{{{hJson}}}");
            }

            // AdminHidden — 어드민 컬럼 렌더링 시각적 숨김 (데이터는 유지)
            if (member.GetCustomAttribute<AdminHiddenAttribute>() != null)
                parts.Add("\"isHidden\":true");

            // 필드 초기값 — 0/false/"" 는 어차피 어드민 기본값이라 싣지 않는다(전송량).
            var defJson = DefaultValueJson(defaultValue);
            if (defJson != null) parts.Add($"\"default\":{defJson}");

            return "{" + string.Join(",", parts) + "}";
        }

        /// <summary>
        /// 필드 초기값을 JSON 리터럴로. 의미 없는 값(null, 0, false, 빈 문자열)은 null 을 돌려준다.
        ///
        /// FP 같은 커스텀 struct 는 어드민이 해석할 수 없으므로 제외한다 —
        /// 다형/노드 데이터는 DB 쪽(float)이 카탈로그에 실리므로 실전에서는 원시 타입만 온다.
        /// </summary>
        static string DefaultValueJson(object value)
        {
            switch (value)
            {
                case null: return null;
                case bool b: return b ? "true" : null;
                case string s: return string.IsNullOrEmpty(s) ? null : $"\"{EscapeJson(s)}\"";
                case int i: return i != 0 ? i.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                case long l: return l != 0 ? l.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
                case float f: return f != 0f ? f.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : null;
                case double d: return d != 0d ? d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : null;
                default: return null;
            }
        }

        /// <summary>타입의 public instance 필드들의 메타데이터 JSON 배열 내용을 반환한다.</summary>
        static string BuildFieldsJson(Type type)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var fieldJsons = fields.Select(f => BuildMemberJson(f, f.FieldType)).ToList();

            // [SpecData] 타입은 sort_order 메타 자동 주입 (어드민 드래그 정렬 마커)
            // 사용자 클래스에 sort_order 필드를 명시한 경우는 그쪽이 우선
            bool isConfig = type.GetCustomAttribute<SpecDataAttribute>() != null;
            if (isConfig && !fields.Any(f => f.Name == "sort_order"))
                fieldJsons.Add("{\"name\":\"sort_order\",\"type\":\"number\",\"isHidden\":true,\"isSortOrder\":true}");

            return string.Join(",", fieldJsons);
        }

        /// <summary>[Json(typeof(T))]의 T 클래스에서 프로퍼티/필드 메타데이터를 생성한다.</summary>
        static string BuildJsonSchemaJson(Type jsonType)
        {
            var elementType = JsonElementType(jsonType);

            var members = new List<string>();
            foreach (var p in elementType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                members.Add(BuildMemberJson(p, p.PropertyType));
            foreach (var f in elementType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                members.Add(BuildMemberJson(f, f.FieldType));

            return members.Count > 0 ? "[" + string.Join(",", members) + "]" : null;
        }

        /// <summary>[SpecData] 타입 목록에서 메타데이터 JSON 문자열 생성.</summary>
        static string BuildConfigMetadataJson(Type[] configTypes)
        {
            var items = new List<string>();
            foreach (var type in configTypes)
            {
                var group = type.GetCustomAttribute<SpecDataAttribute>()?.Group;
                var groupPart = group != null ? $"\"group\":\"{group}\"," : "";
                items.Add("{" +
                    $"\"name\":\"{type.Name}\"," +
                    $"\"tableName\":\"{ToSnakeCase(type.Name)}\"," +
                    groupPart +
                    $"\"fields\":[{BuildFieldsJson(type)}]" +
                    "}");
            }
            return "[" + string.Join(",", items) + "]";
        }

        // ── 타입 카탈로그 ──
        // "base 타입의 파생 중 하나를 고르고 그 필드를 채운다" 를 쓰는 두 자리가 공유한다:
        //   [NodeGraph]   — 여러 개 + 연결 (캔버스)
        //   [Polymorphic] — 하나, 연결 없음 (드롭다운 + 폼)
        // 다형 필드는 사실 연결 없는 노드 하나라 생성기·역직렬화·필드 렌더러를 나눌 이유가 없다.
        // 결과 형태: {"SkillCtx":[{"type":"DamageNode","role":"action","fields":[...],"outs":[...]}], ...}

        /// <summary>
        /// `[NodeGraph]`/`[Polymorphic]` 컬럼이 지목한 base 별로 파생 타입을 모은다.
        ///
        /// 그룹 키는 어드민이 컬럼 메타(`nodeGraph`/`polymorphic`)로 찾아오는 이름이다 —
        /// 노드는 컨텍스트 타입명, 다형 필드는 base 타입명.
        /// </summary>
        static string BuildTypeCatalogJson(Type[] specTypes)
        {
            // 정렬해야 컴파일마다 "변경됨" 으로 뜨지 않는다.
            var bases = new SortedDictionary<string, Type>(StringComparer.Ordinal);
            var visited = new HashSet<Type>();

            foreach (var type in specTypes)
                CollectCatalogBases(type, bases, visited);

            if (bases.Count == 0) return "{}";

            var groups = new List<string>();
            foreach (var kv in bases)
            {
                var derived = DerivedConcreteTypes(kv.Value).Select(BuildNodeJson);
                groups.Add($"\"{kv.Key}\":[{string.Join(",", derived)}]");
            }
            return "{" + string.Join(",", groups) + "}";
        }

        static IEnumerable<Type> DerivedConcreteTypes(Type baseType)
            => UnityEditor.TypeCache.GetTypesDerivedFrom(baseType)
                .Where(n => !n.IsAbstract && !n.ContainsGenericParameters)
                .OrderBy(n => n.FullName, StringComparer.Ordinal);

        /// <summary>
        /// `[NodeGraph]` · `[Polymorphic]` base 를 **깊이 제한 없이** 모은다.
        ///
        /// 최상위 `[SpecData]` 필드만 훑으면 안 되는 이유는 base 가 어느 깊이에나 있을 수 있기 때문이다.
        ///   PerkData.activation(다형) → SummonActivationData.behavior(다형)
        ///   PerkData.activation(다형) → FieldOrbActivationData.tiers(json) → pattern(다형)
        /// 하나라도 빠지면 그 base 만 카탈로그에 없어 어드민 드롭다운이 빈 채로 뜬다.
        ///
        /// 파생 타입 안으로도 들어간다 — base 만 봐서는 파생이 무엇을 품는지 알 수 없다.
        /// </summary>
        static void CollectCatalogBases(Type owner, SortedDictionary<string, Type> bases, HashSet<Type> visited)
        {
            // 자기 자신을 품는 타입이 있으면 무한히 돈다.
            if (owner == null || !visited.Add(owner)) return;

            foreach (var f in owner.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                // 노드 그래프 — Node<TCtx> 로 닫아서 그 컨텍스트의 노드만 모은다.
                var ng = f.GetCustomAttribute<NodeGraphAttribute>();
                if (ng?.ContextType != null)
                {
                    var closed = typeof(Node<>).MakeGenericType(ng.ContextType);
                    bases[ng.ContextType.Name] = closed;
                    foreach (var node in DerivedConcreteTypes(closed))
                        CollectCatalogBases(node, bases, visited);
                }

                // 다형 필드 — base 를 그대로 쓴다.
                var poly = f.GetCustomAttribute<PolymorphicAttribute>();
                if (poly?.BaseType != null)
                {
                    bases[poly.BaseType.Name] = poly.BaseType;
                    foreach (var derived in DerivedConcreteTypes(poly.BaseType))
                        CollectCatalogBases(derived, bases, visited);
                }

                // 중첩 JSON — 그 안의 요소도 다형을 품을 수 있다.
                var json = f.GetCustomAttribute<JsonAttribute>();
                if (json?.TargetType != null)
                    CollectCatalogBases(JsonElementType(json.TargetType), bases, visited);
            }
        }

        /// <summary>`[Json(typeof(List&lt;T&gt;))]` 의 T. 리스트가 아니면 그 타입 자신.</summary>
        static Type JsonElementType(Type jsonType)
            => jsonType.IsGenericType && jsonType.GetGenericTypeDefinition() == typeof(List<>)
                ? jsonType.GetGenericArguments()[0]
                : jsonType;

        /// <summary>노드 하나의 카탈로그 항목. 포트([NodeOut])는 입력칸에서 빼 outs 로 옮긴다.</summary>
        static string BuildNodeJson(Type nodeType)
        {
            var fields = new List<string>();
            var outs = new List<string>();

            // 필드 초기값(`public float search_range = 10f;`)을 읽으려면 인스턴스가 필요하다.
            // 이게 없으면 어드민이 새 값을 만들 때 전부 0 으로 채워, 코드에 적은 기본값이 무의미해진다.
            object defaults = null;
            try { defaults = Activator.CreateInstance(nodeType); }
            catch (Exception) { /* 매개변수 없는 생성자가 없으면 기본값 없이 간다 */ }

            foreach (var f in nodeType.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var port = f.GetCustomAttribute<NodeOutAttribute>();
                if (port == null)
                {
                    fields.Add(BuildMemberJson(f, f.FieldType, defaults != null ? f.GetValue(defaults) : null));
                    continue;
                }
                // 포트는 캔버스 연결이지 사람이 채우는 칸이 아니다.
                var label = string.IsNullOrEmpty(port.Label) ? f.Name : port.Label;
                var listPart = f.FieldType.IsArray ? ",\"list\":true" : "";
                outs.Add($"{{\"name\":\"{f.Name}\",\"label\":\"{EscapeJson(label)}\"{listPart}}}");
            }

            var pureOut = PureOutTypeOf(nodeType);
            var outTypePart = pureOut != null ? $",\"outType\":\"{pureOut}\"" : "";

            return "{" +
                $"\"type\":\"{nodeType.Name}\"," +
                $"\"label\":\"{EscapeJson(NodeLabelOf(nodeType))}\"," +
                $"\"role\":\"{NodeRoleOf(nodeType)}\"" + outTypePart + "," +
                $"\"fields\":[{string.Join(",", fields)}]," +
                $"\"outs\":[{string.Join(",", outs)}]" +
                "}";
        }

        /// <summary>
        /// 캔버스가 노드를 어떻게 그릴지 정하는 역할. base 체인을 거슬러 올라가며 가장 가까운 것을 쓴다.
        /// Branch/Sequence/Loop 가 FlowNode 보다 아래라 자연히 먼저 잡힌다.
        /// </summary>
        static string NodeRoleOf(Type t)
        {
            for (var b = t.BaseType; b != null; b = b.BaseType)
            {
                if (!b.IsGenericType) continue;
                var g = b.GetGenericTypeDefinition();
                if (g == typeof(EntryNode<>)) return "entry";
                if (g == typeof(ActionNode<>)) return "action";
                if (g == typeof(BranchNode<>)) return "branch";
                if (g == typeof(SequenceNode<>)) return "sequence";
                if (g == typeof(LoopNode<>)) return "loop";
                if (g == typeof(PureNode<,>)) return "pure";
                if (g == typeof(FlowNode<>)) return "flow";
            }
            return "node";
        }

        /// <summary>PureNode&lt;TCtx,TOut&gt; 의 TOut. 입력칸에 꽂을 수 있는지 판정하는 데 쓴다.</summary>
        static string PureOutTypeOf(Type t)
        {
            for (var b = t.BaseType; b != null; b = b.BaseType)
                if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(PureNode<,>))
                    return GetJsType(b.GetGenericArguments()[1]);
            return null;
        }

        /// <summary>팔레트 표시명. `DamageNode` → `Damage`.</summary>
        static string NodeLabelOf(Type t)
            => t.Name.Length > 4 && t.Name.EndsWith("Node")
                ? t.Name.Substring(0, t.Name.Length - 4)
                : t.Name;

        // ── [Icon] 아틀라스 sprite 썸네일 추출 (에디터 codegen 시) ──
        // 결과 형태: {"Common/FieldOrb":[{"name":"Baton","thumb":"data:image/png;base64,..."}], ...}
        // 브라우저는 Unity SpriteAtlas를 못 읽으므로 여기서 base64로 구워 어드민에 넘긴다.
        // 2D sprite 에디터 API 의존을 피하려 .spriteatlasv2를 텍스트 파싱해 packable 폴더를 찾고
        // AssetDatabase만으로 sprite를 열거한다. 실패 시 해당 아틀라스는 조용히 생략(어드민 텍스트 fallback).
        static string BuildIconsJson(Type[] configTypes)
        {
            var atlasKeys = new HashSet<string>();
            foreach (var t in configTypes)
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    var icon = f.GetCustomAttribute<IconAttribute>();
                    if (icon != null && !string.IsNullOrEmpty(icon.AtlasKey))
                        atlasKeys.Add(icon.AtlasKey);
                }
            if (atlasKeys.Count == 0) return "{}";

            var entries = new List<string>();
            foreach (var key in atlasKeys)
            {
                var sprites = ExtractAtlasSprites(key);
                if (sprites.Count == 0) continue;
                var items = sprites.Select(s =>
                    $"{{\"name\":\"{EscapeJson(s.name)}\",\"thumb\":\"data:image/png;base64,{s.base64}\"}}");
                entries.Add($"\"{EscapeJson(key)}\":[{string.Join(",", items)}]");
            }
            return "{" + string.Join(",", entries) + "}";
        }

        static List<(string name, string base64)> ExtractAtlasSprites(string atlasKey)
        {
            var result = new List<(string, string)>();
            try
            {
                var shortName = atlasKey.Contains("/")
                    ? atlasKey.Substring(atlasKey.LastIndexOf('/') + 1) : atlasKey;

                // .spriteatlasv2 에셋 경로 찾기 (이름 일치)
                string atlasPath = null;
                foreach (var g in UnityEditor.AssetDatabase.FindAssets(shortName))
                {
                    var p = UnityEditor.AssetDatabase.GUIDToAssetPath(g);
                    if (p.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase)
                        && System.IO.Path.GetFileNameWithoutExtension(p) == shortName)
                    { atlasPath = p; break; }
                }
                if (atlasPath == null)
                {
                    UnityEngine.Debug.LogWarning($"[SupaRun:Icon] SpriteAtlas '{atlasKey}' (name '{shortName}') 못 찾음 — 썸네일 생략");
                    return result;
                }

                // packables GUID 파싱 → 폴더/스프라이트 경로 수집
                var yaml = System.IO.File.ReadAllText(atlasPath);
                var spritePaths = new HashSet<string>();
                foreach (System.Text.RegularExpressions.Match m in
                    System.Text.RegularExpressions.Regex.Matches(yaml, @"guid:\s*([0-9a-fA-F]{32})"))
                {
                    var refPath = UnityEditor.AssetDatabase.GUIDToAssetPath(m.Groups[1].Value);
                    if (string.IsNullOrEmpty(refPath)) continue;
                    if (UnityEditor.AssetDatabase.IsValidFolder(refPath))
                        foreach (var sg in UnityEditor.AssetDatabase.FindAssets("t:Sprite", new[] { refPath }))
                            spritePaths.Add(UnityEditor.AssetDatabase.GUIDToAssetPath(sg));
                    else if (refPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        spritePaths.Add(refPath);
                }

                // 각 sprite: 이름 = Sprite 서브에셋 name, 썸네일 = 소스 PNG 바이트(단일 sprite 가정)
                foreach (var sp in spritePaths)
                {
                    string base64;
                    try { base64 = Convert.ToBase64String(System.IO.File.ReadAllBytes(sp)); }
                    catch { continue; }
                    foreach (var obj in UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath(sp))
                        if (obj is UnityEngine.Sprite sprite)
                            result.Add((sprite.name, base64));
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SupaRun:Icon] '{atlasKey}' 추출 실패: {ex.Message}");
            }
            return result;
        }

        static string EscapeJson(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ── [Component] 어드레서블 주소 추출 (에디터 codegen 시) ──
        // 결과 형태: {"UnityEngine.ParticleSystem":["Common/FxA","InGame/FxB"], ...}
        // 루트에 컴포넌트 T를 가진 어드레서블 프리팹의 주소를 모은다.
        // Addressables 패키지가 없는 프로젝트에선 #if로 제외 → 빈 맵(어드민 드롭다운 비어있음, graceful).
#if SUPARUN_ADDRESSABLES
        static string BuildComponentsJson(Type[] configTypes)
        {
            var compTypes = new Dictionary<string, Type>();   // FullName -> Type (중복 제거)
            foreach (var t in configTypes)
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    var c = f.GetCustomAttribute<ComponentAttribute>();
                    if (c?.ComponentType != null) compTypes[c.ComponentType.FullName] = c.ComponentType;
                }
            if (compTypes.Count == 0) return "{}";

            var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
            var entries = new List<string>();
            foreach (var kv in compTypes)
            {
                var addrs = ExtractAddressesWithComponent(settings, kv.Value);
                var items = addrs.Select(a => $"\"{EscapeJson(a)}\"");
                entries.Add($"\"{EscapeJson(kv.Key)}\":[{string.Join(",", items)}]");
            }
            return "{" + string.Join(",", entries) + "}";
        }

        static List<string> ExtractAddressesWithComponent(
            UnityEditor.AddressableAssets.Settings.AddressableAssetSettings settings, Type comp)
        {
            var result = new List<string>();
            if (settings == null || comp == null) return result;
            try
            {
                foreach (var group in settings.groups)
                {
                    if (group == null) continue;
                    foreach (var entry in group.entries)
                    {
                        if (entry == null || string.IsNullOrEmpty(entry.AssetPath)) continue;
                        var go = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>(entry.AssetPath);
                        if (go != null && go.GetComponent(comp) != null)
                            result.Add(entry.address);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[SupaRun:Component] '{comp.Name}' 주소 추출 실패: {ex.Message}");
            }
            result.Sort();
            return result;
        }
#else
        static string BuildComponentsJson(Type[] configTypes) => "{}";
#endif

        static string GetJsType(Type t)
        {
            if (t == typeof(string)) return "string";
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "long";
            if (t == typeof(float) || t == typeof(double)) return "number";
            if (t == typeof(bool)) return "bool";
            return "string";
        }

        // ── CS 액션 계층 (③ 트랙, #38~#42) ──

        /// <summary>플레이어 귀속 컬럼(소문자 실컬럼명). userId/playerId/hostUserId 관례를 받는다 —
        /// hostUserId(ActiveRoom)를 빼면 GDPR 삭제·리셋(#40·#42)에서 유저 UUID 가 잔존한다.</summary>
        static string PlayerColumnOf(Type type)
        {
            var f = type.GetFields(BindingFlags.Public | BindingFlags.Instance).FirstOrDefault(x =>
                x.Name == "userId" || x.Name == "user_id" ||
                x.Name == "playerId" || x.Name == "player_id" ||
                x.Name == "hostUserId" || x.Name == "host_user_id");
            return f?.Name.ToLower();
        }

        /// <summary>
        /// 세그먼트 계층 (③ 트랙 #43~#45, ADR-0011) — 조건으로 정의되는 플레이어 부분집합.
        /// 평가기는 **DB 함수가 유일한 구현**이다: 어드민(브라우저 RPC)과 게임 서버(Npgsql)가
        /// 같은 것을 부른다. 조건의 표·컬럼명은 메타(table_types)와 고정 화이트리스트에 대조하고
        /// format(%I) 로만 임베드한다 — 대조 실패는 예외다(조용한 무시 금지).
        /// </summary>
        static GeneratedFile GenerateSegmentsMigration()
        {
            return new GeneratedFile("Generated/Migrations/_suparun_segments.sql",
@"-- ══ 세그먼트 (자동 생성, ADR-0011) ═══════════════════════════════════
-- 조건 모델: 술어 목록 + any/all (중첩 없음). 평가는 요청 시 SQL — 사전 계산 없음.

CREATE TABLE IF NOT EXISTS suparun_segment (
    id          TEXT PRIMARY KEY,
    name        TEXT NOT NULL,
    description TEXT,
    match       TEXT NOT NULL DEFAULT 'all',   -- 'all' | 'any'
    conditions  JSONB NOT NULL DEFAULT '[]',
    created_at  BIGINT NOT NULL,
    created_by  TEXT,
    updated_at  BIGINT,
    updated_by  TEXT
);
ALTER TABLE suparun_segment ENABLE ROW LEVEL SECURITY;
-- 세그먼트는 config 가 아니라 **운영 조작**이다(밴과 같은 부류) — 승격 전용 잠금(#50) 밖.
DROP POLICY IF EXISTS operator_read ON suparun_segment;
CREATE POLICY operator_read ON suparun_segment FOR SELECT USING (suparun_is_operator());
DROP POLICY IF EXISTS admin_write ON suparun_segment;
CREATE POLICY admin_write ON suparun_segment FOR ALL USING (is_admin()) WITH CHECK (is_admin());
DROP TRIGGER IF EXISTS audit_suparun_segment ON suparun_segment;
CREATE TRIGGER audit_suparun_segment
  AFTER INSERT OR UPDATE OR DELETE ON suparun_segment
  FOR EACH ROW EXECUTE FUNCTION suparun_audit('id');

-- ── 술어 1개 평가 ──
-- 내부 헬퍼지만 PostgREST 에 노출되므로 같은 가드를 건다.
DROP FUNCTION IF EXISTS suparun_segment_cond_match(text, jsonb);
CREATE FUNCTION suparun_segment_cond_match(p_player_id text, cond jsonb)
RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
DECLARE
    src   text := cond->>'source';
    col   text := lower(coalesce(cond->>'column', ''));
    op    text := coalesce(cond->>'op', '=');
    agg   text := coalesce(cond->>'agg', '');
    tbl   text := lower(coalesce(cond->>'table', ''));
    val   jsonb := cond->'value';
    pcol  text;
    cols  text[];
    fkey  text;
    fval  text;
    fsql  text := '';
    q     text;
    num   numeric;
    ok    boolean;
BEGIN
    IF NOT suparun_is_operator() THEN
        RAISE EXCEPTION '롤 보유자만 평가할 수 있습니다';
    END IF;

    IF src = 'account' THEN
        -- 화이트리스트: 계정 시각 2종. since_days = 최근 N일 안에 발생.
        IF col NOT IN ('created_at', 'last_sign_in_at') THEN
            RAISE EXCEPTION '허용되지 않는 account 컬럼: %', col;
        END IF;
        EXECUTE format(
            'SELECT extract(epoch from %I)::numeric FROM auth.users WHERE id::text = $1', col)
            INTO num USING p_player_id;
        IF num IS NULL THEN RETURN false; END IF;
        IF op = 'since_days' THEN
            RETURN num >= extract(epoch from now() - ((val #>> '{}') || ' days')::interval);
        END IF;
        RETURN suparun_segment_cmp(num, op, (val #>> '{}')::numeric);

    ELSIF src = 'system' THEN
        IF col = 'is_developer' THEN
            ok := EXISTS (SELECT 1 FROM suparun_developer d WHERE d.user_id = p_player_id);
        ELSIF col = 'banned' THEN
            ok := EXISTS (SELECT 1 FROM suparun_ban b WHERE b.user_id = p_player_id
                AND (b.banned_until = 0 OR b.banned_until > (extract(epoch from now()) * 1000)::bigint));
        ELSE
            RAISE EXCEPTION '허용되지 않는 system 컬럼: %', col;
        END IF;
        RETURN ok = ((val #>> '{}')::boolean);

    ELSIF src = 'table' THEN
        -- 표·컬럼의 진실은 메타(table_types) — playerColumn 이 있는 표만, 컬럼도 그 표의 것만.
        SELECT e->>'playerColumn',
               array_agg(lower(f->>'name'))
          INTO pcol, cols
          FROM suparun_meta m,
               jsonb_array_elements(m.value::jsonb) e
               LEFT JOIN LATERAL jsonb_array_elements(e->'fields') f ON true
         WHERE m.key = 'table_types' AND e->>'tableName' = tbl
           AND e->>'playerColumn' IS NOT NULL
         GROUP BY 1;
        IF pcol IS NULL THEN
            RAISE EXCEPTION '허용되지 않는 표: %', tbl;
        END IF;
        IF col <> '' AND NOT col = ANY(cols) THEN
            RAISE EXCEPTION '허용되지 않는 컬럼: %.%', tbl, col;
        END IF;

        -- table_filter: 컬럼=상수 동치 목록 (예: currencyid=gold). 컬럼명은 같은 화이트리스트.
        FOR fkey, fval IN SELECT k, v #>> '{}' FROM jsonb_each(coalesce(cond->'table_filter', '{}'::jsonb)) AS t(k, v)
        LOOP
            IF NOT lower(fkey) = ANY(cols) THEN
                RAISE EXCEPTION '허용되지 않는 필터 컬럼: %.%', tbl, fkey;
            END IF;
            fsql := fsql || format(' AND %I::text = %L', lower(fkey), fval);
        END LOOP;

        IF agg = '' OR op = 'exists' THEN
            q := format('SELECT count(*) FROM %I WHERE %I = $1', tbl, pcol) || fsql;
            EXECUTE q INTO num USING p_player_id;
            RETURN num > 0;
        END IF;
        IF agg NOT IN ('count', 'sum', 'max', 'min') THEN
            RAISE EXCEPTION '허용되지 않는 집계: %', agg;
        END IF;
        IF agg = 'count' THEN
            q := format('SELECT count(*)::numeric FROM %I WHERE %I = $1', tbl, pcol) || fsql;
        ELSE
            q := format('SELECT %s(%I)::numeric FROM %I WHERE %I = $1', agg, col, tbl, pcol) || fsql;
        END IF;
        EXECUTE q INTO num USING p_player_id;
        RETURN suparun_segment_cmp(coalesce(num, 0), op, (val #>> '{}')::numeric);
    END IF;

    RAISE EXCEPTION '허용되지 않는 source: %', src;
END $$;

DROP FUNCTION IF EXISTS suparun_segment_cmp(numeric, text, numeric);
CREATE FUNCTION suparun_segment_cmp(a numeric, op text, b numeric)
RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $$
BEGIN
    CASE op
        WHEN '='  THEN RETURN a = b;
        WHEN '!=' THEN RETURN a <> b;
        WHEN '>=' THEN RETURN a >= b;
        WHEN '<=' THEN RETURN a <= b;
        ELSE RAISE EXCEPTION '허용되지 않는 연산자: %', op;
    END CASE;
END $$;

-- ── 판정 (#45) ── 빈 조건: all=전원 참, any=전원 거짓 (합집합의 항등원).
DROP FUNCTION IF EXISTS suparun_segment_match(text, text);
CREATE FUNCTION suparun_segment_match(p_segment_id text, p_player_id text)
RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
DECLARE
    seg  record;
    cond jsonb;
    hit  boolean;
BEGIN
    IF NOT suparun_is_operator() THEN
        RAISE EXCEPTION '롤 보유자만 평가할 수 있습니다';
    END IF;
    SELECT match, conditions INTO seg FROM suparun_segment WHERE id = p_segment_id;
    IF NOT FOUND THEN RAISE EXCEPTION '없는 세그먼트: %', p_segment_id; END IF;
    FOR cond IN SELECT jsonb_array_elements(seg.conditions)
    LOOP
        hit := suparun_segment_cond_match(p_player_id, cond);
        IF seg.match = 'any' AND hit THEN RETURN true; END IF;
        IF seg.match <> 'any' AND NOT hit THEN RETURN false; END IF;
    END LOOP;
    RETURN seg.match <> 'any';
END $$;

-- ── 대상 수 미리보기 ── 전수 평가다 — 규모가 커지면 여기만 주기 스냅샷으로 바꾼다 (ADR-0011).
DROP FUNCTION IF EXISTS suparun_segment_count(text);
CREATE FUNCTION suparun_segment_count(p_segment_id text)
RETURNS bigint
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
DECLARE n bigint;
BEGIN
    IF NOT suparun_is_operator() THEN
        RAISE EXCEPTION '롤 보유자만 평가할 수 있습니다';
    END IF;
    SELECT count(*) INTO n FROM auth.users u
     WHERE suparun_segment_match(p_segment_id, u.id::text);
    RETURN n;
END $$;

-- ── 소속 목록 (플레이어 상세) ──
DROP FUNCTION IF EXISTS suparun_segments_of(text);
CREATE FUNCTION suparun_segments_of(p_player_id text)
RETURNS TABLE(segment_id text, name text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path = public
AS $$
BEGIN
    IF NOT suparun_is_operator() THEN
        RAISE EXCEPTION '롤 보유자만 평가할 수 있습니다';
    END IF;
    RETURN QUERY
    SELECT s.id, s.name FROM suparun_segment s
     WHERE suparun_segment_match(s.id, p_player_id);
END $$;
");
        }

        /// <summary>
        /// CS 액션 공통 게이트 — 롤 검증 + 감사 기록. JWT 롤 클레임이 아니라 admin_user_role
        /// 표를 매 호출 조회한다: 롤의 진실은 표이고, 회수가 토큰 만료를 기다리면 안 된다.
        /// </summary>
        static GeneratedFile GenerateCsGate()
        {
            return new GeneratedFile("Generated/CsGate.cs",
@"using System.Threading.Tasks;
using Npgsql;

/// <summary>CS 액션 공통 게이트 (자동 생성, ③ 트랙) — 롤 검증 + 감사 기록.</summary>
public static class CsGate
{
    /// <summary>cs 계열 롤 보유 여부. seniorOnly 면 cs-agent 는 제외된다.</summary>
    public static async Task<bool> Allowed(NpgsqlConnection conn, string sub, bool seniorOnly)
    {
        if (string.IsNullOrEmpty(sub)) return false;
        var roles = seniorOnly ? ""('game-admin','cs-senior')"" : ""('game-admin','cs-senior','cs-agent')"";
        await using var cmd = new NpgsqlCommand(
            $""SELECT count(*) FROM admin_user_role WHERE user_id = @uid AND role IN {roles}"", conn);
        cmd.Parameters.AddWithValue(""uid"", sub);
        var n = (long)await cmd.ExecuteScalarAsync();
        return n > 0;
    }

    /// <summary>실행 감사. 서버 직접 연결이라 RLS·트리거 밖 — 여기서 직접 남긴다.
    /// tx 를 주면 액션과 원자다 — 감사 실패 = 전체 롤백(기록 없는 실행을 만들지 않는다).</summary>
    public static async Task Audit(NpgsqlConnection conn, string sub, string action, string rowId, string paramsJson,
        NpgsqlTransaction tx = null)
    {
        await using var cmd = new NpgsqlCommand(
            ""INSERT INTO admin_audit_log (id, admin_id, config_type, row_id, action, before_json, after_json, created_at) "" +
            ""VALUES (gen_random_uuid()::text, @sub, 'player', @row, @act, NULL, @json, (extract(epoch from now()) * 1000)::bigint)"", conn, tx);
        cmd.Parameters.AddWithValue(""sub"", (object)sub ?? System.DBNull.Value);
        cmd.Parameters.AddWithValue(""row"", (object)rowId ?? System.DBNull.Value);
        cmd.Parameters.AddWithValue(""act"", action);
        cmd.Parameters.AddWithValue(""json"", (object)paramsJson ?? System.DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }
}
");
        }

        /// <summary>
        /// 시스템 CS 액션 컨트롤러 — 밴·이름 변경·개발자 지정·리셋·GDPR 삭제 (#39·#40·#42)
        /// + 클라 밴 확인(ban-check). 게임 도메인이 아니라 **계정·운영 상태**를 만지므로
        /// 게임 [Service] 가 아니라 패키지가 직접 생성한다. 리셋·삭제의 [UserData] 표 목록은
        /// 생성 시점의 타입 스캔에서 온다 — 표가 늘면 재배포로 따라온다.
        /// </summary>
        static GeneratedFile GenerateCsSystemController(Type[] tableTypes)
        {
            // 플레이어 귀속 표들 — 리셋(#40)·GDPR 삭제(#42)가 지울 대상.
            var playerTables = tableTypes
                .Select(t => new { Table = ToSnakeCase(t.Name), Col = PlayerColumnOf(t) })
                .Where(x => x.Col != null)
                .ToList();
            var deleteLines = string.Join("\n", playerTables.Select(x =>
                $"        await Exec(\"DELETE FROM {x.Table} WHERE {x.Col} = @uid\", req.playerId, tx);"));

            var sb = new StringBuilder();
            sb.AppendLine("using System.Text.Json;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using Microsoft.AspNetCore.Authorization;");
            sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
            sb.AppendLine("using Npgsql;");
            sb.AppendLine("");
            sb.AppendLine("/// <summary>시스템 CS 액션 (자동 생성, ③ 트랙) — 밴·이름·개발자·리셋·GDPR.</summary>");
            sb.AppendLine("[ApiController]");
            sb.AppendLine("[Route(\"api\")]");
            sb.AppendLine("[Authorize]");
            sb.AppendLine("public class CsSystemController : ControllerBase");
            sb.AppendLine("{");
            sb.AppendLine("    readonly NpgsqlConnection _conn;");
            sb.AppendLine("    public CsSystemController(NpgsqlConnection conn) => _conn = conn;");
            sb.AppendLine("");
            sb.AppendLine("    string Sub => User.FindFirst(\"sub\")?.Value ?? \"\";");
            sb.AppendLine("");
            sb.AppendLine("    async Task Exec(string sql, string uid, NpgsqlTransaction tx = null)");
            sb.AppendLine("    {");
            sb.AppendLine("        await using var cmd = new NpgsqlCommand(sql, _conn, tx);");
            sb.AppendLine("        cmd.Parameters.AddWithValue(\"uid\", uid);");
            sb.AppendLine("        await cmd.ExecuteNonQueryAsync();");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    // ── 밴 확인 — 클라(SupaRunAuth.CheckBan)가 부른다. 본인 또는 cs 롤만. ──");
            sb.AppendLine("    [HttpGet(\"auth/ban-check/{userId}\")]");
            sb.AppendLine("    public async Task<IActionResult> BanCheck(string userId)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (Sub != userId && !await CsGate.Allowed(_conn, Sub, seniorOnly: false))");
            sb.AppendLine("            return StatusCode(403, new { error = \"본인 밴 상태만 확인할 수 있습니다.\" });");
            sb.AppendLine("        await using var cmd = new NpgsqlCommand(");
            sb.AppendLine("            \"SELECT reason, banned_until FROM suparun_ban WHERE user_id = @uid \" +");
            sb.AppendLine("            \"AND (banned_until = 0 OR banned_until > (extract(epoch from now()) * 1000)::bigint)\", _conn);");
            sb.AppendLine("        cmd.Parameters.AddWithValue(\"uid\", userId);");
            sb.AppendLine("        await using var r = await cmd.ExecuteReaderAsync();");
            sb.AppendLine("        if (await r.ReadAsync())");
            sb.AppendLine("            return Ok(new { banned = true, reason = r.IsDBNull(0) ? null : r.GetString(0), bannedUntil = r.GetInt64(1) });");
            sb.AppendLine("        return Ok(new { banned = false, reason = (string)null, bannedUntil = 0L });");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    // ── 밴/해제 (#39) — 액션+감사가 한 트랜잭션이다: 기록 없는 실행을 만들지 않는다. ──");
            sb.AppendLine("    [HttpPost(\"cs/system/SetBan\")]");
            sb.AppendLine("    public async Task<IActionResult> SetBan([FromBody] CsSystem_SetBanRequest req)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!await CsGate.Allowed(_conn, Sub, seniorOnly: false)) return StatusCode(403, new { error = \"CS 롤이 필요합니다.\" });");
            sb.AppendLine("        await using var tx = await _conn.BeginTransactionAsync();");
            sb.AppendLine("        if (req.banned)");
            sb.AppendLine("        {");
            sb.AppendLine("            await using var cmd = new NpgsqlCommand(");
            sb.AppendLine("                \"INSERT INTO suparun_ban (user_id, reason, banned_until, created_at, created_by) \" +");
            sb.AppendLine("                \"VALUES (@uid, @reason, @until, (extract(epoch from now()) * 1000)::bigint, @by) \" +");
            sb.AppendLine("                \"ON CONFLICT (user_id) DO UPDATE SET reason = @reason, banned_until = @until, created_by = @by\", _conn, tx);");
            sb.AppendLine("            cmd.Parameters.AddWithValue(\"uid\", req.playerId);");
            sb.AppendLine("            cmd.Parameters.AddWithValue(\"reason\", (object)req.reason ?? System.DBNull.Value);");
            sb.AppendLine("            cmd.Parameters.AddWithValue(\"until\", req.bannedUntil);");
            sb.AppendLine("            cmd.Parameters.AddWithValue(\"by\", Sub);");
            sb.AppendLine("            await cmd.ExecuteNonQueryAsync();");
            sb.AppendLine("        }");
            sb.AppendLine("        else await Exec(\"DELETE FROM suparun_ban WHERE user_id = @uid\", req.playerId, tx);");
            sb.AppendLine("        await CsGate.Audit(_conn, Sub, \"cs:SetBan\", req.playerId, JsonSerializer.Serialize(req), tx);");
            sb.AppendLine("        await tx.CommitAsync();");
            sb.AppendLine("        return Ok(new { ok = true });");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    // ── 이름 변경 (#39) — 이름의 진실은 auth 메타(raw_user_meta_data.name)다. ──");
            sb.AppendLine("    [HttpPost(\"cs/system/Rename\")]");
            sb.AppendLine("    public async Task<IActionResult> Rename([FromBody] CsSystem_RenameRequest req)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!await CsGate.Allowed(_conn, Sub, seniorOnly: false)) return StatusCode(403, new { error = \"CS 롤이 필요합니다.\" });");
            sb.AppendLine("        await using var tx = await _conn.BeginTransactionAsync();");
            sb.AppendLine("        await using var cmd = new NpgsqlCommand(");
            sb.AppendLine("            \"UPDATE auth.users SET raw_user_meta_data = \" +");
            sb.AppendLine("            \"jsonb_set(coalesce(raw_user_meta_data, '{}'::jsonb), '{name}', to_jsonb(@name::text)) \" +");
            sb.AppendLine("            \"WHERE id = @uid::uuid\", _conn, tx);");
            sb.AppendLine("        cmd.Parameters.AddWithValue(\"uid\", req.playerId);");
            sb.AppendLine("        cmd.Parameters.AddWithValue(\"name\", req.name ?? \"\");");
            sb.AppendLine("        var n = await cmd.ExecuteNonQueryAsync();");
            sb.AppendLine("        if (n == 0) return NotFound(new { error = \"없는 플레이어입니다.\" });");
            sb.AppendLine("        await CsGate.Audit(_conn, Sub, \"cs:Rename\", req.playerId, JsonSerializer.Serialize(req), tx);");
            sb.AppendLine("        await tx.CommitAsync();");
            sb.AppendLine("        return Ok(new { ok = true });");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    // ── 개발자 지정/해제 (#40) ──");
            sb.AppendLine("    [HttpPost(\"cs/system/SetDeveloper\")]");
            sb.AppendLine("    public async Task<IActionResult> SetDeveloper([FromBody] CsSystem_SetDeveloperRequest req)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!await CsGate.Allowed(_conn, Sub, seniorOnly: false)) return StatusCode(403, new { error = \"CS 롤이 필요합니다.\" });");
            sb.AppendLine("        await using var tx = await _conn.BeginTransactionAsync();");
            sb.AppendLine("        if (req.isDeveloper)");
            sb.AppendLine("        {");
            sb.AppendLine("            await using var cmd = new NpgsqlCommand(");
            sb.AppendLine("                \"INSERT INTO suparun_developer (user_id, note, created_at, created_by) \" +");
            sb.AppendLine("                \"VALUES (@uid, @note, (extract(epoch from now()) * 1000)::bigint, @by) \" +");
            sb.AppendLine("                \"ON CONFLICT (user_id) DO UPDATE SET note = @note, created_by = @by\", _conn, tx);");
            sb.AppendLine("            cmd.Parameters.AddWithValue(\"uid\", req.playerId);");
            sb.AppendLine("            cmd.Parameters.AddWithValue(\"note\", (object)req.note ?? System.DBNull.Value);");
            sb.AppendLine("            cmd.Parameters.AddWithValue(\"by\", Sub);");
            sb.AppendLine("            await cmd.ExecuteNonQueryAsync();");
            sb.AppendLine("        }");
            sb.AppendLine("        else await Exec(\"DELETE FROM suparun_developer WHERE user_id = @uid\", req.playerId, tx);");
            sb.AppendLine("        await CsGate.Audit(_conn, Sub, \"cs:SetDeveloper\", req.playerId, JsonSerializer.Serialize(req), tx);");
            sb.AppendLine("        await tx.CommitAsync();");
            sb.AppendLine("        return Ok(new { ok = true });");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    // ── 리셋 (#40) — [UserData] 만 지운다. 계정·밴·개발자 지정은 남는다.");
            sb.AppendLine("    // 표별 DELETE 전체가 한 트랜잭션이다 — 중간 실패 = 부분 삭제 없이 전체 롤백. ──");
            sb.AppendLine("    [HttpPost(\"cs/system/ResetPlayer\")]");
            sb.AppendLine("    public async Task<IActionResult> ResetPlayer([FromBody] CsSystem_ResetPlayerRequest req)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!await CsGate.Allowed(_conn, Sub, seniorOnly: false)) return StatusCode(403, new { error = \"CS 롤이 필요합니다.\" });");
            sb.AppendLine("        await using var tx = await _conn.BeginTransactionAsync();");
            sb.AppendLine(deleteLines);
            sb.AppendLine("        await CsGate.Audit(_conn, Sub, \"cs:ResetPlayer\", req.playerId, null, tx);");
            sb.AppendLine("        await tx.CommitAsync();");
            sb.AppendLine("        return Ok(new { ok = true });");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    // ── GDPR 삭제 (#42) — cs-senior 이상. auth 계정까지 지운다(세션·신원 FK 연쇄).");
            sb.AppendLine("    // 2단계 확인은 서버 표면이다 — confirmPlayerId 재입력 불일치면 실행 자체가 안 된다.");
            sb.AppendLine("    // 전체가 한 트랜잭션 + 감사 포함 — 부분 삭제도, 기록 없는 삭제도 만들지 않는다. ──");
            sb.AppendLine("    [HttpPost(\"cs/system/GdprDelete\")]");
            sb.AppendLine("    public async Task<IActionResult> GdprDelete([FromBody] CsSystem_GdprDeleteRequest req)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!await CsGate.Allowed(_conn, Sub, seniorOnly: true)) return StatusCode(403, new { error = \"cs-senior 이상만 실행할 수 있습니다.\" });");
            sb.AppendLine("        if (req.confirmPlayerId != req.playerId)");
            sb.AppendLine("            return BadRequest(new { error = \"2단계 확인 불일치 — 대상 ID 를 다시 입력하세요.\" });");
            sb.AppendLine("        await using var tx = await _conn.BeginTransactionAsync();");
            sb.AppendLine(deleteLines);
            sb.AppendLine("        await Exec(\"DELETE FROM suparun_ban WHERE user_id = @uid\", req.playerId, tx);");
            sb.AppendLine("        await Exec(\"DELETE FROM suparun_developer WHERE user_id = @uid\", req.playerId, tx);");
            sb.AppendLine("        await Exec(\"DELETE FROM auth.users WHERE id = @uid::uuid\", req.playerId, tx);");
            sb.AppendLine("        await CsGate.Audit(_conn, Sub, \"cs:GdprDelete\", req.playerId, null, tx);");
            sb.AppendLine("        await tx.CommitAsync();");
            sb.AppendLine("        return Ok(new { ok = true });");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine("");
            sb.AppendLine("public class CsSystem_SetBanRequest { public string playerId { get; set; } public bool banned { get; set; } public string reason { get; set; } public long bannedUntil { get; set; } }");
            sb.AppendLine("public class CsSystem_RenameRequest { public string playerId { get; set; } public string name { get; set; } }");
            sb.AppendLine("public class CsSystem_SetDeveloperRequest { public string playerId { get; set; } public bool isDeveloper { get; set; } public string note { get; set; } }");
            sb.AppendLine("public class CsSystem_ResetPlayerRequest { public string playerId { get; set; } }");
            sb.AppendLine("public class CsSystem_GdprDeleteRequest { public string playerId { get; set; } public string confirmPlayerId { get; set; } }");

            return new GeneratedFile("Generated/Controllers/CsSystemController.cs", sb.ToString());
        }

        /// <summary>
        /// cs_actions 메타 — 어드민이 Admin Tools 버튼·모달을 자동으로 그리는 근거 (#38).
        /// 시스템 액션 5종 + 게임 [CsAction] 메서드. playerId 파라미터는 화면이 자동으로 채운다.
        /// </summary>
        static GeneratedFile GenerateCsActionsMetaMigration(Type[] logicTypes)
        {
            var items = new List<string>
            {
                // 시스템 액션 — 컨트롤러(GenerateCsSystemController)와 같은 좌표를 손으로 맞춘다.
                "{\"service\":\"system\",\"method\":\"SetBan\",\"path\":\"api/cs/system/SetBan\",\"label\":\"밴/해제\",\"seniorOnly\":false,\"dangerous\":true,\"params\":[{\"name\":\"playerId\",\"type\":\"string\"},{\"name\":\"banned\",\"type\":\"bool\"},{\"name\":\"reason\",\"type\":\"string\"},{\"name\":\"bannedUntil\",\"type\":\"number\"}]}",
                "{\"service\":\"system\",\"method\":\"Rename\",\"path\":\"api/cs/system/Rename\",\"label\":\"이름 변경\",\"seniorOnly\":false,\"dangerous\":false,\"params\":[{\"name\":\"playerId\",\"type\":\"string\"},{\"name\":\"name\",\"type\":\"string\"}]}",
                "{\"service\":\"system\",\"method\":\"SetDeveloper\",\"path\":\"api/cs/system/SetDeveloper\",\"label\":\"개발자 지정\",\"seniorOnly\":false,\"dangerous\":false,\"params\":[{\"name\":\"playerId\",\"type\":\"string\"},{\"name\":\"isDeveloper\",\"type\":\"bool\"},{\"name\":\"note\",\"type\":\"string\"}]}",
                "{\"service\":\"system\",\"method\":\"ResetPlayer\",\"path\":\"api/cs/system/ResetPlayer\",\"label\":\"플레이어 리셋\",\"seniorOnly\":false,\"dangerous\":true,\"params\":[{\"name\":\"playerId\",\"type\":\"string\"}]}",
                "{\"service\":\"system\",\"method\":\"GdprDelete\",\"path\":\"api/cs/system/GdprDelete\",\"label\":\"GDPR 계정 삭제\",\"seniorOnly\":true,\"dangerous\":true,\"params\":[{\"name\":\"playerId\",\"type\":\"string\"}]}",
            };

            foreach (var type in logicTypes)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(m => !m.IsSpecialName && m.GetCustomAttribute<CsActionAttribute>() != null);
                foreach (var m in methods)
                {
                    var cs = m.GetCustomAttribute<CsActionAttribute>();
                    var ps = string.Join(",", m.GetParameters().Select(p =>
                        $"{{\"name\":\"{p.Name}\",\"type\":\"{GetJsType(p.ParameterType)}\"}}"));
                    items.Add("{" +
                        $"\"service\":\"{ToSnakeCase(type.Name)}\",\"method\":\"{m.Name}\"," +
                        $"\"path\":\"api/{ToSnakeCase(type.Name)}/{m.Name}\"," +
                        $"\"label\":\"{(cs.Label ?? m.Name)}\"," +
                        $"\"seniorOnly\":{(cs.SeniorOnly ? "true" : "false")}," +
                        $"\"dangerous\":{(cs.Dangerous ? "true" : "false")}," +
                        $"\"params\":[{ps}]" + "}");
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("-- cs_actions 메타 (자동 생성, ③ 트랙 #38) — 어드민 Admin Tools 의 버튼 목록");
            AppendMetaUpsert(sb, "cs_actions", "[" + string.Join(",", items) + "]");
            return new GeneratedFile("Generated/Migrations/suparun_meta_cs.sql", sb.ToString());
        }

        /// <summary>[UserData] 타입 목록에서 메타데이터 JSON 문자열 생성.</summary>
        static string BuildTableMetadataJson(Type[] tableTypes)
        {
            var items = new List<string>();
            foreach (var type in tableTypes)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                var group = type.GetCustomAttribute<UserDataAttribute>()?.Group;
                var groupPart = group != null ? $"\"group\":\"{group}\"," : "";
                // 플레이어 귀속 컬럼 — 상세 화면(#37)이 이 컬럼으로 본인 행을 필터한다.
                // **소문자화된 실컬럼명**을 내보낸다(컬럼은 f.Name.ToLower() 로 만들어진다)
                // — 화면이 규약을 추측하지 않게.
                var playerCol = PlayerColumnOf(type);
                var userIdPart = playerCol != null ? $"\"playerColumn\":\"{playerCol}\"," : "";
                var item = "{" +
                    $"\"name\":\"{type.Name}\"," +
                    $"\"tableName\":\"{ToSnakeCase(type.Name)}\"," +
                    groupPart +
                    userIdPart +
                    $"\"fields\":[{BuildFieldsJson(type)}]" +
                    "}";
                items.Add(item);
            }
            return "[" + string.Join(",", items) + "]";
        }
    }

    public class GeneratedFile
    {
        public string Path;
        public string Content;
        public GeneratedFile(string path, string content) { Path = path; Content = content; }
    }
}
