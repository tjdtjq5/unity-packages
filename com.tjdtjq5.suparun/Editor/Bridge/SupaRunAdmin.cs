using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 어드민 진입점. **이 패키지의 유일한 화면이다.**
    ///
    /// 예전에는 EditorWindow 대시보드가 같은 일을 IMGUI 로 한 벌 더 갖고 있었다. 그것을 지운 이유는
    /// 화면이 둘이면 **같은 값을 두 곳이 다른 근거로 쓰게 되고, 그러면 반드시 어긋나기** 때문이다
    /// (EnvironmentSnapshot 의 그 규칙과 같다). 지금 화면은 하나뿐이고, Unity 는 브리지를 통해
    /// 로컬에서만 할 수 있는 일(gcloud·gh·dotnet·파일 쓰기)을 대신한다.
    /// </summary>
    public static class SupaRunAdmin
    {
        [MenuItem("Tjdtjq/SupaRun/Admin %#d")]
        public static void Open() => OpenAsync(null).Forget();

        /// <summary>특정 화면으로 바로 연다. 해시는 어드민 라우터가 읽는다(features/shell/route.ts).</summary>
        public static void Open(string hash) => OpenAsync(hash).Forget();

        static async UniTaskVoid OpenAsync(string hash)
        {
            // **설정이 하나도 없을 때 여는 것이 정상 흐름이다.** 어드민이 첫 셋업(온보딩)을 맡으므로,
            // 여기서 "Supabase 설정이 필요합니다" 로 막으면 셋업하러 갈 길이 사라진다.
            SupaRunBridge.EnsureRunning();
            if (!SupaRunBridge.Running)
            {
                EditorUtility.DisplayDialog("SupaRun",
                    "로컬 브리지를 열지 못했습니다.\n포트가 모두 사용 중인지 Console 을 확인하세요.", "확인");
                return;
            }

            // 어드민은 아이콘/컴포넌트 맵을 DB 에서 읽는다 (ADR-0004). 여기가 그것들을 굽기에
            // 정확한 시점이다 — 실제로 필요해지는 순간이고, 컴파일마다 굽는 낭비가 없다.
            // 페이지를 열기 전에 끝내야 첫 화면부터 아이콘이 보인다.
            // 스프라이트가 그대로면 해시가 같아 왕복 없이 지나간다.
            await SchemaAutoSync.SyncAdminAssets();

            var url = $"http://127.0.0.1:{SupaRunBridge.Port}/admin/";
            if (!string.IsNullOrEmpty(hash)) url += "#" + hash;
            Application.OpenURL(url);

            // 환경 현황은 **페이지를 띄운 뒤** 굽는다. Management API 를 여섯 번 왕복해
            // 7초 가까이 걸리는데(metrics 하나가 2.7초), 그동안 브라우저를 막아 둘 이유가 없다 —
            // 어드민은 이 값이 없어도 열리고, 환경 화면은 사람이 사이드바를 눌러 들어가는 곳이라
            // 그 전에 끝난다. 낡은 값이 보일 때도 화면이 수집 시각을 함께 띄우고 새로고침이 있다.
            //
            // PAT 로만 얻을 수 있는 정보라 Unity 가 넣어 두고 어드민은 읽기만 한다는 구조는 그대로다.
            await EnvironmentSnapshot.CollectAndPublishAsync();
        }
    }
}
