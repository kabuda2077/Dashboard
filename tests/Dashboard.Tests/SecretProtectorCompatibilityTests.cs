namespace Dashboard.Tests;

// Only the current format is supported. Historical entropy is intentionally not read.
public sealed class SecretProtectorCompatibilityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("secret-中文")]
    public void CurrentUserCredentialRoundTripsWithoutWritingPlaintext(string secret)
    {
        var cipher = SecretProtector.Protect(secret);
        Assert.Equal(secret, SecretProtector.Unprotect(cipher));
        if (secret.Length > 0)
        {
            Assert.StartsWith("dpapi:v2:", cipher);
            Assert.DoesNotContain(secret, cipher);
        }
    }

    [Theory]
    [InlineData("plain-secret")]
    [InlineData("dpapi:old-format")]
    [InlineData("dpapi:v2:invalid")]
    public void InvalidOrOldCredentialDoesNotBecomeAnEmptySecret(string value) =>
        Assert.NotNull(Record.Exception(() => SecretProtector.Unprotect(value)));
}
