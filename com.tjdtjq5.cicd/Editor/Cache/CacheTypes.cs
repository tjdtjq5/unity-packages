#if UNITY_EDITOR
namespace Tjdtjq5.CICD.Editor
{
    /// <summary>CI 빌드 캐시 종류 정의.</summary>
    public static class CacheTypes
    {
        public const string Library = "library";
        public const string Gradle = "gradle";
        public const string IL2CPP = "il2cpp";
        public const string DockerImage = "docker-image";

        public struct CacheInfo
        {
            public string Id;
            public string Label;
            public string Description;
        }

        public static readonly CacheInfo[] All =
        {
            new() { Id = Library,     Label = "Library",      Description = "Unity Library 폴더 (임포트/컴파일 캐시)" },
            new() { Id = Gradle,      Label = "Gradle",       Description = "Android Gradle 빌드 캐시" },
            new() { Id = IL2CPP,      Label = "IL2CPP",       Description = "IL2CPP 변환 캐시" },
            new() { Id = DockerImage, Label = "Docker Image",  Description = "Unity Docker 이미지 캐시 (pull 시간 절약)" },
        };

        public static string GetLabel(string id)
        {
            foreach (var c in All)
                if (c.Id == id) return c.Label;
            return id;
        }
    }
}
#endif
