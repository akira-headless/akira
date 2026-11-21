namespace Akira.Infrastructure.Integrations.Arr;

public sealed class ArrOptions
{
    public const string SectionName = "Arr";

    public List<ArrInstanceOptions> Instances { get; set; } = [];

    // Where several instances hold the same item, the first configured one comes first: its version, its
    // artwork, its description. An instance that is no longer configured comes last.
    public int OrderOf(string instanceKey)
    {
        var index = Instances.FindIndex(instance => instance.Key == instanceKey);
        return index < 0 ? int.MaxValue : index;
    }
}
