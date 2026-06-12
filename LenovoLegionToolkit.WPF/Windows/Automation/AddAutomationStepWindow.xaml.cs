using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Controls.Automation;
using LenovoLegionToolkit.WPF.Resources;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using CardControl = LenovoLegionToolkit.WPF.Controls.Custom.CardControl;

namespace LenovoLegionToolkit.WPF.Windows.Automation;

public partial class AddAutomationStepWindow
{
    private readonly List<AbstractAutomationStepControl> _controls;
    private readonly Action<AbstractAutomationStepControl> _addStepControl;
    private readonly HashSet<AbstractAutomationStepControl> _selectedSteps = [];
    private bool _multiSelect;

    public AddAutomationStepWindow(List<AbstractAutomationStepControl> controls, Action<AbstractAutomationStepControl> addStepControl, int totalAvailableStepsCount)
    {
        _controls = controls;
        _addStepControl = addStepControl;

        InitializeComponent();

        _countsTextBlock.Text = string.Format(Resource.Automation_SupportedAvailableCount, controls.Count, totalAvailableStepsCount);

        IsVisibleChanged += AddAutomationStepWindow_IsVisibleChanged;
    }

    private async void AddAutomationStepWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
            await RefreshAsync();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _multiSelect = false;
        _selectedSteps.Clear();
        await RefreshAsync();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var step in _selectedSteps)
            _addStepControl(step);

        Close();
    }

    private void _searchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _ = RefreshAsync();
    }

    private Task RefreshAsync()
    {
        _content.Children.Clear();

        if (!_multiSelect)
            _content.Children.Add(CreateMultipleSelectCardControl());

        var filter = _searchBox.Text?.Trim() ?? string.Empty;

        foreach (var control in _controls)
        {
            if (string.IsNullOrWhiteSpace(filter) ||
                control.Title.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                _content.Children.Add(CreateCardControl(control));
            }
        }

        _backButton.Visibility = _multiSelect ? Visibility.Visible : Visibility.Collapsed;
        _addButton.Visibility = _multiSelect ? Visibility.Visible : Visibility.Collapsed;
        _countsTextBlock.Visibility = _multiSelect ? Visibility.Collapsed : Visibility.Visible;
        RefreshAddButton();

        return Task.CompletedTask;
    }

    private CardControl CreateMultipleSelectCardControl()
    {
        var control = new CardControl
        {
            Icon = SymbolRegular.SquareMultiple24,
            Header = new CardHeaderControl
            {
                Title = Resource.AddAutomationStepWindow_MultipleSteps,
                Accessory = new SymbolIcon { Symbol = SymbolRegular.ChevronRight24 }
            },
            Margin = new(0, 8, 0, 0),
        };

        control.Click += async (_, _) =>
        {
            _multiSelect = true;
            await RefreshAsync();
        };

        return control;
    }

    private CardControl CreateCardControl(AbstractAutomationStepControl stepControl)
    {
        UIElement accessory;

        if (_multiSelect)
        {
            var checkbox = new CheckBox
            {
                Tag = stepControl,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsChecked = _selectedSteps.Contains(stepControl)
            };
            checkbox.Click += (_, e) =>
            {
                if (checkbox.IsChecked == true)
                    _selectedSteps.Add(stepControl);
                else
                    _selectedSteps.Remove(stepControl);

                RefreshAddButton();
                e.Handled = true;
            };
            accessory = checkbox;
        }
        else
        {
            accessory = new SymbolIcon { Symbol = SymbolRegular.ChevronRight24 };
        }

        var control = new CardControl
        {
            Icon = stepControl.Icon,
            Header = new CardHeaderControl
            {
                Title = stepControl.Title,
                Accessory = accessory
            },
            Margin = new(0, 8, 0, 0),
        };

        control.Click += (_, _) =>
        {
            if (_multiSelect)
            {
                if (accessory is not CheckBox checkbox)
                    return;

                var isChecked = !checkbox.IsChecked ?? false;
                checkbox.IsChecked = isChecked;

                if (isChecked)
                    _selectedSteps.Add(stepControl);
                else
                    _selectedSteps.Remove(stepControl);

                RefreshAddButton();
            }
            else
            {
                _addStepControl(stepControl);
                Close();
            }
        };

        return control;
    }

    private void RefreshAddButton()
    {
        if (!_multiSelect)
        {
            _addButton.IsEnabled = false;
            return;
        }

        _addButton.IsEnabled = _selectedSteps.Count > 0;
    }
}
