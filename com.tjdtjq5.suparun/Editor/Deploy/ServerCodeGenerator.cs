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

            // Admin → Config CRUD + 관리자 + 감사로그
            files.Add(GenerateAdminUserModel());
            files.Add(GenerateAdminUserMigration());
            files.Add(GenerateAdminAuditModel());
            files.Add(GenerateAdminAuditMigration());
            files.Add(GenerateAdminController(specTypes));

            // Admin Table → Table 조회 + 통계 + 크로스 검색
            if (tableTypes.Length > 0)
                files.Add(GenerateAdminTableController(tableTypes));

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
                else if (a == "Table" || a == "Config")
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

        // ── Admin Controller (제네릭 Config CRUD + Batch + Audit) ──

        static GeneratedFile GenerateAdminController(Type[] configTypes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Text.Json;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
            sb.AppendLine("");
            sb.AppendLine("[ApiController]");
            sb.AppendLine("[Route(\"admin/api/config\")]");
            sb.AppendLine("public class AdminController : ControllerBase");
            sb.AppendLine("{");
            sb.AppendLine("    readonly IGameDB _db;");
            sb.AppendLine("    public AdminController(IGameDB db) => _db = db;");
            sb.AppendLine("");
            sb.AppendLine("    static readonly JsonSerializerOptions _jsonOpts = new() { IncludeFields = true };");
            sb.AppendLine("    static readonly JsonSerializerOptions _jsonPretty = new() { IncludeFields = true, WriteIndented = true };");
            sb.AppendLine("");

            // 필수 필드 딕셔너리
            sb.AppendLine("    static readonly Dictionary<string, HashSet<string>> _requiredFields = new()");
            sb.AppendLine("    {");
            foreach (var t in configTypes)
            {
                var reqFields = t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => f.GetCustomAttribute<NotNullAttribute>() != null)
                    .Select(f => $"\"{f.Name}\"");
                if (reqFields.Any())
                    sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = new() {{ {string.Join(", ", reqFields)} }},");
            }
            sb.AppendLine("    };");
            sb.AppendLine("");

            // 메타데이터 JSON (하드코딩)
            var metaJson = BuildConfigMetadataJson(configTypes).Replace("\"", "\"\"");
            sb.AppendLine($"    static readonly string _typesJson = @\"{metaJson}\";");
            sb.AppendLine("");

            // DB operation delegates (Reflection 제거 — 타입별 직접 호출)
            sb.AppendLine("    static readonly Dictionary<string, Func<IGameDB, Task<object>>> _getAll = new()");
            sb.AppendLine("    {");
            foreach (var t in configTypes)
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = async db => await db.GetAll<{t.Name}>(),");
            sb.AppendLine("    };");
            sb.AppendLine("    static readonly Dictionary<string, Func<IGameDB, string, Task<object>>> _getOne = new()");
            sb.AppendLine("    {");
            foreach (var t in configTypes)
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = async (db, id) => await db.Get<{t.Name}>(id),");
            sb.AppendLine("    };");
            sb.AppendLine("    static readonly Dictionary<string, Func<IGameDB, object, Task>> _save = new()");
            sb.AppendLine("    {");
            foreach (var t in configTypes)
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = async (db, e) => await db.Save(({t.Name})e),");
            sb.AppendLine("    };");
            sb.AppendLine("    static readonly Dictionary<string, Func<IGameDB, object, Task>> _saveAll = new()");
            sb.AppendLine("    {");
            foreach (var t in configTypes)
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = async (db, e) => await db.SaveAll((List<{t.Name}>)e),");
            sb.AppendLine("    };");
            sb.AppendLine("    static readonly Dictionary<string, Func<IGameDB, string, Task>> _delete = new()");
            sb.AppendLine("    {");
            foreach (var t in configTypes)
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = async (db, id) => await db.Delete<{t.Name}>(id),");
            sb.AppendLine("    };");
            sb.AppendLine("    static readonly Dictionary<string, Func<IGameDB, QueryOptions, Task>> _deleteAll = new()");
            sb.AppendLine("    {");
            foreach (var t in configTypes)
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = async (db, opts) => await db.DeleteAll<{t.Name}>(opts),");
            sb.AppendLine("    };");
            sb.AppendLine("    static readonly Dictionary<string, Func<string, object>> _deserialize = new()");
            sb.AppendLine("    {");
            foreach (var t in configTypes)
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = json => JsonSerializer.Deserialize<{t.Name}>(json, _jsonOpts),");
            sb.AppendLine("    };");
            sb.AppendLine("    static readonly Dictionary<string, Func<string, System.Collections.IList>> _deserializeList = new()");
            sb.AppendLine("    {");
            foreach (var t in configTypes)
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = json => JsonSerializer.Deserialize<List<{t.Name}>>(json, _jsonOpts),");
            sb.AppendLine("    };");
            sb.AppendLine("");

            // ── 헬퍼 메서드 ──
            sb.AppendLine("    string GetAdminId() => User?.FindFirst(\"sub\")?.Value ?? \"unknown\";");
            sb.AppendLine("");

            // Validate
            sb.AppendLine("    string Validate(object entity, string typeName)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_requiredFields.TryGetValue(typeName, out var required)) return null;");
            sb.AppendLine("        foreach (var f in entity.GetType().GetFields())");
            sb.AppendLine("        {");
            sb.AppendLine("            if (!required.Contains(f.Name)) continue;");
            sb.AppendLine("            var val = f.GetValue(entity);");
            sb.AppendLine("            if (val == null || (val is string s && string.IsNullOrEmpty(s)))");
            sb.AppendLine("                return $\"{f.Name} is required\";");
            sb.AppendLine("        }");
            sb.AppendLine("        return null;");
            sb.AppendLine("    }");
            sb.AppendLine("");

            sb.AppendLine("    Task<object> GetOne(string typeName, string id) => _getOne[typeName](_db, id);");
            sb.AppendLine("    Task<object> GetAllData(string typeName) => _getAll[typeName](_db);");
            sb.AppendLine("");

            // Audit
            sb.AppendLine("    async Task Audit(string action, string configType, string rowId, object before, object after)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            await _db.Save(new AdminAuditLog");
            sb.AppendLine("            {");
            sb.AppendLine("                id = Guid.NewGuid().ToString(),");
            sb.AppendLine("                admin_id = GetAdminId(),");
            sb.AppendLine("                config_type = configType,");
            sb.AppendLine("                row_id = rowId,");
            sb.AppendLine("                action = action,");
            sb.AppendLine("                before_json = before != null ? JsonSerializer.Serialize(before, _jsonOpts) : null,");
            sb.AppendLine("                after_json = after != null ? JsonSerializer.Serialize(after, _jsonOpts) : null,");
            sb.AppendLine("                created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()");
            sb.AppendLine("            });");
            sb.AppendLine("        }");
            sb.AppendLine("        catch { }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── GET _types ──
            sb.AppendLine("    [HttpGet(\"_types\")]");
            sb.AppendLine("    public IActionResult GetTypes() => Content(_typesJson, \"application/json\");");
            sb.AppendLine("");

            // ── GET _audit ──
            sb.AppendLine("    [HttpGet(\"_audit\")]");
            sb.AppendLine("    public async Task<IActionResult> GetAuditLog([FromQuery] string type = null, [FromQuery] int limit = 50)");
            sb.AppendLine("    {");
            sb.AppendLine("        var opts = new QueryOptions().OrderByDesc(\"created_at\").SetLimit(limit);");
            sb.AppendLine("        if (type != null) opts.Eq(\"config_type\", type);");
            sb.AppendLine("        var logs = await _db.Query<AdminAuditLog>(opts);");
            sb.AppendLine("        return Ok(logs);");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── GET _export/{typeName} ──
            sb.AppendLine("    [HttpGet(\"_export/{typeName}\")]");
            sb.AppendLine("    public async Task<IActionResult> Export(string typeName)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_getAll.ContainsKey(typeName))");
            sb.AppendLine("            return NotFound(new { error = $\"Unknown config type: {typeName}\" });");
            sb.AppendLine("        var data = await GetAllData(typeName);");
            sb.AppendLine("        var json = JsonSerializer.Serialize(data, _jsonPretty);");
            sb.AppendLine("        return File(System.Text.Encoding.UTF8.GetBytes(json), \"application/json\", $\"{typeName}.json\");");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── POST _import/{typeName} (전체 교체) ──
            sb.AppendLine("    [HttpPost(\"_import/{typeName}\")]");
            sb.AppendLine("    public async Task<IActionResult> Import(string typeName, [FromBody] JsonElement body)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_getAll.ContainsKey(typeName))");
            sb.AppendLine("            return NotFound(new { error = $\"Unknown config type: {typeName}\" });");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var entities = _deserializeList[typeName](body.GetRawText());");
            sb.AppendLine("            foreach (var e in entities) { var err = Validate(e, typeName); if (err != null) return BadRequest(new { error = err }); }");
            sb.AppendLine("            var beforeData = await GetAllData(typeName);");
            sb.AppendLine("            await _db.Transaction(async tx =>");
            sb.AppendLine("            {");
            sb.AppendLine("                await _deleteAll[typeName](tx, new QueryOptions());");
            sb.AppendLine("                await _saveAll[typeName](tx, entities);");
            sb.AppendLine("            });");
            sb.AppendLine("            await Audit(\"import\", typeName, null, beforeData, entities);");
            sb.AppendLine("            return Ok(new { imported = entities.Count });");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            await ServerLogger.LogError(_db, ex.InnerException?.Message ?? ex.Message, stack: ex.InnerException?.StackTrace ?? ex.StackTrace, endpoint: $\"admin/import/{typeName}\", serviceName: \"AdminController\");");
            sb.AppendLine("            return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message });");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── POST _batch/{typeName} (추가/업서트) ──
            sb.AppendLine("    [HttpPost(\"_batch/{typeName}\")]");
            sb.AppendLine("    public async Task<IActionResult> Batch(string typeName, [FromBody] JsonElement body)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_getAll.ContainsKey(typeName))");
            sb.AppendLine("            return NotFound(new { error = $\"Unknown config type: {typeName}\" });");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var entities = _deserializeList[typeName](body.GetRawText());");
            sb.AppendLine("            foreach (var e in entities) { var err = Validate(e, typeName); if (err != null) return BadRequest(new { error = err }); }");
            sb.AppendLine("            await _saveAll[typeName](_db, entities);");
            sb.AppendLine("            await Audit(\"batch\", typeName, null, null, entities);");
            sb.AppendLine("            return Ok(new { saved = entities.Count });");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            await ServerLogger.LogError(_db, ex.InnerException?.Message ?? ex.Message, stack: ex.InnerException?.StackTrace ?? ex.StackTrace, endpoint: $\"admin/batch/{typeName}\", serviceName: \"AdminController\");");
            sb.AppendLine("            return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message });");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── GET {typeName} ──
            sb.AppendLine("    [HttpGet(\"{typeName}\")]");
            sb.AppendLine("    public async Task<IActionResult> GetAll(string typeName)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_getAll.ContainsKey(typeName))");
            sb.AppendLine("            return NotFound(new { error = $\"Unknown config type: {typeName}\" });");
            sb.AppendLine("        try { return Ok(await GetAllData(typeName)); }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            await ServerLogger.LogError(_db, ex.InnerException?.Message ?? ex.Message, stack: ex.InnerException?.StackTrace ?? ex.StackTrace, endpoint: $\"admin/config/{typeName}\", serviceName: \"AdminController\");");
            sb.AppendLine("            return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message });");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── POST {typeName} ──
            sb.AppendLine("    [HttpPost(\"{typeName}\")]");
            sb.AppendLine("    public async Task<IActionResult> Create(string typeName, [FromBody] JsonElement body)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_getAll.ContainsKey(typeName))");
            sb.AppendLine("            return NotFound(new { error = $\"Unknown config type: {typeName}\" });");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var entity = _deserialize[typeName](body.GetRawText());");
            sb.AppendLine("            var err = Validate(entity, typeName); if (err != null) return BadRequest(new { error = err });");
            sb.AppendLine("            await _save[typeName](_db, entity);");
            sb.AppendLine("            var rowId = entity.GetType().GetField(\"id\")?.GetValue(entity)?.ToString();");
            sb.AppendLine("            await Audit(\"create\", typeName, rowId, null, entity);");
            sb.AppendLine("            return Ok(entity);");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            await ServerLogger.LogError(_db, ex.InnerException?.Message ?? ex.Message, stack: ex.InnerException?.StackTrace ?? ex.StackTrace, endpoint: $\"admin/config/{typeName}\", serviceName: \"AdminController\", requestBody: body.GetRawText());");
            sb.AppendLine("            return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message });");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── PUT {typeName}/{id} ──
            sb.AppendLine("    [HttpPut(\"{typeName}/{id}\")]");
            sb.AppendLine("    public async Task<IActionResult> Update(string typeName, string id, [FromBody] JsonElement body)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_getAll.ContainsKey(typeName))");
            sb.AppendLine("            return NotFound(new { error = $\"Unknown config type: {typeName}\" });");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var before = await GetOne(typeName, id);");
            sb.AppendLine("            var entity = _deserialize[typeName](body.GetRawText());");
            sb.AppendLine("            var newId = entity.GetType().GetField(\"id\")?.GetValue(entity) as string;");
            sb.AppendLine("            var idChanged = !string.IsNullOrEmpty(newId) && newId != id;");
            sb.AppendLine("            if (!idChanged) entity.GetType().GetField(\"id\")?.SetValue(entity, id);");
            sb.AppendLine("            var err = Validate(entity, typeName); if (err != null) return BadRequest(new { error = err });");
            sb.AppendLine("            if (idChanged) await _delete[typeName](_db, id);");
            sb.AppendLine("            await _save[typeName](_db, entity);");
            sb.AppendLine("            await Audit(idChanged ? \"rename\" : \"update\", typeName, idChanged ? $\"{id} → {newId}\" : id, before, entity);");
            sb.AppendLine("            return Ok(entity);");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            await ServerLogger.LogError(_db, ex.InnerException?.Message ?? ex.Message, stack: ex.InnerException?.StackTrace ?? ex.StackTrace, endpoint: $\"admin/config/{typeName}/{id}\", serviceName: \"AdminController\", requestBody: body.GetRawText());");
            sb.AppendLine("            return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message });");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── DELETE {typeName}/{id} ──
            sb.AppendLine("    [HttpDelete(\"{typeName}/{id}\")]");
            sb.AppendLine("    public async Task<IActionResult> Delete(string typeName, string id)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_getAll.ContainsKey(typeName))");
            sb.AppendLine("            return NotFound(new { error = $\"Unknown config type: {typeName}\" });");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var before = await GetOne(typeName, id);");
            sb.AppendLine("            await _delete[typeName](_db, id);");
            sb.AppendLine("            await Audit(\"delete\", typeName, id, before, null);");
            sb.AppendLine("            return Ok();");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            await ServerLogger.LogError(_db, ex.InnerException?.Message ?? ex.Message, stack: ex.InnerException?.StackTrace ?? ex.StackTrace, endpoint: $\"admin/config/{typeName}/{id}\", serviceName: \"AdminController\");");
            sb.AppendLine("            return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message });");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── 관리자 목록 ──
            sb.AppendLine("    [HttpGet(\"/admin/api/admins\")]");
            sb.AppendLine("    public async Task<IActionResult> GetAdmins()");
            sb.AppendLine("    {");
            sb.AppendLine("        var admins = await _db.GetAll<AdminUser>();");
            sb.AppendLine("        return Ok(admins);");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── 관리자 추가 ──
            sb.AppendLine("    [HttpPost(\"/admin/api/admins\")]");
            sb.AppendLine("    public async Task<IActionResult> AddAdmin([FromBody] JsonElement body)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var email = body.TryGetProperty(\"email\", out var e) ? e.GetString() : null;");
            sb.AppendLine("            var memo = body.TryGetProperty(\"memo\", out var m) ? m.GetString() : null;");
            sb.AppendLine("            if (string.IsNullOrEmpty(email)) return BadRequest(new { error = \"email is required\" });");
            sb.AppendLine("            var existing = (await _db.GetAll<AdminUser>()).FirstOrDefault(a => a.email == email);");
            sb.AppendLine("            if (existing != null) return BadRequest(new { error = \"already registered\" });");
            sb.AppendLine("            var admin = new AdminUser");
            sb.AppendLine("            {");
            sb.AppendLine("                id = Guid.NewGuid().ToString(),");
            sb.AppendLine("                user_id = null,");
            sb.AppendLine("                email = email,");
            sb.AppendLine("                role = \"admin\",");
            sb.AppendLine("                memo = memo,");
            sb.AppendLine("                created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),");
            sb.AppendLine("                created_by = GetAdminId()");
            sb.AppendLine("            };");
            sb.AppendLine("            await _db.Save(admin);");
            sb.AppendLine("            await Audit(\"admin_add\", \"admin_user\", admin.id, null, admin);");
            sb.AppendLine("            return Ok(admin);");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message });");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── 관리자 삭제 ──
            sb.AppendLine("    [HttpDelete(\"/admin/api/admins/{id}\")]");
            sb.AppendLine("    public async Task<IActionResult> RemoveAdmin(string id)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var admins = await _db.GetAll<AdminUser>();");
            sb.AppendLine("            if (admins.Count <= 1) return BadRequest(new { error = \"Cannot remove the last admin\" });");
            sb.AppendLine("            var target = admins.FirstOrDefault(a => a.id == id);");
            sb.AppendLine("            if (target == null) return NotFound();");
            sb.AppendLine("            await _db.Delete<AdminUser>(id);");
            sb.AppendLine("            await Audit(\"admin_remove\", \"admin_user\", id, target, null);");
            sb.AppendLine("            return Ok();");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message });");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── 관리자 role 변경 ──
            sb.AppendLine("    [HttpPut(\"/admin/api/admins/{id}/role\")]");
            sb.AppendLine("    public async Task<IActionResult> UpdateRole(string id, [FromBody] JsonElement body)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var role = body.TryGetProperty(\"role\", out var r) ? r.GetString() : null;");
            sb.AppendLine("            if (role != \"admin\" && role != \"pending\") return BadRequest(new { error = \"role must be admin or pending\" });");
            sb.AppendLine("            var target = await _db.Get<AdminUser>(id);");
            sb.AppendLine("            if (target == null) return NotFound();");
            sb.AppendLine("            if (role == \"pending\" && target.role == \"admin\")");
            sb.AppendLine("            {");
            sb.AppendLine("                var adminCount = (await _db.GetAll<AdminUser>()).Count(a => a.role == \"admin\");");
            sb.AppendLine("                if (adminCount <= 1) return BadRequest(new { error = \"Cannot demote the last admin\" });");
            sb.AppendLine("            }");
            sb.AppendLine("            var before = target.role;");
            sb.AppendLine("            target.role = role;");
            sb.AppendLine("            await _db.Save(target);");
            sb.AppendLine("            await Audit(\"role_change\", \"admin_user\", id, new { role = before }, new { role });");
            sb.AppendLine("            return Ok(target);");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message });");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            sb.AppendLine("}");

            return new GeneratedFile("Generated/Controllers/AdminController.cs", sb.ToString());
        }

        // ── AdminAuditLog 모델 ──

        static GeneratedFile GenerateAdminAuditModel()
        {
            return new GeneratedFile("Generated/AdminAuditLog.cs",
@"public class AdminAuditLog
{
    public string id;
    public string admin_id;
    public string config_type;
    public string row_id;
    public string action;
    public string before_json;
    public string after_json;
    public long created_at;
}");
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
");}

        /// <summary>[Config] 타입 목록에서 메타데이터 JSON 문자열 생성.</summary>
        static string BuildConfigMetadataJson(Type[] configTypes)
        {
            var items = new List<string>();
            foreach (var type in configTypes)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                var fieldJsons = new List<string>();
                foreach (var f in fields)
                {
                    var parts = new List<string>();
                    parts.Add($"\"name\":\"{f.Name}\"");
                    parts.Add($"\"type\":\"{GetJsType(f.FieldType)}\"");

                    if (f.GetCustomAttribute<PrimaryKeyAttribute>() != null)
                        parts.Add("\"isPrimaryKey\":true");
                    if (f.GetCustomAttribute<NotNullAttribute>() != null)
                        parts.Add("\"isRequired\":true");

                    // JSON 필드 판정: [Json] Attribute 또는 이름 기반
                    var nameLower = f.Name.ToLower();
                    if (f.FieldType == typeof(string) &&
                        (f.GetCustomAttribute<JsonAttribute>() != null ||
                         nameLower == "rewards" || nameLower == "metadata" || nameLower.EndsWith("json")))
                        parts.Add("\"isJson\":true");

                    // ForeignKey
                    var fk = f.GetCustomAttribute<ForeignKeyAttribute>();
                    if (fk != null)
                        parts.Add($"\"foreignKey\":\"{fk.ReferenceType.Name}\"");

                    fieldJsons.Add("{" + string.Join(",", parts) + "}");
                }

                var group = type.GetCustomAttribute<ConfigAttribute>()?.Group;
                var groupPart = group != null ? $"\"group\":\"{group}\"," : "";
                var item = "{" +
                    $"\"name\":\"{type.Name}\"," +
                    $"\"tableName\":\"{ToSnakeCase(type.Name)}\"," +
                    groupPart +
                    $"\"fields\":[{string.Join(",", fieldJsons)}]" +
                    "}";
                items.Add(item);
            }
            return "[" + string.Join(",", items) + "]";
        }

        static string GetJsType(Type t)
        {
            if (t == typeof(string)) return "string";
            if (t == typeof(int)) return "int";
            if (t == typeof(long)) return "long";
            if (t == typeof(float) || t == typeof(double)) return "number";
            if (t == typeof(bool)) return "bool";
            return "string";
        }

        // ── Admin Table Controller (Table 조회 + 통계 + 크로스 검색) ──

        static GeneratedFile GenerateAdminTableController(Type[] tableTypes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Text.Json;");
            sb.AppendLine("using System.Threading.Tasks;");
            sb.AppendLine("using Microsoft.AspNetCore.Mvc;");
            sb.AppendLine("using Microsoft.Extensions.Configuration;");
            sb.AppendLine("using Npgsql;");
            sb.AppendLine("using Dapper;");
            sb.AppendLine("");
            sb.AppendLine("[ApiController]");
            sb.AppendLine("[Route(\"admin/api/table\")]");
            sb.AppendLine("public class AdminTableController : ControllerBase");
            sb.AppendLine("{");
            sb.AppendLine("    readonly IGameDB _db;");
            sb.AppendLine("    readonly string _connStr;");
            sb.AppendLine("    public AdminTableController(IGameDB db, IConfiguration config)");
            sb.AppendLine("    {");
            sb.AppendLine("        _db = db;");
            sb.AppendLine("        _connStr = config.GetConnectionString(\"Supabase\");");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    static readonly JsonSerializerOptions _jsonOpts = new() { IncludeFields = true };");
            sb.AppendLine("");

            // 메타데이터
            var metaJson = BuildTableMetadataJson(tableTypes).Replace("\"", "\"\"");
            sb.AppendLine($"    static readonly string _typesJson = @\"{metaJson}\";");
            sb.AppendLine("");

            // Query delegates
            sb.AppendLine("    static readonly Dictionary<string, Func<IGameDB, QueryOptions, Task<object>>> _query = new()");
            sb.AppendLine("    {");
            foreach (var t in tableTypes)
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = async (db, opts) => await db.Query<{t.Name}>(opts),");
            sb.AppendLine("    };");

            // Count delegates
            sb.AppendLine("    static readonly Dictionary<string, Func<IGameDB, QueryOptions, Task<int>>> _count = new()");
            sb.AppendLine("    {");
            foreach (var t in tableTypes)
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = async (db, opts) => await db.Count<{t.Name}>(opts),");
            sb.AppendLine("    };");
            sb.AppendLine("");

            // Save delegates (플레이어 관리 수정용)
            sb.AppendLine("    static readonly Dictionary<string, Func<IGameDB, object, Task>> _save = new()");
            sb.AppendLine("    {");
            foreach (var t in tableTypes)
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = async (db, e) => await db.Save(({t.Name})e),");
            sb.AppendLine("    };");

            // Deserialize delegates
            sb.AppendLine("    static readonly Dictionary<string, Func<string, object>> _deserialize = new()");
            sb.AppendLine("    {");
            foreach (var t in tableTypes)
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = json => JsonSerializer.Deserialize<{t.Name}>(json, _jsonOpts),");
            sb.AppendLine("    };");

            // hasUserId 테이블 목록
            sb.AppendLine("    static readonly HashSet<string> _userTables = new()");
            sb.AppendLine("    {");
            foreach (var t in tableTypes)
            {
                var hasUserId = t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Any(f => f.Name == "userId" || f.Name == "user_id");
                if (hasUserId)
                    sb.AppendLine($"        \"{ToSnakeCase(t.Name)}\",");
            }
            sb.AppendLine("    };");
            sb.AppendLine("");

            // Field → Column 매핑 (camelCase → snake_case)
            sb.AppendLine("    static readonly Dictionary<string, Dictionary<string, string>> _fieldToColumn = new()");
            sb.AppendLine("    {");
            foreach (var t in tableTypes)
            {
                var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance);
                var entries = string.Join(", ", fields.Select(f => $"[\"{f.Name}\"] = \"{ToSnakeCase(f.Name)}\""));
                sb.AppendLine($"        [\"{ToSnakeCase(t.Name)}\"] = new() {{ {entries} }},");
            }
            sb.AppendLine("    };");
            sb.AppendLine("");

            // 허용 연산자
            sb.AppendLine("    static readonly HashSet<string> _ops = new() { \"=\", \">\", \"<\", \">=\", \"<=\", \"like\" };");
            sb.AppendLine("");

            // ── 헬퍼: 쿼리 파라미터 → WHERE + Dapper Params ──
            sb.AppendLine("    (string where, DynamicParameters parms) BuildWhere(string typeName, IQueryCollection q)");
            sb.AppendLine("    {");
            sb.AppendLine("        var map = _fieldToColumn[typeName];");
            sb.AppendLine("        var clauses = new List<string>();");
            sb.AppendLine("        var p = new DynamicParameters();");
            sb.AppendLine("        int i = 0;");
            sb.AppendLine("        foreach (var key in q.Keys)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (key is \"orderBy\" or \"desc\" or \"limit\" or \"offset\" or \"field\" or \"buckets\") continue;");
            sb.AppendLine("            var parts = key.Split('.');");
            sb.AppendLine("            var fname = parts[0];");
            sb.AppendLine("            if (!map.ContainsKey(fname)) continue;");
            sb.AppendLine("            var col = map[fname];");
            sb.AppendLine("            var op = parts.Length > 1 ? parts[1] switch { \"gt\" => \">\", \"gte\" => \">=\", \"lt\" => \"<\", \"lte\" => \"<=\", \"like\" => \"LIKE\", _ => \"=\" } : \"=\";");
            sb.AppendLine("            var val = q[key].ToString();");
            sb.AppendLine("            clauses.Add($\"\\\"{col}\\\" {op} @p{i}\");");
            sb.AppendLine("            if (long.TryParse(val, out var lv)) p.Add($\"p{i}\", lv);");
            sb.AppendLine("            else if (double.TryParse(val, out var dv)) p.Add($\"p{i}\", dv);");
            sb.AppendLine("            else if (bool.TryParse(val, out var bv)) p.Add($\"p{i}\", bv);");
            sb.AppendLine("            else p.Add($\"p{i}\", val);");
            sb.AppendLine("            i++;");
            sb.AppendLine("        }");
            sb.AppendLine("        return (clauses.Count > 0 ? \"WHERE \" + string.Join(\" AND \", clauses) : \"\", p);");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── 헬퍼: 쿼리 파라미터 → QueryOptions ──
            sb.AppendLine("    QueryOptions BuildQueryOpts(string typeName, IQueryCollection q)");
            sb.AppendLine("    {");
            sb.AppendLine("        var map = _fieldToColumn[typeName];");
            sb.AppendLine("        var opts = new QueryOptions();");
            sb.AppendLine("        foreach (var key in q.Keys)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (key is \"orderBy\" or \"desc\" or \"limit\" or \"offset\" or \"field\" or \"buckets\") continue;");
            sb.AppendLine("            var parts = key.Split('.');");
            sb.AppendLine("            var fname = parts[0];");
            sb.AppendLine("            if (!map.ContainsKey(fname)) continue;");
            sb.AppendLine("            var col = map[fname];");
            sb.AppendLine("            var val = q[key].ToString();");
            sb.AppendLine("            var op = parts.Length > 1 ? parts[1] : \"eq\";");
            sb.AppendLine("            switch (op) {");
            sb.AppendLine("                case \"gt\": opts.Gt(col, val); break;");
            sb.AppendLine("                case \"gte\": opts.Gte(col, val); break;");
            sb.AppendLine("                case \"lt\": opts.Lt(col, val); break;");
            sb.AppendLine("                case \"lte\": opts.Lte(col, val); break;");
            sb.AppendLine("                case \"like\": opts.Like(col, val); break;");
            sb.AppendLine("                default: opts.Eq(col, val); break;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("        if (q.ContainsKey(\"orderBy\") && map.ContainsKey(q[\"orderBy\"].ToString()))");
            sb.AppendLine("        {");
            sb.AppendLine("            var col = map[q[\"orderBy\"].ToString()];");
            sb.AppendLine("            if (q.ContainsKey(\"desc\") && q[\"desc\"] == \"true\") opts.OrderByDesc(col); else opts.OrderByAsc(col);");
            sb.AppendLine("        }");
            sb.AppendLine("        opts.SetLimit(int.TryParse(q[\"limit\"], out var lim) ? Math.Clamp(lim, 1, 500) : 50);");
            sb.AppendLine("        opts.SetOffset(int.TryParse(q[\"offset\"], out var off) ? Math.Max(off, 0) : 0);");
            sb.AppendLine("        return opts;");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── GET _types ──
            sb.AppendLine("    [HttpGet(\"_types\")]");
            sb.AppendLine("    public IActionResult GetTypes() => Content(_typesJson, \"application/json\");");
            sb.AppendLine("");

            // ── GET {typeName} ──
            sb.AppendLine("    [HttpGet(\"{typeName}\")]");
            sb.AppendLine("    public async Task<IActionResult> Query(string typeName)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_query.ContainsKey(typeName)) return NotFound(new { error = $\"Unknown table: {typeName}\" });");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var opts = BuildQueryOpts(typeName, Request.Query);");
            sb.AppendLine("            var countOpts = BuildQueryOpts(typeName, Request.Query);");
            sb.AppendLine("            countOpts.SetLimit(int.MaxValue); countOpts.SetOffset(0);");
            sb.AppendLine("            var rows = await _query[typeName](_db, opts);");
            sb.AppendLine("            var total = await _count[typeName](_db, countOpts);");
            sb.AppendLine("            return Ok(new { rows, total });");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message });");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── GET {typeName}/_stats ──
            sb.AppendLine("    [HttpGet(\"{typeName}/_stats\")]");
            sb.AppendLine("    public async Task<IActionResult> Stats(string typeName, [FromQuery] string field)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_fieldToColumn.TryGetValue(typeName, out var map)) return NotFound();");
            sb.AppendLine("        if (string.IsNullOrEmpty(field) || !map.ContainsKey(field)) return BadRequest(new { error = \"invalid field\" });");
            sb.AppendLine("        var col = map[field];");
            sb.AppendLine("        var (where, parms) = BuildWhere(typeName, Request.Query);");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            using var conn = new NpgsqlConnection(_connStr);");
            sb.AppendLine("            var sql = $\"SELECT COUNT(*) as count, COALESCE(SUM(\\\"{col}\\\"::numeric),0) as sum, COALESCE(AVG(\\\"{col}\\\"::numeric),0) as avg, COALESCE(MIN(\\\"{col}\\\"::numeric),0) as min, COALESCE(MAX(\\\"{col}\\\"::numeric),0) as max FROM \\\"{typeName}\\\" {where}\";");
            sb.AppendLine("            var result = await conn.QueryFirstAsync(sql, parms);");
            sb.AppendLine("            return Ok(new { count = (long)result.count, sum = (decimal)result.sum, avg = (decimal)result.avg, min = (decimal)result.min, max = (decimal)result.max });");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex) { return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message }); }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── GET {typeName}/_distribution ──
            sb.AppendLine("    [HttpGet(\"{typeName}/_distribution\")]");
            sb.AppendLine("    public async Task<IActionResult> Distribution(string typeName, [FromQuery] string field, [FromQuery] int buckets = 10)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_fieldToColumn.TryGetValue(typeName, out var map)) return NotFound();");
            sb.AppendLine("        if (string.IsNullOrEmpty(field) || !map.ContainsKey(field)) return BadRequest(new { error = \"invalid field\" });");
            sb.AppendLine("        var col = map[field];");
            sb.AppendLine("        buckets = Math.Clamp(buckets, 2, 50);");
            sb.AppendLine("        var (where, parms) = BuildWhere(typeName, Request.Query);");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            using var conn = new NpgsqlConnection(_connStr);");
            sb.AppendLine("            var rangeSql = $\"SELECT MIN(\\\"{col}\\\"::numeric) as lo, MAX(\\\"{col}\\\"::numeric) as hi FROM \\\"{typeName}\\\" {where}\";");
            sb.AppendLine("            var range = await conn.QueryFirstAsync(rangeSql, parms);");
            sb.AppendLine("            if (range.lo == null || range.hi == null) return Ok(new { buckets = Array.Empty<object>() });");
            sb.AppendLine("            decimal lo = (decimal)range.lo, hi = (decimal)range.hi;");
            sb.AppendLine("            if (lo == hi) hi = lo + 1;");
            sb.AppendLine("            var step = (hi - lo) / buckets;");
            sb.AppendLine("            var result = new List<object>();");
            sb.AppendLine("            for (int i = 0; i < buckets; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                var bLo = lo + step * i;");
            sb.AppendLine("                var bHi = i == buckets - 1 ? hi + 1 : lo + step * (i + 1);");
            sb.AppendLine("                var cntSql = $\"SELECT COUNT(*) FROM \\\"{typeName}\\\" {(string.IsNullOrEmpty(where) ? \"WHERE\" : where + \" AND\")} \\\"{col}\\\"::numeric >= @bLo AND \\\"{col}\\\"::numeric < @bHi\";");
            sb.AppendLine("                parms.Add(\"bLo\", bLo); parms.Add(\"bHi\", bHi);");
            sb.AppendLine("                var cnt = await conn.ExecuteScalarAsync<long>(cntSql, parms);");
            sb.AppendLine("                result.Add(new { min = bLo, max = bHi, count = cnt });");
            // DynamicParameters에 같은 키 중복 방지
            sb.AppendLine("                parms = new DynamicParameters(parms);");
            sb.AppendLine("            }");
            sb.AppendLine("            return Ok(new { buckets = result });");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex) { return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message }); }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── POST _cross ──
            sb.AppendLine("    [HttpPost(\"_cross\")]");
            sb.AppendLine("    public async Task<IActionResult> CrossQuery([FromBody] JsonElement body)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var conditions = body.GetProperty(\"conditions\");");
            sb.AppendLine("            var limit = body.TryGetProperty(\"limit\", out var lp) ? lp.GetInt32() : 100;");
            sb.AppendLine("            limit = Math.Clamp(limit, 1, 500);");
            sb.AppendLine("            var sqls = new List<string>();");
            sb.AppendLine("            var p = new DynamicParameters();");
            sb.AppendLine("            int idx = 0;");
            sb.AppendLine("            var involvedTables = new List<(string table, string field)>();");
            sb.AppendLine("            foreach (var c in conditions.EnumerateArray())");
            sb.AppendLine("            {");
            sb.AppendLine("                var table = c.GetProperty(\"table\").GetString();");
            sb.AppendLine("                var field = c.GetProperty(\"field\").GetString();");
            sb.AppendLine("                var op = c.GetProperty(\"op\").GetString();");
            sb.AppendLine("                var val = c.GetProperty(\"value\").GetString();");
            sb.AppendLine("                if (!_fieldToColumn.TryGetValue(table, out var map)) return BadRequest(new { error = $\"Unknown table: {table}\" });");
            sb.AppendLine("                if (!map.ContainsKey(field)) return BadRequest(new { error = $\"Unknown field: {field}\" });");
            sb.AppendLine("                if (!_ops.Contains(op)) return BadRequest(new { error = $\"Invalid op: {op}\" });");
            sb.AppendLine("                var col = map[field];");
            sb.AppendLine("                sqls.Add($\"SELECT \\\"user_id\\\" FROM \\\"{table}\\\" WHERE \\\"{col}\\\" {op} @p{idx}\");");
            sb.AppendLine("                if (long.TryParse(val, out var lv)) p.Add($\"p{idx}\", lv);");
            sb.AppendLine("                else if (double.TryParse(val, out var dv)) p.Add($\"p{idx}\", dv);");
            sb.AppendLine("                else if (bool.TryParse(val, out var bv)) p.Add($\"p{idx}\", bv);");
            sb.AppendLine("                else p.Add($\"p{idx}\", val);");
            sb.AppendLine("                involvedTables.Add((table, field));");
            sb.AppendLine("                idx++;");
            sb.AppendLine("            }");
            sb.AppendLine("            if (sqls.Count == 0) return BadRequest(new { error = \"No conditions\" });");
            sb.AppendLine("            var intersectSql = string.Join(\" INTERSECT \", sqls) + $\" LIMIT {limit}\";");
            sb.AppendLine("            using var conn = new NpgsqlConnection(_connStr);");
            sb.AppendLine("            var userIds = (await conn.QueryAsync<string>(intersectSql, p)).ToList();");
            sb.AppendLine("            // 상세 데이터 조회");
            sb.AppendLine("            var details = new Dictionary<string, Dictionary<string, object>>();");
            sb.AppendLine("            if (userIds.Count > 0 && userIds.Count <= 200)");
            sb.AppendLine("            {");
            sb.AppendLine("                foreach (var (table, _) in involvedTables.Distinct())");
            sb.AppendLine("                {");
            sb.AppendLine("                    var detailSql = $\"SELECT * FROM \\\"{table}\\\" WHERE \\\"user_id\\\" = ANY(@ids)\";");
            sb.AppendLine("                    var rows = await conn.QueryAsync(detailSql, new { ids = userIds.ToArray() });");
            sb.AppendLine("                    foreach (var row in rows)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        var dict = (IDictionary<string, object>)row;");
            sb.AppendLine("                        var uid = dict[\"user_id\"]?.ToString();");
            sb.AppendLine("                        if (uid == null) continue;");
            sb.AppendLine("                        if (!details.ContainsKey(uid)) details[uid] = new();");
            sb.AppendLine("                        details[uid][table] = dict;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            return Ok(new { count = userIds.Count, userIds, details });");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex) { return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message }); }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── GET /admin/api/player/{userId} ──
            sb.AppendLine("    [HttpGet(\"/admin/api/player/{userId}\")]");
            sb.AppendLine("    public async Task<IActionResult> GetPlayer(string userId)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var tables = new Dictionary<string, object>();");
            sb.AppendLine("            foreach (var tableName in _userTables)");
            sb.AppendLine("            {");
            sb.AppendLine("                var opts = new QueryOptions().Eq(\"user_id\", userId).SetLimit(200);");
            sb.AppendLine("                tables[tableName] = await _query[tableName](_db, opts);");
            sb.AppendLine("            }");
            sb.AppendLine("            return Ok(new { userId, tables });");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex) { return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message }); }");
            sb.AppendLine("    }");
            sb.AppendLine("");

            // ── PUT {typeName}/{id} (플레이어 데이터 수정) ──
            sb.AppendLine("    [HttpPut(\"{typeName}/{id}\")]");
            sb.AppendLine("    public async Task<IActionResult> UpdateRow(string typeName, string id, [FromBody] JsonElement body)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (!_save.ContainsKey(typeName)) return NotFound(new { error = $\"Unknown table: {typeName}\" });");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var entity = _deserialize[typeName](body.GetRawText());");
            sb.AppendLine("            entity.GetType().GetField(\"id\")?.SetValue(entity, id);");
            sb.AppendLine("            await _save[typeName](_db, entity);");
            // 감사 로그
            sb.AppendLine("            var adminId = User?.FindFirst(\"sub\")?.Value ?? \"unknown\";");
            sb.AppendLine("            try { await _db.Save(new AdminAuditLog { id = Guid.NewGuid().ToString(), admin_id = adminId, config_type = typeName, row_id = id, action = \"player_update\", after_json = body.GetRawText(), created_at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }); } catch { }");
            sb.AppendLine("            return Ok(entity);");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex) { return StatusCode(500, new { error = ex.InnerException?.Message ?? ex.Message }); }");
            sb.AppendLine("    }");

            sb.AppendLine("}");
            return new GeneratedFile("Generated/Controllers/AdminTableController.cs", sb.ToString());
        }

        /// <summary>[Table] 타입 목록에서 메타데이터 JSON 문자열 생성.</summary>
        static string BuildTableMetadataJson(Type[] tableTypes)
        {
            var items = new List<string>();
            foreach (var type in tableTypes)
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                var fieldJsons = new List<string>();
                foreach (var f in fields)
                {
                    var parts = new List<string>();
                    parts.Add($"\"name\":\"{f.Name}\"");
                    parts.Add($"\"type\":\"{GetJsType(f.FieldType)}\"");

                    if (f.GetCustomAttribute<PrimaryKeyAttribute>() != null)
                        parts.Add("\"isPrimaryKey\":true");
                    if (f.GetCustomAttribute<NotNullAttribute>() != null)
                        parts.Add("\"isRequired\":true");

                    var fk = f.GetCustomAttribute<ForeignKeyAttribute>();
                    if (fk != null)
                        parts.Add($"\"foreignKey\":\"{fk.ReferenceType.Name}\"");

                    fieldJsons.Add("{" + string.Join(",", parts) + "}");
                }

                var group = type.GetCustomAttribute<TableAttribute>()?.Group;
                var groupPart = group != null ? $"\"group\":\"{group}\"," : "";
                // user_id 필드 존재 여부
                var hasUserId = fields.Any(f => f.Name == "userId" || f.Name == "user_id");
                var userIdPart = hasUserId ? "\"hasUserId\":true," : "";
                var item = "{" +
                    $"\"name\":\"{type.Name}\"," +
                    $"\"tableName\":\"{ToSnakeCase(type.Name)}\"," +
                    groupPart +
                    userIdPart +
                    $"\"fields\":[{string.Join(",", fieldJsons)}]" +
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
