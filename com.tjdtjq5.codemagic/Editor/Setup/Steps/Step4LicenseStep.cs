#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;
using Tjdtjq5.Codemagic.Editor.License;
using Tjdtjq5.Codemagic.Editor.Settings;
using Tjdtjq5.Codemagic.Editor.Util;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Setup.Steps
{
    /// <summary>Step 4/6 — Unity 라이선스 (.ulf 자동 탐색 + Codemagic GUI 등록 walk-through).</summary>
    /// <remarks>
    /// Codemagic 공개 REST API에 환경 변수 그룹 관리 endpoint가 없어 GUI 가이드 방식 채택.
    /// 사용자는 3개 변수의 [값 복사] 버튼을 차례로 누른 뒤 [✓ 등록 완료] 셀프 체크.
    ///
    /// 등록 변수: UNITY_LICENSE / UNITY_EMAIL / UNITY_PASSWORD
    /// UNITY_SERIAL은 등록 X — yaml 빌드 step에서 UNITY_LICENSE 값으로부터 inline 추출됨.
    /// </remarks>
    public sealed class Step4LicenseStep : ISetupStep
    {
        const string GroupName = "unity_credentials";
        const int VariableCount = 3;

        public string Title => "라이선스";
        public bool IsCompleted =>
            !string.IsNullOrEmpty(_email) && _email.Contains("@")
            && !string.IsNullOrEmpty(_password)
            && !string.IsNullOrEmpty(_ulfContent)
            && _registered;
        public bool IsRequired => true;

        // 평문 시크릿은 instance 필드에서만 보관, Debug.Log 금지.
        string _email = "";
        string _password = "";
        string _ulfContent;        // .ulf 파일 전체 내용 — Codemagic env에 push할 값.
        string _ulfPath;           // .ulf 파일 경로 (사용자 표시용).
        string _ulfStopDate;       // 만료일 "YYYY-MM-DD".
        string _serialMasked;      // "F4-XXXX-...-YYYY" 형태.

        // walk-through 진행 상태.
        readonly bool[] _copied = new bool[VariableCount];
        int _currentIdx;
        bool _registered;          // 셀프 체크 완료 (LocalUserState.LicenseEnvRegistered 동기화).

        // 매 OnDraw에서 alloc 피하기 위한 entry 캐시.
        readonly VariableEntry[] _entries = new VariableEntry[VariableCount];

        public void OnEnter(SetupContext ctx)
        {
            // 이전 세션 prefill.
            _email = SecretStore.UnityEmail;
            _password = SecretStore.UnityPassword;
            _ulfContent = SecretStore.UlfContent;
            _ulfPath = null;
            _registered = ctx.State.LicenseEnvRegistered;
            _currentIdx = 0;
            for (int i = 0; i < _copied.Length; i++) _copied[i] = false;

            // 자동 탐색 — 기본 경로 시도.
            if (string.IsNullOrEmpty(_ulfContent))
                TryAutoDetect(ctx);
            else
                ParseLicenseMeta(ctx);
        }

        public void OnDraw(SetupContext ctx)
        {
            EditorGUILayout.LabelField("Step 4/6: Unity 라이선스", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "Codemagic 빌드에서 Unity를 활성화하기 위해 필요합니다.\n" +
                "Codemagic GUI에 환경 변수 3개를 등록합니다 (UNITY_SERIAL은 빌드 시 .ulf에서 자동 추출).",
                EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(8);

            DrawUnityAccountSection();
            GUILayout.Space(8);
            DrawUlfSection(ctx);
            GUILayout.Space(8);
            DrawEnvRegistrationSection(ctx);
        }

        public void OnLeave(SetupContext ctx)
        {
            // 입력값을 SecretStore에 영속화 (다음 진입 prefill 용도).
            SecretStore.UnityEmail = _email ?? "";
            SecretStore.UnityPassword = _password ?? "";
            if (!string.IsNullOrEmpty(_ulfContent))
                SecretStore.UlfContent = _ulfContent;
        }

        // ── Unity 계정 섹션 ────────────────────────────────────────────────

        void DrawUnityAccountSection()
        {
            EditorGUILayout.LabelField("Unity 계정", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            _email = EditorGUILayout.TextField("이메일", _email);
            if (!string.IsNullOrEmpty(_email) && !IsValidEmail(_email))
                EditorGUILayout.LabelField("  ⚠ 이메일 형식이 올바르지 않습니다.",
                    EditorStyles.wordWrappedMiniLabel);

            _password = EditorGUILayout.PasswordField("비밀번호", _password);
            if (!string.IsNullOrEmpty(_password))
                EditorGUILayout.LabelField($"  현재 입력 길이: {_password.Length}자",
                    EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.LabelField(
                "Google/Apple로만 로그인하던 계정이라면 id.unity.com 에서 비밀번호를 추가하세요.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }

        // ── .ulf 파일 섹션 ─────────────────────────────────────────────────

        void DrawUlfSection(SetupContext ctx)
        {
            EditorGUILayout.LabelField("라이선스 파일 (.ulf)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (!string.IsNullOrEmpty(_ulfContent))
            {
                if (!string.IsNullOrEmpty(_ulfPath))
                    EditorGUILayout.LabelField($"  ✓ {Path.GetFileName(_ulfPath)} 로드됨");
                else
                    EditorGUILayout.LabelField("  ✓ .ulf 로드됨 (이전 세션 저장)");

                EditorGUILayout.LabelField(
                    $"      길이: {_ulfContent.Length} bytes",
                    EditorStyles.wordWrappedMiniLabel);

                if (!string.IsNullOrEmpty(_ulfStopDate))
                {
                    EditorGUILayout.LabelField(
                        $"      만료일: {_ulfStopDate} {ComputeDayDiff(_ulfStopDate)}");
                }

                if (!string.IsNullOrEmpty(_serialMasked))
                    EditorGUILayout.LabelField($"      Serial: {_serialMasked}");
            }
            else
            {
                EditorGUILayout.LabelField("  ✗ .ulf 파일을 찾지 못했습니다.");
                EditorGUILayout.LabelField(
                    "      Unity Hub → Preferences → Licenses → Add → Get a free personal license\n" +
                    "      를 마친 후 [다시 탐색]을 누르세요.",
                    EditorStyles.wordWrappedMiniLabel);
            }

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("다시 탐색"))
                TryAutoDetect(ctx);
            if (GUILayout.Button(".ulf 직접 선택"))
                SelectUlfManually(ctx);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        // ── 환경 변수 등록 walk-through ────────────────────────────────────

        void DrawEnvRegistrationSection(SetupContext ctx)
        {
            EditorGUILayout.LabelField("Codemagic 환경 변수 등록", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawGroupCreationStep(ctx);
            GUILayout.Space(8);

            // walk-through entries 갱신 (struct 재사용, alloc 0).
            RefreshEntries();
            DrawWalkThroughCards(ctx);

            GUILayout.Space(8);
            DrawCompleteButton(ctx);

            EditorGUILayout.EndVertical();
        }

        void DrawGroupCreationStep(SetupContext ctx)
        {
            EditorGUILayout.LabelField("① 변수 그룹 1회 생성");
            GUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("  그룹 이름:", GUILayout.Width(100));
            EditorGUILayout.LabelField(GroupName, GUILayout.Width(200));
            if (GUILayout.Button("📋 복사", EditorStyles.miniButton))
            {
                EditorGUIUtility.systemCopyBuffer = GroupName;
                ctx.ShowNotification($"그룹 이름 복사됨: {GroupName}", MessageType.Info);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);

            var appId = ctx.Settings.CodemagicAppId;
            if (!string.IsNullOrEmpty(appId))
            {
                EditorGUILayout.BeginHorizontal();
                if (EditorGUILayout.LinkButton("🔗 Codemagic Settings 페이지 열기"))
                    Application.OpenURL($"https://codemagic.io/app/{appId}/settings");
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.LabelField(
                    "  ⚠ Codemagic 앱이 선택되지 않음 — Step 3을 먼저 완료하세요.",
                    EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.LabelField(
                "  Settings → Environment variables 탭 → [+ Add variable group] → 이름 paste → Save",
                EditorStyles.wordWrappedMiniLabel);
        }

        void DrawWalkThroughCards(SetupContext ctx)
        {
            EditorGUILayout.LabelField("② 변수 4개 등록  (Secure ✓ 체크 잊지 마세요)");
            GUILayout.Space(2);

            for (int i = 0; i < _entries.Length; i++)
                DrawVariableCard(ctx, i, _entries[i]);
        }

        void DrawVariableCard(SetupContext ctx, int idx, VariableEntry entry)
        {
            bool isDone = _copied[idx];
            bool isActive = idx == _currentIdx && !isDone;

            var marker = isDone ? "✓" : (isActive ? "●" : "○");

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                $"  {marker} Variable {idx + 1}/{VariableCount} — {entry.Key}");

            GUILayout.Space(2);

            // Key
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("    Key:", GUILayout.Width(70));
            EditorGUILayout.LabelField(entry.Key, GUILayout.Width(200));
            if (GUILayout.Button("📋 키", EditorStyles.miniButton))
            {
                EditorGUIUtility.systemCopyBuffer = entry.Key;
                ctx.ShowNotification($"Key 복사됨: {entry.Key}", MessageType.Info);
            }
            EditorGUILayout.EndHorizontal();

            // Value (마스킹 표시)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("    Value:", GUILayout.Width(70));
            EditorGUILayout.LabelField(MaskValue(entry), GUILayout.Width(220));
            if (GUILayout.Button("📋 값", EditorStyles.miniButton))
            {
                EditorGUIUtility.systemCopyBuffer = entry.Value ?? "";
                _copied[idx] = true;
                if (idx + 1 > _currentIdx) _currentIdx = idx + 1;
                ctx.ShowNotification(
                    $"{entry.Key} 값 복사됨 ({(entry.Value?.Length ?? 0)}자)",
                    MessageType.Info);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("    Secure: ✓ 체크 필수", EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        void DrawCompleteButton(SetupContext ctx)
        {
            EditorGUILayout.LabelField("③ 등록 완료 표시");
            GUILayout.Space(2);

            if (_registered)
            {
                EditorGUILayout.LabelField("  ✓ Codemagic env 등록 완료로 표시됨.");
                GUILayout.Space(2);
                if (GUILayout.Button("재등록 (처음부터)", GUILayout.Height(24)))
                {
                    _registered = false;
                    ctx.State.LicenseEnvRegistered = false;
                    ctx.SaveState();
                    for (int i = 0; i < _copied.Length; i++) _copied[i] = false;
                    _currentIdx = 0;
                    ctx.ShowNotification("Codemagic env 등록 상태 리셋됨.",
                        MessageType.Info);
                }
                return;
            }

            bool allCopied = true;
            for (int i = 0; i < _copied.Length; i++)
                if (!_copied[i]) { allCopied = false; break; }

            EditorGUI.BeginDisabledGroup(!allCopied);
            if (GUILayout.Button(
                $"✓ Codemagic에 {VariableCount}개 변수 등록 완료",
                GUILayout.Height(32)))
            {
                _registered = true;
                ctx.State.LicenseEnvRegistered = true;
                ctx.SaveState();
                ctx.ShowNotification(
                    $"✓ Codemagic env 등록 완료로 표시됨 — 다음 단계로 진행 가능.",
                    MessageType.Info);
            }
            EditorGUI.EndDisabledGroup();

            if (!allCopied)
                EditorGUILayout.LabelField(
                    $"  {VariableCount}개 변수의 [값 복사] 버튼을 모두 눌러야 활성화됩니다.",
                    EditorStyles.wordWrappedMiniLabel);
        }

        // ── 자동 탐색 / 파일 로드 ──────────────────────────────────────────

        void TryAutoDetect(SetupContext ctx)
        {
            var path = PlatformPaths.UnityLicensePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;  // 자동 탐색 실패는 에러로 표시 X.

            var (valid, content, error) = UlfReader.TryRead(path);
            if (!valid)
            {
                ctx.ShowNotification($".ulf 자동 탐색 실패: {error}", MessageType.Error);
                return;
            }

            _ulfPath = path;
            _ulfContent = content;
            ParseLicenseMeta(ctx);
        }

        void SelectUlfManually(SetupContext ctx)
        {
            var initDir = PlatformPaths.UnityLicensePath;
            if (!string.IsNullOrEmpty(initDir))
                initDir = Path.GetDirectoryName(initDir);

            var path = EditorUtility.OpenFilePanel(
                "Unity 라이선스 파일 (.ulf) 선택", initDir ?? "", "ulf");
            if (string.IsNullOrEmpty(path)) return;

            var (valid, content, error) = UlfReader.TryRead(path);
            if (!valid)
            {
                ctx.ShowNotification($".ulf 파일 읽기 실패: {error}", MessageType.Error);
                return;
            }

            _ulfPath = path;
            _ulfContent = content;
            ParseLicenseMeta(ctx);
        }

        void ParseLicenseMeta(SetupContext ctx)
        {
            if (string.IsNullOrEmpty(_ulfContent)) return;

            // 만료일 (XML <StopDate ... Value="YYYY-MM-DDTHH:MM:SS">).
            _ulfStopDate = ExtractStopDate(_ulfContent);
            ctx.State.LicenseStopDate = _ulfStopDate;

            // Serial 추출 + 마스킹.
            var serial = UlfSerialExtractor.Extract(_ulfContent);
            _serialMasked = MaskSerial(serial);
            ctx.State.UnitySerialMasked = _serialMasked;
        }

        // ── walk-through entry 캐시 ────────────────────────────────────────

        readonly struct VariableEntry
        {
            public readonly string Key;
            public readonly string Value;
            public VariableEntry(string key, string value) { Key = key; Value = value; }
        }

        void RefreshEntries()
        {
            // UNITY_SERIAL은 등록 대상 X — yaml 빌드 step의 inline 추출 사용.
            // .ulf 파일 정보 섹션에서 마스킹 표시용으로만 _serialMasked 유지.
            _entries[0] = new VariableEntry("UNITY_LICENSE",  _ulfContent ?? "");
            _entries[1] = new VariableEntry("UNITY_EMAIL",    _email ?? "");
            _entries[2] = new VariableEntry("UNITY_PASSWORD", _password ?? "");
        }

        static string MaskValue(VariableEntry e)
        {
            var v = e.Value ?? "";
            if (e.Key == "UNITY_LICENSE")
                return v.Length == 0 ? "(미로드)" : $"········· ({v.Length} bytes)";
            if (e.Key == "UNITY_PASSWORD")
                return v.Length == 0 ? "(미입력)" : new string('•', Math.Min(v.Length, 12));
            // UNITY_EMAIL은 평문 표시.
            return v.Length == 0 ? "(미입력)" : v;
        }

        // ── 헬퍼 ───────────────────────────────────────────────────────────

        static bool IsValidEmail(string email) =>
            !string.IsNullOrEmpty(email) && email.Contains("@") && email.Contains(".");

        static readonly Regex StopDateRegex =
            new Regex("<StopDate[^/]*Value=\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>StopDate 추출 — "YYYY-MM-DD" (시간 부분 잘라냄). 실패 시 null.</summary>
        static string ExtractStopDate(string ulfContent)
        {
            if (string.IsNullOrEmpty(ulfContent)) return null;
            var m = StopDateRegex.Match(ulfContent);
            if (!m.Success) return null;
            var raw = m.Groups[1].Value;
            // "2026-06-03T00:00:00" → "2026-06-03"
            int t = raw.IndexOf('T');
            return t > 0 ? raw.Substring(0, t) : raw;
        }

        /// <summary>Serial 마스킹 — 앞 2자리 + 뒤 4자리만 노출.</summary>
        static string MaskSerial(string serial)
        {
            if (string.IsNullOrEmpty(serial) || serial.Length < 8) return null;
            var prefix = serial.Substring(0, 2);
            var suffix = serial.Substring(serial.Length - 4);
            return $"{prefix}-XXXX-...-{suffix}";
        }

        static string ComputeDayDiff(string stopDate)
        {
            if (DateTime.TryParse(stopDate, out var d))
            {
                var diff = (int)(d.Date - DateTime.UtcNow.Date).TotalDays;
                if (diff < 0) return $"(만료됨, {-diff}일 경과)";
                if (diff < 14) return $"(D-{diff} ⚠)";
                return $"(D-{diff})";
            }
            return "";
        }
    }
}
#endif
