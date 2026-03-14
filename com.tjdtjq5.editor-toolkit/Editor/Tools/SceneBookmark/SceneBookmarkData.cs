using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.EditorToolkit.Editor.Tools
{
    [Serializable]
    public class SceneBookmarkEntry
    {
        public string scenePath;
        public string displayName;
    }

    [Serializable]
    public class SceneBookmarkData
    {
        const string PrefsKey = "EditorToolkit_SceneBookmarks";

        [SerializeField] List<SceneBookmarkEntry> entries = new();

        public IReadOnlyList<SceneBookmarkEntry> Entries => entries;

        // --- 저장/로드 ---

        public static SceneBookmarkData Load()
        {
            var json = EditorPrefs.GetString(PrefsKey, "{}");
            var data = JsonUtility.FromJson<SceneBookmarkData>(json) ?? new SceneBookmarkData();
            data.Sanitize();
            return data;
        }

        public void Save()
        {
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
        }

        // --- 조작 ---

        /// <summary>씬 추가. 이미 존재하면 false 반환.</summary>
        public bool Add(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath)) return false;
            if (entries.Exists(e => e.scenePath == scenePath)) return false;

            entries.Add(new SceneBookmarkEntry
            {
                scenePath = scenePath,
                displayName = Path.GetFileNameWithoutExtension(scenePath)
            });
            Save();
            return true;
        }

        /// <summary>씬 제거.</summary>
        public bool Remove(string scenePath)
        {
            var removed = entries.RemoveAll(e => e.scenePath == scenePath) > 0;
            if (removed) Save();
            return removed;
        }

        /// <summary>순서 변경 (fromIndex → toIndex).</summary>
        public void Reorder(int fromIndex, int toIndex)
        {
            if (fromIndex < 0 || fromIndex >= entries.Count) return;
            toIndex = Mathf.Clamp(toIndex, 0, entries.Count - 1);
            if (fromIndex == toIndex) return;

            var item = entries[fromIndex];
            entries.RemoveAt(fromIndex);
            entries.Insert(toIndex, item);
            Save();
        }

        // --- 내부 ---

        /// <summary>존재하지 않는 씬 경로 자동 제거.</summary>
        void Sanitize()
        {
            var dirty = entries.RemoveAll(e =>
                string.IsNullOrEmpty(e.scenePath) || !File.Exists(e.scenePath)) > 0;
            if (dirty) Save();
        }
    }
}
