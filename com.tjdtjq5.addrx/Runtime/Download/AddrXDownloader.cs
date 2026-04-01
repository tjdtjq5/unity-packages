using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Tjdtjq5.AddrX
{
    /// <summary>
    /// 원격 번들 다운로드 관리. 큐 기반 순차 다운로드 + 진행률 + 재시도.
    /// 로컬 전용 프로젝트에서는 사용하지 않음.
    /// </summary>
    public class AddrXDownloader
    {
        const string Tag = "Downloader";
        const int DefaultMaxRetries = 3;
        const int RetryBaseDelayMs = 1000;

        readonly List<string> _keys = new();
        int _maxRetries = DefaultMaxRetries;

        Action<DownloadProgress> _onProgress;
        Action _onComplete;
        Action<string> _onError;

        /// <summary>다운로드 대상 라벨/키를 추가한다.</summary>
        public AddrXDownloader Add(params string[] keys)
        {
            _keys.AddRange(keys);
            return this;
        }

        /// <summary>최대 재시도 횟수를 설정한다. 기본 3회.</summary>
        public AddrXDownloader WithRetry(int maxRetries)
        {
            _maxRetries = maxRetries;
            return this;
        }

        /// <summary>진행률 콜백을 등록한다.</summary>
        public AddrXDownloader OnProgress(Action<DownloadProgress> callback)
        {
            _onProgress = callback;
            return this;
        }

        /// <summary>전체 완료 콜백을 등록한다.</summary>
        public AddrXDownloader OnComplete(Action callback)
        {
            _onComplete = callback;
            return this;
        }

        /// <summary>에러 콜백을 등록한다.</summary>
        public AddrXDownloader OnError(Action<string> callback)
        {
            _onError = callback;
            return this;
        }

        /// <summary>등록된 키의 전체 다운로드 크기를 바이트로 반환한다.</summary>
        public async Task<long> GetTotalSizeAsync()
        {
            long total = 0;
            foreach (var key in _keys)
            {
                var op = Addressables.GetDownloadSizeAsync(key);
                await op.Task;
                if (op.Status == AsyncOperationStatus.Succeeded)
                    total += op.Result;
                Addressables.Release(op);
            }
            return total;
        }

        /// <summary>다운로드를 시작한다. 키 순서대로 순차 다운로드.</summary>
        public async Task StartAsync()
        {
            if (_keys.Count == 0)
            {
                AddrXLog.Warning(Tag, "다운로드할 키가 없습니다.");
                _onComplete?.Invoke();
                return;
            }

            AddrXLog.Info(Tag, $"다운로드 시작: {_keys.Count}개 키");

            int completed = 0;
            int failed = 0;

            for (int i = 0; i < _keys.Count; i++)
            {
                var key = _keys[i];
                bool success = await DownloadWithRetry(key, i);

                if (success)
                    completed++;
                else
                    failed++;

                _onProgress?.Invoke(new DownloadProgress(
                    i + 1, _keys.Count, key, success));
            }

            if (failed > 0)
            {
                var msg = $"다운로드 완료: {completed}개 성공, {failed}개 실패";
                AddrXLog.Warning(Tag, msg);
                _onError?.Invoke(msg);
            }
            else
            {
                AddrXLog.Info(Tag, $"다운로드 완료: {completed}개 전부 성공");
            }

            _onComplete?.Invoke();
        }

        async Task<bool> DownloadWithRetry(string key, int index)
        {
            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                var sizeOp = Addressables.GetDownloadSizeAsync(key);
                await sizeOp.Task;
                long size = sizeOp.Status == AsyncOperationStatus.Succeeded ? sizeOp.Result : 0;
                Addressables.Release(sizeOp);

                // 다운로드 필요 없음 (이미 캐시됨)
                if (size <= 0)
                {
                    AddrXLog.Verbose(Tag, $"[{index + 1}/{_keys.Count}] '{key}' — 이미 캐시됨");
                    return true;
                }

                if (attempt > 0)
                    AddrXLog.Info(Tag, $"[{index + 1}/{_keys.Count}] '{key}' — 재시도 {attempt}/{_maxRetries}");

                var op = Addressables.DownloadDependenciesAsync(key);
                await op.Task;

                if (op.Status == AsyncOperationStatus.Succeeded)
                {
                    Addressables.Release(op);
                    AddrXLog.Verbose(Tag,
                        $"[{index + 1}/{_keys.Count}] '{key}' — 다운로드 완료 ({FormatSize(size)})");
                    return true;
                }

                var error = op.OperationException?.Message ?? "알 수 없는 오류";
                Addressables.Release(op);

                if (attempt < _maxRetries)
                {
                    AddrXLog.Warning(Tag,
                        $"[{index + 1}/{_keys.Count}] '{key}' — 실패: {error}, 재시도 예정");
                    await Task.Delay(RetryBaseDelayMs * (attempt + 1));
                }
                else
                {
                    AddrXLog.Error(Tag,
                        $"[{index + 1}/{_keys.Count}] '{key}' — 최종 실패: {error}");
                    _onError?.Invoke($"'{key}' 다운로드 실패: {error}");
                }
            }

            return false;
        }

        static string FormatSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F0}KB";
            return $"{bytes / (1024f * 1024f):F1}MB";
        }
    }

    /// <summary>개별 키 다운로드 진행 정보.</summary>
    public readonly struct DownloadProgress
    {
        /// <summary>현재까지 처리된 키 수.</summary>
        public readonly int Current;
        /// <summary>전체 키 수.</summary>
        public readonly int Total;
        /// <summary>현재 처리 중인 키.</summary>
        public readonly string Key;
        /// <summary>현재 키 성공 여부.</summary>
        public readonly bool Success;
        /// <summary>전체 진행률 (0~1).</summary>
        public float Percent => (float)Current / Total;

        public DownloadProgress(int current, int total, string key, bool success)
        {
            Current = current;
            Total = total;
            Key = key;
            Success = success;
        }
    }
}
