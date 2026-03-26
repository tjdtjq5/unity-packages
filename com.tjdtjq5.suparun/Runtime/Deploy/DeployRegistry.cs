using System;
using System.Collections.Generic;
using UnityEngine;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 배포된 엔드포인트 목록 관리.
    /// 배포 시 목록에 추가, 호출 시 IsDeployed로 확인.
    /// </summary>
    public static class DeployRegistry
    {
        static HashSet<string> _deployed;

        // PlayerPrefs에 JSON으로 저장 (Editor + Build 공통)
        const string PrefsKey = "SupaRun_DeployedEndpoints";

        /// <summary>해당 엔드포인트가 배포되었는지 확인.</summary>
        public static bool IsDeployed(string endpoint)
        {
            EnsureLoaded();
            return _deployed.Contains(endpoint);
        }

        /// <summary>배포 시 호출. 엔드포인트 목록을 배포됨으로 등록.</summary>
        public static void MarkDeployed(string[] endpoints)
        {
            EnsureLoaded();
            foreach (var ep in endpoints)
                _deployed.Add(ep);
            Save();
        }

        /// <summary>전체 배포 목록 반환.</summary>
        public static string[] GetDeployedEndpoints()
        {
            EnsureLoaded();
            var arr = new string[_deployed.Count];
            _deployed.CopyTo(arr);
            return arr;
        }

        /// <summary>배포 목록 초기화 (테스트용).</summary>
        public static void Clear()
        {
            _deployed = new HashSet<string>();
            Save();
        }

        static void EnsureLoaded()
        {
            if (_deployed != null) return;

            var json = PlayerPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var data = JsonUtility.FromJson<DeployData>(json);
                    _deployed = new HashSet<string>(data.endpoints ?? Array.Empty<string>());
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[SupaRun] DeployRegistry 로드 실패: {ex.Message}");
                    _deployed = new HashSet<string>();
                }
            }
            else
            {
                _deployed = new HashSet<string>();
            }
        }

        static void Save()
        {
            var data = new DeployData();
            data.endpoints = new string[_deployed.Count];
            _deployed.CopyTo(data.endpoints);
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        [Serializable]
        class DeployData
        {
            public string[] endpoints;
        }
    }
}
