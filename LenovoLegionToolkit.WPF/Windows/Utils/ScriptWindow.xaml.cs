using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Scripting;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class ScriptWindow
{
    private static ScriptWindow? _instance;

    private readonly ScriptEngine _engine = IoCContainer.Resolve<ScriptEngine>();

    private ScriptWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => _codeInput.Focus();
        _codeInput.PreviewKeyDown += CodeInput_PreviewKeyDown;
    }

    public static void ShowInstance()
    {
        if (_instance is null)
        {
            _instance = new ScriptWindow();
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        else
        {
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;
            _instance.Activate();
        }
    }

    private void CodeInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = ExecuteAsync();
            return;
        }

        if (e.Key == Key.Tab)
        {
            e.Handled = true;
            if (Keyboard.Modifiers == ModifierKeys.Shift)
                Unindent();
            else
                Indent();
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            e.Handled = true;
            InsertNewLineWithIndent();
            return;
        }
    }

    private void Indent()
    {
        var caret = _codeInput.CaretIndex;
        _codeInput.Text = _codeInput.Text.Insert(caret, "    ");
        _codeInput.CaretIndex = caret + 4;
    }

    private void Unindent()
    {
        if (_codeInput.SelectionLength > 0)
            return;

        var lineStart = _codeInput.Text.LastIndexOf('\n', Math.Max(0, _codeInput.CaretIndex - 1)) + 1;
        var line = _codeInput.Text.Substring(lineStart);
        var spaces = 0;
        while (spaces < 4 && spaces < line.Length && line[spaces] == ' ')
            spaces++;

        if (spaces > 0)
        {
            _codeInput.Select(lineStart, spaces);
            _codeInput.SelectedText = "";
        }
    }

    private void InsertNewLineWithIndent()
    {
        var lineStart = _codeInput.Text.LastIndexOf('\n', Math.Max(0, _codeInput.CaretIndex - 1)) + 1;
        var line = _codeInput.Text[lineStart.._codeInput.CaretIndex];
        var indent = GetLeadingSpaces(line);
        _codeInput.SelectedText = Environment.NewLine + indent;
        _codeInput.CaretIndex = _codeInput.CaretIndex + Environment.NewLine.Length + indent.Length;
    }

    private async void Execute_Click(object sender, RoutedEventArgs e) => await ExecuteAsync();

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        _engine.Reset();
        _outputBox.Text = "";
        SetStatus("Ready — state reset", StatusColor.Default);
    }

    private async Task ExecuteAsync()
    {
        var code = _codeInput.Text;
        if (string.IsNullOrWhiteSpace(code))
            return;

        _executeButton.IsEnabled = false;
        SetStatus("Running...", StatusColor.Default);
        _outputBox.Text = "";

        try
        {
            var result = await _engine.ExecuteAsync(code);

            _outputBox.Text = FormatResult(result);

            if (result.Error is not null)
                SetStatus($"Error — {result.Elapsed.TotalMilliseconds:F0} ms", StatusColor.Error);
            else
                SetStatus($"OK — {result.Elapsed.TotalMilliseconds:F0} ms", StatusColor.Success);
        }
        catch (Exception ex)
        {
            _outputBox.Text = $"┌─ Unexpected Error ──────────────────────────\n│\n│  {ex.Message}\n│\n└──────────────────────────────────────────────";
            SetStatus("Unexpected error", StatusColor.Error);
        }
        finally
        {
            _executeButton.IsEnabled = true;
        }
    }

    private enum StatusColor { Default, Success, Error }

    private void SetStatus(string text, StatusColor color)
    {
        _statusLabel.Text = text;

        var brush = color switch
        {
            StatusColor.Success => (Brush)FindResource("SystemFillColorSuccessBrush"),
            StatusColor.Error => (Brush)FindResource("SystemFillColorCriticalBrush"),
            _ => (Brush)FindResource("TextFillColorSecondaryBrush")
        };

        _statusLabel.Foreground = brush;
    }

    private static string GetLeadingSpaces(string text)
    {
        var count = 0;
        while (count < text.Length && text[count] == ' ')
            count++;
        return text[..count];
    }

    private static string FormatResult(ScriptResult result)
    {
        var sb = new System.Text.StringBuilder();

        void AppendSection(string title, string? content)
        {
            if (string.IsNullOrEmpty(content)) return;
            sb.Append("--- ").Append(title).AppendLine(" ---");
            sb.AppendLine(content.TrimEnd());
            sb.AppendLine();
        }

        AppendSection("Output", result.Output);
        AppendSection("Return Value", result.ReturnValue?.ToString());
        AppendSection("Error", result.Error);

        sb.Append($"--- Elapsed: {result.Elapsed.TotalMilliseconds:F0} ms ---");
        return sb.ToString().TrimEnd();
    }
}
