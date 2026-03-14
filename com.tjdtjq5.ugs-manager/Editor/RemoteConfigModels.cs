#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tjdtjq5.UGSManager
{
    // ─── 데이터 모델 ────────────────────────────────

    class ConfigEntry
    {
        // 키
        public string Key;
        public string OrigKey;
        public bool IsEditingKey;

        // 타입
        public string DisplayType;       // FLOAT, INT, BOOL, STRING, ENUM (LIST 제거)
        public string OrigDisplayType;
        public string BaseType;          // .rc 기준 (ENUM → STRING, 리스트 → STRING)

        // 리스트 (체크박스)
        public bool IsList;
        public bool OrigIsList;
        public bool IsExpanded;          // 리스트 펼침/접힘

        // 값
        public string Value;
        public string EditValue;
        public bool IsDirty;

        // ENUM
        public string EnumSchemaKey;
        public string OrigEnumSchemaKey;
        public string[] EnumOptions;
        public int EnumIndex;

        // LIST
        public string ListItemType;      // INT, FLOAT, STRING, BOOL, ENUM
        public string OrigListItemType;
        public string ListEnumSchemaKey;
        public string OrigListEnumSchemaKey;
        public List<string> ListItems = new();
        public List<string> OrigListItems;

        /// <summary>현재 상태를 원본으로 확정 (Save 시)</summary>
        public void CommitAsOriginal()
        {
            OrigKey = Key;
            OrigDisplayType = DisplayType;
            OrigIsList = IsList;
            Value = IsList ? string.Join(",", ListItems) : EditValue;
            OrigEnumSchemaKey = EnumSchemaKey;
            OrigListItemType = ListItemType;
            OrigListEnumSchemaKey = ListEnumSchemaKey;
            OrigListItems = ListItems != null ? new List<string>(ListItems) : null;
            IsDirty = false;
        }

        /// <summary>원본으로 되돌리기 (Revert 시)</summary>
        public void Revert()
        {
            Key = OrigKey;
            DisplayType = OrigDisplayType;
            IsList = OrigIsList;
            BaseType = DisplayType == "ENUM" || IsList ? "STRING" : DisplayType;
            EditValue = Value;
            EnumSchemaKey = OrigEnumSchemaKey;
            ListItemType = OrigListItemType;
            ListEnumSchemaKey = OrigListEnumSchemaKey;
            IsEditingKey = false;

            if (OrigIsList && OrigListItems != null)
                ListItems = new List<string>(OrigListItems);
            else if (OrigIsList)
                ListItems = string.IsNullOrEmpty(Value) ? new List<string>() : Value.Split(',').Select(s => s.Trim()).ToList();

            if (OrigDisplayType == "ENUM" && EnumOptions != null)
            {
                EnumIndex = Array.IndexOf(EnumOptions, Value);
                if (EnumIndex < 0) EnumIndex = 0;
            }

            IsDirty = false;
        }

        /// <summary>dirty 여부 재계산</summary>
        public void RecalcDirty()
        {
            string currentVal = IsList ? string.Join(",", ListItems) : EditValue;
            bool valChanged = currentVal != Value;
            bool typeChanged = DisplayType != OrigDisplayType;
            bool listChanged = IsList != OrigIsList;
            bool keyChanged = Key != OrigKey;
            bool enumSchemaChanged = EnumSchemaKey != OrigEnumSchemaKey;
            bool listTypeChanged = ListItemType != OrigListItemType;
            bool listEnumChanged = ListEnumSchemaKey != OrigListEnumSchemaKey;
            IsDirty = valChanged || typeChanged || listChanged || keyChanged || enumSchemaChanged || listTypeChanged || listEnumChanged;
        }
    }

    // ─── 스키마 모델 ────────────────────────────────

    class SchemaData
    {
        public List<GroupInfo> Groups = new();
        public Dictionary<string, List<string>> EnumDefs = new();
        public Dictionary<string, string> EnumMap = new();
        public Dictionary<string, ListSchemaInfo> Lists = new();
    }

    struct GroupInfo
    {
        public string Name;
        public Color Color;
        public string[] Keys;
    }

    struct ListSchemaInfo
    {
        public string ItemType;
        public string EnumSchema;
    }

    // ─── 타입 변환 ──────────────────────────────────

    static class ConfigTypeConverter
    {
        static readonly string[] BASE_TYPES = { "FLOAT", "INT", "BOOL", "STRING", "ENUM" };
        public static string[] BaseTypes => BASE_TYPES;

        /// <summary>단일 값 → LIST 변환</summary>
        public static void SingleToList(ConfigEntry entry, SchemaData schema)
        {
            string val = entry.EditValue ?? "";

            // 현재 타입을 아이템 타입으로
            string itemType = entry.OrigDisplayType;
            if (itemType == "LIST" || itemType == "ENUM")
                itemType = "STRING";

            // ENUM → LIST(ENUM): 스키마 유지
            string enumSchema = null;
            if (entry.OrigDisplayType == "ENUM" && !string.IsNullOrEmpty(entry.EnumSchemaKey))
            {
                itemType = "ENUM";
                enumSchema = entry.EnumSchemaKey;
            }

            entry.ListItemType = itemType;
            entry.ListEnumSchemaKey = enumSchema;
            entry.ListItems = string.IsNullOrEmpty(val) ? new List<string>() : new List<string> { val };
        }

        /// <summary>LIST → 단일 값 변환</summary>
        public static string ListToSingle(ConfigEntry entry, string targetType)
        {
            if (entry.ListItems == null || entry.ListItems.Count == 0)
                return GetDefault(targetType);

            if (targetType == "STRING")
                return string.Join(",", entry.ListItems);

            return ConvertValue(entry.ListItems[0], entry.ListItemType, targetType);
        }

        /// <summary>타입 간 값 변환</summary>
        public static string ConvertValue(string value, string fromType, string toType, string[] enumOpts = null)
        {
            if (string.IsNullOrEmpty(value)) return GetDefault(toType);

            switch (toType)
            {
                case "INT":
                    if (fromType == "FLOAT" && value.Contains('.'))
                        return ((int)Math.Truncate(double.TryParse(value, out var d) ? d : 0)).ToString();
                    return int.TryParse(value, out var i) ? i.ToString() : "0";

                case "FLOAT":
                    return float.TryParse(value, out var f) ? f.ToString() : "0";

                case "BOOL":
                    return (value == "true" || value == "1") ? "true" : "false";

                case "STRING":
                    return value;

                case "ENUM":
                    if (enumOpts != null && Array.IndexOf(enumOpts, value) >= 0) return value;
                    return enumOpts is { Length: > 0 } ? enumOpts[0] : value;

                default:
                    return value;
            }
        }

        /// <summary>LIST 아이템 타입 변환</summary>
        public static List<string> ConvertListItems(List<string> items, string fromType, string toType, string[] enumOpts = null)
        {
            return items.Select(item => ConvertValue(item, fromType, toType, enumOpts)).ToList();
        }

        public static string GetDefault(string type)
        {
            return type switch
            {
                "INT" => "0",
                "FLOAT" => "0",
                "BOOL" => "false",
                _ => ""
            };
        }
    }
}
#endif
