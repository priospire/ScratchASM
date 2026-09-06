namespace OpenCTS.Core;

public static class ScratchAsmLanguage
{
    public const string DisplayName = "ScratchASM";
    public const string CanonicalExtension = ".sasm";
    public const string CompatibilityExtension = ".mono";

    public static bool IsSupportedSourceName(string? sourceName)
    {
        return HasExtension(sourceName, CanonicalExtension) || HasExtension(sourceName, CompatibilityExtension) || HasExtension(sourceName, ".cts");
    }

    public static bool IsCanonicalSourceName(string? sourceName)
    {
        return HasExtension(sourceName, CanonicalExtension);
    }

    public static bool IsCompatibilitySourceName(string? sourceName)
    {
        return HasExtension(sourceName, CompatibilityExtension);
    }

    private static bool HasExtension(string? sourceName, string extension)
    {
        return sourceName?.EndsWith(extension, StringComparison.OrdinalIgnoreCase) == true;
    }
}
