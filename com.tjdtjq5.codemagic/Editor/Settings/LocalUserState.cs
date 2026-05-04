#if UNITY_EDITOR
using System;

namespace Tjdtjq5.Codemagic.Editor.Settings
{
    /// <summary>
    /// Layer 2 — per-user / per-machine 비-시크릿 상태 POCO.
    /// 직렬화 대상: Library/codemagic-setup.json (gitignored).
    /// 위저드 진행 위치 / 라이선스 메타 / keystore 경로 등.
    /// </summary>
    [Serializable]
    public sealed class LocalUserState
    {
        /// <summary>위저드 현재 단계 인덱스 (0-based).</summary>
        public int CurrentStep = 0;

        /// <summary>.ulf 라이선스 만료일 (예: "2026-06-03"). 미동기 시 null/빈 문자열.</summary>
        public string LicenseStopDate = null;

        /// <summary>Unity 시리얼 마스킹 표기 (예: "F4-...-XXXX"). 평문 시리얼은 저장하지 않음.</summary>
        public string UnitySerialMasked = null;

        /// <summary>로컬 keystore 파일 절대 경로. Codemagic upload 후에도 로컬 참조용.</summary>
        public string KeystorePath = null;

        /// <summary>최근 성공 빌드 ID (Codemagic build id). 없으면 null.</summary>
        public string LastSuccessBuildId = null;

        /// <summary>Step 4 셀프 체크 — Codemagic GUI에 unity_credentials 4개 변수 등록 완료.</summary>
        /// <remarks>REST API로 검증 불가 — 사용자가 GUI 작업 후 직접 표시.</remarks>
        public bool LicenseEnvRegistered = false;

        /// <summary>Step 5 셀프 체크 — Codemagic GUI에 android_keystore 4개 변수 등록 완료.</summary>
        public bool KeystoreEnvRegistered = false;

        /// <summary>마지막 저장 시각 (UTC).</summary>
        public DateTime LastSavedAt;
    }
}
#endif
