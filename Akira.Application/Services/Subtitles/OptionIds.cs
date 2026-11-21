using Akira.Core.Enum;

namespace Akira.Server.Services.Subtitles;

/// <summary>
/// Defines the option ids the options list hands out: what kind, which provider, and the provider's own reference.
/// </summary>
public static class OptionIds
{
    public static string Existing(string streamId) => "existing:" + streamId;

    public static string Download(string provider, string reference) => $"download:{provider}:{reference}";

    public static string Translate(string provider, string streamId) => $"translate:{provider}:{streamId}";

    public static (SubtitleRequestKind Kind, string Provider, string Reference)? Parse(string optionId)
    {
        var parts = optionId.Split(':', 3);
        return parts switch
        {
            ["existing", var stream] when stream.Length > 0 => (SubtitleRequestKind.Existing, "existing", stream),
            ["download", var provider, var reference] when provider.Length > 0 && reference.Length > 0 => (SubtitleRequestKind.Download, provider, reference),
            ["translate", var provider, var stream] when provider.Length > 0 && stream.Length > 0 => (SubtitleRequestKind.Translate, provider, stream),
            _ => null
        };
    }
}
