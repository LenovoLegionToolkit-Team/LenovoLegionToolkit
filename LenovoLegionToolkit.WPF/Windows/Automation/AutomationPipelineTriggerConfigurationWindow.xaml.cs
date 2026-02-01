using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.WPF.Controls;
using LenovoLegionToolkit.WPF.Extensions;
using LenovoLegionToolkit.WPF.Utils;
using LenovoLegionToolkit.WPF.Windows.Automation.TabItemContent;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using CardControl = LenovoLegionToolkit.WPF.Controls.Custom.CardControl;
using CardExpander = LenovoLegionToolkit.WPF.Controls.Custom.CardExpander;

namespace LenovoLegionToolkit.WPF.Windows.Automation;

public partial class AutomationPipelineTriggerConfigurationWindow
{
    private void AutomationPipelineTriggerConfigurationWindow_Initialized(object? sender, EventArgs e)
    {
        foreach (var trigger in _triggers)
        {
            var content = Create(trigger);
            if (content is not null)
            {
                var card = CreateTriggerCard(trigger, content as UIElement);
                _triggersStackPanel.Children.Add(card);
            }
            else
            {
                var hiddenCard = new CardControl { Visibility = Visibility.Collapsed, Tag = trigger };
                _triggersStackPanel.Children.Add(hiddenCard);
            }
        }

        if (_triggers.Count() > 1)
        {
            _logicSelection.Visibility = Visibility.Visible;
            _logicComboBox.SelectedIndex = _isOrLogic ? 1 : 0;
        }
    }

    private FrameworkElement CreateTriggerCard(IAutomationPipelineTrigger trigger, UIElement? content)
    {
        var dragHandle = new SymbolIcon
        {
            Symbol = SymbolRegular.GridDots24,
            Margin = new(-8, 0, 8, 0),
            Cursor = Cursors.SizeAll,
            Opacity = 0.5
        };

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        headerPanel.Children.Add(dragHandle);
        headerPanel.Children.Add(new SymbolIcon { Symbol = trigger.Icon(), Margin = new(8, 0, 0, 0) });
        headerPanel.Children.Add(new TextBlock { Text = trigger.DisplayName, Margin = new(4, 0, 8, 0) });

        var expander = new CardExpander
        {
            Header = new CardHeaderControl 
            { 
                 Title = trigger.DisplayName, 
            },
            IsExpanded = true,
            Content = content
        };

        expander.Header = headerPanel;
        expander.Tag = trigger; // Store trigger for Save
        expander.Margin = new Thickness(0, 0, 0, 8);

        dragHandle.MouseLeftButtonDown += (s, e) =>
        {
            if (e.ClickCount > 1) return;
            DragDrop.DoDragDrop(expander, new DataObject("TriggerCard", expander), DragDropEffects.Move);
        };

        expander.AllowDrop = true;
        expander.PreviewDragOver += Item_PreviewDragOver;
        expander.Drop += Item_Drop;
        expander.GiveFeedback += Item_GiveFeedback;

        return expander;
    }

    private DragAdorner? _adorner;

    private void Item_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent("TriggerCard"))
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;

             var position = e.GetPosition(this);
            if (_adorner == null)
            {
                var source = e.Data.GetData("TriggerCard") as UIElement;
                if (source != null)
                {
                    var adornerLayer = AdornerLayer.GetAdornerLayer(this);
                    if (adornerLayer != null)
                    {
                        var offset = new Point(10, 10); 
                        _adorner = new DragAdorner(this, source, offset);
                        adornerLayer.Add(_adorner);
                    }
                }
            }
            _adorner?.UpdatePosition(position);
        }
    }

    private void Item_Drop(object sender, DragEventArgs e)
    {
        CleanupAdorner();
        if (sender is not FrameworkElement targetControl || !e.Data.GetDataPresent("TriggerCard")) return;

        var sourceControl = e.Data.GetData("TriggerCard") as FrameworkElement;
        if (sourceControl == null || sourceControl == targetControl) return;

        int oldIndex = _triggersStackPanel.Children.IndexOf(sourceControl);
        int newIndex = _triggersStackPanel.Children.IndexOf(targetControl);

        if (oldIndex != -1 && newIndex != -1)
        {
            _triggersStackPanel.Children.RemoveAt(oldIndex);
            _triggersStackPanel.Children.Insert(newIndex, sourceControl);
        }
    }

    private void Item_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (e.Effects.HasFlag(DragDropEffects.Move))
        {
            Mouse.SetCursor(Cursors.SizeAll);
            e.UseDefaultCursors = false;
            e.Handled = true;
        }
        else
        {
            e.UseDefaultCursors = true;
            e.Handled = true;
        }
    }

     private void CleanupAdorner()
    {
         if (_adorner != null)
         {
             var adornerLayer = AdornerLayer.GetAdornerLayer(this);
             adornerLayer?.Remove(_adorner);
             _adorner = null;
         }
    }


    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var triggers = _triggersStackPanel.Children
            .OfType<FrameworkElement>()
            .Select(c =>
            {
                if (c is CardExpander expander && expander.Content is IAutomationPipelineTriggerTabItemContent<IAutomationPipelineTrigger> content)
                    return content.GetTrigger();

                if (c.Tag is IAutomationPipelineTrigger trigger)
                    return trigger;

                return null;
            })
            .OfType<IAutomationPipelineTrigger>()
            .ToArray();

        IAutomationPipelineTrigger? result;

        if (triggers.Length > 1)
        {
            result = _logicComboBox.SelectedIndex == 1
                ? new OrAutomationPipelineTrigger(triggers)
                : new AndAutomationPipelineTrigger(triggers);
        }
        else
        {
            result = triggers.FirstOrDefault();
        }

        if (result is not null)
            OnSave?.Invoke(this, result);

        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private readonly IEnumerable<IAutomationPipelineTrigger> _triggers;
    private readonly bool _isOrLogic;

    public event EventHandler<IAutomationPipelineTrigger>? OnSave;

    public AutomationPipelineTriggerConfigurationWindow(IEnumerable<IAutomationPipelineTrigger> triggers, bool isOrLogic = false)
    {
        _triggers = triggers;
        _isOrLogic = isOrLogic;

        InitializeComponent();
    }

    public static bool IsValid(IEnumerable<IAutomationPipelineTrigger> triggers) => triggers.Any(IsValid);

    private static bool IsValid(IAutomationPipelineTrigger trigger) => trigger switch
    {
        IDeviceAutomationPipelineTrigger => true,
        IGodModePresetChangedAutomationPipelineTrigger => true,
        IPeriodicAutomationPipelineTrigger t1 when t1.Period > TimeSpan.Zero => true,
        IProcessesAutomationPipelineTrigger => true,
        IPowerModeAutomationPipelineTrigger => true,
        IITSModeAutomationPipelineTrigger => true,
        ITimeAutomationPipelineTrigger => true,
        IUserInactivityPipelineTrigger t2 when t2.InactivityTimeSpan > TimeSpan.Zero => true,
        IWiFiConnectedPipelineTrigger => true,
        _ => false
    };

    private static IAutomationPipelineTriggerTabItemContent<IAutomationPipelineTrigger>? Create(IAutomationPipelineTrigger trigger) => trigger switch
    {
        IDeviceAutomationPipelineTrigger dt => new DeviceAutomationPipelineTriggerTabItemContent(dt),
        IGodModePresetChangedAutomationPipelineTrigger gpt => new GodModePresetPipelineTriggerTabItemContent(gpt),
        IPeriodicAutomationPipelineTrigger pet => new PeriodicAutomationPipelineTriggerTabItemContent(pet),
        IProcessesAutomationPipelineTrigger pt => new ProcessAutomationPipelineTriggerTabItemControl(pt),
        IPowerModeAutomationPipelineTrigger pmt => new PowerModeAutomationPipelineTriggerTabItemContent(pmt),
        IITSModeAutomationPipelineTrigger pmt => new ITSModeAutomationPipelineTriggerTabItemContent(pmt),
        ITimeAutomationPipelineTrigger tt => new TimeAutomationPipelineTriggerTabItemContent(tt),
        IUserInactivityPipelineTrigger ut when ut.InactivityTimeSpan > TimeSpan.Zero => new UserInactivityPipelineTriggerTabItemContent(ut),
        IWiFiConnectedPipelineTrigger wt => new WiFiConnectedPipelineTriggerTabItemContent(wt),
        _ => null
    };
}
