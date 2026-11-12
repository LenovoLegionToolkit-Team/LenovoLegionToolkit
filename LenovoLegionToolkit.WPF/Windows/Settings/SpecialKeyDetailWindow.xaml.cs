using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation;
using LenovoLegionToolkit.Lib.Automation.Pipeline;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Resources;
using LenovoLegionToolkit.WPF.Utils;

namespace LenovoLegionToolkit.WPF.Windows.Settings;

public partial class SpecialKeyDetailWindow
{
    private readonly AutomationProcessor _automationProcessor = IoCContainer.Resolve<AutomationProcessor>();
    private readonly SpecialKeySettings _settings = IoCContainer.Resolve<SpecialKeySettings>();

    private readonly int _keyCode;
    private readonly string _displayName;
    private bool _isRefreshing;
    private bool _descriptionDirty;
    private CustomSpecialKey _pendingMode;

    private List<Guid> SinglePressActionList =>
        _settings.Store.KeySinglePressActions.GetValueOrDefault(_keyCode, []);

    private List<Guid> DoublePressActionList =>
        _settings.Store.KeyDoublePressActions.GetValueOrDefault(_keyCode, []);

    public SpecialKeyDetailWindow(SpecialKey key)
        : this((int)key, FormatDefaultDisplayName(key)) { }

    public SpecialKeyDetailWindow(int keyCode, string displayName)
    {
        _keyCode = keyCode;
        _displayName = displayName;

        InitializeComponent();

        Title = _title.Text = string.Format(Resource.SelectSpecialKeyPipelinesWindow_Configure_Title, displayName);

        IsVisibleChanged += SpecialKeyDetailWindow_IsVisibleChanged;
    }

    private async void SpecialKeyDetailWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _isRefreshing = true;

        _descriptionTextBox.Text = _settings.Store.KeyDescriptions.TryGetValue(_keyCode, out var desc)
            ? desc : "";
        _descriptionDirty = false;

        var isBuiltIn = Enum.IsDefined(typeof(SpecialKey), (SpecialKey)_keyCode)
            || SpecialKeyCodes.IsDriverKey(_keyCode)
            || SpecialKeyCodes.IsConsumerControlKey(_keyCode)
            || SpecialKeyCodes.IsKeyboardKey(_keyCode);
        _deleteButton.Visibility = isBuiltIn ? Visibility.Collapsed : Visibility.Visible;

        _pendingMode = _settings.Store.KeyModes.TryGetValue(_keyCode, out var m) ? m : CustomSpecialKey.Default;

        _modeComboBox.SetItems(
            [CustomSpecialKey.Default, CustomSpecialKey.Custom],
            _pendingMode,
            v => v.GetDisplayName());

        _modeHeader.Subtitle = Resource.SpecialKeyDetail_Mode_Description;

        var isCustom = _pendingMode == CustomSpecialKey.Custom;
        _customPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

        if (isCustom)
            await RefreshPipelineListAsync();

        _isRefreshing = false;
    }

    private async Task RefreshPipelineListAsync()
    {
        _loaderSingle.IsLoading = true;
        _loaderDouble.IsLoading = true;

        var allPipelines = await _automationProcessor.GetPipelinesAsync();
        var pipelines = allPipelines.Where(p => p.Trigger is null).OrderBy(p => p.Name).ToArray();

        _listSingle.Items.Clear();
        _listDouble.Items.Clear();

        if (pipelines.IsEmpty())
        {
            _listSingle.Items.Add(Resource.SelectSpecialKeyPipelinesWindow_List_Empty);
            _listDouble.Items.Add(Resource.SelectSpecialKeyPipelinesWindow_List_Empty);
        }

        foreach (var pipeline in pipelines)
        {
            var item = new PipelineListItem(pipeline)
            {
                IsChecked = SinglePressActionList.Contains(pipeline.Id)
            };
            _listSingle.Items.Add(item);

            var itemDouble = new PipelineListItem(pipeline)
            {
                IsChecked = DoublePressActionList.Contains(pipeline.Id)
            };
            _listDouble.Items.Add(itemDouble);
        }

        _loaderSingle.IsLoading = false;
        _loaderDouble.IsLoading = false;
    }

    private void DescriptionTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isRefreshing) return;
        _descriptionDirty = true;
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshing) return;

        if (!_modeComboBox.TryGetSelectedItem(out CustomSpecialKey mode)) return;

        _isRefreshing = true;

        _pendingMode = mode;

        var isCustom = mode == CustomSpecialKey.Custom;
        _customPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

        if (isCustom)
            _ = RefreshPipelineListAsync();

        _isRefreshing = false;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_descriptionDirty)
        {
            if (string.IsNullOrWhiteSpace(_descriptionTextBox.Text))
                _settings.Store.KeyDescriptions.Remove(_keyCode);
            else
                _settings.Store.KeyDescriptions[_keyCode] = _descriptionTextBox.Text.Trim();
        }

        _settings.Store.KeyModes[_keyCode] = _pendingMode;

        if (_pendingMode == CustomSpecialKey.Default)
        {
            _settings.Store.KeySinglePressActions.Remove(_keyCode);
            _settings.Store.KeyDoublePressActions.Remove(_keyCode);
        }
        else
        {
            var selectedPipelines = _listSingle.Items.OfType<PipelineListItem>()
                .Where(li => li.IsChecked)
                .Select(li => li.Pipeline.Id)
                .ToList();

            if (selectedPipelines.Count == 0)
                _settings.Store.KeySinglePressActions.Remove(_keyCode);
            else
                _settings.Store.KeySinglePressActions[_keyCode] = selectedPipelines;

            var selectedDoublePipelines = _listDouble.Items.OfType<PipelineListItem>()
                .Where(li => li.IsChecked)
                .Select(li => li.Pipeline.Id)
                .ToList();

            if (selectedDoublePipelines.Count == 0)
                _settings.Store.KeyDoublePressActions.Remove(_keyCode);
            else
                _settings.Store.KeyDoublePressActions[_keyCode] = selectedDoublePipelines;
        }

        _settings.SynchronizeStore();
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.Store.KeyDescriptions.Remove(_keyCode);
        _settings.Store.KeyModes.Remove(_keyCode);
        _settings.Store.KeySinglePressActions.Remove(_keyCode);
        _settings.Store.KeyDoublePressActions.Remove(_keyCode);
        _settings.SynchronizeStore();
        Close();
    }

    private static string FormatDefaultDisplayName(SpecialKey key) => SpecialKeyDisplayName.GetKey(key);

    private class PipelineListItem : UserControl
    {
        private readonly Grid _grid = new()
        {
            Margin = new(8, 4, 0, 16),
            ColumnDefinitions =
            {
                new() { Width = new(32, GridUnitType.Pixel) },
                new() { Width = new(1, GridUnitType.Star) },
            },
        };

        private readonly CheckBox _checkBox = new();

        private readonly TextBlock _nameTextBox = new()
        {
            TextAlignment = TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };

        public AutomationPipeline Pipeline { get; }

        public bool IsChecked
        {
            get => _checkBox.IsChecked ?? false;
            set => _checkBox.IsChecked = value;
        }

        public PipelineListItem(AutomationPipeline pipeline)
        {
            Pipeline = pipeline;

            _nameTextBox.Text = Pipeline.Name;

            Grid.SetColumn(_checkBox, 0);
            Grid.SetColumn(_nameTextBox, 1);

            _grid.Children.Add(_checkBox);
            _grid.Children.Add(_nameTextBox);

            AutomationProperties.SetLabeledBy(_checkBox, _nameTextBox);

            Content = _grid;
        }
    }
}
