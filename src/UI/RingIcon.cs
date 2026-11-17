using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace BatteryMonitor
{
    internal static class RingIcon
    {
        public static Color LevelColor(BatteryReading reading, bool light, DateTime nowUtc)
        {
            return Color.Black;
        }

        public static Bitmap Render(int size, BatteryPair pair, bool light, DateTime nowUtc)
        {
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Bitmap surface = new Bitmap(size * 4, size * 4, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(surface))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.ScaleTransform(size * 4 / 16f, size * 4 / 16f);
                    // Black outline with clear space around the fill and between devices.
                    RectangleF fillArea = new RectangleF(2f, 2f, 12f, 12f);
                    using (GraphicsPath shape = new GraphicsPath())
                    {
                        shape.AddEllipse(fillArea);
                        g.SetClip(shape);
                        DrawHalf(g, fillArea, pair.Left, light, nowUtc, true);
                        DrawHalf(g, fillArea, pair.Right, light, nowUtc, false);
                        g.ResetClip();
                        using (Pen border = new Pen(Color.Black, 1.3f))
                            g.DrawEllipse(border, new RectangleF(0.65f, 0.65f, 14.7f, 14.7f));
                    }
                }
                using (Graphics target = Graphics.FromImage(bitmap))
                {
                    target.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    target.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    target.DrawImage(surface, new Rectangle(0, 0, size, size));
                }
            }
            return bitmap;
        }

        private static void DrawHalf(Graphics g, RectangleF bounds, BatteryReading reading, bool light, DateTime nowUtc, bool left)
        {
            GraphicsState state = g.Save();
            try
            {
                RectangleF half = new RectangleF(left ? bounds.Left : 8.5f, bounds.Top, bounds.Width / 2 - 0.5f, bounds.Height);
                g.SetClip(half, CombineMode.Intersect);
                if (reading.IsValid(nowUtc))
                {
                    int level = reading.Percentage.Value;
                    if (level == 0) return;
                    using (SolidBrush fill = new SolidBrush(LevelColor(reading, light, nowUtc)))
                        g.FillRectangle(fill, bounds.Left, bounds.Bottom - bounds.Height * level / 100f, bounds.Width, bounds.Height * level / 100f);
                }
                else
                {
                    using (Pen dash = new Pen(Color.Black, 1.1f))
                        g.DrawLine(dash, left ? 2.8f : 10f, 8f, left ? 6f : 13.2f, 8f);
                }
            }
            finally { g.Restore(state); }
        }

        public static Icon Create(BatteryPair pair, bool light, DateTime nowUtc, int preferredSize)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64 };
            byte[][] frames = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                using (Bitmap bitmap = Render(sizes[i], pair, light, nowUtc))
                using (MemoryStream png = new MemoryStream())
                {
                    bitmap.Save(png, ImageFormat.Png);
                    frames[i] = png.ToArray();
                }
            using (MemoryStream ico = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, true))
                {
                    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
                    int offset = 6 + 16 * sizes.Length;
                    for (int i = 0; i < sizes.Length; i++)
                    {
                        writer.Write((byte)sizes[i]); writer.Write((byte)sizes[i]); writer.Write((byte)0); writer.Write((byte)0);
                        writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(frames[i].Length); writer.Write(offset);
                        offset += frames[i].Length;
                    }
                    foreach (byte[] frame in frames) writer.Write(frame);
                    writer.Flush();
                }
                ico.Position = 0;
                using (Icon icon = new Icon(ico, preferredSize, preferredSize)) return (Icon)icon.Clone();
            }
        }
    }
}
