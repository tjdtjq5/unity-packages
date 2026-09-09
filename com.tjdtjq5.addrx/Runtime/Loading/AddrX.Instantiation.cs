using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Tjdtjq5.AddrX
{
    public static partial class AddrX
    {
        // ── Instantiator 확장점 ──
        static IAddrXInstantiator _instantiator = new DefaultInstantiator();

        /// <summary>
        /// 인스턴스 생성/파괴 전략. 부트스트랩에서 풀링·DI 구현체로 교체한다.
        /// null 설정 시 기본 전략(Object.Instantiate/Destroy)으로 복귀.
        /// </summary>
        public static IAddrXInstantiator Instantiator
        {
            get => _instantiator;
            set => _instantiator = value ?? new DefaultInstantiator();
        }

        // ── 프리팹 핸들 캐시 (키별 1회 로드, 스코프 기반 해제) ──
        sealed class PrefabEntry
        {
            public Task<SafeHandle<GameObject>> LoadTask;
            public SafeHandle<GameObject> Handle;
            public int Live;
        }

        static readonly Dictionary<object, PrefabEntry> _prefabCache = new();

#if UNITY_EDITOR
        // Enter Play Mode Settings(도메인 리로드 비활성) 대응 — 부트스트랩이 Instantiator를 재등록한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetInstantiation()
        {
            _instantiator = new DefaultInstantiator();
            _prefabCache.Clear();
        }
#endif

        // ════════════════════════════════════════
        //  InstantiateAsync — key (string/object)
        // ════════════════════════════════════════

        /// <summary>키로 프리팹을 로드해 인스턴스화한다. 반환 핸들 Dispose 시 Instantiator.Destroy로 파괴/회수.</summary>
        public static async UniTask<SafeHandle<GameObject>> InstantiateAsync(
            object key, Transform parent = null, bool inWorldSpace = false, CancellationToken ct = default)
        {
            var go = await CreateInstanceAsync(key, parent, inWorldSpace, ct);
            return go == null ? null : WrapInstance<GameObject>(go, key);
        }

        /// <summary>키로 인스턴스화하고 컴포넌트 T로 반환한다.</summary>
        public static async UniTask<SafeHandle<T>> InstantiateAsync<T>(
            object key, Transform parent = null, bool inWorldSpace = false, CancellationToken ct = default)
        {
            var go = await CreateInstanceAsync(key, parent, inWorldSpace, ct);
            return go == null ? null : WrapInstance<T>(go, key);
        }

        /// <summary>키로 인스턴스화 후 위치를 설정한다.</summary>
        public static async UniTask<SafeHandle<GameObject>> InstantiateAsync(
            object key, Vector3 position, Transform parent = null, CancellationToken ct = default)
        {
            var go = await CreateInstanceAsync(key, parent, false, ct);
            if (go == null) return null;
            go.transform.position = position;
            return WrapInstance<GameObject>(go, key);
        }

        /// <summary>키로 인스턴스화 후 위치를 설정하고 컴포넌트 T로 반환한다.</summary>
        public static async UniTask<SafeHandle<T>> InstantiateAsync<T>(
            object key, Vector3 position, Transform parent = null, CancellationToken ct = default)
        {
            var go = await CreateInstanceAsync(key, parent, false, ct);
            if (go == null) return null;
            go.transform.position = position;
            return WrapInstance<T>(go, key);
        }

        // ════════════════════════════════════════
        //  InstantiateAsync — AssetReference
        // ════════════════════════════════════════

        public static UniTask<SafeHandle<GameObject>> InstantiateAsync(
            AssetReference reference, Transform parent = null, bool inWorldSpace = false, CancellationToken ct = default)
            => InstantiateAsync(ResolveKey(reference), parent, inWorldSpace, ct);

        public static UniTask<SafeHandle<T>> InstantiateAsync<T>(
            AssetReference reference, Transform parent = null, bool inWorldSpace = false, CancellationToken ct = default)
            => InstantiateAsync<T>(ResolveKey(reference), parent, inWorldSpace, ct);

        public static UniTask<SafeHandle<GameObject>> InstantiateAsync(
            AssetReference reference, Vector3 position, Transform parent = null, CancellationToken ct = default)
            => InstantiateAsync(ResolveKey(reference), position, parent, ct);

        public static UniTask<SafeHandle<T>> InstantiateAsync<T>(
            AssetReference reference, Vector3 position, Transform parent = null, CancellationToken ct = default)
            => InstantiateAsync<T>(ResolveKey(reference), position, parent, ct);

        // ════════════════════════════════════════
        //  Destroy / Release
        // ════════════════════════════════════════

        /// <summary>AddrX가 생성한 인스턴스를 파괴(또는 풀 반환)한다. 태그로 핸들을 찾아 Dispose한다.</summary>
        public static void Destroy(GameObject go)
        {
            if (go == null) return;

            if (go.TryGetComponent<AddrXInstanceTag>(out var tag) && tag.Handle != null)
                tag.Handle.Dispose();        // → ReleaseInstance → Instantiator.Destroy
            else
                _instantiator.Destroy(go);   // AddrX 비생성 GO도 프로젝트 파괴 정책 경유
        }

        /// <summary>특정 키의 프리팹 로드 핸들을 해제한다(이미 생성된 인스턴스에는 영향 없음).</summary>
        public static void ReleasePrefab(object key)
        {
            if (_prefabCache.TryGetValue(key, out var entry))
            {
                entry.Handle?.Dispose();
                _prefabCache.Remove(key);
            }
        }

        /// <summary>모든 프리팹 로드 핸들을 해제한다(씬 전환/정리 시 호출).</summary>
        public static void ReleaseAllPrefabs()
        {
            foreach (var entry in _prefabCache.Values)
                entry.Handle?.Dispose();
            _prefabCache.Clear();
        }

        // ════════════════════════════════════════
        //  내부 구현
        // ════════════════════════════════════════

        static object ResolveKey(AssetReference reference)
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));
            if (!reference.RuntimeKeyIsValid())
                throw new InvalidOperationException("AssetReference의 RuntimeKey가 유효하지 않습니다.");
            return reference.RuntimeKey;
        }

        /// <summary>
        /// <paramref name="ct"/> 는 <b>대기 지점</b>에서만 관측된다 — 로드 자체는 끊지 않는다.
        /// 이유가 둘이다. (1) Addressables 에 로드 취소 API 가 없다(Release 뿐이고 중도 Release 는
        /// 안전하지 않다). (2) 프리팹 로드는 <see cref="_prefabCache"/> 로 키당 1개를 공유하므로,
        /// 한 호출자의 취소로 <c>LoadTask</c> 를 끊으면 같은 키를 기다리던 다른 호출자까지 죽는다.
        /// 따라서 얻는 보장은 "취소된 호출자에게는 인스턴스를 만들어 주지 않는다"이며, 호출자는
        /// <see cref="OperationCanceledException"/> 을 받는다.
        /// </summary>
        static async UniTask<GameObject> CreateInstanceAsync(
            object key, Transform parent, bool inWorldSpace, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            await EnsureInitialized();

            var prefab = await GetOrLoadPrefabAsync(key);
            if (prefab == null) return null;

            // 로드를 기다리는 사이 호출자가 사라졌다(팝업 파괴 등) — 만들기 전에 끊는다.
            // GetOrLoadPrefabAsync 가 이미 Live 를 올렸으므로 아래 null 분기와 같은 방식으로 되돌린다.
            if (ct.IsCancellationRequested)
            {
                if (_prefabCache.TryGetValue(key, out var cancelled) && cancelled.Live > 0) cancelled.Live--;
                throw new OperationCanceledException(ct);
            }

            var go = _instantiator.Instantiate(prefab, parent, inWorldSpace);
            if (go == null)
            {
                AddrXLog.Error(Tag, $"Instantiator가 null 인스턴스 반환: {key}");
                if (_prefabCache.TryGetValue(key, out var e) && e.Live > 0) e.Live--;  // Live 롤백
                return null;
            }

            if (!go.TryGetComponent<AddrXInstanceTag>(out var tag))
                tag = go.AddComponent<AddrXInstanceTag>();
            tag.Key = key;

            AddrXLog.Verbose(Tag, $"인스턴스 생성: {key}");
            return go;
        }

        static SafeHandle<T> WrapInstance<T>(GameObject go, object key)
        {
            T value = ResolveValue<T>(go);
            var handle = new InstanceHandle<T>(value, () => ReleaseInstance(key, go), key);
            if (go.TryGetComponent<AddrXInstanceTag>(out var tag))
                tag.Handle = handle;
            return handle;
        }

        static T ResolveValue<T>(GameObject go)
        {
            if (typeof(T) == typeof(GameObject))
                return (T)(object)go;
            return go.GetComponent<T>();
        }

        static void ReleaseInstance(object key, GameObject go)
        {
            if (go != null)
                _instantiator.Destroy(go);

            if (_prefabCache.TryGetValue(key, out var entry) && entry.Live > 0)
                entry.Live--;
            // 프리팹 핸들은 여기서 해제하지 않는다 — 인스턴스/풀이 에셋을 참조 중일 수 있어
            // per-instance 해제는 번들 언로드 버그를 유발한다. 씬 전환/정리 시 ReleasePrefab/ReleaseAllPrefabs로 일괄 해제.
        }

        static async UniTask<GameObject> GetOrLoadPrefabAsync(object key)
        {
            if (!_prefabCache.TryGetValue(key, out var entry))
            {
                entry = new PrefabEntry { LoadTask = LoadPrefabHandleAsync(key) };
                _prefabCache[key] = entry;
            }

            // 캐시 히트 fast-path — Task→UniTask 변환은 완료된 Task라도 continuation을 SynchronizationContext에
            // Post하므로 호출당 1프레임을 먹는다(순차 스폰 루프 = 셀당 1프레임 "드르륵"). 핸들이 이미 있거나
            // 로드 Task가 끝나 있으면 await 없이 반환해 같은 프레임에 N개를 만들 수 있게 한다.
            var handle = entry.Handle;
            if (handle == null)
                handle = entry.LoadTask.Status == TaskStatus.RanToCompletion
                    ? entry.LoadTask.Result
                    : await entry.LoadTask.AsUniTask();
            if (handle == null || !handle.IsReady)
            {
                _prefabCache.Remove(key);   // 실패 → 다음 시도 재로드
                return null;
            }

            entry.Handle = handle;
            entry.Live++;
            return handle.Value;
        }

        static async Task<SafeHandle<GameObject>> LoadPrefabHandleAsync(object key)
        {
            var handle = await LoadAsync<GameObject>(key);
            if (handle == null || !handle.IsReady)
            {
                AddrXLog.Error(Tag, $"프리팹 로드 실패: {key}");
                handle?.Dispose();
                return null;
            }
            return handle;
        }
    }
}
