using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// Supabase 프로젝트 관리 — 목록 · 생성 · 삭제 · 복구.
    ///
    /// 환경(dev/prod…)과 짝을 이룬다: 환경은 "어느 프로젝트를 쓰는가" 이고 여기는 "그 프로젝트가 실제로 있는가" 다.
    /// 그래서 목록에 **이 프로젝트를 쓰는 환경**을 같이 표시한다 — 지우려는 것이 지금 쓰는 것인지
    /// 화면에서 바로 보여야 한다.
    ///
    /// ⚠ **리전은 생성할 때만 정할 수 있다.** `PATCH /v1/projects/{ref}` 가 바꾸는 것은 이름뿐이라
    /// 기존 프로젝트의 리전은 읽기 전용으로 보여주고, 옮기려면 새로 만들어 승격하도록 안내한다.
    /// </summary>
    public class ProjectManagerUI
    {
        readonly Func<string> _token;
        readonly Action _repaint;

        bool _expanded;
        bool _loading;
        string _listError;
        SupabaseManagementApi.ProjectInfo[] _projects;

        // 생성 폼
        bool _showCreate;
        string _newName = "";
        string _newPlan = "free";
        int _regionIdx = -1;
        SupabaseManagementApi.RegionInfo[] _regions;
        bool _loadingRegions;

        // 진행 중인 생성 (폴링 포함)
        bool _busy;
        string _busyMessage;
        CancellationTokenSource _cts;

        static readonly string[] Plans = { "free", "pro" };

        public ProjectManagerUI(Func<string> tokenProvider, Action repaint)
        {
            _token = tokenProvider;
            _repaint = repaint;
        }

        public void Draw(SupaRunSettings settings)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _expanded = EditorGUILayout.Foldout(_expanded, "Supabase 프로젝트 관리", true);

            if (!_expanded)
            {
                EditorGUILayout.EndVertical();
                return;
            }

            var token = _token();
            if (string.IsNullOrEmpty(token))
            {
                EditorGUILayout.HelpBox(
                    "Access Token 이 필요합니다. 아래 Supabase 카드에서 먼저 입력하세요.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            GUILayout.Space(4);
            using (new EditorGUI.DisabledScope(_busy))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(_loading ? "불러오는 중…" : "목록 새로고침", GUILayout.Height(22)))
                    Refresh(token).Forget();
                if (GUILayout.Button(_showCreate ? "생성 폼 닫기" : "새 프로젝트 만들기", GUILayout.Height(22)))
                {
                    _showCreate = !_showCreate;
                    if (_showCreate && _regions == null) LoadRegions(token).Forget();
                }
                EditorGUILayout.EndHorizontal();
            }

            if (_busy)
            {
                GUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(_busyMessage ?? "진행 중…", EditorStyles.miniLabel);
                if (GUILayout.Button("취소", GUILayout.Width(50)))
                    _cts?.Cancel();
                EditorGUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(_listError))
                EditorGUILayout.HelpBox(_listError, MessageType.Error);

            if (_showCreate) DrawCreateForm(settings, token);

            GUILayout.Space(6);
            DrawList(settings, token);

            EditorGUILayout.EndVertical();
        }

        // ── 목록 ──

        void DrawList(SupaRunSettings settings, string token)
        {
            if (_projects == null)
            {
                EditorGUILayout.LabelField("목록을 불러오지 않았습니다.", EditorStyles.miniLabel);
                return;
            }
            if (_projects.Length == 0)
            {
                EditorGUILayout.LabelField("프로젝트가 없습니다.", EditorStyles.miniLabel);
                return;
            }

            foreach (var p in _projects)
            {
                // 이 프로젝트를 쓰는 환경들 — 삭제 직전에 이게 보여야 사고를 막는다.
                var users = new List<string>();
                foreach (var e in settings.Environments)
                    if (SupaRunSettings.ProjectIdOf(e.supabaseUrl) == p.id)
                        users.Add(e.name);

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{StatusIcon(p)} {p.name}", EditorStyles.boldLabel, GUILayout.Width(200));
                EditorGUILayout.LabelField(p.region, EditorStyles.miniLabel, GUILayout.Width(120));
                EditorGUILayout.LabelField(p.status, EditorStyles.miniLabel, GUILayout.Width(120));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(
                    users.Count > 0 ? $"환경: {string.Join(", ", users)}" : "연결된 환경 없음",
                    EditorStyles.miniLabel);

                using (new EditorGUI.DisabledScope(_busy))
                {
                    EditorGUILayout.BeginHorizontal();

                    if (p.IsInactive && GUILayout.Button("복구", GUILayout.Width(70)))
                        Restore(p, token).Forget();

                    if (users.Count == 0 && p.IsHealthy && GUILayout.Button("환경으로 등록", GUILayout.Width(110)))
                        AttachAsEnvironment(settings, p, token).Forget();

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("삭제", GUILayout.Width(60)))
                        ConfirmDelete(settings, p, token, users);

                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
            }
        }

        static string StatusIcon(SupabaseManagementApi.ProjectInfo p) =>
            p.IsHealthy ? "●" : p.IsInactive ? "○" : "◐";

        // ── 생성 ──

        void DrawCreateForm(SupaRunSettings settings, string token)
        {
            GUILayout.Space(6);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("새 프로젝트", EditorStyles.miniBoldLabel);

            _newName = EditorGUILayout.TextField("이름", _newName);

            if (_loadingRegions)
                EditorGUILayout.LabelField("리전 목록 불러오는 중…", EditorStyles.miniLabel);
            else if (_regions is { Length: > 0 })
            {
                var labels = new string[_regions.Length];
                for (int i = 0; i < _regions.Length; i++) labels[i] = _regions[i].Label;
                if (_regionIdx < 0) _regionIdx = 0;
                _regionIdx = EditorGUILayout.Popup("리전", _regionIdx, labels);
            }
            else
                EditorGUILayout.LabelField("리전 목록을 불러오지 못했습니다 — 기본 리전으로 만듭니다.",
                    EditorStyles.miniLabel);

            var planIdx = Math.Max(0, Array.IndexOf(Plans, _newPlan));
            _newPlan = Plans[EditorGUILayout.Popup("플랜", planIdx, Plans)];

            EditorGUILayout.HelpBox(
                "리전은 **생성할 때만** 정할 수 있습니다. 나중에 옮기려면 새 프로젝트를 만들어 승격해야 합니다.\n" +
                "DB 비밀번호는 자동 생성되어 환경 설정에 저장됩니다.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(_busy || string.IsNullOrWhiteSpace(_newName)))
            {
                if (GUILayout.Button("만들고 환경으로 등록", GUILayout.Height(24)))
                {
                    var region = _regions is { Length: > 0 } && _regionIdx >= 0 && _regionIdx < _regions.Length
                        ? _regions[_regionIdx].code : null;
                    CreateFlow(settings, token, _newName.Trim(), region, _newPlan).Forget();
                }
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 생성 → 준비 대기 → anon key → 환경 등록.
        ///
        /// 중간에 취소해도 **프로젝트는 남는다**. 목록에서 '환경으로 등록' 으로 이어받을 수 있게
        /// 한 이유가 이것이다 — 2분짜리 대기가 유일한 연결 통로면 놓쳤을 때 복구가 안 된다.
        /// </summary>
        async UniTaskVoid CreateFlow(
            SupaRunSettings settings, string token, string name, string region, string plan)
        {
            _cts = new CancellationTokenSource();
            _busy = true;
            try
            {
                _busyMessage = "프로젝트 생성 요청 중…";
                _repaint();

                var dbPass = GeneratePassword();
                var created = await SupabaseManagementApi.CreateProject(token,
                    new SupabaseManagementApi.CreateProjectRequest
                    {
                        name = name,
                        organizationSlug = await ResolveOrgSlug(token),
                        dbPass = dbPass,
                        region = region,
                        plan = plan,
                    });

                if (!created.ShowErrorDialog("프로젝트 생성")) return;

                var info = created.Value;
                await Refresh(token);

                // 준비될 때까지 대기 — 여기서 취소해도 프로젝트는 이미 만들어져 있다.
                var ready = await WaitUntilHealthy(token, info.id, _cts.Token);
                if (!ready)
                {
                    EditorUtility.DisplayDialog("프로젝트 생성",
                        $"'{name}' 은 만들어졌지만 아직 준비 중입니다.\n" +
                        "목록에서 상태를 확인한 뒤 '환경으로 등록' 을 누르세요.", "확인");
                    return;
                }

                await AttachAsEnvironmentCore(settings, info, token, dbPass);
                _showCreate = false;
                _newName = "";
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[SupaRun] 프로젝트 준비 대기를 취소했습니다. 프로젝트는 남아 있습니다.");
            }
            finally
            {
                _busy = false; _busyMessage = null;
                _cts?.Dispose(); _cts = null;
                _repaint();
            }
        }

        /// <summary>ACTIVE_HEALTHY 가 될 때까지 5초 간격 폴링. 최대 5분.</summary>
        async UniTask<bool> WaitUntilHealthy(string token, string projectRef, CancellationToken ct)
        {
            const int maxTries = 60;
            for (int i = 0; i < maxTries; i++)
            {
                ct.ThrowIfCancellationRequested();
                _busyMessage = $"프로젝트가 준비되기를 기다리는 중… ({i * 5}초)";
                _repaint();

                var r = await SupabaseManagementApi.GetProject(projectRef, token);
                if (r.Ok && r.Value.IsHealthy) return true;

                await EditorDelay(5, ct);
            }
            return false;
        }

        /// <summary>
        /// 에디터에서의 대기. `UniTask.Delay` 를 쓰지 않는 이유는 그것이 PlayerLoop 에 매여 있어
        /// 비플레이 모드에서 돌지 않을 수 있기 때문이다 — 이 패키지의 다른 폴링(ActionsTracker)도
        /// 같은 이유로 `EditorApplication.update` 를 쓴다.
        /// </summary>
        static UniTask EditorDelay(double seconds, CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource();
            var until = EditorApplication.timeSinceStartup + seconds;

            void Tick()
            {
                if (ct.IsCancellationRequested)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetCanceled(ct);
                    return;
                }
                if (EditorApplication.timeSinceStartup < until) return;
                EditorApplication.update -= Tick;
                tcs.TrySetResult();
            }

            EditorApplication.update += Tick;
            return tcs.Task;
        }

        // ── 환경 연결 ──

        async UniTaskVoid AttachAsEnvironment(
            SupaRunSettings settings, SupabaseManagementApi.ProjectInfo p, string token)
        {
            _busy = true;
            try { await AttachAsEnvironmentCore(settings, p, token, null); }
            finally { _busy = false; _repaint(); }
        }

        /// <summary>anon key 를 받아 환경을 만들고 값을 채운다. dbPass 는 방금 만든 경우에만 있다.</summary>
        async UniTask AttachAsEnvironmentCore(
            SupaRunSettings settings, SupabaseManagementApi.ProjectInfo p, string token, string dbPass)
        {
            _busyMessage = "anon key 조회 중…";
            _repaint();

            var key = await SupabaseManagementApi.GetAnonKey(p.id, token);
            if (!key.ShowErrorDialog("anon key 조회")) return;

            // 환경 이름은 프로젝트 이름을 따되, 겹치면 사람이 고르게 한다.
            var envName = Sanitize(p.name);
            if (settings.GetEnvironment(envName) != null)
            {
                var alt = envName + "_2";
                if (!EditorUtility.DisplayDialog("환경 이름 중복",
                    $"환경 '{envName}' 이 이미 있습니다. '{alt}' 로 만들까요?", alt, "취소"))
                    return;
                envName = alt;
            }

            var env = settings.AddEnvironment(envName);
            env.supabaseUrl = p.Url;
            env.supabaseAnonKey = key.Value;
            // 비밀은 파일이 아니라 EditorPrefs 로 간다 — 필드에 직접 넣으면 git 에 올라간다.
            SupaRunSettings.SetAccessTokenOf(env, token);   // PAT 는 계정 단위라 같은 값을 쓴다
            if (!string.IsNullOrEmpty(dbPass)) SupaRunSettings.SetDbPasswordOf(env, dbPass);
            settings.Save();

            EditorUtility.DisplayDialog("환경 등록 완료",
                $"환경 '{envName}' 을 만들었습니다.\n\n" +
                "다음 순서로 이어가세요:\n" +
                "1. Deploy 탭 > 환경 승격 > 스키마 반영\n" +
                "2. 그 환경 어드민에 가입해 관리자 만들기\n" +
                "3. 데이터 승격", "확인");
            _repaint();
        }

        // ── 삭제 ──

        void ConfirmDelete(SupaRunSettings settings, SupabaseManagementApi.ProjectInfo p,
            string token, List<string> users)
        {
            var warn = users.Count > 0
                ? $"\n\n⚠ 이 프로젝트는 환경 [{string.Join(", ", users)}] 이 쓰고 있습니다."
                : "";

            // 스냅샷 복원과 같은 강도의 가드 — 되돌릴 수 없는 동작이다.
            if (!EditorUtility.DisplayDialog("프로젝트 삭제",
                $"'{p.name}' 을 삭제합니다.\n데이터·백업·스냅샷이 함께 사라지며 되돌릴 수 없습니다.{warn}\n\n" +
                "정말 진행하시겠습니까?", "계속", "취소"))
                return;

            var typed = EditorInputDialog.Show("삭제 확인",
                $"확인을 위해 프로젝트 이름 '{p.name}' 을 입력하세요.", "");
            if (typed == null) return;
            if (typed.Trim() != p.name)
            {
                EditorUtility.DisplayDialog("삭제 취소", "이름이 일치하지 않아 취소했습니다.", "확인");
                return;
            }

            Delete(p, token).Forget();
        }

        async UniTaskVoid Delete(SupabaseManagementApi.ProjectInfo p, string token)
        {
            _busy = true; _busyMessage = $"'{p.name}' 삭제 중…"; _repaint();
            try
            {
                var r = await SupabaseManagementApi.DeleteProject(p.id, token);
                if (r.ShowErrorDialog($"'{p.name}' 삭제"))
                    await Refresh(token);
            }
            finally { _busy = false; _busyMessage = null; _repaint(); }
        }

        async UniTaskVoid Restore(SupabaseManagementApi.ProjectInfo p, string token)
        {
            _busy = true; _busyMessage = $"'{p.name}' 복구 중…"; _repaint();
            try
            {
                var r = await SupabaseManagementApi.RestoreProject(p.id, token);
                if (r.ShowErrorDialog($"'{p.name}' 복구"))
                    await Refresh(token);
            }
            finally { _busy = false; _busyMessage = null; _repaint(); }
        }

        // ── 조회 ──

        async UniTask Refresh(string token)
        {
            _loading = true; _listError = null; _repaint();
            try
            {
                var r = await SupabaseManagementApi.ListProjects(token);
                if (r.Ok) _projects = r.Value;
                else _listError = $"{r.Message}\n{r.Hint}";
            }
            finally { _loading = false; _repaint(); }
        }

        async UniTaskVoid LoadRegions(string token)
        {
            _loadingRegions = true; _repaint();
            try
            {
                // 조직 slug 가 필수다 — 없이 부르면 400 이다.
                var slug = await ResolveOrgSlug(token);
                var r = await SupabaseManagementApi.AvailableRegions(token, slug);
                if (r.Ok) _regions = r.Value;
                else r.LogIfFailed("리전 목록 조회");
            }
            finally { _loadingRegions = false; _repaint(); }
        }

        /// <summary>조직이 하나면 그것을 쓰고, 여럿이면 첫 번째를 쓴다.</summary>
        async UniTask<string> ResolveOrgSlug(string token)
        {
            var r = await SupabaseManagementApi.ListOrganizations(token);
            if (!r.Ok || r.Value.Length == 0) return null;
            return r.Value[0].slug;
        }

        // ── 유틸 ──

        /// <summary>DB 비밀번호. 사람이 외울 값이 아니므로 길고 무작위로 만든다.</summary>
        static string GeneratePassword()
        {
            const string chars = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var sb = new System.Text.StringBuilder(bytes.Length);
            foreach (var b in bytes) sb.Append(chars[b % chars.Length]);
            return sb.ToString();
        }

        /// <summary>환경 이름으로 쓸 수 있게 다듬는다.</summary>
        static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "env";
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var c in name.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString().Trim('_');
        }
    }
}
