using Akira.Core.Auth;
using Xunit;

namespace Akira.Core.Tests.Auth;

public class SecretsTests
{
    [Fact]
    public void Secrets_AreUniquePrefixedAndUrlSafe()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => Secrets.NewDeviceToken()).ToList();

        Assert.Equal(100, tokens.Distinct().Count());
        Assert.All(tokens, token =>
        {
            Assert.StartsWith("pdt_", token);
            Assert.Equal(4 + 43, token.Length);
            Assert.DoesNotContain(token, c => c is '+' or '/' or '=');
        });
        Assert.StartsWith("pak_", Secrets.NewApiKey());
    }

    [Fact]
    public void Hash_IsStableSha256Hex()
    {
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", Secrets.Hash("hello"));
        Assert.NotEqual(Secrets.Hash("pdt_a"), Secrets.Hash("pdt_b"));
    }
}
