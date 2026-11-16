using System.Windows;
using System.Windows.Controls;

namespace CloudLightBlizzard.Behaviors;

/// <summary>Reflows sibling columns into rows when the actual content width is limited.</summary>
public static class ResponsiveGrid
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ResponsiveGrid), new PropertyMetadata(false, Changed));
    public static bool GetIsEnabled(DependencyObject value) => (bool)value.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject value, bool enabled) => value.SetValue(IsEnabledProperty, enabled);
    public static readonly DependencyProperty BreakpointProperty=DependencyProperty.RegisterAttached("Breakpoint",typeof(double),typeof(ResponsiveGrid),new PropertyMetadata(760d));
    public static double GetBreakpoint(DependencyObject value)=>(double)value.GetValue(BreakpointProperty);
    public static void SetBreakpoint(DependencyObject value,double width)=>value.SetValue(BreakpointProperty,width);
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(LayoutState), typeof(ResponsiveGrid));

    private sealed class LayoutState
    {
        public ColumnDefinition[] Columns = [];
        public RowDefinition[] Rows = [];
        public (UIElement Element, int Row, int Column, int Span)[] Children = [];
        public bool Narrow;
    }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not Grid grid) return;
        grid.Loaded -= Loaded;
        grid.SizeChanged -= Resized;
        if ((bool)e.NewValue) { grid.Loaded += Loaded; grid.SizeChanged += Resized; }
    }
    private static void Loaded(object sender, RoutedEventArgs e) => Apply((Grid)sender);
    private static void Resized(object sender, SizeChangedEventArgs e) => Apply((Grid)sender);
    private static void Apply(Grid grid)
    {
        if (grid.ActualWidth <= 0) return;
        var state = (LayoutState?)grid.GetValue(StateProperty);
        if (state is null)
        {
            state = new LayoutState { Columns = grid.ColumnDefinitions.ToArray(), Rows = grid.RowDefinitions.ToArray(),
                Children = grid.Children.Cast<UIElement>().Select(c => (c, Grid.GetRow(c), Grid.GetColumn(c), Grid.GetColumnSpan(c))).ToArray() };
            grid.SetValue(StateProperty, state);
        }
        var narrow = grid.ActualWidth < GetBreakpoint(grid);
        if (narrow == state.Narrow) return;
        state.Narrow = narrow;
        grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
        if (narrow)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var positions = state.Children.Select(c => (c.Row, c.Column)).Distinct().OrderBy(c => c.Row).ThenBy(c => c.Column).ToList();
            foreach (var _ in positions) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            foreach (var child in state.Children) { Grid.SetColumn(child.Element, 0); Grid.SetColumnSpan(child.Element, 1); Grid.SetRow(child.Element, positions.IndexOf((child.Row, child.Column))); }
        }
        else
        {
            foreach (var c in state.Columns) grid.ColumnDefinitions.Add(c);
            foreach (var r in state.Rows) grid.RowDefinitions.Add(r);
            foreach (var child in state.Children) { Grid.SetRow(child.Element, child.Row); Grid.SetColumn(child.Element, child.Column); Grid.SetColumnSpan(child.Element, child.Span); }
        }
    }
}
