namespace Tjdtjq5.GameServer.Editor
{
    public static class ServerCacheTypes
    {
        public const string NuGet = "nuget";
        public const string Docker = "docker";
        public const string Skip = "skip";

        public struct CacheInfo
        {
            public string Id;
            public string Label;
            public string Description;
        }

        public static readonly CacheInfo[] All =
        {
            new() { Id = NuGet,  Label = "NuGet",    Description = "NuGet 패키지 restore 캐시 (Dockerfile 레이어 분리)" },
            new() { Id = Docker, Label = "Docker",   Description = "Cloud Build 이미지 레이어 캐시" },
            new() { Id = Skip,   Label = "변경 감지", Description = "코드 미변경 시 배포 스킵" },
        };

        public static string GetLabel(string id)
        {
            foreach (var c in All)
                if (c.Id == id) return c.Label;
            return id;
        }
    }
}
