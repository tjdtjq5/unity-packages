using System.Collections.Generic;

namespace Tjdtjq5.SupaRun
{
    public class QueryFilter
    {
        public string Column;
        public string Operator; // =, >, <, >=, <=, like
        public object Value;

        public QueryFilter(string column, string op, object value)
        {
            Column = column;
            Operator = op;
            Value = value;
        }
    }

    /// <summary>쿼리 옵션. 필터 + 정렬 + 페이지네이션.</summary>
    public class QueryOptions
    {
        public List<QueryFilter> Filters = new();
        public string OrderBy;
        public bool OrderDesc;
        public int Limit = 1000;
        public int Offset;

        public QueryOptions Eq(string column, object value)
        {
            Filters.Add(new QueryFilter(column, "=", value));
            return this;
        }

        public QueryOptions Gt(string column, object value)
        {
            Filters.Add(new QueryFilter(column, ">", value));
            return this;
        }

        public QueryOptions Lt(string column, object value)
        {
            Filters.Add(new QueryFilter(column, "<", value));
            return this;
        }

        public QueryOptions Gte(string column, object value)
        {
            Filters.Add(new QueryFilter(column, ">=", value));
            return this;
        }

        public QueryOptions Lte(string column, object value)
        {
            Filters.Add(new QueryFilter(column, "<=", value));
            return this;
        }

        public QueryOptions Like(string column, string value)
        {
            Filters.Add(new QueryFilter(column, "like", value));
            return this;
        }

        public QueryOptions OrderByAsc(string column)
        {
            OrderBy = column;
            OrderDesc = false;
            return this;
        }

        public QueryOptions OrderByDesc(string column)
        {
            OrderBy = column;
            OrderDesc = true;
            return this;
        }

        public QueryOptions SetLimit(int limit)
        {
            Limit = limit;
            return this;
        }

        public QueryOptions SetOffset(int offset)
        {
            Offset = offset;
            return this;
        }
    }
}
