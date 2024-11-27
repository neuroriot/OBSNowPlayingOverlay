namespace OBSNowPlayingOverlay.WebSocketBehavior
{
    public class WebSocketClientInfo
    {
        public string Guid { get; set; }
        public DateTime LastActiveTime { get; set; }
        public bool IsPlaying { get; set; }

        public WebSocketClientInfo(string guid)
        {
            Guid = guid;
            LastActiveTime = DateTime.Now;
            IsPlaying = false;
        }
    }
}
