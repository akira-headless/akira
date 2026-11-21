using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Akira.Server.Authentication;
using Akira.Core.Auth;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;

namespace Akira.Server.Testing;

/// <summary>
/// Creates an admin with a known API key when the test host starts, so tests about the library need no setup.
/// </summary>
public sealed class TestAdmin(IServiceScopeFactory scopeFactory, SetupState setup) : IHostedService
{
    public const string Name = "Test Admin";
    public const string ApiKey = Secrets.ApiKeyPrefix + "test-admin-key-for-the-test-host-only";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AkiraDbContext>();
        if (!dbContext.Users.Any(user => user.NormalizedName == UserNames.Normalize(Name)))
        {
            var admin = new User { Name = Name, NormalizedName = UserNames.Normalize(Name), Role = UserRole.Admin };
            dbContext.Users.Add(admin);
            dbContext.ApiKeys.Add(new ApiKey
            {
                UserId = admin.Id,
                Name = "Tests",
                KeyHash = Secrets.Hash(ApiKey),
                Prefix = ApiKey[..Secrets.DisplayPrefixLength]
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        setup.Complete();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
