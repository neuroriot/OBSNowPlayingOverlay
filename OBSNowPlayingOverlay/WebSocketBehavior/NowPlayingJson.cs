using Newtonsoft.Json;

#nullable disable

namespace OBSNowPlayingOverlay
{
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class NowPlayingJson
    {
        [JsonProperty("guid")]
        public string Guid { get; set; }

        [JsonProperty("cover")]
        public string Cover { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("artists")]
        public List<string> Artists { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("progress")]
        public double Progress { get; set; }

        [JsonProperty("duration")]
        public double Duration { get; set; }

        [JsonProperty("song_link")]
        public string SongLink { get; set; }

        [JsonProperty("platform")]
        public string Platform { get; set; }

        [JsonProperty("is_live")]
        public bool IsLive { get; set; }
    }
}
