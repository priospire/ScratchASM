using System.Globalization;

namespace OpenCTS.Core;

public static class CtsBinder
{
    public static CtsSemanticModel Bind(CtsCompilationUnit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        Binder binder = new(unit);
        return binder.Bind();
    }

    private sealed class Binder
    {
        private readonly CtsCompilationUnit _unit;
        private readonly List<CtsDiagnostic> _diagnostics = [];
        private readonly Dictionary<string, CtsValue> _constants = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CtsStructDeclaration> _structs = new(StringComparer.Ordinal);
        private readonly HashSet<string> _fileNames = new(StringComparer.Ordinal);

        public Binder(CtsCompilationUnit unit)
        {
            _unit = unit;
        }

        public CtsSemanticModel Bind()
        {
            BindFileDeclarations();
            CtsTargetDeclaration[] targets = _unit.Targets.Select(BindTarget).ToArray();
            CtsCompilationUnit boundUnit = new(targets, _unit.Span)
            {
                FileDeclarations = _unit.FileDeclarations
            };
            return new CtsSemanticModel(boundUnit, _constants, _structs, _diagnostics);
        }

        private void BindFileDeclarations()
        {
            foreach (CtsFileDeclaration declaration in _unit.FileDeclarations)
            {
                if (!_fileNames.Add(GetDeclarationName(declaration)))
                {
                    AddError("CTS1021", $"File-scoped declaration '{GetDeclarationName(declaration)}' is already defined.", declaration.Span);
                    continue;
                }

                switch (declaration)
                {
                    case CtsConstDeclaration constant:
                        if (TryEvaluateConstant(constant.Value, out CtsValue? value, constant.Span))
                        {
                            _constants[constant.Name] = value;
                        }

                        break;
                    case CtsEnumDeclaration enumDeclaration:
                        BindEnum(enumDeclaration);
                        break;
                    case CtsStructDeclaration structDeclaration:
                        _structs[structDeclaration.Name] = BindStruct(structDeclaration);
                        break;
                }
            }
        }

        private void BindEnum(CtsEnumDeclaration declaration)
        {
            HashSet<string> members = new(StringComparer.Ordinal);
            foreach (CtsEnumMember member in declaration.Members)
            {
                if (!members.Add(member.Name))
                {
                    AddError("CTS1022", $"Enum member '{declaration.Name}.{member.Name}' is already defined.", member.Span);
                    continue;
                }

                if (member.Value is null)
                {
                    AddError("CTS1023", $"Enum member '{declaration.Name}.{member.Name}' requires an explicit value.", member.Span);
                    continue;
                }

                if (TryEvaluateConstant(member.Value, out CtsValue? value, member.Span))
                {
                    _constants[$"{declaration.Name}.{member.Name}"] = value;
                }
            }
        }

        private CtsStructDeclaration BindStruct(CtsStructDeclaration declaration)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            List<CtsStructField> fields = [];
            foreach (CtsStructField field in declaration.Fields)
            {
                if (!names.Add(field.Name))
                {
                    AddError("CTS1024", $"Struct field '{declaration.Name}.{field.Name}' is already defined.", field.Span);
                    continue;
                }

                if (!IsScalarType(field.TypeName))
                {
                    AddError("CTS1025", $"Nested structs are not supported; field '{declaration.Name}.{field.Name}' must use num, str, or bool.", field.Span);
                    continue;
                }

                CtsValue defaultValue = DefaultValue(field.TypeName, field.Span);
                if (field.DefaultValue is not null && TryEvaluateConstant(field.DefaultValue, out CtsValue? evaluated, field.Span))
                {
                    defaultValue = evaluated;
                }

                fields.Add(field with { DefaultValue = defaultValue });
            }

            return declaration with { Fields = fields };
        }

        private CtsTargetDeclaration BindTarget(CtsTargetDeclaration target)
        {
            List<CtsTargetMember> members = [];
            Dictionary<string, CtsStructDeclaration> instances = new(StringComparer.Ordinal);
            HashSet<string> dataNames = new(StringComparer.Ordinal);

            foreach (CtsTargetMember member in target.Members)
            {
                switch (member)
                {
                    case CtsVariableDeclaration variable:
                        ValidateVariableScope(target, variable);
                        AddDataName(dataNames, variable.Name, variable.Span);
                        members.Add(variable with { InitialValue = RewriteValue(variable.InitialValue, instances) });
                        break;
                    case CtsStructInstanceDeclaration instance:
                        if (!_structs.TryGetValue(instance.TypeName, out CtsStructDeclaration? structDeclaration))
                        {
                            AddError("CTS1026", $"Unknown struct type '{instance.TypeName}'.", instance.Span);
                            break;
                        }

                        if (!AddDataName(dataNames, instance.Name, instance.Span))
                        {
                            break;
                        }

                        instances[instance.Name] = structDeclaration;
                        foreach (CtsStructField field in structDeclaration.Fields)
                        {
                            string fieldName = $"{instance.Name}.{field.Name}";
                            members.Add(new CtsVariableDeclaration(
                                fieldName,
                                field.DefaultValue ?? DefaultValue(field.TypeName, field.Span),
                                false,
                                instance.Span));
                        }

                        break;
                    case CtsListDeclaration list:
                        AddDataName(dataNames, list.Name, list.Span);
                        members.Add(list with { Items = list.Items.Select(value => RewriteValue(value, instances)).ToArray() });
                        break;
                    case CtsBroadcastDeclaration broadcast:
                        members.Add(broadcast with { Message = RewriteValue(broadcast.Message, instances) });
                        break;
                    default:
                        members.Add(member);
                        break;
                }
            }

            CtsScript[] scripts = target.Scripts.Select(script => BindScript(script, instances)).ToArray();
            return target with { Members = members, Scripts = scripts };
        }

        private CtsScript BindScript(CtsScript script, IReadOnlyDictionary<string, CtsStructDeclaration> instances)
        {
            return script switch
            {
                CtsStackScript stack => stack with { Statements = BindStatements(stack.Statements, instances, inProcedure: false) },
                CtsReporterScript reporter => reporter with { Value = RewriteValue(reporter.Value, instances) },
                CtsHatScript hat => hat with { Statements = BindStatements(hat.Statements, instances, inProcedure: false) },
                CtsGenericHatScript hat => hat with { Statements = BindStatements(hat.Statements, instances, inProcedure: false) },
                CtsAliasHatScript hat => hat with
                {
                    Arguments = hat.Arguments.Select(value => RewriteValue(value, instances)).ToArray(),
                    Statements = BindStatements(hat.Statements, instances, inProcedure: false)
                },
                CtsProcedureDefinition procedure => BindProcedure(procedure, instances),
                _ => script
            };
        }

        private CtsProcedureDefinition BindProcedure(
            CtsProcedureDefinition procedure,
            IReadOnlyDictionary<string, CtsStructDeclaration> instances)
        {
            foreach (CtsProcedureParameter parameter in procedure.Parameters)
            {
                string typeName = parameter.DeclaredType ?? TypeName(parameter.Type);
                if (_structs.ContainsKey(typeName))
                {
                    AddError("CTS1027", $"Struct parameter '{parameter.Name}' is not supported; pass scalar fields instead.", parameter.Span);
                }
                else if (!IsScalarType(typeName))
                {
                    AddError("CTS1028", $"Procedure parameter '{parameter.Name}' must use num, str, or bool.", parameter.Span);
                }
            }

            if (procedure.DeclaredReturnType is not null)
            {
                string prefix = _structs.ContainsKey(procedure.DeclaredReturnType) ? "Struct returns" : "Procedure returns";
                AddError("CTS1029", $"{prefix} are not supported.", procedure.Span);
            }

            CtsProcedureParameter[] parameters = procedure.Parameters
                .Select(parameter => parameter with
                {
                    DefaultValue = parameter.DefaultValue is null ? null : RewriteValue(parameter.DefaultValue, instances)
                })
                .ToArray();
            return procedure with
            {
                Parameters = parameters,
                Statements = BindStatements(procedure.Statements, instances, inProcedure: true, allowLocalDeclarations: true)
            };
        }

        private IReadOnlyList<CtsStatement> BindStatements(
            IReadOnlyList<CtsStatement> statements,
            IReadOnlyDictionary<string, CtsStructDeclaration> instances,
            bool inProcedure,
            bool allowLocalDeclarations = false)
        {
            List<CtsStatement> bound = [];
            bool executableSeen = false;
            foreach (CtsStatement statement in statements)
            {
                if (statement is CtsLocalVariableDeclaration local)
                {
                    if (!inProcedure)
                    {
                        AddError("CTS1030", "Local variables can only be declared in procedures.", local.Span);
                    }
                    else if (!allowLocalDeclarations || executableSeen)
                    {
                        AddError("CTS1031", "Local variables must be declared at the start of a procedure before executable statements.", local.Span);
                    }

                    bound.Add(local with { InitialValue = RewriteValue(local.InitialValue, instances) });
                    continue;
                }

                executableSeen = true;
                bound.Add(BindStatement(statement, instances, inProcedure));
            }

            return bound;
        }

        private CtsStatement BindStatement(
            CtsStatement statement,
            IReadOnlyDictionary<string, CtsStructDeclaration> instances,
            bool inProcedure)
        {
            switch (statement)
            {
                case CtsVariableOperationStatement variable:
                    ValidateDataReference(variable.VariableName, instances, variable.Span, isAssignment: true);
                    return variable with { Value = RewriteValue(variable.Value, instances) };
                case CtsListOperationStatement list:
                    return list with { Arguments = list.Arguments.Select(value => RewriteValue(value, instances)).ToArray() };
                case CtsCallStatement call:
                    return call with { Arguments = call.Arguments.Select(value => RewriteValue(value, instances)).ToArray() };
                case CtsAliasStatement alias:
                    return alias with { Arguments = alias.Arguments.Select(value => RewriteValue(value, instances)).ToArray() };
                case CtsStructuredStatement structured:
                    return structured with
                    {
                        Arguments = structured.Arguments.Select(value => RewriteValue(value, instances)).ToArray(),
                        Substacks = structured.Substacks.ToDictionary(
                            pair => pair.Key,
                            pair => BindStatements(pair.Value, instances, inProcedure),
                            StringComparer.Ordinal)
                    };
                case CtsRawStatement raw:
                    return raw with
                    {
                        Inputs = raw.Inputs.Select(input => input with { Value = RewriteValue(input.Value, instances) }).ToArray()
                    };
                case CtsBlockStatement block:
                    return block with
                    {
                        Inputs = block.Inputs.Select(input => input with { Value = RewriteValue(input.Value, instances) }).ToArray(),
                        Statements = BindStatements(block.Statements, instances, inProcedure),
                        NamedSubstacks = block.NamedSubstacks.ToDictionary(
                            pair => pair.Key,
                            pair => BindStatements(pair.Value, instances, inProcedure),
                            StringComparer.Ordinal)
                    };
                default:
                    return statement;
            }
        }

        private CtsValue RewriteValue(CtsValue value, IReadOnlyDictionary<string, CtsStructDeclaration> instances)
        {
            switch (value)
            {
                case CtsIdentifierValue identifier:
                    if (_constants.TryGetValue(identifier.Name, out CtsValue? constant))
                    {
                        return WithSpan(constant, identifier.Span);
                    }

                    ValidateDataReference(identifier.Name, instances, identifier.Span, isAssignment: false);
                    return identifier;
                case CtsUnaryValue unary:
                    return unary with { Operand = RewriteValue(unary.Operand, instances) };
                case CtsBinaryValue binary:
                    return binary with
                    {
                        Left = RewriteValue(binary.Left, instances),
                        Right = RewriteValue(binary.Right, instances)
                    };
                case CtsFunctionValue function:
                    if (function.Name is "new" or "alloc" or "identity")
                    {
                        AddError("CTS1032", "Struct identity and allocation operations are not supported.", function.Span);
                    }

                    return function with { Arguments = function.Arguments.Select(argument => RewriteValue(argument, instances)).ToArray() };
                case CtsBlockValue block:
                    return block with
                    {
                        Inputs = block.Inputs.Select(input => input with { Value = RewriteValue(input.Value, instances) }).ToArray()
                    };
                default:
                    return value;
            }
        }

        private void ValidateDataReference(
            string name,
            IReadOnlyDictionary<string, CtsStructDeclaration> instances,
            SourceSpan span,
            bool isAssignment)
        {
            if (instances.ContainsKey(name))
            {
                AddError("CTS1033", isAssignment
                    ? $"Whole-instance assignment to '{name}' is not supported."
                    : $"Whole-instance reads of '{name}' are not supported.", span);
                return;
            }

            int dot = name.IndexOf('.', StringComparison.Ordinal);
            if (dot <= 0 || !instances.TryGetValue(name[..dot], out CtsStructDeclaration? declaration))
            {
                return;
            }

            string fieldName = name[(dot + 1)..];
            if (!declaration.Fields.Any(field => string.Equals(field.Name, fieldName, StringComparison.Ordinal)))
            {
                AddError("CTS1034", $"Struct '{declaration.Name}' has no field '{fieldName}'.", span);
            }
        }

        private void ValidateVariableScope(CtsTargetDeclaration target, CtsVariableDeclaration variable)
        {
            if (variable.Scope == CtsVariableScope.Global && !target.IsStage)
            {
                AddError("CTS1035", $"Global variable '{variable.Name}' must be declared on the stage.", variable.Span);
            }

            if (variable.Scope == CtsVariableScope.Sprite && target.IsStage)
            {
                AddError("CTS1036", $"Sprite variable '{variable.Name}' must be declared on a sprite.", variable.Span);
            }

            if (variable.IsCloud && (!target.IsStage || variable.Scope != CtsVariableScope.Global))
            {
                AddError("CTS1037", $"Cloud global variable '{variable.Name}' must be declared on the stage.", variable.Span);
            }
        }

        private bool TryEvaluateConstant(CtsValue expression, out CtsValue value, SourceSpan span)
        {
            switch (expression)
            {
                case CtsNumberValue or CtsStringValue:
                    value = expression;
                    return true;
                case CtsIdentifierValue identifier when identifier.Name is "true" or "false":
                    value = Number(identifier.Name == "true" ? 1 : 0, expression.Span);
                    return true;
                case CtsIdentifierValue identifier when _constants.TryGetValue(identifier.Name, out CtsValue? constant):
                    value = WithSpan(constant, expression.Span);
                    return true;
                case CtsIdentifierValue identifier:
                    AddError("CTS1038", $"Constant '{identifier.Name}' must refer to a prior constant or qualified enum member.", identifier.Span);
                    value = Number(0, span);
                    return false;
                case CtsFunctionValue function:
                    AddError("CTS1039", $"Constant function '{function.Name}' is not a pure deterministic operator.", function.Span);
                    value = Number(0, span);
                    return false;
                case CtsBlockValue block:
                    AddError("CTS1039", $"Constant opcode '{block.Opcode}' is not a pure deterministic operator.", block.Span);
                    value = Number(0, span);
                    return false;
                case CtsUnaryValue unary:
                    if (!TryEvaluateConstant(unary.Operand, out CtsValue operand, span))
                    {
                        value = Number(0, span);
                        return false;
                    }

                    return TryEvaluateUnary(unary, operand, out value);
                case CtsBinaryValue binary:
                    if (!TryEvaluateConstant(binary.Left, out CtsValue left, span) ||
                        !TryEvaluateConstant(binary.Right, out CtsValue right, span))
                    {
                        value = Number(0, span);
                        return false;
                    }

                    return TryEvaluateBinary(binary, left, right, out value);
                default:
                    value = Number(0, span);
                    return false;
            }
        }

        private bool TryEvaluateUnary(CtsUnaryValue unary, CtsValue operand, out CtsValue value)
        {
            if (unary.Operator == "not")
            {
                value = Number(IsTruthy(operand) ? 0 : 1, unary.Span);
                return true;
            }

            if (TryNumber(operand, out double number))
            {
                value = Number(-number, unary.Span);
                return true;
            }

            AddError("CTS1040", $"Operator '{unary.Operator}' requires a numeric constant.", unary.Span);
            value = Number(0, unary.Span);
            return false;
        }

        private bool TryEvaluateBinary(CtsBinaryValue binary, CtsValue left, CtsValue right, out CtsValue value)
        {
            if (binary.Operator is "and" or "or")
            {
                bool booleanResult = binary.Operator == "and" ? IsTruthy(left) && IsTruthy(right) : IsTruthy(left) || IsTruthy(right);
                value = Number(booleanResult ? 1 : 0, binary.Span);
                return true;
            }

            if (binary.Operator is "==" or "!=")
            {
                bool equal = string.Equals(ConstantText(left), ConstantText(right), StringComparison.Ordinal);
                value = Number((binary.Operator == "==" ? equal : !equal) ? 1 : 0, binary.Span);
                return true;
            }

            if (!TryNumber(left, out double leftNumber) || !TryNumber(right, out double rightNumber))
            {
                AddError("CTS1040", $"Operator '{binary.Operator}' requires numeric constants.", binary.Span);
                value = Number(0, binary.Span);
                return false;
            }

            double result;
            switch (binary.Operator)
            {
                case "+": result = leftNumber + rightNumber; break;
                case "-": result = leftNumber - rightNumber; break;
                case "*": result = leftNumber * rightNumber; break;
                case "/": result = rightNumber == 0 ? double.NaN : leftNumber / rightNumber; break;
                case "%": result = rightNumber == 0 ? double.NaN : leftNumber % rightNumber; break;
                case "^": result = Math.Pow(leftNumber, rightNumber); break;
                case "<": result = leftNumber < rightNumber ? 1 : 0; break;
                case ">": result = leftNumber > rightNumber ? 1 : 0; break;
                case "<=": result = leftNumber <= rightNumber ? 1 : 0; break;
                case ">=": result = leftNumber >= rightNumber ? 1 : 0; break;
                default:
                    AddError("CTS1040", $"Operator '{binary.Operator}' is not supported in constants.", binary.Span);
                    value = Number(0, binary.Span);
                    return false;
            }

            if (!double.IsFinite(result))
            {
                AddError("CTS1041", $"Constant operator '{binary.Operator}' produced a non-finite value.", binary.Span);
                value = Number(0, binary.Span);
                return false;
            }

            value = Number(result, binary.Span);
            return true;
        }

        private bool AddDataName(HashSet<string> names, string name, SourceSpan span)
        {
            if (names.Add(name))
            {
                return true;
            }

            AddError("CTS1042", $"Target data name '{name}' is already defined.", span);
            return false;
        }

        private void AddError(string code, string message, SourceSpan span)
        {
            _diagnostics.Add(new CtsDiagnostic(code, DiagnosticSeverity.Error, message, span));
        }

        private static string GetDeclarationName(CtsFileDeclaration declaration) => declaration switch
        {
            CtsConstDeclaration constant => constant.Name,
            CtsEnumDeclaration enumDeclaration => enumDeclaration.Name,
            CtsStructDeclaration structDeclaration => structDeclaration.Name,
            _ => string.Empty
        };

        private static bool IsScalarType(string typeName) => typeName is "num" or "str" or "bool";

        private static string TypeName(CtsParameterType type) => type switch
        {
            CtsParameterType.Number => "num",
            CtsParameterType.Boolean => "bool",
            _ => "str"
        };

        private static CtsValue DefaultValue(string typeName, SourceSpan span) => typeName == "str"
            ? new CtsStringValue(string.Empty, span)
            : Number(0, span);

        private static bool TryNumber(CtsValue value, out double number)
        {
            if (value is CtsNumberValue numeric)
            {
                number = numeric.Number;
                return true;
            }

            number = 0;
            return false;
        }

        private static bool IsTruthy(CtsValue value) => value switch
        {
            CtsNumberValue number => number.Number != 0,
            CtsStringValue text => text.Text.Length > 0,
            _ => false
        };

        private static string ConstantText(CtsValue value) => value switch
        {
            CtsNumberValue number => number.Number.ToString("R", CultureInfo.InvariantCulture),
            CtsStringValue text => text.Text,
            _ => string.Empty
        };

        private static CtsNumberValue Number(double value, SourceSpan span) =>
            new(value, value.ToString("R", CultureInfo.InvariantCulture), span);

        private static CtsValue WithSpan(CtsValue value, SourceSpan span) => value switch
        {
            CtsNumberValue number => number with { Span = span },
            CtsStringValue text => text with { Span = span },
            _ => value
        };
    }
}
