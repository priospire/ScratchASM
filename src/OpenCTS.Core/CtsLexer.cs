namespace OpenCTS.Core;

public enum CtsTokenKind
{
    Identifier,
    Hat,
    Keyword,
    String,
    Number,
    Operator,
    Percent,
    Comment,
    Punctuation
}

public sealed record CtsToken(
    CtsTokenKind Kind,
    string Text,
    int Start,
    int Length,
    SourceSpan Span);

public static class CtsLexer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "stage",
        "project",
        "rawblocks",
        "origin",
        "sprite",
        "const",
        "enum",
        "struct",
        "global",
        "local",
        "proc",
        "as",
        "warp",
        "call",
        "block",
        "input",
        "field",
        "mutation",
        "var",
        "cloud",
        "list",
        "broadcast",
        "extension",
        "state",
        "rotationStyle",
        "costume",
        "center",
        "num",
        "str",
        "bool",
        "shadow",
        "repeat",
        "forever",
        "if",
        "else",
        "repeatuntil",
        "waituntil",
        "substack",
        "and",
        "or",
        "not"
    };

    public static IReadOnlyList<CtsToken> Lex(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<CtsToken> tokens = [];
        int line = 1;
        int column = 1;
        int index = 0;

        while (index < source.Length)
        {
            char current = source[index];
            if (current == '\r')
            {
                index++;
                if (index < source.Length && source[index] == '\n')
                {
                    index++;
                }

                line++;
                column = 1;
                continue;
            }

            if (current == '\n')
            {
                index++;
                line++;
                column = 1;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                index++;
                column++;
                continue;
            }

            int start = index;
            SourceLocation startLocation = new(line, column);

            if (current == '#')
            {
                while (index < source.Length && source[index] is not '\r' and not '\n')
                {
                    index++;
                    column++;
                }

                AddToken(tokens, CtsTokenKind.Comment, source, start, index, startLocation, line, column);
                continue;
            }

            if (current == '"')
            {
                index++;
                column++;
                bool escaped = false;
                while (index < source.Length)
                {
                    char ch = source[index];
                    index++;
                    column++;
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        break;
                    }
                    else if (ch is '\r' or '\n')
                    {
                        break;
                    }
                }

                AddToken(tokens, CtsTokenKind.String, source, start, index, startLocation, line, column);
                continue;
            }

            if (current == '@')
            {
                index++;
                column++;
                while (index < source.Length && (IsIdentifierPart(source[index]) || source[index] == '.'))
                {
                    index++;
                    column++;
                }

                AddToken(tokens, CtsTokenKind.Hat, source, start, index, startLocation, line, column);
                continue;
            }

            if (current == '%')
            {
                index++;
                column++;
                AddToken(tokens, CtsTokenKind.Percent, source, start, index, startLocation, line, column);
                continue;
            }

            if (IsOperatorStart(current))
            {
                index++;
                column++;
                if (index < source.Length && IsTwoCharacterOperator(current, source[index]))
                {
                    index++;
                    column++;
                }

                AddToken(tokens, CtsTokenKind.Operator, source, start, index, startLocation, line, column);
                continue;
            }

            if (char.IsDigit(current))
            {
                index++;
                column++;
                while (index < source.Length && (char.IsDigit(source[index]) || source[index] == '.'))
                {
                    index++;
                    column++;
                }

                if (index < source.Length && source[index] == 's')
                {
                    index++;
                    column++;
                }

                AddToken(tokens, CtsTokenKind.Number, source, start, index, startLocation, line, column);
                continue;
            }

            if (IsIdentifierStart(current))
            {
                index++;
                column++;
                while (index < source.Length && (IsIdentifierPart(source[index]) || source[index] == '.'))
                {
                    index++;
                    column++;
                }

                string text = source[start..index];
                CtsTokenKind kind = Keywords.Contains(text) ? CtsTokenKind.Keyword : CtsTokenKind.Identifier;
                AddToken(tokens, kind, source, start, index, startLocation, line, column);
                continue;
            }

            index++;
            column++;
            AddToken(tokens, CtsTokenKind.Punctuation, source, start, index, startLocation, line, column);
        }

        return tokens;
    }

    private static void AddToken(
        List<CtsToken> tokens,
        CtsTokenKind kind,
        string source,
        int start,
        int end,
        SourceLocation startLocation,
        int endLine,
        int endColumn)
    {
        tokens.Add(new CtsToken(
            kind,
            source[start..end],
            start,
            end - start,
            new SourceSpan(startLocation, new SourceLocation(endLine, endColumn))));
    }

    private static bool IsIdentifierStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    private static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or '-';
    }

    private static bool IsOperatorStart(char value)
    {
        return value is '+' or '-' or '*' or '/' or '^' or '<' or '>' or '=' or '!' or '&' or '|';
    }

    private static bool IsTwoCharacterOperator(char first, char second)
    {
        return (first, second) is ('+', '=') or ('=', '=') or ('<', '=') or ('>', '=') or ('!', '=') or ('&', '&') or ('|', '|');
    }
}
