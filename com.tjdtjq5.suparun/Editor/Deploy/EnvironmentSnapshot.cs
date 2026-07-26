using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 환경 현황을 모아 `suparun_meta.environments` 에 실어 둔다. 어드민이 그걸 읽어 카드로 그린다.
    ///
    /// **왜 DB 를 거치는가**: 이 정보는 전부 Management API + PAT 로만 얻을 수 있는데, PAT 는
    /// 로컬에만 두기로 했다. 그래서 아이콘 맵과 같은 방식을 쓴다 —
    /// **Unity 가 구워서 넣고, 어드민은 읽기만 한다**(ADR-0004 의 SyncAdminAssets 과 같은 패턴).
    ///
    /// 결과적으로 어드민은 Unity 가 꺼져 있어도 **마지막으로 본 상태**를 보여줄 수 있다.
    /// 지금 이 순간의 값이 아니라는 것은 `collected_at` 으로 드러난다.
    /// </summary>
    public static class EnvironmentSnapshot
    {
        /// <summary>
        /// 모든 환경을 훑어 편집 환경 DB 에 기록한다.
        ///
        /// 기록 위치가 **편집 환경**인 이유: 어드민은 자기 환경의 DB 만 읽는다.
        /// dev 어드민을 열면 dev DB 에, prod 어드민을 열면 prod DB 에 같은 내용이 실린다.
        /// </summary>
        public static async UniTask<bool> CollectAndPublishAsync(bool silent = true)
        {
            var settings = SupaRunSettings.Instance;

            // 올리기 전에 **내려받는다** — 어드민에서 정한 이름·역할을 로컬 설정에 반영한다.
            // 방향이 둘인 이유: 지표는 Unity 만 알 수 있고(PAT), 역할은 사람이 정하는 것이다.
            await PullRolesAsync(settings, silent);

            var target = settings.Current;
            var targetId = SupaRunSettings.ProjectIdOf(target.supabaseUrl);
            var targetToken = SupaRunSettings.AccessTokenOf(target);
            if (string.IsNullOrEmpty(targetId) || string.IsNullOrEmpty(targetToken))
            {
                if (!silent) Debug.LogWarning("[SupaRun:Env] 편집 환경에 URL/Access Token 이 없습니다.");
                return false;
            }

            var list = new JArray();
            foreach (var env in settings.Environments)
                list.Add(await CollectOne(env, settings));

            var payload = list.ToString(Formatting.None);

            // 로그인 방식도 함께 싣는다. 어드민이 **잠금 여부를 스스로 판단**해야 하는데,
            // 지금까지는 이 값이 빌드에 구워져 있어 프로바이더를 켜도 재배포 전에는 몰랐다.
            // (`{{AUTH_PROVIDERS_JSON}}` 은 fallback 으로 남는다 — 옛 배포가 깨지지 않게)
            // Guest/GPGS/GameCenter 는 게임 클라이언트용이라 웹에서 누를 수 없다.
            // 그것들까지 세면 "로그인 수단이 있다"고 잘못 판단해 어드민이 들어갈 수 없게 잠긴다.
            var providers = new JArray();
            foreach (var p in settings.enabledAuthProviders)
            {
                var v = p?.Trim();
                if (string.IsNullOrEmpty(v)) continue;
                if (v is "Guest" or "GPGS" or "GameCenter") continue;
                providers.Add(v.ToLowerInvariant());
            }
            var authPayload = new JObject { ["providers"] = providers }.ToString(Formatting.None);

            var sql =
                "INSERT INTO suparun_meta(key, value, updated_at) " +
                $"VALUES ('environments', $envjson${payload}$envjson$::jsonb, now()) " +
                "ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now(); " +
                "INSERT INTO suparun_meta(key, value, updated_at) " +
                $"VALUES ('auth_config', $authjson${authPayload}$authjson$::jsonb, now()) " +
                "ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now();";

            var r = await SupabaseManagementApi.RunQuery(targetId, targetToken, sql);
            if (!r.Ok)
            {
                if (!silent) r.LogIfFailed("환경 현황 기록");
                return false;
            }

            if (!silent) Debug.Log($"[SupaRun:Env] 환경 {settings.Environments.Count}개 현황을 기록했습니다.");
            return true;
        }

        /// <summary>
        /// 어드민이 정한 환경 역할(`suparun_meta.env_roles`)을 로컬 설정에 반영한다.
        ///
        /// **역할만 가져온다** — 이름·편집/빌드 지정뿐이고, 접속 정보(URL·키·PAT)는 건드리지 않는다.
        /// 비밀은 로컬에만 두기로 했고, 어드민은 애초에 그걸 모른다.
        ///
        /// 매칭 기준은 project_ref 다. 어드민이 아는 것은 프로젝트이지 로컬 환경 이름이 아니다.
        /// </summary>
        static async UniTask PullRolesAsync(SupaRunSettings settings, bool silent)
        {
            var cur = settings.Current;
            var id = SupaRunSettings.ProjectIdOf(cur.supabaseUrl);
            var curToken = SupaRunSettings.AccessTokenOf(cur);
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(curToken)) return;

            var r = await SupabaseManagementApi.RunQuery(id, curToken,
                "SELECT value::text AS v FROM suparun_meta WHERE key = 'env_roles';");
            if (!r.Ok) return;   // 아직 정한 적이 없으면 그냥 넘어간다

            JObject roles;
            try
            {
                var rows = JArray.Parse(r.Value);
                if (rows.Count == 0) return;
                var raw = (string)((JObject)rows[0])["v"];
                if (string.IsNullOrWhiteSpace(raw)) return;
                roles = JObject.Parse(raw);
            }
            catch { return; }

            var changed = false;
            foreach (var kv in roles)
            {
                var env = FindByRef(settings, kv.Key);
                if (env == null) continue;   // 로컬에 접속 정보가 없는 프로젝트 — 여기서 만들 수 없다

                var name = (string)kv.Value?["name"];
                if (!string.IsNullOrWhiteSpace(name) && env.name != name)
                {
                    // 이름이 바뀌면 편집/빌드 지정이 옛 이름을 가리킨 채 남는다. 같이 옮긴다.
                    if (settings.EditorEnvironment == env.name) settings.EditorEnvironment = name;
                    if (settings.BuildEnvironment == env.name) settings.BuildEnvironment = name;
                    env.name = name;
                    changed = true;
                }

                if ((bool?)kv.Value?["editor"] == true && settings.EditorEnvironment != env.name)
                { settings.EditorEnvironment = env.name; changed = true; }

                if ((bool?)kv.Value?["build"] == true && settings.BuildEnvironment != env.name)
                { settings.BuildEnvironment = env.name; changed = true; }
            }

            if (!changed) return;
            settings.Save();
            if (!silent)
                Debug.Log($"[SupaRun:Env] 어드민에서 정한 환경 역할을 반영했습니다 " +
                          $"(편집={settings.EditorEnvironment}, 빌드={settings.BuildEnvironment}).");
        }

        static SupaRunSettings.EnvironmentData FindByRef(SupaRunSettings settings, string projectRef)
        {
            foreach (var e in settings.Environments)
                if (SupaRunSettings.ProjectIdOf(e.supabaseUrl) == projectRef) return e;
            return null;
        }

        /// <summary>
        /// 환경 하나. **어느 단계가 실패해도 나머지는 채운다** — 정지된 프로젝트는 메트릭이 안 나오지만
        /// 이름·리전·상태는 보여줄 수 있어야 하고, 그게 정확히 사람이 알고 싶은 상황이다.
        /// </summary>
        static async UniTask<JObject> CollectOne(
            SupaRunSettings.EnvironmentData env, SupaRunSettings settings)
        {
            var o = new JObject
            {
                ["name"] = env.name,
                ["supabase_url"] = env.supabaseUrl,
                ["cloud_run_url"] = env.cloudRunUrl,
                ["service_name"] = env.gcpServiceName,
                ["is_editor"] = settings.EditorEnvironment == env.name,
                ["is_build"] = settings.BuildEnvironment == env.name,
                ["collected_at"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            };

            var id = SupaRunSettings.ProjectIdOf(env.supabaseUrl);
            var token = SupaRunSettings.AccessTokenOf(env);
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(token))
            {
                o["error"] = "URL 또는 Access Token 이 없습니다";
                return o;
            }
            o["project_ref"] = id;

            // ── 프로젝트 기본 정보 ──
            var proj = await SupabaseManagementApi.GetProject(id, token);
            if (proj.Ok)
            {
                o["status"] = proj.Value.status;
                o["region"] = proj.Value.region;
                o["created_at"] = proj.Value.createdAt;
            }
            else
            {
                o["error"] = proj.Message;
                return o;   // 프로젝트를 못 읽으면 나머지도 의미가 없다
            }

            // ── 서비스 헬스 ──
            var health = await SupabaseManagementApi.RawGet(id, token, "health?services=db,rest,auth");
            if (health.Ok)
            {
                try
                {
                    var arr = JArray.Parse(health.Value);
                    var services = new JObject();
                    foreach (var s in arr)
                        services[(string)s["name"]] = (bool?)s["healthy"] ?? false;
                    o["services"] = services;
                }
                catch { /* 형식이 바뀌면 그냥 뺀다 */ }
            }

            // ── 디스크 ──
            var disk = await SupabaseManagementApi.RawGet(id, token, "config/disk/util");
            if (disk.Ok)
            {
                try
                {
                    var m = JObject.Parse(disk.Value)["metrics"];
                    o["disk_total"] = (long?)m?["fs_size_bytes"];
                    o["disk_used"] = (long?)m?["fs_used_bytes"];
                }
                catch { /* 무시 */ }
            }

            // ── 메트릭 (Prometheus 텍스트) ──
            var metrics = await SupabaseManagementApi.RawGet(id, token, "analytics/endpoints/metrics");
            if (metrics.Ok)
            {
                var text = metrics.Value;

                // CPU 는 스냅샷 하나로 정확히 못 구한다(cpu_seconds_total 은 delta 가 필요).
                // load average / 코어 수로 근사한다 — 추세를 보기에는 충분하고, 이 값이 1을 넘으면
                // 실제로 대기가 생기고 있다는 뜻이라 의미도 분명하다.
                var load = First(text, "node_load1");
                var cores = Sum(text, "node_cpu_online");
                if (load.HasValue && cores is > 0)
                    o["cpu_percent"] = Math.Round(load.Value / cores.Value * 100.0, 1);
                if (cores is > 0) o["cpu_cores"] = cores.Value;

                var memTotal = First(text, "node_memory_MemTotal_bytes");
                var memAvail = First(text, "node_memory_MemAvailable_bytes");
                if (memTotal is > 0 && memAvail.HasValue)
                {
                    o["mem_total"] = (long)memTotal.Value;
                    o["mem_used"] = (long)(memTotal.Value - memAvail.Value);
                    o["mem_percent"] = Math.Round((1 - memAvail.Value / memTotal.Value) * 100.0, 1);
                }

                // 라벨(service 별)로 여러 줄이라 합산해야 실제 접속 수가 된다.
                var conn = Sum(text, "connection_stats_connection_count");
                if (conn.HasValue) o["connections"] = (int)conn.Value;
            }

            var maxConn = await SupabaseManagementApi.GetMaxConnections(id, token);
            if (maxConn.Ok) o["max_connections"] = maxConn.Value;

            return o;
        }

        // ── Prometheus 텍스트 파싱 ──
        // 전용 라이브러리를 끌어오지 않는다. 필요한 것은 "이름이 같은 줄의 값" 뿐이고,
        // 그건 정규식 한 줄로 끝난다.

        static readonly Regex LineRx = new(
            @"^(?<name>[a-zA-Z_:][a-zA-Z0-9_:]*)(?<labels>\{[^}]*\})?\s+(?<value>[-+0-9.eE]+)\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>이름이 일치하는 첫 줄의 값. 단일 값 메트릭용.</summary>
        static double? First(string text, string name)
        {
            foreach (Match m in LineRx.Matches(text))
                if (m.Groups["name"].Value == name &&
                    double.TryParse(m.Groups["value"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var v))
                    return v;
            return null;
        }

        /// <summary>이름이 일치하는 모든 줄의 합. 라벨별로 쪼개진 메트릭용(코어·커넥션).</summary>
        static double? Sum(string text, string name)
        {
            double total = 0;
            var found = false;
            foreach (Match m in LineRx.Matches(text))
                if (m.Groups["name"].Value == name &&
                    double.TryParse(m.Groups["value"].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var v))
                {
                    total += v;
                    found = true;
                }
            return found ? total : null;
        }
    }
}
