#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

namespace Tjdtjq5.Codemagic.Editor.Settings
{
    /// <summary>
    /// Layer 2 영속화 IO — Library/codemagic-setup.json 읽기/쓰기.
    /// Library/는 Unity 기본 .gitignore에 포함 → git 노출 차단.
    /// </summary>
    public static class LocalUserStateStore
    {
        // 파일명은 docs/CODEMAGIC_PACKAGE.md의 Layer 2 명세 기준.
        // 추후 Util/PlatformPaths.SetupStateFile 으로 이동 예정 (현재는 inline).
        const string FileName = "codemagic-setup.json";

        /// <summary>
        /// Library/codemagic-setup.json 절대 경로. Library 폴더 자체는 Unity가 보장.
        /// </summary>
        static string FilePath
        {
            get
            {
                // Application.dataPath = ".../<Project>/Assets" → 부모는 프로젝트 루트.
                var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
                return Path.Combine(projectRoot, "Library", FileName);
            }
        }

        /// <summary>
        /// 디스크에서 상태를 로드. 파일 없거나 파싱 실패 시 빈 인스턴스 반환.
        /// </summary>
        public static LocalUserState Load()
        {
            var path = FilePath;
            if (!File.Exists(path))
                return new LocalUserState();

            try
            {
                var json = File.ReadAllText(path);
                var state = JsonUtility.FromJson<LocalUserState>(json);
                return state ?? new LocalUserState();
            }
            catch (Exception ex)
            {
                // 파싱 실패는 치명적이지 않음 — 빈 인스턴스로 복구.
                // 시크릿 미포함 파일이므로 경고 로그 OK.
                Debug.LogWarning($"[Codemagic] LocalUserState 파싱 실패, 빈 인스턴스로 복구: {ex.Message}");
                return new LocalUserState();
            }
        }

        /// <summary>
        /// 상태를 디스크에 저장. LastSavedAt를 UTC now로 갱신. Library 폴더 없으면 생성.
        /// </summary>
        public static void Save(LocalUserState state)
        {
            if (state == null) return;

            state.LastSavedAt = DateTime.UtcNow;

            var path = FilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonUtility.ToJson(state, prettyPrint: true);
            File.WriteAllText(path, json);
        }
    }
}
#endif
