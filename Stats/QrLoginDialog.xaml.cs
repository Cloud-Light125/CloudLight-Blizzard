using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BnetSwitch.Services.Overwatch;

namespace BnetSwitch.Stats;

public partial class QrLoginDialog : Window
{
    public enum QrState { Waiting, Success, Expired }

    private readonly DispatcherTimer _pollTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _expireTimer = new() { Interval = TimeSpan.FromSeconds(120) };
    private DashenClient? _client;
    private bool _busy;

    /// <summary>授权成功后触发(拿到登录态、已落盘后)。</summary>
    public event EventHandler? Authorized;

    public QrLoginDialog()
    {
        InitializeComponent();
        Loaded += async (_, _) => await GenerateQrAsync();
        _pollTimer.Tick += async (_, _) => await PollScanStateAsync();
        _expireTimer.Tick += (_, _) => SetState(QrState.Expired);
    }

    private async Task GenerateQrAsync()
    {
        SetState(QrState.Waiting);
        QrImage.Source = null;
        QrPlaceholder.Visibility = Visibility.Visible;
        try
        {
            _client = new DashenClient();
            var qr = await _client.CreateLoginQrAsync();
            QrImage.Source = ToBitmap(qr.ImageBytes);
            QrPlaceholder.Visibility = Visibility.Collapsed;
            _pollTimer.Start();
            _expireTimer.Start();
        }
        catch
        {
            HintText.Text = "二维码生成失败,请检查网络后点刷新";
            SetState(QrState.Expired);
        }
    }

    private async Task PollScanStateAsync()
    {
        if (_busy || _client is null) return;
        _busy = true;
        try
        {
            var pr = await _client.PollOnceAsync();
            if (pr.State == DashenClient.ScanState.Confirmed)
            {
                SetState(QrState.Success);
                await _client.CompleteLoginAsync();
                DashenAuth.Save(_client);            // 登录态落盘(仅本机)
                Authorized?.Invoke(this, EventArgs.Empty);
                await Task.Delay(500);
                DialogResult = true;
                Close();
            }
            else if (pr.State == DashenClient.ScanState.Expired)
            {
                SetState(QrState.Expired);
            }
        }
        catch { /* 轮询偶发错误,下次再试 */ }
        finally { _busy = false; }
    }

    private static BitmapImage ToBitmap(byte[] bytes)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private void SetState(QrState s)
    {
        SuccessOverlay.Visibility = s == QrState.Success ? Visibility.Visible : Visibility.Collapsed;
        ExpiredOverlay.Visibility = s == QrState.Expired ? Visibility.Visible : Visibility.Collapsed;

        switch (s)
        {
            case QrState.Waiting:
                StatusText.Text = "等待扫码…";
                HintText.Text = "打开「网易大神」App，首页右上角 扫一扫";
                break;
            case QrState.Success:
                _pollTimer.Stop(); _expireTimer.Stop();
                StatusText.Text = "扫码成功，正在获取数据…";
                HintText.Text = "拉取账号资料与赛季战绩，约需 2-5 秒";
                break;
            case QrState.Expired:
                _pollTimer.Stop(); _expireTimer.Stop();
                StatusText.Text = "二维码已失效";
                HintText.Text = "120 秒未扫码自动过期，刷新后重新生成";
                break;
        }
    }

    private async void OnRefreshQr(object sender, RoutedEventArgs e) => await GenerateQrAsync();

    protected override void OnClosed(EventArgs e)
    {
        _pollTimer.Stop(); _expireTimer.Stop();
        base.OnClosed(e);
    }
}
