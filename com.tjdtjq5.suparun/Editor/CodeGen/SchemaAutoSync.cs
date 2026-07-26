using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 컴파일이 끝나면 스키마(마이그레이션 + RLS 정책 + 어드민 메타)를 Supabase 에 직접 반영한다. (ADR-0004 결정 5)
    ///
    /// 왜: [SpecData] 에 필드 하나를 추가할 때마다 서버를 재배포하면 2분을 기다린다(실측 94초).
    /// 스키마와 메타는 서버 코드가 아니라 DB 에 있으면 되므로, Management API 로 바로 밀어 넣는다.
    /// 게임 클라이언트는 원래 [SpecData] 를 Supabase REST 로 직접 읽으므로 즉시 반영되고,
    /// 어드민은 suparun_meta 를 읽어 표를 그리므로 새로고침만 하면 된다.
    ///
    /// 안전장치:
    ///   - 생성된 SQL 해시가 직전과 같으면 아무것도 하지 않는다. 컴파일마다 네트워크를 때리지 않는다.
    ///   - 마이그레이션 SQL 은 전부 멱등(CREATE ... IF NOT EXISTS)이라 반복 실행이 안전하다.
    ///   - 실패해도 에디터를 막지 않는다. 경고만 남기고 해시는 갱신하지 않아 다음 컴파일에 재시도한다.
    ///
    /// 이 훅은 **스키마만** 다룬다. [Service]/[API] 같은 실행 코드는 여전히 배포가 필요하다.
    /// </summary>
    public static class SchemaAutoSync
    {
        const string EnabledKey = "SupaRun.AutoSyncSchema";
        const string LegacyHashFile = "ProjectSettings/SupaRunSchemaHash.txt";

        /// <summary>
        /// 반영 상태는 **환경마다 다르다.** 파일 하나를 공유하면 dev 에 반영한 해시 때문에
        /// prod 반영이 "변경 없음" 으로 스킵되고, 그것도 조용히 스킵된다.
        /// </summary>
        static string HashFileFor(string envName) =>
            $"ProjectSettings/SupaRunSchemaHash.{Sanitize(envName)}.txt";

        /// <summary>
        /// 아이콘 반영 기록은 스키마 것과 **다른 파일**에 둔다. 스키마 쪽 <see cref="Prune"/> 가
        /// "이번에 생성된 마이그레이션 파일" 에 없는 키를 버리므로, 같은 파일에 넣으면
        /// 다음 컴파일에 조용히 지워지고 아이콘을 매번 다시 올리게 된다.
        /// </summary>
        static string AdminAssetsHashFileFor(string envName) =>
            $"ProjectSettings/SupaRunAdminAssetsHash.{Sanitize(envName)}.txt";

        static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return "default";
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        /// <summary>
        /// 환경 도입 전의 해시 파일을 첫 환경 것으로 물려받는다. 멱등.
        /// 물려받지 않으면 이미 반영된 스키마를 전부 다시 밀어 넣게 된다(멱등이라 무해하지만 느리다).
        /// </summary>
        static void MigrateLegacyHashFile(string envName)
        {
            try
            {
                var target = HashFileFor(envName);
                if (File.Exists(target) || !File.Exists(LegacyHashFile)) return;
                File.Move(LegacyHashFile, target);
                Debug.Log($"[SupaRun:Schema] 반영 기록을 환경 '{envName}' 것으로 옮겼습니다.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SupaRun:Schema] 반영 기록 이관 실패 — {ex.Message}");
            }
        }

        /// <summary>
        /// **기본 꺼짐.** 켜면 컴파일할 때마다 실제 DB 에 스키마가 반영된다.
        ///
        /// 기본값이 false 인 이유: 처음 켜는 순간 [UserData] 테이블에 RLS 정책이 새로 생긴다.
        /// 지금은 정책이 하나도 없어 anon 이 완전 차단된 상태인데, 그 문을 여는 변경이다.
        /// 게임 클라이언트도 같은 anon key 를 쓰므로 **한 번은 사람이 SQL 을 보고 켜야 한다.**
        /// 켠 뒤에는 ADR-0004 결정 5 대로 컴파일마다 자동 반영된다.
        /// </summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, false);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        /// <summary>
        /// 반영하면 무엇이 생기는지 요약한다. raw SQL 을 다 읽는 사람은 없으므로 개수만 센다.
        /// 직전 반영과 같으면 그 사실도 알려준다.
        /// </summary>
        public static string Summarize()
        {
            List<GeneratedFile> files;
            try
            {
                files = DeployManager.GenerateSchemaSql();
            }
            catch (Exception ex)
            {
                return $"SQL 생성 실패 — {ex.Message}";
            }
            if (files == null || files.Count == 0) return "[SpecData]/[UserData] 클래스가 없습니다.";

            int policies = 0, triggers = 0, tables = 0, addColumns = 0;
            foreach (var f in files)
            {
                policies += CountOccurrences(f.Content, "CREATE POLICY ");
                triggers += CountOccurrences(f.Content, "CREATE TRIGGER ");
                tables += CountOccurrences(f.Content, "CREATE TABLE IF NOT EXISTS ");
                addColumns += CountOccurrences(f.Content, "ADD COLUMN IF NOT EXISTS ");
            }

            // 요약도 **편집 환경 기준**이다. 반영 기록이 환경마다 다르므로
            // 어느 환경을 보고 있는지에 따라 "변경됨" 판정이 달라진다.
            var stored = ReadStoredHashes(HashFileFor(SupaRunSettings.Instance.EditorEnvironment));
            var changed = files
                .Where(f => !stored.TryGetValue(Path.GetFileName(f.Path), out var h) || h != HashOf(f.Content))
                .Select(f => Path.GetFileName(f.Path))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.AppendLine(changed.Count == 0
                ? "직전 반영과 동일 — 눌러도 바뀌는 것이 없습니다."
                : $"바뀐 파일 {changed.Count}개 — {string.Join(", ", changed)}");
            sb.AppendLine($"  마이그레이션 파일  {files.Count}개 (전체)");
            sb.AppendLine($"  테이블(있으면 유지) {tables}개");
            sb.AppendLine($"  컬럼 추가 시도      {addColumns}개");
            sb.AppendLine($"  RLS 정책            {policies}개");
            sb.Append($"  변경이력 트리거      {triggers}개");
            return sb.ToString();
        }

        static int CountOccurrences(string text, string token)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(token, i, StringComparison.Ordinal)) >= 0) { n++; i += token.Length; }
            return n;
        }

        [DidReloadScripts]
        static void OnScriptsReloaded()
        {
            // 도메인 리로드는 Play 진입/종료에도 일어난다. 그때 DB 를 건드릴 이유가 없다.
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!Enabled) return;

            // 컴파일 직후는 에디터가 바쁘다. 한 프레임 물러나 실행한다.
            EditorApplication.delayCall += () => Sync(silent: true).Forget();
        }

        /// <summary>대시보드 버튼용 — 해시가 같아도 강제로 밀어 넣는다. 대상은 현재 편집 환경.</summary>
        public static UniTask SyncNow() => Sync(silent: false, force: true);

        /// <summary>
        /// **지정한 환경**에 스키마를 반영한다. prod 는 컴파일로 자동 반영되지 않으므로 이 경로로만 바뀐다.
        ///
        /// 편집 환경을 prod 로 잠깐 바꿔서 반영하는 방식은 쓰지 않는다 —
        /// 되돌리는 것을 잊으면 그 다음 컴파일이 곧바로 라이브 스키마를 건드린다.
        /// </summary>
        public static UniTask SyncToEnvironment(SupaRunSettings.EnvironmentData env, bool force = true) =>
            Sync(silent: false, force: force, target: env);

        /// <summary>
        /// 아이콘 썸네일 + 어드레서블 주소 맵을 suparun_meta 에 반영한다.
        ///
        /// **어드민 페이지를 여는 시점에만** 호출한다. SpriteAtlas 를 열어 PNG 를 base64 로 굽고
        /// Addressables 를 전수 스캔하므로 컴파일마다 돌리면 에디터가 멈춘다.
        /// 어드민이 실제로 아이콘을 필요로 하는 순간이 정확히 여기다.
        ///
        /// 실패해도 어드민은 열린다 — 아이콘이 텍스트로 대체될 뿐이다.
        /// </summary>
        public static async UniTask SyncAdminAssets()
        {
            var settings = SupaRunSettings.Instance;
            if (settings == null) return;
            var token = settings.SupabaseAccessToken;
            var projectId = settings.SupabaseProjectId;
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(projectId)) return;

            // 대상은 편집 환경이다 — 위 두 값이 Current 에서 나온다. 기록도 같은 기준이어야
            // dev 에 올린 해시가 prod 반영을 스킵시키지 않는다.
            var hashFile = AdminAssetsHashFileFor(settings.Current.name);
            try
            {
                EditorUtility.DisplayProgressBar("SupaRun", "아이콘 맵 생성 중…", 0.3f);
                var sql = DeployManager.GenerateAdminAssetsSql();
                if (string.IsNullOrEmpty(sql)) return;

                // 스프라이트를 건드리지 않은 날에 300KB 넘는 base64 를 다시 올릴 이유가 없다.
                // 스키마 쪽과 같은 판단이지만 여기서는 그 왕복이 **어드민이 열리는 시간에 직접 얹힌다.**
                var hash = HashOf(sql);
                if (ReadStoredHash(hashFile) == hash) return;

                EditorUtility.DisplayProgressBar("SupaRun", "어드민 자산 반영 중…", 0.7f);
                // 어드민을 여는 흐름 중이라 모달을 띄우지 않는다 — 아이콘이 없어도 어드민은 열린다.
                var r = await SupabaseManagementApi.RunQuery(projectId, token, sql);
                if (r.LogIfFailed("어드민 자산 반영"))
                    WriteStoredHash(hashFile, hash);   // 실패하면 남기지 않는다 — 다음에 재시도한다
                else
                    Debug.LogWarning("[SupaRun:Schema] 아이콘이 텍스트로 표시됩니다.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SupaRun:Schema] 어드민 자산 반영 예외 — {ex.Message}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static async UniTask Sync(bool silent, bool force = false,
            SupaRunSettings.EnvironmentData target = null)
        {
            var settings = SupaRunSettings.Instance;
            if (settings == null) return;

            // 대상 미지정이면 편집 환경. 컴파일 훅이 이 경로로 들어온다.
            var env = target ?? settings.Current;
            var token = SupaRunSettings.AccessTokenOf(env);
            var projectId = SupaRunSettings.ProjectIdOf(env.supabaseUrl);
            var hashFile = HashFileFor(env.name);
            MigrateLegacyHashFile(settings.Environments.Count > 0 ? settings.Environments[0].name : env.name);

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(projectId))
            {
                if (!silent)
                    Debug.LogWarning(
                        $"[SupaRun:Schema] 환경 '{env.name}' 에 Supabase Access Token 또는 URL 이 없습니다.");
                return;
            }

            List<GeneratedFile> sqlFiles;
            try
            {
                var generated = DeployManager.GenerateSchemaSql();
                if (generated == null || generated.Count == 0) return;   // [SpecData]/[UserData] 없음

                // 이름순 정렬은 서버(Program.cs)의 실행 순서와 같아야 한다 —
                // _suparun_core.sql 이 먼저 와야 다른 파일의 정책이 is_admin() 을 찾는다.
                sqlFiles = generated
                    .OrderBy(f => Path.GetFileName(f.Path), StringComparer.Ordinal)
                    .ToList();
            }
            catch (Exception ex)
            {
                if (!silent) Debug.LogWarning($"[SupaRun:Schema] SQL 생성 예외 — {ex.Message}");
                return;
            }

            // 파일별로 비교해 **바뀐 것만** 고른다.
            // 전체를 한 덩어리로 해시하면 테이블 하나만 고쳐도 22개를 전부 다시 밀어 넣게 되고,
            // 파일마다 API 를 왕복하므로 20초 넘게 걸린다.
            var stored = force ? new Dictionary<string, string>() : ReadStoredHashes(hashFile);
            var changed = sqlFiles
                .Where(f => !stored.TryGetValue(Path.GetFileName(f.Path), out var h) || h != HashOf(f.Content))
                .ToList();

            if (changed.Count == 0)
            {
                if (!silent) Debug.Log($"[SupaRun:Schema] '{env.name}' 변경 없음 — 스킵");
                return;
            }

            var names = changed.Select(f => Path.GetFileName(f.Path)).ToList();
            Debug.Log($"[SupaRun:Schema] '{env.name}' 에 변경 {changed.Count}개 적용 중 — {string.Join(", ", names)}");

            // 변경분을 하나로 묶어 1회만 왕복한다. 이름순이라 의존 순서가 지켜진다
            // (`_suparun_core.sql` 의 is_admin() 이 다른 파일의 정책보다 먼저).
            var batch = new StringBuilder();
            foreach (var f in changed)
            {
                batch.AppendLine($"-- ══ {Path.GetFileName(f.Path)} ══");
                batch.AppendLine(f.Content);
                batch.AppendLine();
            }

            var batchResult = await SupabaseManagementApi.RunQuery(projectId, token, batch.ToString());

            if (!batchResult.Ok)
            {
                // 묶어 보내면 어느 파일이 문제인지 알 수 없다 — 그때만 개별로 다시 돌려 범인을 찾는다.
                // 컴파일 훅으로도 들어오는 경로라 모달은 띄우지 않는다(에디터를 가로막는다).
                Debug.LogWarning(
                    $"[SupaRun:Schema] 묶음 실행 실패 — {batchResult.ToShortString()}\n개별 실행으로 원인을 찾습니다…");
                var succeeded = new Dictionary<string, string>(stored);
                var failed = new List<string>();
                foreach (var f in changed)
                {
                    var name = Path.GetFileName(f.Path);
                    var fileResult = await SupabaseManagementApi.RunQuery(projectId, token, f.Content);
                    if (fileResult.Ok) succeeded[name] = HashOf(f.Content);
                    else
                    {
                        failed.Add(name);
                        Debug.LogWarning($"[SupaRun:Schema] {name} 실패 — {fileResult.ToShortString()}");
                    }
                }
                // 성공한 것까지 다시 밀 이유는 없으므로 거기까지는 해시를 남긴다.
                WriteStoredHashes(hashFile, succeeded);
                Debug.LogWarning(
                    $"[SupaRun:Schema] {failed.Count}/{changed.Count}개 실패: {string.Join(", ", failed)}\n" +
                    "다음 컴파일에 재시도합니다. 반복되면 SQL 을 확인하세요.");
                return;
            }

            foreach (var f in changed) stored[Path.GetFileName(f.Path)] = HashOf(f.Content);
            WriteStoredHashes(hashFile, Prune(stored, sqlFiles));
            Debug.Log($"[SupaRun:Schema] '{env.name}' 반영 완료 — {changed.Count}개. 어드민을 새로고침하면 보입니다.");
        }

        /// <summary>
        /// 이번에 생성된 파일에 없는 항목을 버린다.
        /// 파일이 이름을 바꾸거나(_suparun_meta → _config/_table) 타입이 사라지면
        /// 옛 해시가 계속 쌓여 목록이 실제와 어긋난다.
        /// </summary>
        static Dictionary<string, string> Prune(Dictionary<string, string> stored, List<GeneratedFile> current)
        {
            var live = new HashSet<string>(current.Select(f => Path.GetFileName(f.Path)));
            var next = new Dictionary<string, string>();
            foreach (var kv in stored)
                if (live.Contains(kv.Key)) next[kv.Key] = kv.Value;
            return next;
        }

        static string HashOf(string content)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>`파일명=해시` 줄 단위. 형식이 깨지면 빈 맵 — 전부 다시 밀어 넣을 뿐이라 멱등하게 안전하다.</summary>
        static Dictionary<string, string> ReadStoredHashes(string hashFile)
        {
            var map = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(hashFile)) return map;
                foreach (var line in File.ReadAllLines(hashFile))
                {
                    var i = line.IndexOf('=');
                    if (i > 0) map[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch { /* 못 읽으면 전부 재적용 */ }
            return map;
        }

        /// <summary>값 하나짜리 기록. 아이콘 SQL 은 파일로 쪼개지지 않아 맵이 필요 없다.</summary>
        static string ReadStoredHash(string hashFile)
        {
            try { return File.Exists(hashFile) ? File.ReadAllText(hashFile).Trim() : null; }
            catch { return null; }   // 못 읽으면 다시 올린다 (UPSERT 라 무해)
        }

        static void WriteStoredHash(string hashFile, string hash)
        {
            try { File.WriteAllText(hashFile, hash); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SupaRun:Schema] 아이콘 반영 기록 저장 실패 — {ex.Message}");
            }
        }

        static void WriteStoredHashes(string hashFile, Dictionary<string, string> map)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in map.OrderBy(k => k.Key, StringComparer.Ordinal))
                    sb.AppendLine($"{kv.Key}={kv.Value}");
                File.WriteAllText(hashFile, sb.ToString());
            }
            catch (Exception ex)
            {
                // 못 남기면 다음 컴파일에 한 번 더 밀어 넣을 뿐이다 (멱등이라 무해).
                Debug.LogWarning($"[SupaRun:Schema] 해시 저장 실패 — {ex.Message}");
            }
        }

    }
}
