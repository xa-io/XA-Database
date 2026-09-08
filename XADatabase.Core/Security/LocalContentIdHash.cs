using System.Security.Cryptography;
using System.Text;

namespace XADatabase.Core.Security;

public static class LocalContentIdHash
{
    public static string CreateSalt()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    public static ulong Hash(ulong contentId, string salt)
    {
        if (contentId == 0)
            return 0;
        if (string.IsNullOrWhiteSpace(salt))
            throw new ArgumentException("A local ContentID salt is required.", nameof(salt));

        using var hmac = new HMACSHA256(Convert.FromHexString(salt));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(contentId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var value = BitConverter.ToUInt64(digest, 0);
        return value == 0 ? 1UL : value;
    }
}
