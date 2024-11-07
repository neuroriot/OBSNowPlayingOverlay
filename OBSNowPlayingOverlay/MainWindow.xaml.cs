using OBSNowPlayingOverlay.WebSocketBehavior;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ToastNotifications;
using ToastNotifications.Lifetime;
using ToastNotifications.Messages;
using ToastNotifications.Position;
using WebSocketSharp.Server;
using Color = System.Windows.Media.Color;
using Image = SixLabors.ImageSharp.Image;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace OBSNowPlayingOverlay
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Notifier _notifier;
        private readonly HttpClient _httpClient;

        private string latestTitle = "";
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

            _httpClient = new(new HttpClientHandler()
            {
                AllowAutoRedirect = false
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

            try
            {
                wsServer = new WebSocketServer(IPAddress.Loopback, 52998);
                wsServer.AddWebSocketService<NowPlaying>("/");
                wsServer.Start();
            }
            catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
            {
                _notifier.ShowError("伺服器啟動失敗，請確認是否有其他應用程式使用 TCP 52998 Port");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                return;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                wsServer?.Stop();
                wsServer = null;
            }
            catch (Exception)
            {
                // Ignore
            }
        }

        private void grid_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        public async Task UpdateNowPlayingDataAsync(NowPlayingJson nowPlayingJson)
        {
            if (latestTitle != nowPlayingJson.Title)
            {
                latestTitle = nowPlayingJson.Title;
                AnsiConsole.MarkupLineInterpolated($"歌曲切換: [green]{nowPlayingJson.Artists.FirstOrDefault() ?? "無"} - {nowPlayingJson.Title}[/]");
                AnsiConsole.MarkupLineInterpolated($"歌曲連結: [green]{nowPlayingJson.SongLink}[/]");

                try
                {
                    AnsiConsole.MarkupLineInterpolated($"開始下載封面: [green]{nowPlayingJson.Cover}[/]");

                    using var imageStream = await _httpClient.GetStreamAsync(nowPlayingJson.Cover);
                    using var image = await Image.LoadAsync<Rgba32>(imageStream);

                    // 若圖片非正方形才進行裁切
                    if (image.Width != image.Height)
                    {
                        // 將圖片從正中間裁切
                        // 若圖片的寬比較大，就由寬來裁切
                        if (image.Width > image.Height)
                        {
                            int x = image.Width / 2 - image.Height / 2;
                            image.Mutate(i => i
                                .Crop(new Rectangle(x, 0, image.Height, image.Height)));
                        }
                        else
                        {
                            int y = image.Height / 2 - image.Width / 2;
                            image.Mutate(i => i
                                .Crop(new Rectangle(0, y, image.Width, image.Width)));
                        }
                    }

                    // 設定圖片
                    img_Cover.Dispatcher.Invoke(() =>
                    {
                        img_Cover.Source = GetBMP(image);
                    });

                    // 取得圖片的主要顏色
                    // https://gist.github.com/JimBobSquarePants/12e0ef5d904d03110febea196cf1d6ee
                    image.Mutate(x => x
                        // Scale the image down preserving the aspect ratio. This will speed up quantization.
                        // We use nearest neighbor as it will be the fastest approach.
                        .Resize(new ResizeOptions() { Sampler = KnownResamplers.NearestNeighbor, Size = new SixLabors.ImageSharp.Size(128, 0) })
                        // Reduce the color palette to 1 color without dithering.
                        .Quantize(new OctreeQuantizer(new QuantizerOptions { MaxColors = 1 })));

                    // 設定背景顏色
                    var color = image[0, 0];
                    bg.Dispatcher.Invoke(() =>
                    {
                        bg.Background = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
                    });
                }
                catch (Exception ex)
                {
                    _notifier.ShowError("封面圖下載失敗，可能是找不到圖片");
                    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                }
            }

            Color progressColor;
            switch (nowPlayingJson.Platform)
            {
                case "youtube":
                case "youtube_music":
                    progressColor = Color.FromRgb(255, 0, 51);
                    break;
                case "soundcloud":
                    progressColor = Color.FromRgb(255, 85, 0);
                    break;
                case "spotify":
                    progressColor = Color.FromRgb(30, 215, 96);
                    break;
                default:
                    progressColor = Color.FromRgb(255, 0, 51);
                    break;
            }

            rb_Title.Dispatcher.Invoke(() => { rb_Title.Content = nowPlayingJson.Title; });
            lab_Subtitle.Dispatcher.Invoke(() => { lab_Subtitle.Content = nowPlayingJson.Artists.FirstOrDefault() ?? "無"; });
            pb_Process.Dispatcher.Invoke(() =>
            {
                pb_Process.Foreground = new SolidColorBrush(progressColor);
                pb_Process.Value = (nowPlayingJson.Progress / nowPlayingJson.Duration) * 100;
            });

            grid_Pause.Dispatcher.Invoke(() =>
            {
                grid_Pause.Visibility = nowPlayingJson.Status == "playing" ? Visibility.Hidden : Visibility.Visible;
            });
        }

        // https://github.com/SixLabors/ImageSharp/issues/531#issuecomment-2275170928
        private WriteableBitmap GetBMP(Image<Rgba32> _imgState)
        {
            var bmp = new WriteableBitmap(_imgState.Width, _imgState.Height, _imgState.Metadata.HorizontalResolution, _imgState.Metadata.VerticalResolution, PixelFormats.Bgra32, null);

            bmp.Lock();
            try
            {
                _imgState.ProcessPixelRows(accessor =>
                {
                    var backBuffer = bmp.BackBuffer;

                    for (var y = 0; y < _imgState.Height; y++)
                    {
                        Span<Rgba32> pixelRow = accessor.GetRowSpan(y);

                        for (var x = 0; x < _imgState.Width; x++)
                        {
                            var backBufferPos = backBuffer + (y * _imgState.Width + x) * 4;
                            var rgba = pixelRow[x];
                            var color = rgba.A << 24 | rgba.R << 16 | rgba.G << 8 | rgba.B;

                            System.Runtime.InteropServices.Marshal.WriteInt32(backBufferPos, color);
                        }
                    }
                });

                bmp.AddDirtyRect(new Int32Rect(0, 0, _imgState.Width, _imgState.Height));
            }
            finally
            {
                bmp.Unlock();
            }

            return bmp;
        }
    }
}