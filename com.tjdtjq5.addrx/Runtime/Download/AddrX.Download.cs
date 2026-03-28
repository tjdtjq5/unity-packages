using System.Collections.Generic;
using System.Threading.Tasks;

namespace Tjdtjq5.AddrX
{
    public static partial class AddrX
    {
        /// <summary>다운로드 매니저를 생성한다. 체이닝으로 키를 추가하고 StartAsync로 실행.</summary>
        public static AddrXDownloader Download(params string[] keys)
        {
            return new AddrXDownloader().Add(keys);
        }

        /// <summary>리모트 카탈로그 업데이트 여부를 확인한다.</summary>
        public static Task<List<string>> CheckCatalogUpdatesAsync()
        {
            return CatalogChecker.CheckForUpdatesAsync();
        }

        /// <summary>리모트 카탈로그를 업데이트한다.</summary>
        public static Task<bool> UpdateCatalogsAsync(List<string> catalogIds = null)
        {
            return CatalogChecker.UpdateCatalogsAsync(catalogIds);
        }
    }
}
