using Newtonsoft.Json;
using Spectre.Console;
using WebSocketSharp;

namespace OBSNowPlayingOverlay.WebSocketBehavior
{
    public class NowPlaying : WebSocketSharp.Server.WebSocketBehavior
    {

        protected override void OnError(ErrorEventArgs e)
        {
            base.OnError(e);

            AnsiConsole.WriteException(e.Exception, ExceptionFormats.ShortenEverything);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            base.OnMessage(e);

            if (e.IsPing)
                return;

            if (string.IsNullOrEmpty(e.Data))
                return;

            if (e.Data.StartsWith("{") && e.Data.EndsWith("}"))
            {
                try
                {
                    NowPlayingJson nowPlaying = JsonConvert.DeserializeObject<NowPlayingJson>(e.Data)!;
                    MainWindow.MsgQueue.TryAdd(nowPlaying);
                }
                catch (Exception ex)
                {
                    AnsiConsole.WriteLine(e.Data);
                    AnsiConsole.WriteException(ex, ExceptionFormats.Default);
                }
            }
            else
            {
                AnsiConsole.WriteLine(e.Data);
            }
        }
    }
}
