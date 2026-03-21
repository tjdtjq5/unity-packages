#if UNITY_EDITOR
using System.Collections.Generic;
using Tjdtjq5.EditorToolkit.Editor;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.CICD.Editor
{
    /// <summary>Step 5: GitHub Secrets 자동 등록</summary>
    public class SecretsGuideStep : IWizardStep
    {
        public string StepLabel => "Secrets";
        public bool IsCompleted => _registeredSecrets != null && _registeredCount >= _requiredCount && _requiredCount > 0;
        public bool IsRequired => true;

        // ── 상태 ──
        HashSet<string> _registeredSecrets;
        bool _loading;
        bool _registering;
        string _registerResult;
        int _registeredCount;
        int _requiredCount;
        int _manualCount;

        public void OnDraw()
        {
            var settings = BuildAutomationSettings.Instance;
            var secrets = SecretsChecklist.GetRequired(settings);
            _requiredCount = secrets.Count;

            EditorUI.DrawSubLabel("Step 5/6: GitHub Secrets 등록");
            EditorUI.DrawDescription(
                "CI/CD에 필요한 인증 정보를 GitHub에 등록합니다.");

            GUILayout.Space(8);

            // ── 자동 등록 버튼 ──
            int autoCount = 0;
            _manualCount = 0;
            foreach (var s in secrets)
            {
                bool registered = _registeredSecrets != null && _registeredSecrets.Contains(s.Name);
                if (!registered && !string.IsNullOrEmpty(s.AutoValue)) autoCount++;
                if (!registered && string.IsNullOrEmpty(s.AutoValue)) _manualCount++;
            }

            EditorUI.BeginBody();

            if (_registering)
            {
                EditorUI.DrawLoading(true, "Secret 등록 중...");
            }
            else if (autoCount > 0)
            {
                if (EditorUI.DrawColorButton($"자동 등록 ({autoCount}개)", EditorUI.COL_SUCCESS, 32))
                    RegisterAllSecrets(secrets);
            }
            else if (_registeredSecrets != null && _registeredCount >= _requiredCount)
            {
                EditorUI.DrawCellLabel("  ✓ 모든 Secret이 등록되었습니다!", 0, EditorUI.COL_SUCCESS);
            }
            else if (_registeredSecrets != null)
            {
                EditorUI.DrawCellLabel($"  {_registeredCount}/{_requiredCount} 등록됨", 0, EditorUI.COL_WARN);
            }

            // 등록 결과
            if (!string.IsNullOrEmpty(_registerResult))
            {
                GUILayout.Space(4);
                var color = _registerResult.Contains("실패") ? EditorUI.COL_ERROR : EditorUI.COL_SUCCESS;
                EditorUI.DrawDescription($"  {_registerResult}", color);
            }

            EditorUI.EndBody();

            GUILayout.Space(8);

            // ── Secret 상태 목록 ──
            _registeredCount = 0;
            string currentCategory = "";

            foreach (var secret in secrets)
            {
                string category = GetCategory(secret.Name);
                if (category != currentCategory)
                {
                    currentCategory = category;
                    EditorUI.DrawSectionHeader(category, BuildAutomationWindow.COL_PRIMARY);
                }

                bool isRegistered = _registeredSecrets != null && _registeredSecrets.Contains(secret.Name);
                if (isRegistered) _registeredCount++;

                EditorUI.BeginBody();
                EditorUI.BeginRow();

                // 상태 아이콘
                if (_registeredSecrets != null)
                {
                    var icon = isRegistered ? "✓" : "✗";
                    var color = isRegistered ? EditorUI.COL_SUCCESS : EditorUI.COL_ERROR;
                    EditorUI.DrawCellLabel($" {icon}", 20, color);
                }
                else
                {
                    EditorUI.DrawCellLabel(" ○", 20, EditorUI.COL_MUTED);
                }

                var nameColor = isRegistered ? EditorUI.COL_SUCCESS : EditorUI.COL_INFO;
                EditorUI.DrawCellLabel(secret.Name, 0, nameColor);
                EditorUI.EndRow();

                // 미등록 + 자동 등록 불가 → 수동 안내
                if (!isRegistered && string.IsNullOrEmpty(secret.AutoValue))
                {
                    EditorUI.DrawDescription($"  {secret.Description}", EditorUI.COL_MUTED);
                    if (!string.IsNullOrEmpty(secret.HowToGet))
                        EditorUI.DrawDescription($"  → {secret.HowToGet}", EditorUI.COL_WARN);
                    if (EditorUI.DrawLinkButton("GitHub Secrets 페이지에서 등록"))
                        Application.OpenURL(GitHelper.GetSecretsPageUrl());
                }

                EditorUI.EndBody();
            }

            if (secrets.Count == 0)
            {
                EditorUI.BeginBody();
                EditorUI.DrawDescription("등록할 Secret이 없습니다.", EditorUI.COL_MUTED);
                EditorUI.EndBody();
            }

            // 최초 진입 시 자동 확인
            if (_registeredSecrets == null && !_loading)
                RefreshSecretStatus();
        }

        // ── 자동 등록 ──

        void RegisterAllSecrets(List<SecretEntry> secrets)
        {
            if (_registering) return;

            var repo = GitHelper.GetGitHubRepo();
            if (string.IsNullOrEmpty(repo)) return;

            var toRegister = new List<SecretEntry>();
            foreach (var s in secrets)
            {
                bool registered = _registeredSecrets != null && _registeredSecrets.Contains(s.Name);
                if (!registered && !string.IsNullOrEmpty(s.AutoValue))
                    toRegister.Add(s);
            }

            if (toRegister.Count == 0) return;

            _registering = true;
            _registerResult = null;

            System.Threading.Tasks.Task.Run(() =>
            {
                int success = 0, fail = 0;
                string lastError = null;

                foreach (var secret in toRegister)
                {
                    var result = RunGhSecretSet(repo, secret.Name, secret.AutoValue);
                    if (result.success) success++;
                    else { fail++; lastError = result.error; }
                }

                EditorApplication.delayCall += () =>
                {
                    _registering = false;
                    _registerResult = fail == 0
                        ? $"✓ {success}개 Secret 등록 완료!"
                        : $"✓ {success}개 성공, ✗ {fail}개 실패: {lastError}";
                    RefreshSecretStatus();
                };
            });
        }

        static (bool success, string error) RunGhSecretSet(string repo, string name, string value)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "gh",
                    Arguments = $"secret set {name} --repo {repo}",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null) return (false, "프로세스 시작 실패");
                p.StandardInput.Write(value);
                p.StandardInput.Close();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return p.ExitCode == 0 ? (true, null) : (false, stderr.Trim());
            }
            catch (System.Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // ── 상태 확인 ──

        void RefreshSecretStatus()
        {
            if (_loading) return;

            var gh = GhChecker.Check();
            if (!gh.LoggedIn)
            {
                _registeredSecrets = new HashSet<string>();
                return;
            }

            _loading = true;
            var repo = GitHelper.GetGitHubRepo();
            if (string.IsNullOrEmpty(repo))
            {
                _registeredSecrets = new HashSet<string>();
                _loading = false;
                return;
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                var (code, output) = GhChecker.RunGh($"secret list --repo {repo}");
                var result = new HashSet<string>();

                if (code == 0 && !string.IsNullOrEmpty(output))
                {
                    foreach (var line in output.Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        var name = trimmed.Split('\t', ' ')[0].Trim();
                        if (!string.IsNullOrEmpty(name))
                            result.Add(name);
                    }
                }

                EditorApplication.delayCall += () =>
                {
                    _registeredSecrets = result;
                    _loading = false;
                };
            });
        }

        static string GetCategory(string secretName)
        {
            if (secretName.StartsWith("UNITY_")) return "Unity 라이선스";
            if (secretName.StartsWith("ANDROID_")) return "Android";
            if (secretName.StartsWith("GOOGLE_PLAY")) return "Google Play";
            if (secretName.StartsWith("APP_STORE")) return "App Store";
            if (secretName.StartsWith("STEAM_")) return "Steam";
            if (secretName.Contains("WEBHOOK")) return "알림 (웹훅)";
            return "기타";
        }
    }
}
#endif
