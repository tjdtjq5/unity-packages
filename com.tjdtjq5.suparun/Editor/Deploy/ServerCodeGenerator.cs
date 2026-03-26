using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Tjdtjq5.SupaRun.Editor
{
    public static class ServerCodeGenerator
    {
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

            // 서버 로그 시스템
            files.Add(new GeneratedFile("Generated/Migrations/server_logs.sql", GenerateServerLogsMigration()));
            files.Add(GenerateServerLogModel());
            files.Add(GenerateServerLogger());

            // [Table] → Controller + Migration
            foreach (var type in tableTypes)
            {
                files.Add(GenerateReadController(type, "table"));
                files.Add(GenerateMigration(type));
            }

            // [Config] → Controller + Migration
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

            return files;
        }

        // ── 어트리뷰트 스텁 ──

        static GeneratedFile GenerateAttributeStubs()
        {
            var sb = new StringBuilder();
            sb.AppendLine("// Auto-generated attribute stubs for server compilation");
            sb.AppendLine("namespace Tjdtjq5.SupaRun");
            sb.AppendLine("{");

            string[] attrs = { "Table", "Config", "Service", "API", "Cron",
                "PrimaryKey", "ForeignKey", "Index", "Unique", "NotNull", "Default",
                "MaxLength", "Hidden", "RenamedFrom", "CreatedAt", "UpdatedAt",
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

    // Reflection 캐시
    static readonly Dictionary<Type, System.Reflection.FieldInfo[]> _fieldCache = new();
    static System.Reflection.FieldInfo[] CachedFields(Type type)
    {
        if (!_fieldCache.TryGetValue(type, out var fields))
        {
            fields = type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            _fieldCache[type] = fields;
        }
        return fields;
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
            var table = typeof(T).Name.ToLower() + ""s"";
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
            var table = typeof(T).Name.ToLower() + ""s"";
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
            var table = type.Name.ToLower() + ""s"";
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
            var table = typeof(T).Name.ToLower() + ""s"";
            await c.ExecuteAsync($""DELETE FROM {table} WHERE id = @id"", new { id = primaryKey }, _tx);
        }
        finally { if (!IsTransaction) c.Dispose(); }
    }

    public async Task<List<T>> Query<T>(QueryOptions options)
    {
        var c = GetConn();
        try
        {
            var table = typeof(T).Name.ToLower() + ""s"";
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
            var table = typeof(T).Name.ToLower() + ""s"";
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
            var table = type.Name.ToLower() + ""s"";

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
            var table = typeof(T).Name.ToLower() + ""s"";
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
            sb.AppendLine($"[Route(\"api/{name.ToLower()}\")]");
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
            sb.AppendLine($"[Route(\"api/{type.Name}\")]");
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

        /// <summary>[Table]/[Config] 전체의 마이그레이션 SQL을 하나로 합쳐 반환.</summary>
        public static string GenerateMigrationSql()
        {
            var types = new List<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.Contains("Assembly-CSharp")) continue;
                foreach (var type in assembly.GetTypes())
                {
                    if (type.GetCustomAttribute<TableAttribute>() != null ||
                        type.GetCustomAttribute<ConfigAttribute>() != null)
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
            return @"CREATE TABLE IF NOT EXISTS serverlogs (
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

CREATE INDEX IF NOT EXISTS idx_serverlogs_level_createdat ON serverlogs (level, createdat DESC);
CREATE INDEX IF NOT EXISTS idx_serverlogs_createdat ON serverlogs (createdat DESC);
";
        }

        static GeneratedFile GenerateMigration(Type type)
        {
            var sb = new StringBuilder();
            var tableName = type.Name.ToLower() + "s";

            sb.AppendLine($"CREATE TABLE IF NOT EXISTS {tableName} (");

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var lines = new List<string>();

            foreach (var f in fields)
            {
                var col = f.Name.ToLower();
                var sqlType = GetSqlType(f);
                var constraints = GetConstraints(f);
                lines.Add($"    {col} {sqlType}{constraints}");
            }

            sb.AppendLine(string.Join(",\n", lines));
            sb.AppendLine(");");

            // [Config] 타입은 공개 읽기 RLS 정책 추가 (Supabase REST 직접 조회용)
            if (type.GetCustomAttribute<ConfigAttribute>() != null)
            {
                sb.AppendLine();
                sb.AppendLine($"ALTER TABLE {tableName} ENABLE ROW LEVEL SECURITY;");
                sb.AppendLine($"DO $$ BEGIN");
                sb.AppendLine($"  IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = '{tableName}' AND policyname = 'public_read') THEN");
                sb.AppendLine($"    CREATE POLICY public_read ON {tableName} FOR SELECT USING (true);");
                sb.AppendLine($"  END IF;");
                sb.AppendLine($"END $$;");
            }

            return new GeneratedFile($"Generated/Migrations/{tableName}.sql", sb.ToString());
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

        static string GetSqlType(FieldInfo f)
        {
            var maxLen = f.GetCustomAttribute<MaxLengthAttribute>();
            if (f.FieldType == typeof(string))
                return maxLen != null ? $"VARCHAR({maxLen.Length})" : "TEXT";
            if (f.FieldType == typeof(int)) return "INTEGER";
            if (f.FieldType == typeof(long)) return "BIGINT";
            if (f.FieldType == typeof(float)) return "REAL";
            if (f.FieldType == typeof(double)) return "DOUBLE PRECISION";
            if (f.FieldType == typeof(bool)) return "BOOLEAN";
            return "TEXT";
        }

        static string GetConstraints(FieldInfo f)
        {
            var parts = new List<string>();
            if (f.GetCustomAttribute<PrimaryKeyAttribute>() != null) parts.Add(" PRIMARY KEY");
            if (f.GetCustomAttribute<NotNullAttribute>() != null) parts.Add(" NOT NULL");
            if (f.GetCustomAttribute<UniqueAttribute>() != null) parts.Add(" UNIQUE");
            var def = f.GetCustomAttribute<DefaultAttribute>();
            if (def != null) parts.Add($" DEFAULT {def.Value}");
            return string.Join("", parts);
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
    }

    public class GeneratedFile
    {
        public string Path;
        public string Content;
        public GeneratedFile(string path, string content) { Path = path; Content = content; }
    }
}
