using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Tjdtjq5.AddrX
{
    /// <summary>
    /// 씬 단위로 로드한 핸들을 모아 일괄 해제하는 스코프.
    /// 씬 진입 시 프리로드한 핸들들을 Track해 두고, 씬 전환 시 Dispose 한 번으로 정리한다.
    /// </summary>
    public sealed class AddrXSceneScope : IDisposable
    {
        readonly List<IDisposable> _handles = new();
        bool _disposed;

        /// <summary>현재 보유 핸들 수.</summary>
        public int Count => _handles.Count;

        /// <summary>핸들을 스코프에 등록한다.</summary>
        public void Track(IDisposable handle)
        {
            if (handle == null) return;
            _handles.Add(handle);
        }

        /// <summary>등록된 모든 핸들을 역순으로 Dispose한다.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = _handles.Count - 1; i >= 0; i--)
                _handles[i]?.Dispose();
            _handles.Clear();
        }
    }

    public static partial class AddrX
    {
        /// <summary>Addressable 씬을 로드한다. 반환 핸들 Dispose 시 씬이 언로드된다.</summary>
        public static async UniTask<SafeHandle<SceneInstance>> LoadSceneAsync(
            object key, LoadSceneMode mode = LoadSceneMode.Single, bool activateOnLoad = true)
        {
            await EnsureInitialized();

            var op = Addressables.LoadSceneAsync(key, mode, activateOnLoad);
            await op.Task;

            var handle = new AssetHandle<SceneInstance>(op, key);
            if (op.Status == AsyncOperationStatus.Failed)
                AddrXLog.Error(Tag, $"씬 로드 실패: {key} ({op.OperationException?.Message})");
            else
                AddrXLog.Verbose(Tag, $"씬 로드 완료: {key}");
            return handle;
        }

        /// <summary>라벨에 해당하는 에셋을 로드해 스코프에 등록한다(씬 프리로드용).</summary>
        public static async UniTask LoadIntoScopeAsync<T>(
            AddrXSceneScope scope, string label, Action<float> onProgress = null)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            var handles = await LoadByLabelAsync<T>(label, onProgress);
            foreach (var h in handles)
                scope.Track(h);
        }

        /// <summary>여러 라벨 조합으로 로드해 스코프에 등록한다(예: [그룹, "Required"] 교집합으로 코어 프리로드).</summary>
        public static async UniTask LoadIntoScopeAsync<T>(
            AddrXSceneScope scope, IList<string> labels,
            Addressables.MergeMode mergeMode = Addressables.MergeMode.Intersection,
            Action<float> onProgress = null)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            var handles = await LoadByLabelAsync<T>(labels, mergeMode, onProgress);
            foreach (var h in handles)
                scope.Track(h);
        }
    }
}
