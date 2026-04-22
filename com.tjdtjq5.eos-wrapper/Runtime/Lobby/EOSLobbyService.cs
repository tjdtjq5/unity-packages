using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Epic.OnlineServices;
using Epic.OnlineServices.Lobby;
using PlayEveryWare.EpicOnlineServices;
using UnityEngine;

namespace Tjdtjq5.EOS.Lobby
{
    /// <summary>
    /// EOS Lobby SDK의 얇은 Awaitable 래퍼.
    /// 게임 규약(멤버 수, attribute 의미)은 모르며, 호출자가 <see cref="LobbyCreateRequest"/> /
    /// <see cref="LobbySearchCriteria"/> 를 통해 정책을 주입한다.
    ///
    /// 라이프사이클: VContainer Singleton. Dispose에서 notification 해제.
    /// EOSManager shutdown 경합을 피하기 위해 ExitingPlayMode / OnApplicationQuit 에서
    /// notification을 먼저 제거한다.
    /// </summary>
    public sealed class EOSLobbyService : IDisposable
    {
        LobbyInterface _lobby;
        ProductUserId _localUserId;

        LobbyInfo _currentLobby;
        ulong _lobbyUpdateNotifyId;
        ulong _memberStatusNotifyId;
        ulong _memberUpdateNotifyId;
        bool _notificationsRegistered;

        public LobbyInfo CurrentLobby => _currentLobby;

        /// <summary>로비 전체 attribute / 속성 변경 알림. 매개변수: 최신 <see cref="LobbyInfo"/>.</summary>
        public event Action<LobbyInfo> OnLobbyUpdated;

        /// <summary>멤버 상태 변경 알림. (targetUserId, status)</summary>
        public event Action<ProductUserId, LobbyMemberStatus> OnMemberStatusChanged;

        public EOSLobbyService()
        {
            Application.quitting += TryRemoveNotifications;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
#endif
        }

        public void Dispose()
        {
            TryRemoveNotifications();
            Application.quitting -= TryRemoveNotifications;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
#endif
        }

        // ───────── Public API ─────────

        public async Awaitable<LobbyInfo> CreateAsync(LobbyCreateRequest request)
        {
            if (request == null) { Debug.LogError("[EOSLobbyService] CreateAsync: request is null"); return null; }
            if (!EnsureBound()) return null;

            // 1. CreateLobby
            var createOptions = new CreateLobbyOptions
            {
                LocalUserId = _localUserId,
                MaxLobbyMembers = request.MaxMembers,
                PermissionLevel = LobbyPermissionLevel.Publicadvertised,
                BucketId = request.BucketId,
                PresenceEnabled = request.PresenceEnabled,
                AllowInvites = false,
                DisableHostMigration = request.DisableHostMigration,
                EnableJoinById = true,
                EnableRTCRoom = false,
            };

            var createTcs = new TaskCompletionSource<(Result result, string lobbyId)>();
            _lobby.CreateLobby(ref createOptions, null, (ref CreateLobbyCallbackInfo info) =>
            {
                createTcs.SetResult((info.ResultCode, info.LobbyId));
            });
            var created = await createTcs.Task;

            if (created.result != Result.Success || string.IsNullOrEmpty(created.lobbyId))
            {
                Debug.LogError($"[EOSLobbyService] CreateLobby failed: {created.result}");
                return null;
            }

            // 2. Attribute 세팅 (하나의 ModifyLobby 호출로 일괄 커밋)
            if (request.StringAttributes.Count > 0 || request.Int64Attributes.Count > 0)
            {
                var modOk = await ApplyAttributesAsync(created.lobbyId, request.StringAttributes, request.Int64Attributes);
                if (!modOk)
                {
                    Debug.LogError("[EOSLobbyService] CreateAsync: attribute 세팅 실패 — 로비는 생성됨, 계속 진행");
                }
            }

            // 3. LobbyInfo 복사 + notification 등록
            _currentLobby = CopyLobbyInfo(created.lobbyId);
            RegisterNotifications();
            return _currentLobby;
        }

        public async Awaitable<IReadOnlyList<LobbyInfo>> SearchAsync(LobbySearchCriteria criteria)
        {
            if (criteria == null) { Debug.LogError("[EOSLobbyService] SearchAsync: criteria is null"); return Array.Empty<LobbyInfo>(); }
            if (!EnsureBound()) return Array.Empty<LobbyInfo>();

            var createSearchOpts = new CreateLobbySearchOptions { MaxResults = criteria.MaxResults };
            var r = _lobby.CreateLobbySearch(ref createSearchOpts, out LobbySearch search);
            if (r != Result.Success || search == null)
            {
                Debug.LogError($"[EOSLobbyService] CreateLobbySearch failed: {r}");
                return Array.Empty<LobbyInfo>();
            }

            try
            {
                // 문자열 attribute 필터 (서버 사이드)
                // NOTE: BucketId는 EOS가 별도 Search 파라미터 API를 제공하지 않아 클라이언트 측에서 2차 필터링한다 (아래 loop).
                foreach (var (key, value) in criteria.RequireStringEquals)
                {
                    var pOpts = new LobbySearchSetParameterOptions
                    {
                        Parameter = new AttributeData
                        {
                            Key = key,
                            Value = new AttributeDataValue { AsUtf8 = value },
                        },
                        ComparisonOp = ComparisonOp.Equal,
                    };
                    var pr = search.SetParameter(ref pOpts);
                    if (pr != Result.Success)
                        Debug.LogWarning($"[EOSLobbyService] SetParameter({key}={value}) failed: {pr}");
                }

                // Find
                var findOpts = new LobbySearchFindOptions { LocalUserId = _localUserId };
                var findTcs = new TaskCompletionSource<Result>();
                search.Find(ref findOpts, null, (ref LobbySearchFindCallbackInfo info) =>
                {
                    findTcs.SetResult(info.ResultCode);
                });
                var findResult = await findTcs.Task;
                if (findResult != Result.Success && findResult != Result.NotFound)
                {
                    Debug.LogWarning($"[EOSLobbyService] LobbySearch.Find: {findResult}");
                    return Array.Empty<LobbyInfo>();
                }

                // 결과 수집
                var countOpts = new LobbySearchGetSearchResultCountOptions();
                uint count = search.GetSearchResultCount(ref countOpts);
                var results = new List<LobbyInfo>((int)count);
                for (uint i = 0; i < count; i++)
                {
                    var copyOpts = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                    var cr = search.CopySearchResultByIndex(ref copyOpts, out LobbyDetails details);
                    if (cr != Result.Success || details == null) continue;
                    try
                    {
                        var info = ReadDetails(details);
                        if (info == null) continue;
                        if (criteria.RequireAvailableSlot && info.AvailableSlots == 0) continue;
                        // BucketId는 서버 필터가 없으므로 클라이언트에서 한 번 더 체크
                        if (!string.IsNullOrEmpty(criteria.BucketId) &&
                            !string.Equals(info.BucketId, criteria.BucketId, StringComparison.Ordinal)) continue;
                        results.Add(info);
                    }
                    finally
                    {
                        details.Release();
                    }
                }
                return results;
            }
            finally
            {
                search.Release();
            }
        }

        public async Awaitable<LobbyInfo> JoinAsync(string lobbyId)
        {
            if (string.IsNullOrEmpty(lobbyId)) { Debug.LogError("[EOSLobbyService] JoinAsync: lobbyId is null/empty"); return null; }
            if (!EnsureBound()) return null;

            var joinOpts = new JoinLobbyByIdOptions
            {
                LobbyId = lobbyId,
                LocalUserId = _localUserId,
                PresenceEnabled = false,
                CrossplayOptOut = false,
            };

            var tcs = new TaskCompletionSource<Result>();
            _lobby.JoinLobbyById(ref joinOpts, null, (ref JoinLobbyByIdCallbackInfo info) =>
            {
                tcs.SetResult(info.ResultCode);
            });
            var result = await tcs.Task;
            if (result != Result.Success)
            {
                Debug.LogWarning($"[EOSLobbyService] JoinLobbyById failed: {result}");
                return null;
            }

            _currentLobby = CopyLobbyInfo(lobbyId);
            RegisterNotifications();
            return _currentLobby;
        }

        public async Awaitable<bool> LeaveAsync()
        {
            if (_currentLobby == null) return true;
            if (!EnsureBound()) return false;

            TryRemoveNotifications();

            var options = new LeaveLobbyOptions
            {
                LobbyId = _currentLobby.LobbyId,
                LocalUserId = _localUserId,
            };
            var tcs = new TaskCompletionSource<Result>();
            _lobby.LeaveLobby(ref options, null, (ref LeaveLobbyCallbackInfo info) =>
            {
                tcs.SetResult(info.ResultCode);
            });
            var result = await tcs.Task;
            _currentLobby = null;

            if (result != Result.Success && result != Result.NotFound)
            {
                Debug.LogWarning($"[EOSLobbyService] LeaveLobby: {result}");
                return false;
            }
            return true;
        }

        public Awaitable<bool> UpdateStringAttributeAsync(string key, string value)
        {
            if (_currentLobby == null) { Debug.LogError("[EOSLobbyService] UpdateAttribute: no current lobby"); return FalseAwaitable(); }
            var strMap = new Dictionary<string, string> { [key] = value };
            return ApplyAttributesAsync(_currentLobby.LobbyId, strMap, null);
        }

        public Awaitable<bool> UpdateInt64AttributeAsync(string key, long value)
        {
            if (_currentLobby == null) { Debug.LogError("[EOSLobbyService] UpdateAttribute: no current lobby"); return FalseAwaitable(); }
            var intMap = new Dictionary<string, long> { [key] = value };
            return ApplyAttributesAsync(_currentLobby.LobbyId, null, intMap);
        }

        // ───────── Internals ─────────

        bool EnsureBound()
        {
            if (EOSManager.Instance == null)
            {
                Debug.LogError("[EOSLobbyService] EOSManager.Instance is null");
                return false;
            }
            if (_lobby == null) _lobby = EOSManager.Instance.GetEOSLobbyInterface();
            if (_lobby == null)
            {
                Debug.LogError("[EOSLobbyService] GetEOSLobbyInterface() returned null");
                return false;
            }
            if (_localUserId == null || !_localUserId.IsValid())
                _localUserId = EOSManager.Instance.GetProductUserId();
            if (_localUserId == null || !_localUserId.IsValid())
            {
                Debug.LogError("[EOSLobbyService] Local ProductUserId is invalid. EOS Connect 로그인을 먼저 완료하세요.");
                return false;
            }
            return true;
        }

        async Awaitable<bool> ApplyAttributesAsync(string lobbyId, Dictionary<string, string> strAttrs, Dictionary<string, long> intAttrs)
        {
            if (!EnsureBound()) return false;

            var modOpts = new UpdateLobbyModificationOptions
            {
                LobbyId = lobbyId,
                LocalUserId = _localUserId,
            };
            var r = _lobby.UpdateLobbyModification(ref modOpts, out LobbyModification mod);
            if (r != Result.Success || mod == null)
            {
                Debug.LogError($"[EOSLobbyService] UpdateLobbyModification failed: {r}");
                return false;
            }

            try
            {
                if (strAttrs != null)
                {
                    foreach (var (key, value) in strAttrs)
                    {
                        var addOpts = new LobbyModificationAddAttributeOptions
                        {
                            Attribute = new AttributeData
                            {
                                Key = key,
                                Value = new AttributeDataValue { AsUtf8 = value },
                            },
                            Visibility = LobbyAttributeVisibility.Public,
                        };
                        var ar = mod.AddAttribute(ref addOpts);
                        if (ar != Result.Success) Debug.LogWarning($"[EOSLobbyService] AddAttribute({key}={value}) failed: {ar}");
                    }
                }
                if (intAttrs != null)
                {
                    foreach (var (key, value) in intAttrs)
                    {
                        var addOpts = new LobbyModificationAddAttributeOptions
                        {
                            Attribute = new AttributeData
                            {
                                Key = key,
                                Value = new AttributeDataValue { AsInt64 = value },
                            },
                            Visibility = LobbyAttributeVisibility.Public,
                        };
                        var ar = mod.AddAttribute(ref addOpts);
                        if (ar != Result.Success) Debug.LogWarning($"[EOSLobbyService] AddAttribute({key}={value}) failed: {ar}");
                    }
                }

                var updateOpts = new UpdateLobbyOptions { LobbyModificationHandle = mod };
                var tcs = new TaskCompletionSource<Result>();
                _lobby.UpdateLobby(ref updateOpts, null, (ref UpdateLobbyCallbackInfo info) =>
                {
                    tcs.SetResult(info.ResultCode);
                });
                var updateResult = await tcs.Task;
                if (updateResult != Result.Success)
                {
                    Debug.LogError($"[EOSLobbyService] UpdateLobby failed: {updateResult}");
                    return false;
                }

                // 로컬 캐시 갱신
                if (_currentLobby != null && _currentLobby.LobbyId == lobbyId)
                    _currentLobby = CopyLobbyInfo(lobbyId);
                return true;
            }
            finally
            {
                mod.Release();
            }
        }

        LobbyInfo CopyLobbyInfo(string lobbyId)
        {
            var copyOpts = new CopyLobbyDetailsHandleOptions { LobbyId = lobbyId, LocalUserId = _localUserId };
            var r = _lobby.CopyLobbyDetailsHandle(ref copyOpts, out LobbyDetails details);
            if (r != Result.Success || details == null)
            {
                Debug.LogError($"[EOSLobbyService] CopyLobbyDetailsHandle({lobbyId}) failed: {r}");
                return null;
            }
            try
            {
                return ReadDetails(details);
            }
            finally
            {
                details.Release();
            }
        }

        static LobbyInfo ReadDetails(LobbyDetails details)
        {
            var infoOpts = new LobbyDetailsCopyInfoOptions();
            var r = details.CopyInfo(ref infoOpts, out LobbyDetailsInfo? infoNullable);
            if (r != Result.Success || infoNullable == null) return null;
            var info = infoNullable.Value;

            // EOS SDK는 멤버에 따라 attribute key의 case를 다르게 반환한다
            // (생성자는 원본 유지, 참여자는 uppercase로 정규화).
            // 호출자가 "status"/"STATUS" 구분 없이 읽을 수 있도록 OrdinalIgnoreCase 사용.
            var strAttrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var intAttrs = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            var attrCountOpts = new LobbyDetailsGetAttributeCountOptions();
            uint attrCount = details.GetAttributeCount(ref attrCountOpts);
            for (uint i = 0; i < attrCount; i++)
            {
                var idxOpts = new LobbyDetailsCopyAttributeByIndexOptions { AttrIndex = i };
                var ar = details.CopyAttributeByIndex(ref idxOpts, out Epic.OnlineServices.Lobby.Attribute? attrNullable);
                if (ar != Result.Success || !attrNullable.HasValue) continue;
                var attr = attrNullable.Value;
                if (!attr.Data.HasValue) continue;
                var data = attr.Data.Value;
                string key = data.Key;
                if (string.IsNullOrEmpty(key)) continue;
                switch (data.Value.ValueType)
                {
                    case AttributeType.String:
                        strAttrs[key] = data.Value.AsUtf8;
                        break;
                    case AttributeType.Int64:
                        if (data.Value.AsInt64.HasValue) intAttrs[key] = data.Value.AsInt64.Value;
                        break;
                }
            }

            var members = new List<ProductUserId>();
            var mCountOpts = new LobbyDetailsGetMemberCountOptions();
            uint memberCount = details.GetMemberCount(ref mCountOpts);
            for (uint i = 0; i < memberCount; i++)
            {
                var midxOpts = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
                var puid = details.GetMemberByIndex(ref midxOpts);
                if (puid != null && puid.IsValid()) members.Add(puid);
            }

            return new LobbyInfo(
                info.LobbyId, info.LobbyOwnerUserId, info.MaxMembers, info.AvailableSlots,
                info.BucketId, strAttrs, intAttrs, members);
        }

        // ───────── Notifications ─────────

        void RegisterNotifications()
        {
            if (_notificationsRegistered) return;
            if (_lobby == null) return;

            var lobbyUpdateOpts = new AddNotifyLobbyUpdateReceivedOptions();
            _lobbyUpdateNotifyId = _lobby.AddNotifyLobbyUpdateReceived(ref lobbyUpdateOpts, null, OnLobbyUpdateReceived);

            var memberStatusOpts = new AddNotifyLobbyMemberStatusReceivedOptions();
            _memberStatusNotifyId = _lobby.AddNotifyLobbyMemberStatusReceived(ref memberStatusOpts, null, OnMemberStatusReceived);

            var memberUpdateOpts = new AddNotifyLobbyMemberUpdateReceivedOptions();
            _memberUpdateNotifyId = _lobby.AddNotifyLobbyMemberUpdateReceived(ref memberUpdateOpts, null, OnMemberUpdateReceived);

            _notificationsRegistered = true;
        }

        void OnLobbyUpdateReceived(ref LobbyUpdateReceivedCallbackInfo info)
        {
            if (_currentLobby == null || info.LobbyId != _currentLobby.LobbyId) return;
            _currentLobby = CopyLobbyInfo(info.LobbyId);
            OnLobbyUpdated?.Invoke(_currentLobby);
        }

        void OnMemberStatusReceived(ref LobbyMemberStatusReceivedCallbackInfo info)
        {
            if (_currentLobby == null || info.LobbyId != _currentLobby.LobbyId) return;
            _currentLobby = CopyLobbyInfo(info.LobbyId);
            OnMemberStatusChanged?.Invoke(info.TargetUserId, info.CurrentStatus);
            // 이미 떠난 멤버도 있을 수 있으므로 전체 스냅샷으로 UI 동기화
            OnLobbyUpdated?.Invoke(_currentLobby);
        }

        void OnMemberUpdateReceived(ref LobbyMemberUpdateReceivedCallbackInfo info)
        {
            if (_currentLobby == null || info.LobbyId != _currentLobby.LobbyId) return;
            _currentLobby = CopyLobbyInfo(info.LobbyId);
            OnLobbyUpdated?.Invoke(_currentLobby);
        }

        void TryRemoveNotifications()
        {
            if (!_notificationsRegistered) return;
            if (_lobby == null) { _notificationsRegistered = false; return; }
            if (EOSManager.Instance == null || EOSManager.Instance.GetEOSPlatformInterface() == null)
            {
                _notificationsRegistered = false;
                return;
            }

            // EOS SDK 내부 콜백 리스트에 같은 notificationId가 중복으로 들어간 상태(EOS SDK 버그/race)에서는
            // Helper.RemoveCallbackByNotificationId의 SingleOrDefault가 InvalidOperationException을 던진다.
            // 각 Remove를 독립 try/catch로 보호해서 예외가 상위 async 경로를 중단시키지 않도록 방어한다.
            SafeRemove(() => { if (_lobbyUpdateNotifyId != 0) _lobby.RemoveNotifyLobbyUpdateReceived(_lobbyUpdateNotifyId); }, "LobbyUpdate");
            SafeRemove(() => { if (_memberStatusNotifyId != 0) _lobby.RemoveNotifyLobbyMemberStatusReceived(_memberStatusNotifyId); }, "MemberStatus");
            SafeRemove(() => { if (_memberUpdateNotifyId != 0) _lobby.RemoveNotifyLobbyMemberUpdateReceived(_memberUpdateNotifyId); }, "MemberUpdate");

            _lobbyUpdateNotifyId = 0;
            _memberStatusNotifyId = 0;
            _memberUpdateNotifyId = 0;
            _notificationsRegistered = false;
        }

        static void SafeRemove(Action remove, string label)
        {
            try { remove(); }
            catch (Exception e)
            {
                Debug.LogWarning($"[EOSLobbyService] RemoveNotify({label}) 예외(무시): {e.GetType().Name}: {e.Message}");
            }
        }

#if UNITY_EDITOR
        void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                TryRemoveNotifications();
        }
#endif

        // ───────── Helpers ─────────

        static Awaitable<bool> FalseAwaitable()
        {
            var src = new AwaitableCompletionSource<bool>();
            src.SetResult(false);
            return src.Awaitable;
        }
    }
}
