using System.Collections.Generic;
using System.Threading;
using Unity.Entities;
using Unity.Scenes;
using UnityEngine;

namespace N4EGuard
{
    /// <summary>
    /// Netcode public API(<c>CreateServerWorld</c>/<c>CreateClientWorld</c>)로 만든 World에
    /// 현재 씬의 <see cref="SubScene"/>을 수동 로드 + 완료 대기하는 헬퍼.
    ///
    /// Why:
    ///   <c>ClientServerBootstrap.CreateDefaultClientServerWorlds</c>는 내부에서 SubScene을 Server/Client World에 자동 복제한다.
    ///   그러나 public <c>CreateServerWorld</c>/<c>CreateClientWorld</c>는 systems만 만들고 SubScene 연동은 하지 않는다.
    ///   매칭 완료 후 역할에 따라 World를 동적 생성하는 플로우(lazy World creation)에서는 SubScene 베이킹 엔티티
    ///   (예: PlayerGhostPrefab 싱글톤)가 누락된다 → 이 헬퍼로 수동 보정.
    /// </summary>
    public static class NetcodeSubSceneLoader
    {
        /// <summary>
        /// 현재 씬의 <see cref="SubScene.AllSubScenes"/>를 지정 World에 비동기 로드 요청.
        /// 반환된 Entity 리스트는 <see cref="WaitUntilLoadedAsync"/> 인자로 사용.
        /// </summary>
        public static List<Entity> RequestLoadAll(World world)
        {
            var result = new List<Entity>();
            if (world == null || !world.IsCreated) return result;

            // SubScene.AllSubScenes는 #if UNITY_EDITOR 전용이라 빌드에서 사용 불가 → 런타임 공용 FindObjectsByType 사용.
            var subScenes = Object.FindObjectsByType<SubScene>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var subScene in subScenes)
            {
                if (subScene == null || !subScene.SceneGUID.IsValid) continue;
                var sceneEntity = SceneSystem.LoadSceneAsync(world.Unmanaged, subScene.SceneGUID);
                result.Add(sceneEntity);
            }
            Debug.Log($"[N4EGuard][SubSceneLoader] RequestLoadAll({world.Name}): {result.Count} SubScene(s) 요청");
            return result;
        }

        /// <summary>
        /// 지정 World/Scene 쌍 전부가 <see cref="SceneSystem.IsSceneLoaded"/> 통과할 때까지 프레임 대기.
        /// 타임아웃 시 false 반환. CancellationToken 취소 시 false 반환.
        /// </summary>
        public static async Awaitable<bool> WaitUntilLoadedAsync(
            IReadOnlyList<(World world, IReadOnlyList<Entity> scenes)> groups,
            float timeoutSeconds, CancellationToken ct)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + Mathf.Max(0.1f, timeoutSeconds);
            while (true)
            {
                if (ct.IsCancellationRequested) return false;

                bool allLoaded = true;
                for (int i = 0; i < groups.Count; i++)
                {
                    var g = groups[i];
                    if (!AllLoaded(g.world, g.scenes))
                    {
                        allLoaded = false;
                        break;
                    }
                }
                if (allLoaded) return true;

                if (Time.realtimeSinceStartupAsDouble >= deadline)
                {
                    Debug.LogWarning($"[N4EGuard][SubSceneLoader] WaitUntilLoadedAsync 타임아웃 ({timeoutSeconds}s)");
                    return false;
                }

                try { await Awaitable.NextFrameAsync(ct); }
                catch (System.OperationCanceledException) { return false; }
            }
        }

        static bool AllLoaded(World world, IReadOnlyList<Entity> scenes)
        {
            if (world == null || !world.IsCreated) return false;
            if (scenes == null || scenes.Count == 0) return true;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (!SceneSystem.IsSceneLoaded(world.Unmanaged, scenes[i]))
                    return false;
            }
            return true;
        }
    }
}
