using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// <see cref="NodeValue{T}"/> 를 "상수 또는 연결" 두 모양으로 읽고 쓴다.
    ///
    /// <code>
    /// 25              → constant = 25,  source = -1
    /// {"$node":3}     → constant = 기본값, source = 3
    /// </code>
    ///
    /// 이 형태를 쓰는 이유는 브라우저가 다루기 쉬워야 하기 때문이다 —
    /// 어드민 캔버스가 저작 주체라 `{"constant":25,"source":-1}` 처럼 내부 구조를 노출하면
    /// 상수 하나 넣는 데도 두 필드를 알아야 한다.
    /// </summary>
    public sealed class NodeValueConverter : JsonConverter
    {
        const string NodeKey = "$node";

        public override bool CanConvert(Type objectType)
            => objectType.IsGenericType && objectType.GetGenericTypeDefinition() == typeof(NodeValue<>);

        public override object ReadJson(JsonReader reader, Type objectType,
            object existingValue, JsonSerializer serializer)
        {
            var innerType = objectType.GetGenericArguments()[0];
            // 박싱된 struct 에 리플렉션으로 채운 뒤 그대로 돌려준다.
            var box = Activator.CreateInstance(objectType);
            var constant = objectType.GetField("constant");
            var source = objectType.GetField("source");

            var token = JToken.Load(reader);

            if (token.Type == JTokenType.Object && token[NodeKey] != null)
            {
                source.SetValue(box, token[NodeKey].Value<int>());
                return box;
            }

            source.SetValue(box, -1);
            if (token.Type != JTokenType.Null)
                constant.SetValue(box, token.ToObject(innerType, serializer));
            return box;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var type = value.GetType();
            var source = (int)type.GetField("source").GetValue(value);

            if (source >= 0)
            {
                writer.WriteStartObject();
                writer.WritePropertyName(NodeKey);
                writer.WriteValue(source);
                writer.WriteEndObject();
                return;
            }

            serializer.Serialize(writer, type.GetField("constant").GetValue(value));
        }
    }
}
