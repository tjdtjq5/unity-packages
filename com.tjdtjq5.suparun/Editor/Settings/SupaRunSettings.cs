using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    public class SupaRunSettings
    {
        public const string VERSION = "0.4.0";

        // 신규 분리 경로 — 공유 데이터는 ProjectSettings/, 개인 환경은 UserSettings/
        const string ProjectSettingsPath = "ProjectSettings/SupaRunProjectSettings.json";
        const string UserSettingsPath = "UserSettings/SupaRunUserSettings.json";

        // 레거시 경로 — 마이그레이션 입력 (단일 JSON / .asset / 시크릿 JSON)
        const string LegacyUnifiedJsonPath = "UserSettings/SupaRunSettings.json";
        const string LegacyAssetPath = "Assets/Editor/SupaRunSettings.asset";
        const string LegacySecretsJsonPath = "UserSettings/SupaRunSecrets.json";

        // ── 데이터 클래스 (분리) ──

        /// <summary>
        /// 환경 하나(dev / staging / prod …). **환경마다 달라야 하는 값만** 담는다.
        ///
        /// 나누는 기준은 "환경을 갈아탈 때 같이 갈아타야 하는가"다.
        /// Supabase 는 프로젝트 자체가 갈리므로 URL·키·PAT 가 전부 환경별이고,
        /// Cloud Run 은 서비스를 따로 띄우므로 서비스명·URL 이 환경별이다.
        /// 반대로 GCP 프로젝트·GitHub 레포는 하나를 공유하므로 여기 없다.
        /// </summary>
        [Serializable]
        public class EnvironmentData
        {
            public string name = "";

            // Supabase — 환경마다 별도 프로젝트
            public string supabaseUrl = "";
            public string supabaseAnonKey = "";

            // ⚠ 아래 비밀 필드들을 **직접 읽지 말 것.** 값은 이 파일이 아니라 EditorPrefs 에 있다
            //   (git 에 올라가면 안 되므로). 필드는 옛 파일에서 값을 건져 올리는 폴백으로만 남아 있고,
            //   마이그레이션이 끝나면 빈 문자열이다.
            //   반드시 AccessTokenOf / DbPasswordOf / CronSecretOf 를 쓴다.
            public string supabaseDbPassword = "";
            public string supabaseAccessToken = "";

            // Cloud Run — 환경마다 별도 서비스 (어드민 URL 도 따라서 갈린다)
            public string gcpServiceName = "";
            public string cloudRunUrl = "";
            public string cronSecret = "";
        }

        [Serializable]
        class ProjectData
        {
            // ── 환경 (ADR-0004 후속) ──
            public System.Collections.Generic.List<EnvironmentData> environments = new();
            /// <summary>에디터가 보는 환경. 컴파일 시 스키마 자동 반영이 여기로 간다.</summary>
            public string editorEnvironment = "";
            /// <summary>빌드에 구워지는 환경. 에디터와 **별개여야 한다** —
            /// dev 를 보면서 prod 빌드를 뽑는 것이 정상 상태다.</summary>
            public string buildEnvironment = "";

            // ── 레거시 평면 필드 ──
            // 환경 도입 전 형식. MigrateEnvironments() 가 environments[0] 으로 옮긴 뒤 비운다.
            // 필드를 지우면 옛 설정 파일에서 값을 못 읽어 마이그레이션 자체가 불가능해지므로 남긴다.
            public string supabaseUrl = "";
            public string supabaseAnonKey = "";
            public string supabaseDbPassword = "";
            public string supabaseAccessToken = "";

            // Google Cloud — 프로젝트/리전/서비스계정은 환경 공통
            public string gcpProjectId = "";
            public string gcpRegion = "asia-northeast3";
            public string gcpServiceName = "";
            public int gcpMinInstances;
            public bool gcpCloudRunApiEnabled;
            public string gcpServiceAccountEmail = "";

            // GitHub
            public string githubRepoName = "";
            public string githubToken = "";

            // Auth
            public string enabledAuthProviders = "Guest";

            // 스케일링
            public int supabaseMaxConnections = 60;
            public int gcpMaxInstances = 3;
            public int dbPoolSize = 20;

            // 배포 캐시
            public string enabledServerCaches = "nuget,docker";

            // 상태 (배포 결과)
            public string cloudRunUrl = "";
            public string cronSecret = "";
        }

        [Serializable]
        class UserData
        {
            public bool serverLogToConsole = true;
            public bool setupCompleted;
        }

        // ── 레거시 통합 데이터 (마이그레이션 입력) ──

        [Serializable]
        class LegacyData
        {
            public string supabaseUrl = "";
            public string supabaseAnonKey = "";
            public string supabaseDbPassword = "";
            public string supabaseAccessToken = "";
            public string gcpProjectId = "";
            public string gcpRegion = "asia-northeast3";
            public string gcpServiceName = "";
            public int gcpMinInstances;
            public bool gcpCloudRunApiEnabled;
            public string gcpServiceAccountEmail = "";
            public string githubRepoName = "";
            public string githubToken = "";
            public string enabledAuthProviders = "Guest";
            public int supabaseMaxConnections = 60;
            public int gcpMaxInstances = 3;
            public int dbPoolSize = 20;
            public string enabledServerCaches = "nuget,docker";
            public bool serverLogToConsole = true;
            public bool setupCompleted;
            public string cloudRunUrl = "";
            public string cronSecret = "";
        }

        // ── 메모리 캐시 + 로드 ──

        static ProjectData _project;
        static UserData _user;
        static SupaRunSettings _instance;

        static ProjectData P
        {
            get
            {
                if (_project == null)
                {
                    MigrateIfNeeded();
                    _project ??= LoadProject();
                    MigrateEnvironments(_project);
                    MigrateSecretsOutOfFile(_project);
                }
                return _project;
            }
        }

        // ── 환경 ────────────────────────────────────────────────────

        /// <summary>
        /// 평면 필드 → environments[0] 이관. 멱등 — 환경이 하나라도 있으면 아무것도 하지 않는다.
        ///
        /// 이름을 dev 로 짓는 이유: 기존 프로젝트는 그동안 개발용으로 쓰던 것이고,
        /// prod 는 깨끗한 새 프로젝트로 따로 만든다는 결정을 따른다.
        /// </summary>
        static void MigrateEnvironments(ProjectData p)
        {
            p.environments ??= new System.Collections.Generic.List<EnvironmentData>();
            if (p.environments.Count > 0)
            {
                // 선택이 사라진 환경을 가리키면(이름 변경·삭제) 첫 환경으로 되돌린다.
                if (!p.environments.Exists(e => e.name == p.editorEnvironment))
                    p.editorEnvironment = p.environments[0].name;
                if (!p.environments.Exists(e => e.name == p.buildEnvironment))
                    p.buildEnvironment = p.editorEnvironment;
                return;
            }

            // 평면 필드가 통째로 비어 있으면 이관할 것이 없다(신규 프로젝트).
            if (string.IsNullOrEmpty(p.supabaseUrl) && string.IsNullOrEmpty(p.cloudRunUrl)) return;

            p.environments.Add(new EnvironmentData
            {
                name = "dev",
                supabaseUrl = p.supabaseUrl,
                supabaseAnonKey = p.supabaseAnonKey,
                supabaseDbPassword = p.supabaseDbPassword,
                supabaseAccessToken = p.supabaseAccessToken,
                gcpServiceName = p.gcpServiceName,
                cloudRunUrl = p.cloudRunUrl,
                cronSecret = p.cronSecret,
            });
            p.editorEnvironment = "dev";
            p.buildEnvironment = "dev";

            // 평면 필드를 비운다 — 두 곳에 값이 남으면 어느 쪽이 진실인지 알 수 없다.
            p.supabaseUrl = p.supabaseAnonKey = p.supabaseDbPassword = p.supabaseAccessToken = "";
            p.gcpServiceName = p.cloudRunUrl = p.cronSecret = "";

            SaveProject();
            Debug.Log("[SupaRun] 기존 설정을 환경 'dev' 로 옮겼습니다. 대시보드 > Settings 에서 환경을 추가할 수 있습니다.");
        }

        /// <summary>
        /// 비밀을 프로젝트 파일에서 빼내 EditorPrefs 로 옮긴다. 멱등 — 옮길 게 없으면 아무것도 안 한다.
        ///
        /// 이 파일은 git 에 커밋된다. PAT 는 Supabase **계정 전체**의 마스터키라 저장소에 남으면
        /// 클론한 누구나 계정의 모든 프로젝트를 지울 수 있다. 그렇다고 gitignore 로 빼면 팀원이
        /// 설정을 못 받았는데, 이제 <see cref="SupaRunSecretStore"/> 가 공유를 맡으므로 뺄 수 있다.
        ///
        /// 옮기고 나면 이 함수는 두 번 다시 할 일이 없다. 남겨 두는 이유는 git 에서 옛 파일을
        /// 되돌리거나 다른 브랜치를 체크아웃하면 값이 다시 나타나기 때문이다.
        /// </summary>
        static void MigrateSecretsOutOfFile(ProjectData p)
        {
            var moved = 0;

            foreach (var env in p.environments)
            {
                moved += Move("access_token", env.name, env.supabaseAccessToken, v => env.supabaseAccessToken = v);
                moved += Move("db_password", env.name, env.supabaseDbPassword, v => env.supabaseDbPassword = v);
                moved += Move("cron_secret", env.name, env.cronSecret, v => env.cronSecret = v);
            }

            moved += Move("github_token", null, p.githubToken, v => p.githubToken = v);

            // 환경 이관 전 형식의 평면 필드에도 남아 있을 수 있다.
            moved += Move("access_token", p.editorEnvironment, p.supabaseAccessToken, v => p.supabaseAccessToken = v);
            moved += Move("db_password", p.editorEnvironment, p.supabaseDbPassword, v => p.supabaseDbPassword = v);
            moved += Move("cron_secret", p.editorEnvironment, p.cronSecret, v => p.cronSecret = v);

            if (moved == 0) return;

            SaveProject();
            Debug.Log($"[SupaRun] 비밀 {moved}개를 프로젝트 파일에서 이 컴퓨터(EditorPrefs)로 옮겼습니다 — " +
                      "git 에 더 이상 올라가지 않습니다. 팀 공유는 Settings > 에디터 로그인 > 공유 비밀에서 합니다.");
            return;

            static int Move(string name, string env, string value, Action<string> clearInFile)
            {
                if (string.IsNullOrEmpty(value)) return 0;
                // 로컬에 이미 값이 있으면 덮지 않는다 — 파일 쪽이 옛 브랜치의 값일 수 있다.
                if (!SupaRunSecretPrefs.Has(name, env)) SupaRunSecretPrefs.Set(name, env, value);
                clearInFile("");
                return 1;
            }
        }

        /// <summary>환경 목록(읽기 전용 뷰). 편집은 AddEnvironment/RemoveEnvironment 로.</summary>
        public System.Collections.Generic.IReadOnlyList<EnvironmentData> Environments => P.environments;

        /// <summary>에디터가 보는 환경 이름. 컴파일 시 스키마 반영·어드민·대시보드가 전부 이걸 따른다.</summary>
        public string EditorEnvironment
        {
            get => P.editorEnvironment;
            set { P.editorEnvironment = value; SaveProject(); }
        }

        /// <summary>빌드에 구워지는 환경 이름.</summary>
        public string BuildEnvironment
        {
            get => P.buildEnvironment;
            set { P.buildEnvironment = value; SaveProject(); }
        }

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
                var env = GetEnvironment(P.editorEnvironment);
                if (env != null) return env;
                if (P.environments.Count == 0)
                {
                    P.environments.Add(new EnvironmentData { name = "dev" });
                    P.editorEnvironment = P.buildEnvironment = "dev";
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

        /// <summary>환경 삭제. 마지막 하나는 지우지 않는다(설정이 통째로 사라지는 것을 막는다).</summary>
        public bool RemoveEnvironment(string name)
        {
            if (P.environments.Count <= 1) return false;
            var env = GetEnvironment(name);
            if (env == null) return false;
            P.environments.Remove(env);
            if (P.editorEnvironment == name) P.editorEnvironment = P.environments[0].name;
            if (P.buildEnvironment == name) P.buildEnvironment = P.environments[0].name;
            SaveProject();
            return true;
        }

        static UserData U
        {
            get
            {
                if (_user == null)
                {
                    MigrateIfNeeded();
                    _user ??= LoadUser();
                }
                return _user;
            }
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

        static UserData LoadUser()
        {
            if (File.Exists(UserSettingsPath))
            {
                try { return JsonUtility.FromJson<UserData>(File.ReadAllText(UserSettingsPath)); }
                catch (Exception ex) { Debug.LogWarning($"[SupaRun] {UserSettingsPath} 파싱 실패 — 초기화합니다: {ex.Message}"); }
            }
            return new UserData();
        }

        static void SaveProject()
        {
            EnsureDirFor(ProjectSettingsPath);
            File.WriteAllText(ProjectSettingsPath, JsonUtility.ToJson(P, true));
        }

        static void SaveUser()
        {
            EnsureDirFor(UserSettingsPath);
            File.WriteAllText(UserSettingsPath, JsonUtility.ToJson(U, true));
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
            SaveUser();
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

        // 아래 값들은 **git 에 올라가면 안 되므로** 저장 위치가 다르다 — 파일이 아니라
        // EditorPrefs 에 있고, 팀 공유는 suparun_secret 테이블이 맡는다(SupaRunSecretPrefs 참조).
        // 시그니처는 그대로라 호출부는 영향이 없다.
        //
        // 환경을 명시해야 하는 곳(승격·스냅샷처럼 두 환경을 동시에 다루는 코드)은
        // 아래 *Of(env) 정적 메서드를 쓴다. EnvironmentData 의 필드를 직접 읽으면 빈 값이 나온다.

        public static string AccessTokenOf(EnvironmentData env) =>
            env == null ? "" : SupaRunSecretPrefs.Get("access_token", env.name, env.supabaseAccessToken);

        public static void SetAccessTokenOf(EnvironmentData env, string value)
        {
            if (env != null) SupaRunSecretPrefs.Set("access_token", env.name, value);
        }

        public static string DbPasswordOf(EnvironmentData env) =>
            env == null ? "" : SupaRunSecretPrefs.Get("db_password", env.name, env.supabaseDbPassword);

        public static void SetDbPasswordOf(EnvironmentData env, string value)
        {
            if (env != null) SupaRunSecretPrefs.Set("db_password", env.name, value);
        }

        public static string CronSecretOf(EnvironmentData env) =>
            env == null ? "" : SupaRunSecretPrefs.Get("cron_secret", env.name, env.cronSecret);

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

        // ── Google Cloud ──

        public string gcpProjectId
        {
            get => P.gcpProjectId;
            set { P.gcpProjectId = value; }
        }

        public string gcpRegion
        {
            get => P.gcpRegion;
            set { P.gcpRegion = value; }
        }

        /// <summary>Cloud Run 서비스명 — 환경마다 별도 서비스를 띄우므로 환경별이다.</summary>
        public string gcpServiceName
        {
            get => Current.gcpServiceName;
            set { Current.gcpServiceName = value; }
        }

        public int gcpMinInstances
        {
            get => P.gcpMinInstances;
            set { P.gcpMinInstances = value; }
        }

        public bool gcpCloudRunApiEnabled
        {
            get => P.gcpCloudRunApiEnabled;
            set { P.gcpCloudRunApiEnabled = value; }
        }

        public string gcpServiceAccountEmail
        {
            get => P.gcpServiceAccountEmail;
            set { P.gcpServiceAccountEmail = value; }
        }

        // ── GitHub ──

        public string githubRepoName
        {
            get => P.githubRepoName;
            set { P.githubRepoName = value; }
        }

        /// <summary>환경과 무관하다 — 레포 하나를 모든 환경이 공유한다.</summary>
        public string GithubToken
        {
            get => SupaRunSecretPrefs.Get("github_token", null, P.githubToken);
            set => SupaRunSecretPrefs.Set("github_token", null, value);
        }

        // ── Auth ──

        static System.Collections.Generic.List<string> _authProvidersCache;
        static string _authProvidersCacheKey;

        public System.Collections.Generic.List<string> enabledAuthProviders
        {
            get
            {
                var raw = P.enabledAuthProviders;
                if (_authProvidersCacheKey != raw)
                {
                    _authProvidersCacheKey = raw;
                    _authProvidersCache = string.IsNullOrEmpty(raw)
                        ? new System.Collections.Generic.List<string>()
                        : new System.Collections.Generic.List<string>(raw.Split(','));
                }
                return _authProvidersCache;
            }
            set
            {
                P.enabledAuthProviders = string.Join(",", value);
                _authProvidersCacheKey = null;
            }
        }

        // ── 스케일링 ──

        public int supabaseMaxConnections
        {
            get => P.supabaseMaxConnections;
            set { P.supabaseMaxConnections = value; }
        }

        public int gcpMaxInstances
        {
            get => P.gcpMaxInstances;
            set { P.gcpMaxInstances = value; }
        }

        public int dbPoolSize
        {
            get => P.dbPoolSize;
            set { P.dbPoolSize = value; }
        }

        // ── 배포 캐시 ──

        static System.Collections.Generic.List<string> _serverCachesCache;
        static string _serverCachesCacheKey;

        public System.Collections.Generic.List<string> enabledServerCaches
        {
            get
            {
                var raw = P.enabledServerCaches;
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
                P.enabledServerCaches = string.Join(",", value);
                _serverCachesCacheKey = null;
            }
        }

        // ── 기타 ──

        public bool serverLogToConsole
        {
            get => U.serverLogToConsole;
            set { U.serverLogToConsole = value; }
        }

        public bool setupCompleted
        {
            get => U.setupCompleted;
            set { U.setupCompleted = value; }
        }

        public string cloudRunUrl
        {
            get => Current.cloudRunUrl;
            set { Current.cloudRunUrl = value; }
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

        // ── 마이그레이션 ──

        /// <summary>
        /// 마이그레이션 진입점. 멱등 — ProjectSettings/SupaRunProjectSettings.json이 있으면 스킵.
        ///
        /// 흐름:
        /// 1. 새 분리 파일이 이미 있으면 종료 (마이그레이션 완료 상태)
        /// 2. 레거시 단일 JSON(UserSettings/SupaRunSettings.json) 발견 → 2개 파일로 분배 + .bak 백업
        /// 3. 단일 JSON도 없으면 → 더 오래된 .asset/시크릿/EditorPrefs 마이그레이션 시도
        /// </summary>
        static void MigrateIfNeeded()
        {
            if (File.Exists(ProjectSettingsPath)) return;

            var projectData = new ProjectData();
            var userData = new UserData();
            var migrated = false;

            // Step A: 레거시 단일 JSON (v0.3 형식) → 분리
            if (File.Exists(LegacyUnifiedJsonPath))
            {
                try
                {
                    var legacy = JsonUtility.FromJson<LegacyData>(File.ReadAllText(LegacyUnifiedJsonPath));
                    if (legacy != null)
                    {
                        ApplyLegacyToSplit(legacy, projectData, userData);
                        migrated = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SupaRun] {LegacyUnifiedJsonPath} 파싱 실패 — 빈 설정으로 시작: {ex.Message}");
                }

                // 레거시 파일 .bak 백업 (실패해도 무시)
                try
                {
                    var bakPath = LegacyUnifiedJsonPath + ".bak";
                    if (File.Exists(bakPath)) File.Delete(bakPath);
                    File.Move(LegacyUnifiedJsonPath, bakPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SupaRun] 레거시 파일 백업 실패: {ex.Message}");
                }
            }
            else
            {
                // Step B: v0.2 이전 마이그레이션 (.asset / EditorPrefs / SupaRunSecrets.json)
                migrated = MigrateFromV02(projectData, userData) || migrated;
            }

            if (migrated)
            {
                _project = projectData;
                _user = userData;
                SaveProject();
                SaveUser();
                Debug.Log($"[SupaRun] 설정 마이그레이션 완료 → {ProjectSettingsPath} + {UserSettingsPath}");
            }
        }

        /// <summary>레거시 통합 데이터 → 분리된 ProjectData/UserData로 복사.</summary>
        static void ApplyLegacyToSplit(LegacyData legacy, ProjectData project, UserData user)
        {
            // ProjectData
            project.supabaseUrl = legacy.supabaseUrl;
            project.supabaseAnonKey = legacy.supabaseAnonKey;
            project.supabaseDbPassword = legacy.supabaseDbPassword;
            project.supabaseAccessToken = legacy.supabaseAccessToken;
            project.gcpProjectId = legacy.gcpProjectId;
            project.gcpRegion = string.IsNullOrEmpty(legacy.gcpRegion) ? "asia-northeast3" : legacy.gcpRegion;
            project.gcpServiceName = legacy.gcpServiceName;
            project.gcpMinInstances = legacy.gcpMinInstances;
            project.gcpCloudRunApiEnabled = legacy.gcpCloudRunApiEnabled;
            project.gcpServiceAccountEmail = legacy.gcpServiceAccountEmail;
            project.githubRepoName = legacy.githubRepoName;
            project.githubToken = legacy.githubToken;
            project.enabledAuthProviders = string.IsNullOrEmpty(legacy.enabledAuthProviders) ? "Guest" : legacy.enabledAuthProviders;
            project.supabaseMaxConnections = legacy.supabaseMaxConnections > 0 ? legacy.supabaseMaxConnections : 60;
            project.gcpMaxInstances = legacy.gcpMaxInstances > 0 ? legacy.gcpMaxInstances : 3;
            project.dbPoolSize = legacy.dbPoolSize > 0 ? legacy.dbPoolSize : 20;
            project.enabledServerCaches = string.IsNullOrEmpty(legacy.enabledServerCaches) ? "nuget,docker" : legacy.enabledServerCaches;
            project.cloudRunUrl = legacy.cloudRunUrl;
            project.cronSecret = legacy.cronSecret;

            // UserData
            user.serverLogToConsole = legacy.serverLogToConsole;
            user.setupCompleted = legacy.setupCompleted;
        }

        /// <summary>v0.2 이전 마이그레이션: .asset YAML + 시크릿 JSON + EditorPrefs.</summary>
        static bool MigrateFromV02(ProjectData project, UserData user)
        {
            var migrated = false;

            // 1. 레거시 .asset YAML 파싱
            var assetPath = File.Exists(LegacyAssetPath) ? LegacyAssetPath : null;
            if (assetPath == null)
            {
                const string oldAsset = "Assets/Editor/GameServerSettings.asset";
                if (File.Exists(oldAsset)) assetPath = oldAsset;
            }

            if (assetPath != null)
            {
                var yaml = File.ReadAllText(assetPath);
                project.supabaseUrl = ParseYaml(yaml, "supabaseUrl");
                project.gcpProjectId = ParseYaml(yaml, "gcpProjectId");
                project.gcpRegion = ParseYaml(yaml, "gcpRegion", "asia-northeast3");
                project.gcpServiceName = ParseYaml(yaml, "gcpServiceName");
                project.gcpMinInstances = int.TryParse(ParseYaml(yaml, "gcpMinInstances"), out var mi) ? mi : 0;
                project.githubRepoName = ParseYaml(yaml, "githubRepoName");
                project.gcpCloudRunApiEnabled = ParseYaml(yaml, "gcpCloudRunApiEnabled") == "1";
                project.gcpServiceAccountEmail = ParseYaml(yaml, "gcpServiceAccountEmail");
                project.cloudRunUrl = ParseYaml(yaml, "cloudRunUrl");
                project.supabaseMaxConnections = int.TryParse(ParseYaml(yaml, "supabaseMaxConnections"), out var mc) ? mc : 60;
                project.gcpMaxInstances = int.TryParse(ParseYaml(yaml, "gcpMaxInstances"), out var mx) ? mx : 3;
                project.dbPoolSize = int.TryParse(ParseYaml(yaml, "dbPoolSize"), out var dp) ? dp : 20;

                user.setupCompleted = ParseYaml(yaml, "setupCompleted") == "1";
                user.serverLogToConsole = ParseYaml(yaml, "serverLogToConsole", "1") == "1";

                migrated = true;
            }

            // 2. 레거시 시크릿 JSON
            if (File.Exists(LegacySecretsJsonPath))
            {
                try
                {
                    var json = File.ReadAllText(LegacySecretsJsonPath);
                    var secrets = JsonUtility.FromJson<LegacyData>(json);
                    if (!string.IsNullOrEmpty(secrets.supabaseAnonKey)) project.supabaseAnonKey = secrets.supabaseAnonKey;
                    if (!string.IsNullOrEmpty(secrets.supabaseDbPassword)) project.supabaseDbPassword = secrets.supabaseDbPassword;
                    if (!string.IsNullOrEmpty(secrets.githubToken)) project.githubToken = secrets.githubToken;
                    if (!string.IsNullOrEmpty(secrets.supabaseAccessToken)) project.supabaseAccessToken = secrets.supabaseAccessToken;
                    if (!string.IsNullOrEmpty(secrets.cronSecret)) project.cronSecret = secrets.cronSecret;
                    migrated = true;
                }
                catch { /* 파싱 실패 무시 */ }
                File.Delete(LegacySecretsJsonPath);
            }

            // 3. EditorPrefs 시크릿
            var projectPrefix = EditorPrefUtils.ProjectPrefix;
            var legacyPrefixes = new[] { projectPrefix, "SupaRun_", "GameServer_" };

            var secretMap = new (string key, Action<string> setter)[]
            {
                ("SupabaseAnonKey", v => project.supabaseAnonKey = v),
                ("SupabaseDbPassword", v => project.supabaseDbPassword = v),
                ("GithubToken", v => project.githubToken = v),
                ("SupabaseAccessToken", v => project.supabaseAccessToken = v),
                ("CronSecret", v => project.cronSecret = v),
            };

            foreach (var (key, setter) in secretMap)
            {
                foreach (var prefix in legacyPrefixes)
                {
                    var val = EditorPrefs.GetString(prefix + key, "");
                    if (!string.IsNullOrEmpty(val))
                    {
                        setter(val);
                        EditorPrefs.DeleteKey(prefix + key);
                        migrated = true;
                        break;
                    }
                }
            }

            return migrated;
        }

        /// <summary>간단한 YAML "key: value" 파서.</summary>
        static string ParseYaml(string yaml, string key, string fallback = "")
        {
            var prefix = $"  {key}: ";
            foreach (var line in yaml.Split('\n'))
            {
                if (line.StartsWith(prefix))
                    return line.Substring(prefix.Length).Trim();
            }
            return fallback;
        }
    }
}
