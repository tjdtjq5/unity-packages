using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public class SupaRunSettings
    {
        public const string VERSION = "0.4.0";

        // 파일에 남는 것은 **부트스트랩뿐이다.**
        //
        // 어드민(`suparun_env`)을 읽으려면 먼저 어느 Supabase 에 붙을지 알아야 하고, 그건
        // 어드민이 알려줄 수 없다 — 닭과 달걀이다. 그래서 URL·anon key·이름만 파일에 남는다.
        // 셋 다 공개값이라 git 에 있어도 잃을 것이 없고, 있어야 팀원이 클론만으로 붙는다.
        //
        // 나머지는 전부 밖으로 나갔다:
        //   비밀 → EditorPrefs (git 밖) + `suparun_secret` (팀 공유)
        //   판단 → `suparun_env` (어드민이 씀)
        //   사실 → `suparun_env` (Unity 가 구움)
        //   편집/빌드 환경 선택 → EditorPrefs (**개인** — 내 선택이 남에게 번지지 않는다)
        //
        // `UserSettings/SupaRunUserSettings.json` 은 사라졌다. 마지막까지 남아 있던 두 값
        // (`serverLogToConsole`·`setupCompleted`)의 유일한 사용처가 대시보드였고, 그 화면이
        // 없어지면서 파일 하나가 통째로 비었다. 개인 값은 전부 EditorPrefs 로 간다.
        const string ProjectSettingsPath = "ProjectSettings/SupaRunProjectSettings.json";

        // ── 데이터 클래스 ──

        /// <summary>
        /// 환경 하나(dev / staging / prod …). **파일에 남는 것은 부트스트랩 셋뿐이다.**
        ///
        /// 이름은 개발자가 부르는 라벨이라 로컬에 둔다. 접속 정보(URL·anon key)는 공개값이고,
        /// 이것이 있어야 `suparun_env` 를 읽으러 갈 수 있다.
        ///
        /// 나머지(Cloud Run URL·서비스명·비밀)는 여기 없다 —
        /// 비밀은 EditorPrefs, 그 밖은 `suparun_env` 다.
        /// </summary>
        [Serializable]
        public class EnvironmentData
        {
            public string name = "";
            public string supabaseUrl = "";
            public string supabaseAnonKey = "";

            /// <summary>
            /// 컴파일 후 이 환경에 스키마를 자동 반영할 것인가. **팀 공유값**(git)이다 —
            /// dev 는 켜고 prod 는 꺼 두면, prod 를 편집 중일 때 컴파일해도 스키마가 밀리지 않는다.
            /// 꺼진 환경은 배포가 반영을 겸한다(배포 시 항상 선반영).
            /// 기본 꺼짐: 처음 반영하면 [UserData] 표에 RLS 정책이 새로 생기는데,
            /// 게임도 같은 anon key 를 쓰므로 그 문은 한 번은 사람이 열어야 한다.
            /// </summary>
            public bool autoSchemaSync;

            /// <summary>
            /// 이 환경의 어드민에서 행이 늘거나 줄면 Id 상수를 자동 재생성할 것인가.
            /// 마찬가지로 팀 공유값. dev 만 켜는 것이 의도다 — prod 데이터 핫픽스가
            /// Unity 코드 생성을 유발할 이유가 없다. 수동 버튼은 없다(이것이 유일한 경로).
            /// </summary>
            public bool autoIdConstants;
        }

        [Serializable]
        class ProjectData
        {
            /// <summary>환경 목록. 팀이 공유해야 붙을 수 있으므로 이것만 git 에 남는다.</summary>
            public System.Collections.Generic.List<EnvironmentData> environments = new();
        }

        // ── 메모리 캐시 + 로드 ──

        static ProjectData _project;
        static SupaRunSettings _instance;

        static ProjectData P
        {
            get
            {
                if (_project == null)
                {
                    _project ??= LoadProject();
                }
                return _project;
            }
        }

        // ── 환경 ────────────────────────────────────────────────────

        /// <summary>환경 목록(읽기 전용 뷰). 편집은 AddEnvironment/RemoveEnvironment 로.</summary>
        public System.Collections.Generic.IReadOnlyList<EnvironmentData> Environments => P.environments;

        // 편집 환경 선택은 **개인 것**이라 EditorPrefs 에 둔다(파일 아님).
        //
        // 팀 공통으로 두면, A 가 prod 스키마를 반영하려고 편집 환경을 바꾼 순간
        // 아무것도 모르는 B 의 다음 컴파일이 prod 를 건드린다. 공통이 안전해 보이지만
        // 사고 범위는 오히려 넓다.
        const string EditorEnvKey = "EditorEnv";

        static string EnvPref(string key, string fallback)
        {
            var v = EditorPrefs.GetString(EditorPrefUtils.ProjectPrefix + key, "");
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        /// <summary>에디터가 보는 환경 이름. 컴파일 시 스키마 반영·어드민·대시보드가 전부 이걸 따른다.</summary>
        public string EditorEnvironment
        {
            get => EnvPref(EditorEnvKey, P.environments.Count > 0 ? P.environments[0].name : "");
            set => EditorPrefs.SetString(EditorPrefUtils.ProjectPrefix + EditorEnvKey, value ?? "");
        }

        // 빌드 환경 포인터는 없다 — **빌드 = 편집 환경**이다. 환경 전환이 드롭다운 한 번이
        // 된 뒤로, "dev 를 보며 prod 빌드" 를 위해 포인터를 따로 둘 이유가 사라졌다.
        // 출시 빌드는 prod 로 전환하고 뽑는다. 어느 환경이 구워졌는지는 빌드 로그가 말한다.

        /// <summary>이름으로 환경을 찾는다. 없으면 null — 승격 도구가 양쪽을 집을 때 쓴다.</summary>
        public EnvironmentData GetEnvironment(string name) =>
            P.environments.Find(e => e.name == name);

        /// <summary>
        /// 현재 편집 환경. 하나도 없으면 빈 환경을 만들어 돌려준다 —
        /// 설정 화면이 null 검사 없이 그릴 수 있어야 하고, 첫 실행이 정확히 그 상태다.
        /// </summary>
        public EnvironmentData Current
        {
            get
            {
                var env = GetEnvironment(EditorEnvironment);
                if (env != null) return env;
                if (P.environments.Count == 0)
                {
                    P.environments.Add(new EnvironmentData { name = "dev" });
                    SaveProject();
                }
                return P.environments[0];
            }
        }

        /// <summary>환경 추가. 이름이 겹치면 기존 것을 그대로 돌려준다.</summary>
        public EnvironmentData AddEnvironment(string name)
        {
            var existing = GetEnvironment(name);
            if (existing != null) return existing;
            var env = new EnvironmentData { name = name };
            P.environments.Add(env);
            SaveProject();
            return env;
        }

        /// <summary>
        /// 환경 이름 변경. 편집·빌드 선택이 이 이름을 가리키면 같이 따라간다 —
        /// 선택은 이름 문자열로 저장되므로, 슬롯만 바꾸면 선택이 유령 이름을 가리킨다.
        /// </summary>
        public bool RenameEnvironment(string from, string to)
        {
            var env = GetEnvironment(from);
            if (env == null || GetEnvironment(to) != null) return false;
            env.name = to;
            if (EditorEnvironment == from) EditorEnvironment = to;
            SaveProject();
            return true;
        }

        /// <summary>환경 삭제. 마지막 하나는 지우지 않는다(설정이 통째로 사라지는 것을 막는다).</summary>
        public bool RemoveEnvironment(string name)
        {
            if (P.environments.Count <= 1) return false;
            var env = GetEnvironment(name);
            if (env == null) return false;
            P.environments.Remove(env);
            // 선택이 사라진 환경을 가리키면 첫 환경으로 되돌린다.
            if (EditorEnvironment == name) EditorEnvironment = P.environments[0].name;
            SaveProject();
            return true;
        }

        static ProjectData LoadProject()
        {
            if (File.Exists(ProjectSettingsPath))
            {
                try { return JsonUtility.FromJson<ProjectData>(File.ReadAllText(ProjectSettingsPath)); }
                catch (Exception ex) { Debug.LogWarning($"[SupaRun] {ProjectSettingsPath} 파싱 실패 — 초기화합니다: {ex.Message}"); }
            }
            return new ProjectData();
        }

        static void SaveProject()
        {
            EnsureDirFor(ProjectSettingsPath);
            File.WriteAllText(ProjectSettingsPath, JsonUtility.ToJson(P, true));
        }

        static void EnsureDirFor(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        // ── 싱글톤 ──

        public static SupaRunSettings Instance
        {
            get
            {
                if (_instance == null)
                    _instance = new SupaRunSettings();
                return _instance;
            }
        }

        public void Save()
        {
            SaveProject();
            // 환경 설정은 파일이 아니라 DB 다. 바뀐 키가 없으면 아무것도 하지 않는다.
            FlushEnvAsync().Forget();
        }

        // ── Supabase (현재 편집 환경) ──
        // 이 프로퍼티들은 **EditorEnvironment 가 가리키는 환경**의 값을 읽고 쓴다.
        // 시그니처를 그대로 둔 덕분에 이걸 참조하는 20여 개 파일이 수정 없이 환경을 따라간다.

        public string supabaseUrl
        {
            get => Current.supabaseUrl;
            set { Current.supabaseUrl = value; }
        }

        public string SupabaseAnonKey
        {
            get => Current.supabaseAnonKey;
            set { Current.supabaseAnonKey = value; SaveProject(); }
        }

        // 비밀은 **git 에 올라가면 안 되므로** EditorPrefs 에 있고, 팀 공유는 `suparun_secret` 이 맡는다.
        // 폴백 인자가 빈 문자열인 이유: 옛 파일에서 값을 건져 올리던 경로가 사라졌다.
        //
        // 환경을 명시해야 하는 곳(승격·스냅샷처럼 두 환경을 동시에 다루는 코드)은
        // 아래 *Of(env) 를 쓴다.

        public static string AccessTokenOf(EnvironmentData env) =>
            env == null ? "" : SupaRunSecretPrefs.Get("access_token", env.name, "");

        public static void SetAccessTokenOf(EnvironmentData env, string value)
        {
            if (env != null) SupaRunSecretPrefs.Set("access_token", env.name, value);
        }

        public static string DbPasswordOf(EnvironmentData env) =>
            env == null ? "" : SupaRunSecretPrefs.Get("db_password", env.name, "");

        public static void SetDbPasswordOf(EnvironmentData env, string value)
        {
            if (env != null) SupaRunSecretPrefs.Set("db_password", env.name, value);
        }

        public static string CronSecretOf(EnvironmentData env) =>
            env == null ? "" : SupaRunSecretPrefs.Get("cron_secret", env.name, "");

        /// <summary>
        /// **다른 환경**의 `suparun_env` 값 하나를 읽는다. 빌드·스냅샷처럼 편집 환경이 아닌
        /// 곳을 들여다봐야 하는 자리에서 쓴다 — 메모리 캐시는 편집 환경 것뿐이다.
        ///
        /// 편집 환경이면 캐시로 답한다(왕복 없음).
        /// </summary>
        public static async UniTask<string> EnvValueOf(EnvironmentData env, string key)
        {
            if (env == null) return "";
            if (env.name == Instance.EditorEnvironment) return EnvGet(key);

            var id = ProjectIdOf(env.supabaseUrl);
            var token = AccessTokenOf(env);
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(token)) return "";

            var r = await SupabaseManagementApi.RunQuery(id, token,
                $"SELECT value FROM suparun_env WHERE key = '{key}';");
            if (!r.Ok) return "";

            try
            {
                var rows = Newtonsoft.Json.Linq.JArray.Parse(r.Value ?? "[]");
                return rows.Count > 0 ? (string)rows[0]["value"] ?? "" : "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 빌드 시점에 쓰는 동기 판. 빌드 파이프라인(`IPreprocessBuildWithReport`)은 await 를 못 한다.
        /// 편집 환경이면 캐시에서 즉시 답하고, 다른 환경이면 갱신해 두고 쓰라는 뜻으로 빈 값을 준다.
        /// </summary>
        public static string CloudRunUrlOf(EnvironmentData env)
        {
            if (env == null) return "";
            if (env.name == Instance.EditorEnvironment) return EnvGet(K_CLOUD_RUN_URL);

            // 빌드 환경이 편집 환경과 다르면 그 값을 여기서 동기로 가져올 방법이 없다.
            // 빈 값이면 아래 경고가 뜨고, 사람이 편집 환경을 그쪽으로 바꿔 한 번 열면 채워진다.
            return "";
        }

        public string SupabaseDbPassword
        {
            get => DbPasswordOf(Current);
            set => SetDbPasswordOf(Current, value);
        }

        public string SupabaseAccessToken
        {
            get => AccessTokenOf(Current);
            set => SetAccessTokenOf(Current, value);
        }

        // ── 환경 설정 (`suparun_env`) ──────────────────────────────
        // **어드민이 진실이고 여기는 읽는 쪽이다.** 값은 이 환경의 DB 에 있고,
        // 대시보드를 열 때와 배포 직전에 RefreshEnvAsync() 로 당겨 온다.
        //
        // 파일로 캐시하지 않는다 — 설정을 git 에서 걷어내는 것이 목적이고, 컴파일 경로는
        // 이 값을 쓰지 않으므로(스키마 반영은 URL·PAT 만 쓴다) 메모리로 충분하다.
        // 도메인 리로드로 비워지면 다음 갱신 때 다시 채워진다.

        static Dictionary<string, string> _envCache;
        static readonly HashSet<string> _envDirty = new();

        /// <summary>DB 키 상수. **어드민(shared/envSettings.ts)이 같은 키로 쓴다 — 함부로 바꾸지 말 것.**</summary>
        const string K_NAME = "name";
        const string K_GCP_PROJECT = "gcp_project_id";
        const string K_GCP_REGION = "gcp_region";
        const string K_GCP_SERVICE = "gcp_service_name";
        const string K_GCP_MIN = "gcp_min_instances";
        const string K_GITHUB_REPO = "github_repo_name";
        const string K_CACHES = "server_caches";
        // 아래는 사람이 정하지 않는 **사실** — 자동 설정·배포·상태 조회가 알아내 넣는다.
        const string K_GCP_API_ENABLED = "gcp_api_enabled";
        const string K_GCP_SERVICE_ACCOUNT = "gcp_service_account";
        const string K_CLOUD_RUN_URL = "cloud_run_url";
        const string K_MAX_CONNECTIONS = "max_connections";
        const string K_MAX_INSTANCES = "max_instances";
        const string K_DB_POOL_SIZE = "db_pool_size";
        /// <summary>게임 빌드용 로그인(Guest·GPGS·GameCenter). 웹 OAuth 는 여기 없다.</summary>
        const string K_PLATFORM_AUTH = "platform_auth";

        static string EnvGet(string key, string fallback = "") =>
            _envCache != null && _envCache.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)
                ? v : fallback;

        static void EnvSet(string key, string value)
        {
            _envCache ??= new Dictionary<string, string>();
            if (_envCache.TryGetValue(key, out var cur) && cur == (value ?? "")) return;
            _envCache[key] = value ?? "";
            _envDirty.Add(key);
        }

        /// <summary>
        /// 어드민이 정한 설정을 당겨 온다. 대시보드를 열 때와 배포 직전에 부른다.
        /// 실패하면 **캐시를 건드리지 않는다** — 빈 값으로 덮으면 배포가 엉뚱한 곳으로 간다.
        /// </summary>
        public static async UniTask<SupabaseResult<int>> RefreshEnvAsync()
        {
            var env = Instance.Current;
            var id = ProjectIdOf(env.supabaseUrl);
            var token = AccessTokenOf(env);
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(token))
                return SupabaseResult<int>.Local(
                    "편집 환경에 Supabase URL 또는 Access Token 이 없습니다.");

            var r = await SupabaseManagementApi.RunQuery(id, token, "SELECT key, value FROM suparun_env;");
            if (!r.Ok) return r.CarryFailure<int>();

            var map = new Dictionary<string, string>();
            try
            {
                foreach (var row in Newtonsoft.Json.Linq.JArray.Parse(r.Value ?? "[]"))
                    map[(string)row["key"]] = (string)row["value"] ?? "";
            }
            catch (Exception ex) { return SupabaseResult<int>.Failure(ex); }

            _envCache = map;
            _envDirty.Clear();
            return SupabaseResult<int>.Success(map.Count);
        }

        /// <summary>Save() 가 부른다. 바뀐 키만 올린다.</summary>
        static async UniTaskVoid FlushEnvAsync()
        {
            if (_envDirty.Count == 0 || _envCache == null) return;

            var env = Instance.Current;
            var id = ProjectIdOf(env.supabaseUrl);
            var token = AccessTokenOf(env);
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(token)) return;

            var keys = new List<string>(_envDirty);
            _envDirty.Clear();

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var values = new List<string>();
            foreach (var k in keys)
            {
                var v = _envCache.TryGetValue(k, out var s) ? s : "";
                // 달러 인용 — 값에 따옴표가 있어도 SQL 이 깨지지 않는다.
                values.Add($"('{k}', $ev${v}$ev$, {now}, 'editor')");
            }

            var r = await SupabaseManagementApi.RunQuery(id, token,
                "INSERT INTO suparun_env(key, value, updated_at, updated_by) VALUES " +
                string.Join(",", values) +
                " ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, " +
                "updated_at = EXCLUDED.updated_at, updated_by = EXCLUDED.updated_by;");

            r.LogIfFailed("환경 설정 저장");
        }

        /// <summary>환경 이름. 어드민에서 정하고 여기서는 읽는다.</summary>
        public string EnvName
        {
            get => EnvGet(K_NAME, Current.name);
            set => EnvSet(K_NAME, value);
        }

        // ── Google Cloud ──

        public string gcpProjectId
        {
            get => EnvGet(K_GCP_PROJECT);
            set => EnvSet(K_GCP_PROJECT, value);
        }

        public string gcpRegion
        {
            get => EnvGet(K_GCP_REGION, "asia-northeast3");
            set => EnvSet(K_GCP_REGION, value);
        }

        /// <summary>Cloud Run 서비스명 — 환경마다 별도 서비스를 띄우므로 환경별이다.</summary>
        public string gcpServiceName
        {
            get => EnvGet(K_GCP_SERVICE);
            set => EnvSet(K_GCP_SERVICE, value);
        }

        public int gcpMinInstances
        {
            get => int.TryParse(EnvGet(K_GCP_MIN), out var n) ? n : 0;
            set => EnvSet(K_GCP_MIN, value.ToString());
        }

        // 아래 둘은 **사실**이다 — 사람이 정하는 값이 아니라 자동 설정이 알아낸 결과다.
        // Unity 가 굽고 어드민은 상태로만 본다.

        public bool gcpCloudRunApiEnabled
        {
            get => EnvGet(K_GCP_API_ENABLED) == "1";
            set => EnvSet(K_GCP_API_ENABLED, value ? "1" : "0");
        }

        public string gcpServiceAccountEmail
        {
            get => EnvGet(K_GCP_SERVICE_ACCOUNT);
            set => EnvSet(K_GCP_SERVICE_ACCOUNT, value);
        }

        // ── GitHub ──

        public string githubRepoName
        {
            get => EnvGet(K_GITHUB_REPO);
            set => EnvSet(K_GITHUB_REPO, value);
        }

        /// <summary>환경과 무관하다 — 레포 하나를 모든 환경이 공유한다.</summary>
        public string GithubToken
        {
            get => SupaRunSecretPrefs.Get("github_token", null, "");
            set => SupaRunSecretPrefs.Set("github_token", null, value);
        }

        // ── 게임 로그인 ──

        static List<string> _platformAuthCache;
        static string _platformAuthCacheKey;

        /// <summary>
        /// 게임 빌드에 들어가는 로그인. **Supabase auth config 에 없는 것들만** 여기 있다.
        ///
        /// 웹 OAuth(google·kakao …)는 여기 **없다** — 진실이 Supabase 에 있고 어드민이 직접 읽고 쓴다.
        /// 예전에는 한 문자열에 둘이 섞여 있었고, 그래서 웹에서 켠 프로바이더가 Unity 가 한 번 돌 때마다
        /// 지워졌다. 같은 값을 두 곳이 다른 근거로 쓰면 반드시 어긋난다.
        /// </summary>
        public static readonly string[] PlatformAuthKinds = { "Guest", "GPGS", "GameCenter" };

        public List<string> platformAuth
        {
            get
            {
                var raw = EnvGet(K_PLATFORM_AUTH, "Guest");
                if (_platformAuthCacheKey != raw)
                {
                    _platformAuthCacheKey = raw;
                    _platformAuthCache = string.IsNullOrEmpty(raw)
                        ? new List<string>()
                        : new List<string>(raw.Split(','));
                }
                return _platformAuthCache;
            }
            set
            {
                EnvSet(K_PLATFORM_AUTH, string.Join(",", value));
                _platformAuthCacheKey = null;
            }
        }

        // ── 스케일링 ──
        // 사람이 정하지 않는다 — DB 의 max_connections 에서 StatusTab 이 계산해 넣는다. 즉 **사실**이다.

        public int supabaseMaxConnections
        {
            get => int.TryParse(EnvGet(K_MAX_CONNECTIONS), out var n) && n > 0 ? n : 60;
            set => EnvSet(K_MAX_CONNECTIONS, value.ToString());
        }

        public int gcpMaxInstances
        {
            get => int.TryParse(EnvGet(K_MAX_INSTANCES), out var n) && n > 0 ? n : 3;
            set => EnvSet(K_MAX_INSTANCES, value.ToString());
        }

        public int dbPoolSize
        {
            get => int.TryParse(EnvGet(K_DB_POOL_SIZE), out var n) && n > 0 ? n : 20;
            set => EnvSet(K_DB_POOL_SIZE, value.ToString());
        }

        // ── 배포 캐시 ──

        static System.Collections.Generic.List<string> _serverCachesCache;
        static string _serverCachesCacheKey;

        /// <summary>
        /// ⚠ 돌려주는 리스트를 직접 고쳐도 저장되지 않는다. 값은 `suparun_env` 에 있고
        /// 세터를 거쳐야 dirty 로 잡힌다 — `caches.Add(x)` 가 아니라 `caches = 새 리스트` 로 쓸 것.
        /// </summary>
        public System.Collections.Generic.List<string> enabledServerCaches
        {
            get
            {
                var raw = EnvGet(K_CACHES, "nuget,docker");
                if (_serverCachesCacheKey != raw)
                {
                    _serverCachesCacheKey = raw;
                    _serverCachesCache = string.IsNullOrEmpty(raw)
                        ? new System.Collections.Generic.List<string>()
                        : new System.Collections.Generic.List<string>(raw.Split(','));
                }
                return _serverCachesCache;
            }
            set
            {
                EnvSet(K_CACHES, string.Join(",", value));
                _serverCachesCacheKey = null;
            }
        }

        // ── 기타 ──

        /// <summary>배포 결과. 사람이 정하는 값이 아니라 Cloud Run 이 알려준 주소다.</summary>
        public string cloudRunUrl
        {
            get => EnvGet(K_CLOUD_RUN_URL);
            set => EnvSet(K_CLOUD_RUN_URL, value);
        }

        public string CronSecret
        {
            get => CronSecretOf(Current);
            set => SupaRunSecretPrefs.Set("cron_secret", Current.name, value);
        }

        // ── 설정 완료 판단 ──

        public bool IsSupabaseConfigured =>
            !string.IsNullOrEmpty(supabaseUrl) &&
            !string.IsNullOrEmpty(SupabaseAnonKey) &&
            !string.IsNullOrEmpty(SupabaseDbPassword);

        public bool IsGcpConfigured =>
            !string.IsNullOrEmpty(gcpProjectId);

        public bool IsGitHubConfigured =>
            !string.IsNullOrEmpty(GithubToken) &&
            !string.IsNullOrEmpty(githubRepoName);

        public bool IsDeployConfigured =>
            IsGcpConfigured && IsGitHubConfigured;

        public bool HasCache(string cacheId) => enabledServerCaches.Contains(cacheId);

        // ── Supabase 프로젝트 ID 추출 ──

        public string SupabaseProjectId => ProjectIdOf(supabaseUrl);

        /// <summary>`https://xxx.supabase.co` → `xxx`. 환경을 지정해 다루는 쪽(승격·스키마 반영)이 쓴다.</summary>
        public static string ProjectIdOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return "";
            try
            {
                var uri = new Uri(url);
                return uri.Host.Split('.')[0];
            }
            catch { return ""; }
        }

        public string SupabaseApiSettingsUrl =>
            $"https://supabase.com/dashboard/project/{SupabaseProjectId}/settings/api-keys";
        public string SupabaseDatabaseSettingsUrl =>
            $"https://supabase.com/dashboard/project/{SupabaseProjectId}/settings/database";
        public string SupabaseDashboardUrl =>
            $"https://supabase.com/dashboard/project/{SupabaseProjectId}";

    }
}
