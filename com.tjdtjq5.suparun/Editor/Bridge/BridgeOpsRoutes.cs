using System;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Net;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// **Unity 가 실행하는 것들.** 어드민이 버튼을 누르고 여기가 돌린다.
    ///
    /// 원래 이것들은 대시보드의 Deploy 탭에 있었다. 대시보드를 없애면서 옮겨 왔는데,
    /// 화면만 옮긴 것이 아니라 **주인이 바뀌었다** — 예전에는 탭이 상태를 들고 있었고(`_state`)
    /// OnGUI 가 매 프레임 그것을 그렸다. 지금은 이 정적 필드가 유일한 상태이고 어드민이 폴링한다.
    ///
    /// 그래서 지켜야 하는 것 하나: **여기서 UI 를 부르지 않는다.** 확인 대화상자(DisplayDialog)를
    /// 띄우면 브라우저는 눌린 줄 알고 기다리는데 Unity 창 뒤에 모달이 떠 있게 된다.
    /// 확인은 어드민이 받고, 여기는 받은 대로 실행한다.
    ///
    /// 왜 브라우저가 직접 못 하나: 스키마 반영·배포·Id 생성은 전부 로컬 파일과 CLI 를 만진다.
    /// gcloud·gh·dotnet 은 이 컴퓨터의 명령이고, 생성된 상수 파일은 이 프로젝트에 쓰인다.
    /// </summary>
    static class BridgeOpsRoutes
    {
        public static bool Matches(string path) => path.StartsWith("/ops/");

        public static async UniTask Handle(HttpListenerRequest req, HttpListenerResponse res, string path)
        {
            var m = req.HttpMethod;
            var s = SupaRunSettings.Instance;

            switch (path)
            {
                case "/ops/state" when m == "GET":
                    // 배포는 스스로 진행하지 않는다 — ActionsTracker 가 EditorApplication.update 로
                    // 폴링하고, 그 결과를 여기서 우리 상태로 옮긴다. 읽을 때 옮기는 것으로 충분하다.
                    SyncDeployFromTracker(s);
                    BridgeIo.Write(res, 200, BuildState(s));
                    return;

                // ── 스키마 ──
                // 수동 반영·요약 라우트는 없다 — 반영 경로는 둘뿐이다:
                // 자동 켠 환경은 컴파일이, 끈 환경은 배포가(선반영) 민다. 승격은 따로 남는다.

                // 컴파일 후 자동 반영 토글. **편집 환경의 팀 공유값**이다(설정 파일, git).
                case "/ops/env-auto-schema" when m == "POST":
                {
                    var enabled = (bool?)BridgeIo.ReadBody(req)["enabled"] ?? false;
                    s.Current.autoSchemaSync = enabled;
                    s.Save();
                    BridgeIo.Write(res, 200, new JObject { ["enabled"] = enabled });
                    return;
                }

                // 행 편집 시 Id 상수 자동 생성 토글. 같은 성격(편집 환경, 팀 공유).
                case "/ops/env-auto-ids" when m == "POST":
                {
                    var enabled = (bool?)BridgeIo.ReadBody(req)["enabled"] ?? false;
                    s.Current.autoIdConstants = enabled;
                    s.Save();
                    BridgeIo.Write(res, 200, new JObject { ["enabled"] = enabled });
                    return;
                }

                // ── Id 상수 ──
                // 동기다. 코드 스캔 + 파일 쓰기라 몇 초 안에 끝나고, 결과를 바로 돌려주는 편이
                // 폴링으로 받는 것보다 화면이 단순하다.
                //
                // 부르는 곳은 어드민의 **자동 트리거뿐이다**(행 추가/삭제·스냅샷 복원 — 수동 버튼은
                // 없다). 환경별 토글이 꺼져 있으면 조용히 건너뛴다 — 정책은 여기 한 곳이 갖고,
                // 어드민은 "PK 집합이 바뀌었을 수 있다" 고 알리기만 한다.
                case "/ops/id-constants" when m == "POST":
                {
                    if (!s.Current.autoIdConstants)
                    { BridgeIo.Write(res, 200, new JObject { ["skipped"] = true }); return; }
                    try
                    {
                        var r = IdConstantGenerator.Generate();
                        var errors = new JArray();
                        foreach (var e in r.Errors) errors.Add(e);
                        var generated = new JArray();
                        foreach (var g in r.Generated) generated.Add(g);

                        BridgeIo.Write(res, 200, new JObject
                        {
                            ["ok"] = r.Ok,
                            ["fileCount"] = r.FileCount,
                            ["outputDir"] = r.OutputDir,
                            ["generated"] = generated,
                            ["errors"] = errors,
                        });
                    }
                    catch (Exception ex) { BridgeIo.Fail(res, 500, ex.Message); }
                    return;
                }

                // ── 배포 ──

                case "/ops/deploy" when m == "POST":
                {
                    if (_deployPhase is "verifying" or "deploying" or "tracking")
                    { BridgeIo.Fail(res, 409, "이미 배포 중입니다."); return; }

                    if (!s.IsGitHubConfigured)
                    { BridgeIo.Fail(res, 409, "GitHub·GCP 설정이 먼저 필요합니다.", "설정 화면의 배포 항목을 채우세요."); return; }

                    // 빌드 검증 생략 여부는 **어드민이 정한다.** dotnet 이 없을 때 여기서 임의로
                    // 넘기면, 서버 빌드가 GitHub 에서 깨진 뒤에야 알게 된다.
                    var body = BridgeIo.ReadBody(req);
                    var skipVerify = (bool?)body["skipVerify"] ?? false;

                    if (!DeployManager.IsDotnetAvailable() && !skipVerify)
                    {
                        BridgeIo.Fail(res, 412, ".NET SDK 가 없어 빌드 검증을 할 수 없습니다.",
                            "검증 없이 배포하려면 다시 누르세요.");
                        return;
                    }

                    StartDeploy(s, skipVerify).Forget();
                    BridgeIo.Write(res, 200, new JObject { ["started"] = true });
                    return;
                }

                // 결과 화면을 닫는다. 상태가 success/failed 로 남아 있으면 다음 배포 버튼이 안 보인다.
                case "/ops/deploy-reset" when m == "POST":
                    if (_deployPhase is "verifying" or "deploying" or "tracking")
                    { BridgeIo.Fail(res, 409, "진행 중에는 지울 수 없습니다."); return; }
                    _deployPhase = "idle";
                    _deployMessage = _deployError = null;
                    BridgeIo.Write(res, 200, new JObject { ["ok"] = true });
                    return;

                // ── 환경 ──

                case "/ops/env-select" when m == "POST":
                {
                    var name = ((string)BridgeIo.ReadBody(req)["name"] ?? "").Trim();
                    if (s.GetEnvironment(name) == null)
                    { BridgeIo.Fail(res, 404, $"'{name}' 환경이 없습니다."); return; }

                    // 라이브로 보이는 이름이어도 막지 않는다 — 확인은 어드민이 받았다.
                    // 여기서 또 물으면 Unity 창 뒤에 모달이 떠 브라우저가 영영 기다린다.
                    s.EditorEnvironment = name;
                    Debug.Log($"[SupaRun] 편집 환경 → {name}");

                    // 환경이 바뀌면 그 환경의 값으로 갈아타야 한다. 캐시가 남아 있으면
                    // 화면은 새 환경인데 판정은 옛 환경 값으로 나온다.
                    await SupaRunSettings.RefreshEnvAsync();

                    // 환경 현황 스냅샷도 새 환경 DB 에 다시 굽는다 — 어드민 메뉴로 열 때만 굽던
                    // 시절엔 전환 입장 뒤 카드가 낡은 채(미연결 거짓 표시 등) 남았다.
                    // 몇 초짜리 수집이라 응답을 붙잡지 않는다.
                    EnvironmentSnapshot.CollectAndPublishAsync().Forget();
                    BridgeIo.Write(res, 200, new JObject { ["name"] = name });
                    return;
                }

                // 빌드 환경 라우트는 없다 — 빌드 = 편집 환경(SupaRunBuildProcessor 참조).

                case "/ops/env-add" when m == "POST":
                {
                    var name = ((string)BridgeIo.ReadBody(req)["name"] ?? "").Trim();
                    if (name.Length == 0) { BridgeIo.Fail(res, 400, "이름이 필요합니다."); return; }
                    if (s.GetEnvironment(name) != null)
                    { BridgeIo.Fail(res, 409, $"'{name}' 은 이미 있습니다."); return; }

                    s.AddEnvironment(name);
                    BridgeIo.Write(res, 200, new JObject { ["name"] = name });
                    return;
                }

                // 이름 변경 — **편집 환경만.** 이름의 진실은 슬롯이라 여기서 바꾼다(어드민의 이름
                // 필드가 이 라우트로 온다). 남의 환경 이름을 원격으로 바꾸는 길은 열지 않는다 —
                // 설정 화면이 곧 편집 환경이고, 그 밖의 경우가 없다.
                case "/ops/env-rename" when m == "POST":
                {
                    var to = ((string)BridgeIo.ReadBody(req)["to"] ?? "").Trim();
                    if (to.Length == 0) { BridgeIo.Fail(res, 400, "이름이 필요합니다."); return; }

                    var from = s.EditorEnvironment;
                    if (to == from) { BridgeIo.Write(res, 200, new JObject { ["name"] = to }); return; }
                    if (!s.RenameEnvironment(from, to))
                    { BridgeIo.Fail(res, 409, $"'{to}' 은 이미 있습니다."); return; }

                    // 반영 기록(해시)과 DB 표시 이름이 같이 움직여야 한다. 해시를 두고 오면 다음
                    // 반영이 전체 재반영으로 보이고, DB 를 두고 오면 카드가 옛 이름을 말한다.
                    SchemaAutoSync.RenameHashFiles(from, to);
                    s.EnvName = to;
                    s.Save();

                    Debug.Log($"[SupaRun] 환경 이름 변경 — {from} → {to}");
                    BridgeIo.Write(res, 200, new JObject { ["name"] = to });
                    return;
                }

                case "/ops/env-remove" when m == "POST":
                {
                    var name = ((string)BridgeIo.ReadBody(req)["name"] ?? "").Trim();
                    if (s.Environments.Count <= 1)
                    { BridgeIo.Fail(res, 409, "마지막 환경은 지울 수 없습니다."); return; }

                    var wasEditor = name == s.EditorEnvironment;
                    if (!s.RemoveEnvironment(name))
                    { BridgeIo.Fail(res, 404, $"'{name}' 환경이 없습니다."); return; }

                    // 편집 환경을 지웠으면 선택이 남은 환경으로 옮겨졌다(RemoveEnvironment 가 한다).
                    // 캐시도 그 환경의 값으로 갈아타야 어드민이 리로드했을 때 옛 값이 안 섞인다.
                    if (wasEditor) await SupaRunSettings.RefreshEnvAsync();

                    // 지운 것은 목록에서만 사라진다. Supabase 프로젝트와 그 데이터는 그대로다 —
                    // 여기서 프로젝트까지 지우면 되돌릴 수 없는 일이 목록 편집처럼 보인다.
                    Debug.Log($"[SupaRun] 환경 제거 — {name} (Supabase 프로젝트는 그대로입니다)");
                    BridgeIo.Write(res, 200, new JObject { ["name"] = name });
                    return;
                }

                // ── 승격 ──
                // 편집 환경을 잠깐 바꿔 처리하지 않는다. 되돌리는 것을 잊으면 그 다음 컴파일이
                // 곧바로 라이브 스키마를 건드린다 — 대상은 항상 명시적으로 받는다.

                case "/ops/promote-schema" when m == "POST":
                {
                    var target = ResolveTarget(s, req, res);
                    if (target == null) return;
                    if (_schemaRunning) { BridgeIo.Fail(res, 409, "이미 진행 중입니다."); return; }
                    RunSchema(target, isPromote: true).Forget();
                    BridgeIo.Write(res, 200, new JObject { ["started"] = true });
                    return;
                }

                // 릴리스 오케스트레이션 (#51) — 트래픽 전환 → 게시 → logic 게이트, 순차+단계 기록.
                // 대상은 **편집 환경 자신**이다(릴리스는 그 환경 안의 조작) — 다른 ops 와 달리
                // target 을 받지 않는다.
                case "/ops/release" when m == "POST":
                {
                    var body = BridgeIo.ReadBody(req);
                    var lv = (int?)body["logicVersion"] ?? 0;
                    var lmin = (int?)body["logicMin"] ?? 1;
                    var schema = (string)body["versionSchema"] ?? "";
                    if (lv <= 0 || schema.Length == 0)
                    { BridgeIo.Fail(res, 400, "logicVersion 과 versionSchema 가 필요합니다."); return; }
                    if (_schemaRunning) { BridgeIo.Fail(res, 409, "이미 진행 중입니다."); return; }
                    RunRelease(s, lv, lmin, schema, (string)body["memo"], (string)body["revisionTag"]).Forget();
                    BridgeIo.Write(res, 200, new JObject { ["started"] = true });
                    return;
                }

                // 데이터는 이제 즉시 주입되지 않는다 — 대상에 **미게시 버전**을 만들 뿐이고
                // (ADR-0010, #30), 라이브 반영은 대상 어드민의 게시(publish)가 따로 한다.
                case "/ops/upload-version" when m == "POST":
                {
                    var target = ResolveTarget(s, req, res);
                    if (target == null) return;
                    if (_schemaRunning) { BridgeIo.Fail(res, 409, "이미 진행 중입니다."); return; }
                    RunUpload(s.Current, target).Forget();
                    BridgeIo.Write(res, 200, new JObject { ["started"] = true });
                    return;
                }
            }

            BridgeIo.Fail(res, 404, $"알 수 없는 경로: {path}");
        }

        /// <summary>승격 대상을 꺼낸다. 못 찾으면 응답까지 마치고 null 을 준다.</summary>
        static SupaRunSettings.EnvironmentData ResolveTarget(
            SupaRunSettings s, HttpListenerRequest req, HttpListenerResponse res)
        {
            var name = ((string)BridgeIo.ReadBody(req)["target"] ?? "").Trim();
            var env = s.GetEnvironment(name);
            if (env == null) { BridgeIo.Fail(res, 404, $"'{name}' 환경이 없습니다."); return null; }
            if (name == s.EditorEnvironment)
            { BridgeIo.Fail(res, 400, "편집 환경 자신에게는 승격할 수 없습니다."); return null; }
            return env;
        }

        // ── 상태 ──

        static JObject BuildState(SupaRunSettings s)
        {
            var envs = new JArray();
            foreach (var e in s.Environments)
                envs.Add(new JObject
                {
                    ["name"] = e.name,
                    // 값이 아니라 **채워졌는지**만 준다. anon key 는 게임 빌드에도 들어가는 값이지만
                    // 굳이 화면으로 흘릴 이유가 없다.
                    ["configured"] = !string.IsNullOrEmpty(e.supabaseUrl) &&
                                     !string.IsNullOrEmpty(e.supabaseAnonKey),
                    ["projectRef"] = SupaRunSettings.ProjectIdOf(e.supabaseUrl),
                    ["cloudRunUrl"] = SupaRunSettings.CloudRunUrlOf(e),
                    ["autoSchemaSync"] = e.autoSchemaSync,
                    ["autoIdConstants"] = e.autoIdConstants,
                });

            return new JObject
            {
                ["editorEnv"] = s.EditorEnvironment,
                ["environments"] = envs,
                ["dotnet"] = DeployManager.IsDotnetAvailable(),
                ["deployConfigured"] = s.IsGitHubConfigured,
                ["schema"] = new JObject
                {
                    ["running"] = _schemaRunning,
                    ["label"] = _schemaLabel,
                    ["error"] = _schemaError,
                },
                ["deploy"] = new JObject
                {
                    ["phase"] = _deployPhase,
                    ["message"] = _deployMessage,
                    ["error"] = _deployError,
                    ["url"] = ActionsTracker.CloudRunUrl ?? s.cloudRunUrl,
                    // timeSinceStartup 은 메인 스레드 전용이다. 브리지 Pump 가 메인 스레드에서
                    // 도는 덕에 여기서는 안전하다 — OffThread 안에서 부르면 예외가 난다.
                    ["elapsed"] = _deployPhase == "tracking" ? (int)ActionsTracker.ElapsedSeconds : 0,
                    ["actionsUrl"] = ActionsUrl(s),
                },
            };
        }

        static string ActionsUrl(SupaRunSettings s)
        {
            var gh = PrerequisiteChecker.CheckGh();
            if (!gh.LoggedIn || string.IsNullOrEmpty(s.githubRepoName)) return null;
            return ActionsTracker.GetActionsUrl($"{gh.Account}/{s.githubRepoName}");
        }

        // ── 스키마·승격 실행 ──

        static bool _schemaRunning;
        static string _schemaLabel;
        static string _schemaError;

        static async UniTaskVoid RunSchema(SupaRunSettings.EnvironmentData target, bool isPromote)
        {
            _schemaRunning = true;
            _schemaError = null;
            _schemaLabel = isPromote ? $"'{target.name}' 에 스키마 반영 중" : "스키마 반영 중";
            try
            {
                if (await SchemaAutoSync.SyncToEnvironment(target))
                    _schemaLabel = isPromote ? $"'{target.name}' 스키마 반영 완료" : "스키마 반영 완료";
                else
                {
                    _schemaError = "반영 실패 — Unity Console 을 확인하세요.";
                    _schemaLabel = null;
                }
            }
            catch (Exception ex)
            {
                _schemaError = ex.Message;
                _schemaLabel = null;
            }
            finally { _schemaRunning = false; }
        }

        static async UniTaskVoid RunRelease(
            SupaRunSettings s, int logicVersion, int logicMin, string schema, string memo, string tag)
        {
            _schemaRunning = true;
            _schemaError = null;
            _schemaLabel = $"릴리스 실행 중 — logic {logicVersion}, {schema}";
            try
            {
                var relId = await ReleaseOrchestrator.RunAsync(s, logicVersion, logicMin, schema, memo, tag);
                if (relId != null) _schemaLabel = $"릴리스 완료 — {relId}";
                else { _schemaError = "릴리스 실패 — 매니페스트의 단계 기록을 확인하세요."; _schemaLabel = null; }
            }
            catch (Exception ex)
            {
                _schemaError = ex.Message;
                _schemaLabel = null;
            }
            finally { _schemaRunning = false; }
        }

        static async UniTaskVoid RunUpload(
            SupaRunSettings.EnvironmentData from, SupaRunSettings.EnvironmentData to)
        {
            _schemaRunning = true;
            _schemaError = null;
            _schemaLabel = $"'{from.name}' → '{to.name}' 버전 업로드 중";
            try
            {
                var schema = await EnvironmentPromoter.UploadVersionAsync(from, to);
                if (schema != null)
                    _schemaLabel = $"'{to.name}' 에 미게시 버전 생성 완료 ({schema}) — 게시는 그쪽 어드민에서";
                else { _schemaError = "업로드에 실패했습니다. Console 을 확인하세요."; _schemaLabel = null; }
            }
            catch (Exception ex)
            {
                _schemaError = ex.Message;
                _schemaLabel = null;
            }
            finally { _schemaRunning = false; }
        }

        // ── 배포 실행 ──
        //
        // 옛 DeployTab 의 상태 기계를 그대로 옮겼다. 다른 점은 두 가지다:
        //   Repaint 가 없다 — 어드민이 폴링한다
        //   dotnet 경고 분기가 없다 — 검증 생략은 요청이 정하고 여기는 받은 대로 한다

        static string _deployPhase = "idle";   // idle|verifying|deploying|tracking|success|failed|skipped
        static string _deployMessage;
        static string _deployError;

        static async UniTaskVoid StartDeploy(SupaRunSettings s, bool skipVerify)
        {
            _deployError = null;

            // 스키마를 먼저 민다 — "배포했다 = 스키마도 최신" 불변식.
            // 자동 반영을 끈 환경(prod)은 이 경로가 유일한 반영 통로다. 해시 가드가 있어
            // 변경이 없으면 즉시 통과하고, 실패하면 배포를 중단한다 — 옛 스키마 위에
            // 새 서버 코드만 뜨는 어긋남이 배포 실패보다 비싸다.
            _deployPhase = "verifying";
            _deployMessage = "스키마 반영 중…";
            if (!await SchemaAutoSync.SyncToEnvironment(s.Current, force: false))
            {
                Fail("스키마 반영 실패 — 배포를 중단합니다. Unity Console 을 확인하세요.");
                return;
            }

            if (!skipVerify && DeployManager.IsDotnetAvailable())
            {
                _deployPhase = "verifying";
                _deployMessage = "빌드 검증 중…";

                // 코드 생성과 파일 쓰기는 메인 스레드에서.
                var (tempDir, prepError) = DeployManager.PrepareBuildTest(s);
                if (tempDir == null) { Fail(prepError); return; }

                var build = await UniTask.RunOnThreadPool(() => DeployManager.RunDotnetBuild(tempDir));
                if (!build.success) { Fail("빌드 검증 실패:\n" + build.output); return; }
            }

            await DoDeploy(s);
        }

        /// <summary>
        /// 배포 **직전에** 어드민이 정한 값을 당겨 온다.
        ///
        /// 배포 대상(GCP 프로젝트·리전·서비스명·레포)의 진실은 `suparun_env` 이고 어드민이 고친다.
        /// 낡은 값으로 밀면 **엉뚱한 곳에 배포된다** — 되돌리기 비싼 실수다.
        /// 읽기에 실패하면 진행하지 않는다. 마지막으로 본 값으로 밀어붙이는 것보다 멈추는 편이 낫다.
        /// </summary>
        static async UniTask DoDeploy(SupaRunSettings s)
        {
            _deployPhase = "deploying";
            _deployMessage = "배포 설정 확인 중…";

            var env = await SupaRunSettings.RefreshEnvAsync();
            if (!env.Ok) { Fail($"배포 설정을 읽지 못했습니다 — {env.ToShortString()}"); return; }

            _deployMessage = "코드 스캔 중…";

            DeployManager.Deploy(s,
                onProgress: msg => _deployMessage = msg,
                onSuccess: () =>
                {
                    var gh = PrerequisiteChecker.CheckGh();
                    ActionsTracker.StartTracking(
                        $"{gh.Account}/{s.githubRepoName}", GitHubPusher.LastPushedSha);
                    _deployPhase = "tracking";
                    _deployMessage = "Push 완료 — GitHub Actions 빌드 대기 중";
                },
                onFailed: Fail,
                onSkipped: () =>
                {
                    _deployPhase = "skipped";
                    _deployMessage = "코드 변경 없음 — 배포를 건너뛰었습니다";
                });
        }

        static void Fail(string error)
        {
            _deployPhase = "failed";
            _deployMessage = null;
            _deployError = error;
        }

        /// <summary>
        /// ActionsTracker 의 결과를 우리 상태로 옮긴다. 상태를 읽을 때 부른다 —
        /// 옛 대시보드는 OnGUI 가 매 프레임 이 일을 했는데, 그 화면이 없어졌다.
        /// </summary>
        static void SyncDeployFromTracker(SupaRunSettings s)
        {
            if (_deployPhase != "tracking") return;

            switch (ActionsTracker.CurrentStatus)
            {
                case ActionsTracker.Status.Success:
                    _deployPhase = "success";
                    _deployMessage = "배포 완료";
                    // 크론 등록은 배포가 끝나야 의미가 있다 — 서버가 그때 생긴다.
                    DeployManager.RegisterCronJobs().Forget();
                    break;

                case ActionsTracker.Status.Failed:
                    Fail(ActionsTracker.FailedLog);
                    break;

                case ActionsTracker.Status.Timeout:
                    Fail("10분을 넘겼습니다 — GitHub Actions 에서 직접 확인하세요.");
                    break;
            }
        }
    }
}
