namespace Akira.Infrastructure.Integrations.Lingarr;

/// <summary>
/// Represents Lingarr's answer to a file translation, with the id of the translation request it started.
/// </summary>
internal sealed record LingarrJob(int JobId);
