using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace N4EGuard.Editor
{
    /// <summary>
    /// Netcode for Entities coding rule validator.
    /// Auto-runs after compilation via [InitializeOnLoad].
    /// Scans project .cs files and reports violations as Console warnings.
    ///
    /// Rules:
    /// B4 — [GhostField] on int/byte without Quantization=0
    /// B5 — Side-effects in PredictedSimulationSystemGroup
    /// B6 — ReceiveRpcCommandRequest without DestroyEntity
    /// B7 — ISystem without [WorldSystemFilter]
    ///
    /// Suppress per line: // N4EGuard:ignore B4
    /// Suppress all on line: // N4EGuard:ignore
    /// </summary>
    [InitializeOnLoad]
    static class N4EGuardValidator
    {
        const string ScanRoot = "Assets/_Project/2_Scripts";
        const string IgnoreTag = "N4EGuard:ignore";
        const string LogPrefix = "[N4EGuard]";

        // B4: integer types that need Quantization=0 on GhostField
        static readonly HashSet<string> IntegerTypes = new()
        {
            "int", "byte", "short", "ushort", "uint", "long", "ulong", "sbyte",
        };

        // B5: side-effect patterns forbidden in prediction systems
        static readonly string[] PredictionSideEffects =
        {
            ".CreateEntity(",
            "PoolSystem",
            "AudioSource.Play",
            "Object.Instantiate(",
            "GameObject.Instantiate(",
        };

        // B7: systems intentionally running in all worlds (no filter needed)
        static readonly HashSet<string> WorldFilterAllowlist = new()
        {
            "LateWorldRegisterSystem",
        };

        static readonly Regex IntegerFieldRegex = new(
            @"\b(int|byte|short|ushort|uint|long|ulong|sbyte)\s+\w+",
            RegexOptions.Compiled);

        static readonly Regex ISystemDeclRegex = new(
            @"struct\s+(\w+)\s*:.*\bISystem\b",
            RegexOptions.Compiled);

        static N4EGuardValidator()
        {
            if (Application.isBatchMode) return;
            EditorApplication.delayCall += RunValidation;
        }

        static void RunValidation()
        {
            if (!Directory.Exists(ScanRoot)) return;

            var files = Directory.GetFiles(ScanRoot, "*.cs", SearchOption.AllDirectories);
            int warnings = 0;

            foreach (var filePath in files)
            {
                var normalized = filePath.Replace('\\', '/');

                // Skip Editor folders
                if (normalized.Contains("/Editor/") || normalized.Contains("/_Editor/"))
                    continue;

                string[] lines;
                try { lines = File.ReadAllLines(filePath); }
                catch { continue; }

                warnings += CheckB4(normalized, lines);
                warnings += CheckB5(normalized, lines);
                warnings += CheckB6(normalized, lines);
                warnings += CheckB7(normalized, lines);
            }
        }

        // ── B4: [GhostField] on integer type without Quantization=0 ──

        static int CheckB4(string file, string[] lines)
        {
            int warnings = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimStart();
                if (line.StartsWith("//")) continue;
                if (!line.Contains("[GhostField")) continue;
                if (IsSuppressed(lines[i], "B4")) continue;
                if (line.Contains("Quantization")) continue;

                // Find the field declaration belonging to THIS [GhostField].
                // Case 1: inline — [GhostField] public int Value;
                string fieldPart = null;
                int closeBracket = line.LastIndexOf(']');
                if (closeBracket >= 0 && closeBracket < line.Length - 1)
                {
                    var after = line.Substring(closeBracket + 1).TrimStart();
                    if (after.Length > 0 && !after.StartsWith("["))
                        fieldPart = after;
                }

                // Case 2: separate line — skip other attributes, find first field line
                if (string.IsNullOrEmpty(fieldPart))
                {
                    for (int j = i + 1; j < Math.Min(i + 5, lines.Length); j++)
                    {
                        var next = lines[j].TrimStart();
                        if (string.IsNullOrWhiteSpace(next)) continue;
                        if (next.StartsWith("//")) continue;
                        if (next.StartsWith("[")) continue; // skip stacked attributes
                        fieldPart = next;
                        break;
                    }
                }

                if (fieldPart != null && IntegerFieldRegex.IsMatch(fieldPart))
                {
                    Warn("B4", file, i + 1,
                        "[GhostField] on integer type without Quantization=0 — precision loss risk");
                    warnings++;
                }
            }

            return warnings;
        }

        // ── B5: Side-effects in PredictedSimulationSystemGroup ──

        static int CheckB5(string file, string[] lines)
        {
            // First pass: is this a prediction system?
            bool isPrediction = false;
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//")) continue;
                if (trimmed.Contains("PredictedSimulationSystemGroup"))
                {
                    isPrediction = true;
                    break;
                }
            }
            if (!isPrediction) return 0;

            // Second pass: find side-effect calls
            int warnings = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//")) continue;
                if (IsSuppressed(lines[i], "B5")) continue;

                foreach (var pattern in PredictionSideEffects)
                {
                    if (trimmed.Contains(pattern))
                    {
                        Warn("B5", file, i + 1,
                            $"Side-effect in PredictedSimulationSystemGroup — rollback will re-execute: {pattern.Trim()}");
                        warnings++;
                        break;
                    }
                }
            }

            return warnings;
        }

        // ── B6: ReceiveRpcCommandRequest without DestroyEntity ──

        static int CheckB6(string file, string[] lines)
        {
            int rpcLine = -1;
            bool hasDestroy = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//")) continue;

                if (trimmed.Contains("ReceiveRpcCommandRequest") && rpcLine < 0)
                    rpcLine = i;

                if (trimmed.Contains("DestroyEntity"))
                    hasDestroy = true;
            }

            if (rpcLine < 0 || hasDestroy) return 0;

            // Check file-level suppress (first 10 lines)
            for (int i = 0; i < Math.Min(lines.Length, 10); i++)
            {
                if (IsSuppressed(lines[i], "B6")) return 0;
            }

            Warn("B6", file, rpcLine + 1,
                "ReceiveRpcCommandRequest without DestroyEntity — RPC will re-process every frame");
            return 1;
        }

        // ── B7: ISystem without [WorldSystemFilter] ──

        static int CheckB7(string file, string[] lines)
        {
            string systemName = null;
            int systemLine = -1;

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//")) continue;

                var match = ISystemDeclRegex.Match(trimmed);
                if (match.Success)
                {
                    systemName = match.Groups[1].Value;
                    systemLine = i;
                    break;
                }
            }

            if (systemName == null) return 0;
            if (WorldFilterAllowlist.Contains(systemName)) return 0;
            if (IsSuppressed(lines[systemLine], "B7")) return 0;

            // Check if WorldSystemFilter or implicit filter exists
            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//")) continue;
                if (trimmed.Contains("WorldSystemFilter")) return 0;
                // PredictedSimulationSystemGroup implies both-worlds prediction — intentional
                if (trimmed.Contains("PredictedSimulationSystemGroup")) return 0;
            }

            Warn("B7", file, systemLine + 1,
                $"ISystem '{systemName}' has no [WorldSystemFilter] — runs in all worlds");
            return 1;
        }

        // ── Helpers ──

        static bool IsSuppressed(string line, string rule)
        {
            int idx = line.IndexOf(IgnoreTag, StringComparison.Ordinal);
            if (idx < 0) return false;

            // "N4EGuard:ignore" with no rule → suppress all
            string after = line.Substring(idx + IgnoreTag.Length).TrimStart();
            if (string.IsNullOrEmpty(after) || after[0] == '*')
                return true;

            // "N4EGuard:ignore B4 B7" → check if specific rule is listed
            return after.Contains(rule);
        }

        static void Warn(string rule, string file, int line, string message)
        {
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(file);
            Debug.LogWarning($"{LogPrefix} {rule}: {message}\n{file}:{line}", obj);
        }
    }
}
