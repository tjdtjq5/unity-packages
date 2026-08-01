using System;
using System.Collections.Generic;
using System.Net;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 어드민의 배포 체크리스트가 쓰는 라우트.
    ///
    /// **어드민은 준비를 갖추는 곳, Unity 는 실행하는 곳.** 여기에는 배포 자체가 없다 —
    /// 상태를 알려주고, 값을 정하게 하고, 자동화를 대신 돌려줄 뿐이다.
    /// 배포 버튼은 Unity Deploy 탭에만 있다.
    ///
    /// gcloud·gh 는 로컬 명령이라 브라우저가 직접 못 돌린다. 그래서 브리지가 손 역할을 한다.
    /// 어차피 Unity 가 켜져 있어야 어드민이 열리므로(브리지가 서빙한다) 전제가 깨지지 않는다.
    /// </summary>
    static class BridgeDeployRoutes
    {
        // ── 자동 설정 진행 ──
        // 수십 초짜리 작업이라 응답을 붙잡고 있을 수 없다. 백그라운드로 돌리고 진행을 상태에 싣는다.
        // 어드민은 로그인 완료 감지 때문에 어차피 폴링하므로 추가 비용이 거의 없다.

        static bool _autoRunning;
        static string _autoStep = "";
        static string _autoError;

        public static bool Matches(string path) =>
            path.StartsWith("/deploy/") || path.StartsWith("/setup/");

        public static async UniTask Handle(HttpListenerRequest req, HttpListenerResponse res, string path)
        {
            var m = req.HttpMethod;
            var s = SupaRunSettings.Instance;

            switch (path)
            {
                // ── 첫 셋업 ──
                // 관리자 표가 비어 있으면 **아무도 자기를 등록할 수 없다** — RLS 가 관리자만 쓰게 하고,
                // 관리자가 0명이니 그 조건을 만족하는 사람이 없다. PAT 를 쥔 이쪽이 그 매듭을 끊는다.

                case "/setup/state" when m == "GET":
                {
                    var env = s.Current;
                    var hasPat = !string.IsNullOrEmpty(SupaRunSettings.AccessTokenOf(env));
                    var projectRef = SupaRunSettings.ProjectIdOf(env.supabaseUrl);
                    var hasProject = !string.IsNullOrEmpty(projectRef) &&
                                     !string.IsNullOrEmpty(env.supabaseAnonKey);

                    // 관리자 수(unclaimed)는 더 묻지 않는다 — 첫 관리자 매듭은 기계 계정이
                    // 서빙 시점에 스스로 등록하면서 풀린다. "첫 로그인 버튼" 이 사라졌다.
                    BridgeIo.Write(res, 200, new JObject
                    {
                        ["hasPat"] = hasPat,
                        ["hasProject"] = hasProject,
                        ["projectRef"] = projectRef,
                        ["schemaReady"] = hasPat && hasProject && await SchemaReady(s),
                        ["initRunning"] = _initRunning,
                        ["initError"] = _initError,
                    });
                    return;
                }

                // PAT 저장. **PAT 검사 앞에 있는 라우트다** — 아직 PAT 가 없는 사람이 부르는 곳이다.
                //
                // 값은 브라우저에서 **들어오기만** 한다. 어드민은 이걸 다시 읽지 않고, DB 에도 안 간다.
                // 결정 6 이 막은 것은 DB→브라우저 방향이었고, 이건 그 반대다.
                case "/setup/pat" when m == "POST":
                {
                    var body = BridgeIo.ReadBody(req);
                    var pat = ((string)body["pat"] ?? "").Trim();
                    if (pat.Length == 0) { BridgeIo.Fail(res, 400, "토큰이 비어 있습니다."); return; }

                    // 진짜 토큰인지 먼저 확인한다 — 저장하고 나서 목록이 안 나오면 원인을 찾기 어렵다.
                    var probe = await SupabaseManagementApi.ListProjects(pat);
                    if (!probe.Ok)
                    { BridgeIo.Fail(res, 401, "토큰이 유효하지 않습니다.", probe.Hint); return; }

                    SupaRunSettings.SetAccessTokenOf(s.Current, pat);
                    BridgeIo.Write(res, 200, new JObject { ["projects"] = probe.Value.Length });
                    return;
                }

                // 프로젝트 선택. anon key 는 PAT 로 받아 채운다 — 사람이 복사해 올 이유가 없다.
                //
                // `env` 를 주면 그 환경 슬롯에 붙인다. 온보딩은 안 주므로 편집 환경에 붙는다.
                // 이 인자가 있어야 **새로 만든 슬롯을 연결할 수 있다** — 없으면 슬롯을 추가해도
                // 그 환경을 편집 환경으로 바꿔야만 붙일 수 있어, 붙이는 동안 컴파일 대상이 옮겨간다.
                case "/setup/project" when m == "POST":
                {
                    var body = BridgeIo.ReadBody(req);
                    var target = ((string)body["ref"] ?? "").Trim();
                    if (target.Length == 0) { BridgeIo.Fail(res, 400, "ref 가 필요합니다."); return; }

                    var envName = ((string)body["env"] ?? "").Trim();
                    var slot = envName.Length == 0 ? s.Current : s.GetEnvironment(envName);
                    if (slot == null) { BridgeIo.Fail(res, 404, $"'{envName}' 환경이 없습니다."); return; }

                    // PAT 는 Supabase **계정** 토큰이라 환경마다 다를 이유가 없다. 편집 환경 것으로
                    // 조회하고, 대상 슬롯에도 같은 값을 넣어 그 환경이 혼자서도 동작하게 한다.
                    var pat = SupaRunSettings.AccessTokenOf(s.Current);
                    if (string.IsNullOrEmpty(pat)) { BridgeIo.Fail(res, 409, "먼저 토큰을 넣으세요."); return; }

                    var key = await SupabaseManagementApi.GetAnonKey(target, pat);
                    if (!key.Ok) { BridgeIo.Fail(res, 502, key.Message, key.Hint); return; }

                    slot.supabaseUrl = $"https://{target}.supabase.co";
                    slot.supabaseAnonKey = key.Value;
                    s.Save();
                    if (!ReferenceEquals(slot, s.Current)) SupaRunSettings.SetAccessTokenOf(slot, pat);

                    // 접속 주소가 바뀌었으니 Auth 리다이렉트 목록도 다시 맞춘다.
                    // **편집 환경을 건드렸을 때만** — 이 동기화는 편집 환경 기준으로 돈다.
                    if (ReferenceEquals(slot, s.Current))
                    {
                        AuthUrlSyncManager.InvalidateCache();
                        AuthUrlSyncManager.CheckAndSync(s);
                    }

                    // 연결이 바뀌었으니 환경 현황 스냅샷도 다시 굽는다 — 안 그러면 방금 연결한
                    // 프로젝트가 카드에서 "미연결" 인 채로 남는다(스냅샷이 진실보다 낡아서).
                    EnvironmentSnapshot.CollectAndPublishAsync().Forget();

                    BridgeIo.Write(res, 200, new JObject { ["ref"] = target });
                    return;
                }

                // 스키마 반영. **물어보지 않고 부르는 자리다** — 안 하면 아무것도 동작하지 않으므로
                // "하시겠습니까?" 가 의미 없는 질문이다.
                case "/setup/init" when m == "POST":
                {
                    if (_initRunning) { BridgeIo.Fail(res, 409, "이미 진행 중입니다."); return; }
                    RunInit(s).Forget();
                    BridgeIo.Write(res, 200, new JObject { ["started"] = true });
                    return;
                }

                // claim-admin·reset-password 라우트는 없다 — 사람 로그인이 사라지면서
                // 첫 관리자 등록은 기계 계정(SupaRunMachineAccount)이 서빙 시점에 스스로 하고,
                // 비밀번호 복구도 그 안의 PAT 리셋이 대신한다.

                case "/deploy/status" when m == "GET":
                    BridgeIo.Write(res, 200, BuildStatus(s));
                    return;

                // ── 로그인 ──
                // fire-and-forget 이다. 브라우저가 열리고 사람이 거기서 끝낸다.
                // **완료는 폴링이 잡는다** — 다시 누를 필요가 없다.
                case "/deploy/gcloud-login" when m == "POST":
                    PrerequisiteChecker.RunGcloudLogin();
                    BridgeIo.Write(res, 200, new JObject { ["started"] = true });
                    return;

                case "/deploy/gh-login" when m == "POST":
                    PrerequisiteChecker.RunGhLogin();
                    BridgeIo.Write(res, 200, new JObject { ["started"] = true });
                    return;

                // 어드민이 대상 값을 저장한 뒤 부른다.
                // 어드민은 `suparun_env` 에 **직접** 쓰므로, Unity 가 다시 읽지 않으면
                // 아래 ready/blocked 판정이 낡은 캐시로 나온다 — 화면과 판정이 어긋난다.
                case "/deploy/refresh" when m == "POST":
                    PrerequisiteChecker.InvalidateCache();
                    PrerequisiteChecker.InvalidateBillingCache();
                    await SupaRunSettings.RefreshEnvAsync();
                    BridgeIo.Write(res, 200, new JObject { ["ok"] = true });
                    return;

                // ── GCP 프로젝트 ──
                case "/deploy/gcp-projects" when m == "GET":
                {
                    var arr = new JArray();
                    foreach (var (id, name) in await OffThread(PrerequisiteChecker.GetGcpProjects))
                        arr.Add(new JObject { ["id"] = id, ["name"] = name });
                    BridgeIo.Write(res, 200, new JObject { ["projects"] = arr });
                    return;
                }

                case "/deploy/gcp-projects" when m == "POST":
                {
                    var body = BridgeIo.ReadBody(req);
                    var id = ((string)body["id"] ?? "").Trim();
                    if (id.Length == 0) { BridgeIo.Fail(res, 400, "프로젝트 ID 가 필요합니다."); return; }

                    var name = (string)body["name"] ?? id;
                    var r = await OffThread(() => PrerequisiteChecker.CreateGcpProject(id, name));
                    if (!r.success) { BridgeIo.Fail(res, 502, r.error); return; }

                    // 방금 만든 것을 바로 쓰게 한다 — 목록을 다시 부르러 가는 왕복을 없앤다.
                    s.gcpProjectId = id;
                    s.Save();
                    PrerequisiteChecker.InvalidateBillingCache();
                    BridgeIo.Write(res, 200, new JObject { ["id"] = id });
                    return;
                }

                // ── 결제 ──
                case "/deploy/billing-accounts" when m == "GET":
                {
                    var arr = new JArray();
                    foreach (var (id, name) in await OffThread(PrerequisiteChecker.GetBillingAccounts))
                        arr.Add(new JObject { ["id"] = id, ["name"] = name });
                    BridgeIo.Write(res, 200, new JObject { ["accounts"] = arr });
                    return;
                }

                case "/deploy/billing-link" when m == "POST":
                {
                    var body = BridgeIo.ReadBody(req);
                    var account = ((string)body["account"] ?? "").Trim();
                    if (account.Length == 0) { BridgeIo.Fail(res, 400, "결제 계정이 필요합니다."); return; }
                    if (string.IsNullOrEmpty(s.gcpProjectId))
                    { BridgeIo.Fail(res, 409, "GCP 프로젝트를 먼저 고르세요."); return; }

                    var pid = s.gcpProjectId;
                    var r = await OffThread(() => PrerequisiteChecker.LinkBilling(pid, account));
                    if (!r.success) { BridgeIo.Fail(res, 502, r.error); return; }
                    BridgeIo.Write(res, 200, new JObject { ["ok"] = true });
                    return;
                }

                // ── GitHub 레포 ──
                case "/deploy/gh-repos" when m == "GET":
                {
                    var arr = new JArray();
                    foreach (var name in await OffThread(PrerequisiteChecker.GetGhRepos)) arr.Add(name);
                    BridgeIo.Write(res, 200, new JObject { ["repos"] = arr });
                    return;
                }

                case "/deploy/gh-repos" when m == "POST":
                {
                    var body = BridgeIo.ReadBody(req);
                    var name = ((string)body["name"] ?? "").Trim();
                    if (name.Length == 0) { BridgeIo.Fail(res, 400, "레포 이름이 필요합니다."); return; }

                    var gh = PrerequisiteChecker.CheckGh();
                    if (!gh.LoggedIn) { BridgeIo.Fail(res, 409, "gh 에 먼저 로그인하세요."); return; }

                    var full = $"{gh.Account}/{name}";
                    var r = await OffThread(() => PrerequisiteChecker.EnsureRepoExists(full));
                    if (!r.success) { BridgeIo.Fail(res, 502, r.error); return; }

                    s.githubRepoName = name;
                    s.Save();
                    BridgeIo.Write(res, 200,
                        new JObject { ["name"] = name, ["alreadyExisted"] = r.alreadyExisted });
                    return;
                }

                // ── 자동 설정 ──
                case "/deploy/auto-setup" when m == "POST":
                {
                    if (_autoRunning) { BridgeIo.Fail(res, 409, "이미 진행 중입니다."); return; }

                    var blocked = AutoSetupBlockedReason(s);
                    if (blocked != null) { BridgeIo.Fail(res, 409, blocked); return; }

                    RunAutoSetup(s).Forget();
                    BridgeIo.Write(res, 200, new JObject { ["started"] = true });
                    return;
                }
            }

            BridgeIo.Fail(res, 404, $"알 수 없는 경로: {path}");
        }

        // ── 상태 ──

        /// <summary>
        /// 체크리스트가 필요로 하는 전부를 한 번에 준다. 어드민은 이것만 주기적으로 부른다 —
        /// 로그인이 끝났는지, 자동 설정이 어디까지 갔는지가 전부 여기로 드러난다.
        /// </summary>
        static JObject BuildStatus(SupaRunSettings s)
        {
            var gcloud = PrerequisiteChecker.CheckGcloud();
            var gh = PrerequisiteChecker.CheckGh();
            var dotnetMajor = PrerequisiteChecker.GetDotnetMajorVersion();

            var pid = s.gcpProjectId;
            // 프로젝트가 없으면 물어볼 것도 없다. CLI 왕복을 아낀다.
            var billing = !string.IsNullOrEmpty(pid) && gcloud.LoggedIn &&
                          PrerequisiteChecker.IsBillingEnabled(pid);

            var permOk = s.gcpCloudRunApiEnabled && !string.IsNullOrEmpty(s.gcpServiceAccountEmail);

            var ready = gcloud.LoggedIn && gh.LoggedIn && billing && permOk &&
                        !string.IsNullOrEmpty(pid) &&
                        !string.IsNullOrEmpty(s.gcpServiceName) &&
                        !string.IsNullOrEmpty(s.githubRepoName);

            return new JObject
            {
                ["tools"] = new JObject
                {
                    ["dotnet"] = Tool(dotnetMajor > 0, false, null, dotnetMajor > 0 ? $"{dotnetMajor}.0" : null, "dotnet"),
                    ["gcloud"] = Tool(gcloud.Installed, gcloud.LoggedIn, gcloud.Account, gcloud.Version, "gcloud"),
                    ["gh"] = Tool(gh.Installed, gh.LoggedIn, gh.Account, gh.Version, "gh"),
                },
                ["billing"] = new JObject
                {
                    ["enabled"] = billing,
                    // 프로젝트를 안 고르면 물어볼 수 없다. 화면이 이유를 한 줄로 적는다.
                    ["blocked"] = string.IsNullOrEmpty(pid) ? "GCP 프로젝트를 먼저 고르세요." : null,
                },
                ["permission"] = new JObject
                {
                    ["ok"] = permOk,
                    ["serviceAccount"] = s.gcpServiceAccountEmail,
                    ["blocked"] = AutoSetupBlockedReason(s),
                },
                ["target"] = new JObject
                {
                    ["name"] = s.EnvName,
                    ["gcpProjectId"] = pid,
                    ["gcpRegion"] = s.gcpRegion,
                    ["gcpServiceName"] = s.gcpServiceName,
                    ["gcpMinInstances"] = s.gcpMinInstances,
                    ["githubRepoName"] = s.githubRepoName,
                    ["serverCaches"] = string.Join(",", s.enabledServerCaches),
                },
                ["autoSetup"] = new JObject
                {
                    ["running"] = _autoRunning,
                    ["step"] = _autoStep,
                    ["error"] = _autoError,
                },
                ["ready"] = ready,
            };
        }

        static JObject Tool(bool installed, bool loggedIn, string account, string version, string key) =>
            new()
            {
                ["installed"] = installed,
                ["loggedIn"] = loggedIn,
                ["account"] = account,
                ["version"] = version,
                // 설치가 안 됐을 때만 준다 — 필요 없을 때 화면에 명령어가 떠 있을 이유가 없다.
                ["installCommand"] = installed ? null : PrerequisiteChecker.InstallCommand(key),
            };

        /// <summary>설정에서 원시값을 뽑아 판정에 넘긴다.</summary>
        static string AutoSetupBlockedReason(SupaRunSettings s) => BlockedReason(
            PrerequisiteChecker.CheckGcloud().LoggedIn,
            PrerequisiteChecker.CheckGh().LoggedIn,
            s.gcpProjectId,
            s.githubRepoName,
            s.gcpServiceName,
            !string.IsNullOrEmpty(s.gcpProjectId) && PrerequisiteChecker.IsBillingEnabled(s.gcpProjectId));

        /// <summary>
        /// 자동 설정을 아직 못 누르는 이유. 없으면 null — 화면이 버튼을 열어 준다.
        ///
        /// **순수 함수다 — 단위 테스트 대상.** `SupaRunSettings`·`PrerequisiteChecker` 의존을 끊어
        /// 원시값만 받는다(옛 `GcpSetupUI.GetPhase` 가 같은 이유로 그렇게 돼 있었다).
        ///
        /// 순서가 곧 사람이 밟는 순서다. 위에서부터 막힌 첫 이유를 돌려주므로
        /// 화면은 **한 번에 하나만** 말하게 된다.
        /// </summary>
        internal static string BlockedReason(
            bool gcloudLoggedIn, bool ghLoggedIn,
            string gcpProjectId, string githubRepo, string serviceName, bool billingEnabled)
        {
            if (!gcloudLoggedIn) return "gcloud 에 먼저 로그인하세요.";
            if (!ghLoggedIn) return "gh 에 먼저 로그인하세요.";
            if (string.IsNullOrEmpty(gcpProjectId)) return "GCP 프로젝트를 먼저 고르세요.";
            if (string.IsNullOrEmpty(githubRepo)) return "GitHub 레포를 먼저 고르세요.";
            if (string.IsNullOrEmpty(serviceName)) return "Cloud Run 서비스명을 먼저 정하세요.";
            if (!billingEnabled) return "결제 계정을 먼저 연결하세요.";
            return null;
        }

        // ── 자동 설정 실행 ──

        static async UniTaskVoid RunAutoSetup(SupaRunSettings s)
        {
            _autoRunning = true;
            _autoError = null;
            _autoStep = "Cloud Run API 켜는 중 (1/3)";

            var gh = PrerequisiteChecker.CheckGh();
            var repo = $"{gh.Account}/{s.githubRepoName}";
            var pid = s.gcpProjectId;
            var region = s.gcpRegion;
            var service = s.gcpServiceName;

            try
            {
                // CLI 를 여럿 기다리는 동안 에디터가 얼면 안 된다. 프로세스 대기는 스레드풀로 민다.
                var r = await OffThread(() =>
                    PrerequisiteChecker.AutoSetupCloudRun(pid, region, service, repo));

                if (!r.success)
                {
                    _autoError = r.error;
                    return;
                }

                // 결과는 Unity 가 알아낸 **사실**이다. 어드민은 상태로만 본다.
                s.gcpCloudRunApiEnabled = true;
                s.gcpServiceAccountEmail = r.saEmail;
                s.Save();
                _autoStep = "완료";
            }
            catch (Exception ex)
            {
                _autoError = ex.Message;
            }
            finally
            {
                _autoRunning = false;
            }
        }

        // ── 첫 셋업 ──

        static bool _initRunning;
        static string _initError;

        /// <summary>스키마가 반영돼 있는가. 표 하나만 물어보면 충분하다.</summary>
        static async UniTask<bool> SchemaReady(SupaRunSettings s)
        {
            var env = s.Current;
            var id = SupaRunSettings.ProjectIdOf(env.supabaseUrl);
            var token = SupaRunSettings.AccessTokenOf(env);
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(token)) return false;

            var r = await SupabaseManagementApi.RunQuery(id, token,
                "SELECT to_regclass('public.suparun_env') IS NOT NULL AS ok;");
            if (!r.Ok) return false;

            try
            {
                var rows = JArray.Parse(r.Value ?? "[]");
                return rows.Count > 0 && (bool?)rows[0]["ok"] == true;
            }
            catch { return false; }
        }

        static async UniTaskVoid RunInit(SupaRunSettings s)
        {
            _initRunning = true;
            _initError = null;
            try
            {
                if (!await SchemaAutoSync.SyncToEnvironment(s.Current, force: true))
                {
                    _initError = "스키마 반영 실패 — Unity Console 을 확인하세요.";
                    return;
                }

                // 표가 방금 생겼다 — 캐시를 이 환경의 값으로 갈아탄 뒤(이전 환경 것이 남아 있을
                // 수 있다) 표시 이름을 박는다. 이름의 진실은 슬롯이고 DB 는 사본이다 —
                // 브리지가 없는 배포 어드민은 이 사본밖에 못 본다.
                await SupaRunSettings.RefreshEnvAsync();
                s.EnvName = s.Current.name;
                s.Save();

                // 가입 즉시 로그인 보장. 새 Supabase 프로젝트는 mailer_autoconfirm 이 꺼져 있어
                // 첫 관리자 가입이 확인 메일 경로로 빠진다 — 기본 SMTP 는 시간당 2통이라 사실상
                // 막힌 길이다. 예전에는 배포(DeployManager)가 켜 줬는데, 셋업 직후 자동 입장이
                // 생기면서 **배포 전에 가입 화면에 도달**하므로 여기서 보장해야 한다.
                var patch = await SupabaseManagementApi.PatchAuthConfig(
                    SupaRunSettings.ProjectIdOf(s.Current.supabaseUrl),
                    SupaRunSettings.AccessTokenOf(s.Current),
                    "{\"mailer_autoconfirm\":true}");
                patch.LogIfFailed("autoconfirm 설정");

                // Auth 리다이렉트 동기화도 여기서 — 셋업 흐름은 chooseProject 시점에 슬롯이
                // 아직 편집 환경이 아니라(전환은 그 다음) 그쪽 동기화가 건너뛰어진다.
                AuthUrlSyncManager.InvalidateCache();
                AuthUrlSyncManager.CheckAndSync(s);

                // 메타 표가 방금 생겼다 — 이 환경의 DB 에 첫 스냅샷을 굽는다.
                // 없으면 새 환경의 환경 화면이 빈 채로 뜬다.
                EnvironmentSnapshot.CollectAndPublishAsync().Forget();
            }
            catch (Exception ex)
            {
                _initError = ex.Message;
                UnityEngine.Debug.LogError($"[SupaRun:Setup] 초기화 실패 — {ex.Message}");
            }
            finally { _initRunning = false; }
        }

        /// <summary>
        /// 블로킹 CLI 를 스레드풀에서 돌린다. 메인 스레드에서 그대로 부르면 에디터가 얼고
        /// **브리지 전체가 멈춘다**(요청 처리가 메인 스레드의 Pump 에서 돈다).
        /// </summary>
        static async UniTask<T> OffThread<T>(Func<T> work)
        {
            await UniTask.SwitchToThreadPool();
            try { return work(); }
            finally { await UniTask.SwitchToMainThread(); }
        }
    }
}
