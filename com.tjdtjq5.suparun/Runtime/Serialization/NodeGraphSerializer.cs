using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 컬럼에 저장된 그래프 JSON 을 C# 객체로 되돌린다.
    ///
    /// 저장 형식:
    /// <code>
    /// { "nodes":[ {"type":"DamageNode","amount":25,"next":1},
    ///             {"type":"ChanceNode","probability":0.3,"onTrue":2,"onFalse":-1} ],
    ///   "entry": 0,
    ///   "layout":[ {"x":80,"y":40}, {"x":320,"y":40} ] }
    /// </code>
    ///
    /// `layout` 은 캔버스 좌표라 여기서 무시한다 — 노드를 옮긴 것만으로 게임이 달라지면 안 된다.
    ///
    /// 모르는 `type` 은 예외를 던지지 않고 그 자리를 null 로 두고 이름을 모아 돌려준다.
    /// 카탈로그에서 노드가 사라진 뒤에도 나머지 그래프는 읽을 수 있어야 하기 때문이다.
    /// </summary>
    public static class NodeGraphSerializer
    {
        static readonly JsonSerializer _serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = { new NodeValueConverter() },
            MissingMemberHandling = MissingMemberHandling.Ignore,
        });

        // 컨텍스트별 "노드 이름 → 타입" 표. 어셈블리 스캔이 비싸서 1회만 만든다.
        static readonly Dictionary<Type, Dictionary<string, Type>> _typeMaps
            = new Dictionary<Type, Dictionary<string, Type>>();

        public static NodeGraphData<TCtx> Parse<TCtx>(string json)
        {
            var empty = new NodeGraphData<TCtx>
            {
                Nodes = new Node<TCtx>[0],
                Entry = -1,
                UnknownTypes = new string[0],
            };
            if (string.IsNullOrWhiteSpace(json)) return empty;

            JObject root;
            try { root = JObject.Parse(json); }
            catch (JsonException) { return empty; }

            var array = root["nodes"] as JArray;
            if (array == null || array.Count == 0) return empty;

            var map = GetTypeMap(typeof(Node<TCtx>));
            var nodes = new Node<TCtx>[array.Count];
            var unknown = new List<string>();

            for (int i = 0; i < array.Count; i++)
            {
                var item = array[i] as JObject;
                if (item == null) continue;

                var typeName = item["type"]?.Value<string>();
                if (string.IsNullOrEmpty(typeName)) continue;

                if (!map.TryGetValue(typeName, out var type))
                {
                    if (!unknown.Contains(typeName)) unknown.Add(typeName);
                    continue;
                }

                try { nodes[i] = (Node<TCtx>)item.ToObject(type, _serializer); }
                catch (JsonException) { if (!unknown.Contains(typeName)) unknown.Add(typeName); }
            }

            return new NodeGraphData<TCtx>
            {
                Nodes = nodes,
                Entry = root["entry"]?.Value<int>() ?? 0,
                UnknownTypes = unknown.ToArray(),
            };
        }

        /// <summary>
        /// `[Polymorphic]` 컬럼 하나를 복원한다 — 연결 없는 노드 하나와 같은 형태다.
        ///
        /// <code>{"type":"GunPatternData","range":10,"magazine_size":3}</code>
        ///
        /// 모르는 타입이거나 비어 있으면 null 을 돌려준다. 예외를 던지지 않는 이유는
        /// 클래스 이름을 바꾼 뒤에도 나머지 행은 읽혀야 하기 때문이다 — 대신
        /// <paramref name="unknownType"/> 에 무엇을 못 찾았는지 남긴다.
        /// </summary>
        public static TBase ParseOne<TBase>(string json, out string unknownType) where TBase : class
        {
            unknownType = null;
            if (string.IsNullOrWhiteSpace(json)) return null;

            JObject obj;
            try { obj = JObject.Parse(json); }
            catch (JsonException) { return null; }

            var typeName = obj["type"]?.Value<string>();
            if (string.IsNullOrEmpty(typeName)) return null;

            if (!GetTypeMap(typeof(TBase)).TryGetValue(typeName, out var type))
            {
                unknownType = typeName;
                return null;
            }

            try { return (TBase)obj.ToObject(type, _serializer); }
            catch (JsonException) { unknownType = typeName; return null; }
        }

        public static TBase ParseOne<TBase>(string json) where TBase : class
            => ParseOne<TBase>(json, out _);

        /// <summary>다형 값 하나를 저장 형식으로 되돌린다. 이관 도구가 쓴다.</summary>
        public static string SerializeOne(object value)
        {
            if (value == null) return null;
            var obj = JObject.FromObject(value, _serializer);
            obj.AddFirst(new JProperty("type", value.GetType().Name));
            return obj.ToString(Formatting.None);
        }

        /// <summary>복원한 그래프를 저장 형식으로 되돌린다. 왕복 검증과 마이그레이션 도구용.</summary>
        public static string Serialize<TCtx>(NodeGraphData<TCtx> data)
        {
            var array = new JArray();
            if (data?.Nodes != null)
                foreach (var node in data.Nodes)
                {
                    if (node == null) { array.Add(JValue.CreateNull()); continue; }
                    var obj = JObject.FromObject(node, _serializer);
                    obj.AddFirst(new JProperty("type", node.GetType().Name));
                    array.Add(obj);
                }

            return new JObject
            {
                ["nodes"] = array,
                ["entry"] = data?.Entry ?? -1,
            }.ToString(Formatting.None);
        }

        /// <summary>
        /// base 타입의 파생을 어셈블리에서 찾아 이름표를 만든다.
        /// 그래프는 `Node&lt;TCtx&gt;` 를, 다형 필드는 그 base 를 그대로 넘긴다.
        ///
        /// 런타임이라 TypeCache 를 쓸 수 없어 직접 훑는다 — 대신 base 당 1회만 한다.
        /// </summary>
        static Dictionary<string, Type> GetTypeMap(Type baseType)
        {
            if (_typeMaps.TryGetValue(baseType, out var cached)) return cached;

            var map = new Dictionary<string, Type>(StringComparer.Ordinal);

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (IsSystemAssembly(asm.FullName)) continue;

                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                catch (Exception) { continue; }

                foreach (var t in types)
                {
                    if (t == null || t.IsAbstract || t.ContainsGenericParameters) continue;
                    if (!baseType.IsAssignableFrom(t)) continue;
                    map[t.Name] = t;   // 이름 충돌 시 나중 것이 이긴다 — 카탈로그도 단순명을 쓴다
                }
            }

            _typeMaps[baseType] = map;
            return map;
        }

        static bool IsSystemAssembly(string name)
            => name.StartsWith("System", StringComparison.Ordinal)
            || name.StartsWith("mscorlib", StringComparison.Ordinal)
            || name.StartsWith("netstandard", StringComparison.Ordinal)
            || name.StartsWith("Mono.", StringComparison.Ordinal)
            || name.StartsWith("Unity.", StringComparison.Ordinal)
            || name.StartsWith("UnityEngine", StringComparison.Ordinal)
            || name.StartsWith("UnityEditor", StringComparison.Ordinal);
    }
}
