using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace LenovoLegionToolkit.Lib.Scripting;

public class ScriptEngine
{
    private ScriptOptions? _options;
    private ScriptState<object>? _state;
    private ScriptGlobals _globals = new();

    private ScriptOptions Options
    {
        get
        {
            if (_options is not null)
                return _options;

            var assemblies = GetAssemblies();

            _options = ScriptOptions.Default
                .WithReferences(assemblies)
                .WithImports(
                    "System",
                    "System.Linq",
                    "System.Threading.Tasks",
                    "LenovoLegionToolkit.Lib",
                    "LenovoLegionToolkit.Lib.Controllers",
                    "LenovoLegionToolkit.Lib.Controllers.Sensors",
                    "LenovoLegionToolkit.Lib.Utils"
                );

            return _options;
        }
    }

    public void Reset()
    {
        _state = null;
        _globals = new ScriptGlobals();
    }

    public async Task<ScriptResult> ExecuteAsync(string code, CancellationToken ct = default)
    {
        var validationResult = Validate(code);
        if (validationResult is not null)
        {
            return validationResult;
        }

        var sw = global::System.Diagnostics.Stopwatch.StartNew();

        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            object? returnValue;

            if (_state is null)
            {
                _state = await CSharpScript.RunAsync(code, Options, _globals, typeof(ScriptGlobals), ct).ConfigureAwait(false);
                returnValue = _state.ReturnValue;
            }
            else
            {
                _state = await _state.ContinueWithAsync(code, Options, cancellationToken: ct).ConfigureAwait(false);
                returnValue = _state.ReturnValue;
            }

            sw.Stop();
            return new ScriptResult(writer.ToString().TrimEnd(), returnValue, null, sw.Elapsed);
        }
        catch (CompilationErrorException ex)
        {
            sw.Stop();
            return new ScriptResult(writer.ToString().TrimEnd(), null, string.Join(Environment.NewLine, ex.Diagnostics), sw.Elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ScriptResult(writer.ToString().TrimEnd(), null, ex.ToString(), sw.Elapsed);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static ScriptResult? Validate(string code)
    {
        if (code.Contains("#r", StringComparison.OrdinalIgnoreCase))
        {
            return new ScriptResult(null, null, "Security check failed: #r directives are not allowed.", TimeSpan.Zero);
        }

        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
        var validator = new ScriptSecurityValidator();
        validator.Visit(tree.GetRoot());

        if (validator.Violations.Count > 0)
        {
            var errors = validator.Violations
                .Select(v => $"Line {v.Line}, Col {v.Column}: {v.Message}");
            return new ScriptResult(null, null, "Security check failed:" + Environment.NewLine + string.Join(Environment.NewLine, errors), TimeSpan.Zero);
        }

        return null;
    }

    private static readonly HashSet<string> AllowedAssemblyPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "LenovoLegionToolkit",
        "Newtonsoft.Json",
        "NeoSmart.AsyncLock",
        "System.Runtime",
        "System.Linq",
        "System.Console",
        "System.Collections",
        "System.Threading",
        "System.Text",
    };

    private static MetadataReference[] GetAssemblies()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Where(a =>
            {
                var name = a.GetName().Name;
                return name is not null && AllowedAssemblyPrefixes.Any(p =>
                    name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            })
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToArray();
    }
}
