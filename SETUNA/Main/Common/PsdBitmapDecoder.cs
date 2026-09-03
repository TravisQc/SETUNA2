using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Prowl.Aperture;

namespace SETUNA.Main
{
    /// <summary>Converts Aperture's flattened PSD pixels into an owned GDI bitmap.</summary>
    internal static class PsdBitmapDecoder
    {
        public static Bitmap Decode(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            using (var image = Prowl.Aperture.Image.Load(stream, new DecodeOptions
            {
                TargetPixelFormat = Prowl.Aperture.PixelFormat.Rgba8,
                UsePooledMemory = true,
                MaxPixels = 64_000_000
            }))
            {
                var frame = image.RootFrame;
                var bitmap = new Bitmap(frame.Width, frame.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var data = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.WriteOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                try
                {
                    var destination = new byte[Math.Abs(data.Stride) * bitmap.Height];
                    for (var y = 0; y < bitmap.Height; y++)
                    {
                        var sourceRow = frame.GetRow(y);
                        var destinationOffset = y * Math.Abs(data.Stride);

                        for (var x = 0; x < bitmap.Width; x++)
                        {
                            var sourceOffset = x * 4;
                            var pixelOffset = destinationOffset + sourceOffset;
                            destination[pixelOffset] = sourceRow[sourceOffset + 2];
                            destination[pixelOffset + 1] = sourceRow[sourceOffset + 1];
                            destination[pixelOffset + 2] = sourceRow[sourceOffset];
                            destination[pixelOffset + 3] = sourceRow[sourceOffset + 3];
                        }
                    }

                    Marshal.Copy(destination, 0, data.Scan0, destination.Length);
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                return bitmap;
            }
        }
    }
}
