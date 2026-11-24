using PomoQuote.AiUsage;

namespace PomoQuote.Widget;

/// <summary>
/// The measurements of the AI usage area in logical pixels (100% display scale). Pure numbers, so the layout can be tested: the
/// columns of a quota row never overlap, Compact has a fixed height whichever provider is shown, and Expanded is capped.
/// </summary>
internal static class UsageGeometry
{
    /// <summary>The widget's width at 100%: the smallest (and only) width the layout supports.</summary>
    public const int WidgetWidth = 280;

    public const int Margin = 14;
    public const int RowHeight = 18;

    /// <summary>Compact: two quota rows plus 5 px above and below. Never more than three lines of content.</summary>
    public const int CompactHeight = 46;

    /// <summary>Expanded grows with its cards up to this height, then only the usage area scrolls.</summary>
    public const int MaxExpandedHeight = 168;

    /// <summary>The logo column, on the far left in both layouts.</summary>
    public const int LogoColumn = 30;

    /// <summary>The logo itself (the brief asks for about 18 to 20 px).</summary>
    public const int LogoSize = 20;

    /// <summary>The column on the right that holds the Expand/Collapse button (and the scrollbar of Expanded).</summary>
    public const int ToggleColumn = 16;

    public const int CardPadding = 6;
    public const int CardGap = 6;
    public const int HeaderHeight = 16;
    public const int FooterHeight = 14;

    private const int LabelWidth = 36;
    private const int PercentWidth = 40;
    private const int ResetWidth = 50;
    private const int Gap = 4;

    /// <summary>The columns of one quota row, from the left edge <paramref name="left"/> across <paramref name="width"/>.</summary>
    internal readonly record struct RowColumns(float LabelX, float LabelWidth, float BarX, float BarWidth, float PercentX, float PercentWidth, float ResetX, float ResetWidth)
    {
        public float Right => ResetX + ResetWidth;
    }

    public static RowColumns Columns(float left, float width)
    {
        var bar = Math.Max(24f, width - LabelWidth - PercentWidth - ResetWidth - 3 * Gap);
        var labelX = left;
        var barX = labelX + LabelWidth + Gap;
        var percentX = barX + bar + Gap;
        var resetX = percentX + PercentWidth + Gap;
        return new RowColumns(labelX, LabelWidth, barX, bar, percentX, PercentWidth, resetX, ResetWidth);
    }

    /// <summary>The width the rows get in Compact: the widget minus its margins, the logo column and the button column.</summary>
    public static float CompactRowsWidth => WidgetWidth - 2 * Margin - LogoColumn - ToggleColumn;

    /// <summary>The width a card's rows get in Expanded: the card minus its padding and the logo column.</summary>
    public static float CardRowsWidth => WidgetWidth - 2 * Margin - ToggleColumn - 2 * CardPadding - LogoColumn;

    /// <summary>A card: padding, the header, one row per window (one line for N/A or a note), the footer, padding.</summary>
    public static int CardHeight(UsageProviderView view) => CardPadding + HeaderHeight + Math.Max(1, view.Rows.Count) * RowHeight + FooterHeight + CardPadding;

    public static int ExpandedContentHeight(IReadOnlyList<UsageProviderView> views) =>
        views.Count == 0 ? 0 : views.Sum(CardHeight) + CardGap * (views.Count - 1);

    /// <summary>The height of the whole AI usage area: 0 without providers, fixed in Compact, the cards (capped) in Expanded.</summary>
    public static int AreaHeight(IReadOnlyList<UsageProviderView> views, bool expanded) =>
        views.Count == 0 ? 0 : expanded ? Math.Min(MaxExpandedHeight, ExpandedContentHeight(views)) : CompactHeight;

    /// <summary>How far the Expanded area can scroll (0 when everything fits).</summary>
    public static int MaxScroll(IReadOnlyList<UsageProviderView> views) => Math.Max(0, ExpandedContentHeight(views) - MaxExpandedHeight);
}
