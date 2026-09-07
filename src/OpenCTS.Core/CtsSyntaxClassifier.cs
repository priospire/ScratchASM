namespace OpenCTS.Core;

public sealed record CtsColorSpan(int Start, int Length, string Color, string Kind);

public static class CtsSyntaxClassifier
{
    private static readonly IReadOnlyDictionary<string, CtsAliasDefinition> Aliases =
        CtsBlockRegistry.Definitions.GroupBy(item => item.Name).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    private static readonly HashSet<string> CostumeDrawingWords = new(StringComparer.Ordinal)
    {
        "rect", "line", "circle", "ellipse", "path", "text",
        "fill", "stroke", "width", "r", "rx", "ry", "font", "size", "anchor"
    };

    public static IReadOnlyList<CtsColorSpan> Classify(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        IReadOnlyList<CtsToken> tokens = CtsLexer.Lex(source);
        return ClassifyTokens(source, tokens);
    }

    public static IReadOnlyList<CtsColorSpan> ClassifyLines(string source, IEnumerable<(int Start, int Length, int Line)> lines)
    {
        List<CtsToken> tokens = [];
        foreach ((int start, int length, int line) in lines)
            foreach (CtsToken token in CtsLexer.Lex(source.Substring(start, length)))
                tokens.Add(token with
                {
                    Start = token.Start + start,
                    Span = new SourceSpan(token.Span.Start with { Line = line }, token.Span.End with { Line = line })
                });
        return ClassifyTokens(source, tokens);
    }

    private static IReadOnlyList<CtsColorSpan> ClassifyTokens(string source, IReadOnlyList<CtsToken> tokens)
    {
        ClassificationContext context = BuildContext(tokens);
        List<CtsColorSpan> spans = [];
        for (int index = 0; index < tokens.Count; index++)
        {
            CtsToken token = tokens[index];
            string? color = GetColor(index, tokens, source, context);
            if (color is null)
            {
                continue;
            }

            spans.Add(new CtsColorSpan(token.Start, token.Length, color, token.Kind.ToString()));
        }

        return spans;
    }

    private static string? GetColor(
        int tokenIndex,
        IReadOnlyList<CtsToken> tokens,
        string source,
        ClassificationContext context)
    {
        CtsToken token = tokens[tokenIndex];
        if (context.VariableTokenStarts.Contains(token.Start))
        {
            return ScratchCategoryColors.Variables;
        }

        if (context.OperatorTokenStarts.Contains(token.Start))
        {
            return ScratchCategoryColors.Operators;
        }

        if (context.MyBlockTokenStarts.Contains(token.Start))
        {
            return ScratchCategoryColors.MyBlocks;
        }

        if (context.CostumeTokenStarts.Contains(token.Start))
        {
            return ScratchCategoryColors.Looks;
        }

        if (token.Kind == CtsTokenKind.Hat)
        {
            if (token.Text == "@block")
            {
                return ScratchCategoryColors.RawSyntax;
            }

            string name = token.Text[1..] switch
            {
                "greenflag" => "event.greenflag",
                "key" => "event.key",
                "clicked" => "event.clicked",
                "clone" => "control.clone",
                string value => value
            };
            return Aliases.GetValueOrDefault(name)?.CategoryColor
                ?? ScratchCategoryColors.Events;
        }

        if (token.Kind == CtsTokenKind.Percent)
        {
            int lineStart = token.Start;
            while (lineStart > 0 && source[lineStart - 1] is not '\r' and not '\n')
            {
                lineStart--;
            }

            return source[lineStart..token.Start].Trim().Length == 0
                ? ScratchCategoryColors.RawSyntax
                : ScratchCategoryColors.Operators;
        }

        if (token.Kind == CtsTokenKind.Operator)
        {
            return token.Text is "=" or "+="
                ? GetAssignmentColor(tokenIndex, tokens, context)
                : ScratchCategoryColors.Operators;
        }

        if (token.Kind == CtsTokenKind.Keyword)
        {
            if (token.Text is "proc" or "call")
            {
                return ScratchCategoryColors.MyBlocks;
            }

            if (token.Text is "repeat" or "forever" or "if" or "else" or "repeatuntil" or "waituntil")
            {
                return ScratchCategoryColors.Control;
            }

            if (token.Text == "substack")
            {
                return ScratchCategoryColors.Control;
            }

            if (token.Text is "and" or "or" or "not")
            {
                return ScratchCategoryColors.Operators;
            }

            if (token.Text is "const" or "enum")
            {
                return ScratchCategoryColors.Operators;
            }

            if (token.Text is "var" or "cloud" or "global" or "local" or "struct" or "num" or "str" or "bool")
            {
                return ScratchCategoryColors.Variables;
            }

            if (token.Text == "list")
            {
                return ScratchCategoryColors.Lists;
            }

            if (token.Text == "broadcast")
            {
                return ScratchCategoryColors.Events;
            }

            if (token.Text is "extension")
            {
                return ScratchCategoryColors.Extensions;
            }

            if (token.Text is "costume" or "state" or "rotationStyle" or "center")
            {
                return ScratchCategoryColors.Looks;
            }

            return token.Text is "block" or "input" or "field" or "mutation"
                ? ScratchCategoryColors.RawSyntax
                : ScratchCategoryColors.NeutralKeyword;
        }

        if (token.Kind == CtsTokenKind.Identifier)
        {
            string? nativeColor = CtsBlockRegistry.GetOpcodeColor(token.Text);
            if (nativeColor is not null) return nativeColor;
            string prefix = token.Text.Split('_')[0];
            if (context.ExtensionColors.TryGetValue(prefix, out string? extensionColor)) return extensionColor;
            if (context.Broadcasts.Contains(token.Text))
            {
                return ScratchCategoryColors.Events;
            }

            if (context.Constants.Contains(token.Text) || context.Enums.Contains(token.Text))
            {
                return ScratchCategoryColors.Operators;
            }

            if (context.Structs.Contains(token.Text))
            {
                return ScratchCategoryColors.Variables;
            }

            CtsAliasDefinition? exactAlias = Aliases.GetValueOrDefault(token.Text);
            if (exactAlias is not null)
            {
                return exactAlias.CategoryColor;
            }

            int dotIndex = token.Text.IndexOf('.', StringComparison.Ordinal);
            string category = dotIndex >= 0 ? token.Text[..dotIndex] : token.Text;
            if (context.Enums.Contains(category))
            {
                return ScratchCategoryColors.Operators;
            }
            if (context.Lists.Contains(category))
            {
                return ScratchCategoryColors.Lists;
            }

            if (context.Variables.Contains(category))
            {
                return ScratchCategoryColors.Variables;
            }

            if (context.Procedures.Contains(category))
            {
                return ScratchCategoryColors.MyBlocks;
            }

            if (IsOperatorFunction(token.Text))
            {
                return ScratchCategoryColors.Operators;
            }

            return CtsBlockRegistry.GetCategoryColor(category);
        }

        if (token.Kind == CtsTokenKind.String)
        {
            if (token.Text.Length >= 2)
            {
                string? opcodeColor = CtsBlockRegistry.GetOpcodeColor(token.Text[1..^1]);
                if (opcodeColor is not null)
                {
                    return opcodeColor;
                }
                string prefix = token.Text[1..^1].Split('_')[0];
                if (context.ExtensionColors.TryGetValue(prefix, out string? extensionColor)) return extensionColor;
            }

            return ScratchCategoryColors.StringLiteral;
        }

        if (token.Kind == CtsTokenKind.Number)
        {
            return ScratchCategoryColors.NumberLiteral;
        }

        if (token.Kind == CtsTokenKind.Comment)
        {
            return ScratchCategoryColors.Comment;
        }

        return null;
    }

    private static ClassificationContext BuildContext(IReadOnlyList<CtsToken> tokens)
    {
        HashSet<string> variables = CollectDeclaredNames(tokens, "var");
        HashSet<string> lists = CollectDeclaredNames(tokens, "list");
        HashSet<string> broadcasts = CollectDeclaredNames(tokens, "broadcast");
        HashSet<string> constants = CollectDeclaredNames(tokens, "const");
        HashSet<string> enums = CollectDeclaredNames(tokens, "enum");
        HashSet<string> structs = CollectDeclaredNames(tokens, "struct");
        HashSet<string> procedures = new(StringComparer.Ordinal);
        HashSet<int> myBlockTokenStarts = [];
        HashSet<int> costumeTokenStarts = [];
        HashSet<int> variableTokenStarts = [];
        HashSet<int> operatorTokenStarts = [];
        Dictionary<string, string> extensionColors = TurboWarpExtensionCatalog.Entries.GroupBy(entry => entry.Id)
            .ToDictionary(group => group.Key, group => group.First().Color, StringComparer.Ordinal);
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i].Text != "extension" || tokens[i + 1].Kind is not (CtsTokenKind.Identifier or CtsTokenKind.String)) continue;
            string color = extensionColors.GetValueOrDefault(tokens[i + 1].Text.Trim('"'), ScratchCategoryColors.Extensions);
            if (i + 3 < tokens.Count && tokens[i + 3].Span.Start.Line == tokens[i].Span.Start.Line &&
                System.Text.RegularExpressions.Regex.IsMatch(tokens[i + 3].Text, "^\"#[0-9a-fA-F]{6}\"$")) color = tokens[i + 3].Text[1..^1];
            extensionColors[tokens[i + 1].Text.Trim('"')] = color;
        }

        for (int index = 0; index < tokens.Count; index++)
        {
            CtsToken token = tokens[index];
            if (token.Text is "const" or "enum")
            {
                if (index + 1 < tokens.Count && tokens[index + 1].Kind == CtsTokenKind.Identifier)
                {
                    operatorTokenStarts.Add(tokens[index + 1].Start);
                }
            }
            else if (token.Text == "struct")
            {
                if (index + 1 < tokens.Count && tokens[index + 1].Kind == CtsTokenKind.Identifier)
                {
                    variableTokenStarts.Add(tokens[index + 1].Start);
                }
            }

            if (token.Kind == CtsTokenKind.Identifier && index + 2 < tokens.Count &&
                tokens[index + 1].Text == ":" && structs.Contains(tokens[index + 2].Text))
            {
                variables.Add(token.Text);
                variableTokenStarts.Add(token.Start);
                variableTokenStarts.Add(tokens[index + 2].Start);
            }

            if (token.Kind == CtsTokenKind.Identifier && index + 2 < tokens.Count &&
                tokens[index + 1].Text == ":" && tokens[index + 2].Text is "num" or "str" or "bool")
            {
                variableTokenStarts.Add(token.Start);
            }
        }

        for (int index = 0; index < tokens.Count; index++)
        {
            CtsToken token = tokens[index];
            if (token.Text == "proc" && index + 1 < tokens.Count && tokens[index + 1].Kind == CtsTokenKind.Identifier)
            {
                CtsToken procedure = tokens[index + 1];
                procedures.Add(procedure.Text);
                myBlockTokenStarts.Add(procedure.Start);

                HashSet<string> procedureParameters = new(StringComparer.Ordinal);
                bool inParameters = false;
                for (int cursor = index + 2; cursor < tokens.Count && tokens[cursor].Span.Start.Line == token.Span.Start.Line; cursor++)
                {
                    CtsToken candidate = tokens[cursor];
                    if (candidate.Text == "(")
                    {
                        inParameters = true;
                        continue;
                    }

                    if (candidate.Text == ")")
                    {
                        break;
                    }

                    if (inParameters && candidate.Kind == CtsTokenKind.Identifier &&
                        cursor + 1 < tokens.Count && tokens[cursor + 1].Text == ":")
                    {
                        procedureParameters.Add(candidate.Text);
                        myBlockTokenStarts.Add(candidate.Start);
                    }
                }

                int bodyCursor = index + 2;
                while (bodyCursor < tokens.Count && tokens[bodyCursor].Span.Start.Line == token.Span.Start.Line)
                {
                    bodyCursor++;
                }

                int currentLine = -1;
                for (; bodyCursor < tokens.Count; bodyCursor++)
                {
                    CtsToken candidate = tokens[bodyCursor];
                    if (candidate.Span.Start.Line != currentLine)
                    {
                        currentLine = candidate.Span.Start.Line;
                        if (candidate.Span.Start.Column <= token.Span.Start.Column)
                        {
                            break;
                        }
                    }

                    if (candidate.Kind == CtsTokenKind.Identifier && procedureParameters.Contains(candidate.Text))
                    {
                        myBlockTokenStarts.Add(candidate.Start);
                    }
                }
            }
            else if (token.Text == "call" && index + 1 < tokens.Count && tokens[index + 1].Kind == CtsTokenKind.Identifier)
            {
                myBlockTokenStarts.Add(tokens[index + 1].Start);
            }
        }

        bool awaitingCostumeBody = false;
        int costumeDepth = 0;
        foreach (CtsToken token in tokens)
        {
            if (token.Text == "costume" && token.Kind == CtsTokenKind.Keyword)
            {
                awaitingCostumeBody = true;
            }

            if (awaitingCostumeBody && token.Text == "{")
            {
                awaitingCostumeBody = false;
                costumeDepth = 1;
                continue;
            }

            if (costumeDepth == 0)
            {
                continue;
            }

            if (token.Text == "{")
            {
                costumeDepth++;
            }
            else if (token.Text == "}")
            {
                costumeDepth--;
            }
            else if (CostumeDrawingWords.Contains(token.Text))
            {
                costumeTokenStarts.Add(token.Start);
            }
        }

        return new ClassificationContext(
            variables,
            lists,
            broadcasts,
            procedures,
            constants,
            enums,
            structs,
            myBlockTokenStarts,
            costumeTokenStarts,
            variableTokenStarts,
            operatorTokenStarts,
            extensionColors);
    }

    private static string GetAssignmentColor(
        int tokenIndex,
        IReadOnlyList<CtsToken> tokens,
        ClassificationContext context)
    {
        CtsToken token = tokens[tokenIndex];
        int first = tokenIndex;
        int last = tokenIndex + 1;
        while (first > 0 && tokens[first - 1].Span.Start.Line == token.Span.Start.Line) first--;
        while (last < tokens.Count && tokens[last].Span.Start.Line == token.Span.Start.Line) last++;
        IEnumerable<CtsToken> lineTokens = Enumerable.Range(first, last - first).Select(index => tokens[index]);

        if (lineTokens.Any(candidate => context.CostumeTokenStarts.Contains(candidate.Start)) ||
            lineTokens.Any(candidate => candidate.Text is "state" or "costume" or "center" or "rotationStyle"))
        {
            return ScratchCategoryColors.Looks;
        }

        if (lineTokens.Any(candidate => candidate.Text is "block" or "input" or "field" or "mutation"))
        {
            return ScratchCategoryColors.RawSyntax;
        }

        if (lineTokens.Any(candidate => candidate.Text == "broadcast" || context.Broadcasts.Contains(candidate.Text)))
        {
            return ScratchCategoryColors.Events;
        }

        if (lineTokens.Any(candidate => candidate.Text == "proc"))
        {
            return ScratchCategoryColors.MyBlocks;
        }

        if (lineTokens.Any(candidate => candidate.Text == "list" || context.Lists.Contains(candidate.Text)))
        {
            return ScratchCategoryColors.Lists;
        }

        return ScratchCategoryColors.Variables;
    }

    private static HashSet<string> CollectDeclaredNames(IReadOnlyList<CtsToken> tokens, string keyword)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            if (tokens[i].Text == keyword && tokens[i + 1].Kind == CtsTokenKind.Identifier)
            {
                names.Add(tokens[i + 1].Text);
            }
        }

        return names;
    }

    private static bool IsOperatorFunction(string name)
    {
        return name is "add" or "subtract" or "multiply" or "divide" or "random" or "greater" or "less" or
            "equals" or "and" or "or" or "not" or "join" or "letter" or "length" or "contains" or "mod" or
            "round" or "abs" or "floor" or "ceil" or "ceiling" or "sqrt" or "sin" or "cos" or "tan" or
            "asin" or "acos" or "atan" or "ln" or "log" or "log10" or "exp" or "pow10";
    }

    private sealed record ClassificationContext(
        IReadOnlySet<string> Variables,
        IReadOnlySet<string> Lists,
        IReadOnlySet<string> Broadcasts,
        IReadOnlySet<string> Procedures,
        IReadOnlySet<string> Constants,
        IReadOnlySet<string> Enums,
        IReadOnlySet<string> Structs,
        IReadOnlySet<int> MyBlockTokenStarts,
        IReadOnlySet<int> CostumeTokenStarts,
        IReadOnlySet<int> VariableTokenStarts,
        IReadOnlySet<int> OperatorTokenStarts,
        IReadOnlyDictionary<string, string> ExtensionColors);
}
