using OpenCTS.Core;

namespace OpenCTS.LanguageServices;

public sealed class DocumentAnalyzer
{
    public DocumentAnalysis Analyze(string source, string sourceName = "document.sasm", int version = 0, bool includeColors = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        CtsCompileResult compile = CtsCompiler.Compile(source, sourceName);
        List<StructuredDiagnostic> diagnostics = compile.Diagnostics.Select(diagnostic => new StructuredDiagnostic(
            diagnostic.Code,
            diagnostic.Severity.ToString().ToLowerInvariant(),
            diagnostic.Message,
            sourceName,
            new ScratchAsmTextRange(diagnostic.Span.Start, diagnostic.Span.End))).ToList();
        CtsCompilationUnit unit = source.Length <= 64 * 1024 * 1024 ? CtsParser.Parse(source).CompilationUnit :
            new CtsCompilationUnit([], new SourceSpan(new SourceLocation(1, 1), new SourceLocation(1, 1)));
        CtsRawBlocksDeclaration? raw = unit.Targets.SelectMany(target => target.Members).OfType<CtsRawBlocksDeclaration>().FirstOrDefault();
        if (raw is not null)
            diagnostics.Add(new StructuredDiagnostic("SASM5001", "info",
                "Raw blocks preserve the original block graph. Edit their JSON fields or inputs without removing referenced IDs.",
                sourceName, new ScratchAsmTextRange(raw.Span.Start, new SourceLocation(raw.Span.Start.Line, raw.Span.Start.Column + 9))));

        return new DocumentAnalysis(
            version,
            sourceName,
            includeColors ? CtsSyntaxClassifier.Classify(source) : [],
            diagnostics,
            SymbolIndex.Create(unit));
    }
}
