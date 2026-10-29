using SharpGLTF.Memory;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace RevitTrueGltf.Utils
{
    /// <summary>
    /// Converts Revit grayscale bump maps into tangent-space normal maps for glTF.
    /// Uses System.Drawing (GDI+), which is already loaded by Revit: no extra native
    /// dependencies, no third-party license.
    /// </summary>
    public static class BumpToNormalConverter
    {
        public static MemoryImage Convert(string bumpPath, float strength = 2.0f)
        {
            int w, h;
            byte[] height;
            using (var src = LoadBitmap(bumpPath))
            {
                w = src.Width;
                h = src.Height;
                height = ReadRedChannel(src);
            }

            var bgra = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int yUp = Math.Max(0, y - 1);
                int yDown = Math.Min(h - 1, y + 1);
                for (int x = 0; x < w; x++)
                {
                    int xLeft = Math.Max(0, x - 1);
                    int xRight = Math.Min(w - 1, x + 1);

                    float left = height[y * w + xLeft] / 255f;
                    float right = height[y * w + xRight] / 255f;
                    float up = height[yUp * w + x] / 255f;
                    float down = height[yDown * w + x] / 255f;

                    float dx = (right - left) * strength;
                    float dy = (up - down) * strength;

                    var n = Vector3.Normalize(new Vector3(-dx, dy, 1.0f));

                    int i = (y * w + x) * 4;
                    bgra[i] = (byte)((n.Z * 0.5f + 0.5f) * 255.0f);
                    bgra[i + 1] = (byte)((n.Y * 0.5f + 0.5f) * 255.0f);
                    bgra[i + 2] = (byte)((n.X * 0.5f + 0.5f) * 255.0f);
                    bgra[i + 3] = 255;
                }
            }

            using (var dst = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                var data = dst.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(bgra, y * w * 4, IntPtr.Add(data.Scan0, y * data.Stride), w * 4);
                }
                finally
                {
                    dst.UnlockBits(data);
                }

                using (var ms = new MemoryStream())
                {
                    dst.Save(ms, ImageFormat.Png);
                    if (ms.TryGetBuffer(out ArraySegment<byte> buffer))
                        return new MemoryImage(buffer);
                    return new MemoryImage(ms.ToArray());
                }
            }
        }

        private static Bitmap LoadBitmap(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var img = Image.FromStream(fs, false, false))
            {
                return new Bitmap(img);
            }
        }

        private static byte[] ReadRedChannel(Bitmap bmp)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[w * 4];
                var red = new byte[w * h];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < w; x++)
                        red[y * w + x] = row[x * 4 + 2];
                }
                return red;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }
    }
}
