using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine.Networking;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>Generate() 결과 — 대시보드(DeployTab)에서 렌더.</summary>
    public struct IdGenResult
    {
        public bool Ok;               // 에러 0건이면 true (파일 일부 생성돼도 에러 있으면 false)
        public int FileCount;
        public string OutputDir;
        public List<string> Generated; // "EnemyIds (12 ids)"
        public List<string> Skipped;   // "PlayerStatConfig (상수 X · 드롭다운 O)"
        public List<string> Errors;    // "enemy_config: fetch 실패 — ..."
    }

    /// <summary>
    /// [SpecData] + [PrimaryKey] 테이블의 PK 값을 DB에서 읽어 두 가지를 생성한다:
    ///   1. {Name}Ids.g.cs (const string) — 코드 참조용. [SkipIdConstants] 테이블은 제외(손 브리지).
    ///   2. SpecDataIdIndex.g.cs (ByConfig 딕셔너리) — 인스펙터 드롭다운 소스. 모든 테이블 포함(제외 테이블도).
    ///
    /// [SkipIdConstants]는 "코드 상수 클래스는 안 만든다"만 뜻하고, 드롭다운 인덱스에는 그대로 들어간다.
    /// 절대 abort 안 함 — 테이블별 실패는 개별 수집. 불일치 책임은 사용처(파이프라인 런타임 경고).
    ///
    /// 에디터 타임 도구(소스젠 아님). 데이터 소스=anon REST([SpecData]는 public_read RLS).
    /// 트리거=SupaRun 대시보드 > Deploy > Generate Id Constants (수동).
    /// </summary>
    public static class IdConstantGenerator
    {
        const string DefaultOutputDir = "Assets/Generated/SupaRunIds";
        const string IndexClassName = "SpecDataIdIndex";

        public static IdGenResult Generate()
        {
            var result = new IdGenResult
            {
                Generated = new List<string>(),
                Skipped = new List<string>(),
                Errors = new List<string>(),
            };

            var settings = SupaRunSettings.Instance;
            var baseUrl = settings.supabaseUrl;
            var anon = settings.SupabaseAnonKey;
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(anon))
            {
                result.Errors.Add("Supabase URL / anon key 미설정 — Settings에서 연결하세요.");
                return result;
            }

            var (outputDir, ns) = ResolveOutputConfig();
            var types = FindSpecDataTypesWithPk();
            if (types.Count == 0)
            {
                result.Errors.Add("[SpecData] + [PrimaryKey] (string) 타입 없음.");
                return result;
            }

            var restUrl = baseUrl.TrimEnd('/') + "/rest/v1";
            var files = new List<(string name, string content)>();
            var index = new List<(string configName, List<string> ids)>(); // 드롭다운 인덱스 (모든 테이블)

            try
            {
                for (int i = 0; i < types.Count; i++)
                {
                    var type = types[i];
                    EditorUtility.DisplayProgressBar("SupaRun · Id 상수 생성", type.Name, (float)i / types.Count);

                    var pk = AttributeRegistry.Get(type).PrimaryKey;
                    if (pk == null || pk.FieldType != typeof(string)) continue;

                    var table = ToSnakeCase(type.Name);
                    var pkCol = pk.Name.ToLowerInvariant();

                    // 모든 테이블 fetch — [SkipIdConstants] 테이블도 드롭다운 인덱스에는 들어가야 함
                    var (ok, body, httpErr) = HttpGet($"{restUrl}/{table}?select={pkCol}", anon);
                    if (!ok) { result.Errors.Add($"{table}: fetch 실패 — {httpErr}"); continue; }

                    var ids = ParseIds(body, pkCol);
                    if (ids == null) { result.Errors.Add($"{table}: 응답 파싱 실패 — {Trunc(body)}"); continue; }

                    index.Add((type.Name, ids));

                    // 코드 상수 클래스는 손 브리지 테이블 제외
                    if (type.GetCustomAttribute<SkipIdConstantsAttribute>() != null)
                    {
                        result.Skipped.Add($"{type.Name} (상수 X · 드롭다운 O)");
                        continue;
                    }

                    var className = StripConfigSuffix(type.Name) + "Ids";
                    files.Add(($"{className}.g.cs", BuildPlainIds(className, ns, table, ids)));
                    result.Generated.Add($"{className} ({ids.Count} ids)");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            // 드롭다운 인덱스 (한 테이블이라도 읽었으면)
            if (index.Count > 0)
            {
                files.Add(($"{IndexClassName}.g.cs", BuildIndex(ns, index)));
                result.Generated.Add($"{IndexClassName} ({index.Count} tables)");
            }

            if (files.Count > 0)
            {
                Directory.CreateDirectory(outputDir);
                foreach (var (name, content) in files)
                    File.WriteAllText(Path.Combine(outputDir, name), content);
                AssetDatabase.Refresh();
            }

            result.Ok = result.Errors.Count == 0;
            result.FileCount = files.Count;
            result.OutputDir = outputDir;
            return result;
        }

        // ── 타입/설정 수집 ──

        static List<Type> FindSpecDataTypesWithPk()
        {
            var result = new List<Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t.GetCustomAttribute<SpecDataAttribute>() == null) continue;
                    if (AttributeRegistry.Get(t).PrimaryKey != null) result.Add(t);
                }
            }
            return result;
        }

        static (string dir, string ns) ResolveOutputConfig()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                SupaRunIdsConfigAttribute cfg;
                try { cfg = asm.GetCustomAttribute<SupaRunIdsConfigAttribute>(); }
                catch { continue; }
                if (cfg != null)
                    return (string.IsNullOrEmpty(cfg.OutputDir) ? DefaultOutputDir : cfg.OutputDir, cfg.Namespace);
            }
            return (DefaultOutputDir, null);
        }

        // ── 코드 생성 ──

        static string BuildPlainIds(string className, string ns, string table, List<string> ids)
        {
            var pairs = ids
                .Select(id => (id, member: ToPascal(id)))
                .GroupBy(p => p.member).Select(g => g.First())     // 이름 충돌 시 첫째 (드묾)
                .OrderBy(p => p.member, StringComparer.Ordinal)     // 안정 정렬 (DB row 순서 무관)
                .ToList();

            var sb = new StringBuilder();
            var ind = Ind(ns);
            WriteHeader(sb, $"{table} (DB)  |  {pairs.Count} ids");
            OpenNs(sb, ns);
            sb.AppendLine($"{ind}public static class {className}");
            sb.AppendLine($"{ind}{{");
            foreach (var (id, member) in pairs)
                sb.AppendLine($"{ind}    public const string {member} = \"{Esc(id)}\";");
            sb.AppendLine($"{ind}}}");
            CloseNs(sb, ns);
            return sb.ToString();
        }

        /// <summary>모든 SpecData 테이블의 PK 값 인덱스 — SpecDataIdDrawer가 config 이름으로 조회.</summary>
        static string BuildIndex(string ns, List<(string configName, List<string> ids)> index)
        {
            var sb = new StringBuilder();
            var ind = Ind(ns);
            WriteHeader(sb, $"드롭다운 소스 (SpecDataIdDrawer가 config 이름으로 조회)  |  {index.Count} tables");
            OpenNs(sb, ns);
            sb.AppendLine($"{ind}public static class {IndexClassName}");
            sb.AppendLine($"{ind}{{");
            sb.AppendLine($"{ind}    public static readonly System.Collections.Generic.Dictionary<string, string[]> ByConfig =");
            sb.AppendLine($"{ind}        new System.Collections.Generic.Dictionary<string, string[]>");
            sb.AppendLine($"{ind}    {{");
            foreach (var (configName, ids) in index.OrderBy(x => x.configName, StringComparer.Ordinal))
            {
                var sorted = ids.OrderBy(x => x, StringComparer.Ordinal);
                var arr = string.Join(", ", sorted.Select(id => $"\"{Esc(id)}\""));
                sb.AppendLine($"{ind}        {{ \"{Esc(configName)}\", new string[] {{ {arr} }} }},");
            }
            sb.AppendLine($"{ind}    }};");
            sb.AppendLine($"{ind}}}");
            CloseNs(sb, ns);
            return sb.ToString();
        }

        // ── HTTP (동기, UnityWebRequest) ──

        static (bool ok, string body, string err) HttpGet(string url, string anon)
        {
            try
            {
                using var req = UnityWebRequest.Get(url);
                req.SetRequestHeader("apikey", anon);
                req.SetRequestHeader("Authorization", "Bearer " + anon);
                req.timeout = 30;
                req.SendWebRequest();
                // 전송은 Unity 네트워킹 스레드에서 진행 — 메인 스레드 sleep으로 isDone 폴링.
                while (!req.isDone) System.Threading.Thread.Sleep(10);

                if (req.result != UnityWebRequest.Result.Success)
                    return (false, null, $"HTTP {req.responseCode} {req.error}");
                return (true, req.downloadHandler.text, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        static List<string> ParseIds(string body, string pkCol)
        {
            if (string.IsNullOrEmpty(body) || !body.TrimStart().StartsWith("["))
                return null; // 배열이 아니면 REST 오류 객체 → 파싱 실패로 처리

            var list = new List<string>();
            var rx = new Regex($"\"{Regex.Escape(pkCol)}\"\\s*:\\s*\"([^\"]*)\"");
            foreach (Match m in rx.Matches(body))
                list.Add(m.Groups[1].Value);
            return list;
        }

        // ── 문자열 유틸 ──

        static void WriteHeader(StringBuilder sb, string source)
        {
            sb.AppendLine("// <auto-generated>");
            sb.AppendLine("//   SupaRun IdConstantGenerator — 편집 금지.");
            sb.AppendLine("//   재생성: SupaRun 대시보드 > Deploy > Generate Id Constants");
            sb.AppendLine($"//   Source: {source}");
            sb.AppendLine("// </auto-generated>");
            sb.AppendLine();
        }

        static void OpenNs(StringBuilder sb, string ns)
        {
            if (string.IsNullOrEmpty(ns)) return;
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
        }

        static void CloseNs(StringBuilder sb, string ns)
        {
            if (!string.IsNullOrEmpty(ns)) sb.AppendLine("}");
        }

        static string Ind(string ns) => string.IsNullOrEmpty(ns) ? "" : "    ";

        static string Esc(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

        /// <summary>테이블 클래스명의 Config/Data 접미사 제거 — SkillData → SkillIds 처럼 짧은 상수 클래스명을 만든다.</summary>
        static string StripConfigSuffix(string name)
        {
            if (name.EndsWith("Config")) return name.Substring(0, name.Length - "Config".Length);
            if (name.EndsWith("Data")) return name.Substring(0, name.Length - "Data".Length);
            return name;
        }

        static string ToSnakeCase(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(name[i]));
            }
            return sb.ToString();
        }

        /// <summary>kebab/snake id → PascalCase C# 식별자. "crit-prob" → "CritProb".</summary>
        static string ToPascal(string id)
        {
            var parts = id.Split(new[] { '-', '_', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach (var p in parts)
            {
                sb.Append(char.ToUpperInvariant(p[0]));
                if (p.Length > 1) sb.Append(p.Substring(1));
            }

            // 식별자 정제: 영숫자/_ 만 허용, 선두 숫자면 _ 접두.
            var clean = new StringBuilder();
            foreach (var c in sb.ToString())
                clean.Append(char.IsLetterOrDigit(c) ? c : '_');
            var name = clean.ToString();
            if (name.Length == 0 || char.IsDigit(name[0])) name = "_" + name;
            return name;
        }

        static string Trunc(string s) => string.IsNullOrEmpty(s) ? "" : s.Substring(0, Math.Min(200, s.Length));
    }
}
