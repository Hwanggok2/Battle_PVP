using System;
using PlayFab.ClientModels;

namespace BattlePvp.Networking
{
    /// <summary>서버 응답을 고정된 진단 코드로 변환한다. 계정/방 ID나 인증 데이터를 로그에 복사하지 않는다.</summary>
    public static class RoomServiceErrors
    {
        public static string Classify(ExecuteCloudScriptResult result)
        {
            if (result == null) return "response_missing";
            if (result.Error == null) return "response_mismatch";
            string message = result.Error.Message ?? string.Empty;
            if (Contains(message, "ROOM_PASSWORD")) return "password_invalid";
            if (Contains(message, "ROOM_KICKED")) return "kicked";
            if (Contains(message, "Room is full")) return "room_full";
            if (Contains(message, "lease has expired") || Contains(message, "Room does not exist") ||
                Contains(message, "Room is not registered")) return "room_closed";
            if (Contains(message, "Connection approval is absent")) return "proof_not_ready";
            if (Contains(message, "must have joined")) return "membership_missing";
            if (Contains(result.Error.Error, "FunctionNotFound")) return "function_missing";
            return "script_failed";
        }

        public static bool CanRetryProof(string code, int attempt) => attempt < 3 &&
            (code == "proof_not_ready" || code == "service_unavailable");

        public static string Message(string code) => code switch
        {
            "room_closed" => "방이 종료되었거나 만료되었습니다. 목록을 새로고침해 주세요.",
            "password_invalid" => "방 암호가 일치하지 않습니다.",
            "kicked" => "이 방에서 강퇴되어 참가할 수 없습니다.",
            "room_full" => "방이 가득 찼습니다.",
            "membership_missing" => "방 참가 등록을 확인하지 못했습니다. 목록에서 다시 참가해 주세요.",
            "function_missing" => "서버의 방 인증 기능이 준비되지 않았습니다. CloudScript 배포를 확인해 주세요.",
            _ => "방 참가자 인증을 확인하지 못했습니다. 잠시 후 다시 참가해 주세요."
        };

        private static bool Contains(string text, string value) =>
            text != null && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
