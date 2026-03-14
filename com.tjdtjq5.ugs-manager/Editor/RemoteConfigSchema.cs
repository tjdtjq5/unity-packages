#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    /// <summary>.schema.json 파싱 + 저장 + 키 관리</summary>
    static class RemoteConfigSchema
    {
        // ─── 파싱 ───────────────────────────────────────

        public static SchemaData Parse(string json)
        {
            var data = new SchemaData();
            if (string.IsNullOrEmpty(json)) return data;

            ParseGroups(json, data);
            ParseEnumDefs(json, data);
            ParseEnumMap(json, data);
            ParseLists(json, data);

            return data;
        }

        static void ParseGroups(string json, SchemaData data)
        {
            int idx = json.IndexOf("\"groups\"", StringComparison.Ordinal);
            if (idx < 0) return;

            int arrStart = json.IndexOf('[', idx);
            int arrEnd = FindBracket(json, arrStart);
            string block = json.Substring(arrStart + 1, arrEnd - arrStart - 1);

            int searchFrom = 0;
            while (true)
            {
                int objStart = block.IndexOf('{', searchFrom);
                if (objStart < 0) break;
                int objEnd = FindBrace(block, objStart);
                string obj = block.Substring(objStart, objEnd - objStart + 1);

                string name = ExtractStr(obj, "name");
                string color = ExtractStr(obj, "color");
                var keys = ExtractStrArray(obj, "keys");

                if (!string.IsNullOrEmpty(name))
                {
                    ColorUtility.TryParseHtmlString(color, out Color c);
                    data.Groups.Add(new GroupInfo { Name = name, Color = c, Keys = keys });
                }

                searchFrom = objEnd + 1;
            }
        }

        static void ParseEnumDefs(string json, SchemaData data)
        {
            int idx = json.IndexOf("\"enumDefs\"", StringComparison.Ordinal);
            if (idx < 0)
            {
                // v1 호환: "enums" 필드
                idx = json.IndexOf("\"enums\"", StringComparison.Ordinal);
                if (idx < 0) return;
            }

            int braceStart = json.IndexOf('{', idx + 8);
            int braceEnd = FindBrace(json, braceStart);
            string block = json.Substring(braceStart + 1, braceEnd - braceStart - 1);

            int searchFrom = 0;
            while (searchFrom < block.Length)
            {
                int ks = block.IndexOf('"', searchFrom);
                if (ks < 0) break;
                int ke = block.IndexOf('"', ks + 1);
                if (ke < 0) break;
                string key = block.Substring(ks + 1, ke - ks - 1);

                // 값이 배열이면 직접 옵션 (v2 enumDefs)
                // 값이 객체이면 v1의 enums.{key}.options
                int nextChar = ke + 1;
                while (nextChar < block.Length && (block[nextChar] == ':' || block[nextChar] == ' ' || block[nextChar] == '\n' || block[nextChar] == '\r' || block[nextChar] == '\t'))
                    nextChar++;

                if (nextChar < block.Length && block[nextChar] == '[')
                {
                    // v2: "EventType": ["a", "b", ...]
                    var options = ExtractStrArrayAt(block, nextChar);
                    int arrEnd = block.IndexOf(']', nextChar);
                    data.EnumDefs[key] = new List<string>(options);
                    searchFrom = arrEnd + 1;
                }
                else if (nextChar < block.Length && block[nextChar] == '{')
                {
                    // v1: "event_name": { "options": [...] }
                    int objEnd = FindBrace(block, nextChar);
                    string obj = block.Substring(nextChar, objEnd - nextChar + 1);
                    var options = ExtractStrArray(obj, "options");
                    data.EnumDefs[key] = new List<string>(options);

                    // v1에서는 키 이름 = enum 이름이자 매핑
                    data.EnumMap[key] = key;

                    searchFrom = objEnd + 1;
                }
                else
                {
                    searchFrom = nextChar + 1;
                }
            }
        }

        static void ParseEnumMap(string json, SchemaData data)
        {
            int idx = json.IndexOf("\"enumMap\"", StringComparison.Ordinal);
            if (idx < 0) return;

            int braceStart = json.IndexOf('{', idx + 9);
            int braceEnd = FindBrace(json, braceStart);
            string block = json.Substring(braceStart + 1, braceEnd - braceStart - 1);

            int searchFrom = 0;
            while (searchFrom < block.Length)
            {
                int ks = block.IndexOf('"', searchFrom);
                if (ks < 0) break;
                int ke = block.IndexOf('"', ks + 1);
                if (ke < 0) break;
                string key = block.Substring(ks + 1, ke - ks - 1);

                int vs = block.IndexOf('"', ke + 1);
                if (vs < 0) break;
                int ve = block.IndexOf('"', vs + 1);
                if (ve < 0) break;
                string val = block.Substring(vs + 1, ve - vs - 1);

                data.EnumMap[key] = val;
                searchFrom = ve + 1;
            }
        }

        static void ParseLists(string json, SchemaData data)
        {
            int idx = json.IndexOf("\"lists\"", StringComparison.Ordinal);
            if (idx < 0) return;

            int braceStart = json.IndexOf('{', idx + 7);
            int braceEnd = FindBrace(json, braceStart);
            string block = json.Substring(braceStart + 1, braceEnd - braceStart - 1);

            int searchFrom = 0;
            while (searchFrom < block.Length)
            {
                int ks = block.IndexOf('"', searchFrom);
                if (ks < 0) break;
                int ke = block.IndexOf('"', ks + 1);
                if (ke < 0) break;
                string key = block.Substring(ks + 1, ke - ks - 1);

                int objStart = block.IndexOf('{', ke);
                if (objStart < 0) break;
                int objEnd = FindBrace(block, objStart);
                string obj = block.Substring(objStart, objEnd - objStart + 1);

                string itemType = ExtractStr(obj, "itemType");
                string enumSchema = ExtractStr(obj, "enumSchema");

                data.Lists[key] = new ListSchemaInfo
                {
                    ItemType = string.IsNullOrEmpty(itemType) ? "STRING" : itemType.ToUpper(),
                    EnumSchema = enumSchema
                };

                searchFrom = objEnd + 1;
            }
        }

        // ─── 저장 ───────────────────────────────────────

        public static void Save(string filePath, SchemaData schema)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");

            // groups
            sb.AppendLine("  \"groups\": [");
            for (int i = 0; i < schema.Groups.Count; i++)
            {
                var g = schema.Groups[i];
                string color = "#" + ColorUtility.ToHtmlStringRGB(g.Color);
                string keys = string.Join(", ", g.Keys.Select(k => $"\"{k}\""));
                string comma = i < schema.Groups.Count - 1 ? "," : "";
                sb.AppendLine($"    {{ \"name\": \"{g.Name}\", \"color\": \"{color}\", \"keys\": [{keys}] }}{comma}");
            }
            sb.AppendLine("  ],");

            // enumDefs
            sb.AppendLine("  \"enumDefs\": {");
            var enumKeys = schema.EnumDefs.Keys.ToList();
            for (int i = 0; i < enumKeys.Count; i++)
            {
                string opts = string.Join(", ", schema.EnumDefs[enumKeys[i]].Select(o => $"\"{o}\""));
                string comma = i < enumKeys.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{enumKeys[i]}\": [{opts}]{comma}");
            }
            sb.AppendLine("  },");

            // enumMap
            sb.AppendLine("  \"enumMap\": {");
            var mapKeys = schema.EnumMap.Keys.ToList();
            for (int i = 0; i < mapKeys.Count; i++)
            {
                string comma = i < mapKeys.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{mapKeys[i]}\": \"{schema.EnumMap[mapKeys[i]]}\"{comma}");
            }
            sb.AppendLine("  },");

            // lists
            sb.AppendLine("  \"lists\": {");
            var listKeys = schema.Lists.Keys.ToList();
            for (int i = 0; i < listKeys.Count; i++)
            {
                var info = schema.Lists[listKeys[i]];
                string enumPart = !string.IsNullOrEmpty(info.EnumSchema)
                    ? $", \"enumSchema\": \"{info.EnumSchema}\""
                    : "";
                string comma = i < listKeys.Count - 1 ? "," : "";
                sb.AppendLine($"    \"{listKeys[i]}\": {{ \"itemType\": \"{info.ItemType}\"{enumPart} }}{comma}");
            }
            sb.AppendLine("  }");

            sb.AppendLine("}");

            File.WriteAllText(filePath, sb.ToString());
        }

        // ─── 키 관리 ────────────────────────────────────

        public static void RenameKey(SchemaData schema, string oldKey, string newKey)
        {
            // groups
            for (int i = 0; i < schema.Groups.Count; i++)
            {
                var g = schema.Groups[i];
                for (int j = 0; j < g.Keys.Length; j++)
                {
                    if (g.Keys[j] == oldKey) g.Keys[j] = newKey;
                }
            }

            // enumMap
            if (schema.EnumMap.TryGetValue(oldKey, out string enumVal))
            {
                schema.EnumMap.Remove(oldKey);
                schema.EnumMap[newKey] = enumVal;
            }

            // lists
            if (schema.Lists.TryGetValue(oldKey, out var listVal))
            {
                schema.Lists.Remove(oldKey);
                schema.Lists[newKey] = listVal;
            }
        }

        public static void RemoveKey(SchemaData schema, string key)
        {
            for (int i = 0; i < schema.Groups.Count; i++)
            {
                var g = schema.Groups[i];
                var keys = g.Keys.ToList();
                keys.Remove(key);
                schema.Groups[i] = new GroupInfo { Name = g.Name, Color = g.Color, Keys = keys.ToArray() };
            }

            schema.EnumMap.Remove(key);
            schema.Lists.Remove(key);
        }

        // ─── JSON 유틸 ──────────────────────────────────

        static string ExtractStr(string block, string field)
        {
            string key = $"\"{field}\"";
            int ki = block.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return "";
            int colon = block.IndexOf(':', ki + key.Length);
            if (colon < 0) return "";
            int qs = block.IndexOf('"', colon + 1);
            if (qs < 0) return "";
            int qe = block.IndexOf('"', qs + 1);
            return qe > qs ? block.Substring(qs + 1, qe - qs - 1) : "";
        }

        static string[] ExtractStrArray(string block, string field)
        {
            string key = $"\"{field}\"";
            int ki = block.IndexOf(key, StringComparison.Ordinal);
            if (ki < 0) return Array.Empty<string>();
            int arrS = block.IndexOf('[', ki);
            if (arrS < 0) return Array.Empty<string>();
            return ExtractStrArrayAt(block, arrS);
        }

        static string[] ExtractStrArrayAt(string block, int arrStart)
        {
            int arrEnd = block.IndexOf(']', arrStart);
            if (arrEnd < 0) return Array.Empty<string>();
            string arrStr = block.Substring(arrStart + 1, arrEnd - arrStart - 1);

            var result = new List<string>();
            int i = 0;
            while (i < arrStr.Length)
            {
                int qs = arrStr.IndexOf('"', i);
                if (qs < 0) break;
                int qe = arrStr.IndexOf('"', qs + 1);
                if (qe < 0) break;
                result.Add(arrStr.Substring(qs + 1, qe - qs - 1));
                i = qe + 1;
            }
            return result.ToArray();
        }

        static int FindBrace(string json, int open)
        {
            int depth = 1;
            for (int i = open + 1; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}') { depth--; if (depth == 0) return i; }
            }
            return json.Length - 1;
        }

        static int FindBracket(string json, int open)
        {
            int depth = 1;
            for (int i = open + 1; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']') { depth--; if (depth == 0) return i; }
            }
            return json.Length - 1;
        }
    }
}
#endif
