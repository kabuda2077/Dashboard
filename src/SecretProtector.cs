using System.Security.Cryptography;
using System.Text;

namespace Dashboard;

internal interface ISecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedSecret);
}

internal sealed class DpapiSecretProtector : ISecretProtector
{
    public static DpapiSecretProtector Instance { get; } = new();
    private DpapiSecretProtector() { }
    public string Protect(string secret) => SecretProtector.Protect(secret);
    public string Unprotect(string protectedSecret) => SecretProtector.Unprotect(protectedSecret);
}

internal static class SecretProtector
{
    internal const string ProtectedPrefix = "dpapi:v2:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Dashboard.Secret.v2");

    public static string Protect(string secret)
    {
        if (string.IsNullOrEmpty(secret)) return "";
        var bytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            return ProtectedPrefix + Convert.ToBase64String(
                ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static string Unprotect(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (!value.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
            throw new FormatException("Unsupported credential format.");
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(value[ProtectedPrefix.Length..]),
            Entropy, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
