using UnityEditor;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 비밀의 **로컬** 저장소. `EditorPrefs` 라 이 컴퓨터에만 있고 git 에 올라가지 않는다.
    ///
    /// 예전에는 여기 있던 값들을 `ProjectSettings/SupaRunProjectSettings.json` 으로 옮겼었다.
    /// 이유는 공유였다 — EditorPrefs 는 팀원에게 전달할 방법이 없어서, 파일에 담아 git 으로
    /// 나르는 것 말고는 손이 없었다. 그 대가로 계정 마스터키가 저장소에 남았다.
    ///
    /// 이제 공유는 `suparun_secret` 테이블이 맡는다(<see cref="SupaRunSecretStore"/>).
    /// 공유 경로가 생겼으니 로컬 저장소는 다시 git 밖으로 나올 수 있다 —
    /// **파일에서 빼는 것이 목적이지 로컬에서 없애는 것이 목적이 아니다.**
    /// 로컬에 남아 있어야 컴파일마다 도는 스키마 반영이 네트워크 없이 계속 동작한다.
    ///
    /// 환경별 값과 공통 값을 나눈다: PAT·DB 비밀번호·Cron Secret 은 Supabase 프로젝트마다
    /// 다르고, GitHub 토큰은 레포 하나를 공유하므로 환경과 무관하다.
    /// </summary>
    public static class SupaRunSecretPrefs
    {
        static string Key(string name, string env) =>
            string.IsNullOrEmpty(env)
                ? $"{EditorPrefUtils.ProjectPrefix}Secret_{name}"
                : $"{EditorPrefUtils.ProjectPrefix}Secret_{env}_{name}";

        /// <summary>
        /// 값을 읽는다. 없으면 <paramref name="legacy"/> — 아직 파일에 남아 있는 옛 값이다.
        /// 폴백을 두는 이유: 마이그레이션이 돌기 전이나 git 에서 옛 파일을 되돌린 경우에도
        /// 조용히 빈 값이 되어 배포가 실패하는 일이 없어야 한다.
        /// </summary>
        public static string Get(string name, string env, string legacy) =>
            EditorPrefs.GetString(Key(name, env), null) is { Length: > 0 } v ? v : legacy ?? "";

        public static void Set(string name, string env, string value)
        {
            if (string.IsNullOrEmpty(value)) EditorPrefs.DeleteKey(Key(name, env));
            else EditorPrefs.SetString(Key(name, env), value);
        }

        /// <summary>이 컴퓨터에 값이 있는가. "팀원이 아직 안 받아온 상태" 를 화면에 알릴 때 쓴다.</summary>
        public static bool Has(string name, string env) =>
            !string.IsNullOrEmpty(EditorPrefs.GetString(Key(name, env), ""));
    }
}
