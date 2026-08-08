using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LenovoLegionToolkit.WPF.Behaviors
{
    public static class DragDropBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(DragDropBehavior), new UIPropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty DragHandleNameProperty =
            DependencyProperty.RegisterAttached("DragHandleName", typeof(string), typeof(DragDropBehavior), new UIPropertyMetadata(null));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        public static string GetDragHandleName(DependencyObject obj) => (string)obj.GetValue(DragHandleNameProperty);
        public static void SetDragHandleName(DependencyObject obj, string value) => obj.SetValue(DragHandleNameProperty, value);

        private static Point _startPoint;
        private static FrameworkElement? _draggedElement;
        private static Panel? _sourcePanel;

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Panel panel)
                return;

            if ((bool)e.NewValue)
            {
                panel.AllowDrop = true;
                panel.AddHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseLeftButtonDown), true);
                panel.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnMouseLeftButtonUp), true);
                panel.MouseMove += OnMouseMove;
                panel.DragOver += OnDragOver;
                panel.Drop += OnDrop;
            }
            else
            {
                panel.AllowDrop = false;
                panel.RemoveHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseLeftButtonDown));
                panel.RemoveHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnMouseLeftButtonUp));
                panel.MouseMove -= OnMouseMove;
                panel.DragOver -= OnDragOver;
                panel.Drop -= OnDrop;
            }
        }

        private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Panel panel)
                return;

            _startPoint = e.GetPosition(panel);
            _sourcePanel = panel;

            var dragHandleName = GetDragHandleName(panel);
            if (!string.IsNullOrEmpty(dragHandleName))
            {
                if (e.OriginalSource is DependencyObject depObj)
                {
                    var current = depObj;
                    bool foundHandle = false;
                    FrameworkElement? directChild = null;

                    while (current != null && current != panel)
                    {
                        if (current is FrameworkElement fe)
                        {
                            if (fe != panel && GetIsEnabled(fe))
                                return;

                            if (panel.Children.Contains(fe))
                            {
                                directChild = fe;
                            }
                            if (fe.Name == dragHandleName)
                            {
                                foundHandle = true;
                            }
                        }
                        current = VisualTreeHelper.GetParent(current);
                    }

                    if (foundHandle && directChild != null)
                    {
                        _draggedElement = directChild;
                        panel.CaptureMouse();
                        e.Handled = true;
                    }
                }
            }
            else
            {
                if (e.OriginalSource is FrameworkElement element && panel.Children.Contains(element))
                {
                    _draggedElement = element;
                    panel.CaptureMouse();
                    e.Handled = true;
                }
                else if (e.OriginalSource is Visual visual)
                {
                    var parent = FindParentInPanel(panel, visual);
                    if (parent != null)
                    {
                        _draggedElement = parent;
                        panel.CaptureMouse();
                        e.Handled = true;
                    }
                }
            }
        }

        private static void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Panel panel)
            {
                panel.ReleaseMouseCapture();
            }
            _draggedElement = null;
            _sourcePanel = null;
        }

        private static void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedElement != null)
            {
                var currentPoint = e.GetPosition(_sourcePanel);

                if (Math.Abs(currentPoint.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(currentPoint.Y - _startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var elementToDrag = _draggedElement;
                    if (elementToDrag == null)
                        return;

                    _draggedElement = null;
                    _sourcePanel = null;

                    if (sender is Panel panel)
                    {
                        panel.ReleaseMouseCapture();
                    }

                    var data = new DataObject(typeof(FrameworkElement), elementToDrag);
                    DragDrop.DoDragDrop(elementToDrag, data, DragDropEffects.Move);
                }
            }
        }

        private static void OnDragOver(object sender, DragEventArgs e)
        {
            if (sender is not Panel panel || !e.Data.GetDataPresent(typeof(FrameworkElement)))
                return;

            var draggedElement = e.Data.GetData(typeof(FrameworkElement)) as FrameworkElement;
            if (draggedElement == null || !panel.Children.Contains(draggedElement))
                return;

            e.Effects = DragDropEffects.Move;
            e.Handled = true;

            var dropPosition = e.GetPosition(panel);
            int newIndex = -1;
            double minDistance = double.MaxValue;

            int currentIndex = panel.Children.IndexOf(draggedElement);
            if (currentIndex < 0)
                return;

            for (int i = 0; i < panel.Children.Count; i++)
            {
                var child = panel.Children[i] as FrameworkElement;
                if (child == null || child == draggedElement) continue;

                var childCenter = child.TransformToAncestor(panel).Transform(new Point(child.ActualWidth / 2, child.ActualHeight / 2));
                
                double dx = dropPosition.X - childCenter.X;
                double dy = dropPosition.Y - childCenter.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    int targetIndex = i;

                    bool insertAfter = false;
                    if (panel is StackPanel { Orientation: Orientation.Horizontal } || panel is WrapPanel)
                    {
                        double threshold = currentIndex < targetIndex
                            ? childCenter.X - child.ActualWidth / 2
                            : childCenter.X + child.ActualWidth / 2;
                        insertAfter = dropPosition.X >= threshold;
                    }
                    else
                    {
                        double threshold = currentIndex < targetIndex
                            ? childCenter.Y - child.ActualHeight / 2
                            : childCenter.Y + child.ActualHeight / 2;
                        insertAfter = dropPosition.Y >= threshold;
                    }

                    newIndex = insertAfter ? targetIndex + 1 : targetIndex;

                    if (newIndex > currentIndex)
                    {
                        newIndex--;
                    }
                }
            }

            if (newIndex >= 0 && newIndex != currentIndex)
            {
                panel.Children.Remove(draggedElement);
                panel.Children.Insert(newIndex, draggedElement);
            }
        }

        private static void OnDrop(object sender, DragEventArgs e)
        {
            if (sender is not Panel panel || !e.Data.GetDataPresent(typeof(FrameworkElement)))
                return;

            var droppedElement = e.Data.GetData(typeof(FrameworkElement)) as FrameworkElement;
            if (droppedElement == null || !panel.Children.Contains(droppedElement))
                return;

            var dropPosition = e.GetPosition(panel);
            int newIndex = -1;
            double minDistance = double.MaxValue;

            int currentIndex = panel.Children.IndexOf(droppedElement);
            if (currentIndex < 0)
                return;

            for (int i = 0; i < panel.Children.Count; i++)
            {
                var child = panel.Children[i] as FrameworkElement;
                if (child == null || child == droppedElement) continue;

                var childCenter = child.TransformToAncestor(panel).Transform(new Point(child.ActualWidth / 2, child.ActualHeight / 2));
                
                double dx = dropPosition.X - childCenter.X;
                double dy = dropPosition.Y - childCenter.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                if (distance < minDistance)
                {
                    minDistance = distance;
                    int targetIndex = i;

                    bool insertAfter = false;
                    if (panel is StackPanel { Orientation: Orientation.Horizontal } || panel is WrapPanel)
                    {
                        double threshold = currentIndex < targetIndex
                            ? childCenter.X - child.ActualWidth / 2
                            : childCenter.X + child.ActualWidth / 2;
                        insertAfter = dropPosition.X >= threshold;
                    }
                    else
                    {
                        double threshold = currentIndex < targetIndex
                            ? childCenter.Y - child.ActualHeight / 2
                            : childCenter.Y + child.ActualHeight / 2;
                        insertAfter = dropPosition.Y >= threshold;
                    }

                    newIndex = insertAfter ? targetIndex + 1 : targetIndex;

                    if (newIndex > currentIndex)
                    {
                        newIndex--;
                    }
                }
            }

            if (newIndex >= 0 && newIndex != currentIndex)
            {
                panel.Children.Remove(droppedElement);
                panel.Children.Insert(newIndex, droppedElement);
            }
        }

        private static FrameworkElement? FindParentInPanel(Panel panel, Visual child)
        {
            DependencyObject? parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is FrameworkElement frameworkElement)
                {
                    if (frameworkElement != panel && GetIsEnabled(frameworkElement))
                        return null;

                    if (panel.Children.Contains(frameworkElement))
                    {
                        return frameworkElement;
                    }
                }
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
