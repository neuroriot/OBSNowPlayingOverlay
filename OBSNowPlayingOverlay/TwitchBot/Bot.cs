using Spectre.Console;
using System.Diagnostics;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;

namespace OBSNowPlayingOverlay.TwitchBot
{
    public class Bot
    {
        public bool? IsConnect { get { return client?.IsConnected; } }

        private TwitchClient? client = null;
        private DateTime latestNPCommandExecuteTime = DateTime.MinValue;

        private readonly string[] _musicCommandArray = new[] { "music", "playing", "np", "nowplaying", "Now playing", "Music" };

        public void SetBotCred(string accessToken, string userLogin)
        {
            var credentials = new ConnectionCredentials(userLogin, accessToken);
            var clientOptions = new ClientOptions
            {
                MessagesAllowedInPeriod = 750,
                ThrottlingPeriod = TimeSpan.FromSeconds(30)
            };

            var customClient = new WebSocketClient(clientOptions);
            client = new TwitchClient(customClient);
            client.Initialize(credentials, userLogin, autoReListenOnExceptions: !Debugger.IsAttached);

            client.OnJoinedChannel += client_OnJoinedChannel;
            client.OnConnected += client_OnConnected;
            client.OnChatCommandReceived += client_OnChatCommandReceived;
        }

        public void StartBot()
        {
            Console.WriteLine("Twitch Bot is connecting...");
            client?.Connect();
        }

        public void StopBot()
        {
            if (client != null)
            {
                Console.WriteLine("Twitch Bot is offline...");
                client.Disconnect();

                client.OnJoinedChannel -= client_OnJoinedChannel;
                client.OnConnected -= client_OnConnected;
                client.OnChatCommandReceived -= client_OnChatCommandReceived;
            }

            client = null;
        }

        private void client_OnChatCommandReceived(object? sender, OnChatCommandReceivedArgs e)
        {
            // Trigger command every 30 seconds
            if (_musicCommandArray.Contains(e.Command.CommandText.Trim()) && DateTime.Now.Subtract(latestNPCommandExecuteTime).TotalSeconds >= 30)
            {
                AnsiConsole.MarkupLineInterpolated($"Twitch Bot command trigger: [green]{e.Command.ChatMessage.Username} - {e.Command.CommandText}[/]");

                client?.SendMessage(e.Command.ChatMessage.Channel, $"Now playing:{MainWindow.NowPlayingTitle}");
                client?.SendMessage(e.Command.ChatMessage.Channel, $"URL:{MainWindow.NowPlayingUrl}");

                latestNPCommandExecuteTime = DateTime.Now;
            }
        }

        private void client_OnConnected(object? sender, OnConnectedArgs e)
        {
            Console.WriteLine($"Twitch Bot is connected to IRC");
        }

        private void client_OnJoinedChannel(object? sender, OnJoinedChannelArgs e)
        {
            AnsiConsole.MarkupLineInterpolated($"Twitch Bot is connected to channel: [green]{e.Channel}[/]");
        }
    }
}
