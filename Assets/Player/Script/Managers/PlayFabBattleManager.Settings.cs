using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Mirror;
using UnityEngine.SceneManagement;

namespace BattlePvp.Networking
{
    public partial class PlayFabBattleManager
    {
        public bool RoomSettingsBusy { get; private set; }
        public bool IsListedPrivate(string roomId) => _lastLoadedRoomInfos.TryGetValue(roomId, out RoomInfo info) && info.IsPrivate;

        private bool CanManageRoom(RoomFlow flow) => IsCurrentRoomFlow(flow) && flow.IsHost && NetworkServer.active &&
            SceneManager.GetActiveScene().name == "Battle_waiting" && !BattlePvp.UI.BattleStartController.IsStarting;

        public async Task<string> SaveRoomSettings(string title, int capacity, bool isPrivate, string password)
        {
            RoomFlow flow = _activeRoomFlow;
            if (RoomSettingsBusy || !CanManageRoom(flow)) return "방장만 대기 중에 설정할 수 있습니다.";
            title = title?.Trim();
            if (string.IsNullOrEmpty(title) || title.Length > 40 || title.IndexOfAny(new[] {'\n', '\r'}) >= 0)
                return "방 제목은 1~40자로 입력해 주세요.";
            if (!RoomAdmission.ValidCapacity(capacity, NetworkManager.singleton.numPlayers)) return "정원은 현재 인원 이상, 2~8명으로 설정해 주세요.";
            if (isPrivate && string.IsNullOrEmpty(password) && !flow.Info.IsPrivate) return "비밀방 암호를 입력해 주세요.";
            if (password != null && password.Length > 32) return "암호는 32자 이내로 입력해 주세요.";
            RoomSettingsBusy = true;
            try
            {
                var result = await ExecuteRoomMutation(flow, "UpdateRoomSettings", new Dictionary<string, object>
                {
                    {"roomId", flow.Ticket.RoomId}, {"roomName", title}, {"capacity", capacity}, {"isPrivate", isPrivate},
                    {"passwordHash", isPrivate ? RoomAdmission.PasswordHash(flow.Ticket.RoomId, password) : string.Empty}
                });
                if (!CanManageRoom(flow)) return "방 상태가 변경되었습니다.";
                if (result?.Error != null || !(result?.FunctionResult is IDictionary<string, object> data) ||
                    !data.TryGetValue("ok", out object ok) || !Equals(ok, true)) return "설정을 저장하지 못했습니다. 잠시 후 다시 시도해 주세요.";
                flow.Info = ParseRoomInfoFromCloudScript(result.FunctionResult, flow); _currentRoomInfo = flow.Info;
                (NetworkManager.singleton as BattleNetworkManager)?.ApplyRoomCapacity(flow.Info.Capacity);
                BattleMapSelection.Instance?.PublishRoomSettings(flow.Info);
                UpdateListedRoom(flow.Ticket.RoomId, flow.Info); NotifyRoomRegistryChanged(flow);
                return null;
            }
            catch (Exception) { return "설정 저장을 확인하지 못했습니다. 잠시 후 다시 시도해 주세요."; }
            finally { RoomSettingsBusy = false; }
        }

        public async Task<string> KickRoomPlayer(uint netId)
        {
            RoomFlow flow = _activeRoomFlow;
            if (RoomSettingsBusy || !CanManageRoom(flow) || !NetworkServer.spawned.TryGetValue(netId, out var target)) return "현재 강퇴할 수 없습니다.";
            NetworkConnectionToClient connection = target.connectionToClient;
            if (connection == null || connection == NetworkServer.localConnection ||
                !(connection.authenticationData is AuthenticatedRoomPlayer account) || account.RoomId != flow.Ticket.RoomId)
                return "강퇴할 수 없는 참가자입니다.";
            RoomSettingsBusy = true;
            try
            {
                var result = await ExecuteRoomMutation(flow, "KickRoomPlayer", new Dictionary<string, object>
                    {{"roomId", flow.Ticket.RoomId}, {"playerId", account.PlayFabId}});
                if (!CanManageRoom(flow)) return "방 상태가 변경되었습니다.";
                if (result?.Error != null || !(result?.FunctionResult is IDictionary<string, object> data) ||
                    !data.TryGetValue("ok", out object ok) || !Equals(ok, true)) return "강퇴 요청을 처리하지 못했습니다.";
                (NetworkManager.singleton as BattleNetworkManager)?.BanRoomAccount(account.PlayFabId);
                // Disconnect only the original connection, never a replacement occupying its old ID.
                if (NetworkServer.connections.TryGetValue(connection.connectionId, out var current) && ReferenceEquals(current, connection)) connection.Disconnect();
                flow.Info = ParseRoomInfoFromCloudScript(result.FunctionResult, flow); _currentRoomInfo = flow.Info;
                UpdateListedRoom(flow.Ticket.RoomId, flow.Info); NotifyRoomRegistryChanged(flow);
                return null;
            }
            catch (Exception) { return "강퇴 결과를 확인하지 못했습니다. 잠시 후 다시 시도해 주세요."; }
            finally { RoomSettingsBusy = false; }
        }
    }
}
