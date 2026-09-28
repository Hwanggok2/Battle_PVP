using System.Security.Cryptography;
using System.Text;

namespace BattlePvp.Networking
{
    public static class RoomAdmission
    {
        public static bool ValidCapacity(int capacity, int participants) => capacity >= 2 && capacity <= 8 && capacity >= participants;
        public static string PasswordHash(string roomId, string password)
        {
            if (string.IsNullOrEmpty(password)) return string.Empty;
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(roomId + ":" + password));
            var text = new StringBuilder(64);
            foreach (byte b in bytes) text.Append(b.ToString("x2"));
            return text.ToString();
        }
    }
}
