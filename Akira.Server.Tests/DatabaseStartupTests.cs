using Microsoft.Extensions.Options;
using Akira.Infrastructure.Configuration;
using Akira.Server.Testing;
using Xunit;

namespace Akira.Server.Tests;

public class DatabaseStartupTests
{
    [Fact]
    public async Task MariaDbWithoutSettings_FailsAtStartupNamingThem()
    {
        await using var factory = new AkiraApiFactory(new DatabaseOptions { Provider = DatabaseProvider.MariaDb });

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Database:Host is required", exception.Message);
        Assert.Contains("Database:Password is required", exception.Message);
    }
}
