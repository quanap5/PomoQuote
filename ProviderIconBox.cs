using PomoQuote.AiUsage;

namespace PomoQuote;

/// <summary>A small control that paints a provider icon at its own pixel size (so it stays crisp at any DPI); empty when there is no icon.</summary>
internal sealed class ProviderIconBox : Control
{
    private readonly ProviderIcons _icons;
    private readonly string _providerId;

    public ProviderIconBox(ProviderIcons icons, string providerId, int logicalSize = 16)
    {
        _icons = icons;
        _providerId = providerId;
        SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        BackColor = Color.Transparent;
        TabStop = false;
        Size = new Size(logicalSize, logicalSize);
        Margin = new Padding(0, 3, 6, 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var icon = _icons.Get(_providerId, IconBackground.Light, Math.Min(ClientSize.Width, ClientSize.Height));
        if (icon is not null) e.Graphics.DrawImageUnscaled(icon, 0, 0);
    }
}
