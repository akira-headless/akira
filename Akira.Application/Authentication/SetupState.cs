using Akira.Core.Auth;

namespace Akira.Server.Authentication;

/// <summary>
/// Holds whether the first admin exists, and the one-time code that creates it until then.
/// </summary>
public sealed class SetupState
{
    private volatile bool _isComplete;

    public bool IsComplete => _isComplete;

    // Printed in the log at startup. Only someone who can read the server's log can claim it,
    // so a server reachable on the network before setup cannot be taken over by the first visitor.
    public string? SetupCode { get; private set; }

    public void Initialize(bool hasUsers)
    {
        _isComplete = hasUsers;
        SetupCode = hasUsers ? null : PairingCodes.New();
    }

    public void Complete()
    {
        _isComplete = true;
        SetupCode = null;
    }
}
