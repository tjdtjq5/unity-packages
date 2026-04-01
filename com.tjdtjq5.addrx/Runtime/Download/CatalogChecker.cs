using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Tjdtjq5.AddrX
{
    /// <summary>리모트 카탈로그 업데이트 여부를 확인하고 적용한다.</summary>
    public static class CatalogChecker
    {
        const string Tag = "CatalogChecker";

        /// <summary>업데이트 가능한 카탈로그가 있는지 확인한다.</summary>
        public static async Task<List<string>> CheckForUpdatesAsync()
        {
            var op = Addressables.CheckForCatalogUpdates(false);
            try
            {
                await op.Task;

                if (op.Status != AsyncOperationStatus.Succeeded)
                {
                    AddrXLog.Error(Tag, $"카탈로그 업데이트 확인 실패: {op.OperationException?.Message}");
                    return new List<string>();
                }

                var catalogs = new List<string>(op.Result);

                if (catalogs.Count > 0)
                    AddrXLog.Info(Tag, $"업데이트 가능한 카탈로그: {catalogs.Count}개");
                else
                    AddrXLog.Verbose(Tag, "카탈로그 최신 상태");

                return catalogs;
            }
            finally
            {
                Addressables.Release(op);
            }
        }

        /// <summary>업데이트 가능한 카탈로그를 적용한다.</summary>
        public static async Task<bool> UpdateCatalogsAsync(List<string> catalogIds = null)
        {
            catalogIds ??= await CheckForUpdatesAsync();
            if (catalogIds.Count == 0) return true;

            AddrXLog.Info(Tag, $"카탈로그 업데이트 시작: {catalogIds.Count}개");

            var op = Addressables.UpdateCatalogs(catalogIds, false);
            try
            {
                await op.Task;

                if (op.Status == AsyncOperationStatus.Succeeded)
                {
                    AddrXLog.Info(Tag, "카탈로그 업데이트 완료");
                    return true;
                }

                AddrXLog.Error(Tag, $"카탈로그 업데이트 실패: {op.OperationException?.Message}");
                return false;
            }
            finally
            {
                Addressables.Release(op);
            }
        }
    }
}
