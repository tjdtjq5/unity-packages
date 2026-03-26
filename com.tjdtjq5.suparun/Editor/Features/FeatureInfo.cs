namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>Feature 메타데이터. feature.json에서 파싱.</summary>
    [System.Serializable]
    public class FeatureInfo
    {
        public string name;
        public string id;
        public string description;
        public int tier;
        public string[] dependencies = System.Array.Empty<string>();

        // 런타임에 채워지는 필드
        [System.NonSerialized] public string sourcePath;   // 템플릿 원본 경로
        [System.NonSerialized] public string installPath;  // 설치된 경로
        [System.NonSerialized] public bool isInstalled;
        [System.NonSerialized] public bool isCustom;       // 패키지 템플릿에 없는 유저 커스텀
    }
}
