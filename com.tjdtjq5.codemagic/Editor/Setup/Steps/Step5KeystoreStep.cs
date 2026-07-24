#if UNITY_EDITOR
using System;
using System.IO;
using Tjdtjq5.Codemagic.Editor.Settings;
using Tjdtjq5.Codemagic.Editor.Util;
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
            EditorGUILayout.LabelField("Step 5/6: Android 서명 (선택)", EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                "Android 빌드(AAB/APK)를 서명할 keystore를 등록합니다.\n" +
                "iOS/WebGL만 빌드한다면 [건너뛰기]로 넘어가세요.",
                EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(8);

            DrawKeystoreInputSection(ctx);
            GUILayout.Space(8);
            DrawEnvRegistrationSection(ctx);

            GUILayout.Space(8);
            EditorGUILayout.LabelField(
                "이 단계는 선택입니다. Android를 빌드하지 않으면 [건너뛰기]로 다음으로 진행하세요.",
                EditorStyles.wordWrappedMiniLabel);
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
            EditorGUILayout.LabelField("Keystore 정보", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            _keystorePath = EditorGUILayout.TextField("Keystore", _keystorePath);
            if (GUILayout.Button("...", EditorStyles.miniButton))
            {
                var p = EditorUtility.OpenFilePanel("Keystore 선택", "", "keystore,jks");
                if (!string.IsNullOrEmpty(p)) _keystorePath = p;
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_keystorePath))
            {
                if (File.Exists(_keystorePath))
                    EditorGUILayout.LabelField($"  ✓ 파일 존재: {Path.GetFileName(_keystorePath)}");
                else
                    EditorGUILayout.LabelField("  ✗ 파일이 존재하지 않습니다.");
            }

            _keystorePass = EditorGUILayout.PasswordField("Keystore 비밀번호", _keystorePass);
            if (!string.IsNullOrEmpty(_keystorePass) && _keystorePass.Length < 6)
                EditorGUILayout.LabelField("  ⚠ 비밀번호는 6자 이상이어야 합니다.",
                    EditorStyles.wordWrappedMiniLabel);

            _alias = EditorGUILayout.TextField("Key Alias", _alias);

            _keyPass = EditorGUILayout.PasswordField("Key 비밀번호", _keyPass);
            if (!string.IsNullOrEmpty(_keyPass) && _keyPass.Length < 6)
                EditorGUILayout.LabelField("  ⚠ 비밀번호는 6자 이상이어야 합니다.",
                    EditorStyles.wordWrappedMiniLabel);

            GUILayout.Space(4);
            if (GUILayout.Button("Keystore 생성 (keytool)"))
                CreateKeystore(ctx);

            EditorGUILayout.EndVertical();
        }

        // ── 환경 변수 등록 walk-through ────────────────────────────────────

        void DrawEnvRegistrationSection(SetupContext ctx)
        {
            EditorGUILayout.LabelField("Codemagic 환경 변수 등록", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 사전 조건 검사 — keystore 입력이 다 채워지지 않았으면 walk-through 비활성.
            bool keystoreReady =
                !string.IsNullOrEmpty(_keystorePath) && File.Exists(_keystorePath)
                && !string.IsNullOrEmpty(_keystorePass) && _keystorePass.Length >= 6
                && !string.IsNullOrEmpty(_alias)
                && !string.IsNullOrEmpty(_keyPass) && _keyPass.Length >= 6;

            if (!keystoreReady)
            {
                EditorGUILayout.LabelField(
                    "  ⚠ 위 Keystore 정보를 모두 입력하면 등록 단계가 활성화됩니다.\n" +
                    "    (keystore 파일 / 비밀번호 6자+ / alias / key 비밀번호 6자+)",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.EndVertical();
                return;
            }

            DrawGroupCreationStep(ctx);
            GUILayout.Space(8);

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
                EditorGUILayout.LabelField("  ✓ Codemagic keystore env 등록 완료로 표시됨.");
                GUILayout.Space(2);
                if (GUILayout.Button("재등록 (처음부터)", GUILayout.Height(24)))
                {
                    _registered = false;
                    ctx.State.KeystoreEnvRegistered = false;
                    ctx.SaveState();
                    for (int i = 0; i < _copied.Length; i++) _copied[i] = false;
                    _currentIdx = 0;
                    ctx.ShowNotification("Codemagic keystore env 등록 상태 리셋됨.",
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
                ctx.State.KeystoreEnvRegistered = true;
                ctx.SaveState();
                ctx.ShowNotification(
                    $"✓ Codemagic keystore env 등록 완료로 표시됨.",
                    MessageType.Info);
            }
            EditorGUI.EndDisabledGroup();

            if (!allCopied)
                EditorGUILayout.LabelField(
                    $"  {VariableCount}개 변수의 [값 복사] 버튼을 모두 눌러야 활성화됩니다.",
                    EditorStyles.wordWrappedMiniLabel);
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
                ctx.ShowNotification($"✓ Keystore 생성 완료: {Path.GetFileName(savePath)}",
                    MessageType.Info);
                EditorUtility.DisplayDialog("Keystore 생성",
                    $"Keystore가 생성되었습니다!\n{savePath}\n\n" +
                    ".gitignore에 *.keystore 가 추가되었습니다.", "확인");
            }
            else if (savePath == null && error == null)
            {
                ctx.ShowNotification("Keystore 생성 취소됨.", MessageType.Info);
            }
            else
            {
                ctx.ShowNotification($"Keystore 생성 실패: {error ?? "알 수 없는 오류"}",
                    MessageType.Error);
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
