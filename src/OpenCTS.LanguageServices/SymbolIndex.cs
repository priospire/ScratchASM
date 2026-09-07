using OpenCTS.Core;

namespace OpenCTS.LanguageServices;

public static class SymbolIndex
{
    public static IReadOnlyList<ScratchAsmSymbol> CreateTargetOutline(string source)
    {
        List<ScratchAsmSymbol> targets = [];
        int lineNumber = 0;
        foreach (ReadOnlySpan<char> line in source.AsSpan().EnumerateLines())
        {
            lineNumber++;
            ReadOnlySpan<char> header = line.TrimStart();
            if (!header.StartsWith("stage ", StringComparison.Ordinal) && !header.StartsWith("sprite ", StringComparison.Ordinal)) continue;
            if (!header.TrimEnd().EndsWith("{", StringComparison.Ordinal)) continue;
            CtsTargetDeclaration? target = CtsParser.Parse(header.ToString() + "\n}\n").CompilationUnit.Targets.FirstOrDefault();
            if (target is null) continue;
            targets.Add(new ScratchAsmSymbol(target.Name, ScratchAsmSymbolKind.Target,
                new ScratchAsmTextRange(new SourceLocation(lineNumber, 1), new SourceLocation(lineNumber, line.Length + 1)),
                Detail: target.IsStage ? "stage" : "sprite"));
        }
        return targets;
    }

    public static IReadOnlyList<ScratchAsmSymbol> Create(string source)
        => Create(CtsParser.Parse(source).CompilationUnit);

    public static IReadOnlyList<ScratchAsmSymbol> Create(CtsCompilationUnit unit)
    {
        List<ScratchAsmSymbol> symbols = [];
        foreach (CtsFileDeclaration declaration in unit.FileDeclarations)
        {
            switch (declaration)
            {
                case CtsConstDeclaration constant:
                    Add(symbols, constant.Name, ScratchAsmSymbolKind.Constant, constant.Span);
                    break;
                case CtsEnumDeclaration enumeration:
                    Add(symbols, enumeration.Name, ScratchAsmSymbolKind.Enum, enumeration.Span);
                    foreach (CtsEnumMember member in enumeration.Members)
                    {
                        Add(symbols, $"{enumeration.Name}.{member.Name}", ScratchAsmSymbolKind.EnumMember, member.Span, enumeration.Name);
                    }

                    break;
                case CtsStructDeclaration structure:
                    Add(symbols, structure.Name, ScratchAsmSymbolKind.Struct, structure.Span);
                    foreach (CtsStructField field in structure.Fields)
                    {
                        Add(symbols, field.Name, ScratchAsmSymbolKind.Field, field.Span, structure.Name, field.TypeName);
                    }

                    break;
            }
        }

        foreach (CtsTargetDeclaration target in unit.Targets)
        {
            Add(symbols, target.Name, ScratchAsmSymbolKind.Target, target.Span, detail: target.IsStage ? "stage" : "sprite");
            foreach (CtsTargetMember member in target.Members)
            {
                switch (member)
                {
                    case CtsVariableDeclaration variable:
                        Add(symbols, variable.Name, ScratchAsmSymbolKind.Variable, variable.Span, target.Name);
                        break;
                    case CtsStructInstanceDeclaration instance:
                        Add(symbols, instance.Name, ScratchAsmSymbolKind.Variable, instance.Span, target.Name, instance.TypeName);
                        break;
                    case CtsListDeclaration list:
                        Add(symbols, list.Name, ScratchAsmSymbolKind.List, list.Span, target.Name);
                        break;
                    case CtsBroadcastDeclaration broadcast:
                        Add(symbols, broadcast.Name, ScratchAsmSymbolKind.Broadcast, broadcast.Span, target.Name);
                        break;
                    case CtsExtensionDeclaration extension:
                        Add(symbols, extension.Name, ScratchAsmSymbolKind.Extension, extension.Span, target.Name);
                        break;
                }
            }

            foreach (CtsProcedureDefinition procedure in target.Scripts.OfType<CtsProcedureDefinition>())
            {
                Add(symbols, procedure.Name, ScratchAsmSymbolKind.Procedure, procedure.Span, target.Name);
                foreach (CtsProcedureParameter parameter in procedure.Parameters)
                {
                    Add(symbols, parameter.Name, ScratchAsmSymbolKind.Parameter, parameter.Span, procedure.Name, parameter.Type.ToString());
                }

                CollectLocals(symbols, procedure.Statements, procedure.Name);
            }
        }

        return symbols;
    }

    private static void CollectLocals(List<ScratchAsmSymbol> symbols, IEnumerable<CtsStatement> statements, string procedure)
    {
        foreach (CtsStatement statement in statements)
        {
            if (statement is CtsLocalVariableDeclaration local)
            {
                Add(symbols, local.Name, ScratchAsmSymbolKind.Local, local.Span, procedure);
            }

            if (statement is CtsStructuredStatement structured)
            {
                foreach (IReadOnlyList<CtsStatement> substack in structured.Substacks.Values)
                {
                    CollectLocals(symbols, substack, procedure);
                }
            }

            if (statement is CtsBlockStatement block)
            {
                CollectLocals(symbols, block.Statements, procedure);
                foreach (IReadOnlyList<CtsStatement> substack in block.NamedSubstacks.Values)
                {
                    CollectLocals(symbols, substack, procedure);
                }
            }
        }
    }

    private static void Add(
        List<ScratchAsmSymbol> symbols,
        string name,
        ScratchAsmSymbolKind kind,
        SourceSpan span,
        string? container = null,
        string? detail = null)
    {
        symbols.Add(new ScratchAsmSymbol(name, kind, new ScratchAsmTextRange(span.Start, span.End), container, detail));
    }
}
