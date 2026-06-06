using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Tjdtjq5.AddrX
{
    public static partial class AddrX
    {
        /// <summary>
        /// 키에 해당하는 Addressable 리소스가 카탈로그에 존재하는지 확인한다(로드하지 않음).
        /// 위치 조회 후 즉시 Release하므로 부수효과가 없다.
        /// </summary>
        public static async UniTask<bool> ExistsAsync(object key)
        {
            await EnsureInitialized();

            var op = Addressables.LoadResourceLocationsAsync(key);
            await op.Task;
            try
            {
                return op.Status == AsyncOperationStatus.Succeeded
                       && op.Result != null
                       && op.Result.Count > 0;
            }
            finally
            {
                Addressables.Release(op);
            }
        }
    }
}
