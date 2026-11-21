namespace Akira.Infrastructure.Markers;

public sealed class MarkerOptions
{
    public const string SectionName = "Markers";

    public bool Enabled { get; set; } = true;

    // Fingerprints are kept so a new episode is compared without reading the others again
    public string Path { get; set; } = "config/fingerprints";

    // How much of the start an intro is looked for in, and of the end the credits
    public TimeSpan IntroWindow { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan CreditsWindow { get; set; } = TimeSpan.FromMinutes(4);

    // A recap is clips from anywhere in the episodes before it, so this fingerprints all of every episode once
    public bool Recaps { get; set; } = true;

    public TimeSpan MinLength { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan MaxIntro { get; set; } = TimeSpan.FromSeconds(150);

    public string ResolvePath() => System.IO.Path.GetFullPath(Path);
}
