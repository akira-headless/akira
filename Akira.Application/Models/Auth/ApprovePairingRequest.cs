using Akira.Core.Enum;

namespace Akira.Server.Models.Auth;

/// <summary>
/// Represents the body of a request to approve a pairing code. UserId is the owner of a personal device, the approver
/// when null, and stays null for a shared device.
/// </summary>
public sealed record ApprovePairingRequest(string Code, DeviceKind Kind, Guid? UserId);
