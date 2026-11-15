using System.Globalization;
using System.Windows.Data;

namespace CloudLightBlizzard.Controls;
public sealed class NavigationIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
    {
        "overview" => "\uE80F", "accounts" => "\uE77B", "region" => "\uE774", "stats" => "\uE9D9",
        "drops" => "\uE719", "snapshots" => "\uE8B7", "diagnostics" => "\uE9CE", "settings" => "\uE713", _ => "\uE946",
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
