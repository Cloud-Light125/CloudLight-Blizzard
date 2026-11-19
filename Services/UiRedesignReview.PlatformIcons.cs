using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CloudLightBlizzard.Controls;
using ShapePath = System.Windows.Shapes.Path;

namespace CloudLightBlizzard.Services;

internal sealed partial class UiRedesignReview
{
    private async Task RunPlatformIconsReviewAsync()
    {
        var output = Path.Combine(_output, "screenshots");
        Directory.CreateDirectory(output);
        var results = new List<string>();
        try
        {
            var tabsBar = (Grid)_page.FindName("PlatformTabsBar");
            var tabs = Descendants<RadioButton>(tabsBar).ToArray();
            _main.CurrentAccount = _main.SavedAccounts.First();
            _main.GameRegionTitle = "当前文件：无法确认";
            var hover = tabs[0].Template.Triggers.OfType<MultiTrigger>().Single(
                trigger => trigger.Conditions.Any(condition => condition.Property == UIElement.IsMouseOverProperty));
            if (hover.Setters.Cast<Setter>().Any(setter => setter.Property != Border.BackgroundProperty))
                throw new InvalidOperationException("Hover must only change the tab background.");

            var dpi = VisualTreeHelper.GetDpi(_window);
            foreach (var (width, height) in new[] { (1366, 768), (1600, 900), (1920, 1080) })
            foreach (var dark in new[] { false, true })
            {
                var context = $"{width}x{height}-{(dark ? "dark" : "light")}";
                ThemeManager.Apply(dark);
                _window.Width = width / dpi.DpiScaleX;
                _window.Height = height / dpi.DpiScaleY;
                foreach (var (scenario, page, platform, count) in new[]
                {
                    (0, "overview", "BattleNet", 1), (1, "accounts", "BattleNet", 4),
                    (2, "regions", "Overwatch", 1), (3, "stats", "Overwatch", 1),
                    (4, "snapshots", "Overwatch", 1), (22, "drops-bilibili", "Bilibili", 3)
                })
                {
                    _scenarios.SelectedIndex = scenario;
                    await _window.Dispatcher.InvokeAsync(() => _window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                    var icons = Descendants<PlatformIcon>(_window).Where(icon => icon.IsVisible).ToArray();
                    RequireIconCount(icons, platform, count, page);
                    if (scenario == 0)
                    {
                        RequireIconCount(icons, "Overwatch", 1, page);
                        RequireIconCount(icons, "Bilibili", 1, page);
                    }
                    foreach (var icon in icons.Where(icon => icon.Platform is "BattleNet" or "Overwatch" or "Bilibili"))
                        CheckBrandInk(icon);
                    if (scenario == 22)
                    {
                        foreach (var tab in tabs)
                        {
                            tab.IsChecked = true;
                            tabsBar.UpdateLayout();
                            foreach (var peer in tabs)
                                CheckPlatformTab(peer);

                            tab.IsEnabled = false;
                            tabsBar.UpdateLayout();
                            CheckPlatformTab(tab);
                            tab.IsEnabled = true;
                        }
                        _page.SelectPlatform(Drops.DropsPlatform.Bilibili);
                        await _window.Dispatcher.InvokeAsync(() => _window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
                    }
                    Capture(Path.Combine(output, $"{page}-{context}.png"));
                }

                results.Add($"PASS {context} all six pages, each tab selected/unselected and disabled; DPI={dpi.PixelsPerInchX:G}");
            }
            results.Add("PASS hover template changes background only");
            results.Add($"binding-errors={_bindings.Errors.Count}");
            results.AddRange(_bindings.Errors);
            Environment.ExitCode = _bindings.Errors.Count == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            results.Add($"FAIL {error}");
            Environment.ExitCode = 1;
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_output, "platform-icons-results.txt"), results);
            _window.Close();
        }
    }

    private static void RequireIconCount(PlatformIcon[] icons, string platform, int count, string page)
    {
        var actual = icons.Count(icon => icon.Platform == platform);
        if (actual != count)
            throw new InvalidOperationException($"{page}: expected {count} {platform} icons, found {actual}.");
    }

    private static void CheckBrandInk(PlatformIcon icon)
    {
        var paths = Descendants<ShapePath>(icon).ToArray();
        var expected = icon.Platform == "Overwatch" ? 2 : 1;
        if (paths.Length != expected)
            throw new InvalidOperationException($"{icon.Platform}: incomplete mark.");

        var ink = Rect.Empty;
        foreach (var path in paths)
        {
            var bounds = path.RenderedGeometry.Bounds;
            if (path.Fill is null || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
                throw new InvalidOperationException($"{icon.Platform}: missing vector artwork.");
            ink.Union(path.TransformToAncestor(icon).TransformBounds(bounds));
        }

        var horizontalInset = Math.Min(ink.Left, icon.ActualWidth - ink.Right);
        var verticalInset = Math.Min(ink.Top, icon.ActualHeight - ink.Bottom);
        if (horizontalInset < 2 || verticalInset < 2 ||
            Math.Abs(ink.Left + ink.Right - icon.ActualWidth) > 2 ||
            Math.Abs(ink.Top + ink.Bottom - icon.ActualHeight) > 2)
            throw new InvalidOperationException($"{icon.Platform}: artwork is off-center or exceeds its inset ({ink}).");
    }

    private static void CheckPlatformTab(RadioButton tab)
    {
        var frame = (Border)tab.Template.FindName("Frame", tab);
        var content = (StackPanel)tab.Content;
        var label = content.Children.OfType<TextBlock>().Single();
        var bounds = content.TransformToAncestor(frame).TransformBounds(new Rect(content.RenderSize));
        if (label.TextTrimming != TextTrimming.None || label.ActualWidth < label.DesiredSize.Width - 0.5 ||
            bounds.Width < content.DesiredSize.Width - 0.5 || bounds.Height < content.DesiredSize.Height - 0.5 ||
            bounds.Left < tab.Padding.Left - 0.5 || bounds.Top < tab.Padding.Top - 0.5 ||
            bounds.Right > frame.ActualWidth - tab.Padding.Right + 0.5 ||
            bounds.Bottom > frame.ActualHeight - tab.Padding.Bottom + 0.5 ||
            Math.Abs(bounds.Top + bounds.Bottom - frame.ActualHeight) > 1 ||
            VisualTreeHelper.GetClip(content) is not null || VisualTreeHelper.GetClip(frame) is not null)
            throw new InvalidOperationException($"{tab.Tag}: platform tab content is clipped or off-center.");
    }
}
