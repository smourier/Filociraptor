namespace Filociraptor.Configuration;

internal sealed class Settings
{
    private const string _defaultFontFamily = "Segoe UI";
    private const double _defaultFontSize = 12.5;

    // the text the dark look draws with, which is what this setting held before there was a light look.
    // a settings file still carrying it was never given a colour, so it follows the look instead of staying pale.
    private const string _legacyTextColor = "#FFE4E4E4";
    private const double _defaultPreviewPercent = 75;
    private const double _defaultCellSpacingPercent = 100;
    private const double _defaultZoom = 1;

    public string FontFamily { get; set; } = _defaultFontFamily;
    public double FontSize { get; set; } = _defaultFontSize;

    // "#AARRGGBB", which is what D3DCOLORVALUE writes and reads back, so the file says #FFE4E4E4 rather than a number.
    public string TextColor { get; set; } = string.Empty;

    // the material the window is made of, which needs Windows 11 and is left off until it is asked for.
    public Backdrop Backdrop { get; set; }

    // which look to draw with, following the system unless one of the two is asked for by name.
    public Appearance Appearance { get; set; }

    public bool SquareThumbnails { get; set; }
    public bool ThumbnailTitles { get; set; } = true;
    public bool WrapThumbnailTitles { get; set; }

    // the room around a thumbnail, as a percentage of what the font size asks for.
    // a hundred is that and nothing more, and it moves with the font because the title underneath does.
    public double CellSpacingPercent { get; set; } = _defaultCellSpacingPercent;

    // how much of the window a hover preview takes, as a percentage. zero is no preview at all.
    public double PreviewPercent { get; set; } = _defaultPreviewPercent;

    // Windows 11 shows an archive as a folder. this opens it with its application instead.
    public bool OpenArchivesAsFiles { get; set; }

    public double Zoom { get; set; } = _defaultZoom;

    // where the window was, written by WindowPosition as one line.
    public string Window { get; set; } = string.Empty;

    public List<RecentFolder> RecentFolders { get; set; } = [];

    [JsonIgnore]
    public D3DCOLORVALUE Text
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TextColor) || string.Equals(TextColor, _legacyTextColor, StringComparison.OrdinalIgnoreCase))
                return Theme.Text;

            return D3DCOLORVALUE.TryParseFromName(TextColor, out var color) ? color : Theme.Text;
        }
    }
}
