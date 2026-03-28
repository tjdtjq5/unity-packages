using UnityEngine;

namespace Tjdtjq5.AddrX
{
    /// <summary>AddrX 패키지 전역 설정. Runtime에서 접근 가능, Editor에서 편집.</summary>
    public class AddrXSettings : ScriptableObject
    {
        const string ResourcePath = "AddrXSettings";

        static AddrXSettings _instance;

        [Header("로그")]
        [Tooltip("이 레벨 미만의 로그는 출력되지 않습니다.")]
        [SerializeField] LogLevel _logLevel = LogLevel.Info;

        [Header("디버깅")]
        [Tooltip("Handle Tracker 활성화 (Debug/Development 빌드)")]
        [SerializeField] bool _enableTracking = true;

        [Tooltip("Leak Detector 활성화 (씬 전환 시 미해제 핸들 경고)")]
        [SerializeField] bool _enableLeakDetection = true;

        [Header("초기화")]
        [Tooltip("RuntimeInitializeOnLoadMethod로 자동 초기화")]
        [SerializeField] bool _autoInitialize = true;

        public LogLevel LogLevel => _logLevel;
        public bool EnableTracking => _enableTracking;
        public bool EnableLeakDetection => _enableLeakDetection;
        public bool AutoInitialize => _autoInitialize;

        /// <summary>싱글톤 접근. Resources 폴더에서 로드, 없으면 기본값 인스턴스 생성.</summary>
        public static AddrXSettings Instance
        {
            get
            {
                if (_instance != null) return _instance;

                _instance = Resources.Load<AddrXSettings>(ResourcePath);
                if (_instance != null) return _instance;

                // 에디터 외부 또는 SO 미생성 시 기본값 폴백
                _instance = CreateInstance<AddrXSettings>();
                return _instance;
            }
        }

        /// <summary>설정을 AddrXLog에 반영한다.</summary>
        public void Apply()
        {
            AddrXLog.Level = _logLevel;
        }

        void OnEnable()
        {
            Apply();
        }

        void OnValidate()
        {
            Apply();
        }

#if UNITY_EDITOR
        /// <summary>에디터 전용: 지정된 경로에 SO를 저장한다. SettingsProvider에서 호출.</summary>
        public static AddrXSettings GetOrCreate()
        {
            // 디스크에 저장된 에셋이면 바로 반환 (인메모리 폴백은 무시)
            if (_instance != null && UnityEditor.AssetDatabase.Contains(_instance))
                return _instance;

            _instance = Resources.Load<AddrXSettings>(ResourcePath);
            if (_instance != null) return _instance;

            // 없으면 생성
            var dir = "Assets/AddrX/Resources";
            if (!System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            _instance = CreateInstance<AddrXSettings>();
            UnityEditor.AssetDatabase.CreateAsset(_instance, $"{dir}/{ResourcePath}.asset");
            UnityEditor.AssetDatabase.SaveAssets();
            return _instance;
        }
#endif
    }
}
