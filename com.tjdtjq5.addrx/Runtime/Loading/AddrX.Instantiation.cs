using System;
using System.Collections.Generic;
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
            object key, Transform parent = null, bool inWorldSpace = false)
        {
            var go = await CreateInstanceAsync(key, parent, inWorldSpace);
            return go == null ? null : WrapInstance<GameObject>(go, key);
        }

        /// <summary>키로 인스턴스화하고 컴포넌트 T로 반환한다.</summary>
        public static async UniTask<SafeHandle<T>> InstantiateAsync<T>(
            object key, Transform parent = null, bool inWorldSpace = false)
        {
            var go = await CreateInstanceAsync(key, parent, inWorldSpace);
            return go == null ? null : WrapInstance<T>(go, key);
        }

        /// <summary>키로 인스턴스화 후 위치를 설정한다.</summary>
        public static async UniTask<SafeHandle<GameObject>> InstantiateAsync(
            object key, Vector3 position, Transform parent = null)
        {
            var go = await CreateInstanceAsync(key, parent, false);
            if (go == null) return null;
            go.transform.position = position;
            return WrapInstance<GameObject>(go, key);
        }

        /// <summary>키로 인스턴스화 후 위치를 설정하고 컴포넌트 T로 반환한다.</summary>
        public static async UniTask<SafeHandle<T>> InstantiateAsync<T>(
            object key, Vector3 position, Transform parent = null)
        {
            var go = await CreateInstanceAsync(key, parent, false);
            if (go == null) return null;
            go.transform.position = position;
            return WrapInstance<T>(go, key);
        }

        // ════════════════════════════════════════
        //  InstantiateAsync — AssetReference
        // ════════════════════════════════════════

        public static UniTask<SafeHandle<GameObject>> InstantiateAsync(
            AssetReference reference, Transform parent = null, bool inWorldSpace = false)
            => InstantiateAsync(ResolveKey(reference), parent, inWorldSpace);

        public static UniTask<SafeHandle<T>> InstantiateAsync<T>(
            AssetReference reference, Transform parent = null, bool inWorldSpace = false)
            => InstantiateAsync<T>(ResolveKey(reference), parent, inWorldSpace);

        public static UniTask<SafeHandle<GameObject>> InstantiateAsync(
            AssetReference reference, Vector3 position, Transform parent = null)
            => InstantiateAsync(ResolveKey(reference), position, parent);

        public static UniTask<SafeHandle<T>> InstantiateAsync<T>(
            AssetReference reference, Vector3 position, Transform parent = null)
            => InstantiateAsync<T>(ResolveKey(reference), position, parent);

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

        static async UniTask<GameObject> CreateInstanceAsync(object key, Transform parent, bool inWorldSpace)
        {
            await EnsureInitialized();

            var prefab = await GetOrLoadPrefabAsync(key);
            if (prefab == null) return null;

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

            var handle = await entry.LoadTask.AsUniTask();
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
