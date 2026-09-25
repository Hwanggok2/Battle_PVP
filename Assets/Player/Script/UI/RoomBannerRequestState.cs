namespace BattlePvp.UI
{
    /// <summary>배너가 표시하는 방과 활성 기간에 속하는 최신 메타데이터 응답만 허용한다.</summary>
    public sealed class RoomBannerRequestState
    {
        private uint _request;
        private string _roomId;
        private bool _active;

        public void SetActive(bool active)
        {
            _active = active;
            unchecked { _request++; }
        }

        public uint BeginRequest(string roomId)
        {
            _roomId = roomId;
            unchecked { return ++_request; }
        }

        public bool IsCurrent(uint request, string currentRoomId) =>
            _active && request == _request && _roomId == currentRoomId;
    }
}
