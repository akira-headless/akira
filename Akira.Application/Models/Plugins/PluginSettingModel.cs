using Akira.Contracts.Plugins;

namespace Akira.Server.Models.Plugins;

/// <summary>
/// Represents a setting of a plugin. Values are never returned, only whether one is set.
/// </summary>
public sealed record PluginSettingModel(string Key, string Label, PluginSettingKind Kind, bool Required, bool IsSet, string? Description);
