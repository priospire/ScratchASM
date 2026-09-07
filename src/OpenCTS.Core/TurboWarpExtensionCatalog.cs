using System.Text.Json;

namespace OpenCTS.Core;

public sealed record TurboWarpExtension(string Id, string Name, string Url, string Color)
{
    public override string ToString() => Name + "  (" + Id + ")";
}

public static class TurboWarpExtensionCatalog
{
    public static IReadOnlyList<TurboWarpExtension> Entries { get; } = Read();
    private static IReadOnlyList<TurboWarpExtension> Read()
    {
        using Stream stream = typeof(TurboWarpExtensionCatalog).Assembly.GetManifestResourceStream("OpenCTS.Core.turbowarp-catalog.json")!;
        return JsonSerializer.Deserialize<TurboWarpExtension[]>(stream) ?? [];
    }
}
