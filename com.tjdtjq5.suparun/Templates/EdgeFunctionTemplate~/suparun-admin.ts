// suparun-admin — 어드민이 직접 할 수 없는 호출을 대신한다.
//
// 왜 여기인가: 어드민 웹은 `api.supabase.com` 을 못 부른다. 그쪽 CORS 가 `https://supabase.com`
// 오리진만 허용하기 때문이다(실측 확인). 그리고 PAT 는 Supabase 계정 전체의 마스터키라
// 브라우저에 내려보낼 수 없다. 누군가 대신 불러야 하는데, 그 자리를 Cloud Run 이 맡으면
// **첫 배포 전에는 존재하지 않는다** — 배포에 필요한 값을 어드민에서 받으려는데 정작
// 어드민을 띄울 서버가 없는 순환이 생긴다.
//
// Edge Function 은 Supabase 프로젝트가 만들어지는 순간 존재한다. PAT 하나로 배포되고
// Cloud Run·GitHub·gcloud 가 전부 필요 없다. 그래서 순환이 끊긴다.
//
// 의존성을 쓰지 않는다(fetch 만). Management API 로 배포하면 Supabase 가 서버에서 번들링하는데
// 그 결과물 상한이 5MB 다. supabase-js 를 끌어오면 쓸데없이 그 예산을 먹는다.

// 배포된 소스가 무엇인지 확인하는 표식. 바꿀 때마다 올린다.
const VERSION = 11;

const SUPABASE_URL = Deno.env.get("SUPABASE_URL") ?? "";
const SERVICE_ROLE = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";
const ANON_KEY = Deno.env.get("SUPABASE_ANON_KEY") ?? "";

const CORS: Record<string, string> = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, apikey, content-type",
  "Access-Control-Allow-Methods": "GET, POST, PATCH, DELETE, OPTIONS",
};

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...CORS, "Content-Type": "application/json" },
  });
}

/** 호출자가 누구인가. 토큰 검증은 Supabase auth 에 맡긴다 — 여기서 서명을 직접 다루지 않는다. */
interface Caller {
  userId: string | null;
  email: string | null;
  isAdmin: boolean;
  /** 아직 아무도 관리자가 아닌 상태. 첫 로그인이 주인이 되는 구간이다. */
  unclaimed: boolean;
}

async function identify(req: Request): Promise<Caller> {
  const empty: Caller = { userId: null, email: null, isAdmin: false, unclaimed: false };

  const auth = req.headers.get("Authorization") ?? "";
  const token = auth.startsWith("Bearer ") ? auth.slice(7) : "";
  // anon key 를 그대로 보내는 경우가 있다(로그인 전 어드민). 그건 사용자가 아니다.
  if (!token || token === ANON_KEY) return { ...empty, unclaimed: await isUnclaimed() };

  // 서명·만료 검증을 GoTrue 가 한다. 200 이 아니면 유효하지 않은 토큰이다.
  const userRes = await fetch(`${SUPABASE_URL}/auth/v1/user`, {
    headers: { apikey: ANON_KEY, Authorization: `Bearer ${token}` },
  });
  if (!userRes.ok) return { ...empty, unclaimed: await isUnclaimed() };

  const user = await userRes.json();
  const userId: string = user.id;
  const email: string | null = user.email ?? null;
  // 같은 이메일이라도 프로바이더가 다르면 별개 사용자다. 목록에서 구분하려면 이 값이 필요하다.
  //
  // **연결된 신원을 전부 적는다.** Supabase 가 같은 이메일의 프로바이더를 한 계정에 묶기도 해서
  // (`app_metadata.providers` 가 여러 개가 된다), 하나만 적으면 "처음 쓴 것" 만 남아
  // 목록이 실제와 어긋난다.
  const list: string[] = user.app_metadata?.providers
    ?? (user.app_metadata?.provider ? [user.app_metadata.provider] : []);
  const provider: string | null = list.length > 0 ? list.join("+") : null;

  // **uid 로만 찾는다.** 예전에는 이메일로도 매칭했는데, 이메일은 신원이 아니다 —
  // 같은 이메일을 쓰는 GitHub 계정과 Google 계정은 Supabase 에서 서로 다른 사용자다.
  // 이메일로 매칭하면 새 계정이 남의 관리자 행을 자기 것으로 인식하고, 그런데 `user_id` 는
  // 그대로라서 DB 의 `is_admin()`(uid 비교)은 계속 거짓이다 — **함수는 관리자라 하고 RLS 는
  // 아니라고 하는** 상태가 만들어진다. 실제로 그 상태에 빠지는 것을 확인했다.
  const rows = await rest(
    `admin_user?select=id,role,provider&user_id=eq.${encodeURIComponent(userId)}&limit=1`,
  );
  const me = Array.isArray(rows) && rows.length > 0 ? rows[0] : null;

  if (me) {
    // 신원이 나중에 더 묶일 수 있다(같은 이메일로 다른 프로바이더 로그인). 달라지면 갱신한다.
    if (provider && me.provider !== provider) {
      await rest(`admin_user?id=eq.${encodeURIComponent(me.id)}`, "PATCH", { provider });
    }
    return { userId, email, isAdmin: me.role === "admin", unclaimed: false };
  }

  // 아무도 관리자가 아니면 **처음 로그인한 사람이 주인이 된다.**
  // 이 규칙이 없으면 첫 관리자를 만들 방법이 없다 — admin_user 에 쓰려면 이미 관리자여야 하고,
  // 표가 비어 있으면 아무도 관리자가 아니다.
  const unclaimed = await isUnclaimed();
  await rest("admin_user", "POST", {
    id: crypto.randomUUID(),
    user_id: userId,
    email,
    provider,
    role: unclaimed ? "admin" : "pending",
    memo: unclaimed ? "first admin" : "",
    created_at: Date.now(),
    created_by: unclaimed ? "auto" : "",
  });

  return { userId, email, isAdmin: unclaimed, unclaimed: false };
}

async function isUnclaimed(): Promise<boolean> {
  const rows = await rest("admin_user?select=id&role=eq.admin&limit=1");
  return Array.isArray(rows) && rows.length === 0;
}

/**
 * PostgREST 호출. service_role 이라 RLS 를 지나간다 — 인가는 이 파일이 직접 판단한다.
 *
 * `prefer` 를 호출부가 정하는 이유: `on_conflict` 업서트는 `resolution=merge-duplicates` 가
 * 없으면 409 로 떨어진다. 예전에는 이 함수가 Prefer 를 고정해서 업서트가 **조용히 실패**했고,
 * 어드민과 이 함수가 서로 다른 사실을 믿는 상태가 만들어졌다.
 */
async function rest(
  path: string,
  method = "GET",
  body?: unknown,
  prefer = "return=representation",
): Promise<unknown> {
  const res = await fetch(`${SUPABASE_URL}/rest/v1/${path}`, {
    method,
    headers: {
      apikey: SERVICE_ROLE,
      Authorization: `Bearer ${SERVICE_ROLE}`,
      "Content-Type": "application/json",
      Prefer: prefer,
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!res.ok) {
    console.error(`[suparun-admin] rest ${method} ${path} -> ${res.status} ${await res.text()}`);
    return null;
  }
  const text = await res.text();
  return text ? JSON.parse(text) : null;
}

// ── Management API 대행 ──────────────────────────────────────────────
// 브라우저가 못 하는 이유가 둘이다: `api.supabase.com` 의 CORS 가 `https://supabase.com`
// 오리진만 허용하고, PAT 는 계정 마스터키라 내려보낼 수 없다. 여기서 대신 부른다.

const MGMT = "https://api.supabase.com/v1";

/**
 * 로그인 수단이 하나도 없을 때만 열리는 경로. **로그인 수단을 켜는 것**뿐이다.
 * 프로젝트 생성·삭제까지 열면 열려 있는 동안 할 수 있는 일이 불필요하게 넓어진다.
 */
const SETUP_ROUTES = new Set(["/auth-config"]);

/** PAT. Unity 가 Edge Function 을 배포할 때 함께 넣는다 — 둘 다 PAT 가 있어야 하는 작업이라 늘 같이 온다. */
async function pat(): Promise<string | null> {
  const rows = await rest("suparun_secret?select=value&key=eq.supabase_access_token&limit=1");
  return Array.isArray(rows) && rows.length > 0 ? (rows[0] as { value: string }).value : null;
}

async function mgmt(path: string, method = "GET", body?: unknown): Promise<Response> {
  const token = await pat();
  if (!token) throw new HttpError(409, "Access Token 이 DB 에 없습니다.");
  return await fetch(`${MGMT}${path}`, {
    method,
    headers: {
      Authorization: `Bearer ${token}`,
      ...(body === undefined ? {} : { "Content-Type": "application/json" }),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
}

class HttpError extends Error {
  constructor(readonly status: number, message: string) {
    super(message);
  }
}

/** Management API 응답을 그대로 통과시키되, 실패면 본문을 살려 올린다. */
async function mgmtJson(path: string, method = "GET", body?: unknown): Promise<unknown> {
  const res = await mgmt(path, method, body);
  const text = await res.text();
  if (!res.ok) throw new HttpError(res.status, text || `HTTP ${res.status}`);
  return text ? JSON.parse(text) : null;
}

/**
 * 첫 조직. 리전 목록과 프로젝트 생성이 조직 slug 를 **필수로** 요구한다 —
 * 없이 부르면 400 이다(실측 확인).
 */
async function firstOrgSlug(): Promise<string> {
  const orgs = await mgmtJson("/organizations") as { slug: string }[];
  if (!Array.isArray(orgs) || orgs.length === 0) throw new HttpError(502, "조직을 찾지 못했습니다.");
  return orgs[0].slug;
}

/** Supabase 가 요구하는 auth config 필드 접두사. Unity 의 AuthProviderGuide 와 같아야 한다. */
const PROVIDER_FIELD: Record<string, string> = {
  google: "external_google",
  apple: "external_apple",
  github: "external_github",
  kakao: "external_kakao",
  discord: "external_discord",
  facebook: "external_facebook",
  twitter: "external_twitter",
  twitch: "external_twitch",
  spotify: "external_spotify",
  slack: "external_slack_oidc",
};

/**
 * **로그인이 실제로 한 번이라도 성공했는가.** 셋업 문을 닫는 근거다.
 *
 * 처음에는 "로그인 수단이 켜져 있는가" 로 판단했는데, 그러면 **저장하는 순간 문이 닫힌다.**
 * 그런데 그 설정이 맞는지는 저장한 뒤에만 알 수 있다 — 값을 잘못 넣으면 로그인도 안 되고
 * 고칠 수도 없는 상태가 만들어진다. 실제로 그 상태에 빠지는 것을 확인했다.
 *
 * 그래서 기준을 결과로 옮겼다. 누군가 실제로 들어온 적이 있으면 그 사람이 고칠 수 있으므로
 * 닫아도 된다. 아무도 못 들어왔으면 여전히 열어 둬야 한다 — 자기 치유가 된다.
 *
 * 여는 대가는 작다: 관리자가 이미 있으면 남이 프로바이더를 켜고 로그인해도 `pending` 이라
 * 자리를 뺏지 못한다. 관리자가 없을 때만 첫 로그인이 주인이 되는데, 그건 이미 정한 규칙이다.
 */
async function loginVerified(): Promise<boolean> {
  const rows = await rest("suparun_meta?select=value&key=eq.auth_verified&limit=1");
  const v = Array.isArray(rows) && rows.length > 0
    ? (rows[0] as { value?: { verified?: boolean } }).value
    : null;
  return v?.verified === true;
}

/**
 * 지금 켜져 있는 웹 로그인 수단. **Supabase 의 실제 설정에서 바로 읽는다.**
 *
 * 예전에는 이 목록의 사본을 `suparun_meta.auth_config` 에 두고 어드민이 그걸 읽었다.
 * 그런데 Unity 도 자기 로컬 설정을 근거로 같은 자리를 덮어써서, 웹에서 켠 프로바이더가
 * Unity 가 한 번 돌 때마다 사라졌다. **두 곳이 같은 값을 다른 근거로 쓰면 반드시 어긋난다.**
 * 사본을 없애고 진실 하나만 남긴다.
 */
async function currentProviders(): Promise<string[]> {
  const cfg = await mgmtJson(`/projects/${projectRef()}/config/auth`) as Record<string, unknown>;
  return enabledProviders(cfg);
}

/**
 * 셋업 문을 열어 둘 것인가. **두 조건 중 하나라도 걸리면 연다.**
 *
 * 각각이 상대의 구멍을 막는다:
 *   로그인 수단이 없다 → 아무도 못 들어오므로, 켜는 일 자체를 아무도 못 하게 된다
 *   한 번도 성공한 적 없다 → 설정이 틀렸을 수 있는데, 저장 시점에 닫으면 고칠 방법이 사라진다
 *
 * 한쪽만 보면 실제로 갇힌다. 프로바이더 기준만 보면 잘못된 값을 저장하는 순간 닫히고,
 * 검증 기준만 보면 한 번 로그인한 뒤 프로바이더를 끄면 닫힌 채로 남는다. 둘 다 겪었다.
 */
async function setupOpen(providers: string[]): Promise<boolean> {
  return providers.length === 0 || !(await loginVerified());
}

/** 로그인 성공을 남긴다. 한 번 참이 되면 다시 거짓으로 돌리지 않는다. */
async function markLoginVerified(): Promise<void> {
  await rest("suparun_meta?on_conflict=key", "POST", [{
    key: "auth_verified",
    value: { verified: true },
    updated_at: new Date().toISOString(),
  }], "resolution=merge-duplicates");
}

/** 켜져 있는 웹 로그인 수단. 어드민이 잠금 여부를 이걸로 판단한다. */
function enabledProviders(cfg: Record<string, unknown>): string[] {
  const out: string[] = [];
  for (const [key, field] of Object.entries(PROVIDER_FIELD)) {
    if (cfg[`${field}_enabled`] === true) out.push(key);
  }
  return out;
}

Deno.serve(async (req: Request) => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: CORS });

  // 호출 경로는 `/suparun-admin/<route>` 로 들어온다.
  const url = new URL(req.url);
  const path = url.pathname.replace(/^\/suparun-admin/, "") || "/";

  // 살아 있는지, 어느 소스가 올라가 있는지. 인증 없이 답한다 —
  // 배포가 됐는지 확인하는 용도라 여기서 막으면 진단이 안 된다.
  if (path === "/ping") {
    return json({ ok: true, fn: "suparun-admin", version: VERSION });
  }

  const caller = await identify(req);

  try {
    // 유효한 사용자 토큰이 도착했다는 것은 로그인이 실제로 됐다는 뜻이다. 그때 문을 닫는다.
    if (caller.userId) await markLoginVerified();

    // 셋업 라우트를 열지 말지 — 자세한 근거는 setupOpen 참조.
    const providers = await currentProviders();
    const open = await setupOpen(providers);

    if (path === "/whoami") {
      return json({
        userId: caller.userId,
        email: caller.email,
        isAdmin: caller.isAdmin,
        unclaimed: caller.unclaimed,
        /** 로그인 없이 로그인 수단을 켤 수 있는 구간인가. 화면이 폼을 열지 말지 정한다. */
        setupOpen: open,
        /** 로그인 화면이 그릴 버튼. 사본이 아니라 Supabase 의 실제 설정이다. */
        providers,
      });
    }

    if (!caller.isAdmin && !(SETUP_ROUTES.has(path) && open)) {
      return json({
        error: caller.userId ? "관리자 권한이 없습니다." : "로그인이 필요합니다.",
        unclaimed: caller.unclaimed,
        setupOpen: open,
      }, caller.userId ? 403 : 401);
    }

    return await route(path, req, url);
  } catch (e) {
    if (e instanceof HttpError) return json({ error: e.message }, e.status);
    return json({ error: String(e) }, 500);
  }
});

async function route(path: string, req: Request, url: URL): Promise<Response> {
  const m = req.method;

  // ── 로그인 프로바이더 ──
  if (path === "/auth-config" && m === "GET") {
    const cfg = await mgmtJson(`/projects/${projectRef()}/config/auth`) as Record<string, unknown>;
    // **secret 이 든 필드는 지운다.** Supabase 는 client secret 을 그대로 돌려주는데,
    // 흘리면 PAT 를 브라우저에 안 주려던 노력이 여기서 전부 무너진다.
    const safe: Record<string, unknown> = {};
    for (const [k, v] of Object.entries(cfg)) {
      if (!k.startsWith("external_") || k.toLowerCase().includes("secret")) continue;
      safe[k] = v;
    }
    return json(safe);
  }

  if (path === "/auth-config" && m === "POST") {
    const patch = await req.json() as
      { provider: string; clientId?: string; secret?: string; enabled: boolean };
    const field = PROVIDER_FIELD[(patch.provider ?? "").toLowerCase()];
    if (!field) throw new HttpError(400, `'${patch.provider}' 는 웹 로그인 프로바이더가 아닙니다.`);

    // 통째로 넘기지 않고 필요한 필드만 조립한다 — 이 경로로 site_url 같은 다른 설정까지
    // 바뀌면 원인을 찾을 수 없는 사고가 된다.
    const fields: Record<string, unknown> = { [`${field}_enabled`]: patch.enabled };
    if (patch.enabled) {
      if (!patch.clientId?.trim()) throw new HttpError(400, "Client ID 가 비어 있습니다.");
      fields[`${field}_client_id`] = patch.clientId.trim();
      // secret 은 비어 있으면 건드리지 않는다 — 화면에 안 보여주므로 "그대로 두기" 가 기본이어야 한다.
      if (patch.secret?.trim()) fields[`${field}_secret`] = patch.secret.trim();
      fields[`${field}_skip_nonce_check`] = true;
    }

    await mgmtJson(`/projects/${projectRef()}/config/auth`, "PATCH", fields);

    // 방금 보낸 것만 반영하지 않고 실제 상태를 다시 읽는다 —
    // 다른 경로(Supabase 대시보드)로 바뀐 것과 어긋나지 않게.
    return json({ ok: true, providers: await currentProviders() });
  }

  // ── 프로젝트 ──
  if (path === "/projects" && m === "GET") {
    const list = await mgmtJson("/projects") as
      { id: string; name: string; status: string; region: string }[];
    return json({
      projects: (list ?? []).map((p) => ({
        ref: p.id,
        name: p.name,
        status: p.status,
        region: p.region,
        url: `https://${p.id}.supabase.co`,
      })),
    });
  }

  if (path === "/projects" && m === "POST") {
    const body = await req.json() as { name?: string; region?: string; plan?: string };
    const name = (body.name ?? "").trim();
    if (!name) throw new HttpError(400, "이름이 필요합니다.");

    const created = await mgmtJson("/projects", "POST", {
      name,
      organization_slug: await firstOrgSlug(),
      db_pass: generatePassword(),
      ...(body.region ? { region: body.region } : {}),
      ...(body.plan ? { plan: body.plan } : {}),
    }) as { id: string; name: string; status: string };

    // 만든 직후는 아직 COMING_UP 이다. 어드민이 상태를 폴링한다.
    return json({ ref: created.id, name: created.name, status: created.status });
  }

  if (path === "/projects" && m === "DELETE") {
    const ref = url.searchParams.get("ref");
    if (!ref) throw new HttpError(400, "ref 가 필요합니다.");
    await mgmtJson(`/projects/${ref}`, "DELETE");
    return json({ ok: true });
  }

  // ── 생성 보조 ──
  if (path === "/regions" && m === "GET") {
    // 응답이 배열이 아니라 객체다. 쓰는 것은 `all.specific` 이고,
    // `smartGroup`(Americas·APAC …)은 대륙 묶음이라 여기서는 안 쓴다.
    const slug = await firstOrgSlug();
    const res = await mgmtJson(
      `/projects/available-regions?organization_slug=${encodeURIComponent(slug)}`,
    ) as { all?: { specific?: { code: string; name?: string; provider?: string }[] } };

    const regions = (res?.all?.specific ?? [])
      .filter((r) => !!r.code)
      .map((r) => ({
        code: r.code,
        label: [r.name, r.provider].filter(Boolean).join(" · ") || r.code,
      }));
    return json({ regions });
  }

  if (path === "/api-keys" && m === "GET") {
    const ref = url.searchParams.get("ref");
    if (!ref) throw new HttpError(400, "ref 가 필요합니다.");
    const keys = await mgmtJson(`/projects/${ref}/api-keys`) as { name: string; api_key: string }[];
    const anon = (keys ?? []).find((k) => k.name === "anon");
    if (!anon) throw new HttpError(409, "아직 anon key 가 없습니다. 프로젝트가 준비 중일 수 있습니다.");
    return json({ anonKey: anon.api_key });
  }

  return json({ error: `알 수 없는 경로: ${path}` }, 404);
}

/** 이 함수가 올라가 있는 프로젝트. SUPABASE_URL 에서 뽑는다. */
function projectRef(): string {
  return new URL(SUPABASE_URL).hostname.split(".")[0];
}

/** 새 프로젝트의 DB 비밀번호. 사람이 볼 일이 없으므로 길고 무작위면 된다. */
function generatePassword(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(24));
  return btoa(String.fromCharCode(...bytes)).replace(/[+/=]/g, "").slice(0, 24);
}
