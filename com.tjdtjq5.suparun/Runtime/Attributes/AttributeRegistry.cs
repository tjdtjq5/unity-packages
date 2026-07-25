#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 타입의 속성 분류(어느 필드가 [PrimaryKey]/[NotNull]/[Unique]/[MaxLength]/[Default]/[CreatedAt]/[UpdatedAt]인지)를
    /// 한 번 스캔해 캐시한다. 이전엔 LocalGameDB(런타임 검증)와 ServerCodeGenerator(마이그레이션 제약)가
    /// 각자 필드를 순회하며 GetCustomAttribute를 반복했다 — 그 스캔을 단일 캐시 계약으로 통합.
    ///
    /// 소스 제너레이터(DefGenerator)는 별도 컴파일(Roslyn 심볼)이라 reflection 기반 이 레지스트리를 공유하지 않는다.
    /// 분류는 직접 GetCustomAttribute와 byte-동일해야 한다(두 소비자가 동작 동일성을 의존).
    /// </summary>
    public sealed class TypeAttributeInfo
    {
        public readonly FieldInfo[] Fields;
        public readonly FieldInfo? PrimaryKey;                       // [PrimaryKey] 첫 필드 (없으면 null)
        public readonly FieldInfo[] NotNull;
        public readonly FieldInfo[] Unique;
        public readonly (FieldInfo field, int length)[] MaxLength;
        public readonly (FieldInfo field, object value)[] Default;
        public readonly FieldInfo[] CreatedAt;
        public readonly FieldInfo[] UpdatedAt;

        internal TypeAttributeInfo(FieldInfo[] fields, FieldInfo? primaryKey, FieldInfo[] notNull,
            FieldInfo[] unique, (FieldInfo, int)[] maxLength, (FieldInfo, object)[] @default,
            FieldInfo[] createdAt, FieldInfo[] updatedAt)
        {
            Fields = fields;
            PrimaryKey = primaryKey;
            NotNull = notNull;
            Unique = unique;
            MaxLength = maxLength;
            Default = @default;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }

        // ── 필드 단위 질의 (ServerCodeGenerator의 per-field 헬퍼용; 직접 GetCustomAttribute와 동등) ──
        public bool IsPrimaryKey(FieldInfo f) => PrimaryKey == f;
        public bool IsNotNull(FieldInfo f) => Array.IndexOf(NotNull, f) >= 0;
        public bool IsUnique(FieldInfo f) => Array.IndexOf(Unique, f) >= 0;

        public int? GetMaxLength(FieldInfo f)
        {
            foreach (var (field, length) in MaxLength) if (field == f) return length;
            return null;
        }

        public object? GetDefault(FieldInfo f)
        {
            foreach (var (field, value) in Default) if (field == f) return value;
            return null;
        }

        /// <summary>[Default] attribute 존재 여부 (Value가 null이어도 true) — GetDefault만으로는 구분 불가한 케이스용.</summary>
        public bool HasDefault(FieldInfo f)
        {
            foreach (var (field, _) in Default) if (field == f) return true;
            return false;
        }
    }

    /// <summary>타입별 <see cref="TypeAttributeInfo"/>를 캐시 제공. thread-safe (ConcurrentDictionary).</summary>
    public static class AttributeRegistry
    {
        static readonly ConcurrentDictionary<Type, TypeAttributeInfo> _cache = new();

        public static TypeAttributeInfo Get(Type type) => _cache.GetOrAdd(type, Scan);

        static TypeAttributeInfo Scan(Type type)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

            FieldInfo? primaryKey = null;
            var notNull = new List<FieldInfo>();
            var unique = new List<FieldInfo>();
            var maxLength = new List<(FieldInfo, int)>();
            var defaults = new List<(FieldInfo, object)>();
            var createdAt = new List<FieldInfo>();
            var updatedAt = new List<FieldInfo>();

            foreach (var f in fields)
            {
                if (primaryKey == null && f.GetCustomAttribute<PrimaryKeyAttribute>() != null) primaryKey = f;
                if (f.GetCustomAttribute<NotNullAttribute>() != null) notNull.Add(f);
                if (f.GetCustomAttribute<UniqueAttribute>() != null) unique.Add(f);

                var ml = f.GetCustomAttribute<MaxLengthAttribute>();
                if (ml != null) maxLength.Add((f, ml.Length));

                var def = f.GetCustomAttribute<DefaultAttribute>();
                if (def != null) defaults.Add((f, def.Value));

                if (f.GetCustomAttribute<CreatedAtAttribute>() != null) createdAt.Add(f);
                if (f.GetCustomAttribute<UpdatedAtAttribute>() != null) updatedAt.Add(f);
            }

            return new TypeAttributeInfo(fields, primaryKey, notNull.ToArray(), unique.ToArray(),
                maxLength.ToArray(), defaults.ToArray(), createdAt.ToArray(), updatedAt.ToArray());
        }
    }
}
