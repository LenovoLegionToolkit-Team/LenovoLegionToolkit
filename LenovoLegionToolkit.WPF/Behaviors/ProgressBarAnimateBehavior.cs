using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Animation;
using Microsoft.Xaml.Behaviors;

namespace LenovoLegionToolkit.WPF.Behaviors;

public class ProgressBarAnimateBehavior : Behavior<ProgressBar>
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(ProgressBarAnimateBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ProgressBar progressBar)
            return;

        var behaviors = Interaction.GetBehaviors(progressBar);
        var existing = behaviors.OfType<ProgressBarAnimateBehavior>().FirstOrDefault();

        if ((bool)e.NewValue)
        {
            if (existing == null)
            {
                behaviors.Add(new ProgressBarAnimateBehavior());
            }
        }
        else
        {
            if (existing != null)
            {
                behaviors.Remove(existing);
            }
        }
    }

    private bool _isAnimating;

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.ValueChanged += ProgressBar_ValueChanged;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        AssociatedObject.ValueChanged -= ProgressBar_ValueChanged;
    }

    private void ProgressBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is not ProgressBar progressBar)
            return;

        if (_isAnimating)
            return;

        _isAnimating = true;

        var doubleAnimation = new DoubleAnimation(e.OldValue,
            e.NewValue,
            new Duration(TimeSpan.FromMilliseconds(250)),
            FillBehavior.Stop);
        doubleAnimation.Completed += Completed;

        progressBar.BeginAnimation(RangeBase.ValueProperty, doubleAnimation);

        e.Handled = true;
    }

    private void Completed(object? sender, EventArgs e) => _isAnimating = false;
}
