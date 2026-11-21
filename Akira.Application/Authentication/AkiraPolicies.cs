namespace Akira.Server.Authentication;

public static class AkiraPolicies
{
    // Anything acting as a user: a personal device, a profile session or an API key
    public const string User = "user";
    public const string Admin = "admin";

    // Any device token, including a shared device that has not picked a profile yet
    public const string Device = "device";

    // Any valid credential: a user, or a shared device without a profile
    public const string Client = "client";
}
