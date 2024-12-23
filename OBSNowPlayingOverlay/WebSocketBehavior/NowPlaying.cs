using Newtonsoft.Json;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using WebSocketSharp;

namespace OBSNowPlayingOverlay.WebSocketBehavior
{
    public class NowPlaying : WebSocketSharp.Server.WebSocketBehavior
    {
        private readonly static ConcurrentDictionary<string, WebSocketClientInfo> _clientDict = new();
        readonly Regex _wsMsgRegex = new(@"(?<Type>\S+) - (?<SiteName>\S+) \((?<Guid>[^)]+)\)");
        private readonly Timer clientActivityTimer;

        public NowPlaying()
        {
            clientActivityTimer = new Timer(CheckClientActivity, null, 0, 3000);
        }

        private static void CheckClientActivity(object? state)
        {
            var inactiveClients = _clientDict.Where(c => (DateTime.Now - c.Value.LastActiveTime).TotalSeconds > 3).ToList();

            foreach (var client in inactiveClients)
            {
                client.Value.IsPlaying = false;
            }

            // Update the latest playback Client
            if (_clientDict.TryGetValue(MainWindow.LatestWebSocketGuid, out var clientInfo))
            {
                if (clientInfo.IsPlaying)
                {
                    MainWindow.LatestWebSocketGuid = clientInfo.Guid;
                }
                else
                {
                    MainWindow.LatestWebSocketGuid = string.Empty;
                }
            }
            else
            {
                MainWindow.LatestWebSocketGuid = string.Empty;
            }
        }

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

                // Update the logic to handle JSON messages.
                if (e.Data.StartsWith("{") && e.Data.EndsWith("}"))
                {
                    NowPlayingJson nowPlaying = JsonConvert.DeserializeObject<NowPlayingJson>(e.Data)!;

                    // Find the client in the list or create a new one
                    if (!_clientDict.TryGetValue(nowPlaying.Guid, out var client))
                    {
                        client = new WebSocketClientInfo(nowPlaying.Guid);
                        _clientDict.TryAdd(nowPlaying.Guid, client);
                    }

                    client.LastActiveTime = DateTime.Now;
                    client.IsPlaying = nowPlaying.Status == "playing";

                    // If this client is the only one playing, set it as the latest
                    if (_clientDict.Count(c => c.Value.IsPlaying) == 1)
                    {
                        MainWindow.LatestWebSocketGuid = client.Guid;
                    }

                    MainWindow.MsgQueue.TryAdd(nowPlaying);
                }
                else if (_wsMsgRegex.IsMatch(e.Data))
                {
                    var match = _wsMsgRegex.Match(e.Data);
                    var guid = match.Groups["Guid"].ToString();

                    if (match.Groups["Type"].ToString() == "connected")
                    {
                        AnsiConsole.MarkupLineInterpolated($"Connection status change: [springgreen4]New connection[/] | [yellow4_1]{match.Groups["SiteName"]}[/] | [purple4_1]{guid}[/]");

                        if (!_clientDict.ContainsKey(guid))
                        {
                            _clientDict.TryAdd(guid, new WebSocketClientInfo(guid));
                        }
                    }
                    else if (match.Groups["Type"].ToString() == "closed")
                    {
                        AnsiConsole.MarkupLineInterpolated($"Connection status change: [orangered1]Close[/] | [yellow4_1]{match.Groups["SiteName"]}[/] | [purple4_1]{guid}[/]");
                        _clientDict.TryRemove(guid, out _);

                        if (MainWindow.LatestWebSocketGuid == guid)
                        {
                            MainWindow.LatestWebSocketGuid = string.Empty;
                        }
                    }
                }
                else
                {
                    AnsiConsole.MarkupLineInterpolated($"Unprocessed data received: [dodgerblue2]{e.Data}[/]");
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
