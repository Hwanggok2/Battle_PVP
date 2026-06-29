using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using System.Collections.Generic;
using BattlePvp.Stats;
using BattlePvp.Managers;
using System;
using System.Text;
using Mirror;

namespace BattlePvp.Networking
{
    /// <summary>
    /// PlayFab??Shared Group Data瑜??댁슜??諛⑷?由?諛??좎? ?곗씠?곕? ?숆린?뷀븯???대옒?ㅼ엯?덈떎.
    /// </summary>
    public class PlayFabBattleManager : MonoBehaviour
    {
        public static PlayFabBattleManager Instance { get; private set; }

        private const string ROOM_REGISTRY_ID = "GLOBAL_ROOM_REGISTRY"; // 諛?紐⑸줉????ν븷 怨듭슜 洹몃９ ID
        private const string ROOM_STARTED_KEY = "IsStarted";
        private const string ROOM_PLAYER_COUNT_KEY = "PlayerCount";
        private const string ROOM_MASTER_NAME_KEY = "MasterName";
        private const string ROOM_RELAY_JOIN_CODE_KEY = "RelayJoinCode";
        private const string REGISTER_ROOM_FUNCTION = "RegisterRoomToRegistry";
        private const string GET_ACTIVE_ROOMS_FUNCTION = "GetActiveRooms";
        private const string GET_ACTIVE_ROOM_INFOS_FUNCTION = "GetActiveRoomInfos";
        private const string JOIN_ROOM_FUNCTION = "JoinRoom";
        private const string LEAVE_ROOM_FUNCTION = "LeaveRoom";
        private const string UPDATE_ROOM_RELAY_JOIN_CODE_FUNCTION = "UpdateRoomRelayJoinCode";
        private const string ADMIN_VALIDATE_ROOM_KEY_FUNCTION = "AdminValidateRoomKey";
        private const string ADMIN_DELETE_ROOM_FUNCTION = "AdminDeleteRoom";
        private const string ADMIN_CLEAR_ROOM_REGISTRY_FUNCTION = "AdminClearRoomRegistry";
        private const string CLOUDSCRIPT_NOT_FOUND = "CloudScriptNotFound";

        public struct RoomInfo
        {
            public RoomInfo(string roomName, string masterName, int playerCount, string relayJoinCode = "")
            {
                RoomName = roomName;
                MasterName = masterName;
                PlayerCount = playerCount;
                RelayJoinCode = relayJoinCode ?? string.Empty;
            }

            public string RoomName { get; private set; }
            public string MasterName { get; private set; }
            public int PlayerCount { get; private set; }
            public string RelayJoinCode { get; private set; }
        }

        [Header("Scene Settings")]
        [SerializeField] private string _waitSceneName = "Battle_wait"; // ?몄뒪?숉꽣?먯꽌 ?ㅼ젙 媛??

        public event Action<Dictionary<string, string>> OnRoomListLoaded;
        public event Action<Dictionary<string, RoomInfo>> OnRoomInfoListLoaded;
        public event Action OnRoomRegistryChanged;
        public event Action OnRoomJoined;

        private bool _isHost = false; // 諛⑹옣 ?щ? ?뺤씤 ?뚮옒洹?

        private string _ownedRoomId;
        private string _joinedRoomId;
        private bool _hasJoinedRoomCount;
        private RoomInfo _currentRoomInfo;
        private readonly Dictionary<string, string> _knownRooms = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _knownRoomMasters = new Dictionary<string, string>();
        private readonly Dictionary<string, int> _knownRoomCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _knownRoomRelayJoinCodes = new Dictionary<string, string>();
        private readonly Dictionary<string, RoomInfo> _lastLoadedRoomInfos = new Dictionary<string, RoomInfo>();
        private bool _clientStatisticUpdatesDisabled;

        public string CurrentRoomId => _joinedRoomId;
        public RoomInfo CurrentRoomInfo => _currentRoomInfo;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                
                // 諛??낆옣???깃났?섎㈃ ???꾪솚 ?대깽???곌껐
                OnRoomJoined += HandleRoomJoinSuccess;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private async void HandleRoomJoinSuccess()
        {
            if (NetworkManager.singleton == null)
            {
                Debug.LogError("[PlayFabManager] NetworkManager.singleton is missing! Mirror setup required.");
                return;
            }

            var relayTransport = EnsureRelayTransport();
            if (relayTransport == null)
                return;

            if (_isHost)
            {
                await StartRelayHostAsync(relayTransport);
            }
            else
            {
                await StartRelayClientAsync(relayTransport);
            }
        }

        private UnityRelayTransport EnsureRelayTransport()
        {
            var networkManager = NetworkManager.singleton;
            var relayTransport = networkManager.GetComponent<UnityRelayTransport>();
            if (relayTransport == null)
                relayTransport = networkManager.gameObject.AddComponent<UnityRelayTransport>();

            networkManager.transport = relayTransport;
            Transport.active = relayTransport;
            return relayTransport;
        }

        private async System.Threading.Tasks.Task StartRelayHostAsync(UnityRelayTransport relayTransport)
        {
            try
            {
                Debug.Log($"[PlayFabManager] Creating Unity Relay allocation for Room: {_waitSceneName}");
                string joinCode = await relayTransport.PrepareHostAsync(NetworkManager.singleton.maxConnections);
                Debug.Log($"[PlayFabManager] Unity Relay host prepared. JoinCode={joinCode}");

                bool updated = await UpdateCurrentRoomRelayJoinCodeAsync(joinCode);
                if (!updated)
                    throw new InvalidOperationException("Failed to publish Relay join code to PlayFab.");

                NetworkManager.singleton.StartHost();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayFabManager] Failed to start Relay host: {ex}");
                LeaveCurrentRoom();
            }
        }

        private async System.Threading.Tasks.Task StartRelayClientAsync(UnityRelayTransport relayTransport)
        {
            try
            {
                string joinCode = _currentRoomInfo.RelayJoinCode;
                if (string.IsNullOrWhiteSpace(joinCode))
                {
                    await RefreshCurrentRoomInfoAsync();
                    joinCode = _currentRoomInfo.RelayJoinCode;
                }

                if (string.IsNullOrWhiteSpace(joinCode))
                    throw new InvalidOperationException("Room does not have a Relay join code yet.");

                Debug.Log($"[PlayFabManager] Joining Unity Relay allocation. JoinCode={joinCode}");
                await relayTransport.PrepareClientAsync(joinCode);

                NetworkManager.singleton.networkAddress = "relay";
                NetworkManager.singleton.StartClient();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayFabManager] Failed to start Relay client: {ex}");
                LeaveCurrentRoom();
            }
        }

        private System.Threading.Tasks.Task RefreshCurrentRoomInfoAsync()
        {
            var completion = new System.Threading.Tasks.TaskCompletionSource<bool>();
            RefreshCurrentRoomInfo(_ => completion.TrySetResult(true));
            return completion.Task;
        }

        private System.Threading.Tasks.Task<bool> UpdateCurrentRoomRelayJoinCodeAsync(string joinCode)
        {
            var completion = new System.Threading.Tasks.TaskCompletionSource<bool>();
            UpdateCurrentRoomRelayJoinCode(joinCode, success => completion.TrySetResult(success));
            return completion.Task;
        }

        #region [Room Management]

        /// <summary>
        /// ?덈줈??諛⑹쓣 ?앹꽦?섍퀬 湲濡쒕쾶 ?덉??ㅽ듃由ъ뿉 ?깅줉?⑸땲??
        /// </summary>
        public void CreateRoom(string roomName)
        {
            LeaveJoinedRoomBeforeSwitch(null);

            _isHost = true; // 留뚮뱶???щ엺???몄뒪?멸? ?⑸땲??
            string roomId = Guid.NewGuid().ToString("N"); // ?곷Ц???レ옄濡쒕쭔 援ъ꽦???덉쟾???앸퀎??
            _ownedRoomId = roomId;
            _joinedRoomId = roomId;
            _hasJoinedRoomCount = true;
            string masterName = GetCurrentPlayerNickname();
            _knownRooms[roomId] = roomName;
            _knownRoomMasters[roomId] = masterName;
            _knownRoomCounts[roomId] = 1;
            _knownRoomRelayJoinCodes[roomId] = string.Empty;
            _currentRoomInfo = new RoomInfo(roomName, masterName, 1, string.Empty);
            OnRoomRegistryChanged?.Invoke();
            var request = new CreateSharedGroupRequest
            {
                SharedGroupId = roomId 
            };

            PlayFabClientAPI.CreateSharedGroup(request, 
                result => {
                    Debug.Log($"諛??앹꽦 ?깃났: {roomName} (ID: {roomId})");
                    UpdateRoomData(roomId, ROOM_STARTED_KEY, "false");
                    UpdateRoomData(roomId, ROOM_PLAYER_COUNT_KEY, "1");
                    UpdateRoomData(roomId, ROOM_MASTER_NAME_KEY, masterName);
                    
                    // 湲濡쒕쾶 ?덉??ㅽ듃由ъ뿉 ??諛⑹쓽 怨좎쑀 ID? ?ㅼ젣 ?쒕ぉ???띿쑝濡??깅줉?⑸땲??
                    RegisterRoomToRegistry(roomId, roomName, masterName);
                }, 
                error => Debug.LogError($"諛??앹꽦 ?ㅽ뙣: {error.GenerateErrorReport()}")
            );
        }

        private void JoinRoomThroughCloudScript(string roomId)
        {
            LeaveJoinedRoomBeforeSwitch(roomId);
            _isHost = !string.IsNullOrEmpty(_ownedRoomId) && roomId == _ownedRoomId;

            var parameters = new Dictionary<string, object>
            {
                { "roomId", roomId }
            };

            PlayFabClientAPI.ExecuteCloudScript(
                new ExecuteCloudScriptRequest
                {
                    FunctionName = JOIN_ROOM_FUNCTION,
                    FunctionParameter = parameters,
                    GeneratePlayStreamEvent = false
                },
                result =>
                {
                    if (result.Error != null)
                    {
                        Debug.LogError($"[PlayFab] CloudScript room join failed: {FormatCloudScriptError(result)}");
                        return;
                    }

                    RoomInfo joinedInfo = ParseRoomInfoFromCloudScript(result.FunctionResult, roomId);
                    _joinedRoomId = roomId;
                    _hasJoinedRoomCount = true;
                    _knownRoomCounts[roomId] = joinedInfo.PlayerCount;
                    _knownRoomMasters[roomId] = joinedInfo.MasterName;
                    _knownRooms[roomId] = joinedInfo.RoomName;
                    _knownRoomRelayJoinCodes[roomId] = joinedInfo.RelayJoinCode;
                    _currentRoomInfo = joinedInfo;

                    Debug.Log($"[PlayFab] Joined room through CloudScript: {roomId}");
                    OnRoomRegistryChanged?.Invoke();
                    OnRoomJoined?.Invoke();
                },
                error => Debug.LogError($"[PlayFab] CloudScript room join request failed: {error.GenerateErrorReport()}")
            );
        }

        private RoomInfo ParseRoomInfoFromCloudScript(object functionResult, string roomId)
        {
            if (functionResult is IDictionary<string, object> resultDict &&
                resultDict.TryGetValue("roomInfo", out object roomInfoObject) &&
                roomInfoObject is IDictionary<string, object> infoDict)
            {
                return new RoomInfo(
                    GetStringValue(infoDict, "roomName", GetKnownRoomName(roomId)),
                    GetStringValue(infoDict, "masterName", GetKnownRoomMasterName(roomId)),
                    GetIntValue(infoDict, "playerCount", GetKnownRoomCount(roomId)),
                    GetStringValue(infoDict, "relayJoinCode", GetKnownRoomRelayJoinCode(roomId)));
            }

            return new RoomInfo(
                GetKnownRoomName(roomId),
                GetKnownRoomMasterName(roomId),
                GetKnownRoomCount(roomId),
                GetKnownRoomRelayJoinCode(roomId));
        }

        /// <summary>
        /// 紐⑤뱺 ?뚮젅?댁뼱媛 蹂????덈뒗 怨듭슜 洹몃９???꾩옱 諛??뺣낫瑜?異붽??⑸땲??
        /// </summary>
        private void RegisterRoomToRegistry(string roomId, string roomName, string masterName)
        {
            var parameters = new Dictionary<string, object>
            {
                { "roomId", roomId },
                { "roomName", roomName },
                { "masterName", masterName }
            };

            PlayFabClientAPI.ExecuteCloudScript(
                new ExecuteCloudScriptRequest
                {
                    FunctionName = REGISTER_ROOM_FUNCTION,
                    FunctionParameter = parameters,
                    GeneratePlayStreamEvent = false
                },
                result =>
                {
                    if (result.Error != null)
                    {
                        Debug.LogError($"[PlayFab] CloudScript room registry update failed: {FormatCloudScriptError(result)}");
                        TryRegisterRoomToRegistryWithClientApi(roomId, roomName);
                        return;
                    }

                    Debug.Log($"[PlayFab] Room '{roomName}' registered through CloudScript. Result={SerializeForLog(result.FunctionResult)}");
                    OnRoomRegistryChanged?.Invoke();
                    OnRoomJoined?.Invoke();
                },
                error =>
                {
                    Debug.LogError($"[PlayFab] CloudScript room registry request failed: {error.GenerateErrorReport()}");
                    TryRegisterRoomToRegistryWithClientApi(roomId, roomName);
                }
            );
        }

        private void TryRegisterRoomToRegistryWithClientApi(string roomId, string roomName)
        {
            Debug.LogWarning("[PlayFab] Falling back to client SharedGroup room registry. Deploy combinedCloudScript.js to fix cross-client room sharing reliably.");

            EnsureCurrentPlayerInSharedGroup(
                ROOM_REGISTRY_ID,
                () => UpdateRoomRegistryData(roomId, roomName),
                error =>
                {
                    if (IsSharedGroupNotFound(error))
                    {
                        CreateRegistryAndRegister(roomId, roomName);
                        return;
                    }

                    Debug.LogError($"[PlayFab] Client room registry fallback failed. Other clients may not see this room: {error.GenerateErrorReport()}");
                });
        }

        private void UpdateRoomRegistryData(string roomId, string roomName)
        {
            var request = new UpdateSharedGroupDataRequest
            {
                SharedGroupId = ROOM_REGISTRY_ID,
                Data = new Dictionary<string, string> { { roomId, roomName } }
            };

            PlayFabClientAPI.UpdateSharedGroupData(request,
                result => {
                    Debug.Log($"湲濡쒕쾶 ?덉??ㅽ듃由ъ뿉 諛?'{roomName}' ?깅줉 ?꾨즺");
                    OnRoomRegistryChanged?.Invoke();
                    OnRoomJoined?.Invoke();
                },
                error => {
                    // ?덉??ㅽ듃由?洹몃９???놁쑝硫??앹꽦 ?쒕룄 (理쒖큹 1??
                    if (IsSharedGroupNotFound(error))
                    {
                        CreateRegistryAndRegister(roomId, roomName);
                    }
                    else
                    {
                        Debug.LogError($"[PlayFab] Room registry update failed. Other clients will not see this room: {error.GenerateErrorReport()}");
                    }
                }
            );
        }

        private void CreateRegistryAndRegister(string roomId, string roomName)
        {
            PlayFabClientAPI.CreateSharedGroup(new CreateSharedGroupRequest { SharedGroupId = ROOM_REGISTRY_ID },
                result => UpdateRoomRegistryData(roomId, roomName),
                error => {
                    Debug.LogError($"[PlayFab] Room registry creation failed. Other clients will not see this room: {error.GenerateErrorReport()}");
                }
            );
        }

        private void EnsureCurrentPlayerInSharedGroup(string groupId, Action onComplete, Action<PlayFabError> onError)
        {
            PlayFabClientAPI.GetAccountInfo(new GetAccountInfoRequest(),
                accountResult =>
                {
                    string playFabId = accountResult.AccountInfo.PlayFabId;
                    var addRequest = new AddSharedGroupMembersRequest
                    {
                        SharedGroupId = groupId,
                        PlayFabIds = new List<string> { playFabId }
                    };

                    PlayFabClientAPI.AddSharedGroupMembers(addRequest,
                        _ => onComplete?.Invoke(),
                        error =>
                        {
                            if (IsAlreadySharedGroupMember(error))
                            {
                                onComplete?.Invoke();
                                return;
                            }

                            onError?.Invoke(error);
                        });
                },
                onError);
        }

        private static bool IsSharedGroupNotFound(PlayFabError error)
        {
            return error != null && (error.Error.ToString().Contains("SharedGroupNotFound") || (int)error.Error == 1088);
        }

        private static bool IsAlreadySharedGroupMember(PlayFabError error)
        {
            return error != null && error.GenerateErrorReport().IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// ?꾩옱 媛쒖꽕??諛?紐⑸줉??媛?몄샃?덈떎. (Key: RoomID, Value: RoomName)
        /// </summary>
        public void GetActiveRooms(Action<Dictionary<string, string>> callback)
        {
            if (UseCloudScriptRoomRegistry())
            {
                GetActiveRoomsFromCloudScript(callback);
                return;
            }

            var request = new GetSharedGroupDataRequest { SharedGroupId = ROOM_REGISTRY_ID };

            PlayFabClientAPI.GetSharedGroupData(request,
                result => {
                    Dictionary<string, string> rooms = new Dictionary<string, string>();
                    if (result.Data != null)
                    {
                        foreach (var kv in result.Data)
                        {
                            // Value媛 null???꾨땲硫??ы븿
                            if (kv.Value != null) rooms[kv.Key] = kv.Value.Value;
                        }
                    }
                    foreach (var kv in _knownRooms)
                    {
                        rooms[kv.Key] = kv.Value;
                    }
                    OnRoomListLoaded?.Invoke(rooms);
                    callback?.Invoke(rooms);
                },
                error => {
                    Debug.LogWarning("諛?紐⑸줉??媛?몄삱 ???녾굅??紐⑸줉??鍮꾩뼱 ?덉뒿?덈떎.");
                    var rooms = new Dictionary<string, string>(_knownRooms);
                    OnRoomListLoaded?.Invoke(rooms);
                    callback?.Invoke(rooms);
                }
            );
        }

        private void GetActiveRoomsFromCloudScript(Action<Dictionary<string, string>> callback)
        {
            PlayFabClientAPI.ExecuteCloudScript(
                new ExecuteCloudScriptRequest
                {
                    FunctionName = GET_ACTIVE_ROOMS_FUNCTION,
                    GeneratePlayStreamEvent = false
                },
                result =>
                {
                    Dictionary<string, string> rooms = new Dictionary<string, string>();
                    if (result.Error != null)
                    {
                        Debug.LogError($"[PlayFab] CloudScript room list load failed: {result.Error.Message}");
                    }
                    else
                    {
                        rooms = ParseRoomListFromCloudScript(result.FunctionResult);
                    }

                    foreach (var kv in _knownRooms)
                    {
                        rooms[kv.Key] = kv.Value;
                    }

                    OnRoomListLoaded?.Invoke(rooms);
                    callback?.Invoke(rooms);
                },
                error =>
                {
                    Debug.LogWarning($"[PlayFab] CloudScript room list request failed: {error.GenerateErrorReport()}");
                    var rooms = new Dictionary<string, string>(_knownRooms);
                    OnRoomListLoaded?.Invoke(rooms);
                    callback?.Invoke(rooms);
                }
            );
        }

        private bool UseCloudScriptRoomRegistry()
        {
            return true;
        }

        private Dictionary<string, string> ParseRoomListFromCloudScript(object functionResult)
        {
            var rooms = new Dictionary<string, string>();
            if (!(functionResult is IDictionary<string, object> resultDict))
                return rooms;

            if (!resultDict.TryGetValue("rooms", out object roomsObject) || roomsObject == null)
                return rooms;

            if (roomsObject is IDictionary<string, object> objectRooms)
            {
                foreach (var kv in objectRooms)
                {
                    if (kv.Value != null)
                        rooms[kv.Key] = kv.Value.ToString();
                }
            }
            else if (roomsObject is IDictionary<string, string> stringRooms)
            {
                foreach (var kv in stringRooms)
                {
                    if (!string.IsNullOrEmpty(kv.Value))
                        rooms[kv.Key] = kv.Value;
                }
            }

            return rooms;
        }

        private void GetActiveRoomInfosFromCloudScript(Action<Dictionary<string, RoomInfo>> callback)
        {
            PlayFabClientAPI.ExecuteCloudScript(
                new ExecuteCloudScriptRequest
                {
                    FunctionName = GET_ACTIVE_ROOM_INFOS_FUNCTION,
                    GeneratePlayStreamEvent = false
                },
                result =>
                {
                    Dictionary<string, RoomInfo> roomInfos = new Dictionary<string, RoomInfo>();
                    if (result.Error != null)
                    {
                        Debug.LogError($"[PlayFab] CloudScript room info list load failed: {FormatCloudScriptError(result)}");
                        GetActiveRoomInfosFromSharedGroup(callback);
                        return;
                    }
                    else
                    {
                        roomInfos = ParseRoomInfoListFromCloudScript(result.FunctionResult);
                        Debug.Log($"[PlayFab] CloudScript room info list loaded {roomInfos.Count} room(s). Result={SerializeForLog(result.FunctionResult)}");
                    }

                    foreach (var kv in _knownRooms)
                    {
                        if (!roomInfos.ContainsKey(kv.Key))
                            roomInfos[kv.Key] = BuildKnownRoomInfo(kv.Key, kv.Value);
                    }

                    MergeKnownRoomInfos(roomInfos);
                    OnRoomInfoListLoaded?.Invoke(roomInfos);
                    callback?.Invoke(roomInfos);
                },
                error =>
                {
                    Debug.LogWarning($"[PlayFab] CloudScript room info list request failed: {error.GenerateErrorReport()}");
                    GetActiveRoomInfosFromSharedGroup(callback);
                }
            );
        }

        private void GetActiveRoomInfosFromSharedGroup(Action<Dictionary<string, RoomInfo>> callback)
        {
            EnsureCurrentPlayerInSharedGroup(
                ROOM_REGISTRY_ID,
                () => GetActiveRoomInfosFromSharedGroupAfterMembership(callback),
                error =>
                {
                    if (IsSharedGroupNotFound(error))
                    {
                        CreateRegistryThenLoadSharedGroupRooms(callback);
                        return;
                    }

                    Debug.LogWarning($"[PlayFab] Could not join room registry before fallback load: {error.GenerateErrorReport()}");
                    GetActiveRoomInfosFromSharedGroupAfterMembership(callback);
                });
        }

        private void GetActiveRoomsFromSharedGroup(Action<Dictionary<string, string>> callback)
        {
            PlayFabClientAPI.GetSharedGroupData(
                new GetSharedGroupDataRequest { SharedGroupId = ROOM_REGISTRY_ID },
                result =>
                {
                    var rooms = new Dictionary<string, string>();
                    if (result.Data != null)
                    {
                        foreach (var kv in result.Data)
                        {
                            if (kv.Value == null)
                                continue;

                            rooms[kv.Key] = ParseRoomNameFromRegistryValue(kv.Key, kv.Value.Value);
                        }
                    }

                    foreach (var kv in _knownRooms)
                        rooms[kv.Key] = kv.Value;

                    OnRoomListLoaded?.Invoke(rooms);
                    callback?.Invoke(rooms);
                },
                error =>
                {
                    Debug.LogWarning($"[PlayFab] SharedGroup room list fallback failed: {error.GenerateErrorReport()}");
                    var rooms = new Dictionary<string, string>(_knownRooms);
                    OnRoomListLoaded?.Invoke(rooms);
                    callback?.Invoke(rooms);
                });
        }

        private void CreateRegistryThenLoadSharedGroupRooms(Action<Dictionary<string, RoomInfo>> callback)
        {
            PlayFabClientAPI.CreateSharedGroup(
                new CreateSharedGroupRequest { SharedGroupId = ROOM_REGISTRY_ID },
                _ => EnsureCurrentPlayerInSharedGroup(
                    ROOM_REGISTRY_ID,
                    () => GetActiveRoomInfosFromSharedGroupAfterMembership(callback),
                    error =>
                    {
                        Debug.LogWarning($"[PlayFab] Could not join newly created room registry: {error.GenerateErrorReport()}");
                        CompleteRoomInfoLoad(new Dictionary<string, RoomInfo>(), callback);
                    }),
                error =>
                {
                    if (IsAlreadySharedGroupMember(error) || error.GenerateErrorReport().IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        GetActiveRoomInfosFromSharedGroupAfterMembership(callback);
                        return;
                    }

                    Debug.LogWarning($"[PlayFab] Could not create room registry for fallback load: {error.GenerateErrorReport()}");
                    CompleteRoomInfoLoad(new Dictionary<string, RoomInfo>(), callback);
                });
        }

        private void GetActiveRoomInfosFromSharedGroupAfterMembership(Action<Dictionary<string, RoomInfo>> callback)
        {
            GetActiveRoomsFromSharedGroup(rooms =>
            {
                var roomInfos = new Dictionary<string, RoomInfo>();
                if (rooms.Count == 0)
                {
                    CompleteRoomInfoLoad(roomInfos, callback);
                    return;
                }

                int remaining = rooms.Count;
                foreach (var kvp in rooms)
                {
                    string roomId = kvp.Key;
                    string roomName = string.IsNullOrWhiteSpace(kvp.Value) ? "Unnamed Room" : kvp.Value;
                    string fallbackMasterName = GetKnownRoomMasterName(roomId);
                    int fallbackCount = GetKnownRoomCount(roomId);

                    PlayFabClientAPI.GetSharedGroupData(
                        new GetSharedGroupDataRequest { SharedGroupId = roomId },
                        result =>
                        {
                            int playerCount = fallbackCount;
                            if (result.Data != null &&
                                result.Data.TryGetValue(ROOM_PLAYER_COUNT_KEY, out SharedGroupDataRecord countRecord))
                            {
                                playerCount = ParseRoomPlayerCount(countRecord.Value, fallbackCount);
                            }

                            string masterName = fallbackMasterName;
                            if (result.Data != null &&
                                result.Data.TryGetValue(ROOM_MASTER_NAME_KEY, out SharedGroupDataRecord masterRecord))
                            {
                                masterName = string.IsNullOrWhiteSpace(masterRecord.Value) ? fallbackMasterName : masterRecord.Value;
                            }

                            if (playerCount > 0)
                                roomInfos[roomId] = new RoomInfo(roomName, masterName, playerCount, GetKnownRoomRelayJoinCode(roomId));

                            if (--remaining == 0)
                                CompleteRoomInfoLoad(roomInfos, callback);
                        },
                        error =>
                        {
                            Debug.LogWarning($"[PlayFab] Room detail fallback failed for {roomId}: {error.GenerateErrorReport()}");
                            roomInfos[roomId] = BuildKnownRoomInfo(roomId, roomName);

                            if (--remaining == 0)
                                CompleteRoomInfoLoad(roomInfos, callback);
                        });
                }
            });
        }

        private void CompleteRoomInfoLoad(Dictionary<string, RoomInfo> roomInfos, Action<Dictionary<string, RoomInfo>> callback)
        {
            MergeKnownRoomInfos(roomInfos);
            Debug.Log($"[PlayFab] SharedGroup room info fallback loaded {roomInfos.Count} room(s).");
            OnRoomInfoListLoaded?.Invoke(roomInfos);
            callback?.Invoke(roomInfos);
        }

        private void MergeKnownRoomInfos(Dictionary<string, RoomInfo> roomInfos)
        {
            foreach (var kv in _knownRooms)
            {
                if (!roomInfos.ContainsKey(kv.Key))
                    roomInfos[kv.Key] = BuildKnownRoomInfo(kv.Key, kv.Value);
            }

            if (roomInfos.Count == 0 && _lastLoadedRoomInfos.Count > 0)
            {
                foreach (var kv in _lastLoadedRoomInfos)
                    roomInfos[kv.Key] = kv.Value;

                Debug.LogWarning($"[PlayFab] Room query returned empty. Reusing {_lastLoadedRoomInfos.Count} cached room(s).");
                return;
            }

            if (roomInfos.Count <= 0)
                return;

            _lastLoadedRoomInfos.Clear();
            foreach (var kv in roomInfos)
            {
                _lastLoadedRoomInfos[kv.Key] = kv.Value;
                _knownRooms[kv.Key] = kv.Value.RoomName;
                _knownRoomMasters[kv.Key] = kv.Value.MasterName;
                _knownRoomCounts[kv.Key] = kv.Value.PlayerCount;
                _knownRoomRelayJoinCodes[kv.Key] = kv.Value.RelayJoinCode;
            }
        }

        private static string ParseRoomNameFromRegistryValue(string roomId, string registryValue)
        {
            if (string.IsNullOrWhiteSpace(registryValue))
                return roomId;

            string text = registryValue.Trim();
            if (!text.StartsWith("{", StringComparison.Ordinal))
                return text;

            try
            {
                var parsed = PlayFab.PluginManager.GetPlugin<ISerializerPlugin>(PluginContract.PlayFab_Serializer).DeserializeObject<Dictionary<string, object>>(text);
                if (parsed != null && parsed.TryGetValue("roomName", out object roomName) && roomName != null)
                {
                    string name = roomName.ToString();
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PlayFab] Failed to parse room registry value for {roomId}: {ex.Message}");
            }

            return roomId;
        }

        private Dictionary<string, RoomInfo> ParseRoomInfoListFromCloudScript(object functionResult)
        {
            var roomInfos = new Dictionary<string, RoomInfo>();
            if (!(functionResult is IDictionary<string, object> resultDict))
                return roomInfos;

            if (!resultDict.TryGetValue("roomInfos", out object roomInfosObject) || roomInfosObject == null)
                return roomInfos;

            if (!(roomInfosObject is IDictionary<string, object> objectRoomInfos))
                return roomInfos;

            foreach (var kv in objectRoomInfos)
            {
                if (!(kv.Value is IDictionary<string, object> infoDict))
                    continue;

                string roomName = GetStringValue(infoDict, "roomName", "Unnamed Room");
                string masterName = GetStringValue(infoDict, "masterName", "Unknown");
                int playerCount = GetIntValue(infoDict, "playerCount", 0);
                string relayJoinCode = GetStringValue(infoDict, "relayJoinCode", GetKnownRoomRelayJoinCode(kv.Key));
                if (playerCount <= 0)
                    continue;

                _knownRooms[kv.Key] = roomName;
                _knownRoomMasters[kv.Key] = masterName;
                _knownRoomCounts[kv.Key] = playerCount;
                _knownRoomRelayJoinCodes[kv.Key] = relayJoinCode;
                roomInfos[kv.Key] = new RoomInfo(roomName, masterName, playerCount, relayJoinCode);
            }

            return roomInfos;
        }

        private static string SerializeForLog(object value)
        {
            if (value == null)
                return "null";

            try
            {
                return PlayFab.PluginManager.GetPlugin<ISerializerPlugin>(PluginContract.PlayFab_Serializer).SerializeObject(value);
            }
            catch (Exception)
            {
                return value.ToString();
            }
        }

        private static string GetStringValue(IDictionary<string, object> values, string key, string fallback)
        {
            if (values.TryGetValue(key, out object value) && value != null)
            {
                string text = value.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return fallback;
        }

        private static int GetIntValue(IDictionary<string, object> values, string key, int fallback)
        {
            if (!values.TryGetValue(key, out object value) || value == null)
                return fallback;

            if (value is int intValue)
                return intValue;

            if (value is long longValue)
                return Mathf.Max(0, (int)longValue);

            if (value is double doubleValue)
                return Mathf.Max(0, Mathf.RoundToInt((float)doubleValue));

            return int.TryParse(value.ToString(), out int parsed) ? Mathf.Max(0, parsed) : fallback;
        }

        public void GetActiveRoomInfos(Action<Dictionary<string, RoomInfo>> callback)
        {
            if (UseCloudScriptRoomRegistry())
            {
                GetActiveRoomInfosFromCloudScript(callback);
                return;
            }

            GetActiveRooms(rooms =>
            {
                var roomInfos = new Dictionary<string, RoomInfo>();
                if (rooms.Count == 0)
                {
                    OnRoomInfoListLoaded?.Invoke(roomInfos);
                    callback?.Invoke(roomInfos);
                    return;
                }

                int remaining = rooms.Count;
                foreach (var kvp in rooms)
                {
                    string roomId = kvp.Key;
                    string roomName = string.IsNullOrWhiteSpace(kvp.Value) ? "Unnamed Room" : kvp.Value;
                    string fallbackMasterName = GetKnownRoomMasterName(roomId);
                    int fallbackCount = GetKnownRoomCount(roomId);

                    PlayFabClientAPI.GetSharedGroupData(
                        new GetSharedGroupDataRequest { SharedGroupId = roomId },
                        result =>
                        {
                            int playerCount = fallbackCount;
                            if (result.Data != null &&
                                result.Data.TryGetValue(ROOM_PLAYER_COUNT_KEY, out SharedGroupDataRecord countRecord))
                            {
                                playerCount = ParseRoomPlayerCount(countRecord.Value, fallbackCount);
                            }

                            string masterName = fallbackMasterName;
                            if (result.Data != null &&
                                result.Data.TryGetValue(ROOM_MASTER_NAME_KEY, out SharedGroupDataRecord masterRecord))
                            {
                                masterName = string.IsNullOrWhiteSpace(masterRecord.Value) ? fallbackMasterName : masterRecord.Value;
                            }

                            _knownRoomCounts[roomId] = playerCount;
                            _knownRoomMasters[roomId] = masterName;
                            roomInfos[roomId] = new RoomInfo(roomName, masterName, playerCount, GetKnownRoomRelayJoinCode(roomId));
                            if (roomId == _joinedRoomId)
                                _currentRoomInfo = roomInfos[roomId];
                            FinishRoomInfoLoad();
                        },
                        error =>
                        {
                            roomInfos[roomId] = BuildKnownRoomInfo(roomId, roomName);
                            if (roomId == _joinedRoomId)
                                _currentRoomInfo = roomInfos[roomId];
                            FinishRoomInfoLoad();
                        }
                    );
                }

                void FinishRoomInfoLoad()
                {
                    remaining--;
                    if (remaining > 0) return;

                    OnRoomInfoListLoaded?.Invoke(roomInfos);
                    callback?.Invoke(roomInfos);
                }
            });
        }

        /// <summary>
        /// 湲곗〈 諛⑹뿉 李몄뿬?⑸땲?? (Shared Group 硫ㅻ쾭 異붽?)
        /// </summary>
        public void JoinRoom(string roomId)
        {
            // ?ㅼ젣 援ы쁽?먯꽌??癒쇱? GetSharedGroupData ?깆쓣 ?듯빐 諛?議댁옱 ?щ?瑜??뺤씤???섎룄 ?덉뒿?덈떎.
            // ?ш린?쒕뒗 怨㏓컮濡?硫ㅻ쾭 異붽?瑜??쒕룄?⑸땲??
            // 二쇱쓽: PlayFabId???꾩옱 濡쒓렇?몃맂 ?좎???ID?ъ빞 ?⑸땲??
            if (UseCloudScriptRoomRegistry())
            {
                JoinRoomThroughCloudScript(roomId);
                return;
            }

            LeaveJoinedRoomBeforeSwitch(roomId);
            _isHost = !string.IsNullOrEmpty(_ownedRoomId) && roomId == _ownedRoomId;

            PlayFabClientAPI.GetAccountInfo(new GetAccountInfoRequest(), 
                result => {
                    var addRequest = new AddSharedGroupMembersRequest
                    {
                        SharedGroupId = roomId,
                        PlayFabIds = new List<string> { result.AccountInfo.PlayFabId }
                    };

                    PlayFabClientAPI.AddSharedGroupMembers(addRequest,
                        addResult => {
                            Debug.Log($"諛?李몄뿬 ?깃났: {roomId}");
                            UpdateRoomPlayerCount(roomId, 1, () =>
                            {
                                _joinedRoomId = roomId;
                                _hasJoinedRoomCount = true;
                                OnRoomJoined?.Invoke();
                            });
                        },
                        addError => Debug.LogError($"諛?李몄뿬 ?ㅽ뙣: {addError.GenerateErrorReport()}")
                    );
                },
                error => Debug.LogError($"??PlayFabID瑜?媛?몄삤?붾뜲 ?ㅽ뙣?덉뒿?덈떎: {error.GenerateErrorReport()}")
            );
        }

        /// <summary>
        /// 諛??곹깭 ?뺣낫瑜??낅뜲?댄듃?⑸땲??
        /// </summary>
        public void UpdateRoomData(string groupId, string key, string value)
        {
            var request = new UpdateSharedGroupDataRequest
            {
                SharedGroupId = groupId,
                Data = new Dictionary<string, string> { { key, value } }
            };

            PlayFabClientAPI.UpdateSharedGroupData(request, 
                result => Debug.Log($"諛??곗씠???낅뜲?댄듃 ?꾨즺: {key}={value}"),
                error => Debug.LogError($"諛??곗씠???낅뜲?댄듃 ?ㅽ뙣: {error.GenerateErrorReport()}")
            );
        }

        public void UpdateCurrentRoomRelayJoinCode(string relayJoinCode, Action<bool> callback = null)
        {
            if (string.IsNullOrWhiteSpace(_joinedRoomId))
            {
                Debug.LogWarning("[PlayFab] Cannot update Relay join code without a joined room.");
                callback?.Invoke(false);
                return;
            }

            relayJoinCode = relayJoinCode?.Trim() ?? string.Empty;
            string roomId = _joinedRoomId;
            _knownRoomRelayJoinCodes[roomId] = relayJoinCode;
            _currentRoomInfo = new RoomInfo(
                _currentRoomInfo.RoomName,
                _currentRoomInfo.MasterName,
                _currentRoomInfo.PlayerCount,
                relayJoinCode);

            var parameters = new Dictionary<string, object>
            {
                { "roomId", roomId },
                { "relayJoinCode", relayJoinCode }
            };

            PlayFabClientAPI.ExecuteCloudScript(
                new ExecuteCloudScriptRequest
                {
                    FunctionName = UPDATE_ROOM_RELAY_JOIN_CODE_FUNCTION,
                    FunctionParameter = parameters,
                    GeneratePlayStreamEvent = false
                },
                result =>
                {
                    if (result.Error != null)
                    {
                        Debug.LogError($"[PlayFab] Relay join code update failed: {FormatCloudScriptError(result)}");
                        callback?.Invoke(false);
                        return;
                    }

                    OnRoomRegistryChanged?.Invoke();
                    callback?.Invoke(true);
                },
                error =>
                {
                    Debug.LogError($"[PlayFab] Relay join code update request failed: {error.GenerateErrorReport()}");
                    callback?.Invoke(false);
                });
        }

        public void LeaveCurrentRoom()
        {
            if (!_hasJoinedRoomCount || string.IsNullOrEmpty(_joinedRoomId)) return;

            string roomId = _joinedRoomId;
            _joinedRoomId = null;
            _hasJoinedRoomCount = false;

            if (UseCloudScriptRoomRegistry())
            {
                LeaveRoomThroughCloudScript(roomId);
                return;
            }

            UpdateRoomPlayerCount(roomId, -1);
        }

        private void LeaveJoinedRoomBeforeSwitch(string nextRoomId)
        {
            if (!_hasJoinedRoomCount || string.IsNullOrEmpty(_joinedRoomId))
                return;

            if (!string.IsNullOrEmpty(nextRoomId) && string.Equals(_joinedRoomId, nextRoomId, StringComparison.Ordinal))
                return;

            string previousRoomId = _joinedRoomId;
            _joinedRoomId = null;
            _hasJoinedRoomCount = false;

            Debug.Log($"[PlayFab] Leaving previous room before switching: {previousRoomId}");

            if (UseCloudScriptRoomRegistry())
            {
                LeaveRoomThroughCloudScript(previousRoomId);
                return;
            }

            UpdateRoomPlayerCount(previousRoomId, -1);
        }

        public void RefreshCurrentRoomInfo(Action<RoomInfo> callback = null)
        {
            if (string.IsNullOrEmpty(_joinedRoomId))
            {
                callback?.Invoke(_currentRoomInfo);
                return;
            }

            string roomId = _joinedRoomId;
            GetActiveRoomInfos(rooms =>
            {
                if (rooms.TryGetValue(roomId, out RoomInfo info))
                    _currentRoomInfo = info;

                callback?.Invoke(_currentRoomInfo);
            });
        }

        public void AdminDeleteRoom(string adminKey, string roomId, Action<bool, string> callback = null)
        {
            if (string.IsNullOrWhiteSpace(adminKey))
            {
                callback?.Invoke(false, "Admin key is empty.");
                return;
            }

            if (string.IsNullOrWhiteSpace(roomId))
            {
                callback?.Invoke(false, "Room id is empty.");
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "adminKey", adminKey.Trim() },
                { "roomId", roomId.Trim() }
            };

            ExecuteRoomAdminCloudScript(ADMIN_DELETE_ROOM_FUNCTION, parameters, (ok, message) =>
            {
                if (ok)
                {
                    _knownRooms.Remove(roomId);
                    _knownRoomMasters.Remove(roomId);
                    _knownRoomCounts.Remove(roomId);
                    _knownRoomRelayJoinCodes.Remove(roomId);
                    OnRoomRegistryChanged?.Invoke();
                }

                callback?.Invoke(ok, message);
            });
        }

        public void AdminValidateRoomKey(string adminKey, Action<bool, string> callback = null)
        {
            if (string.IsNullOrWhiteSpace(adminKey))
            {
                callback?.Invoke(false, "Admin key is empty.");
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "adminKey", adminKey.Trim() }
            };

            ExecuteRoomAdminCloudScript(ADMIN_VALIDATE_ROOM_KEY_FUNCTION, parameters, callback);
        }

        public void AdminClearRoomRegistry(string adminKey, Action<bool, string> callback = null)
        {
            if (string.IsNullOrWhiteSpace(adminKey))
            {
                callback?.Invoke(false, "Admin key is empty.");
                return;
            }

            var parameters = new Dictionary<string, object>
            {
                { "adminKey", adminKey.Trim() }
            };

            ExecuteRoomAdminCloudScript(ADMIN_CLEAR_ROOM_REGISTRY_FUNCTION, parameters, (ok, message) =>
            {
                if (ok)
                {
                    _knownRooms.Clear();
                    _knownRoomMasters.Clear();
                    _knownRoomCounts.Clear();
                    _knownRoomRelayJoinCodes.Clear();
                    OnRoomRegistryChanged?.Invoke();
                }

                callback?.Invoke(ok, message);
            });
        }

        private void ExecuteRoomAdminCloudScript(string functionName, Dictionary<string, object> parameters, Action<bool, string> callback)
        {
            PlayFabClientAPI.ExecuteCloudScript(
                new ExecuteCloudScriptRequest
                {
                    FunctionName = functionName,
                    FunctionParameter = parameters,
                    GeneratePlayStreamEvent = false
                },
                result =>
                {
                    if (result.Error != null)
                    {
                        string message = FormatCloudScriptError(result);
                        Debug.LogError($"[PlayFab] {functionName} failed: {message}");
                        callback?.Invoke(false, message);
                        return;
                    }

                    Debug.Log($"[PlayFab] {functionName} completed.");
                    callback?.Invoke(true, "Completed.");
                },
                error =>
                {
                    string message = error.GenerateErrorReport();
                    Debug.LogError($"[PlayFab] {functionName} request failed: {message}");
                    callback?.Invoke(false, message);
                });
        }

        private static string FormatCloudScriptError(ExecuteCloudScriptResult result)
        {
            var builder = new StringBuilder();

            if (result.Error != null)
            {
                if (!string.IsNullOrWhiteSpace(result.Error.Error))
                    builder.Append(result.Error.Error).Append(": ");

                builder.Append(result.Error.Message);

                if (!string.IsNullOrWhiteSpace(result.Error.StackTrace))
                    builder.Append("\n").Append(result.Error.StackTrace);
            }

            if (result.Logs != null && result.Logs.Count > 0)
            {
                builder.Append("\nLogs:");
                foreach (var logLine in result.Logs)
                {
                    builder.Append("\n[")
                        .Append(logLine.Level)
                        .Append("] ")
                        .Append(logLine.Message);
                }
            }

            return builder.Length > 0 ? builder.ToString() : "CloudScript failed.";
        }

        private void OnApplicationQuit()
        {
            LeaveCurrentRoom();
        }

        private void LeaveRoomThroughCloudScript(string roomId)
        {
            var parameters = new Dictionary<string, object>
            {
                { "roomId", roomId }
            };

            PlayFabClientAPI.ExecuteCloudScript(
                new ExecuteCloudScriptRequest
                {
                    FunctionName = LEAVE_ROOM_FUNCTION,
                    FunctionParameter = parameters,
                    GeneratePlayStreamEvent = false
                },
                result =>
                {
                    if (result.Error != null)
                    {
                        Debug.LogError($"[PlayFab] CloudScript room leave failed: {result.Error.Message}");
                        return;
                    }

                    int playerCount = 0;
                    if (result.FunctionResult is IDictionary<string, object> resultDict)
                        playerCount = GetIntValue(resultDict, "playerCount", 0);

                    if (playerCount <= 0)
                    {
                        _knownRooms.Remove(roomId);
                        _knownRoomMasters.Remove(roomId);
                        _knownRoomCounts.Remove(roomId);
                        _knownRoomRelayJoinCodes.Remove(roomId);
                    }
                    else
                    {
                        _knownRoomCounts[roomId] = playerCount;
                    }

                    OnRoomRegistryChanged?.Invoke();
                },
                error => Debug.LogError($"[PlayFab] CloudScript room leave request failed: {error.GenerateErrorReport()}")
            );
        }

        private void UpdateRoomPlayerCount(string roomId, int delta, Action onComplete = null)
        {
            int fallbackCount = GetKnownRoomCount(roomId);
            PlayFabClientAPI.GetSharedGroupData(
                new GetSharedGroupDataRequest { SharedGroupId = roomId },
                result =>
                {
                    int currentCount = fallbackCount;
                    if (result.Data != null &&
                        result.Data.TryGetValue(ROOM_PLAYER_COUNT_KEY, out SharedGroupDataRecord countRecord))
                    {
                        currentCount = ParseRoomPlayerCount(countRecord.Value, fallbackCount);
                    }

                    int nextCount = Mathf.Max(0, currentCount + delta);
                    _knownRoomCounts[roomId] = nextCount;
                    UpdateRoomData(roomId, ROOM_PLAYER_COUNT_KEY, nextCount.ToString());
                    OnRoomRegistryChanged?.Invoke();
                    onComplete?.Invoke();
                },
                error =>
                {
                    int nextCount = Mathf.Max(0, fallbackCount + delta);
                    _knownRoomCounts[roomId] = nextCount;
                    OnRoomRegistryChanged?.Invoke();
                    onComplete?.Invoke();
                }
            );
        }

        private int GetKnownRoomCount(string roomId)
        {
            if (_knownRoomCounts.TryGetValue(roomId, out int knownCount))
                return Mathf.Max(0, knownCount);

            return string.IsNullOrEmpty(roomId) ? 0 : 1;
        }

        private string GetKnownRoomName(string roomId)
        {
            if (_knownRooms.TryGetValue(roomId, out string roomName) &&
                !string.IsNullOrWhiteSpace(roomName))
            {
                return roomName;
            }

            return "Unnamed Room";
        }

        private string GetKnownRoomMasterName(string roomId)
        {
            if (_knownRoomMasters.TryGetValue(roomId, out string masterName) &&
                !string.IsNullOrWhiteSpace(masterName))
            {
                return masterName;
            }

            return "Unknown";
        }

        private string GetKnownRoomRelayJoinCode(string roomId)
        {
            if (_knownRoomRelayJoinCodes.TryGetValue(roomId, out string relayJoinCode) &&
                !string.IsNullOrWhiteSpace(relayJoinCode))
            {
                return relayJoinCode;
            }

            if (_lastLoadedRoomInfos.TryGetValue(roomId, out RoomInfo lastInfo) &&
                !string.IsNullOrWhiteSpace(lastInfo.RelayJoinCode))
            {
                return lastInfo.RelayJoinCode;
            }

            return string.Empty;
        }

        private RoomInfo BuildKnownRoomInfo(string roomId, string roomName = null)
        {
            return new RoomInfo(
                string.IsNullOrWhiteSpace(roomName) ? GetKnownRoomName(roomId) : roomName,
                GetKnownRoomMasterName(roomId),
                GetKnownRoomCount(roomId),
                GetKnownRoomRelayJoinCode(roomId));
        }

        private string GetCurrentPlayerNickname()
        {
            string nickname = GlobalDataManager.Instance != null
                ? GlobalDataManager.Instance.PlayerNickname
                : null;

            return string.IsNullOrWhiteSpace(nickname) ? "Unknown" : nickname.Trim();
        }

        private int ParseRoomPlayerCount(string value, int fallbackCount)
        {
            if (int.TryParse(value, out int count))
                return Mathf.Max(0, count);

            return Mathf.Max(0, fallbackCount);
        }

        #endregion

        #region [User Data Sync]

        /// <summary>
        /// ?뚮젅?댁뼱??湲곕낯 ?ㅽ꺈 諛?怨꾩궛???뚯깮 ?ㅽ꺈 ?뺣낫瑜?PlayFab????ν빀?덈떎. (10媛????쒗븳???쇳빐 ??踰덉뿉 ?섎늻???꾩넚)
        /// </summary>
        public void SavePlayerStats(StatContainer stats, float atk, float maxHp, float defPercent, float pene, float regen, float moveSpd, float atkSpd)
        {
            // 1. Primary ?ㅽ꺈 ?꾩넚 (STR, CON, AGI, DEF - 4媛?
            int str = Mathf.RoundToInt(stats.STR.Invested);
            int con = Mathf.RoundToInt(stats.CON.Invested);
            int agi = Mathf.RoundToInt(stats.AGI.Invested);
            int defSub = Mathf.RoundToInt(stats.DEF.Invested);

            var primaryRequest = new UpdateUserDataRequest
            {
                Data = new Dictionary<string, string>
                {
                    { "STR", str.ToString() },
                    { "CON", con.ToString() },
                    { "AGI", agi.ToString() },
                    { "DEF", defSub.ToString() }
                },
                Permission = UserDataPermission.Public
            };

            PlayFabClientAPI.UpdateUserData(primaryRequest, 
                result => {
                    Debug.Log("<color=green>[PlayFab] Primary stats saved.</color>");
                    
                    // 2. Secondary ?ㅽ꺈 ?꾩넚 (?섎㉧吏 7媛?
                    var secondaryRequest = new UpdateUserDataRequest
                    {
                        Data = new Dictionary<string, string>
                        {
                            { "ATK", atk.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) },
                            { "MaxHP", maxHp.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) },
                            { "DefPercent", defPercent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) },
                            { "Pene", pene.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) },
                            { "Regen", regen.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) },
                            { "MoveSpd", moveSpd.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) },
                            { "AtkSpd", atkSpd.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) }
                        },
                        Permission = UserDataPermission.Public
                    };

                    PlayFabClientAPI.UpdateUserData(secondaryRequest, 
                        res => Debug.Log("<color=green>[PlayFab] Secondary stats saved.</color>"),
                        err => Debug.LogError($"[PlayFab] Secondary stats save FAILED: {err.GenerateErrorReport()}")
                    );
                },
                error => Debug.LogError($"[PlayFab] Primary stats save FAILED: {error.GenerateErrorReport()}")
            );
        }

        /// <summary>
        /// PlayFab?먯꽌 ?뚮젅?댁뼱 ?ㅽ꺈 ?뺣낫瑜?遺덈윭?듬땲?? (FormatException 諛⑹?瑜??꾪빐 double/float ?뚯떛 ?ъ슜)
        /// </summary>
        public void LoadPlayerStats(Action<StatContainer> onLoaded)
        {
            Debug.Log("[PlayFabBattleManager] Requesting player stats from Cloud...");
            
            PlayFabClientAPI.GetUserData(new GetUserDataRequest(), 
                result => {
                    var stats = new StatContainer();
                    if (result.Data != null && result.Data.Count > 0)
                    {
                        // 紐⑤뱺 ?꾨뱶??????덉쟾?섍쾶 ?뚯떛?섏뿬 FormatException 諛⑹?
                        if (result.Data.ContainsKey("STR")) stats.STR.Invested = (float)ParseValue(result.Data["STR"].Value);
                        if (result.Data.ContainsKey("CON")) stats.CON.Invested = (float)ParseValue(result.Data["CON"].Value);
                        if (result.Data.ContainsKey("AGI")) stats.AGI.Invested = (float)ParseValue(result.Data["AGI"].Value);
                        if (result.Data.ContainsKey("DEF")) stats.DEF.Invested = (float)ParseValue(result.Data["DEF"].Value);
                        
                        Debug.Log($"<color=cyan>[PlayFab] Stats loaded: STR={stats.STR.Invested}, AGI={stats.AGI.Invested}, CON={stats.CON.Invested}, DEF={stats.DEF.Invested}</color>");
                    }
                    else
                    {
                        Debug.LogWarning("[PlayFabBattleManager] No player data found on Cloud. Using defaults.");
                    }
                    onLoaded?.Invoke(stats);
                },
                error => {
                    Debug.LogError($"<color=red>[PlayFabBattleManager] Player stats load FAILED: {error.GenerateErrorReport()}</color>");
                    onLoaded?.Invoke(new StatContainer());
                }
            );
        }

        private double ParseValue(string val)
        {
            if (string.IsNullOrEmpty(val)) return 0;
            if (double.TryParse(val, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
            {
                return result;
            }
            return 0;
        }

        #endregion
        
        /// <summary>
        /// ?밸━/?뚮젅??湲곕줉??由щ뜑蹂대뱶???낅뜲?댄듃?⑸땲??
        /// </summary>
        public void UpdateStatistics(int points)
        {
            if (_clientStatisticUpdatesDisabled)
                return;

            var request = new UpdatePlayerStatisticsRequest
            {
                Statistics = new List<StatisticUpdate>
                {
                    new StatisticUpdate { StatisticName = "TotalPoints", Value = points }
                }
            };

            PlayFabClientAPI.UpdatePlayerStatistics(request, 
                result => Debug.Log("由щ뜑蹂대뱶 ?낅뜲?댄듃 ?깃났"),
                error => HandleStatisticsUpdateError(error)
            );
        }

        private void HandleStatisticsUpdateError(PlayFabError error)
        {
            string report = error.GenerateErrorReport();
            if (report.Contains("This API must be enabled for client access"))
            {
                _clientStatisticUpdatesDisabled = true;
                Debug.LogWarning("[PlayFab] Client leaderboard updates are disabled in Game Manager API Features. Skipping future client statistic updates.");
                return;
            }

            Debug.LogError($"Leaderboard update failed: {report}");
        }
    }
}
