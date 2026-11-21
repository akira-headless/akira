using Akira.Core.Enum;

namespace Akira.Core.Entities;

public class PairingRequest : BaseEntity
{
    public required string Code { get; set; }
    public required string PollSecretHash { get; set; }

    public required string DeviceName { get; set; }
    public required string Client { get; set; }
    public string? ClientVersion { get; set; }
    public string? Platform { get; set; }

    public PairingStatus Status { get; set; } = PairingStatus.Pending;
    public DateTimeOffset ExpiresAt { get; set; }

    // The device created on approval, handed its token on the next poll
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }
}
