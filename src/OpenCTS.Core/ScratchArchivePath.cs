namespace OpenCTS.Core;

internal static class ScratchArchivePath
{
    public static bool IsSafe(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith('/') || path.Contains('\\')) return false;
        string[] parts = path.TrimEnd('/').Split('/');
        return parts.All(part => part.Length > 0 && part is not "." and not ".." &&
            part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }
}
