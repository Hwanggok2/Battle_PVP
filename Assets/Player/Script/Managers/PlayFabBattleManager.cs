using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;
using System.Collections.Generic;
using BattlePvp.Stats;
using System;
using Mirror;

namespace BattlePvp.Networking
{
    /// <summary>
    /// PlayFab의 Shared Group Data를 이용해 방관리 및 유저 데이터를 동기화하는 클래스입니다.
    /// </summary>
    public class PlayFabBattleManager : MonoBehaviour
    {
        public static PlayFabBattleManager Instance { get; private set; }

        private const string ROOM_REGISTRY_ID = "GLOBAL_ROOM_REGISTRY"; // 방 목록을 저장할 공용 그룹 ID

        [Header("Scene Settings")]
        [SerializeField] private string _waitSceneName = "Battle_wait"; // 인스펙터에서 설정 가능

        public event Action<Dictionary<string, string>> OnRoomListLoaded;
        public event Action OnRoomJoined;

        private bool _isHost = false; // 방장 여부 확인 플래그

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                
                // 방 입장에 성공하면 씬 전환 이벤트 연결
                OnRoomJoined += HandleRoomJoinSuccess;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void HandleRoomJoinSuccess()
        {
            if (NetworkManager.singleton == null)
            {
                Debug.LogError("[PlayFabManager] NetworkManager.singleton is missing! Mirror setup required.");
                return;
            }

            if (_isHost)
            {
                Debug.Log($"[PlayFabManager] Starting Mirror Host for Room: {_waitSceneName}");
                NetworkManager.singleton.StartHost();
            }
            else
            {
                Debug.Log($"[PlayFabManager] Starting Mirror Client connecting to localhost...");
                NetworkManager.singleton.networkAddress = "localhost"; // 현재는 로컬 테스트용
                NetworkManager.singleton.StartClient();
            }
        }

        #region [Room Management]

        /// <summary>
        /// 새로운 방을 생성하고 글로벌 레지스트리에 등록합니다.
        /// </summary>
        public void CreateRoom(string roomName)
        {
            _isHost = true; // 만드는 사람이 호스트가 됩니다.
            string roomId = Guid.NewGuid().ToString("N"); // 영문자+숫자로만 구성된 안전한 식별자
            var request = new CreateSharedGroupRequest
            {
                SharedGroupId = roomId 
            };

            PlayFabClientAPI.CreateSharedGroup(request, 
                result => {
                    Debug.Log($"방 생성 성공: {roomName} (ID: {roomId})");
                    UpdateRoomData(roomId, "IsStarted", "false");
                    
                    // 글로벌 레지스트리에 이 방의 고유 ID와 실제 제목을 쌍으로 등록합니다.
                    RegisterRoomToRegistry(roomId, roomName);
                }, 
                error => Debug.LogError($"방 생성 실패: {error.GenerateErrorReport()}")
            );
        }

        /// <summary>
        /// 모든 플레이어가 볼 수 있는 공용 그룹에 현재 방 정보를 추가합니다.
        /// </summary>
        private void RegisterRoomToRegistry(string roomId, string roomName)
        {
            var request = new UpdateSharedGroupDataRequest
            {
                SharedGroupId = ROOM_REGISTRY_ID,
                Data = new Dictionary<string, string> { { roomId, roomName } }
            };

            PlayFabClientAPI.UpdateSharedGroupData(request,
                result => {
                    Debug.Log($"글로벌 레지스트리에 방 '{roomName}' 등록 완료");
                    OnRoomJoined?.Invoke();
                },
                error => {
                    // 레지스트리 그룹이 없으면 생성 시도 (최초 1회)
                    if (error.Error.ToString().Contains("SharedGroupNotFound") || (int)error.Error == 1088)
                    {
                        CreateRegistryAndRegister(roomId, roomName);
                    }
                    else
                    {
                        Debug.LogWarning($"레지스트리 등록 실패(권한 등): {error.GenerateErrorReport()}. 일단 호스트를 시작합니다.");
                        // 권한이 없더라도(NotAuthorized) 방 생성은 성공했으므로 게임은 진행할 수 있게 함
                        OnRoomJoined?.Invoke();
                    }
                }
            );
        }

        private void CreateRegistryAndRegister(string roomId, string roomName)
        {
            PlayFabClientAPI.CreateSharedGroup(new CreateSharedGroupRequest { SharedGroupId = ROOM_REGISTRY_ID },
                result => RegisterRoomToRegistry(roomId, roomName),
                error => OnRoomJoined?.Invoke()
            );
        }

        /// <summary>
        /// 현재 개설된 방 목록을 가져옵니다. (Key: RoomID, Value: RoomName)
        /// </summary>
        public void GetActiveRooms(Action<Dictionary<string, string>> callback)
        {
            var request = new GetSharedGroupDataRequest { SharedGroupId = ROOM_REGISTRY_ID };

            PlayFabClientAPI.GetSharedGroupData(request,
                result => {
                    Dictionary<string, string> rooms = new Dictionary<string, string>();
                    if (result.Data != null)
                    {
                        foreach (var kv in result.Data)
                        {
                            // Value가 null이 아니면 포함
                            if (kv.Value != null) rooms[kv.Key] = kv.Value.Value;
                        }
                    }
                    callback?.Invoke(rooms);
                },
                error => {
                    Debug.LogWarning("방 목록을 가져올 수 없거나 목록이 비어 있습니다.");
                    callback?.Invoke(new Dictionary<string, string>());
                }
            );
        }

        /// <summary>
        /// 기존 방에 참여합니다. (Shared Group 멤버 추가)
        /// </summary>
        public void JoinRoom(string roomId)
        {
            // 실제 구현에서는 먼저 GetSharedGroupData 등을 통해 방 존재 여부를 확인할 수도 있습니다.
            // 여기서는 곧바로 멤버 추가를 시도합니다.
            // 주의: PlayFabId는 현재 로그인된 유저의 ID여야 합니다.
            PlayFabClientAPI.GetAccountInfo(new GetAccountInfoRequest(), 
                result => {
                    var addRequest = new AddSharedGroupMembersRequest
                    {
                        SharedGroupId = roomId,
                        PlayFabIds = new List<string> { result.AccountInfo.PlayFabId }
                    };

                    PlayFabClientAPI.AddSharedGroupMembers(addRequest,
                        addResult => {
                            Debug.Log($"방 참여 성공: {roomId}");
                            OnRoomJoined?.Invoke();
                        },
                        addError => Debug.LogError($"방 참여 실패: {addError.GenerateErrorReport()}")
                    );
                },
                error => Debug.LogError($"내 PlayFabID를 가져오는데 실패했습니다: {error.GenerateErrorReport()}")
            );
        }

        /// <summary>
        /// 방 상태 정보를 업데이트합니다.
        /// </summary>
        public void UpdateRoomData(string groupId, string key, string value)
        {
            var request = new UpdateSharedGroupDataRequest
            {
                SharedGroupId = groupId,
                Data = new Dictionary<string, string> { { key, value } }
            };

            PlayFabClientAPI.UpdateSharedGroupData(request, 
                result => Debug.Log($"방 데이터 업데이트 완료: {key}={value}"),
                error => Debug.LogError($"방 데이터 업데이트 실패: {error.GenerateErrorReport()}")
            );
        }

        #endregion

        #region [User Data Sync]

        /// <summary>
        /// 플레이어의 기본 스탯 및 계산된 파생 스탯 정보를 PlayFab에 저장합니다. (10개 키 제한을 피해 두 번에 나누어 전송)
        /// </summary>
        public void SavePlayerStats(StatContainer stats, float atk, float maxHp, float defPercent, float pene, float regen, float moveSpd, float atkSpd)
        {
            // 1. Primary 스탯 전송 (STR, CON, AGI, DEF - 4개)
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
                    
                    // 2. Secondary 스탯 전송 (나머지 7개)
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
        /// PlayFab에서 플레이어 스탯 정보를 불러옵니다. (FormatException 방지를 위해 double/float 파싱 사용)
        /// </summary>
        public void LoadPlayerStats(Action<StatContainer> onLoaded)
        {
            Debug.Log("[PlayFabBattleManager] Requesting player stats from Cloud...");
            
            PlayFabClientAPI.GetUserData(new GetUserDataRequest(), 
                result => {
                    var stats = new StatContainer();
                    if (result.Data != null && result.Data.Count > 0)
                    {
                        // 모든 필드에 대해 안전하게 파싱하여 FormatException 방지
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
        /// 승리/플레이 기록을 리더보드에 업데이트합니다.
        /// </summary>
        public void UpdateStatistics(int points)
        {
            var request = new UpdatePlayerStatisticsRequest
            {
                Statistics = new List<StatisticUpdate>
                {
                    new StatisticUpdate { StatisticName = "TotalPoints", Value = points }
                }
            };

            PlayFabClientAPI.UpdatePlayerStatistics(request, 
                result => Debug.Log("리더보드 업데이트 성공"),
                error => Debug.LogError($"리더보드 업데이트 실패: {error.GenerateErrorReport()}")
            );
        }
    }
}
