namespace OBSNowPlayingOverlay
{
    public class Config
    {
        public bool IsLoadSystemFonts { get; set; } = false;
        public bool IsUseCoverImageAsBackground { get; set; } = false;
        public int SeletedFontIndex { get; set; } = 1;
        public int MainWindowWidth { get; set; } = 400;
        public int MarqueeSpeed { get; set; } = 50;
    }
}
