namespace Akira.Server.Authentication;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public TimeSpan PairingLifetime { get; set; } = TimeSpan.FromMinutes(10);

    // A profile picked on a shared device lasts this long, then the PIN is asked again
    public TimeSpan ProfileSessionLifetime { get; set; } = TimeSpan.FromHours(12);

    // The Data Protection key ring, relative to the working directory like the SQLite database
    public string KeysPath { get; set; } = "config/keys";

    // How long a browser stays signed in before it asks again
    public TimeSpan BrowserSessionLifetime { get; set; } = TimeSpan.FromDays(30);

    public int PinMaxAttempts { get; set; } = 5;
    public TimeSpan PinLockout { get; set; } = TimeSpan.FromMinutes(5);

    // Per client address, for the endpoints that work without credentials
    public int PairingStartsPerMinute { get; set; } = 10;
    public int AnonymousRequestsPerMinute { get; set; } = 60;
}
