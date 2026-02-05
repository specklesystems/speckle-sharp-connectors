using System.Drawing.Drawing2D;

namespace Speckle.Connectors.GrasshopperShared.HostApp;

public static class BitmapBuilder
{
  public static Bitmap CreateSquareIconBitmap(string text, int width = 24, int height = 24)
  {
    Bitmap bitmap = new(width, height);
    using Graphics graphics = Graphics.FromImage(bitmap);

    // Enable high-quality rendering
    graphics.SmoothingMode = SmoothingMode.AntiAlias;

    // Set background to transparent
    graphics.Clear(Color.Transparent);

    // Rectangle with a 1px offset
    Rectangle squareRect = new(1, 1, width - 2, height - 2);

    using (Brush blueBrush = new SolidBrush(Color.Blue))
    {
      graphics.FillRectangle(blueBrush, squareRect);
    }

    // Draw white letters in the center
    using (Font font = new("Arial", 8, FontStyle.Bold, GraphicsUnit.Pixel))
    using (Brush whiteBrush = new SolidBrush(Color.White))
    {
      StringFormat format = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

      graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
      graphics.DrawString(text, font, whiteBrush, new RectangleF(1, 1, width - 2, height - 2), format);
    }

    return bitmap;
  }

  public static Bitmap CreateCircleIconBitmap(string text, int width = 24, int height = 24)
  {
    Bitmap bitmap = new(width, height);
    using Graphics graphics = Graphics.FromImage(bitmap);

    // Enable high-quality rendering
    graphics.SmoothingMode = SmoothingMode.AntiAlias;

    // Set background to transparent
    graphics.Clear(Color.Transparent);

    // Rectangle with a 1px offset
    Rectangle squareRect = new(1, 1, width - 2, height - 2);

    using (Brush blueBrush = new SolidBrush(Color.Blue))
    {
      graphics.FillEllipse(blueBrush, squareRect);
    }

    // Draw white letters in the center
    using (Font font = new("Arial", 8, FontStyle.Bold, GraphicsUnit.Pixel))
    using (Brush whiteBrush = new SolidBrush(Color.White))
    {
      StringFormat format = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

      graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
      graphics.DrawString(text, font, whiteBrush, new RectangleF(1, 1, width - 2, height - 2), format);
    }

    return bitmap;
  }

  public static Bitmap CreateHexagonalBitmap(string text, int width = 24, int height = 24)
  {
    Bitmap bitmap = new(width, height);
    using Graphics graphics = Graphics.FromImage(bitmap);

    // Enable high-quality rendering
    graphics.SmoothingMode = SmoothingMode.AntiAlias;

    // Set background to transparent
    graphics.Clear(Color.Transparent);

    // Calculate hexagon points centered within the bitmap
    float side = (width - 2) / 2.236f; // 2.236f approximates 4 / √3 for regular hex dimensions
    float h = side * (float)Math.Sqrt(3) / 2;
    float centerX = width / 2f;
    float centerY = height / 2f;

    Point[] hexagonPoints =
    [
      new((int)(centerX - side / 2), (int)(centerY - h)),
      new((int)(centerX + side / 2), (int)(centerY - h)),
      new((int)(centerX + side), (int)centerY),
      new((int)(centerX + side / 2), (int)(centerY + h)),
      new((int)(centerX - side / 2), (int)(centerY + h)),
      new((int)(centerX - side), (int)centerY),
    ];

    using (Brush blueBrush = new SolidBrush(Color.Blue))
    {
      graphics.FillPolygon(blueBrush, hexagonPoints);
    }

    // Draw white letters in the center
    using Font font = new("Monospace", 10, FontStyle.Bold, GraphicsUnit.Pixel);
    using Brush whiteBrush = new SolidBrush(Color.White);
    StringFormat format = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
    graphics.DrawString(text, font, whiteBrush, new RectangleF(0, 1, width, height), format);

    return bitmap;
  }
}
