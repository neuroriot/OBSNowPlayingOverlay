using OBSNowPlayingOverlay.WebSocketBehavior;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using ToastNotifications;
using ToastNotifications.Lifetime;
using ToastNotifications.Messages;
using ToastNotifications.Position;
using WebSocketSharp.Server;

namespace OBSNowPlayingOverlay
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Notifier _notifier;
        private WebSocketServer? wsServer;

        public static BlockingCollection<NowPlayingJson> MsgQueue { get; } = new();

        public MainWindow()
        {
            InitializeComponent();

            _notifier = new Notifier(cfg =>
            {
                cfg.PositionProvider = new WindowPositionProvider(
                    parentWindow: Application.Current.MainWindow,
                    corner: Corner.TopRight,
                    offsetX: 10,
                    offsetY: 10);

                cfg.LifetimeSupervisor = new TimeAndCountBasedLifetimeSupervisor(
                    notificationLifetime: TimeSpan.FromSeconds(3),
                    maximumNotificationCount: MaximumNotificationCount.FromCount(2));

                cfg.Dispatcher = Application.Current.Dispatcher;
            });

            Task.Run(async () =>
            {
                while (!MsgQueue.IsCompleted)
                {
                    NowPlayingJson data;

                    try
                    {
                        data = MsgQueue.Take();
                    }
                    catch (InvalidOperationException)
                    {
                        AnsiConsole.WriteLine("Adding was completed!");
                        break;
                    }

                    await UpdateNowPlayingDataAsync(data);
                }
            });
        }

        private void btn_ToggleWSServer_Click(object sender, RoutedEventArgs e)
        {
            if (wsServer == null)
            {
                _notifier.ShowInformation("啟動 WebSocket 伺服器", new() { UnfreezeOnMouseLeave = true });

                try
                {
                    var nowPlaying = new NowPlaying();

                    wsServer = new WebSocketServer(IPAddress.Loopback, 8000);
                    wsServer.AddWebSocketService<NowPlaying>("/");
                    wsServer.Start();
                }
                catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
                {
                    _notifier.ShowError("伺服器啟動失敗，請確認是否有其他應用程式使用 TCP 8000 Port");
                    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                    return;
                }

                btn_ToggleWSServer.Dispatcher.Invoke(() => btn_ToggleWSServer.Content = "關閉 WS 伺服器");
            }
            else
            {
                _notifier.ShowInformation("停止 WebSocket 伺服器", new() { UnfreezeOnMouseLeave = true });

                wsServer.Stop();
                wsServer = null;

                btn_ToggleWSServer.Dispatcher.Invoke(() => btn_ToggleWSServer.Content = "開啟 WS 伺服器");
            }
        }

        private HttpClient _httpClient = new();
        private string latestCoverUrl = "";
        private string latestTitle = "";
        public async Task UpdateNowPlayingDataAsync(NowPlayingJson nowPlayingJson)
        {
            if (latestCoverUrl != nowPlayingJson.Cover)
            {


                latestCoverUrl = nowPlayingJson.Cover;

                var imageStream = await _httpClient.GetStreamAsync(nowPlayingJson.Cover);

                img_Cover.Dispatcher.Invoke(() => img_Cover.Source = BitmapFrame.Create(imageStream,
                                      BitmapCreateOptions.None,
                                      BitmapCacheOption.OnLoad));
            }

            if (latestTitle != nowPlayingJson.Title)
            {
                latestTitle = nowPlayingJson.Title;
                AnsiConsole.WriteLine($"歌曲切換: {nowPlayingJson.Artists.FirstOrDefault() ?? "無"} - {nowPlayingJson.Title}");
            }

            rb_Title.Dispatcher.Invoke(() => { rb_Title.Content = nowPlayingJson.Title; });
            lab_Subtitle.Dispatcher.Invoke(() => { lab_Subtitle.Content = nowPlayingJson.Artists.FirstOrDefault() ?? "無"; });
            pb_Process.Dispatcher.Invoke(() => { pb_Process.Value = (nowPlayingJson.Progress / nowPlayingJson.Duration) * 100; });
        }
    }
}