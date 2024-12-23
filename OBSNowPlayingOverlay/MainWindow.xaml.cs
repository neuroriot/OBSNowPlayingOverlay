using ImageMagick;
using OBSNowPlayingOverlay.TwitchBot;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using Spectre.Console;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Image = SixLabors.ImageSharp.Image;
using ImageBrush = System.Windows.Media.ImageBrush;
using Rectangle = SixLabors.ImageSharp.Rectangle;

namespace OBSNowPlayingOverlay
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static BlockingCollection<NowPlayingJson> MsgQueue { get; } = new();
        public static Bot TwitchBot { get; private set; } = new();
        public static string LatestWebSocketGuid { get; set; } = "";
        public static string NowPlayingTitle { get; private set; } = "";
        public static string NowPlayingUrl { get; private set; } = "";

        private readonly HttpClient _httpClient;
        private string latestTitle = "";
        private bool isUseCoverImageAsBackground = false;

        public MainWindow()
        {
            InitializeComponent();

            _httpClient = new(new HttpClientHandler()
            {
                AllowAutoRedirect = false
            });

            Task.Run(async () =>
            {
                try
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
                            break;
                        }

                        await UpdateNowPlayingDataAsync(data);
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.WriteException(ex);
                }
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            MsgQueue.CompleteAdding();
        }

        private void grid_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        public async Task UpdateNowPlayingDataAsync(NowPlayingJson nowPlayingJson)
        {
            // If LatestWebSocketGuid is empty, it means that one of the senders has been closed. Update the status with the latest received sender.
            if (LatestWebSocketGuid == "")
            {
                AnsiConsole.MarkupLineInterpolated($"The last Guid is empty, change this Guid to continue updating the status: [purple4_1]{nowPlayingJson.Guid}[/]");
                LatestWebSocketGuid = nowPlayingJson.Guid;
            }

            // Check whether the Guid of the sending end is the latest to avoid repeatedly updating the playback status
            if (LatestWebSocketGuid != nowPlayingJson.Guid)
                return;

            // Check if the title has changed
            if (latestTitle != nowPlayingJson.Title)
            {
                latestTitle = nowPlayingJson.Title;
                NowPlayingTitle = nowPlayingJson.Title;
                NowPlayingUrl = nowPlayingJson.SongLink;
                var artists = nowPlayingJson.Artists != null ? string.Join(", ", nowPlayingJson.Artists) : "Without";
                double grayLevel = 0d;

                AnsiConsole.MarkupLineInterpolated($"Song switching: [green]{artists} - {nowPlayingJson.Title}[/]");
                AnsiConsole.MarkupLineInterpolated($"Song link: [green]{nowPlayingJson.SongLink}[/]");

                rb_Title.Dispatcher.Invoke(() => { rb_Title.Content = nowPlayingJson.Title; });
                rb_Subtitle.Dispatcher.Invoke(() => { rb_Subtitle.Content = artists; });

                try
                {
                    AnsiConsole.MarkupLineInterpolated($"Download cover: [green]{nowPlayingJson.Cover}[/]");

                    // Since YouTube started converting images to avif, we first used MagickImage to read the images and convert them to jpegs.
                    // https://github.com/dlemstra/Magick.NET/blob/main/docs/ConvertImage.md
                    using var magickImage = new MagickImage(await _httpClient.GetStreamAsync(nowPlayingJson.Cover));
                    magickImage.Format = MagickFormat.Jpeg;

                    using var imageStream = new MemoryStream(magickImage.ToByteArray());
                    using var image = await Image.LoadAsync<Rgba32>(imageStream);
                    using var coverImage = image.Clone();

                    // Only crop the image if it is not square
                    if (coverImage.Width != coverImage.Height)
                    {
                        // Crop the image from the center
                        // If the width of the picture is relatively large, it will be cropped by the width.
                        if (coverImage.Width > coverImage.Height)
                        {
                            int x = coverImage.Width / 2 - coverImage.Height / 2;
                            coverImage.Mutate(i => i
                                .Crop(new Rectangle(x, 0, coverImage.Height, coverImage.Height)));
                        }
                        else
                        {
                            int y = coverImage.Height / 2 - coverImage.Width / 2;
                            coverImage.Mutate(i => i
                                .Crop(new Rectangle(0, y, coverImage.Width, coverImage.Width)));
                        }
                    }

                    // Set cover image
                    img_Cover.Dispatcher.Invoke(() =>
                    {
                        img_Cover.Source = GetBMP(coverImage);
                    });

                    if (isUseCoverImageAsBackground)
                    {
                        using var bgImage = coverImage.Clone();
                        bgImage.Mutate(x => x.Resize(188, 188/2));
                        // Blur the picture
                        bgImage.Mutate(x => x.GaussianBlur(3));

                        bg.Dispatcher.Invoke(() =>
                        {
                            bg.Background = new ImageBrush()
                            {
                                ImageSource = GetBMP(bgImage),
                                TileMode = TileMode.Tile,
                                Stretch = Stretch.None
                            };
                        });

                        // Scale the image to improve calculation efficiency and grayscale it (from usually 336x188)
                        bgImage.Mutate(x => x.Resize(128, 72).Grayscale());
                        //bgImage.Mutate(x => x.Resize(336, 188).Grayscale());
                        //image.Mutate(x => x.Grayscale());

                        // Calculate the average brightness of the scaled image
                        double totalBrightness = 0;
                        int width = bgImage.Width;
                        int height = bgImage.Height;

                        for (int y = 0; y < height; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                // Use indexer to get pixels
                                Rgba32 pixel = bgImage[x, y];
                                double brightness = (pixel.R + pixel.G + pixel.B) / 3.0;
                                totalBrightness += brightness;
                            }
                        }

                        grayLevel = Math.Round((totalBrightness / (width * height)), 2);
                    }
                    else
                    {
                        // Get the main color of the image
                        // https://gist.github.com/JimBobSquarePants/12e0ef5d904d03110febea196cf1d6ee
                        image.Mutate(x => x
                            // Scale the image down preserving the aspect ratio. This will speed up quantization.
                            // We use nearest neighbor as it will be the fastest approach.
                            .Resize(new ResizeOptions() { Sampler = KnownResamplers.NearestNeighbor, Size = new SixLabors.ImageSharp.Size(128, 0) })
                            // Reduce the color palette to 1 color without dithering.
                            .Quantize(new OctreeQuantizer(new QuantizerOptions { MaxColors = 1 })));

                        // Set background color
                        var color = image[0, 0];
                        bg.Dispatcher.Invoke(() =>
                        {
                            bg.Background = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
                        });

                        grayLevel = Math.Round((color.R + color.G + color.B) / 3d, 2);
                    }

                    AnsiConsole.MarkupLineInterpolated($"Cover grayscale: [green]{grayLevel}[/]");

                    // Get whether the image is a bright color
                    var isLightBackground = grayLevel >= 128;

                    // SolidColorBrush cannot be created on a different thread than DispatcherObject, so it can only be written in this ugly way
                    // https://stackoverflow.com/a/8010725/15800522
                    rb_Title.Dispatcher.Invoke(() => { rb_Title.Foreground = new SolidColorBrush(isLightBackground ? Color.FromRgb(0, 0, 0) : Color.FromRgb(255, 255, 255)); });
                    rb_Subtitle.Dispatcher.Invoke(() => { rb_Subtitle.Foreground = new SolidColorBrush(isLightBackground ? Color.FromRgb(0, 0, 0) : Color.FromRgb(255, 255, 255)); });

                    // Set the color of the words according to the grayscale level, but the effect is a bit bad, so I will give it up for now.
                    //var colorLevel = (byte)(255d - grayLevel);
                    //rb_Title.Dispatcher.Invoke(() => { rb_Title.Foreground = new SolidColorBrush(Color.FromRgb(colorLevel, colorLevel, colorLevel)); });
                    //rb_Subtitle.Dispatcher.Invoke(() => { rb_Subtitle.Foreground = new SolidColorBrush(Color.FromRgb(colorLevel, colorLevel, colorLevel)); });
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine("[red]The download of the cover image failed. It may be that the image cannot be found or there is an image parsing error[/]");
                    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                }
            }

            Color progressColor = Color.FromRgb(255, 0, 51);
            if (!string.IsNullOrEmpty(nowPlayingJson.Platform))
            {
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
                    case "bilibili":
                        progressColor = Color.FromRgb(0, 174, 236);
                        break;
                }
            }

            if (nowPlayingJson.IsLive) // When live streaming, just fill up the progress bar
            {
                pb_Process.Dispatcher.Invoke(() =>
                {
                    pb_Process.Foreground = new SolidColorBrush(progressColor);
                    pb_Process.Value = 100;
                });
            }
            else if (double.IsNormal(nowPlayingJson.Progress) && double.IsNormal(nowPlayingJson.Duration)) // Normal video
            {
                pb_Process.Dispatcher.Invoke(() =>
                {
                    pb_Process.Foreground = new SolidColorBrush(progressColor);
                    pb_Process.Value = (nowPlayingJson.Progress / nowPlayingJson.Duration) * 100;
                });
            }

            if (!string.IsNullOrEmpty(nowPlayingJson.Status))
            {
                grid_Pause.Dispatcher.Invoke(() =>
                {
                    grid_Pause.Visibility = nowPlayingJson.Status == "playing" ? Visibility.Hidden : Visibility.Visible;
                });
            }
        }

        internal void SetFont(FontFamily fontFamily)
        {
            rb_Title.Dispatcher.Invoke(() => rb_Title.FontFamily = fontFamily);
            rb_Subtitle.Dispatcher.Invoke(() => rb_Subtitle.FontFamily = fontFamily);
        }

        internal void SetWindowWidth(int width)
        {
            if (width < 400 || width > 1000)
                return;

            Dispatcher.Invoke(() => { Width = width; });
        }

        internal void SetMarqueeSpeed(int speed)
        {
            if (speed < 25 || speed > 200)
                return;

            rb_Title.Dispatcher.Invoke(() =>
            {
                rb_Title.Speed = speed;
                rb_Title.OnApplyTemplate(); // This type of method needs to be executed to trigger updates
            });
            rb_Subtitle.Dispatcher.Invoke(() =>
            {
                rb_Subtitle.Speed = speed;
                rb_Subtitle.OnApplyTemplate();
            });
        }

        internal void SetUseCoverImageAsBackground(bool useCoverImage)
        {
            isUseCoverImageAsBackground = useCoverImage;
            latestTitle = ""; // Clear last saved title to trigger status update
        }

        internal void SetTopmost(bool isTopmost)
        {
            Dispatcher.Invoke(() =>
            {
                Topmost = isTopmost;
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