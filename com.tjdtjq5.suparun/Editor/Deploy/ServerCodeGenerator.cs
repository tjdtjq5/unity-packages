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
                GenerateConfigMetaMigration(specTypes),
                GenerateTypeCatalogMigration(specTypes),
                GenerateAdminUserMigration(),
                GenerateAdminAuditMigration(),
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

            string[] attrs = { "UserData", "SpecData", "Service", "API", "Cron",
                "PrimaryKey", "ForeignKey", "Index", "Unique", "NotNull", "Default",
                "MaxLength", "Hidden", "Json", "RenamedFrom", "CreatedAt", "UpdatedAt",
                "Public", "Private" };

            foreach (var a in attrs)
            {
                if (a == "ForeignKey")
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

            sb.AppendLine("[ApiController]");
            sb.AppendLine($"[Route(\"api/{ToSnakeCase(type.Name)}\")]");
            sb.AppendLine($"public class {type.Name}Controller : ControllerBase");
            sb.AppendLine("{");

            // IGameDB는 항상 주입 (서비스 + ServerLogger 양쪽에서 사용)
            sb.AppendLine("    readonly IGameDB _db;");
            sb.AppendLine($"    public {type.Name}Controller(IGameDB db) => _db = db;");

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

            // [API] 어트리뷰트가 붙은 메서드만 엔드포인트로 생성
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && m.GetCustomAttribute<APIAttribute>() != null);

            var svcPrefix = type.Name.Replace("Service", "");

            foreach (var m in methods)
            {
                var reqName = $"{svcPrefix}_{m.Name}Request";
                var paramList = m.GetParameters().Any()
                    ? $"[FromBody] {reqName} req"
                    : "";
                var args = string.Join(", ", m.GetParameters().Select(p => $"req.{p.Name}"));

                // 접근 제어
                var authAttr = m.GetCustomAttribute<PublicAttribute>() != null
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
                foreach (var line in svcCtorLines)
                    sb.AppendLine("    " + line);
                var svcArgsStr = string.Join(", ", svcCtorArgs);
                sb.AppendLine(ctorParams.Length > 0
                    ? $"            var service = new {type.Name}({svcArgsStr});"
                    : $"            var service = new {type.Name}();");

                var isTask = typeof(System.Threading.Tasks.Task).IsAssignableFrom(m.ReturnType);
                var hasResult = m.ReturnType.IsGenericType && m.ReturnType != typeof(System.Threading.Tasks.Task);
                var isVoidReturn = m.ReturnType == typeof(void) || m.ReturnType == typeof(System.Threading.Tasks.Task);

                if (hasResult)
                {
                    sb.AppendLine(isTask
                        ? $"            var result = await service.{m.Name}({args});"
                        : $"            var result = service.{m.Name}({args});");
                    sb.AppendLine("            return Ok(result);");
                }
                else if (isVoidReturn)
                {
                    sb.AppendLine(isTask
                        ? $"            await service.{m.Name}({args});"
                        : $"            service.{m.Name}({args});");
                    sb.AppendLine("            return Ok();");
                }
                else
                {
                    // 동기 + 값 반환 (long, string 등)
                    sb.AppendLine($"            var result = service.{m.Name}({args});");
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
                .Where(m => !m.IsSpecialName && m.GetCustomAttribute<APIAttribute>() != null && m.GetParameters().Length > 0);

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
            return @"CREATE TABLE IF NOT EXISTS server_log (
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
                sb.AppendLine();
                AppendPolicy(sb, tableName, "admin_write", "FOR ALL", "is_admin()", "is_admin()");

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

-- ── 관리자 판별 ──
-- 모든 RLS 정책이 이 함수를 쓴다.
-- SECURITY DEFINER 가 필수다 — admin_user 자신에도 RLS 가 걸려 있어서, 없으면
-- 함수가 자기 참조에서 막혀 **항상 false** 가 되고 관리자가 아무것도 못 하게 된다.
-- search_path 고정: 없으면 호출자가 스키마를 바꿔치기해 가짜 admin_user 로 우회할 수 있다.
CREATE OR REPLACE FUNCTION is_admin() RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER STABLE SET search_path = public
AS $$
BEGIN
    RETURN EXISTS (
        SELECT 1 FROM admin_user
        WHERE user_id = auth.uid()::text AND role = 'admin'
    );
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
        IF v_unsafe THEN
            v_preset := 'custom';
        ELSIF v_names = ARRAY['admin_write', 'public_read'] THEN
            v_preset := 'public';
        ELSIF v_names = ARRAY['admin_all'] THEN
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
    EXECUTE format('DROP POLICY IF EXISTS public_read ON %I', p_table);
    EXECUTE format('DROP POLICY IF EXISTS admin_write ON %I', p_table);
    EXECUTE format('DROP POLICY IF EXISTS owner_read  ON %I', p_table);
    EXECUTE format('DROP POLICY IF EXISTS admin_all   ON %I', p_table);
    EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', p_table);

    IF p_preset = 'public' THEN
        EXECUTE format('CREATE POLICY public_read ON %I FOR SELECT USING (true)', p_table);
        EXECUTE format('CREATE POLICY admin_write ON %I FOR ALL USING (is_admin()) WITH CHECK (is_admin())', p_table);

    ELSIF p_preset = 'admin' THEN
        EXECUTE format('CREATE POLICY admin_all ON %I FOR ALL USING (is_admin()) WITH CHECK (is_admin())', p_table);

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
    public string role;         // ""admin"" = 접근 가능, ""pending"" = 승인 대기
    public string memo;
    public long created_at;
    public string created_by;
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
    role TEXT NOT NULL DEFAULT 'pending',
    memo TEXT,
    created_at BIGINT NOT NULL,
    created_by TEXT
);

ALTER TABLE admin_user ADD COLUMN IF NOT EXISTS role TEXT NOT NULL DEFAULT 'pending';

CREATE UNIQUE INDEX IF NOT EXISTS idx_admin_user_email ON admin_user (email) WHERE email IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS idx_admin_user_uid ON admin_user (user_id) WHERE user_id IS NOT NULL;

-- admin_user 자체: 관리자만 접근. 단 미등록 유저의 자동 등록(첫 가입자=admin)은
-- 서버가 service_role 로 하므로 RLS 를 우회한다 — 여기서 막아도 그 흐름은 그대로 동작한다.
ALTER TABLE admin_user ENABLE ROW LEVEL SECURITY;
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'admin_user' AND policyname = 'admin_all') THEN
    CREATE POLICY admin_all ON admin_user FOR ALL USING (is_admin()) WITH CHECK (is_admin());
  END IF;
END $$;

-- 본인 행은 읽을 수 있게 한다 — 어드민 페이지가 ""내가 승인 대기인지"" 를 보여주려면 필요하다.
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = 'admin_user' AND policyname = 'self_read') THEN
    CREATE POLICY self_read ON admin_user FOR SELECT USING (user_id = auth.uid()::text);
  END IF;
END $$;
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
            var elementType = jsonType;
            if (jsonType.IsGenericType && jsonType.GetGenericTypeDefinition() == typeof(List<>))
                elementType = jsonType.GetGenericArguments()[0];

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

            foreach (var type in specTypes)
                foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    // 노드 그래프 — Node<TCtx> 로 닫아서 그 컨텍스트의 노드만 모은다.
                    var ng = f.GetCustomAttribute<NodeGraphAttribute>();
                    if (ng?.ContextType != null)
                        bases[ng.ContextType.Name] = typeof(Node<>).MakeGenericType(ng.ContextType);

                    // 다형 필드 — base 를 그대로 쓴다.
                    var poly = f.GetCustomAttribute<PolymorphicAttribute>();
                    if (poly?.BaseType != null)
                        bases[poly.BaseType.Name] = poly.BaseType;
                }

            if (bases.Count == 0) return "{}";

            var groups = new List<string>();
            foreach (var kv in bases)
            {
                var derived = UnityEditor.TypeCache.GetTypesDerivedFrom(kv.Value)
                    .Where(n => !n.IsAbstract && !n.ContainsGenericParameters)
                    .OrderBy(n => n.FullName, StringComparer.Ordinal)
                    .Select(BuildNodeJson);
                groups.Add($"\"{kv.Key}\":[{string.Join(",", derived)}]");
            }
            return "{" + string.Join(",", groups) + "}";
        }

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

        /// <summary>[UserData] 타입 목록에서 메타데이터 JSON 문자열 생성.</summary>
        static string BuildTableMetadataJson(Type[] tableTypes)
        {
            var items = new List<string>();
            foreach (var type in tableTypes)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                var group = type.GetCustomAttribute<UserDataAttribute>()?.Group;
                var groupPart = group != null ? $"\"group\":\"{group}\"," : "";
                var hasUserId = fields.Any(f => f.Name == "userId" || f.Name == "user_id");
                var userIdPart = hasUserId ? "\"hasUserId\":true," : "";
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
