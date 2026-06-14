using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Scripting;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Windows.Utils;

public partial class ScriptWindow
{
    private static ScriptWindow? _instance;

    private readonly ScriptEngine _engine = IoCContainer.Resolve<ScriptEngine>();
    private readonly ThemeManager _themeManager = IoCContainer.Resolve<ThemeManager>();

    private ScriptWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            _codeInput.Focus();
            UpdateSyntaxColors();
        };
        _codeInput.PreviewKeyDown += CodeInput_PreviewKeyDown;
        _themeManager.ThemeApplied += (_, _) => UpdateSyntaxColors();
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
    }

    private async void Execute_Click(object sender, RoutedEventArgs e) => await ExecuteAsync();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _engine.Reset();
        _outputBox.Text = "";
        SetStatus(Resource.ScriptConsole_Status_Ready, StatusColor.Default);
    }

    private async Task ExecuteAsync()
    {
        var code = _codeInput.Text;
        if (string.IsNullOrWhiteSpace(code))
            return;

        _executeButton.IsEnabled = false;
        SetStatus(Resource.ScriptConsole_Status_Running, StatusColor.Default);
        _outputBox.Text = "";

        try
        {
            var result = await _engine.ExecuteAsync(code);

            _outputBox.Text = FormatResult(result);

            if (result.Error is not null)
                SetStatus(string.Format(Resource.ScriptConsole_Status_Error, $"{result.Elapsed.TotalMilliseconds:F0}"), StatusColor.Error);
            else
                SetStatus(string.Format(Resource.ScriptConsole_Status_Ok, $"{result.Elapsed.TotalMilliseconds:F0}"), StatusColor.Success);
        }
        catch (Exception ex)
        {
            _outputBox.Text = string.Format(Resource.ScriptConsole_UnexpectedError_Detail, ex.Message);
            SetStatus(Resource.ScriptConsole_Status_UnexpectedError, StatusColor.Error);
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

    private void UpdateSyntaxColors()
    {
        if (_codeInput.SyntaxHighlighting == null) return;

        Color GetColor(string resourceKey)
        {
            if (Application.Current.TryFindResource(resourceKey) is Color color)
                return color;
            return Colors.Gray;
        }

        var commentColor = GetColor("PaletteGreenColor");
        var stringColor = GetColor("PaletteOrangeColor");
        var keywordColor = GetColor("PaletteLightBlueColor");
        var textColor = GetColor("TextFillColorPrimary");
        var numberColor = GetColor("PaletteTealColor");
        var preprocessorColor = GetColor("TextFillColorSecondary");

        void SetColor(string name, Color color)
        {
            var rule = _codeInput.SyntaxHighlighting.GetNamedColor(name);
            if (rule != null)
                rule.Foreground = new ICSharpCode.AvalonEdit.Highlighting.SimpleHighlightingBrush(color);
        }

        SetColor("Comment", commentColor);
        SetColor("String", stringColor);
        SetColor("Char", stringColor);
        SetColor("Preprocessor", preprocessorColor);
        SetColor("Punctuation", textColor);
        SetColor("ValueTypeKeywords", keywordColor);
        SetColor("ReferenceTypeKeywords", keywordColor);
        SetColor("MethodCall", textColor);
        SetColor("NumberLiteral", numberColor);
        SetColor("ThisOrBaseReference", keywordColor);
        SetColor("NullOrValueKeywords", keywordColor);
        SetColor("Keywords", keywordColor);
        SetColor("GotoKeywords", keywordColor);
        SetColor("ContextKeywords", keywordColor);
        SetColor("ExceptionKeywords", keywordColor);
        SetColor("CheckedKeyword", keywordColor);
        SetColor("UnsafeKeywords", keywordColor);
        SetColor("OperatorKeywords", keywordColor);
        SetColor("ParameterModifiers", keywordColor);
        SetColor("Modifiers", keywordColor);
        SetColor("Visibility", keywordColor);
        SetColor("NamespaceKeywords", keywordColor);
        SetColor("GetSetAddRemove", keywordColor);
        SetColor("TrueFalse", keywordColor);
        SetColor("TypeKeywords", keywordColor);

        _codeInput.TextArea.TextView.Redraw();
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

        AppendSection(Resource.ScriptConsole_Output_Title, result.Output);
        AppendSection(Resource.ScriptConsole_Section_ReturnValue, result.ReturnValue?.ToString());
        AppendSection(Resource.ScriptConsole_Section_Error, result.Error);

        sb.Append(string.Format(Resource.ScriptConsole_Section_Elapsed, $"{result.Elapsed.TotalMilliseconds:F0}"));
        return sb.ToString().TrimEnd();
    }
}
