using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// IGameDB의 로컬 구현. 메모리 Dictionary + JSON 직렬화.
    /// Unity Play 모드에서 서버 없이 [Service]를 즉시 실행.
    /// </summary>
    public class LocalGameDB : IGameDB
    {
        // 타입명 → (PK문자열 → JSON문자열)
        readonly Dictionary<string, Dictionary<string, string>> _tables = new();

        static LocalGameDB _instance;
        public static LocalGameDB Instance => _instance ??= new LocalGameDB();

        public static void Reset() => _instance = new LocalGameDB();

        // ── Reflection 캐시 (속성 분류와 함께 AttributeRegistry로 통합) ──
        static FieldInfo[] CachedFields(Type type) => AttributeRegistry.Get(type).Fields;

        // ── 직렬화 (시스템 표준 Newtonsoft로 통일) ──
        // 이전엔 JsonUtility를 썼으나 properties/Dictionary/[JsonProperty]를 처리 못 해,
        // 같은 [Table] 타입이 Realtime/REST/서버(전부 Newtonsoft) 경로와 불일치(조용한 데이터 손상)했다.
        // 단일 지점으로 모아 두 경로가 동일 직렬화 규칙을 쓰도록 보장한다.
        static string Serialize<T>(T entity) => JsonConvert.SerializeObject(entity);
        static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json);

        // ── Get ──

        public UniTask<T> Get<T>(object primaryKey, CancellationToken ct = default)
        {
            var table = GetTable(typeof(T).Name);
            var pk = primaryKey?.ToString() ?? "";

            if (table.TryGetValue(pk, out var json))
            {
                var entity = Deserialize<T>(json);
                Log($"Get<{typeof(T).Name}>(\"{pk}\") → found");
                return UniTask.FromResult(entity);
            }

            Log($"Get<{typeof(T).Name}>(\"{pk}\") → not found");
            return UniTask.FromResult(default(T));
        }

        // ── GetAll ──

        public UniTask<List<T>> GetAll<T>(CancellationToken ct = default)
        {
            var table = GetTable(typeof(T).Name);
            var list = table.Values
                .Select(json => Deserialize<T>(json))
                .ToList();

            // [Table] 타입에 대해 성능 경고 (100건 초과 시)
            if (list.Count > 100 && IsTableType<T>())
            {
                Debug.LogWarning(
                    $"[SupaRun:성능] GetAll<{typeof(T).Name}>()이 {list.Count}건을 로드했습니다. " +
                    $"[Table] 데이터에는 _db.Query<{typeof(T).Name}>(new QueryOptions().Eq(\"필드명\", 값))를 사용하세요.");
            }

            Log($"GetAll<{typeof(T).Name}>() → {list.Count} rows");
            return UniTask.FromResult(list);
        }

        // ── Save ──

        public UniTask Save<T>(T entity, CancellationToken ct = default)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            var type = typeof(T);
            var pk = GetPrimaryKey(entity, type);

            if (string.IsNullOrEmpty(pk))
                throw new InvalidOperationException(
                    $"[PrimaryKey] field is null or empty on {type.Name}");

            // 어트리뷰트 검증
            ValidateNotNull(entity, type);
            ValidateMaxLength(entity, type);
            ValidateUnique(entity, type, pk);

            // 기본값 적용
            ApplyDefaults(entity, type);

            // 시간 자동 기록
            var table = GetTable(type.Name);
            bool isNew = !table.ContainsKey(pk);
            ApplyCreatedAt(entity, type, isNew);
            ApplyUpdatedAt(entity, type);

            // JSON으로 직렬화하여 저장 (값 복사)
            table[pk] = Serialize(entity);

            Log($"Save<{type.Name}>(\"{pk}\") → {(isNew ? "created" : "updated")}");
            return UniTask.CompletedTask;
        }

        // ── Delete ──

        public UniTask Delete<T>(object primaryKey, CancellationToken ct = default)
        {
            var table = GetTable(typeof(T).Name);
            var pk = primaryKey?.ToString() ?? "";
            var removed = table.Remove(pk);

            Log($"Delete<{typeof(T).Name}>(\"{pk}\") → {(removed ? "deleted" : "not found")}");
            return UniTask.CompletedTask;
        }

        // ── Query ──

        public UniTask<List<T>> Query<T>(QueryOptions options, CancellationToken ct = default)
        {
            if (options == null || options.Filters.Count == 0)
            {
                // 필터 없는 Query는 GetAll과 동일하지만 성능 경고 없이 실행
                var allTable = GetTable(typeof(T).Name);
                var allList = allTable.Values
                    .Select(json => Deserialize<T>(json))
                    .ToList();
                Log($"Query<{typeof(T).Name}>(no filters) → {allList.Count} rows");
                return UniTask.FromResult(allList);
            }

            var table = GetTable(typeof(T).Name);
            var fields = CachedFields(typeof(T));
            var results = new List<T>();

            foreach (var json in table.Values)
            {
                var entity = Deserialize<T>(json);
                if (MatchesFilters(entity, fields, options.Filters))
                    results.Add(entity);
            }

            // 정렬
            if (!string.IsNullOrEmpty(options.OrderBy))
            {
                var orderField = System.Array.Find(fields, f =>
                    f.Name.Equals(options.OrderBy, System.StringComparison.OrdinalIgnoreCase));
                if (orderField != null)
                {
                    results.Sort((a, b) =>
                    {
                        var va = orderField.GetValue(a) as System.IComparable;
                        var vb = orderField.GetValue(b) as System.IComparable;
                        var cmp = va?.CompareTo(vb) ?? 0;
                        return options.OrderDesc ? -cmp : cmp;
                    });
                }
            }

            // 페이지네이션
            if (options.Offset > 0)
                results = results.Skip(options.Offset).ToList();
            if (options.Limit > 0)
                results = results.Take(options.Limit).ToList();

            Log($"Query<{typeof(T).Name}>({options.Filters.Count} filters) → {results.Count} rows");
            return UniTask.FromResult(results);
        }

        static bool MatchesFilters<T>(T entity, FieldInfo[] fields, System.Collections.Generic.List<QueryFilter> filters)
        {
            foreach (var filter in filters)
            {
                var field = System.Array.Find(fields, f =>
                    f.Name.Equals(filter.Column, System.StringComparison.OrdinalIgnoreCase));
                if (field == null) continue;

                var value = field.GetValue(entity);
                if (!CompareValues(value, filter.Operator, filter.Value))
                    return false;
            }
            return true;
        }

        static bool CompareValues(object fieldValue, string op, object filterValue)
        {
            if (fieldValue == null)
                return filterValue == null && op == "=";

            var fieldStr = fieldValue.ToString();
            var filterStr = filterValue?.ToString() ?? "";

            return op switch
            {
                "=" => fieldStr == filterStr,
                ">" => CompareNumeric(fieldValue, filterValue) > 0,
                "<" => CompareNumeric(fieldValue, filterValue) < 0,
                ">=" => CompareNumeric(fieldValue, filterValue) >= 0,
                "<=" => CompareNumeric(fieldValue, filterValue) <= 0,
                "like" => fieldStr.IndexOf(filterStr, System.StringComparison.OrdinalIgnoreCase) >= 0,
                _ => fieldStr == filterStr
            };
        }

        static int CompareNumeric(object a, object b)
        {
            if (a is System.IComparable ca && b != null)
            {
                try { return ca.CompareTo(System.Convert.ChangeType(b, a.GetType())); }
                catch (System.Exception ex) { UnityEngine.Debug.Log($"[SupaRun:LocalDB] Compare fallback: {ex.Message}"); }
            }
            return string.Compare(a?.ToString(), b?.ToString(), System.StringComparison.Ordinal);
        }

        // ── Count ──

        public UniTask<int> Count<T>(QueryOptions options, CancellationToken ct = default)
        {
            if (options == null || options.Filters.Count == 0)
            {
                var count = GetTable(typeof(T).Name).Count;
                Log($"Count<{typeof(T).Name}>(no filters) → {count}");
                return UniTask.FromResult(count);
            }

            var table = GetTable(typeof(T).Name);
            var fields = CachedFields(typeof(T));
            var result = 0;

            foreach (var json in table.Values)
            {
                var entity = Deserialize<T>(json);
                if (MatchesFilters(entity, fields, options.Filters))
                    result++;
            }

            Log($"Count<{typeof(T).Name}>({options.Filters.Count} filters) → {result}");
            return UniTask.FromResult(result);
        }

        // ── SaveAll ──

        public UniTask SaveAll<T>(List<T> entities, CancellationToken ct = default)
        {
            if (entities == null || entities.Count == 0)
                return UniTask.CompletedTask;

            foreach (var e in entities)
                _ = Save(e, ct);  // LocalGameDB는 동기 완료 — fire-and-forget OK

            Log($"SaveAll<{typeof(T).Name}>({entities.Count} entities)");
            return UniTask.CompletedTask;
        }

        // ── DeleteAll ──

        public UniTask DeleteAll<T>(QueryOptions options, CancellationToken ct = default)
        {
            var table = GetTable(typeof(T).Name);
            var fields = CachedFields(typeof(T));
            var toRemove = new List<string>();

            foreach (var (pk, json) in table)
            {
                var entity = Deserialize<T>(json);
                if (options == null || options.Filters.Count == 0 || MatchesFilters(entity, fields, options.Filters))
                    toRemove.Add(pk);
            }

            foreach (var pk in toRemove)
                table.Remove(pk);

            Log($"DeleteAll<{typeof(T).Name}>({options?.Filters.Count ?? 0} filters) → {toRemove.Count} deleted");
            return UniTask.CompletedTask;
        }

        // ── Transaction ──

        public async UniTask Transaction(Func<IGameDB, UniTask> action, CancellationToken ct = default)
        {
            // 스냅샷 생성 → 실패 시 롤백
            var snapshot = _tables.ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, string>(kv.Value));
            try
            {
                await action(this);
            }
            catch
            {
                _tables.Clear();
                foreach (var kv in snapshot)
                    _tables[kv.Key] = kv.Value;
                throw;
            }
        }

        // ── 내부 헬퍼 ──

        Dictionary<string, string> GetTable(string typeName)
        {
            if (!_tables.TryGetValue(typeName, out var table))
            {
                table = new Dictionary<string, string>();
                _tables[typeName] = table;
            }
            return table;
        }

        static string GetPrimaryKey<T>(T entity, Type type)
        {
            var pk = AttributeRegistry.Get(type).PrimaryKey;
            if (pk == null)
                throw new InvalidOperationException(
                    $"[PrimaryKey] attribute not found on {type.Name}. " +
                    "Add [PrimaryKey] to a field.");
            return pk.GetValue(entity)?.ToString() ?? "";
        }

        // ── 어트리뷰트 검증 ──

        static void ValidateNotNull<T>(T entity, Type type)
        {
            foreach (var field in AttributeRegistry.Get(type).NotNull)
            {
                var value = field.GetValue(entity);
                if (value == null || (value is string s && string.IsNullOrEmpty(s)))
                    throw new InvalidOperationException(
                        $"[NotNull] violation: {type.Name}.{field.Name} cannot be null");
            }
        }

        static void ValidateMaxLength<T>(T entity, Type type)
        {
            foreach (var (field, length) in AttributeRegistry.Get(type).MaxLength)
            {
                if (field.GetValue(entity) is string s && s.Length > length)
                    throw new InvalidOperationException(
                        $"[MaxLength({length})] violation: {type.Name}.{field.Name} " +
                        $"length is {s.Length}");
            }
        }

        void ValidateUnique<T>(T entity, Type type, string currentPk)
        {
            var table = GetTable(type.Name);

            foreach (var field in AttributeRegistry.Get(type).Unique)
            {
                var value = field.GetValue(entity)?.ToString();
                if (string.IsNullOrEmpty(value)) continue;

                foreach (var (pk, json) in table)
                {
                    if (pk == currentPk) continue; // 자기 자신은 제외
                    var existing = Deserialize<T>(json);
                    var existingValue = field.GetValue(existing)?.ToString();
                    if (value == existingValue)
                        throw new InvalidOperationException(
                            $"[Unique] violation: {type.Name}.{field.Name} " +
                            $"value \"{value}\" already exists");
                }
            }
        }

        // ── 기본값/시간 적용 ──

        static void ApplyDefaults<T>(T entity, Type type)
        {
            foreach (var (field, attrValue) in AttributeRegistry.Get(type).Default)
            {
                var value = field.GetValue(entity);
                bool isDefault = value == null
                    || (value is int i && i == 0)
                    || (value is long l && l == 0)
                    || (value is float f && f == 0f)
                    || (value is string s && string.IsNullOrEmpty(s));

                if (isDefault)
                {
                    var defaultValue = Convert.ChangeType(attrValue, field.FieldType);
                    field.SetValue(entity, defaultValue);
                }
            }
        }

        static void ApplyCreatedAt<T>(T entity, Type type, bool isNew)
        {
            if (!isNew) return;

            foreach (var field in AttributeRegistry.Get(type).CreatedAt)
                SetTimeField(entity, field);
        }

        static void ApplyUpdatedAt<T>(T entity, Type type)
        {
            foreach (var field in AttributeRegistry.Get(type).UpdatedAt)
                SetTimeField(entity, field);
        }

        static void SetTimeField<T>(T entity, FieldInfo field)
        {
            if (field.FieldType == typeof(long))
                field.SetValue(entity, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            else if (field.FieldType == typeof(double))
                field.SetValue(entity, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0);
            else if (field.FieldType == typeof(DateTime))
                field.SetValue(entity, DateTime.UtcNow);
            else if (field.FieldType == typeof(string))
                field.SetValue(entity, DateTime.UtcNow.ToString("o"));
            else
                Debug.LogWarning($"[SupaRun:LocalDB] {field.DeclaringType?.Name}.{field.Name}: " +
                    $"[CreatedAt/UpdatedAt] 지원 타입은 long, double, DateTime, string. " +
                    $"현재 타입: {field.FieldType.Name}");
        }

        // ── [Table] 타입 캐시 ──

        static readonly Dictionary<System.Type, bool> _tableTypeCache = new();

        static bool IsTableType<T>()
        {
            var type = typeof(T);
            if (_tableTypeCache.TryGetValue(type, out var cached))
                return cached;
            var result = type.GetCustomAttribute(typeof(TableAttribute)) != null;
            _tableTypeCache[type] = result;
            return result;
        }

        // ── 로그 ──

        static void Log(string message)
        {
            SupaRun.LogVerbose($"[SupaRun:LocalDB] {message}");
        }
    }
}
