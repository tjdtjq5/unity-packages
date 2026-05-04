#if UNITY_EDITOR
using System;
using System.IO;
using Tjdtjq5.Codemagic.Editor.Settings;
using Tjdtjq5.Codemagic.Editor.Util;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Setup.Steps
{
    /// <summary>Step 5/6 — Android keystore (선택). 기존 keystore 등록 또는 keytool 신규 생성 + Codemagic GUI walk-through.</summary>
    /// <remarks>
    /// Step 4와 동일하게 REST API 미공개 → GUI 가이드. 그룹 이름 "android_keystore" / 변수 4개.
    /// </remarks>
    public sealed class Step5KeystoreStep : ISetupStep
    {
        const string GroupName = "android_keystore";
        const int VariableCount = 4;

        public string Title => "서명";
        public bool IsCompleted => true;     // 선택 step.
        public bool IsRequired => false;

        // 평문 시크릿은 instance 필드에서만 보관.
        string _keystorePath = "";
        string _alias = "";
        string _keystorePass = "";
        string _keyPass = "";

        // walk-through 진행 상태.
        readonly bool[] _copied = new bool[VariableCount];
        int _currentIdx;
        bool _registered;

        readonly VariableEntry[] _entries = new VariableEntry[VariableCount];

        public void OnEnter(SetupContext ctx)
        {
            _keystorePath = ctx.State.KeystorePath ?? "";
            _alias = ctx.Settings.KeyAlias ?? "";
            _keystorePass = SecretStore.KeystorePassword;
            _keyPass = SecretStore.KeyPassword;
            _registered = ctx.State.KeystoreEnvRegistered;
            _currentIdx = 0;
            for (int i = 0; i < _copied.Length; i++) _copied[i] = false;
        }

        public void OnDraw(SetupContext ctx)
        {
            EditorUI.DrawSubLabel("Step 5/6: Android 서명 (선택)");
            EditorUI.DrawDescription(
                "Android 빌드(AAB/APK)를 서명할 keystore를 등록합니다.\n" +
                "iOS/WebGL만 빌드한다면 [건너뛰기]로 넘어가세요.");

            GUILayout.Space(8);

            DrawKeystoreInputSection(ctx);
            GUILayout.Space(8);
            DrawEnvRegistrationSection(ctx);

            GUILayout.Space(8);
            EditorUI.DrawDescription(
                "이 단계는 선택입니다. Android를 빌드하지 않으면 [건너뛰기]로 다음으로 진행하세요.",
                EditorUI.COL_MUTED);
        }

        public void OnLeave(SetupContext ctx)
        {
            // 영속화 — 시크릿은 SecretStore, 메타는 Settings/State.
            SecretStore.KeystorePassword = _keystorePass ?? "";
            SecretStore.KeyPassword = _keyPass ?? "";

            ctx.Settings.KeyAlias = _alias ?? "";
            ctx.Settings.Save();

            ctx.State.KeystorePath = _keystorePath ?? "";
        }

        // ── Keystore 입력 섹션 ─────────────────────────────────────────────

        void DrawKeystoreInputSection(SetupContext ctx)
        {
            EditorUI.DrawSectionHeader("Keystore 정보", EditorUI.COL_INFO);
            EditorUI.BeginBody();

            EditorUI.BeginRow();
            _keystorePath = EditorUI.DrawTextField("Keystore", _keystorePath);
            if (EditorUI.DrawMiniButton("..."))
            {
                var p = EditorUtility.OpenFilePanel("Keystore 선택", "", "keystore,jks");
                if (!string.IsNullOrEmpty(p)) _keystorePath = p;
            }
            EditorUI.EndRow();

            if (!string.IsNullOrEmpty(_keystorePath))
            {
                if (File.Exists(_keystorePath))
                    EditorUI.DrawCellLabel($"  ✓ 파일 존재: {Path.GetFileName(_keystorePath)}",
                        0, EditorUI.COL_SUCCESS);
                else
                    EditorUI.DrawCellLabel("  ✗ 파일이 존재하지 않습니다.", 0, EditorUI.COL_ERROR);
            }

            _keystorePass = EditorUI.DrawPasswordField("Keystore 비밀번호", _keystorePass);
            if (!string.IsNullOrEmpty(_keystorePass) && _keystorePass.Length < 6)
                EditorUI.DrawDescription("  ⚠ 비밀번호는 6자 이상이어야 합니다.", EditorUI.COL_ERROR);

            _alias = EditorUI.DrawTextField("Key Alias", _alias);

            _keyPass = EditorUI.DrawPasswordField("Key 비밀번호", _keyPass);
            if (!string.IsNullOrEmpty(_keyPass) && _keyPass.Length < 6)
                EditorUI.DrawDescription("  ⚠ 비밀번호는 6자 이상이어야 합니다.", EditorUI.COL_ERROR);

            GUILayout.Space(4);
            if (EditorUI.DrawColorButton("Keystore 생성 (keytool)", EditorUI.COL_INFO))
                CreateKeystore(ctx);

            EditorUI.EndBody();
        }

        // ── 환경 변수 등록 walk-through ────────────────────────────────────

        void DrawEnvRegistrationSection(SetupContext ctx)
        {
            EditorUI.DrawSectionHeader("Codemagic 환경 변수 등록", EditorUI.COL_INFO);
            EditorUI.BeginBody();

            // 사전 조건 검사 — keystore 입력이 다 채워지지 않았으면 walk-through 비활성.
            bool keystoreReady =
                !string.IsNullOrEmpty(_keystorePath) && File.Exists(_keystorePath)
                && !string.IsNullOrEmpty(_keystorePass) && _keystorePass.Length >= 6
                && !string.IsNullOrEmpty(_alias)
                && !string.IsNullOrEmpty(_keyPass) && _keyPass.Length >= 6;

            if (!keystoreReady)
            {
                EditorUI.DrawDescription(
                    "  ⚠ 위 Keystore 정보를 모두 입력하면 등록 단계가 활성화됩니다.\n" +
                    "    (keystore 파일 / 비밀번호 6자+ / alias / key 비밀번호 6자+)",
                    EditorUI.COL_WARN);
                EditorUI.EndBody();
                return;
            }

            DrawGroupCreationStep(ctx);
            GUILayout.Space(8);

            RefreshEntries();
            DrawWalkThroughCards(ctx);

            GUILayout.Space(8);
            DrawCompleteButton(ctx);

            EditorUI.EndBody();
        }

        void DrawGroupCreationStep(SetupContext ctx)
        {
            EditorUI.DrawCellLabel("① 변수 그룹 1회 생성", 0, EditorUI.COL_INFO);
            GUILayout.Space(2);

            EditorUI.BeginRow();
            EditorUI.DrawCellLabel("  그룹 이름:", 100, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel(GroupName, 200, EditorUI.COL_INFO);
            if (EditorUI.DrawMiniButton("📋 복사"))
            {
                EditorGUIUtility.systemCopyBuffer = GroupName;
                ctx.ShowNotification($"그룹 이름 복사됨: {GroupName}", EditorUI.NotificationType.Info);
            }
            EditorUI.EndRow();

            GUILayout.Space(2);

            var appId = ctx.Settings.CodemagicAppId;
            if (!string.IsNullOrEmpty(appId))
            {
                EditorUI.BeginRow();
                if (EditorUI.DrawLinkButton("🔗 Codemagic Settings 페이지 열기"))
                    Application.OpenURL($"https://codemagic.io/app/{appId}/settings");
                EditorUI.EndRow();
            }
            else
            {
                EditorUI.DrawDescription(
                    "  ⚠ Codemagic 앱이 선택되지 않음 — Step 3을 먼저 완료하세요.",
                    EditorUI.COL_WARN);
            }

            EditorUI.DrawDescription(
                "  Settings → Environment variables 탭 → [+ Add variable group] → 이름 paste → Save",
                EditorUI.COL_MUTED);
        }

        void DrawWalkThroughCards(SetupContext ctx)
        {
            EditorUI.DrawCellLabel("② 변수 4개 등록  (Secure ✓ 체크 잊지 마세요)", 0, EditorUI.COL_INFO);
            GUILayout.Space(2);

            for (int i = 0; i < _entries.Length; i++)
                DrawVariableCard(ctx, i, _entries[i]);
        }

        void DrawVariableCard(SetupContext ctx, int idx, VariableEntry entry)
        {
            bool isDone = _copied[idx];
            bool isActive = idx == _currentIdx && !isDone;

            var color = isDone ? EditorUI.COL_SUCCESS
                       : isActive ? EditorUI.COL_INFO
                       : EditorUI.COL_MUTED;
            var marker = isDone ? "✓" : (isActive ? "●" : "○");

            EditorUI.BeginSubBox();

            EditorUI.DrawCellLabel(
                $"  {marker} Variable {idx + 1}/{VariableCount} — {entry.Key}",
                0, color);

            GUILayout.Space(2);

            // Key
            EditorUI.BeginRow();
            EditorUI.DrawCellLabel("    Key:", 70, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel(entry.Key, 200, EditorUI.COL_INFO);
            if (EditorUI.DrawMiniButton("📋 키"))
            {
                EditorGUIUtility.systemCopyBuffer = entry.Key;
                ctx.ShowNotification($"Key 복사됨: {entry.Key}", EditorUI.NotificationType.Info);
            }
            EditorUI.EndRow();

            // Value (마스킹 표시)
            EditorUI.BeginRow();
            EditorUI.DrawCellLabel("    Value:", 70, EditorUI.COL_MUTED);
            EditorUI.DrawCellLabel(MaskValue(entry), 220, EditorUI.COL_MUTED);
            if (EditorUI.DrawMiniButton("📋 값"))
            {
                EditorGUIUtility.systemCopyBuffer = entry.Value ?? "";
                _copied[idx] = true;
                if (idx + 1 > _currentIdx) _currentIdx = idx + 1;
                ctx.ShowNotification(
                    $"{entry.Key} 값 복사됨 ({(entry.Value?.Length ?? 0)}자)",
                    EditorUI.NotificationType.Info);
            }
            EditorUI.EndRow();

            EditorUI.DrawDescription("    Secure: ✓ 체크 필수", EditorUI.COL_WARN);

            EditorUI.EndSubBox();
            GUILayout.Space(4);
        }

        void DrawCompleteButton(SetupContext ctx)
        {
            EditorUI.DrawCellLabel("③ 등록 완료 표시", 0, EditorUI.COL_INFO);
            GUILayout.Space(2);

            if (_registered)
            {
                EditorUI.DrawCellLabel(
                    "  ✓ Codemagic keystore env 등록 완료로 표시됨.", 0, EditorUI.COL_SUCCESS);
                GUILayout.Space(2);
                if (EditorUI.DrawColorButton("재등록 (처음부터)", EditorUI.COL_MUTED, 24))
                {
                    _registered = false;
                    ctx.State.KeystoreEnvRegistered = false;
                    ctx.SaveState();
                    for (int i = 0; i < _copied.Length; i++) _copied[i] = false;
                    _currentIdx = 0;
                    ctx.ShowNotification("Codemagic keystore env 등록 상태 리셋됨.",
                        EditorUI.NotificationType.Info);
                }
                return;
            }

            bool allCopied = true;
            for (int i = 0; i < _copied.Length; i++)
                if (!_copied[i]) { allCopied = false; break; }

            EditorUI.BeginDisabled(!allCopied);
            if (EditorUI.DrawColorButton(
                $"✓ Codemagic에 {VariableCount}개 변수 등록 완료",
                allCopied ? EditorUI.COL_SUCCESS : EditorUI.COL_MUTED,
                32))
            {
                _registered = true;
                ctx.State.KeystoreEnvRegistered = true;
                ctx.SaveState();
                ctx.ShowNotification(
                    $"Codemagic keystore env 등록 완료로 표시됨.",
                    EditorUI.NotificationType.Success);
            }
            EditorUI.EndDisabled();

            if (!allCopied)
                EditorUI.DrawDescription(
                    $"  {VariableCount}개 변수의 [값 복사] 버튼을 모두 눌러야 활성화됩니다.",
                    EditorUI.COL_MUTED);
        }

        // ── Keystore 생성 (keytool) ────────────────────────────────────────

        void CreateKeystore(SetupContext ctx)
        {
            // 입력 검증.
            if (string.IsNullOrEmpty(_keystorePass) || _keystorePass.Length < 6)
            {
                EditorUtility.DisplayDialog("Keystore 생성",
                    "Keystore 비밀번호를 6자 이상 입력하세요.", "확인");
                return;
            }
            if (string.IsNullOrEmpty(_keyPass) || _keyPass.Length < 6)
            {
                EditorUtility.DisplayDialog("Keystore 생성",
                    "Key 비밀번호를 6자 이상 입력하세요.", "확인");
                return;
            }
            if (string.IsNullOrEmpty(_alias))
            {
                EditorUtility.DisplayDialog("Keystore 생성",
                    "Key Alias를 입력하세요.", "확인");
                return;
            }

            // keytool 호출.
            var (ok, savePath, error) = KeystoreCreator.Create(_alias, _keystorePass, _keyPass);

            if (ok && !string.IsNullOrEmpty(savePath))
            {
                _keystorePath = savePath;
                ctx.State.KeystorePath = savePath;
                ctx.Settings.KeyAlias = _alias;
                ctx.Settings.Save();
                ctx.ShowNotification($"Keystore 생성 완료: {Path.GetFileName(savePath)}",
                    EditorUI.NotificationType.Success);
                EditorUtility.DisplayDialog("Keystore 생성",
                    $"Keystore가 생성되었습니다!\n{savePath}\n\n" +
                    ".gitignore에 *.keystore 가 추가되었습니다.", "확인");
            }
            else if (savePath == null && error == null)
            {
                ctx.ShowNotification("Keystore 생성 취소됨.", EditorUI.NotificationType.Info);
            }
            else
            {
                ctx.ShowNotification($"Keystore 생성 실패: {error ?? "알 수 없는 오류"}",
                    EditorUI.NotificationType.Error);
            }
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
            _entries[0] = new VariableEntry("KEYSTORE_BASE64",   KeystoreHelper.ToBase64(_keystorePath) ?? "");
            _entries[1] = new VariableEntry("KEYSTORE_PASSWORD", _keystorePass ?? "");
            _entries[2] = new VariableEntry("KEY_ALIAS",         _alias ?? "");
            _entries[3] = new VariableEntry("KEY_PASSWORD",      _keyPass ?? "");
        }

        static string MaskValue(VariableEntry e)
        {
            var v = e.Value ?? "";
            if (e.Key == "KEYSTORE_BASE64")
                return v.Length == 0 ? "(미생성)" : $"········· ({v.Length} bytes)";
            if (e.Key == "KEYSTORE_PASSWORD" || e.Key == "KEY_PASSWORD")
                return v.Length == 0 ? "(미입력)" : new string('•', Math.Min(v.Length, 12));
            // KEY_ALIAS는 평문.
            return v.Length == 0 ? "(미입력)" : v;
        }
    }
}
#endif
