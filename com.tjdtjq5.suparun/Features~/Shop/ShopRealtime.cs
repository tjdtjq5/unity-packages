using System;
using Tjdtjq5.SupaRun.Supabase;

namespace Tjdtjq5.SupaRun
{
    public static partial class ServerAPI
    {
        public static partial class ShopService
        {
            /// <summary>
            /// 상점 상품 변경 실시간 감지.
            /// Subscribe() 후 shopproducts 변경 시 콜백.
            /// Unsubscribe()로 해제. 비용: ⚡1
            /// </summary>
            public static RealtimeChannel OnChange(Action<ShopProduct> callback)
            {
                var ch = SupaRun.Realtime.Channel("shop-products");
                ch.OnPostgresChange<ShopProduct>("shop_product", ChangeEvent.All, callback);
                return ch;
            }
        }
    }
}
