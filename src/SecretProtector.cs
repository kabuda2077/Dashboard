using System.Security.Cryptography;
using System.Text;

namespace Dashboard;

internal static class SecretProtector
{
    private const string ProtectedPrefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Dashboard.Secret.v1");
    private static readonly byte[] LegacyEntropy = Encoding.UTF8.GetBytes("MihomoDashboard.Secret.v1");

    public static string Protect(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return "";
        }

        var plainBytes = Encoding.UTF8.GetBytes(secret);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return ProtectedPrefix + Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret))
        {
            return "";
        }

        if (!protectedSecret.StartsWith(ProtectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return protectedSecret;
        }

        var protectedBytes = Convert.FromBase64String(protectedSecret[ProtectedPrefix.Length..]);
        var plainBytes = UnprotectBytes(protectedBytes);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] UnprotectBytes(byte[] protectedBytes)
    {
        try
        {
            return ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return ProtectedData.Unprotect(protectedBytes, LegacyEntropy, DataProtectionScope.CurrentUser);
        }
    }
}
