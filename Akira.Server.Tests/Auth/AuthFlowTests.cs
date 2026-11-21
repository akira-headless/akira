using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Akira.Server.Authentication;
using Akira.Infrastructure.Configuration;
using Akira.Server.Testing;
using Xunit;
using static Akira.Server.Testing.Auth.AuthApi;

namespace Akira.Server.Tests.Auth;

public abstract class AuthFlowTests
{
    protected abstract Task<(DatabaseOptions? Options, IAsyncDisposable? Database)> CreateDatabaseAsync(CancellationToken cancellationToken);

    [Fact]
    public async Task Setup_IsRequiredUntilTheFirstAdmin_AndNeedsTheCodeFromTheLog()
    {
        await using var api = await StartAsync(seedAdmin: false);
        using var anonymous = api.Factory.CreateAnonymousClient();

        var (gated, gatedBody) = await GetAsync(anonymous, "/api/v1/items");
        Assert.Equal(HttpStatusCode.Forbidden, gated);
        Assert.True(gatedBody.GetProperty("setupRequired").GetBoolean());
        Assert.True((await GetAsync(anonymous, "/api/v1/setup")).Body.GetProperty("setupRequired").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(anonymous, "/api/v1/health")).Status);

        var (wrong, _, _) = await PostAsync(anonymous, "/api/v1/setup", new { setupCode = "AAA-AAA", name = "Rowan" });
        Assert.Equal(HttpStatusCode.Forbidden, wrong);

        var code = api.Factory.Services.GetRequiredService<SetupState>().SetupCode!;
        var (created, body, _) = await PostAsync(anonymous, "/api/v1/setup", new { setupCode = code.ToLowerInvariant(), name = "Rowan" });
        Assert.Equal(HttpStatusCode.Created, created);
        Assert.Equal("admin", body.GetProperty("user").GetProperty("role").GetString());
        var key = body.GetProperty("apiKey").GetProperty("key").GetString()!;
        Assert.StartsWith("pak_", key);

        using var admin = api.Factory.CreateAnonymousClient();
        admin.DefaultRequestHeaders.Add(AkiraAuthenticationHandler.ApiKeyHeader, key);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(admin, "/api/v1/items")).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(anonymous, "/api/v1/setup", new { setupCode = code, name = "Again" })).Status);
        Assert.False((await GetAsync(anonymous, "/api/v1/setup")).Body.GetProperty("setupRequired").GetBoolean());
    }

    [Theory]
    [InlineData(null, "Authorization: Bearer")]
    [InlineData("nonsense", "neither a device token nor an API key")]
    [InlineData("pdt_unknown", "unknown or was revoked")]
    public async Task WithoutValidCredentials_IsUnauthorizedWithAHint(string? token, string hint)
    {
        await using var api = await StartAsync();
        using var client = token is null ? api.Factory.CreateAnonymousClient() : api.Factory.CreateClient(token);

        var (status, body) = await GetAsync(client, "/api/v1/items");

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Contains(hint, body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task PersonalPairing_HandsOutTheTokenOnce_AndActsAsTheOwner()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        using var anonymous = api.Factory.CreateAnonymousClient();
        var kid = await CreateUserAsync(admin, "Kid");

        var pairing = await StartPairingAsync(anonymous, "Kid's tablet");
        var (waiting, waitingBody, _) = await PollAsync(anonymous, pairing);
        Assert.Equal(HttpStatusCode.Accepted, waiting);
        Assert.Equal("pending", waitingBody.GetProperty("status").GetString());
        Assert.Contains(pairing.Code, (await GetAsync(admin, "/api/v1/auth/pairings")).Body.EnumerateArray()
            .Select(p => p.GetProperty("code").GetString()));

        var (approved, _, _) = await PostAsync(admin, "/api/v1/auth/pairings/approve",
            new { code = pairing.Code.Replace("-", "").ToLowerInvariant(), kind = "personal", userId = kid });
        Assert.Equal(HttpStatusCode.OK, approved);

        var (done, doneBody, _) = await PollAsync(anonymous, pairing);
        Assert.Equal(HttpStatusCode.OK, done);
        var token = doneBody.GetProperty("token").GetString()!;
        Assert.Equal(HttpStatusCode.Gone, (await PollAsync(anonymous, pairing)).Status);

        using var tablet = api.Factory.CreateClient(token);
        var me = (await GetAsync(tablet, "/api/v1/users/me")).Body;
        Assert.Equal(("Kid", "personal", "device"), (me.GetProperty("user").GetProperty("name").GetString(),
            me.GetProperty("device").GetProperty("kind").GetString(), me.GetProperty("credential").GetString()));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(tablet, "/api/v1/items")).Status);

        var (forbidden, forbiddenBody) = await GetAsync(tablet, "/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden);
        Assert.Contains("admin", forbiddenBody.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task PollWithTheWrongSecret_LooksLikeAnUnknownPairing()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateAnonymousClient();
        var pairing = await StartPairingAsync(anonymous);

        var (status, _, _) = await PollAsync(anonymous, pairing with { PollSecret = "guess" });

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task SharedDevice_NeedsAProfile_ChecksPins_AndLocksOut()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        using var anonymous = api.Factory.CreateAnonymousClient();
        var kid = await CreateUserAsync(admin, "Kid");
        var parent = await CreateUserAsync(admin, "Parent", pin: "2580");

        using var tv = api.Factory.CreateClient(await PairAsync(admin, anonymous, "shared"));

        var (needsProfile, hint) = await GetAsync(tv, "/api/v1/items");
        Assert.Equal(HttpStatusCode.Forbidden, needsProfile);
        Assert.Contains("shared device", hint.GetProperty("detail").GetString());

        var profiles = (await GetAsync(tv, "/api/v1/auth/profiles")).Body.EnumerateArray()
            .ToDictionary(p => p.GetProperty("name").GetString()!, p => p.GetProperty("hasPin").GetBoolean());
        Assert.Equal((false, true), (profiles["Kid"], profiles["Parent"]));

        // An admin without a PIN is not one tap away for whoever holds the remote
        Assert.DoesNotContain(TestAdmin.Name, profiles.Keys);
        var adminId = (await GetAsync(admin, "/api/v1/users/me")).Body.GetProperty("user").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(tv, $"/api/v1/auth/profiles/{adminId}/session", new { })).Status);

        var (kidSession, kidBody, _) = await PostAsync(tv, $"/api/v1/auth/profiles/{kid}/session", new { });
        Assert.Equal(HttpStatusCode.OK, kidSession);
        using var asKid = api.Factory.CreateClient(kidBody.GetProperty("token").GetString()!);
        var me = (await GetAsync(asKid, "/api/v1/users/me")).Body;
        Assert.Equal(("Kid", "profile"), (me.GetProperty("user").GetProperty("name").GetString(), me.GetProperty("credential").GetString()));

        // A profile left picked on the TV makes nothing that outlives it
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(asKid, "/api/v1/api-keys", new { name = "Mine now" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, await SendAsync(asKid, HttpMethod.Put, "/api/v1/users/me/pin", new { newPin = "1357" }));

        var (wrong, wrongBody, _) = await PostAsync(tv, $"/api/v1/auth/profiles/{parent}/session", new { pin = "0000" });
        Assert.Equal(HttpStatusCode.Forbidden, wrong);
        Assert.Contains("4 attempts left", wrongBody.GetProperty("detail").GetString());

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await PostAsync(tv, $"/api/v1/auth/profiles/{parent}/session", new { pin = "0000" });
        }

        var (locked, _, lockedResponse) = await PostAsync(tv, $"/api/v1/auth/profiles/{parent}/session", new { pin = "0000" });
        Assert.Equal(HttpStatusCode.TooManyRequests, locked);
        Assert.NotNull(lockedResponse.Headers.RetryAfter);

        // Even the right PIN waits out the lockout
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await PostAsync(tv, $"/api/v1/auth/profiles/{parent}/session", new { pin = "2580" })).Status);

        // A second lockout in a row takes twice as long
        api.Time.Advance(TimeSpan.FromMinutes(6));
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await PostAsync(tv, $"/api/v1/auth/profiles/{parent}/session", new { pin = "0000" });
        }

        api.Time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await PostAsync(tv, $"/api/v1/auth/profiles/{parent}/session", new { pin = "2580" })).Status);

        api.Time.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(tv, $"/api/v1/auth/profiles/{parent}/session", new { pin = "2580" })).Status);
    }

    [Fact]
    public async Task Browser_SignsInWithAnApiKey_IntoACookieItsScriptsCannotRead()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();

        var (status, body, response) = await PostAsync(admin, "/api/v1/auth/browser-session", new { name = "Firefox", platform = "Windows" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(("Firefox", "web"), (body.GetProperty("device").GetProperty("name").GetString(), body.GetProperty("device").GetProperty("client").GetString()));
        Assert.DoesNotContain("pdt_", body.GetRawText());
        var setCookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.StartsWith("akira_session=pdt_", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);

        using var browser = Browser(api, setCookie);
        Assert.Equal("device", (await GetAsync(browser, "/api/v1/users/me")).Body.GetProperty("credential").GetString());

        // Changing something needs the header no other site can send, and nothing works from another origin
        Assert.Equal(HttpStatusCode.Unauthorized, await SendAsync(browser, HttpMethod.Post, "/api/v1/api-keys", new { name = "x" }));
        browser.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(browser, "/api/v1/users/me")).Status);
        browser.DefaultRequestHeaders.Remove("Origin");
        browser.DefaultRequestHeaders.Add("Origin", "http://localhost");
        browser.DefaultRequestHeaders.Add("X-Akira-Request", "1");
        Assert.Equal(HttpStatusCode.Created, await SendAsync(browser, HttpMethod.Post, "/api/v1/api-keys", new { name = "From the browser" }));

        // A browser that is in use stays signed in: past half of the lifetime a request starts the 30 days again
        api.Time.Advance(TimeSpan.FromDays(20));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(browser, "/api/v1/users/me")).Status);
        api.Time.Advance(TimeSpan.FromDays(20));
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(browser, "/api/v1/users/me")).Status);

        api.Time.Advance(TimeSpan.FromDays(30) + TimeSpan.FromMinutes(1));
        var (expired, expiredBody) = await GetAsync(browser, "/api/v1/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, expired);
        Assert.Contains("browser session expired", expiredBody.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Browser_SignsOut_ByLosingItsDevice()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        var setCookie = (await PostAsync(admin, "/api/v1/auth/browser-session", new { })).Response.Headers.GetValues("Set-Cookie").Single();
        using var browser = Browser(api, setCookie);
        browser.DefaultRequestHeaders.Add("X-Akira-Request", "1");

        var response = await browser.DeleteAsync("/api/v1/auth/browser-session", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains("akira_session=;", response.Headers.GetValues("Set-Cookie").Single());
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(browser, "/api/v1/users/me")).Status);
        Assert.DoesNotContain("web", (await GetAsync(admin, "/api/v1/devices")).Body.EnumerateArray().Select(d => d.GetProperty("client").GetString()));
        // The API key it signed in with is not touched
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(admin, "/api/v1/users/me")).Status);
    }

    [Fact]
    public async Task PairedBrowser_KeepsItsDevice_AndTheTokenItsScriptsSawStopsWorking()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        using var anonymous = api.Factory.CreateAnonymousClient();
        var token = await PairAsync(admin, anonymous, "personal", client: "web");
        using var paired = api.Factory.CreateClient(token);
        var deviceId = (await GetAsync(paired, "/api/v1/users/me")).Body.GetProperty("device").GetProperty("id").GetGuid();

        // Kodi's token pasted into the sign in takes nothing over: Kodi stays signed in on its own device
        using var kodi = api.Factory.CreateClient(await PairAsync(admin, anonymous, "personal"));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(kodi, "/api/v1/auth/browser-session", new { })).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(kodi, "/api/v1/users/me")).Status);

        var (status, body, response) = await PostAsync(paired, "/api/v1/auth/browser-session", new { });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(deviceId, body.GetProperty("device").GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(paired, "/api/v1/users/me")).Status);
        using var browser = Browser(api, response.Headers.GetValues("Set-Cookie").Single());
        Assert.Equal(deviceId, (await GetAsync(browser, "/api/v1/users/me")).Body.GetProperty("device").GetProperty("id").GetGuid());

        using var shared = api.Factory.CreateClient(await PairAsync(admin, anonymous, "shared"));
        // A shared device without a profile is turned away before it gets that far
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(shared, "/api/v1/auth/browser-session", new { })).Status);
    }

    private static HttpClient Browser(StartedApi api, string setCookie)
    {
        var browser = api.Factory.CreateAnonymousClient();
        browser.DefaultRequestHeaders.Add("Cookie", setCookie.Split(';')[0]);
        return browser;
    }

    [Fact]
    public async Task ProfileSession_Expires_AndSwitchingEndsTheOldOne()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        using var anonymous = api.Factory.CreateAnonymousClient();
        var kid = await CreateUserAsync(admin, "Kid");
        var parent = await CreateUserAsync(admin, "Parent");
        using var tv = api.Factory.CreateClient(await PairAsync(admin, anonymous, "shared"));

        var first = (await PostAsync(tv, $"/api/v1/auth/profiles/{kid}/session", new { })).Body.GetProperty("token").GetString()!;
        using var asKid = api.Factory.CreateClient(first);

        // Asked with the device's own token, as the apps do
        var second = (await PostAsync(tv, $"/api/v1/auth/profiles/{parent}/session", new { })).Body.GetProperty("token").GetString()!;
        using var asParent = api.Factory.CreateClient(second);

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(asKid, "/api/v1/items")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(asParent, "/api/v1/items")).Status);

        api.Time.Advance(TimeSpan.FromHours(12) + TimeSpan.FromMinutes(1));
        var (expired, body) = await GetAsync(asParent, "/api/v1/items");
        Assert.Equal(HttpStatusCode.Unauthorized, expired);
        Assert.Contains("expired", body.GetProperty("detail").GetString());

        // The device itself keeps working, it only has to pick a profile again
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(tv, "/api/v1/auth/profiles")).Status);
    }

    [Fact]
    public async Task PinSignIn_AtHome_MakesAPersonalDevice_CountsWrongPins_AndRefusesAnUntrustedProxy()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        using var anonymous = api.Factory.CreateAnonymousClient();
        await CreateUserAsync(admin, "Kid");
        var parent = await CreateUserAsync(admin, "Parent", pin: "2580");

        // Only who set a PIN can sign in this way
        var names = (await GetAsync(anonymous, "/api/v1/auth/pin/users")).Body.EnumerateArray().Select(user => user.GetProperty("name").GetString()).ToList();
        Assert.Equal(["Parent"], names);

        var (wrong, wrongBody, _) = await PostAsync(anonymous, "/api/v1/auth/pin/sign-in",
            new { userId = parent, pin = "0000", deviceName = "Laptop", client = "web", clientVersion = (string?)null, platform = "Windows" });
        Assert.Equal(HttpStatusCode.Forbidden, wrong);
        Assert.Contains("4 attempts left", wrongBody.GetProperty("detail").GetString());

        var (signedIn, body, _) = await PostAsync(anonymous, "/api/v1/auth/pin/sign-in",
            new { userId = parent, pin = "2580", deviceName = "Laptop", client = "web", clientVersion = (string?)null, platform = "Windows" });
        Assert.Equal(HttpStatusCode.OK, signedIn);
        using var laptop = api.Factory.CreateClient(body.GetProperty("token").GetString()!);
        var me = (await GetAsync(laptop, "/api/v1/users/me")).Body;
        Assert.Equal(("Parent", "personal"), (me.GetProperty("user").GetProperty("name").GetString(), me.GetProperty("device").GetProperty("kind").GetString()));

        // Through a proxy Akira does not trust, the one asking could be anyone
        using var proxied = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/pin/users");
        proxied.Headers.Add("X-Forwarded-For", "203.0.113.9");
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.SendAsync(proxied, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task PersonalDevice_CannotStartProfileSessions()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        using var anonymous = api.Factory.CreateAnonymousClient();
        var kid = await CreateUserAsync(admin, "Kid");
        var parent = await CreateUserAsync(admin, "Parent");
        using var phone = api.Factory.CreateClient(await PairAsync(admin, anonymous, "personal", kid));

        Assert.Equal(["Kid"], (await GetAsync(phone, "/api/v1/auth/profiles")).Body.EnumerateArray()
            .Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(phone, $"/api/v1/auth/profiles/{parent}/session", new { })).Status);
    }

    [Fact]
    public async Task User_ApprovesOwnDevices_ButNotSharedOrSomeoneElses()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        using var anonymous = api.Factory.CreateAnonymousClient();
        var kid = await CreateUserAsync(admin, "Kid");
        var parent = await CreateUserAsync(admin, "Parent");
        using var kidPhone = api.Factory.CreateClient(await PairAsync(admin, anonymous, "personal", kid));

        var forShared = await StartPairingAsync(anonymous);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostAsync(kidPhone, "/api/v1/auth/pairings/approve", new { code = forShared.Code, kind = "shared" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostAsync(kidPhone, "/api/v1/auth/pairings/approve", new { code = forShared.Code, kind = "personal", userId = parent })).Status);

        var (own, body, _) = await PostAsync(kidPhone, "/api/v1/auth/pairings/approve", new { code = forShared.Code, kind = "personal" });
        Assert.Equal(HttpStatusCode.OK, own);
        Assert.Equal(kid, body.GetProperty("userId").GetGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(kidPhone, "/api/v1/auth/pairings")).Status);
    }

    [Fact]
    public async Task Pairing_Expires_AndCanBeDenied()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        using var anonymous = api.Factory.CreateAnonymousClient();

        var expiring = await StartPairingAsync(anonymous);
        api.Time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(HttpStatusCode.Gone, (await PollAsync(anonymous, expiring)).Status);
        Assert.Equal(HttpStatusCode.NotFound,
            (await PostAsync(admin, "/api/v1/auth/pairings/approve", new { code = expiring.Code, kind = "shared" })).Status);

        var denied = await StartPairingAsync(anonymous);
        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(admin, HttpMethod.Post, "/api/v1/auth/pairings/deny", new { code = denied.Code }));
        var (gone, body, _) = await PollAsync(anonymous, denied);
        Assert.Equal(HttpStatusCode.Gone, gone);
        Assert.Contains("denied", body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task RevokedDevice_LosesAccess()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        using var anonymous = api.Factory.CreateAnonymousClient();
        var kid = await CreateUserAsync(admin, "Kid");
        using var phone = api.Factory.CreateClient(await PairAsync(admin, anonymous, "personal", kid));
        var deviceId = (await GetAsync(phone, "/api/v1/users/me")).Body.GetProperty("device").GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(phone, HttpMethod.Delete, $"/api/v1/devices/{deviceId}"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(phone, "/api/v1/items")).Status);
    }

    [Fact]
    public async Task ApiKey_IsShownOnce_WorksAndCanBeRevoked()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();

        var (created, body, _) = await PostAsync(admin, "/api/v1/api-keys", new { name = "Home Assistant" });
        Assert.Equal(HttpStatusCode.Created, created);
        var key = body.GetProperty("key").GetString()!;
        var id = body.GetProperty("summary").GetProperty("id").GetGuid();

        var listed = (await GetAsync(admin, "/api/v1/api-keys")).Body.EnumerateArray().Single(k => k.GetProperty("id").GetGuid() == id);
        Assert.Equal(key[..12], listed.GetProperty("prefix").GetString());
        Assert.False(listed.TryGetProperty("key", out _));

        using var script = api.Factory.CreateAnonymousClient();
        script.DefaultRequestHeaders.Add(AkiraAuthenticationHandler.ApiKeyHeader, key);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(script, "/api/v1/items")).Status);
        Assert.Equal("apiKey", (await GetAsync(script, "/api/v1/users/me")).Body.GetProperty("credential").GetString());

        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(admin, HttpMethod.Delete, $"/api/v1/api-keys/{id}"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(script, "/api/v1/items")).Status);
    }

    [Fact]
    public async Task Users_HaveUniqueNames_AndTheLastAdminStays()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        var kid = await CreateUserAsync(admin, "Kid");

        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(admin, "/api/v1/users", new { name = "  KID ", role = "user" })).Status);

        var adminId = (await GetAsync(admin, "/api/v1/users/me")).Body.GetProperty("user").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, await SendAsync(admin, HttpMethod.Patch, $"/api/v1/users/{adminId}", new { role = "user" }));
        Assert.Equal(HttpStatusCode.Conflict, await SendAsync(admin, HttpMethod.Delete, $"/api/v1/users/{adminId}"));

        Assert.Equal(HttpStatusCode.OK, await SendAsync(admin, HttpMethod.Patch, $"/api/v1/users/{kid}", new { role = "admin" }));
        Assert.Equal(HttpStatusCode.OK, await SendAsync(admin, HttpMethod.Patch, $"/api/v1/users/{adminId}", new { role = "user" }));
    }

    [Fact]
    public async Task DeletedUser_LosesTheirDevices()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        using var anonymous = api.Factory.CreateAnonymousClient();
        var kid = await CreateUserAsync(admin, "Kid");
        using var phone = api.Factory.CreateClient(await PairAsync(admin, anonymous, "personal", kid));

        Assert.Equal(HttpStatusCode.NoContent, await SendAsync(admin, HttpMethod.Delete, $"/api/v1/users/{kid}"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(phone, "/api/v1/items")).Status);
    }

    [Fact]
    public async Task OwnPin_NeedsTheCurrentOneToChange()
    {
        await using var api = await StartAsync();
        using var admin = api.Factory.CreateClient();
        using var anonymous = api.Factory.CreateAnonymousClient();
        var kid = await CreateUserAsync(admin, "Kid");
        using var phone = api.Factory.CreateClient(await PairAsync(admin, anonymous, "personal", kid));

        Assert.Equal(HttpStatusCode.OK, await SendAsync(phone, HttpMethod.Put, "/api/v1/users/me/pin", new { newPin = "1357" }));
        Assert.Equal(HttpStatusCode.Forbidden, await SendAsync(phone, HttpMethod.Put, "/api/v1/users/me/pin", new { newPin = "2468" }));
        Assert.Equal(HttpStatusCode.OK,
            await SendAsync(phone, HttpMethod.Put, "/api/v1/users/me/pin", new { currentPin = "1357", newPin = "2468" }));
        Assert.Equal(HttpStatusCode.BadRequest,
            await SendAsync(phone, HttpMethod.Put, "/api/v1/users/me/pin", new { currentPin = "2468", newPin = "12" }));

        // An admin resets someone else's PIN without knowing it
        Assert.Equal(HttpStatusCode.OK, await SendAsync(admin, HttpMethod.Put, $"/api/v1/users/{kid}/pin", new { newPin = (string?)null }));
        Assert.False((await GetAsync(phone, "/api/v1/users/me")).Body.GetProperty("user").GetProperty("hasPin").GetBoolean());
    }

    [Fact]
    public async Task PairingStarts_AreRateLimitedPerAddress()
    {
        await using var api = await StartAsync(settings: new() { ["Auth:PairingStartsPerMinute"] = "2" });
        using var anonymous = api.Factory.CreateAnonymousClient();
        var body = new { deviceName = "TV", client = "Kodi" };

        Assert.Equal(HttpStatusCode.Created, (await PostAsync(anonymous, "/api/v1/auth/pairings", body)).Status);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(anonymous, "/api/v1/auth/pairings", body)).Status);
        var (limited, _, response) = await PostAsync(anonymous, "/api/v1/auth/pairings", body);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited);
        Assert.NotNull(response.Headers.RetryAfter);
    }

    [Fact]
    public async Task Pairing_RejectsNamesThatAreTooLongOrMissing()
    {
        await using var api = await StartAsync();
        using var anonymous = api.Factory.CreateAnonymousClient();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostAsync(anonymous, "/api/v1/auth/pairings", new { deviceName = new string('x', 101), client = "Kodi" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await PostAsync(anonymous, "/api/v1/auth/pairings", new { deviceName = "TV", client = "" })).Status);
    }

    private async Task<StartedApi> StartAsync(bool seedAdmin = true, Dictionary<string, string>? settings = null)
    {
        var (options, database) = await CreateDatabaseAsync(TestContext.Current.CancellationToken);
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var factory = new AkiraApiFactory(options) { SeedAdmin = seedAdmin, Time = time, Settings = settings ?? [] };
        _ = factory.Server;
        return new StartedApi(factory, time, database);
    }

    protected sealed record StartedApi(AkiraApiFactory Factory, FakeTimeProvider Time, IAsyncDisposable? Database) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            if (Database is not null)
            {
                await Database.DisposeAsync();
            }
        }
    }
}
