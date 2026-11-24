using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace HelloWorldBot.Mascot;

/// <summary>Cuts frames out of the sprite sheet and enlarges them by a whole factor with nearest-neighbour scaling.</summary>
internal sealed class MascotRenderer : IDisposable
{
    private readonly Bitmap _sheet;

    /// <summary>Loads the sprite sheet; throws if the file is missing or not an image.</summary>
    public MascotRenderer(string sheetPath)
    {
        using var file = Image.FromFile(sheetPath);
        _sheet = new Bitmap(file); // a copy, so the file is never left locked
    }

    /// <summary>
    /// Draws the frame for <paramref name="pose"/> as a premultiplied-alpha bitmap (what a layered window
    /// needs), mirrored when the robot faces left. The caller disposes the bitmap.
    /// </summary>
    public Bitmap Render(MascotPose pose, int sizeScale, int dpi)
    {
        var size = MascotSprites.PixelSize(sizeScale, dpi);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.CompositingMode = CompositingMode.SourceCopy;

        if (!pose.FacingRight)
        {
            graphics.TranslateTransform(size, 0);
            graphics.ScaleTransform(-1, 1);
        }

        var cell = MascotSprites.CellSize;
        var source = new Rectangle(pose.Frame * cell, MascotSprites.Row(pose.Animation) * cell, cell, cell);
        graphics.DrawImage(_sheet, new Rectangle(0, 0, size, size), source, GraphicsUnit.Pixel);
        return bitmap;
    }

    public void Dispose() => _sheet.Dispose();
}
