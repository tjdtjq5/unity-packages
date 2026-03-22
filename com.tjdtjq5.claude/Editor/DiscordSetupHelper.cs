using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Tjdtjq5.Claude
{
    /// <summary>
    /// Discord 설정 위자드 헬퍼.
    /// Bot Token에서 Client ID 추출, 초대 URL 생성,
    /// 채널/멤버 목록 조회, 테스트 메시지 전송.
    /// </summary>
    public static class DiscordSetupHelper
    {
        public struct ChannelInfo
        {
            public string Id;
            public string Name;
            public string ServerName;
        }

        // ── Client ID 추출 ──

        /// <summary>Bot Token에서 Client ID를 추출한다 (Base64 디코딩)</summary>
        public static string ExtractClientId(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;

            var firstDot = token.IndexOf('.');
            if (firstDot < 0) return null;

            try
            {
                var base64 = token.Substring(0, firstDot);
                // Base64 패딩 보정
                var padded = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
                var bytes = Convert.FromBase64String(padded);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }

        // ── URL 생성 ──

        /// <summary>Discord 개발자 포털 열기</summary>
        public static void OpenDeveloperPortal()
        {
            Application.OpenURL("https://discord.com/developers/applications");
        }

        /// <summary>봇 초대 URL 생성 + 브라우저 열기</summary>
        public static bool OpenInviteUrl(string token)
        {
            var clientId = ExtractClientId(token);
            if (string.IsNullOrEmpty(clientId))
            {
                Debug.LogWarning("[Discord] Bot Token에서 Client ID를 추출할 수 없습니다");
                return false;
            }

            // 필요 권한: Send Messages + Read Message History + View Channels + Manage Messages
            var permissions = 2048 | 1024 | 65536 | 32768;
            var url = $"https://discord.com/oauth2/authorize?client_id={clientId}&permissions={permissions}&scope=bot";
            Application.OpenURL(url);
            return true;
        }

        // ── 채널 목록 조회 ──

        /// <summary>봇이 접근 가능한 텍스트 채널 목록 조회 (비동기)</summary>
        public static void FetchChannels(string token, Action<List<ChannelInfo>> onSuccess, Action<string> onError)
        {
            var script =
                "import('discord.js').then(({Client,GatewayIntentBits})=>{" +
                "const c=new Client({intents:[GatewayIntentBits.Guilds]});" +
                "c.on('ready',()=>{" +
                "const r=c.channels.cache" +
                ".filter(ch=>ch.type===0)" +
                ".map(ch=>ch.id+'|'+ch.name+'|'+ch.guild.name)" +
                ".join('\\n');" +
                "console.log(r);c.destroy();" +
                "});" +
                "c.login(process.env.DISCORD_TOKEN);" +
                "});";

            RunNodeScript(script, output =>
            {
                var list = new List<ChannelInfo>();
                foreach (var line in output.Split('\n'))
                {
                    var parts = line.Trim().Split('|');
                    if (parts.Length < 3) continue;
                    list.Add(new ChannelInfo
                    {
                        Id = parts[0],
                        Name = parts[1],
                        ServerName = parts[2]
                    });
                }
                onSuccess?.Invoke(list);
            }, onError, token: token);
        }

        // ── 토큰 검증 ──

        /// <summary>
        /// 토큰으로 로그인 + Intent 활성화 여부까지 검증 (비동기).
        /// 성공 시 봇 이름을 반환.
        /// </summary>
        public static void ValidateToken(string token,
            Action<string> onSuccess, Action<string> onError)
        {
            var script =
                "import('discord.js').then(({Client,GatewayIntentBits})=>{" +
                "const c=new Client({intents:[GatewayIntentBits.Guilds,GatewayIntentBits.GuildMessages,GatewayIntentBits.MessageContent]});" +
                "c.on('ready',()=>{" +
                "console.log('OK|'+c.user.tag);" +
                "c.destroy();" +
                "});" +
                "c.login(process.env.DISCORD_TOKEN).catch(e=>{" +
                "console.error(e.message);process.exit(1);" +
                "});" +
                "});";

            RunNodeScript(script, output =>
            {
                var trimmed = output.Trim();
                if (trimmed.StartsWith("OK|"))
                {
                    var botName = trimmed.Substring(3);
                    onSuccess?.Invoke(botName);
                }
                else
                {
                    onError?.Invoke(trimmed);
                }
            }, onError, token: token);
        }

        // ── 테스트 메시지 ──

        /// <summary>테스트 메시지 전송 (비동기)</summary>
        public static void SendTestMessage(string token, string channelId,
            Action onSuccess, Action<string> onError)
        {
            var script =
                "import('discord.js').then(({Client,GatewayIntentBits})=>{" +
                "const c=new Client({intents:[GatewayIntentBits.Guilds]});" +
                "c.on('ready',async()=>{" +
                "const ch=await c.channels.fetch(process.env.DISCORD_CHANNEL);" +
                "await ch.send('Unity 연결 테스트 - Claude Code 패키지에서 전송됨');" +
                "console.log('OK');c.destroy();" +
                "});" +
                "c.login(process.env.DISCORD_TOKEN);" +
                "});";

            RunNodeScript(script, output =>
            {
                if (output.Trim().Contains("OK"))
                    onSuccess?.Invoke();
                else
                    onError?.Invoke(output);
            }, onError, token: token, channelId: channelId);
        }

        // ── 내부 헬퍼 ──

        static string EscapeForJs(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("'", "\\'")
                    .Replace("\n", "\\n").Replace("\r", "\\r");
        }

        /// <summary>Node.js require가 Bridge~/node_modules를 찾을 수 있는 경로에서 실행</summary>
        static string GetNodeWorkDir()
        {
            var candidates = new[]
            {
                Path.GetFullPath(Path.Combine("Packages", "com.tjdtjq5.claude", "Bridge~")),
                Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(Application.dataPath)!, "Packages", "com.tjdtjq5.claude", "Bridge~")),
            };

            foreach (var c in candidates)
                if (Directory.Exists(Path.Combine(c, "node_modules")))
                    return c;

            return null;
        }

        /// <summary>
        /// Node.js 스크립트 실행. 토큰은 환경변수(DISCORD_TOKEN)로 전달하여 명령줄 노출 방지.
        /// </summary>
        static void RunNodeScript(string script, Action<string> onSuccess, Action<string> onError,
            string token = null, string channelId = null)
        {
            var workDir = GetNodeWorkDir();
            if (workDir == null)
            {
                EditorApplication.delayCall += () =>
                    onError?.Invoke("Bridge~/node_modules가 없습니다. npm install을 실행하세요.");
                return;
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                Process p = null;
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "node",
                        Arguments = $"-e \"{script.Replace("\"", "\\\"")}\"",
                        WorkingDirectory = workDir,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    // [W1] 토큰/채널ID는 환경변수로 전달 (명령줄 노출 방지)
                    if (!string.IsNullOrEmpty(token))
                        psi.EnvironmentVariables["DISCORD_TOKEN"] = token;
                    if (!string.IsNullOrEmpty(channelId))
                        psi.EnvironmentVariables["DISCORD_CHANNEL"] = channelId;

                    p = Process.Start(psi);
                    var stdout = p!.StandardOutput.ReadToEnd();
                    var stderr = p.StandardError.ReadToEnd();
                    bool exited = p.WaitForExit(15000);

                    // [W2] 타임아웃 시 프로세스 kill
                    if (!exited)
                    {
                        try { p.Kill(); } catch { /* ignore */ }
                        EditorApplication.delayCall += () => onError?.Invoke("타임아웃: Discord 응답 없음 (15초)");
                        return;
                    }

                    if (p.ExitCode == 0)
                    {
                        // 빈 결과도 성공 (채널에 메시지 없는 경우 등)
                        EditorApplication.delayCall += () => onSuccess?.Invoke(stdout ?? "");
                    }
                    else
                    {
                        var error = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
                        EditorApplication.delayCall += () => onError?.Invoke(error.Trim());
                    }
                }
                catch (Exception ex)
                {
                    EditorApplication.delayCall += () => onError?.Invoke(ex.Message);
                }
                finally
                {
                    p?.Dispose();
                }
            });
        }
    }
}
