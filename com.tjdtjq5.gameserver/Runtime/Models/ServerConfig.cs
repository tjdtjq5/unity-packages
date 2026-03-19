using System;

namespace Tjdtjq5.GameServer
{
    [Serializable]
    public class ServerConfig
    {
        /// <summary>Cloud Run 서버 URL. 비어있으면 개발 모드 (LocalGameDB).</summary>
        public string cloudRunUrl;
        public string supabaseUrl;
        public string supabaseAnonKey;

        /// <summary>프로덕션 모드 여부. cloudRunUrl이 설정되어 있으면 true.</summary>
        public bool IsProduction => !string.IsNullOrEmpty(cloudRunUrl);
    }
}
