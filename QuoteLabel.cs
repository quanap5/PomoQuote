using System.Drawing.Text;
using PomoQuote.Quotes;

namespace PomoQuote;

/// <summary>
/// The big text area of the main window: an English sentence in green with its Vietnamese translation under
/// it, centred, as large as fits (English at most three lines, see <see cref="QuoteLayout"/>). The background
/// is transparent so a wallpaper shows through; over a wallpaper the text is lighter and has a dark shadow.
/// It owns no fonts or bitmaps between paints, so changing the sentence thousands of times leaks nothing.
/// </summary>
internal sealed class QuoteLabel : Control
{
    /// <summary>The smallest comfortable height of the band at 100% display scale.</summary>
    public const int LogicalMinimumHeight = 150;

    private static readonly Color PlainEnglish = Color.Green;
    private static readonly Color PlainVietnamese = Color.FromArgb(70, 70, 70);
    private static readonly Color WallpaperEnglish = Color.FromArgb(120, 230, 120);
    private static readonly Color WallpaperVietnamese = Color.FromArgb(240, 240, 240);

    private string _english = "";
    private string? _vietnamese;
    private string? _badge;
    private bool _onWallpaper;
    private QuoteFit? _fit;
    private SizeF _fitArea;

    public QuoteLabel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    /// <summary>The sentence shown (empty when there is none).</summary>
    public string English => _english;

    /// <summary>The translation shown, or null when it is off or missing.</summary>
    public string? Vietnamese => _vietnamese;

    /// <summary>The layout chosen for the last paint (null before the first paint); exposed for tests.</summary>
    internal QuoteFit? LastFit => _fit;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool OnWallpaper
    {
        get => _onWallpaper;
        set
        {
            if (_onWallpaper == value) return;
            _onWallpaper = value;
            Invalidate();
        }
    }

    /// <summary>The small note under the sentence (a machine translation that nobody reviewed), or null.</summary>
    public string? Badge => _badge;

    public void SetQuote(string english, string? vietnamese, string? badge = null)
    {
        var translation = string.IsNullOrWhiteSpace(vietnamese) ? null : vietnamese;
        var note = translation is null ? null : badge; // a note about the translation only while it is shown
        if (english == _english && translation == _vietnamese && note == _badge) return;
        _english = english;
        _vietnamese = translation;
        _badge = note;
        _fit = null;
        Text = _vietnamese is null ? english : _badge is null ? $"{english} {_vietnamese}" : $"{english} {_vietnamese} ({_badge})";
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        _fit = null;
        base.OnResize(e);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        _fit = null;
        base.OnDpiChangedAfterParent(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_english.Length == 0) return;

        var graphics = e.Graphics;
        var padding = new SizeF(ClientSize.Width * 0.06f, ClientSize.Height * 0.06f);
        var area = new RectangleF(padding.Width, padding.Height, ClientSize.Width - 2 * padding.Width, ClientSize.Height - 2 * padding.Height);
        var scale = DeviceDpi / 96f;
        var badgeHeight = _badge is null ? 0f : 14f * scale;
        area.Height -= badgeHeight;
        if (area.Width < 8 || area.Height < 8) return;

        if (_fit is null || _fitArea != area.Size)
        {
            _fit = QuoteLayout.Fit(graphics, _english, _vietnamese, area.Size, scale);
            _fitArea = area.Size;
        }

        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var format = QuoteLayout.CreateFormat();
        var fit = _fit;
        var englishColor = _onWallpaper ? WallpaperEnglish : PlainEnglish;
        var vietnameseColor = _onWallpaper ? WallpaperVietnamese : PlainVietnamese;
        var englishRect = Offset(fit.English, area);
        using (var font = new Font(QuoteLayout.EnglishFamily, fit.EnglishPixels, FontStyle.Regular, GraphicsUnit.Pixel))
        {
            DrawText(graphics, _english, font, englishColor, englishRect, format, scale);
        }

        if (fit.Vietnamese.Height > 0 && _vietnamese is not null)
        {
            using var font = new Font(QuoteLayout.VietnameseFamily, fit.VietnamesePixels, FontStyle.Regular, GraphicsUnit.Pixel);
            DrawText(graphics, _vietnamese, font, vietnameseColor, Offset(fit.Vietnamese, area), format, scale);
        }

        if (_badge is not null)
        {
            using var font = new Font(QuoteLayout.VietnameseFamily, 10.5f * scale, FontStyle.Italic, GraphicsUnit.Pixel);
            using var centre = new StringFormat(StringFormatFlags.NoWrap) { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            DrawText(graphics, _badge, font, vietnameseColor, new RectangleF(area.X, area.Bottom, area.Width, badgeHeight), centre, scale);
        }
    }

    private static RectangleF Offset(RectangleF rect, RectangleF area) =>
        new(area.X + rect.X, area.Y + rect.Y, rect.Width, rect.Height);

    private void DrawText(Graphics graphics, string text, Font font, Color color, RectangleF rect, StringFormat format, float scale)
    {
        if (_onWallpaper)
        {
            var shadowRect = rect;
            shadowRect.Offset(Math.Max(1f, scale), Math.Max(1f, scale));
            using var shadow = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
            graphics.DrawString(text, font, shadow, shadowRect, format);
        }

        using var brush = new SolidBrush(color);
        graphics.DrawString(text, font, brush, rect, format);
    }
}
