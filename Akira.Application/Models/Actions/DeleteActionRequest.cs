using Akira.Core.Enum;

namespace Akira.Server.Models.Actions;

/// <summary>
/// Represents the body of a request to delete the files of an item. Scope, DeleteFiles, Unmonitor and Instances are all
/// required, and Search needs DeleteFiles true and Unmonitor false.
/// </summary>
public sealed record DeleteActionRequest(
    ActionScope? Scope,
    bool? DeleteFiles,
    bool? Unmonitor,
    IReadOnlyList<string>? Instances,
    bool Search = false,
    bool DryRun = false);
