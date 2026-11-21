using Microsoft.EntityFrameworkCore;
using Akira.Server.Authentication;
using Akira.Core.Auth;
using Akira.Core.Entities;
using Akira.Core.Enum;
using Akira.Infrastructure.Data;
using Akira.Server.Interfaces.Services;
using Akira.Server.Models.Auth;

namespace Akira.Server.Services.Auth;

public class SetupService : ISetupService
{
    private readonly AkiraDbContext _dbContext;
    private readonly SetupState _state;
    private readonly ILogger<SetupService> _logger;

    public SetupService(
        AkiraDbContext dbContext,
        SetupState state,
        ILogger<SetupService> logger)
    {
        _dbContext = dbContext;
        _state = state;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var hasUsers = await _dbContext.Users.AnyAsync(cancellationToken);
        _state.Initialize(hasUsers);
        if (!_state.IsComplete)
        {
            _logger.LogWarning(
                "No users exist yet. Create the first admin with POST /api/v1/setup and setup code {SetupCode}",
                _state.SetupCode);
        }
    }

    public SetupStatus GetStatus() => new(!_state.IsComplete);

    /// <inheritdoc />
    public async Task<ServiceResult<SetupResponse>> CompleteAsync(SetupRequest request, CancellationToken cancellationToken)
    {
        if (_state.IsComplete || await _dbContext.Users.AnyAsync(cancellationToken))
        {
            return ServiceResult<SetupResponse>.Fail(ServiceError.Conflict, "Setup is already done.");
        }

        var code = PairingCodes.Normalize(request.SetupCode);
        if (code is null || _state.SetupCode is not { } expected || !Secrets.FixedTimeEquals(code, expected))
        {
            return ServiceResult<SetupResponse>.Fail(ServiceError.Forbidden, "The setup code is wrong. It is in the server log.");
        }

        if (!UserNames.IsValid(request.Name))
        {
            return ServiceResult<SetupResponse>.Fail(ServiceError.Invalid, $"A name is 1 to {UserNames.MaxLength} characters.");
        }

        if (request.Pin is not null && !Pins.IsValid(request.Pin))
        {
            return ServiceResult<SetupResponse>.Fail(ServiceError.Invalid, $"A PIN is {Pins.MinLength} to {Pins.MaxLength} digits.");
        }

        var admin = new User
        {
            Name = request.Name.Trim(),
            NormalizedName = UserNames.Normalize(request.Name),
            Role = UserRole.Admin,
            PinHash = request.Pin is null ? null : Pins.Hash(request.Pin)
        };

        var key = Secrets.NewApiKey();
        var apiKey = new ApiKey
        {
            UserId = admin.Id,
            User = admin,
            Name = "Setup",
            KeyHash = Secrets.Hash(key),
            Prefix = key[..Secrets.DisplayPrefixLength]
        };

        _dbContext.Users.Add(admin);
        _dbContext.ApiKeys.Add(apiKey);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _state.Complete();

        _logger.LogInformation("Setup done, {Name} is the first admin", admin.Name);
        var createdKey = new CreatedApiKey(apiKey.ToSummary(), key);
        return ServiceResult<SetupResponse>.Ok(new SetupResponse(admin.ToSummary(), createdKey));
    }
}
