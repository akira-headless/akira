using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Akira.Contracts.Accounts;
using Akira.Contracts.Library;
using Akira.Contracts.Media;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Akira.Server.Authentication;
using Akira.Server.Endpoints;

namespace Akira.Server.Bridges;

/// <summary>
/// Signs and reads the urls under /api/v1/signed: what they point at, for which device and credential, until when. The
/// token is that payload with an HMAC under a key only this server has, so a url cannot be made or changed elsewhere.
/// It holds ids, never a credential. It is short, as renderers cut long urls off, and the same payload gives the same
/// url.
/// </summary>
public sealed class SignedUrls : IMediaAccess
{
    public const string Route = "/signed";

    private const string KeyFile = "signed-url-key";
    private const int KeyLength = 32;
    private const int SignatureLength = 16;

    // Kind, expiry, device, id and flags
    private const int HeaderLength = 1 + 4 + 16 + 16 + 1;
    private const byte HasToken = 1;
    private const byte DetailIsId = 2;

    // Test hosts in one process share the keys folder
    private static readonly Lock KeyLock = new();

    private readonly byte[] _key;
    private readonly TimeProvider _time;

    public SignedUrls(
        IDataProtectionProvider provider,
        IOptions<AuthOptions> authOptions,
        TimeProvider time)
    {
        _key = LoadKey(provider.CreateProtector("Akira.SignedUrl.v2"), Path.GetFullPath(authOptions.Value.KeysPath));
        _time = time;
    }

    /// <inheritdoc />
    public string FileUrl(Caller caller, Guid fileId, string fileName, TimeSpan lifetime) =>
        Create(Target(SignedKind.File, fileId, null, caller), fileName, Expiry(lifetime));

    /// <inheritdoc />
    public string SubtitleUrl(Caller caller, Guid fileId, string streamId, TimeSpan lifetime) =>
        Create(Target(SignedKind.Subtitle, fileId, streamId, caller), "subtitle.vtt", Expiry(lifetime));

    /// <inheritdoc />
    public string ArtworkUrl(Caller caller, Guid itemId, ArtworkKind kind, ArtworkSize size, TimeSpan lifetime) =>
        Create(Target(SignedKind.Artwork, itemId, $"{kind}:{size}", caller), kind.ToString().ToLowerInvariant(), StableExpiry(lifetime));

    /// <inheritdoc />
    public string HlsUrl(Caller caller, Guid sessionId, TimeSpan lifetime) =>
        Create(Target(SignedKind.Hls, sessionId, caller.UserId.ToString("N"), caller), "master.m3u8", Expiry(lifetime));

    /// <inheritdoc />
    public string ProgressiveUrl(Caller caller, Guid fileId, TimeSpan lifetime) =>
        Create(Target(SignedKind.Progressive, fileId, caller.UserId.ToString("N"), caller), "stream.ts", Expiry(lifetime));

    /// <summary>
    /// Returns a url for every item's artwork, with {itemId} and {kind} to fill in. It opens nothing but artwork and stops when
    /// the device is revoked.
    /// </summary>
    public string ArtworkTemplate(Guid deviceId, TimeSpan lifetime)
    {
        var target = new SignedTarget(SignedKind.LibraryArtwork, Guid.Empty, null, deviceId);
        return $"{ItemRoutes.ApiPrefix}{Route}/{Token(target, Expiry(lifetime))}/{{itemId}}.{{kind}}";
    }

    /// <summary>
    /// Reads a token back, or null when it was not made here, was changed or has expired.
    /// </summary>
    public SignedTarget? Read(string token)
    {
        var bytes = new byte[Base64Url.GetMaxDecodedLength(token.Length)];
        if (!Base64Url.TryDecodeFromChars(token, bytes, out var length) || length < HeaderLength + SignatureLength)
        {
            return null;
        }

        var payload = bytes.AsSpan(0, length - SignatureLength);
        var signature = bytes.AsSpan(length - SignatureLength, SignatureLength);
        if (!CryptographicOperations.FixedTimeEquals(Sign(payload), signature))
        {
            return null;
        }

        var expires = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadUInt32BigEndian(payload[1..]));
        if (expires <= _time.GetUtcNow() || !Enum.IsDefined((SignedKind)payload[0]))
        {
            return null;
        }

        var flags = payload[HeaderLength - 1];
        var rest = payload[HeaderLength..];
        Guid? tokenId = null;
        if ((flags & HasToken) != 0)
        {
            if (rest.Length < 16)
            {
                return null;
            }

            tokenId = new Guid(rest[..16], bigEndian: true);
            rest = rest[16..];
        }

        string? detail = null;
        if ((flags & DetailIsId) != 0)
        {
            if (rest.Length != 16)
            {
                return null;
            }

            detail = new Guid(rest, bigEndian: true).ToString("N");
        }
        else if (rest.Length > 0)
        {
            detail = Encoding.UTF8.GetString(rest);
        }

        var deviceId = new Guid(payload.Slice(5, 16), bigEndian: true);
        var id = new Guid(payload.Slice(21, 16), bigEndian: true);
        return new SignedTarget((SignedKind)payload[0], id, detail, deviceId) { TokenId = tokenId };
    }

    private static SignedTarget Target(SignedKind kind, Guid id, string? detail, Caller caller) =>
        new(kind, id, detail, caller.DeviceId) { TokenId = caller.TokenId };

    private string Create(SignedTarget target, string name, DateTimeOffset expires) =>
        $"{ItemRoutes.ApiPrefix}{Route}/{Token(target, expires)}/{Uri.EscapeDataString(name)}";

    private string Token(SignedTarget target, DateTimeOffset expires)
    {
        // The user of a stream goes in as an id, half the length of its text
        var detailId = Guid.Empty;
        var detailIsId = target.Kind is SignedKind.Hls or SignedKind.Progressive && Guid.TryParseExact(target.Detail, "N", out detailId);
        var detail = detailIsId || target.Detail is null ? [] : Encoding.UTF8.GetBytes(target.Detail);
        var length = HeaderLength + (target.TokenId is null ? 0 : 16) + (detailIsId ? 16 : detail.Length);

        var bytes = new byte[length + SignatureLength];
        var payload = bytes.AsSpan(0, length);
        payload[0] = (byte)target.Kind;
        BinaryPrimitives.WriteUInt32BigEndian(payload[1..], (uint)Math.Clamp(expires.ToUnixTimeSeconds(), 0, uint.MaxValue));
        target.DeviceId.TryWriteBytes(payload.Slice(5, 16), bigEndian: true, out _);
        target.Id.TryWriteBytes(payload.Slice(21, 16), bigEndian: true, out _);

        var rest = payload[HeaderLength..];
        if (target.TokenId is { } tokenId)
        {
            payload[HeaderLength - 1] |= HasToken;
            tokenId.TryWriteBytes(rest, bigEndian: true, out _);
            rest = rest[16..];
        }

        if (detailIsId)
        {
            payload[HeaderLength - 1] |= DetailIsId;
            detailId.TryWriteBytes(rest, bigEndian: true, out _);
        }
        else
        {
            detail.CopyTo(rest);
        }

        Sign(payload).CopyTo(bytes.AsSpan(length));
        return Base64Url.EncodeToString(bytes);
    }

    private byte[] Sign(ReadOnlySpan<byte> payload) => HMACSHA256.HashData(_key, payload)[..SignatureLength];

    private DateTimeOffset Expiry(TimeSpan lifetime) => _time.GetUtcNow() + lifetime;

    // Artwork keeps its url for a whole lifetime at a time, so a client's image cache finds what it loaded: the url ends
    // at the end of the next period, which is one to two lifetimes away
    private DateTimeOffset StableExpiry(TimeSpan lifetime)
    {
        var period = Math.Max(60, (long)lifetime.TotalSeconds);
        var periods = _time.GetUtcNow().ToUnixTimeSeconds() / period;
        return DateTimeOffset.FromUnixTimeSeconds((periods + 2) * period);
    }

    // Kept next to the key ring and protected with it, like every secret the server reads back. With the key ring gone
    // the key is made again, and the urls handed out before end
    private static byte[] LoadKey(IDataProtector protector, string folder)
    {
        var path = Path.Combine(folder, KeyFile);
        lock (KeyLock)
        {
            if (File.Exists(path))
            {
                try
                {
                    var stored = Convert.FromBase64String(protector.Unprotect(File.ReadAllText(path)));
                    if (stored.Length == KeyLength)
                    {
                        return stored;
                    }
                }
                catch (Exception exception) when (exception is CryptographicException or FormatException)
                {
                }
            }

            var key = RandomNumberGenerator.GetBytes(KeyLength);
            Directory.CreateDirectory(folder);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, protector.Protect(Convert.ToBase64String(key)));
            File.Move(temporary, path, overwrite: true);
            return key;
        }
    }
}
