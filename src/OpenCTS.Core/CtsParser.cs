using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenCTS.Core;

public static class CtsParser
{
    private static CtsStringValue DecodeString(string literal, SourceSpan span, List<CtsDiagnostic> diagnostics)
    {
        try { return new CtsStringValue(JsonSerializer.Deserialize<string>(literal) ?? "", span); }
        catch (JsonException ex)
        {
            AddError(diagnostics, $"Improper syntax in string escape: {ex.Message}", span);
            return new CtsStringValue("", span);
        }
    }

    public static CtsParseResult Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        List<CtsDiagnostic> diagnostics = [];
        List<CtsTargetDeclaration> targets = [];
        List<CtsFileDeclaration> fileDeclarations = [];
        IReadOnlyList<SourceLine> lines = ReadLines(source);
        int index = 0;

        while (index < lines.Count)
        {
            SourceLine line = lines[index];
            string content = StripComment(line.Text);
            string trimmed = content.Trim();
            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            if (StartsWithWord(trimmed, "const"))
            {
                CtsConstDeclaration? declaration = ParseConstDeclaration(line, content, diagnostics);
                if (declaration is not null)
                {
                    fileDeclarations.Add(declaration);
                }

                index++;
                continue;
            }

            if (StartsWithWord(trimmed, "project"))
            {
                SourceSpan span = AtLineStart(line);
                try
                {
                    string? fileName = JsonSerializer.Deserialize<string>(trimmed[7..].Trim());
                    if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName) ||
                        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                        !fileName.EndsWith(".sb3", StringComparison.OrdinalIgnoreCase))
                    {
                        AddError(diagnostics, "Project companion must be a .sb3 filename in the source directory.", span);
                    }
                    else if (fileDeclarations.OfType<CtsProjectReference>().Any())
                    {
                        AddError(diagnostics, "Only one project companion may be declared.", span);
                    }
                    else
                    {
                        fileDeclarations.Add(new CtsProjectReference(fileName, span));
                    }
                }
                catch (JsonException)
                {
                    AddError(diagnostics, "Expected project \"companion.assets.sb3\".", span);
                }
                index++;
                continue;
            }

            if (StartsWithWord(trimmed, "enum"))
            {
                CtsEnumDeclaration? declaration = ParseEnumDeclaration(lines, ref index, diagnostics);
                if (declaration is not null)
                {
                    fileDeclarations.Add(declaration);
                }

                continue;
            }

            if (StartsWithWord(trimmed, "struct"))
            {
                CtsStructDeclaration? declaration = ParseStructDeclaration(lines, ref index, diagnostics);
                if (declaration is not null)
                {
                    fileDeclarations.Add(declaration);
                }

                continue;
            }

            if (TryParseTargetHeader(line, content, diagnostics, out bool isStage, out string name, out SourceSpan targetSpan))
            {
                index++;
                CtsTargetBody body = ParseTargetBody(lines, ref index, diagnostics);
                targets.Add(new CtsTargetDeclaration(isStage, name, body.Members, body.Scripts, targetSpan));
                continue;
            }

            AddError(diagnostics, "Expected 'stage {' or 'sprite \"Name\" {'.", AtLineStart(line));
            index++;
        }

        SourceSpan unitSpan = lines.Count == 0
            ? new SourceSpan(new SourceLocation(1, 1), new SourceLocation(1, 1))
            : new SourceSpan(new SourceLocation(1, 1), EndOfLine(lines[^1]));

        CtsCompilationUnit unit = new(targets, unitSpan)
        {
            FileDeclarations = fileDeclarations
        };
        return new CtsParseResult(unit, diagnostics);
    }

    private static CtsConstDeclaration? ParseConstDeclaration(
        SourceLine line,
        string content,
        List<CtsDiagnostic> diagnostics)
    {
        string trimmed = content.Trim();
        int startColumn = FirstNonWhitespaceColumn(content);
        SourceSpan span = Span(line.LineNumber, startColumn, startColumn + trimmed.Length);
        CtsLineScanner scanner = new(trimmed, line.LineNumber, startColumn);
        scanner.ConsumeWord("const");
        scanner.SkipWhitespace();
        string? name = scanner.ReadIdentifier();
        if (name is null)
        {
            AddError(diagnostics, "Expected a constant name.", scanner.PointSpan());
            return null;
        }

        int equals = FindAssignmentEquals(trimmed);
        if (equals < 0)
        {
            AddError(diagnostics, "Expected '=' after constant name.", scanner.PointSpan());
            return null;
        }

        List<CtsValue> values = ParseExpressions(trimmed[(equals + 1)..], line.LineNumber, startColumn + equals + 1, diagnostics);
        if (values.Count != 1)
        {
            AddError(diagnostics, "Constant declarations require exactly one expression.", span);
            return null;
        }

        return new CtsConstDeclaration(name, values[0], span);
    }

    private static CtsEnumDeclaration? ParseEnumDeclaration(
        IReadOnlyList<SourceLine> lines,
        ref int index,
        List<CtsDiagnostic> diagnostics)
    {
        SourceLine line = lines[index];
        string content = StripComment(line.Text);
        string trimmed = content.Trim();
        int startColumn = FirstNonWhitespaceColumn(content);
        SourceSpan span = Span(line.LineNumber, startColumn, startColumn + trimmed.Length);
        CtsLineScanner scanner = new(trimmed, line.LineNumber, startColumn);
        scanner.ConsumeWord("enum");
        scanner.SkipWhitespace();
        string? name = scanner.ReadIdentifier();
        scanner.SkipWhitespace();
        if (name is null || !scanner.TryConsume('{'))
        {
            AddError(diagnostics, "Expected 'enum Name {'.", span);
            index++;
            return null;
        }

        List<CtsEnumMember> members = [];
        int openBrace = trimmed.IndexOf('{');
        int closeBrace = trimmed.LastIndexOf('}');
        if (closeBrace > openBrace)
        {
            foreach (string item in SplitInlineDeclarationItems(trimmed[(openBrace + 1)..closeBrace]))
            {
                string memberText = item.Trim();
                if (memberText.Length == 0)
                {
                    continue;
                }

                SourceSpan memberSpan = Span(line.LineNumber, startColumn + openBrace + 1, startColumn + closeBrace);
                int equals = FindAssignmentEquals(memberText);
                string memberName = (equals < 0 ? memberText : memberText[..equals]).Trim();
                if (!IsValidSimpleIdentifier(memberName))
                {
                    AddError(diagnostics, "Expected an enum member name.", memberSpan);
                    continue;
                }

                CtsValue? value = null;
                if (equals >= 0)
                {
                    List<CtsValue> values = ParseExpressions(memberText[(equals + 1)..], line.LineNumber, startColumn + openBrace + equals + 2, diagnostics);
                    if (values.Count == 1)
                    {
                        value = values[0];
                    }
                }

                members.Add(new CtsEnumMember(memberName, value, memberSpan));
            }

            index++;
            return new CtsEnumDeclaration(name, members, span);
        }

        index++;
        while (index < lines.Count)
        {
            SourceLine memberLine = lines[index];
            string memberContent = StripComment(memberLine.Text);
            string memberText = memberContent.Trim();
            if (memberText.Length == 0)
            {
                index++;
                continue;
            }

            if (memberText == "}")
            {
                index++;
                return new CtsEnumDeclaration(name, members, span);
            }

            memberText = memberText.TrimEnd(',').TrimEnd();
            int memberColumn = FirstNonWhitespaceColumn(memberContent);
            SourceSpan memberSpan = Span(memberLine.LineNumber, memberColumn, memberColumn + memberText.Length);
            int equals = FindAssignmentEquals(memberText);
            string memberName = (equals < 0 ? memberText : memberText[..equals]).Trim();
            if (!IsValidSimpleIdentifier(memberName))
            {
                AddError(diagnostics, "Expected an enum member name.", memberSpan);
                index++;
                continue;
            }

            CtsValue? value = null;
            if (equals >= 0)
            {
                List<CtsValue> values = ParseExpressions(memberText[(equals + 1)..], memberLine.LineNumber, memberColumn + equals + 1, diagnostics);
                if (values.Count == 1)
                {
                    value = values[0];
                }
            }

            members.Add(new CtsEnumMember(memberName, value, memberSpan));
            index++;
        }

        AddError(diagnostics, "Expected '}' to close enum declaration.", span);
        return new CtsEnumDeclaration(name, members, span);
    }

    private static CtsStructDeclaration? ParseStructDeclaration(
        IReadOnlyList<SourceLine> lines,
        ref int index,
        List<CtsDiagnostic> diagnostics)
    {
        SourceLine line = lines[index];
        string content = StripComment(line.Text);
        string trimmed = content.Trim();
        int startColumn = FirstNonWhitespaceColumn(content);
        SourceSpan span = Span(line.LineNumber, startColumn, startColumn + trimmed.Length);
        CtsLineScanner scanner = new(trimmed, line.LineNumber, startColumn);
        scanner.ConsumeWord("struct");
        scanner.SkipWhitespace();
        string? name = scanner.ReadIdentifier();
        scanner.SkipWhitespace();
        if (name is null || !scanner.TryConsume('{'))
        {
            AddError(diagnostics, "Expected 'struct Name {'.", span);
            index++;
            return null;
        }

        List<CtsStructField> fields = [];
        int openBrace = trimmed.IndexOf('{');
        int closeBrace = trimmed.LastIndexOf('}');
        if (closeBrace > openBrace)
        {
            foreach (string item in SplitInlineDeclarationItems(trimmed[(openBrace + 1)..closeBrace]))
            {
                string fieldText = item.Trim();
                if (fieldText.Length == 0)
                {
                    continue;
                }

                SourceSpan fieldSpan = Span(line.LineNumber, startColumn + openBrace + 1, startColumn + closeBrace);
                int colon = fieldText.IndexOf(':');
                int equals = FindAssignmentEquals(fieldText);
                if (colon <= 0 || equals >= 0 && equals < colon)
                {
                    AddError(diagnostics, "Expected a struct field in the form 'name: type = default'.", fieldSpan);
                    continue;
                }

                string fieldName = fieldText[..colon].Trim();
                string typeName = fieldText[(colon + 1)..(equals < 0 ? fieldText.Length : equals)].Trim();
                if (!IsValidSimpleIdentifier(fieldName) || !IsValidSimpleIdentifier(typeName))
                {
                    AddError(diagnostics, "Expected valid struct field and type names.", fieldSpan);
                    continue;
                }

                CtsValue? defaultValue = null;
                if (equals >= 0)
                {
                    List<CtsValue> values = ParseExpressions(fieldText[(equals + 1)..], line.LineNumber, startColumn + openBrace + equals + 2, diagnostics);
                    if (values.Count == 1)
                    {
                        defaultValue = values[0];
                    }
                }

                fields.Add(new CtsStructField(fieldName, typeName, defaultValue, fieldSpan));
            }

            index++;
            return new CtsStructDeclaration(name, fields, span);
        }

        index++;
        while (index < lines.Count)
        {
            SourceLine fieldLine = lines[index];
            string fieldContent = StripComment(fieldLine.Text);
            string fieldText = fieldContent.Trim();
            if (fieldText.Length == 0)
            {
                index++;
                continue;
            }

            if (fieldText == "}")
            {
                index++;
                return new CtsStructDeclaration(name, fields, span);
            }

            fieldText = fieldText.TrimEnd(',').TrimEnd();
            int fieldColumn = FirstNonWhitespaceColumn(fieldContent);
            SourceSpan fieldSpan = Span(fieldLine.LineNumber, fieldColumn, fieldColumn + fieldText.Length);
            int colon = fieldText.IndexOf(':');
            int equals = FindAssignmentEquals(fieldText);
            if (colon <= 0 || equals >= 0 && equals < colon)
            {
                AddError(diagnostics, "Expected a struct field in the form 'name: type = default'.", fieldSpan);
                index++;
                continue;
            }

            string fieldName = fieldText[..colon].Trim();
            string typeName = fieldText[(colon + 1)..(equals < 0 ? fieldText.Length : equals)].Trim();
            if (!IsValidSimpleIdentifier(fieldName) || !IsValidSimpleIdentifier(typeName))
            {
                AddError(diagnostics, "Expected valid struct field and type names.", fieldSpan);
                index++;
                continue;
            }

            CtsValue? defaultValue = null;
            if (equals >= 0)
            {
                List<CtsValue> values = ParseExpressions(fieldText[(equals + 1)..], fieldLine.LineNumber, fieldColumn + equals + 1, diagnostics);
                if (values.Count == 1)
                {
                    defaultValue = values[0];
                }
            }

            fields.Add(new CtsStructField(fieldName, typeName, defaultValue, fieldSpan));
            index++;
        }

        AddError(diagnostics, "Expected '}' to close struct declaration.", span);
        return new CtsStructDeclaration(name, fields, span);
    }

    private static CtsTargetBody ParseTargetBody(
        IReadOnlyList<SourceLine> lines,
        ref int index,
        List<CtsDiagnostic> diagnostics)
    {
        List<CtsTargetMember> members = [];
        List<CtsScript> scripts = [];

        while (index < lines.Count)
        {
            SourceLine line = lines[index];
            string content = StripComment(line.Text);
            string trimmed = content.Trim();
            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            if (trimmed == "}")
            {
                index++;
                return new CtsTargetBody(members, scripts);
            }

            int indent = CountIndent(content);
            int startColumn = FirstNonWhitespaceColumn(content);

            if (TryParseTargetMember(lines, ref index, indent, startColumn, diagnostics, out CtsTargetMember? member))
            {
                if (member is not null)
                {
                    members.Add(member);
                }

                continue;
            }

            if (trimmed.StartsWith('@'))
            {
                CtsScript? hat = ParseHatScript(lines, ref index, indent, startColumn, diagnostics);
                if (hat is not null)
                {
                    scripts.Add(hat);
                }

                continue;
            }

            if (StartsWithWord(trimmed, "proc"))
            {
                CtsProcedureDefinition? procedure = ParseProcedure(lines, ref index, indent, startColumn, diagnostics);
                if (procedure is not null)
                {
                    scripts.Add(procedure);
                }

                continue;
            }

            AddError(diagnostics, "Expected a ScratchASM declaration, hat block, or procedure declaration.", Span(line.LineNumber, startColumn, startColumn + trimmed.Length));
            index++;
        }

        SourceLine lastLine = lines[^1];
        AddError(diagnostics, "Expected '}' to close target.", PointAt(lastLine.LineNumber, lastLine.Text.Length + 1));
        return new CtsTargetBody(members, scripts);
    }

    private static bool TryParseTargetMember(
        IReadOnlyList<SourceLine> lines,
        ref int index,
        int indent,
        int startColumn,
        List<CtsDiagnostic> diagnostics,
        out CtsTargetMember? member)
    {
        member = null;
        SourceLine line = lines[index];
        string content = StripComment(line.Text);
        string trimmed = content.Trim();
        SourceSpan memberSpan = Span(line.LineNumber, startColumn, startColumn + trimmed.Length);

        if (StartsWithWord(trimmed, "origin"))
        {
            try
            {
                string? name = JsonSerializer.Deserialize<string>(trimmed[6..].Trim());
                if (string.IsNullOrEmpty(name)) AddError(diagnostics, "Expected origin \"Original sprite name\".", memberSpan);
                else member = new CtsTargetOriginDeclaration(name, memberSpan);
            }
            catch (JsonException) { AddError(diagnostics, "Expected origin \"Original sprite name\".", memberSpan); }
            index++;
            return true;
        }

        if (StartsWithWord(trimmed, "rawblocks"))
        {
            StringBuilder json = new(trimmed[9..]);
            index++;
            if (trimmed[9..].Trim() == "{")
            {
                while (index < lines.Count)
                {
                    SourceLine jsonLine = lines[index++];
                    json.Append('\n').Append(jsonLine.Text);
                    if (jsonLine.Text.Trim() == "}" && CountIndent(jsonLine.Text) == indent)
                    {
                        break;
                    }
                }
            }
            try
            {
                if (JsonNode.Parse(json.ToString()) is not JsonObject)
                {
                    AddError(diagnostics, "rawblocks requires a JSON object keyed by block ID.", memberSpan);
                }
                else
                {
                    member = new CtsRawBlocksDeclaration(json.ToString(), memberSpan);
                }
            }
            catch (JsonException ex)
            {
                int errorLine = line.LineNumber + (int)(ex.LineNumber ?? 0);
                int errorColumn = (int)(ex.BytePositionInLine ?? 0) + 1;
                if (errorLine == line.LineNumber) errorColumn += startColumn + 8;
                AddError(diagnostics, $"Improper syntax in rawblocks JSON: {ex.Message}", PointAt(errorLine, errorColumn));
            }
            return true;
        }

        if (StartsWithWord(trimmed, "global"))
        {
            member = ParseScopedVariableDeclaration(trimmed, line.LineNumber, startColumn, memberSpan, CtsVariableScope.Global, isCloud: false, diagnostics);
            index++;
            return true;
        }

        if (StartsWithWord(trimmed, "sprite"))
        {
            member = ParseScopedVariableDeclaration(trimmed, line.LineNumber, startColumn, memberSpan, CtsVariableScope.Sprite, isCloud: false, diagnostics);
            index++;
            return true;
        }

        if (StartsWithWord(trimmed, "var"))
        {
            member = ParseVariableDeclaration(trimmed, line.LineNumber, startColumn, memberSpan, isCloud: false, diagnostics);
            index++;
            return true;
        }

        if (StartsWithWord(trimmed, "cloud"))
        {
            member = ParseCloudVariableDeclaration(trimmed, line.LineNumber, startColumn, memberSpan, diagnostics);
            index++;
            return true;
        }

        if (StartsWithWord(trimmed, "list"))
        {
            member = ParseListDeclaration(trimmed, line.LineNumber, startColumn, memberSpan, diagnostics);
            index++;
            return true;
        }

        if (StartsWithWord(trimmed, "broadcast"))
        {
            member = ParseBroadcastDeclaration(trimmed, line.LineNumber, startColumn, memberSpan, diagnostics);
            index++;
            return true;
        }

        if (StartsWithWord(trimmed, "extension"))
        {
            member = ParseExtensionDeclaration(trimmed, line.LineNumber, startColumn, memberSpan, diagnostics);
            index++;
            return true;
        }

        if (StartsWithWord(trimmed, "state"))
        {
            member = ParseStateDeclaration(trimmed, line.LineNumber, startColumn, memberSpan, diagnostics);
            index++;
            return true;
        }

        if (StartsWithWord(trimmed, "rotationStyle"))
        {
            member = ParseRotationStyleDeclaration(trimmed, line.LineNumber, startColumn, memberSpan, diagnostics);
            index++;
            return true;
        }

        if (StartsWithWord(trimmed, "costume"))
        {
            member = ParseCostumeDeclaration(lines, ref index, indent, startColumn, diagnostics);
            return true;
        }

        CtsLineScanner instanceScanner = new(trimmed, line.LineNumber, startColumn);
        string? instanceName = instanceScanner.ReadIdentifier();
        instanceScanner.SkipWhitespace();
        if (instanceName is not null && instanceScanner.TryConsume(':'))
        {
            instanceScanner.SkipWhitespace();
            string? typeName = instanceScanner.ReadIdentifier();
            instanceScanner.SkipWhitespace();
            if (typeName is null || !instanceScanner.End)
            {
                AddError(diagnostics, "Expected a struct instance in the form 'name: Type'.", memberSpan);
            }
            else
            {
                member = new CtsStructInstanceDeclaration(instanceName, typeName, memberSpan);
            }

            index++;
            return true;
        }

        return false;
    }

    private static CtsVariableDeclaration? ParseScopedVariableDeclaration(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan memberSpan,
        CtsVariableScope scope,
        bool isCloud,
        List<CtsDiagnostic> diagnostics)
    {
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        scanner.ConsumeWord(scope == CtsVariableScope.Global ? "global" : "sprite");
        scanner.SkipWhitespace();
        if (!scanner.ConsumeWord("var"))
        {
            AddError(diagnostics, $"Expected 'var' after '{(scope == CtsVariableScope.Global ? "global" : "sprite")}'.", scanner.PointSpan());
            return null;
        }

        return ParseVariableTail(scanner, memberSpan, scope, isCloud, diagnostics);
    }

    private static CtsVariableDeclaration? ParseVariableDeclaration(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan memberSpan,
        bool isCloud,
        List<CtsDiagnostic> diagnostics)
    {
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        scanner.ConsumeWord("var");
        return ParseVariableTail(scanner, memberSpan, CtsVariableScope.Contextual, isCloud, diagnostics);
    }

    private static CtsVariableDeclaration? ParseCloudVariableDeclaration(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan memberSpan,
        List<CtsDiagnostic> diagnostics)
    {
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        scanner.ConsumeWord("cloud");
        scanner.SkipWhitespace();
        scanner.ConsumeWord("global");
        scanner.SkipWhitespace();
        if (!scanner.ConsumeWord("var"))
        {
            AddError(diagnostics, "Expected 'var' after 'cloud'.", scanner.PointSpan());
            return null;
        }

        return ParseVariableTail(scanner, memberSpan, CtsVariableScope.Global, isCloud: true, diagnostics);
    }

    private static CtsVariableDeclaration? ParseVariableTail(
        CtsLineScanner scanner,
        SourceSpan memberSpan,
        CtsVariableScope scope,
        bool isCloud,
        List<CtsDiagnostic> diagnostics)
    {
        scanner.SkipWhitespace();
        string? name = scanner.ReadIdentifier();
        if (name is null)
        {
            AddError(diagnostics, "Expected a variable name.", scanner.PointSpan());
            return null;
        }

        scanner.SkipWhitespace();
        if (!scanner.TryConsume('='))
        {
            AddError(diagnostics, "Expected '=' after variable name.", scanner.PointSpan());
            return null;
        }

        CtsValue? value = scanner.ReadExpression(diagnostics);
        return value is null ? null : new CtsVariableDeclaration(name, value, isCloud, memberSpan, scope);
    }

    private static CtsListDeclaration? ParseListDeclaration(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan memberSpan,
        List<CtsDiagnostic> diagnostics)
    {
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        scanner.ConsumeWord("list");
        scanner.SkipWhitespace();
        string? name = scanner.ReadIdentifier();
        if (name is null)
        {
            AddError(diagnostics, "Expected a list name.", scanner.PointSpan());
            return null;
        }

        scanner.SkipWhitespace();
        if (!scanner.TryConsume('='))
        {
            AddError(diagnostics, "Expected '=' after list name.", scanner.PointSpan());
            return null;
        }

        scanner.SkipWhitespace();
        if (!scanner.TryConsume('['))
        {
            AddError(diagnostics, "Expected '[' before list items.", scanner.PointSpan());
            return null;
        }

        List<CtsValue> items = ParseValueList(scanner, ']', diagnostics);
        if (!scanner.TryConsume(']'))
        {
            AddError(diagnostics, "Expected ']' after list items.", scanner.PointSpan());
            return null;
        }

        return new CtsListDeclaration(name, items, memberSpan);
    }

    private static CtsBroadcastDeclaration? ParseBroadcastDeclaration(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan memberSpan,
        List<CtsDiagnostic> diagnostics)
    {
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        scanner.ConsumeWord("broadcast");
        scanner.SkipWhitespace();
        string? name = scanner.ReadIdentifier();
        if (name is null)
        {
            AddError(diagnostics, "Expected a broadcast name.", scanner.PointSpan());
            return null;
        }

        scanner.SkipWhitespace();
        if (!scanner.TryConsume('='))
        {
            AddError(diagnostics, "Expected '=' after broadcast name.", scanner.PointSpan());
            return null;
        }

        CtsValue? message = scanner.ReadValue(diagnostics);
        return message is null ? null : new CtsBroadcastDeclaration(name, message, memberSpan);
    }

    private static CtsExtensionDeclaration? ParseExtensionDeclaration(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan memberSpan,
        List<CtsDiagnostic> diagnostics)
    {
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        scanner.ConsumeWord("extension");
        scanner.SkipWhitespace();
        string? name = scanner.ReadIdentifier();
        if (name is null)
        {
            AddError(diagnostics, "Expected an extension name.", scanner.PointSpan());
            return null;
        }

        return new CtsExtensionDeclaration(name, memberSpan);
    }

    private static CtsStateDeclaration ParseStateDeclaration(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan memberSpan,
        List<CtsDiagnostic> diagnostics)
    {
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        scanner.ConsumeWord("state");
        Dictionary<string, CtsValue> properties = ParseAssignments(scanner, diagnostics);
        return new CtsStateDeclaration(properties, memberSpan);
    }

    private static CtsRotationStyleDeclaration? ParseRotationStyleDeclaration(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan memberSpan,
        List<CtsDiagnostic> diagnostics)
    {
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        scanner.ConsumeWord("rotationStyle");
        CtsValue? value = scanner.ReadValue(diagnostics);
        string? text = ValueToText(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            AddError(diagnostics, "Expected a rotation style value.", scanner.PointSpan());
            return null;
        }

        return new CtsRotationStyleDeclaration(text, memberSpan);
    }

    private static CtsCostumeDeclaration? ParseCostumeDeclaration(
        IReadOnlyList<SourceLine> lines,
        ref int index,
        int indent,
        int startColumn,
        List<CtsDiagnostic> diagnostics)
    {
        SourceLine line = lines[index];
        string content = StripComment(line.Text);
        string trimmed = content.Trim();
        SourceSpan memberSpan = Span(line.LineNumber, startColumn, startColumn + trimmed.Length);
        CtsLineScanner scanner = new(trimmed, line.LineNumber, startColumn);
        scanner.ConsumeWord("costume");
        CtsValue? nameValue = scanner.ReadValue(diagnostics);
        string name = ValueToText(nameValue) ?? "costume";

        CtsValue? widthValue = scanner.ReadValue(diagnostics);
        if (!scanner.TryConsume('x'))
        {
            AddError(diagnostics, "Expected costume size like 480x360.", scanner.PointSpan());
            index++;
            return null;
        }

        CtsValue? heightValue = scanner.ReadValue(diagnostics);
        if (!scanner.ConsumeWord("center"))
        {
            AddError(diagnostics, "Expected 'center' in costume declaration.", scanner.PointSpan());
            index++;
            return null;
        }

        if (!TryReadPoint(scanner, diagnostics, out CtsValue centerX, out CtsValue centerY))
        {
            index++;
            return null;
        }

        if (!scanner.TryConsume('{'))
        {
            AddError(diagnostics, "Expected '{' after costume declaration.", scanner.PointSpan());
            index++;
            return null;
        }

        int width = ValueToInt(widthValue);
        int height = ValueToInt(heightValue);
        double rotationCenterX = ValueToDouble(centerX);
        double rotationCenterY = ValueToDouble(centerY);

        index++;
        List<CtsSvgShape> shapes = [];
        while (index < lines.Count)
        {
            SourceLine shapeLine = lines[index];
            string shapeContent = StripComment(shapeLine.Text);
            string shapeTrimmed = shapeContent.Trim();
            if (shapeTrimmed.Length == 0)
            {
                index++;
                continue;
            }

            int shapeIndent = CountIndent(shapeContent);
            if (shapeTrimmed == "}")
            {
                index++;
                return new CtsCostumeDeclaration(name, width, height, rotationCenterX, rotationCenterY, shapes, memberSpan);
            }

            if (shapeIndent <= indent)
            {
                AddError(diagnostics, "Expected '}' to close costume declaration.", Span(shapeLine.LineNumber, FirstNonWhitespaceColumn(shapeContent), FirstNonWhitespaceColumn(shapeContent) + shapeTrimmed.Length));
                return new CtsCostumeDeclaration(name, width, height, rotationCenterX, rotationCenterY, shapes, memberSpan);
            }

            CtsSvgShape? shape = ParseSvgShape(shapeTrimmed, shapeLine.LineNumber, FirstNonWhitespaceColumn(shapeContent), diagnostics);
            if (shape is not null)
            {
                shapes.Add(shape);
            }

            index++;
        }

        AddError(diagnostics, "Expected '}' to close costume declaration.", PointAt(line.LineNumber, line.Text.Length + 1));
        return new CtsCostumeDeclaration(name, width, height, rotationCenterX, rotationCenterY, shapes, memberSpan);
    }

    private static CtsSvgShape? ParseSvgShape(
        string trimmed,
        int lineNumber,
        int startColumn,
        List<CtsDiagnostic> diagnostics)
    {
        SourceSpan shapeSpan = Span(lineNumber, startColumn, startColumn + trimmed.Length);
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        string? kind = scanner.ReadIdentifier();
        if (kind is null)
        {
            AddError(diagnostics, "Expected an SVG shape name.", scanner.PointSpan());
            return null;
        }

        List<CtsValue> arguments = [];
        switch (kind)
        {
            case "line":
                if (!TryReadPoint(scanner, diagnostics, out CtsValue x1, out CtsValue y1) ||
                    !TryReadPoint(scanner, diagnostics, out CtsValue x2, out CtsValue y2))
                {
                    return null;
                }

                arguments.AddRange([x1, y1, x2, y2]);
                break;

            case "rect":
                if (!TryReadPoint(scanner, diagnostics, out CtsValue x, out CtsValue y) ||
                    !TryReadPoint(scanner, diagnostics, out CtsValue width, out CtsValue height))
                {
                    return null;
                }

                arguments.AddRange([x, y, width, height]);
                break;

            case "circle":
            case "ellipse":
                if (!TryReadPoint(scanner, diagnostics, out CtsValue cx, out CtsValue cy))
                {
                    return null;
                }

                arguments.AddRange([cx, cy]);
                break;

            case "path":
                CtsValue? path = scanner.ReadValue(diagnostics);
                if (path is null)
                {
                    return null;
                }

                arguments.Add(path);
                break;

            case "text":
                if (!TryReadPoint(scanner, diagnostics, out CtsValue tx, out CtsValue ty))
                {
                    return null;
                }

                CtsValue? text = scanner.ReadValue(diagnostics);
                if (text is null)
                {
                    return null;
                }

                arguments.AddRange([tx, ty, text]);
                break;

            default:
                AddError(diagnostics, $"Unsupported costume shape '{kind}'.", shapeSpan);
                return null;
        }

        Dictionary<string, CtsValue> attributes = ParseAssignments(scanner, diagnostics);
        return new CtsSvgShape(kind, arguments, attributes, shapeSpan);
    }

    private static Dictionary<string, CtsValue> ParseAssignments(CtsLineScanner scanner, List<CtsDiagnostic> diagnostics)
    {
        Dictionary<string, CtsValue> assignments = new(StringComparer.Ordinal);

        while (!scanner.End)
        {
            scanner.SkipWhitespace();
            if (scanner.End)
            {
                break;
            }

            string? name = scanner.ReadIdentifier();
            if (name is null)
            {
                AddError(diagnostics, "Expected an assignment name.", scanner.PointSpan());
                break;
            }

            scanner.SkipWhitespace();
            if (!scanner.TryConsume('='))
            {
                AddError(diagnostics, "Expected '=' in assignment.", scanner.PointSpan());
                break;
            }

            CtsValue? value = scanner.ReadValue(diagnostics);
            if (value is not null)
            {
                assignments[name] = value;
            }
        }

        return assignments;
    }

    private static bool TryReadPoint(
        CtsLineScanner scanner,
        List<CtsDiagnostic> diagnostics,
        out CtsValue x,
        out CtsValue y)
    {
        x = new CtsNumberValue(0, "0", scanner.PointSpan());
        y = new CtsNumberValue(0, "0", scanner.PointSpan());

        CtsValue? readX = scanner.ReadValue(diagnostics);
        if (readX is null)
        {
            return false;
        }

        if (!scanner.TryConsume(','))
        {
            AddError(diagnostics, "Expected ',' between coordinate values.", scanner.PointSpan());
            return false;
        }

        CtsValue? readY = scanner.ReadValue(diagnostics);
        if (readY is null)
        {
            return false;
        }

        x = readX;
        y = readY;
        return true;
    }

    private static int ValueToInt(CtsValue? value)
    {
        return value is CtsNumberValue number ? checked((int)Math.Round(number.Number)) : 0;
    }

    private static double ValueToDouble(CtsValue? value)
    {
        return value is CtsNumberValue number ? number.Number : 0;
    }

    private static CtsScript? ParseHatScript(
        IReadOnlyList<SourceLine> lines,
        ref int index,
        int indent,
        int startColumn,
        List<CtsDiagnostic> diagnostics)
    {
        SourceLine line = lines[index];
        string content = StripComment(line.Text);
        string trimmed = content.Trim();
        SourceSpan scriptSpan = Span(line.LineNumber, startColumn, startColumn + trimmed.Length);

        if (!trimmed.EndsWith(':'))
        {
            AddError(diagnostics, "Hat declarations must end with ':'.", PointAt(line.LineNumber, line.Text.Length + 1));
            index = SkipIndentedBlock(lines, index + 1, indent);
            return null;
        }

        string header = trimmed[..^1].TrimEnd();
        int headerColumn = startColumn;
        CtsLineScanner scanner = new(header, line.LineNumber, headerColumn);
        if (!scanner.TryConsume('@'))
        {
            AddError(diagnostics, "Expected a hat declaration.", scanner.PointSpan());
            index++;
            return null;
        }

        string? hatName = scanner.ReadIdentifier();
        if (hatName is null)
        {
            AddError(diagnostics, "Expected a hat name.", scanner.PointSpan());
            index++;
            return null;
        }

        if (hatName == "block")
        {
            scanner.SkipWhitespace();
            CtsValue? opcodeValue = scanner.ReadValue(diagnostics);
            string? opcode = ValueToText(opcodeValue);
            if (string.IsNullOrWhiteSpace(opcode))
            {
                AddError(diagnostics, "Expected an opcode after '@block'.", scanner.PointSpan());
                index++;
                return null;
            }

            ParseBlockClauses(
                scanner,
                diagnostics,
                out List<CtsRawInput> inputs,
                out List<CtsRawField> fields,
                out Dictionary<string, CtsValue> mutation);
            index++;
            IReadOnlyList<CtsStatement> genericStatements = ParseStatementBlock(lines, ref index, indent, diagnostics);
            return new CtsGenericHatScript(opcode, inputs, fields, mutation, genericStatements, scriptSpan);
        }

        string registryName = hatName switch
        {
            "greenflag" => "event.greenflag",
            "key" => "event.key",
            "clicked" => "event.clicked",
            "stageclicked" => "event.stageclicked",
            "backdrop" => "event.backdrop",
            "received" => "event.received",
            "clone" => "control.clone",
            _ => hatName
        };
        string argumentText = header[(1 + hatName.Length)..];
        List<CtsValue> arguments = ParseExpressions(
            argumentText,
            line.LineNumber,
            startColumn + 1 + hatName.Length,
            diagnostics);

        index++;
        IReadOnlyList<CtsStatement> statements = ParseStatementBlock(lines, ref index, indent, diagnostics);
        if (CtsBlockRegistry.TryResolve(registryName, arguments.Count, out CtsAliasDefinition definition) &&
            definition.Shape == CtsBlockShape.Hat)
        {
            return new CtsAliasHatScript(registryName, arguments, statements, scriptSpan);
        }

        string? argument = arguments.Count == 1 ? ValueToText(arguments[0]) : null;
        return new CtsHatScript(hatName, argument, statements, scriptSpan);
    }

    private static CtsProcedureDefinition? ParseProcedure(
        IReadOnlyList<SourceLine> lines,
        ref int index,
        int indent,
        int startColumn,
        List<CtsDiagnostic> diagnostics)
    {
        SourceLine line = lines[index];
        string content = StripComment(line.Text);
        string trimmed = content.Trim();
        SourceSpan scriptSpan = Span(line.LineNumber, startColumn, startColumn + trimmed.Length);

        if (!trimmed.EndsWith(':'))
        {
            AddError(diagnostics, "Procedure declarations must end with ':'.", PointAt(line.LineNumber, line.Text.Length + 1));
            index = SkipIndentedBlock(lines, index + 1, indent);
            return null;
        }

        string header = trimmed[..^1].TrimEnd();
        CtsLineScanner scanner = new(header, line.LineNumber, startColumn);
        if (!scanner.ConsumeWord("proc"))
        {
            AddError(diagnostics, "Expected 'proc'.", scanner.PointSpan());
            index++;
            return null;
        }

        scanner.SkipWhitespace();
        string? name = scanner.ReadIdentifier();
        if (name is null)
        {
            AddError(diagnostics, "Expected a procedure name.", scanner.PointSpan());
            index++;
            return null;
        }

        scanner.SkipWhitespace();
        if (!scanner.TryConsume('('))
        {
            AddError(diagnostics, "Expected '(' after procedure name.", scanner.PointSpan());
            index++;
            return null;
        }

        List<CtsProcedureParameter> parameters = [];
        scanner.SkipWhitespace();
        while (!scanner.End && scanner.Peek() != ')')
        {
            CtsProcedureParameter? parameter = ParseParameter(scanner, diagnostics);
            if (parameter is not null)
            {
                parameters.Add(parameter);
            }

            scanner.SkipWhitespace();
            if (scanner.TryConsume(','))
            {
                scanner.SkipWhitespace();
                continue;
            }

            break;
        }

        if (!scanner.TryConsume(')'))
        {
            AddError(diagnostics, "Expected ')' after procedure parameters.", scanner.PointSpan());
            index++;
            return null;
        }

        string? declaredReturnType = null;
        scanner.SkipWhitespace();
        if (scanner.TryConsume(':'))
        {
            scanner.SkipWhitespace();
            declaredReturnType = scanner.ReadIdentifier();
            if (declaredReturnType is null)
            {
                AddError(diagnostics, "Expected a return type after ':'.", scanner.PointSpan());
            }
        }

        string? displaySignature = null;
        bool warp = false;
        scanner.SkipWhitespace();
        if (scanner.ConsumeWord("as"))
        {
            scanner.SkipWhitespace();
            CtsValue? signatureValue = scanner.ReadValue(diagnostics);
            if (signatureValue is CtsStringValue signature)
            {
                displaySignature = signature.Text;
            }
            else if (signatureValue is not null)
            {
                AddError(diagnostics, "Procedure display signatures must be strings.", signatureValue.Span);
            }

            scanner.SkipWhitespace();
        }

        if (!scanner.End)
        {
            if (scanner.ConsumeWord("warp"))
            {
                warp = true;
            }
            else
            {
                AddError(diagnostics, "Expected 'warp' or end of procedure declaration.", scanner.PointSpan());
            }
        }

        index++;
        IReadOnlyList<CtsStatement> statements = ParseStatementBlock(lines, ref index, indent, diagnostics);
        return new CtsProcedureDefinition(name, parameters, displaySignature, warp, statements, scriptSpan, declaredReturnType);
    }

    private static CtsProcedureParameter? ParseParameter(CtsLineScanner scanner, List<CtsDiagnostic> diagnostics)
    {
        SourceSpan start = scanner.PointSpan();
        string? name = scanner.ReadIdentifier();
        if (name is null)
        {
            AddError(diagnostics, "Expected a parameter name.", scanner.PointSpan());
            return null;
        }

        scanner.SkipWhitespace();
        if (!scanner.TryConsume(':'))
        {
            AddError(diagnostics, "Expected ':' after parameter name.", scanner.PointSpan());
            return null;
        }

        scanner.SkipWhitespace();
        string? typeText = scanner.ReadIdentifier();
        CtsParameterType type = typeText switch
        {
            "num" => CtsParameterType.Number,
            "str" => CtsParameterType.String,
            "bool" => CtsParameterType.Boolean,
            _ => CtsParameterType.String
        };

        CtsValue? defaultValue = null;
        scanner.SkipWhitespace();
        if (scanner.TryConsume('='))
        {
            scanner.SkipWhitespace();
            defaultValue = scanner.ReadValue(diagnostics);
        }

        return new CtsProcedureParameter(name, type, defaultValue, new SourceSpan(start.Start, scanner.PointSpan().End), typeText);
    }

    private static IReadOnlyList<CtsStatement> ParseStatementBlock(
        IReadOnlyList<SourceLine> lines,
        ref int index,
        int parentIndent,
        List<CtsDiagnostic> diagnostics)
    {
        List<CtsStatement> statements = [];

        while (index < lines.Count)
        {
            SourceLine line = lines[index];
            string content = StripComment(line.Text);
            string trimmed = content.Trim();
            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            int indent = CountIndent(content);
            if (indent <= parentIndent || trimmed == "}")
            {
                break;
            }

            int startColumn = FirstNonWhitespaceColumn(content);
            if (IsStructuredHeader(trimmed))
            {
                CtsStatement? structured = ParseStructuredStatement(lines, ref index, indent, startColumn, diagnostics);
                if (structured is not null)
                {
                    statements.Add(structured);
                }

                continue;
            }

            if (StartsWithWord(trimmed, "block"))
            {
                CtsStatement? blockStatement = ParseGenericBlockStatement(lines, ref index, indent, startColumn, diagnostics);
                if (blockStatement is not null)
                {
                    statements.Add(blockStatement);
                }

                continue;
            }

            CtsStatement? statement = ParseStatement(trimmed, line.LineNumber, startColumn, diagnostics);
            if (statement is not null)
            {
                statements.Add(statement);
            }

            index++;
        }

        return statements;
    }

    private static CtsStatement? ParseStatement(
        string trimmed,
        int lineNumber,
        int startColumn,
        List<CtsDiagnostic> diagnostics)
    {
        SourceSpan statementSpan = Span(lineNumber, startColumn, startColumn + trimmed.Length);
        if (StartsWithWord(trimmed, "local"))
        {
            return ParseLocalVariableDeclaration(trimmed, lineNumber, startColumn, statementSpan, diagnostics);
        }

        if (trimmed.StartsWith('%'))
        {
            return ParseRawStatement(trimmed, lineNumber, startColumn, statementSpan, diagnostics);
        }

        if (StartsWithWord(trimmed, "call"))
        {
            return ParseCallStatement(trimmed, lineNumber, startColumn, statementSpan, diagnostics);
        }

        if (StartsWithWord(trimmed, "block"))
        {
            return ParseGenericBlockHeader(
                trimmed,
                lineNumber,
                startColumn,
                statementSpan,
                [],
                new Dictionary<string, IReadOnlyList<CtsStatement>>(StringComparer.Ordinal),
                diagnostics);
        }

        if (TryParseVariableOperation(trimmed, lineNumber, startColumn, statementSpan, diagnostics, out CtsStatement? variableOperation))
        {
            return variableOperation;
        }

        if (TryParseListOperation(trimmed, lineNumber, startColumn, statementSpan, diagnostics, out CtsStatement? listOperation))
        {
            return listOperation;
        }

        int commandLength = 0;
        while (commandLength < trimmed.Length && !char.IsWhiteSpace(trimmed[commandLength]))
        {
            commandLength++;
        }

        string commandName = trimmed[..commandLength];
        if (!commandName.Contains('.', StringComparison.Ordinal) && !CtsBlockRegistry.HasAlias(commandName))
        {
            AddError(diagnostics, "Expected a category command such as motion.move.", Span(lineNumber, startColumn, startColumn + commandLength));
            return null;
        }

        string argumentText = trimmed[commandLength..];
        List<CtsValue> arguments = ParseExpressions(argumentText, lineNumber, startColumn + commandLength, diagnostics);

        return new CtsAliasStatement(commandName, arguments, statementSpan);
    }

    private static CtsLocalVariableDeclaration? ParseLocalVariableDeclaration(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan statementSpan,
        List<CtsDiagnostic> diagnostics)
    {
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        scanner.ConsumeWord("local");
        scanner.SkipWhitespace();
        if (!scanner.ConsumeWord("var"))
        {
            AddError(diagnostics, "Expected 'var' after 'local'.", scanner.PointSpan());
            return null;
        }

        scanner.SkipWhitespace();
        string? name = scanner.ReadIdentifier();
        if (name is null)
        {
            AddError(diagnostics, "Expected a local variable name.", scanner.PointSpan());
            return null;
        }

        scanner.SkipWhitespace();
        if (!scanner.TryConsume('='))
        {
            AddError(diagnostics, "Expected '=' after local variable name.", scanner.PointSpan());
            return null;
        }

        CtsValue? value = scanner.ReadExpression(diagnostics);
        return value is null ? null : new CtsLocalVariableDeclaration(name, value, statementSpan);
    }

    private static bool IsStructuredHeader(string trimmed)
    {
        if (!trimmed.EndsWith(':'))
        {
            return false;
        }

        string header = trimmed[..^1].TrimEnd();
        return StartsWithWord(header, "repeat") ||
            string.Equals(header, "forever", StringComparison.Ordinal) ||
            StartsWithWord(header, "if") ||
            StartsWithWord(header, "repeatuntil");
    }

    private static CtsStructuredStatement? ParseStructuredStatement(
        IReadOnlyList<SourceLine> lines,
        ref int index,
        int indent,
        int startColumn,
        List<CtsDiagnostic> diagnostics)
    {
        SourceLine line = lines[index];
        string trimmed = StripComment(line.Text).Trim();
        SourceSpan span = Span(line.LineNumber, startColumn, startColumn + trimmed.Length);
        string header = trimmed[..^1].TrimEnd();
        int separator = header.IndexOfAny([' ', '\t']);
        string command = separator < 0 ? header : header[..separator];
        string argumentsText = separator < 0 ? string.Empty : header[separator..];
        string registryName = command switch
        {
            "repeat" => "repeat",
            "forever" => "forever",
            "if" => "if",
            "repeatuntil" => "repeatuntil",
            _ => command
        };

        List<CtsValue> arguments = ParseExpressions(
            argumentsText,
            line.LineNumber,
            startColumn + (separator < 0 ? header.Length : separator),
            diagnostics);

        if (!CtsBlockRegistry.TryResolve(registryName, arguments.Count, out CtsAliasDefinition definition) ||
            definition.Shape != CtsBlockShape.CBlock)
        {
            AddError(diagnostics, $"Invalid structured block '{command}' or argument count.", span);
            index++;
            return null;
        }

        index++;
        IReadOnlyList<CtsStatement> first = ParseStatementBlock(lines, ref index, indent, diagnostics);
        Dictionary<string, IReadOnlyList<CtsStatement>> substacks = new(StringComparer.Ordinal)
        {
            ["SUBSTACK"] = first
        };

        if (command == "if" && index < lines.Count)
        {
            SourceLine next = lines[index];
            string nextContent = StripComment(next.Text);
            if (CountIndent(nextContent) == indent && string.Equals(nextContent.Trim(), "else:", StringComparison.Ordinal))
            {
                index++;
                substacks["SUBSTACK2"] = ParseStatementBlock(lines, ref index, indent, diagnostics);
                registryName = "ifelse";
            }
        }

        return new CtsStructuredStatement(registryName, arguments, substacks, span);
    }

    private static bool TryParseVariableOperation(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan span,
        List<CtsDiagnostic> diagnostics,
        out CtsStatement? statement)
    {
        statement = null;
        int operatorIndex = trimmed.IndexOf("+=", StringComparison.Ordinal);
        bool isChange = operatorIndex >= 0;
        int operatorLength = 2;
        if (!isChange)
        {
            operatorIndex = FindAssignmentEquals(trimmed);
            operatorLength = 1;
        }

        if (operatorIndex <= 0)
        {
            return false;
        }

        string name = UnquoteIdentifier(trimmed[..operatorIndex].Trim());
        if (!IsValidDataName(name))
        {
            return false;
        }

        string valueText = trimmed[(operatorIndex + operatorLength)..];
        List<CtsValue> values = ParseExpressions(valueText, lineNumber, startColumn + operatorIndex + operatorLength, diagnostics);
        if (values.Count != 1)
        {
            AddError(diagnostics, "Variable assignment requires exactly one expression.", span);
            return true;
        }

        statement = new CtsVariableOperationStatement(name, isChange, values[0], span);
        return true;
    }

    private static bool TryParseListOperation(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan span,
        List<CtsDiagnostic> diagnostics,
        out CtsStatement? statement)
    {
        statement = null;
        int commandEnd = trimmed.IndexOfAny([' ', '\t']);
        string head = commandEnd < 0 ? trimmed : trimmed[..commandEnd];
        if (CtsBlockRegistry.HasAlias(head))
        {
            return false;
        }

        int dot = head.LastIndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        string operation = head[(dot + 1)..];
        if (string.Equals(head[..dot], "list", StringComparison.Ordinal))
        {
            return false;
        }

        if (operation is not ("add" or "delete" or "delete_all" or "deleteall" or "insert" or "replace" or "show" or "hide"))
        {
            return false;
        }

        string listName = UnquoteIdentifier(head[..dot]);
        string argsText = commandEnd < 0 ? string.Empty : trimmed[commandEnd..];
        List<CtsValue> arguments = ParseExpressions(argsText, lineNumber, startColumn + Math.Max(commandEnd, head.Length), diagnostics);
        int expected = operation switch
        {
            "add" or "delete" => 1,
            "insert" or "replace" => 2,
            _ => 0
        };
        if (arguments.Count != expected)
        {
            AddError(diagnostics, $"List operation '{operation}' expects {expected} argument(s).", span);
            return true;
        }

        statement = new CtsListOperationStatement(listName, operation.Replace("_", string.Empty, StringComparison.Ordinal), arguments, span);
        return true;
    }

    private static int FindAssignmentEquals(string text)
    {
        bool inString = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"' && (i == 0 || text[i - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString && text[i] == '=' &&
                (i + 1 >= text.Length || text[i + 1] != '=') &&
                (i == 0 || text[i - 1] is not ('<' or '>' or '=')))
            {
                return i;
            }
        }

        return -1;
    }

    private static IReadOnlyList<string> SplitInlineDeclarationItems(string text)
    {
        List<string> items = [];
        int start = 0;
        int parenthesisDepth = 0;
        bool inString = false;
        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '"' && (index == 0 || text[index - 1] != '\\'))
            {
                inString = !inString;
            }
            else if (!inString && character == '(')
            {
                parenthesisDepth++;
            }
            else if (!inString && character == ')')
            {
                parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
            }
            else if (!inString && parenthesisDepth == 0 && character == ',')
            {
                items.Add(text[start..index]);
                start = index + 1;
            }
        }

        items.Add(text[start..]);
        return items;
    }

    private static bool IsValidDataName(string name)
    {
        return name.Length > 0 && (IsIdentifierStart(name[0]) || name.Any(char.IsWhiteSpace));
    }

    private static bool IsValidSimpleIdentifier(string name)
    {
        return name.Length > 0 && IsIdentifierStart(name[0]) && name.Skip(1).All(IsIdentifierPart);
    }

    private static string UnquoteIdentifier(string text)
    {
        return text.Length >= 2 && text[0] == '`' && text[^1] == '`' ? text[1..^1] : text;
    }

    private static List<CtsValue> ParseExpressions(
        string text,
        int lineNumber,
        int baseColumn,
        List<CtsDiagnostic> diagnostics)
    {
        CtsExpressionParser parser = new(text, lineNumber, baseColumn, diagnostics);
        return parser.ParseAll();
    }

    private static CtsStatement? ParseCallStatement(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan statementSpan,
        List<CtsDiagnostic> diagnostics)
    {
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        scanner.ConsumeWord("call");
        scanner.SkipWhitespace();
        string? procedureName = scanner.ReadIdentifier();
        if (procedureName is null)
        {
            AddError(diagnostics, "Expected a procedure name after 'call'.", scanner.PointSpan());
            return null;
        }

        scanner.SkipWhitespace();
        if (!scanner.TryConsume('('))
        {
            AddError(diagnostics, "Expected '(' after procedure name.", scanner.PointSpan());
            return null;
        }

        int openParenthesis = trimmed.IndexOf('(');
        if (!trimmed.EndsWith(')') || openParenthesis < 0)
        {
            AddError(diagnostics, "Expected ')' after call arguments.", scanner.PointSpan());
            return null;
        }

        string argumentText = trimmed[(openParenthesis + 1)..^1];
        List<CtsValue> arguments = ParseExpressions(
            argumentText,
            lineNumber,
            startColumn + openParenthesis + 1,
            diagnostics);

        return new CtsCallStatement(procedureName, arguments, statementSpan);
    }

    private static CtsStatement? ParseGenericBlockStatement(
        IReadOnlyList<SourceLine> lines,
        ref int index,
        int indent,
        int startColumn,
        List<CtsDiagnostic> diagnostics)
    {
        SourceLine line = lines[index];
        string content = StripComment(line.Text);
        string trimmed = content.Trim();
        SourceSpan statementSpan = Span(line.LineNumber, startColumn, startColumn + trimmed.Length);
        bool hasBody = trimmed.EndsWith('{');
        string header = hasBody ? trimmed[..^1].TrimEnd() : trimmed;

        if (!hasBody)
        {
            index++;
            return ParseGenericBlockHeader(header, line.LineNumber, startColumn, statementSpan, [], new Dictionary<string, IReadOnlyList<CtsStatement>>(StringComparer.Ordinal), diagnostics);
        }

        index++;
        IReadOnlyList<CtsStatement> statements = [];
        IReadOnlyDictionary<string, IReadOnlyList<CtsStatement>> namedSubstacks =
            new Dictionary<string, IReadOnlyList<CtsStatement>>(StringComparer.Ordinal);
        if (StartsWithNamedSubstack(lines, index, indent))
        {
            namedSubstacks = ParseNamedSubstacks(lines, ref index, indent, diagnostics);
        }
        else
        {
            statements = ParseStatementBlock(lines, ref index, indent, diagnostics);
        }

        if (index >= lines.Count)
        {
            AddError(diagnostics, "Expected '}' to close generic block.", PointAt(line.LineNumber, line.Text.Length + 1));
            return ParseGenericBlockHeader(header, line.LineNumber, startColumn, statementSpan, statements, namedSubstacks, diagnostics);
        }

        string closeContent = StripComment(lines[index].Text);
        string closeTrimmed = closeContent.Trim();
        if (closeTrimmed == "}")
        {
            index++;
        }
        else
        {
            AddError(diagnostics, "Expected '}' to close generic block.", Span(lines[index].LineNumber, FirstNonWhitespaceColumn(closeContent), FirstNonWhitespaceColumn(closeContent) + closeTrimmed.Length));
        }

        return ParseGenericBlockHeader(header, line.LineNumber, startColumn, statementSpan, statements, namedSubstacks, diagnostics);
    }

    private static bool StartsWithNamedSubstack(IReadOnlyList<SourceLine> lines, int index, int parentIndent)
    {
        while (index < lines.Count)
        {
            string content = StripComment(lines[index].Text);
            string trimmed = content.Trim();
            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            return CountIndent(content) > parentIndent && StartsWithWord(trimmed, "substack");
        }

        return false;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<CtsStatement>> ParseNamedSubstacks(
        IReadOnlyList<SourceLine> lines,
        ref int index,
        int parentIndent,
        List<CtsDiagnostic> diagnostics)
    {
        Dictionary<string, IReadOnlyList<CtsStatement>> substacks = new(StringComparer.Ordinal);
        while (index < lines.Count)
        {
            SourceLine line = lines[index];
            string content = StripComment(line.Text);
            string trimmed = content.Trim();
            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            int indent = CountIndent(content);
            if (indent <= parentIndent || trimmed == "}")
            {
                break;
            }

            int startColumn = FirstNonWhitespaceColumn(content);
            SourceSpan span = Span(line.LineNumber, startColumn, startColumn + trimmed.Length);
            CtsLineScanner scanner = new(trimmed, line.LineNumber, startColumn);
            if (!scanner.ConsumeWord("substack"))
            {
                AddError(diagnostics, "Expected a named substack declaration.", span);
                index++;
                continue;
            }

            string? name = scanner.ReadIdentifier();
            if (name is null)
            {
                AddError(diagnostics, "Expected a substack input name.", scanner.PointSpan());
                index++;
                continue;
            }

            if (!scanner.TryConsume(':'))
            {
                AddError(diagnostics, "Named substack declarations must end with ':'.", scanner.PointSpan());
                index++;
                continue;
            }

            index++;
            IReadOnlyList<CtsStatement> statements = ParseStatementBlock(lines, ref index, indent, diagnostics);
            if (!substacks.TryAdd(name, statements))
            {
                AddError(diagnostics, $"Substack '{name}' is already declared for this block.", span);
            }
        }

        return substacks;
    }

    private static CtsBlockStatement? ParseGenericBlockHeader(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan statementSpan,
        IReadOnlyList<CtsStatement> statements,
        IReadOnlyDictionary<string, IReadOnlyList<CtsStatement>> namedSubstacks,
        List<CtsDiagnostic> diagnostics)
    {
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        scanner.ConsumeWord("block");
        scanner.SkipWhitespace();
        CtsValue? opcodeValue = scanner.ReadValue(diagnostics);
        string? opcode = ValueToText(opcodeValue);
        if (string.IsNullOrWhiteSpace(opcode))
        {
            AddError(diagnostics, "Expected an opcode after 'block'.", scanner.PointSpan());
            return null;
        }

        ParseBlockClauses(scanner, diagnostics, out List<CtsRawInput> inputs, out List<CtsRawField> fields, out Dictionary<string, CtsValue> mutation);
        return new CtsBlockStatement(opcode, inputs, fields, mutation, statements, namedSubstacks, statementSpan);
    }

    private static CtsStatement? ParseRawStatement(
        string trimmed,
        int lineNumber,
        int startColumn,
        SourceSpan statementSpan,
        List<CtsDiagnostic> diagnostics)
    {
        CtsLineScanner scanner = new(trimmed, lineNumber, startColumn);
        scanner.TryConsume('%');
        scanner.SkipWhitespace();
        CtsValue? opcodeValue = scanner.ReadValue(diagnostics);
        string? opcode = ValueToText(opcodeValue);
        if (string.IsNullOrWhiteSpace(opcode))
        {
            AddError(diagnostics, "Expected an opcode after '%'.", scanner.PointSpan());
            return null;
        }

        ParseBlockClauses(scanner, diagnostics, out List<CtsRawInput> inputs, out List<CtsRawField> fields, out Dictionary<string, CtsValue> mutation);
        return new CtsRawStatement(opcode, inputs, fields, mutation, statementSpan);
    }

    private static void ParseBlockClauses(
        CtsLineScanner scanner,
        List<CtsDiagnostic> diagnostics,
        out List<CtsRawInput> inputs,
        out List<CtsRawField> fields,
        out Dictionary<string, CtsValue> mutation,
        char? terminator = null)
    {
        inputs = [];
        fields = [];
        mutation = new Dictionary<string, CtsValue>(StringComparer.Ordinal);

        while (!scanner.End)
        {
            scanner.SkipWhitespace();
            if (terminator is not null && scanner.Peek() == terminator)
            {
                break;
            }

            if (scanner.End)
            {
                break;
            }

            string? clause = scanner.ReadIdentifier();
            if (clause is null)
            {
                AddError(diagnostics, "Expected raw clause 'input', 'field', or 'mutation'.", scanner.PointSpan());
                return;
            }

            scanner.SkipWhitespace();
            string? name = scanner.ReadIdentifier();
            if (name is null)
            {
                AddError(diagnostics, "Expected a raw clause name.", scanner.PointSpan());
                return;
            }

            scanner.SkipWhitespace();
            if (!scanner.TryConsume('='))
            {
                AddError(diagnostics, "Expected '=' in raw clause.", scanner.PointSpan());
                return;
            }

            scanner.SkipWhitespace();
            switch (clause)
            {
                case "input":
                {
                    CtsValue? value = scanner.ReadValue(diagnostics);
                    if (value is not null)
                    {
                        inputs.Add(new CtsRawInput(name, value));
                    }

                    break;
                }

                case "field":
                {
                    CtsRawField? field = ParseRawField(scanner, name, diagnostics);
                    if (field is not null)
                    {
                        fields.Add(field);
                    }

                    break;
                }

                case "mutation":
                {
                    CtsValue? value = scanner.ReadValue(diagnostics);
                    if (value is not null)
                    {
                        mutation[name] = value;
                    }

                    break;
                }

                default:
                    AddError(diagnostics, "Expected raw clause 'input', 'field', or 'mutation'.", scanner.PointSpan());
                    return;
            }
        }
    }

    private static CtsRawField? ParseRawField(CtsLineScanner scanner, string name, List<CtsDiagnostic> diagnostics)
    {
        if (scanner.TryConsume('('))
        {
            scanner.SkipWhitespace();
            CtsValue? value = scanner.ReadValue(diagnostics);
            scanner.SkipWhitespace();
            CtsValue? id = null;
            if (scanner.TryConsume(','))
            {
                scanner.SkipWhitespace();
                id = scanner.ReadValue(diagnostics);
                scanner.SkipWhitespace();
            }

            if (!scanner.TryConsume(')'))
            {
                AddError(diagnostics, "Expected ')' after raw field tuple.", scanner.PointSpan());
                return null;
            }

            return value is null ? null : new CtsRawField(name, value, id);
        }

        CtsValue? singleValue = scanner.ReadValue(diagnostics);
        return singleValue is null ? null : new CtsRawField(name, singleValue, null);
    }

    private static List<CtsValue> ParseValueList(CtsLineScanner scanner, char close, List<CtsDiagnostic> diagnostics)
    {
        List<CtsValue> values = [];
        scanner.SkipWhitespace();
        while (!scanner.End && scanner.Peek() != close)
        {
            CtsValue? value = scanner.ReadValue(diagnostics);
            if (value is not null)
            {
                values.Add(value);
            }

            scanner.SkipWhitespace();
            if (scanner.TryConsume(','))
            {
                scanner.SkipWhitespace();
                continue;
            }

            break;
        }

        return values;
    }

    private static bool TryParseTargetHeader(
        SourceLine line,
        string content,
        List<CtsDiagnostic> diagnostics,
        out bool isStage,
        out string name,
        out SourceSpan span)
    {
        string trimmed = content.Trim();
        int startColumn = FirstNonWhitespaceColumn(content);
        span = Span(line.LineNumber, startColumn, startColumn + trimmed.Length);
        isStage = false;
        name = string.Empty;

        if (trimmed.StartsWith("stage", StringComparison.Ordinal))
        {
            CtsLineScanner scanner = new(trimmed, line.LineNumber, startColumn);
            scanner.ConsumeWord("stage");
            scanner.SkipWhitespace();
            if (!scanner.TryConsume('{'))
            {
                AddError(diagnostics, "Expected '{' after stage.", scanner.PointSpan());
                return false;
            }

            isStage = true;
            name = "Stage";
            return true;
        }

        if (trimmed.StartsWith("sprite", StringComparison.Ordinal))
        {
            CtsLineScanner scanner = new(trimmed, line.LineNumber, startColumn);
            scanner.ConsumeWord("sprite");
            scanner.SkipWhitespace();
            CtsValue? spriteName = scanner.ReadValue(diagnostics);
            scanner.SkipWhitespace();
            if (!scanner.TryConsume('{'))
            {
                AddError(diagnostics, "Expected '{' after sprite name.", scanner.PointSpan());
                return false;
            }

            isStage = false;
            name = ValueToText(spriteName) ?? "Sprite";
            return true;
        }

        return false;
    }

    private static int SkipIndentedBlock(IReadOnlyList<SourceLine> lines, int index, int parentIndent)
    {
        while (index < lines.Count)
        {
            string content = StripComment(lines[index].Text);
            string trimmed = content.Trim();
            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            int indent = CountIndent(content);
            if (indent <= parentIndent || trimmed == "}")
            {
                break;
            }

            index++;
        }

        return index;
    }

    private static IReadOnlyList<SourceLine> ReadLines(string source)
    {
        List<SourceLine> lines = [];
        int lineNumber = 1;
        int index = 0;

        while (index < source.Length)
        {
            int start = index;
            while (index < source.Length && source[index] is not '\r' and not '\n')
            {
                index++;
            }

            string text = source[start..index];
            lines.Add(new SourceLine(lineNumber, text));

            if (index < source.Length && source[index] == '\r')
            {
                index++;
            }

            if (index < source.Length && source[index] == '\n')
            {
                index++;
            }

            lineNumber++;
        }

        if (source.Length == 0)
        {
            lines.Add(new SourceLine(1, string.Empty));
        }

        return lines;
    }

    private static string StripComment(string text)
    {
        bool inString = false;
        bool escaped = false;
        for (int i = 0; i < text.Length; i++)
        {
            char value = text[i];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (value == '\\')
            {
                escaped = inString;
                continue;
            }

            if (value == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString && value == '#')
            {
                return text[..i];
            }
        }

        return text;
    }

    private static int CountIndent(string text)
    {
        int indent = 0;
        foreach (char value in text)
        {
            if (value == ' ')
            {
                indent++;
            }
            else if (value == '\t')
            {
                indent += 4;
            }
            else
            {
                break;
            }
        }

        return indent;
    }

    private static int FirstNonWhitespaceColumn(string text)
    {
        return CountIndent(text) + 1;
    }

    private static bool StartsWithWord(string text, string word)
    {
        return text.StartsWith(word, StringComparison.Ordinal) &&
            (text.Length == word.Length || !IsIdentifierPart(text[word.Length]));
    }

    private static string? ValueToText(CtsValue? value)
    {
        return value switch
        {
            CtsStringValue stringValue => stringValue.Text,
            CtsNumberValue numberValue => numberValue.Text,
            CtsIdentifierValue identifierValue => identifierValue.Name,
            _ => null
        };
    }

    private static void AddError(List<CtsDiagnostic> diagnostics, string message, SourceSpan span)
    {
        diagnostics.Add(new CtsDiagnostic("CTS1001", DiagnosticSeverity.Error, message, span));
    }

    private static SourceSpan AtLineStart(SourceLine line)
    {
        return Span(line.LineNumber, 1, Math.Max(1, line.Text.Length + 1));
    }

    private static SourceSpan PointAt(int line, int column)
    {
        return Span(line, column, column);
    }

    private static SourceSpan Span(int line, int startColumn, int endColumn)
    {
        return new SourceSpan(new SourceLocation(line, startColumn), new SourceLocation(line, endColumn));
    }

    private static SourceLocation EndOfLine(SourceLine line)
    {
        return new SourceLocation(line.LineNumber, line.Text.Length + 1);
    }

    private static bool IsIdentifierStart(char value)
    {
        return char.IsLetter(value) || value == '_';
    }

    private static bool IsIdentifierPart(char value)
    {
        return char.IsLetterOrDigit(value) || value is '_' or '-' or '.';
    }

    private sealed record CtsTargetBody(IReadOnlyList<CtsTargetMember> Members, IReadOnlyList<CtsScript> Scripts);

    private sealed class CtsExpressionParser
    {
        private readonly string _text;
        private readonly int _line;
        private readonly int _baseColumn;
        private readonly List<CtsDiagnostic> _diagnostics;
        private int _index;

        public CtsExpressionParser(string text, int line, int baseColumn, List<CtsDiagnostic> diagnostics)
        {
            _text = text;
            _line = line;
            _baseColumn = baseColumn;
            _diagnostics = diagnostics;
        }

        public List<CtsValue> ParseAll()
        {
            List<CtsValue> values = [];
            SkipWhitespace();
            while (!End)
            {
                if (TryConsume(','))
                {
                    SkipWhitespace();
                    continue;
                }

                int start = _index;
                CtsValue? value = ParseOr();
                if (value is not null)
                {
                    values.Add(value);
                }

                SkipWhitespace();
                if (_index == start)
                {
                    AddError(_diagnostics, $"Unexpected character '{_text[_index]}'.", PointSpan());
                    _index++;
                }
            }

            return values;
        }

        private CtsValue? ParseOr()
        {
            CtsValue? left = ParseAnd();
            while (left is not null)
            {
                int checkpoint = _index;
                if (!TryConsumeOperator("||") && !TryConsumeWord("or"))
                {
                    _index = checkpoint;
                    break;
                }

                CtsValue? right = ParseAnd();
                if (right is null)
                {
                    AddError(_diagnostics, "Expected an expression after 'or'.", PointSpan());
                    break;
                }

                left = new CtsBinaryValue("or", left, right, Combine(left.Span, right.Span));
            }

            return left;
        }

        private CtsValue? ParseAnd()
        {
            CtsValue? left = ParseComparison();
            while (left is not null)
            {
                int checkpoint = _index;
                if (!TryConsumeOperator("&&") && !TryConsumeWord("and"))
                {
                    _index = checkpoint;
                    break;
                }

                CtsValue? right = ParseComparison();
                if (right is null)
                {
                    AddError(_diagnostics, "Expected an expression after 'and'.", PointSpan());
                    break;
                }

                left = new CtsBinaryValue("and", left, right, Combine(left.Span, right.Span));
            }

            return left;
        }

        private CtsValue? ParseComparison()
        {
            CtsValue? left = ParseAdditive();
            while (left is not null)
            {
                string? operation = TryConsumeOperator("<=") ? "<=" :
                    TryConsumeOperator(">=") ? ">=" :
                    TryConsumeOperator("!=") ? "!=" :
                    TryConsumeOperator("==") ? "==" :
                    TryConsumeOperator(">") ? ">" :
                    TryConsumeOperator("<") ? "<" : null;
                if (operation is null)
                {
                    break;
                }

                CtsValue? right = ParseAdditive();
                if (right is null)
                {
                    AddError(_diagnostics, $"Expected an expression after '{operation}'.", PointSpan());
                    break;
                }

                left = new CtsBinaryValue(operation, left, right, Combine(left.Span, right.Span));
            }

            return left;
        }

        private CtsValue? ParseAdditive()
        {
            CtsValue? left = ParseMultiplicative();
            while (left is not null)
            {
                string? operation = TryConsumeOperator("+") ? "+" :
                    TryConsumeOperator("-") ? "-" : null;
                if (operation is null)
                {
                    break;
                }

                CtsValue? right = ParseMultiplicative();
                if (right is null)
                {
                    AddError(_diagnostics, $"Expected an expression after '{operation}'.", PointSpan());
                    break;
                }

                left = new CtsBinaryValue(operation, left, right, Combine(left.Span, right.Span));
            }

            return left;
        }

        private CtsValue? ParseMultiplicative()
        {
            CtsValue? left = ParsePower();
            while (left is not null)
            {
                string? operation = TryConsumeOperator("*") ? "*" :
                    TryConsumeOperator("/") ? "/" :
                    TryConsumeOperator("%") ? "%" : null;
                if (operation is null)
                {
                    break;
                }

                CtsValue? right = ParsePower();
                if (right is null)
                {
                    AddError(_diagnostics, $"Expected an expression after '{operation}'.", PointSpan());
                    break;
                }

                left = new CtsBinaryValue(operation, left, right, Combine(left.Span, right.Span));
            }

            return left;
        }

        private CtsValue? ParsePower()
        {
            CtsValue? left = ParseUnary();
            if (left is not null && TryConsumeOperator("^"))
            {
                CtsValue? right = ParsePower();
                if (right is null)
                {
                    AddError(_diagnostics, "Expected an integer literal exponent after '^'.", PointSpan());
                    return left;
                }

                return new CtsBinaryValue("^", left, right, Combine(left.Span, right.Span));
            }

            return left;
        }

        private CtsValue? ParseUnary()
        {
            SkipWhitespace();
            int start = _index;
            string? operation = TryConsumeOperator("!") ? "not" :
                TryConsumeWord("not") ? "not" :
                TryConsumeOperator("-") ? "-" : null;
            if (operation is null)
            {
                return ParsePrimary();
            }

            CtsValue? operand = ParseUnary();
            if (operand is null)
            {
                AddError(_diagnostics, $"Expected an expression after '{operation}'.", PointSpan());
                return null;
            }

            return new CtsUnaryValue(operation, operand, SpanFrom(start, _index));
        }

        private CtsValue? ParsePrimary()
        {
            SkipWhitespace();
            if (End)
            {
                return null;
            }

            int start = _index;
            if (_text[_index] == '[')
            {
                CtsLineScanner scanner = new(_text[_index..], _line, _baseColumn + _index);
                CtsValue? blockValue = scanner.ReadValue(_diagnostics);
                _index += scanner.Position;
                return blockValue;
            }

            if (TryConsume('('))
            {
                CtsValue? inner = ParseOr();
                if (!TryConsume(')'))
                {
                    AddError(_diagnostics, "Expected ')' after expression.", PointSpan());
                }

                return inner;
            }

            if (StartsWith("e^(") || StartsWith("10^("))
            {
                string name = StartsWith("e^(") ? "exp" : "pow10";
                _index += name == "exp" ? 3 : 4;
                List<CtsValue> arguments = ParseFunctionArguments();
                return new CtsFunctionValue(name, arguments, SpanFrom(start, _index));
            }

            if (_text[_index] == '"')
            {
                return ReadString();
            }

            if (char.IsDigit(_text[_index]) || (_text[_index] == '.' && _index + 1 < _text.Length && char.IsDigit(_text[_index + 1])))
            {
                return ReadNumber();
            }

            string? identifier = ReadIdentifier();
            if (identifier is null)
            {
                return null;
            }

            SkipWhitespace();
            if (TryConsume('('))
            {
                List<CtsValue> arguments = ParseFunctionArguments();
                return new CtsFunctionValue(identifier, arguments, SpanFrom(start, _index));
            }

            SourceSpan span = SpanFrom(start, _index);
            if (CtsBlockRegistry.TryResolveExpression(identifier, 0, out _ ) ||
                identifier.EndsWith(".length", StringComparison.Ordinal) ||
                identifier.EndsWith(".contents", StringComparison.Ordinal))
            {
                return new CtsFunctionValue(identifier, [], span);
            }

            return new CtsIdentifierValue(identifier, span);
        }

        private List<CtsValue> ParseFunctionArguments()
        {
            List<CtsValue> arguments = [];
            SkipWhitespace();
            while (!End && !TryConsume(')'))
            {
                CtsValue? argument = ParseOr();
                if (argument is null)
                {
                    AddError(_diagnostics, "Expected a function argument.", PointSpan());
                    break;
                }

                arguments.Add(argument);
                SkipWhitespace();
                if (TryConsume(')'))
                {
                    return arguments;
                }

                if (!TryConsume(','))
                {
                    AddError(_diagnostics, "Expected ',' or ')' after function argument.", PointSpan());
                    break;
                }
            }

            return arguments;
        }

        private CtsStringValue ReadString()
        {
            int start = _index++;
            System.Text.StringBuilder builder = new();
            bool escaped = false;
            while (!End)
            {
                char value = _text[_index++];
                if (escaped)
                {
                    builder.Append(value switch
                    {
                        'n' => '\n', 'r' => '\r', 't' => '\t', _ => value
                    });
                    escaped = false;
                }
                else if (value == '\\')
                {
                    escaped = true;
                }
                else if (value == '"')
                {
                    return DecodeString(_text[start.._index], SpanFrom(start, _index), _diagnostics);
                }
                else
                {
                    builder.Append(value);
                }
            }

            AddError(_diagnostics, "Unterminated string literal.", SpanFrom(start, _index));
            return new CtsStringValue(builder.ToString(), SpanFrom(start, _index));
        }

        private CtsNumberValue ReadNumber()
        {
            int start = _index;
            while (!End && char.IsDigit(_text[_index]))
            {
                _index++;
            }

            if (!End && _text[_index] == '.')
            {
                _index++;
                while (!End && char.IsDigit(_text[_index]))
                {
                    _index++;
                }
            }

            if (!End && _text[_index] is 'e' or 'E')
            {
                int exponent = _index++;
                if (!End && _text[_index] is '+' or '-') _index++;
                int digits = _index;
                while (!End && char.IsDigit(_text[_index])) _index++;
                if (_index == digits) _index = exponent;
            }

            int numericEnd = _index;
            if (!End && _text[_index] == 's')
            {
                _index++;
            }

            string numberText = _text[start..numericEnd];
            _ = double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out double number);
            return new CtsNumberValue(number, numberText, SpanFrom(start, _index));
        }

        private string? ReadIdentifier()
        {
            SkipWhitespace();
            if (End)
            {
                return null;
            }

            if (_text[_index] == '`')
            {
                int start = ++_index;
                while (!End && _text[_index] != '`')
                {
                    _index++;
                }

                string name = _text[start.._index];
                if (!End)
                {
                    _index++;
                }

                return name;
            }

            if (!IsIdentifierStart(_text[_index]))
            {
                return null;
            }

            int identifierStart = _index++;
            while (!End && (char.IsLetterOrDigit(_text[_index]) || _text[_index] is '_' or '.'))
            {
                _index++;
            }

            return _text[identifierStart.._index];
        }

        private bool TryConsumeWord(string word)
        {
            SkipWhitespace();
            if (!_text.AsSpan(_index).StartsWith(word, StringComparison.Ordinal) ||
                (_index + word.Length < _text.Length && IsIdentifierPart(_text[_index + word.Length])))
            {
                return false;
            }

            _index += word.Length;
            return true;
        }

        private bool TryConsumeOperator(string operation)
        {
            SkipWhitespace();
            if (!_text.AsSpan(_index).StartsWith(operation, StringComparison.Ordinal))
            {
                return false;
            }

            _index += operation.Length;
            return true;
        }

        private bool TryConsume(char value)
        {
            SkipWhitespace();
            if (End || _text[_index] != value)
            {
                return false;
            }

            _index++;
            return true;
        }

        private bool StartsWith(string text)
        {
            return _text.AsSpan(_index).StartsWith(text, StringComparison.Ordinal);
        }

        private void SkipWhitespace()
        {
            while (!End && char.IsWhiteSpace(_text[_index]))
            {
                _index++;
            }
        }

        private bool End => _index >= _text.Length;

        private SourceSpan PointSpan()
        {
            int column = _baseColumn + _index;
            return new SourceSpan(new SourceLocation(_line, column), new SourceLocation(_line, column));
        }

        private SourceSpan SpanFrom(int start, int end)
        {
            return new SourceSpan(
                new SourceLocation(_line, _baseColumn + start),
                new SourceLocation(_line, _baseColumn + end));
        }

        private static SourceSpan Combine(SourceSpan left, SourceSpan right)
        {
            return new SourceSpan(left.Start, right.End);
        }
    }

    private sealed record SourceLine(int LineNumber, string Text);

    private sealed class CtsLineScanner
    {
        private readonly string _text;
        private readonly int _line;
        private readonly int _baseColumn;
        private int _index;

        public CtsLineScanner(string text, int line, int baseColumn)
        {
            _text = text;
            _line = line;
            _baseColumn = baseColumn;
        }

        public bool End => _index >= _text.Length;

        public int Position => _index;

        public char Peek()
        {
            return End ? '\0' : _text[_index];
        }

        public void SkipWhitespace()
        {
            while (!End && char.IsWhiteSpace(_text[_index]))
            {
                _index++;
            }
        }

        public bool TryConsume(char value)
        {
            SkipWhitespace();
            if (!End && _text[_index] == value)
            {
                _index++;
                return true;
            }

            return false;
        }

        public bool ConsumeWord(string word)
        {
            SkipWhitespace();
            if (_text.AsSpan(_index).StartsWith(word, StringComparison.Ordinal) &&
                (_index + word.Length == _text.Length || !IsIdentifierPart(_text[_index + word.Length])))
            {
                _index += word.Length;
                return true;
            }

            return false;
        }

        public string? ReadIdentifier()
        {
            SkipWhitespace();
            if (End || !IsIdentifierStart(_text[_index]))
            {
                return null;
            }

            int start = _index;
            _index++;
            while (!End && (IsIdentifierPart(_text[_index]) || _text[_index] == '.'))
            {
                _index++;
            }

            return _text[start.._index];
        }

        public CtsValue? ReadValue(List<CtsDiagnostic> diagnostics)
        {
            SkipWhitespace();
            if (End)
            {
                AddError(diagnostics, "Expected a value.", PointSpan());
                return null;
            }

            if (_text[_index] == '[')
            {
                return ReadBlockValue(diagnostics);
            }

            if (_text[_index] == '"')
            {
                return ReadString(diagnostics);
            }

            if (char.IsDigit(_text[_index]) ||
                (_text[_index] is '-' or '+' && _index + 1 < _text.Length && char.IsDigit(_text[_index + 1])))
            {
                return ReadNumber();
            }

            if (IsIdentifierStart(_text[_index]))
            {
                SourceSpan start = PointSpan();
                string? name = ReadIdentifier();
                return name is null ? null : new CtsIdentifierValue(name, new SourceSpan(start.Start, PointSpan().End));
            }

            AddError(diagnostics, "Expected a value.", PointSpan());
            return null;
        }

        public CtsValue? ReadExpression(List<CtsDiagnostic> diagnostics)
        {
            SkipWhitespace();
            if (End)
            {
                AddError(diagnostics, "Expected a value.", PointSpan());
                return null;
            }

            CtsExpressionParser parser = new(_text[_index..], _line, _baseColumn + _index, diagnostics);
            List<CtsValue> values = parser.ParseAll();
            _index = _text.Length;
            if (values.Count != 1)
            {
                AddError(diagnostics, "Expected exactly one expression.", PointSpan());
                return null;
            }

            return values[0];
        }

        private CtsBlockValue? ReadBlockValue(List<CtsDiagnostic> diagnostics)
        {
            SourceLocation startLocation = PointSpan().Start;
            _index++;
            SkipWhitespace();

            bool isShadow = ConsumeWord("shadow");
            SkipWhitespace();
            CtsValue? opcodeValue = ReadValue(diagnostics);
            string? opcode = ValueToText(opcodeValue);
            if (string.IsNullOrWhiteSpace(opcode))
            {
                AddError(diagnostics, "Expected an opcode in reporter block value.", PointSpan());
                return null;
            }

            ParseBlockClauses(
                this,
                diagnostics,
                out List<CtsRawInput> inputs,
                out List<CtsRawField> fields,
                out Dictionary<string, CtsValue> mutation,
                ']');

            SkipWhitespace();
            if (!TryConsume(']'))
            {
                AddError(diagnostics, "Expected ']' after reporter block value.", PointSpan());
            }

            return new CtsBlockValue(
                opcode,
                inputs,
                fields,
                mutation,
                isShadow,
                new SourceSpan(startLocation, PointSpan().End));
        }

        public SourceSpan PointSpan()
        {
            int column = _baseColumn + _index;
            return new SourceSpan(new SourceLocation(_line, column), new SourceLocation(_line, column));
        }

        private CtsStringValue ReadString(List<CtsDiagnostic> diagnostics)
        {
            SourceLocation startLocation = new(_line, _baseColumn + _index);
            int startIndex = _index;
            _index++;
            StringBuilderBuilder builder = new();
            bool escaped = false;

            while (!End)
            {
                char value = _text[_index++];
                if (escaped)
                {
                    builder.Append(value switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '"' => '"',
                        '\\' => '\\',
                        _ => value
                    });
                    escaped = false;
                    continue;
                }

                if (value == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (value == '"')
                {
                    return DecodeString(_text[startIndex.._index],
                        new SourceSpan(startLocation, new SourceLocation(_line, _baseColumn + _index)), diagnostics);
                }

                builder.Append(value);
            }

            AddError(diagnostics, "Unterminated string literal.", new SourceSpan(startLocation, new SourceLocation(_line, _baseColumn + _index)));
            return new CtsStringValue(builder.ToString(), new SourceSpan(startLocation, new SourceLocation(_line, _baseColumn + _index)));
        }

        private CtsNumberValue ReadNumber()
        {
            SourceLocation startLocation = new(_line, _baseColumn + _index);
            int start = _index;
            if (!End && _text[_index] is '-' or '+')
            {
                _index++;
            }

            while (!End && char.IsDigit(_text[_index]))
            {
                _index++;
            }

            if (!End && _text[_index] == '.')
            {
                _index++;
                while (!End && char.IsDigit(_text[_index]))
                {
                    _index++;
                }
            }

            if (!End && _text[_index] is 'e' or 'E')
            {
                int exponent = _index++;
                if (!End && _text[_index] is '+' or '-') _index++;
                int digits = _index;
                while (!End && char.IsDigit(_text[_index])) _index++;
                if (_index == digits) _index = exponent;
            }

            int numericEnd = _index;
            if (!End && _text[_index] == 's')
            {
                _index++;
            }

            string numericText = _text[start..numericEnd];
            _ = double.TryParse(numericText, NumberStyles.Float, CultureInfo.InvariantCulture, out double number);
            return new CtsNumberValue(
                number,
                numericText,
                new SourceSpan(startLocation, new SourceLocation(_line, _baseColumn + _index)));
        }
    }

    private sealed class StringBuilderBuilder
    {
        private readonly System.Text.StringBuilder _builder = new();

        public void Append(char value)
        {
            _builder.Append(value);
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}
