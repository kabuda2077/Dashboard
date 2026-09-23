using System.Security.Cryptography;
using System.Text;

namespace Dashboard.Tests;

public sealed class SecretProtectorCompatibilityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("independent-test-secret")]
    public void CurrentDpapiRoundTripsWithoutPlaintext(string secret)
    {
        var protectedValue = SecretProtector.Protect(secret);
        Assert.Equal(secret, SecretProtector.Unprotect(protectedValue));
        if (secret.Length > 0)
        {
            Assert.StartsWith("dpapi:", protectedValue);
            Assert.DoesNotContain(secret, protectedValue);
        }
    }

    [Fact]
    public void ReadsLegacyDpapiEntropy()
    {
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes("legacy-test-secret"),
            Encoding.UTF8.GetBytes("MihomoDashboard.Secret.v1"), DataProtectionScope.CurrentUser);
        Assert.Equal("legacy-test-secret", SecretProtector.Unprotect("dpapi:" + Convert.ToBase64String(encrypted)));
    }

    [Theory]
    [InlineData("not-protected")]
    [InlineData("dpapi:broken-base64")]
    public void InvalidProtectedValueDoesNotBecomeAnEmptySecret(string value)
    {
        Assert.ThrowsAny<Exception>(() => SecretProtector.Unprotect(value));
    }
}
