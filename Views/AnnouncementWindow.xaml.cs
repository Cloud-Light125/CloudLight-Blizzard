using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using CloudLightBlizzard.Models;
using CloudLightBlizzard.Services;

namespace CloudLightBlizzard.Views;

public partial class AnnouncementWindow : Window
{
    private readonly AnnouncementService _service;
    private readonly ObservableCollection<AnnouncementRow> _items;

    public AnnouncementWindow(IEnumerable<Announcement> announcements, AnnouncementService service)
    {
        _service = service;
        _items = new ObservableCollection<AnnouncementRow>(announcements.Select(item =>
            new AnnouncementRow(item, service.IsUnread(item))));
        InitializeComponent();
        ThemeManager.Attach(this);
        AnnouncementList.ItemsSource = _items;
        EmptyHint.Text = service.LastFailureMessage ?? "暂无可用公告";
        EmptyHint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAnnouncementSelected(object sender, SelectionChangedEventArgs e)
    {
        if (AnnouncementList.SelectedItem is not AnnouncementRow row) return;
        DetailHint.Visibility = Visibility.Collapsed;
        DetailTitle.Visibility = DetailTime.Visibility = DetailContent.Visibility = Visibility.Visible;
        DetailTitle.Text = row.Announcement.Title;
        DetailTime.Text = row.PublishedText;
        DetailContent.Text = row.Announcement.Content;
        _service.MarkRead(row.Announcement);
        row.IsUnread = false;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

public sealed class AnnouncementRow : INotifyPropertyChanged
{
    private bool _isUnread;
    public Announcement Announcement { get; }
    public string PublishedText => Announcement.PublishedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public Visibility UnreadVisibility => _isUnread ? Visibility.Visible : Visibility.Hidden;
    public bool IsUnread
    {
        get => _isUnread;
        set { if (_isUnread == value) return; _isUnread = value; OnPropertyChanged(); OnPropertyChanged(nameof(UnreadVisibility)); }
    }

    public AnnouncementRow(Announcement announcement, bool isUnread) { Announcement = announcement; _isUnread = isUnread; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
