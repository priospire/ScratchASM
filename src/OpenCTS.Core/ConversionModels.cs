namespace OpenCTS.Core;

public sealed record SourceLocation(int Line, int Column);

public enum DiagnosticSeverity
{
    Error,
    Warning,
    Info
}

public sealed record SourceSpan(SourceLocation Start, SourceLocation End);

public sealed record ValidationIssue(
    string Message,
    string JsonPath,
    SourceLocation? Location,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error,
    string? Code = null,
    SourceSpan? Span = null);

public sealed class ConversionResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public IReadOnlyList<ValidationIssue> Issues { get; init; } = Array.Empty<ValidationIssue>();
}

public sealed class ConversionOptions
{
    public bool Overwrite { get; init; } = true;

    public string? ScratchAsmSourceText { get; init; }

    public string? MonocodeSourceText { get; init; }

    public bool AttemptSafeRepair { get; init; }
}
