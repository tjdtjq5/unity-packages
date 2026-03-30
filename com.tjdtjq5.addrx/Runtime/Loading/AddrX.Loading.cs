using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Tjdtjq5.AddrX
{
    public static partial class AddrX
    {
        /// <summary>AssetReference로 에셋을 로드한다.</summary>
        public static async Task<SafeHandle<T>> LoadAsync<T>(AssetReference reference)
        {
            if (reference == null)
                throw new ArgumentNullException(nameof(reference));
            if (!reference.RuntimeKeyIsValid())
                throw new InvalidOperationException(
                    "AssetReference의 RuntimeKey가 유효하지 않습니다.");

            return await LoadAsync<T>(reference.RuntimeKey);
        }

        /// <summary>여러 에셋을 동시에 로드하고 진행률을 보고한다.</summary>
        public static async Task<SafeHandle<T>[]> LoadBatchAsync<T>(
            IList<string> keys, Action<float> onProgress = null)
        {
            await EnsureInitialized();

            int count = keys.Count;
            if (count == 0) return Array.Empty<SafeHandle<T>>();

            var ops = new AsyncOperationHandle<T>[count];
            for (int i = 0; i < count; i++)
                ops[i] = Addressables.LoadAssetAsync<T>(keys[i]);

            // 진행률 콜백 등록
            if (onProgress != null)
            {
                int completed = 0;
                for (int i = 0; i < count; i++)
                {
                    ops[i].Completed += _ =>
                    {
                        completed++;
                        try { onProgress((float)completed / count); }
                        catch (Exception e)
                        {
                            AddrXLog.Error(Tag,
                                $"진행률 콜백 예외: {e.Message}");
                        }
                    };
                }
            }

            // 전체 완료 대기
            var tasks = new Task[count];
            for (int i = 0; i < count; i++)
                tasks[i] = ops[i].Task;
            await Task.WhenAll(tasks);

            // SafeHandle 래핑
            var handles = new SafeHandle<T>[count];
            int failCount = 0;
            for (int i = 0; i < count; i++)
            {
                handles[i] = new SafeHandle<T>(ops[i], keys[i]);
                if (ops[i].Status == AsyncOperationStatus.Failed)
                {
                    AddrXLog.Error(Tag,
                        $"배치 로드 실패: {keys[i]} ({ops[i].OperationException?.Message})");
                    failCount++;
                }
            }

            if (failCount > 0)
                AddrXLog.Warning(Tag, $"배치 로드: {count}개 중 {failCount}개 실패");
            else
                AddrXLog.Verbose(Tag, $"배치 로드 완료: {count}개 ({typeof(T).Name})");

            return handles;
        }

        /// <summary>Addressable 에셋을 로드하고 인스턴스화한다.</summary>
        public static async Task<SafeHandle<GameObject>> InstantiateAsync(
            string key, Transform parent = null)
        {
            await EnsureInitialized();

            var op = Addressables.InstantiateAsync(key, parent);
            await op.Task;

            var handle = new SafeHandle<GameObject>(op, key);

            if (op.Status == AsyncOperationStatus.Failed)
            {
                AddrXLog.Error(Tag,
                    $"인스턴스 생성 실패: {key} ({op.OperationException?.Message})");
            }
            else
            {
                AddrXLog.Verbose(Tag, $"인스턴스 생성 완료: {key}");
            }

            return handle;
        }

        /// <summary>AssetLabelReference로 에셋을 로드한다. Inspector 드롭다운 연동.</summary>
        public static Task<SafeHandle<T>[]> LoadByLabelAsync<T>(
            AssetLabelReference labelRef, Action<float> onProgress = null)
        {
            if (labelRef == null)
                throw new ArgumentNullException(nameof(labelRef));
            return LoadByLabelAsync<T>(labelRef.labelString, onProgress);
        }

        /// <summary>라벨에 해당하는 모든 에셋을 로드한다.</summary>
        public static async Task<SafeHandle<T>[]> LoadByLabelAsync<T>(
            string label, Action<float> onProgress = null)
        {
            await EnsureInitialized();

            var locationsOp = Addressables.LoadResourceLocationsAsync(label, typeof(T));
            await locationsOp.Task;

            try
            {
                if (locationsOp.Status == AsyncOperationStatus.Failed)
                {
                    AddrXLog.Error(Tag, $"라벨 위치 조회 실패: {label}");
                    return Array.Empty<SafeHandle<T>>();
                }

                var locations = locationsOp.Result;
                if (locations.Count == 0)
                {
                    AddrXLog.Warning(Tag, $"라벨 '{label}'에 해당하는 에셋 없음");
                    return Array.Empty<SafeHandle<T>>();
                }

                return await LoadFromLocations<T>(locations, $"라벨 '{label}'", onProgress);
            }
            finally
            {
                Addressables.Release(locationsOp);
            }
        }

        /// <summary>여러 라벨 조합으로 에셋을 로드한다. Union(합집합) 또는 Intersection(교집합).</summary>
        public static async Task<SafeHandle<T>[]> LoadByLabelAsync<T>(
            IList<string> labels,
            Addressables.MergeMode mergeMode = Addressables.MergeMode.Union,
            Action<float> onProgress = null)
        {
            await EnsureInitialized();

            var locationsOp = Addressables.LoadResourceLocationsAsync(
                (IEnumerable)labels, mergeMode, typeof(T));
            await locationsOp.Task;

            try
            {
                if (locationsOp.Status == AsyncOperationStatus.Failed)
                {
                    AddrXLog.Error(Tag, $"라벨 위치 조회 실패: [{string.Join(", ", labels)}]");
                    return Array.Empty<SafeHandle<T>>();
                }

                var locations = locationsOp.Result;
                if (locations.Count == 0)
                {
                    AddrXLog.Warning(Tag,
                        $"라벨 [{string.Join(", ", labels)}]에 해당하는 에셋 없음 ({mergeMode})");
                    return Array.Empty<SafeHandle<T>>();
                }

                var desc = $"라벨 [{string.Join(", ", labels)}] ({mergeMode})";
                return await LoadFromLocations<T>(locations, desc, onProgress);
            }
            finally
            {
                Addressables.Release(locationsOp);
            }
        }

        /// <summary>리소스 위치 목록에서 개별 SafeHandle로 로드하는 내부 공통 로직.</summary>
        static async Task<SafeHandle<T>[]> LoadFromLocations<T>(
            IList<UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation> locations,
            string desc, Action<float> onProgress)
        {
            int count = locations.Count;
            var ops = new AsyncOperationHandle<T>[count];
            var keys = new string[count];

            for (int i = 0; i < count; i++)
            {
                keys[i] = locations[i].PrimaryKey;
                ops[i] = Addressables.LoadAssetAsync<T>(locations[i]);
            }

            if (onProgress != null)
            {
                int completed = 0;
                for (int i = 0; i < count; i++)
                {
                    ops[i].Completed += _ =>
                    {
                        completed++;
                        try { onProgress((float)completed / count); }
                        catch (Exception e)
                        {
                            AddrXLog.Error(Tag, $"진행률 콜백 예외: {e.Message}");
                        }
                    };
                }
            }

            var tasks = new Task[count];
            for (int i = 0; i < count; i++)
                tasks[i] = ops[i].Task;
            await Task.WhenAll(tasks);

            var handles = new SafeHandle<T>[count];
            int failCount = 0;
            for (int i = 0; i < count; i++)
            {
                handles[i] = new SafeHandle<T>(ops[i], keys[i]);
                if (ops[i].Status == AsyncOperationStatus.Failed)
                {
                    AddrXLog.Error(Tag,
                        $"라벨 로드 실패: {keys[i]} ({ops[i].OperationException?.Message})");
                    failCount++;
                }
            }

            if (failCount > 0)
                AddrXLog.Warning(Tag, $"{desc} 로드: {count}개 중 {failCount}개 실패");
            else
                AddrXLog.Verbose(Tag, $"{desc} 로드 완료: {count}개 ({typeof(T).Name})");

            return handles;
        }

        static async Task EnsureInitialized()
        {
            if (!_initialized && _initTask != null)
                await _initTask;

            if (!_initialized)
                throw new InvalidOperationException(
                    "AddrX가 초기화되지 않았습니다. AddrX.Initialize()를 먼저 호출하세요.");
        }
    }
}
