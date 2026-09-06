using System.Text;

namespace OpenCTS.Core;

public static class ScratchAsmSourceRepairer
{
    public static ScratchAsmSourceRepairResult Repair(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<ValidationIssue> issues = [];
        string repaired = source;

        if (repaired.Length > 0 && repaired[0] == '\uFEFF')
        {
            repaired = repaired[1..];
            AddIssue(issues, "Removed UTF-8 byte order mark.", 1, 1);
        }

        string normalized = repaired.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);
        if (!string.Equals(normalized, repaired, StringComparison.Ordinal))
        {
            repaired = normalized;
            AddIssue(issues, "Normalized line endings.", 1, 1);
        }

        repaired = ReplaceCharacters(repaired, issues);
        repaired = RewriteScratchLikeStatements(repaired, issues);
        repaired = WrapImplicitStage(repaired, issues);
        repaired = CloseMissingBraces(repaired, issues);

        if (repaired.Length > 0 && !repaired.EndsWith('\n'))
        {
            repaired += "\n";
            SourceLocation location = OffsetToLocation(repaired, Math.Max(0, repaired.Length - 1));
            AddIssue(issues, "Added final newline.", location.Line, location.Column);
        }

        return new ScratchAsmSourceRepairResult(repaired, issues);
    }

    private static string ReplaceCharacters(string source, List<ValidationIssue> issues)
    {
        StringBuilder builder = new(source.Length);
        bool changedTabs = false;
        bool changedQuotes = false;
        bool changedSpaces = false;
        bool inString = false;
        bool smartString = false;
        bool escaped = false;
        bool inComment = false;

        for (int i = 0; i < source.Length; i++)
        {
            char character = source[i];
            if (inComment)
            {
                builder.Append(character);
                if (character == '\n') inComment = false;
                continue;
            }
            if (inString)
            {
                if (!escaped && smartString && character == '\u201D')
                {
                    builder.Append('"');
                    inString = false;
                    smartString = false;
                    changedQuotes = true;
                    continue;
                }
                builder.Append(character);
                if (!escaped && character == '"') inString = false;
                escaped = !escaped && character == '\\';
                continue;
            }
            if (character == '#') { inComment = true; builder.Append(character); continue; }
            if (character == '"') { inString = true; builder.Append(character); continue; }
            if (character == '\u201C')
            {
                builder.Append('"'); inString = true; smartString = true; changedQuotes = true; continue;
            }
            switch (character)
            {
                case '\t':
                    builder.Append("  ");
                    changedTabs = true;
                    break;
                case '\u201C':
                case '\u201D':
                    builder.Append('"');
                    changedQuotes = true;
                    break;
                case '\u2018':
                case '\u2019':
                    builder.Append('\'');
                    changedQuotes = true;
                    break;
                case '\u00A0':
                    builder.Append(' ');
                    changedSpaces = true;
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        if (changedTabs)
        {
            AddIssue(issues, "Expanded tab indentation to spaces.", 1, 1);
        }

        if (changedQuotes)
        {
            AddIssue(issues, "Replaced smart quotes with plain quotes.", 1, 1);
        }

        if (changedSpaces)
        {
            AddIssue(issues, "Replaced non-breaking spaces with regular spaces.", 1, 1);
        }

        return builder.ToString();
    }

    private static string RewriteScratchLikeStatements(string source, List<ValidationIssue> issues)
    {
        string[] lines = source.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            string indent = line[..(line.Length - line.TrimStart().Length)];
            string trimmed = line.TrimStart();
            string? rewritten = TryRewriteStatement(trimmed);
            if (rewritten is not null)
            {
                lines[i] = indent + rewritten;
                AddIssue(issues, $"Rewrote Scratch-like statement as '{rewritten}'.", i + 1, indent.Length + 1);
            }
        }

        return string.Join('\n', lines);
    }

    private static string? TryRewriteStatement(string trimmed)
    {
        if (trimmed.StartsWith("set ", StringComparison.Ordinal))
        {
            int to = FindOutsideQuotes(trimmed, " to ");
            if (to > 4)
            {
                string name = trimmed[4..to].Trim();
                string value = trimmed[(to + 4)..].Trim();
                if (name.Length > 0 && value.Length > 0)
                {
                    return $"{name} = {value}";
                }
            }
        }

        if (trimmed.StartsWith("change ", StringComparison.Ordinal))
        {
            int by = FindOutsideQuotes(trimmed, " by ");
            if (by > 7)
            {
                string name = trimmed[7..by].Trim();
                string value = trimmed[(by + 4)..].Trim();
                if (name.Length > 0 && value.Length > 0)
                {
                    return $"{name} += {value}";
                }
            }
        }

        if (trimmed.StartsWith("add ", StringComparison.Ordinal))
        {
            int to = FindOutsideQuotes(trimmed, " to ");
            if (to > 4)
            {
                string item = trimmed[4..to].Trim();
                string list = trimmed[(to + 4)..].Trim();
                if (item.Length > 0 && list.Length > 0)
                {
                    return $"{list}.add {item}";
                }
            }
        }

        if (trimmed.StartsWith("delete all of ", StringComparison.Ordinal))
        {
            string list = trimmed["delete all of ".Length..].Trim();
            return list.Length == 0 ? null : $"{list}.delete_all";
        }

        if (trimmed.StartsWith("delete ", StringComparison.Ordinal))
        {
            int of = FindOutsideQuotes(trimmed, " of ");
            if (of < 0)
            {
                of = FindOutsideQuotes(trimmed, " from ");
            }

            if (of > 7)
            {
                string index = trimmed[7..of].Trim();
                string list = trimmed[(of + (trimmed.AsSpan(of, Math.Min(trimmed.Length - of, 6)).StartsWith(" from ", StringComparison.Ordinal) ? 6 : 4))..].Trim();
                if (index.Length > 0 && list.Length > 0)
                {
                    return $"{list}.delete {index}";
                }
            }
        }

        if (trimmed.StartsWith("insert ", StringComparison.Ordinal))
        {
            int at = FindOutsideQuotes(trimmed, " at ");
            int of = at < 0 ? -1 : FindOutsideQuotes(trimmed, " of ", at + 4);
            if (at > 7 && of > at)
            {
                string item = trimmed[7..at].Trim();
                string index = trimmed[(at + 4)..of].Trim();
                string list = trimmed[(of + 4)..].Trim();
                if (item.Length > 0 && index.Length > 0 && list.Length > 0)
                {
                    return $"{list}.insert {index} {item}";
                }
            }
        }

        if (trimmed.StartsWith("replace ", StringComparison.Ordinal))
        {
            int of = FindOutsideQuotes(trimmed, " of ");
            int with = of < 0 ? -1 : FindOutsideQuotes(trimmed, " with ", of + 4);
            if (of > 8 && with > of)
            {
                string index = trimmed[8..of].Trim();
                string list = trimmed[(of + 4)..with].Trim();
                string item = trimmed[(with + 6)..].Trim();
                if (index.Length > 0 && list.Length > 0 && item.Length > 0)
                {
                    return $"{list}.replace {index} {item}";
                }
            }
        }

        if (trimmed.StartsWith("show list ", StringComparison.Ordinal))
        {
            string list = trimmed["show list ".Length..].Trim();
            return list.Length == 0 ? null : $"{list}.show";
        }

        if (trimmed.StartsWith("hide list ", StringComparison.Ordinal))
        {
            string list = trimmed["hide list ".Length..].Trim();
            return list.Length == 0 ? null : $"{list}.hide";
        }

        return null;
    }

    private static string WrapImplicitStage(string source, List<ValidationIssue> issues)
    {
        if (source.Split('\n').Any(static line =>
            line.TrimStart().StartsWith("stage ", StringComparison.Ordinal) ||
            line.TrimStart().StartsWith("stage{", StringComparison.Ordinal) ||
            line.TrimStart().StartsWith("sprite ", StringComparison.Ordinal)))
        {
            return source;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return source;
        }

        string[] lines = source.Split('\n');
        StringBuilder builder = new();
        builder.AppendLine("stage {");
        foreach (string line in lines)
        {
            if (line.Length == 0)
            {
                builder.AppendLine();
            }
            else
            {
                builder.Append("  ");
                builder.AppendLine(line);
            }
        }

        builder.Append('}');
        AddIssue(issues, "Wrapped source in an implicit stage target.", 1, 1);
        return builder.ToString();
    }

    private static string CloseMissingBraces(string source, List<ValidationIssue> issues)
    {
        int balance = CountBraceBalance(source);
        if (balance <= 0)
        {
            return source;
        }

        StringBuilder builder = new(source.TrimEnd());
        for (int i = 0; i < balance; i++)
        {
            builder.AppendLine();
            builder.Append('}');
        }

        AddIssue(issues, $"Added {balance} missing closing brace(s) at end of file.", source.Split('\n').Length, 1);
        return builder.ToString();
    }

    private static int CountBraceBalance(string source)
    {
        int balance = 0;
        bool inString = false;
        bool inBacktick = false;
        for (int i = 0; i < source.Length; i++)
        {
            char character = source[i];
            if (character == '#' && !inString && !inBacktick)
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }
            }
            else if (character == '"' && !inBacktick && (i == 0 || source[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (character == '`' && !inString)
            {
                inBacktick = !inBacktick;
            }
            else if (!inString && !inBacktick && character == '{')
            {
                balance++;
            }
            else if (!inString && !inBacktick && character == '}')
            {
                balance--;
            }
        }

        return balance;
    }

    private static int FindOutsideQuotes(string text, string value, int start = 0)
    {
        bool inString = false;
        bool inBacktick = false;
        for (int i = start; i <= text.Length - value.Length; i++)
        {
            char character = text[i];
            if (character == '"' && !inBacktick && (i == 0 || text[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (character == '`' && !inString)
            {
                inBacktick = !inBacktick;
            }

            if (!inString && !inBacktick && text.AsSpan(i).StartsWith(value, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static SourceLocation OffsetToLocation(string source, int offset)
    {
        int line = 1;
        int column = 1;
        for (int i = 0; i < Math.Min(offset, source.Length); i++)
        {
            if (source[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return new SourceLocation(line, column);
    }

    private static void AddIssue(List<ValidationIssue> issues, string message, int line, int column)
    {
        SourceLocation location = new(line, column);
        issues.Add(new ValidationIssue(
            message,
            "$",
            location,
            DiagnosticSeverity.Warning,
            "REPAIR200",
            new SourceSpan(location, location)));
    }
}

public sealed record ScratchAsmSourceRepairResult(
    string SourceText,
    IReadOnlyList<ValidationIssue> Issues);
