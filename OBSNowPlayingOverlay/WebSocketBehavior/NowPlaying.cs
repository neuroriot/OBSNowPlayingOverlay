using Newtonsoft.Json;
using Spectre.Console;
using System.Text.RegularExpressions;
using WebSocketSharp;

namespace OBSNowPlayingOverlay.WebSocketBehavior
{
    public class NowPlaying : WebSocketSharp.Server.WebSocketBehavior
    {
        readonly Regex _wsMsgRegex = new(@"(?<Type>\S+) - (?<SiteName>\S+) \((?<Guid>[^)]+)\)");

        protected override void OnError(ErrorEventArgs e)
        {
            base.OnError(e);

            AnsiConsole.WriteException(e.Exception);
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            try
            {
                base.OnMessage(e);

                if (e.IsPing)
                    return;

                if (string.IsNullOrEmpty(e.Data))
                    return;

                if (e.Data.StartsWith("{") && e.Data.EndsWith("}"))
                {
                    NowPlayingJson nowPlaying = JsonConvert.DeserializeObject<NowPlayingJson>(e.Data)!;
                    MainWindow.MsgQueue.TryAdd(nowPlaying);
                }
                else if (_wsMsgRegex.IsMatch(e.Data))
                {
                    var match = _wsMsgRegex.Match(e.Data);
                    if (match.Groups["Type"].ToString() == "connected")
                    {
                        AnsiConsole.MarkupLineInterpolated($"連線狀態變更: [springgreen4]新連線[/] | [yellow4_1]{match.Groups["SiteName"]}[/] | [purple4_1]{match.Groups["Guid"]}[/]");
                        AnsiConsole.MarkupLineInterpolated($"改由此 Guid 繼續更新狀態: [purple4_1]{match.Groups["Guid"]}[/]");
                        MainWindow.LatestWebSocketGuid = match.Groups["Guid"].ToString();
                    }
                    else if (match.Groups["Type"].ToString() == "closed")
                    {
                        AnsiConsole.MarkupLineInterpolated($"連線狀態變更: [orangered1]已關閉[/] | [yellow4_1]{match.Groups["SiteName"]}[/] | [purple4_1]{match.Groups["Guid"]}[/]");
                        MainWindow.LatestWebSocketGuid = string.Empty;
                    }
                }
                else
                {
                    AnsiConsole.MarkupLineInterpolated($"接收到未處理的資料: [dodgerblue2]{e.Data}[/]");
                }
            }
            catch (OperationCanceledException) { }
            catch (InvalidOperationException) { }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[olive]{e.Data}[/]");
                AnsiConsole.WriteException(ex, ExceptionFormats.Default);
            }
        }
    }
}
