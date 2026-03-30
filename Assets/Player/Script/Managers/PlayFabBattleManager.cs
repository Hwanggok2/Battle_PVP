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

        public event Action<List<string>> OnRoomListLoaded;
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
            var request = new CreateSharedGroupRequest
            {
                SharedGroupId = roomName 
            };

            PlayFabClientAPI.CreateSharedGroup(request, 
                result => {
                    Debug.Log($"방 생성 성공: {roomName}");
                    UpdateRoomData(roomName, "IsStarted", "false");
                    
                    // 글로벌 레지스트리에 이 방의 존재를 알립니다.
                    RegisterRoomToRegistry(roomName);
                }, 
                error => Debug.LogError($"방 생성 실패: {error.GenerateErrorReport()}")
            );
        }

        /// <summary>
        /// 모든 플레이어가 볼 수 있는 공용 그룹에 현재 방 이름을 추가합니다.
        /// </summary>
        private void RegisterRoomToRegistry(string roomName)
        {
            // 실제 구현에서는 서버 로직(CloudScript/Azure Functions)을 쓰는 것이 보안상 좋으나,
            // 클라이언트 사이드에서는 이 레지스트리 그룹의 멤버로 자신을 추가하거나 데이터를 갱신합니다.
            var request = new UpdateSharedGroupDataRequest
            {
                SharedGroupId = ROOM_REGISTRY_ID,
                Data = new Dictionary<string, string> { { roomName, DateTime.UtcNow.ToString() } }
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
                        CreateRegistryAndRegister(roomName);
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

        private void CreateRegistryAndRegister(string roomName)
        {
            PlayFabClientAPI.CreateSharedGroup(new CreateSharedGroupRequest { SharedGroupId = ROOM_REGISTRY_ID },
                result => RegisterRoomToRegistry(roomName),
                error => OnRoomJoined?.Invoke()
            );
        }

        /// <summary>
        /// 현재 개설된 방 목록을 가져옵니다.
        /// </summary>
        public void GetActiveRooms(Action<List<string>> callback)
        {
            var request = new GetSharedGroupDataRequest { SharedGroupId = ROOM_REGISTRY_ID };

            PlayFabClientAPI.GetSharedGroupData(request,
                result => {
                    List<string> rooms = new List<string>(result.Data.Keys);
                    callback?.Invoke(rooms);
                },
                error => {
                    Debug.LogWarning("방 목록을 가져올 수 없거나 목록이 비어 있습니다.");
                    callback?.Invoke(new List<string>());
                }
            );
        }

        /// <summary>
        /// 기존 방에 참여합니다. (Shared Group 멤버 추가)
        /// </summary>
        public void JoinRoom(string roomName)
        {
            // 실제 구현에서는 먼저 GetSharedGroupData 등을 통해 방 존재 여부를 확인할 수도 있습니다.
            // 여기서는 곧바로 멤버 추가를 시도합니다.
            // 주의: PlayFabId는 현재 로그인된 유저의 ID여야 합니다.
            PlayFabClientAPI.GetAccountInfo(new GetAccountInfoRequest(), 
                result => {
                    var addRequest = new AddSharedGroupMembersRequest
                    {
                        SharedGroupId = roomName,
                        PlayFabIds = new List<string> { result.AccountInfo.PlayFabId }
                    };

                    PlayFabClientAPI.AddSharedGroupMembers(addRequest,
                        addResult => {
                            Debug.Log($"방 참여 성공: {roomName}");
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
        /// 플레이어의 스탯 정보를 PlayFab에 저장합니다.
        /// </summary>
        public void SavePlayerStats(StatContainer stats)
        {
            var request = new UpdateUserDataRequest
            {
                Data = new Dictionary<string, string>
                {
                    { "STR", stats.STR.Invested.ToString() },
                    { "CON", stats.CON.Invested.ToString() },
                    { "AGI", stats.AGI.Invested.ToString() },
                    { "DEF", stats.DEF.Invested.ToString() }
                },
                Permission = UserDataPermission.Public
            };

            PlayFabClientAPI.UpdateUserData(request, 
                result => Debug.Log("플레이어 스탯 저장 성공"),
                error => Debug.LogError($"플레이어 스탯 저장 실패: {error.GenerateErrorReport()}")
            );
        }

        /// <summary>
        /// PlayFab에서 플레이어 스탯 정보를 불러옵니다.
        /// </summary>
        public void LoadPlayerStats(Action<StatContainer> onLoaded)
        {
            PlayFabClientAPI.GetUserData(new GetUserDataRequest(), 
                result => {
                    if (result.Data != null)
                    {
                        var stats = new StatContainer();
                        if (result.Data.ContainsKey("STR")) stats.STR.Invested = int.Parse(result.Data["STR"].Value);
                        if (result.Data.ContainsKey("CON")) stats.CON.Invested = int.Parse(result.Data["CON"].Value);
                        if (result.Data.ContainsKey("AGI")) stats.AGI.Invested = int.Parse(result.Data["AGI"].Value);
                        if (result.Data.ContainsKey("DEF")) stats.DEF.Invested = int.Parse(result.Data["DEF"].Value);
                        
                        onLoaded?.Invoke(stats);
                    }
                },
                error => Debug.LogError($"플레이어 스탯 로드 실패: {error.GenerateErrorReport()}")
            );
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
