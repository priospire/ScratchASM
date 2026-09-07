using OpenCTS.Core;

namespace OpenCTS.LanguageServices;

public sealed class ScratchAsmLanguageService
{
    private static readonly string[] Keywords =
    [
        "const", "enum", "struct", "stage", "sprite", "global var", "cloud global var", "sprite var",
        "local var", "list", "broadcast", "extension", "proc", "call", "repeat", "forever", "if", "else",
        "repeatuntil", "waituntil", "block", "substack"
    ];

    private readonly DocumentAnalyzer _analyzer = new();
    private readonly CatalogService _catalog = new();

    public DocumentAnalysis Analyze(string source, string sourceName = "document.sasm", int version = 0) =>
        _analyzer.Analyze(source, sourceName, version);

    public IReadOnlyList<ScratchAsmCompletion> GetCompletions(string source, int position, IReadOnlyList<ScratchAsmSymbol>? knownSymbols = null)
    {
        position = Math.Clamp(position, 0, source.Length);
        string prefix = PrefixAt(source, position);
        IEnumerable<ScratchAsmCompletion> keywords = Keywords.Select(keyword => new ScratchAsmCompletion(keyword, keyword, "keyword"));
        IEnumerable<ScratchAsmCompletion> aliases = CtsBlockRegistry.Definitions.Select(definition => new ScratchAsmCompletion(
            definition.Name,
            definition.Name,
            definition.Shape.ToString().ToLowerInvariant(),
            definition.Opcode));
        IEnumerable<ScratchAsmCompletion> symbols = (knownSymbols ?? SymbolIndex.Create(source)).Select(symbol => new ScratchAsmCompletion(
            symbol.Name,
            symbol.Name,
            symbol.Kind.ToString().ToLowerInvariant(),
            symbol.Detail));

        return keywords.Concat(aliases).Concat(symbols)
            .Where(item => prefix.Length == 0 || item.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .GroupBy(static item => item.Label, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static item => item.Label, StringComparer.Ordinal)
            .Take(512)
            .ToArray();
    }

    public IReadOnlyList<ScratchAsmSignature> GetSignatures(string source, int position)
    {
        position = Math.Clamp(position, 0, source.Length);
        int open = source.LastIndexOf('(', Math.Max(0, position - 1));
        if (open < 0)
        {
            return [];
        }

        string name = PrefixAt(source, open);
        return CtsBlockRegistry.Definitions.Where(definition => definition.Name == name)
            .Select(definition => new ScratchAsmSignature(
                $"{definition.Name}({string.Join(", ", definition.Bindings.Select(binding => binding.Name.ToLowerInvariant()))})",
                definition.Bindings.Select(binding => binding.Name).ToArray(),
                definition.Opcode))
            .ToArray();
    }

    public IReadOnlyList<ScratchAsmCatalogItem> SearchCatalog(string query, string? category = null, string? shape = null, int limit = 20) =>
        _catalog.Search(query, category, shape, limit);

    private static string PrefixAt(string source, int position)
    {
        int start = position;
        while (start > 0 && (char.IsLetterOrDigit(source[start - 1]) || source[start - 1] is '_' or '-' or '.'))
        {
            start--;
        }

        return source[start..position];
    }
}
