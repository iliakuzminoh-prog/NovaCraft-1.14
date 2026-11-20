using System.Security.Cryptography;
using System.Text;

namespace NovaCraft.Services;

public static class OfflineProfileRules
{
    public static string OfflineUuid(string username)
    {
        ArgumentNullException.ThrowIfNull(username);
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        var h = Convert.ToHexString(hash).ToLowerInvariant();
        return $"{h[..8]}-{h[8..12]}-{h[12..16]}-{h[16..20]}-{h[20..]}";
    }

    /// <summary>
    /// UUID produced by earlier NovaCraft builds (new Guid(md5).ToString()): wrong byte order,
    /// no version/variant bits. Kept only to recognise and migrate old profiles.
    /// </summary>
    public static string LegacyGuidUuid(string username)
    {
        ArgumentNullException.ThrowIfNull(username);
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        return new Guid(hash).ToString();
    }

    /// <summary>True for a 36-char UUID with version nibble 3 and RFC 4122 variant (8, 9, a, b).</summary>
    public static bool HasNameBasedUuidFormat(string? uuid)
    {
        if (uuid is null || uuid.Length != 36 || !Guid.TryParseExact(uuid, "D", out _))
            return false;
        return uuid[14] == '3' && "89abAB".Contains(uuid[19]);
    }

    /// <summary>
    /// True when the stored UUID must be replaced: it is missing, unparsable, a random v4 default,
    /// or the old byte-order-broken value. A name-based UUID that merely belongs to another nickname
    /// (profile renamed with "keep UUID") is NOT flagged.
    /// </summary>
    public static bool IsLegacyOrInvalidUuid(string username, string? uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return true;
        if (string.Equals(uuid, OfflineUuid(username), StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(uuid, LegacyGuidUuid(username), StringComparison.OrdinalIgnoreCase)) return true;
        return !HasNameBasedUuidFormat(uuid);
    }
}

public static class OfflineLaunchProfileRules
{
    public static string GetValidatedUsernameForLaunch(string? profileUsername)
    {
        var validation = NickValidator.Validate(profileUsername);
        if (!validation.ok)
            throw new ArgumentException(validation.error, nameof(profileUsername));

        return profileUsername!;
    }
}

public static class NickValidator
{
    public static (bool ok, string error) Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (false, "Ник не может быть пустым.");

        if (value.Length is < 3 or > 16)
            return (false, "Ник должен содержать от 3 до 16 символов.");

        foreach (var ch in value)
        {
            if (!((ch >= 'A' && ch <= 'Z') ||
                  (ch >= 'a' && ch <= 'z') ||
                  (ch >= '0' && ch <= '9') ||
                  ch == '_'))
                return (false, "Разрешены только A–Z, a–z, 0–9 и _.");
        }

        return (true, "");
    }
}
