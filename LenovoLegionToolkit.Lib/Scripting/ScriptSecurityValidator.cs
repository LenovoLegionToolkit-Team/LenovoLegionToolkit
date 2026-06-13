using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LenovoLegionToolkit.Lib.Scripting;

public class ScriptSecurityValidator : CSharpSyntaxWalker
{
    public record Violation(string Message, int Line, int Column);

    private readonly List<Violation> _violations = [];
    private readonly HashSet<(int, int)> _seen = [];

    public IReadOnlyList<Violation> Violations => _violations;

    private static readonly HashSet<string> BlockedNamespacePrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Diagnostics",
        "System.IO",
        "System.Net",
        "System.Reflection",
        "System.Runtime.InteropServices",
        "Microsoft.Win32",
        "System.Management",
        "System.Data",
        "System.DirectoryServices",
        "System.Security",
        "System.Web",
        "System.Linq.Expressions",
    };

    private static readonly (string Type, string Message)[] BlockedTypes =
    [
        ("System.Diagnostics.Process", "Process execution is not allowed"),
        ("System.IO.File", "File system operations are not allowed"),
        ("System.IO.Directory", "Directory operations are not allowed"),
        ("System.IO.FileInfo", "File system operations are not allowed"),
        ("System.IO.DirectoryInfo", "Directory operations are not allowed"),
        ("System.IO.FileStream", "File stream operations are not allowed"),
        ("System.Net.Http.HttpClient", "Network access is not allowed"),
        ("System.Net.WebClient", "Network access is not allowed"),
        ("System.Net.Sockets.Socket", "Network access is not allowed"),
        ("System.Net.Sockets.TcpClient", "Network access is not allowed"),
        ("System.Net.Sockets.UdpClient", "Network access is not allowed"),
        ("System.Reflection.Assembly", "Reflection is not allowed"),
        ("System.Reflection.MethodInfo", "Reflection is not allowed"),
        ("System.Reflection.MethodBase", "Reflection is not allowed"),
        ("System.Reflection.ConstructorInfo", "Reflection is not allowed"),
        ("System.AppDomain", "Assembly/AppDomain enumeration is not allowed"),
        ("System.Type", "Runtime type discovery is not allowed"),
        ("System.Activator", "Dynamic instantiation is not allowed"),
        ("System.Runtime.InteropServices.Marshal", "Native interop is not allowed"),
        ("Microsoft.Win32.Registry", "Registry access is not allowed"),
        ("System.Management.ManagementObject", "WMI access is not allowed"),
    ];

    private static readonly HashSet<string> DangerousMethodNames = new(StringComparer.Ordinal)
    {
        "GetAssemblies",
        "GetType",
        "GetMethod",
        "GetMethods",
        "GetField",
        "GetFields",
        "GetProperty",
        "GetProperties",
        "GetConstructor",
        "GetConstructors",
        "GetEvent",
        "GetEvents",
        "GetNestedType",
        "GetNestedTypes",
        "GetInterface",
        "GetInterfaces",
        "Load",
        "LoadFrom",
        "LoadFile",
        "LoadWithPartialName",
        "CreateInstance",
        "InvokeMember",
        "Invoke",
        "Compile",
        "MakeGenericType",
        "MakeGenericMethod",
        "CreateDelegate",
    };

    public ScriptSecurityValidator() : base(SyntaxWalkerDepth.StructuredTrivia) { }

    public override void VisitUsingDirective(UsingDirectiveSyntax node)
    {
        var ns = node.Name?.ToString() ?? "";

        foreach (var prefix in BlockedNamespacePrefixes)
        {
            if (IsBlockedNamespace(ns, prefix))
            {
                AddViolation(node, $"Using '{ns}' is blocked — {prefix} is not allowed in scripts");
                break;
            }
        }

        base.VisitUsingDirective(node);
    }

    public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (node.Parent is InvocationExpressionSyntax)
        {
            var text = node.ToString();

            foreach (var (type, message) in BlockedTypes)
            {
                if (text.StartsWith(type, StringComparison.OrdinalIgnoreCase)
                    && (text.Length == type.Length || text[type.Length] == '.'))
                {
                    AddViolation(node, $"{message}: '{text}'");
                    break;
                }
            }

            var methodName = node.Name.Identifier.Text;
            if (DangerousMethodNames.Contains(methodName))
            {
                AddViolation(node, $"Reflection/discovery is not allowed: '{text}'");
            }
        }

        base.VisitMemberAccessExpression(node);
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        var typeName = node.Type.ToString();

        foreach (var (type, message) in BlockedTypes)
        {
            if (typeName.StartsWith(type, StringComparison.OrdinalIgnoreCase))
            {
                AddViolation(node, $"{message}: cannot create '{typeName}'");
                break;
            }
        }

        base.VisitObjectCreationExpression(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (node.Identifier.Text.Equals("DllImport", StringComparison.OrdinalIgnoreCase))
        {
            AddViolation(node, "Native interop (DllImport) is not allowed");
        }

        base.VisitIdentifierName(node);
    }

    private void AddViolation(SyntaxNode node, string message)
    {
        var loc = node.GetLocation();
        var lineSpan = loc.GetLineSpan();
        var line = lineSpan.StartLinePosition.Line + 1;
        var col = lineSpan.StartLinePosition.Character + 1;

        if (_seen.Add((line, col)))
            _violations.Add(new Violation(message, line, col));
    }

    private static bool IsBlockedNamespace(string ns, string prefix) => ns.Equals(prefix, StringComparison.OrdinalIgnoreCase) || ns.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);
}
