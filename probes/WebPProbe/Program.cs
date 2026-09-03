using System;
using System.Drawing;
using WebPWrapper;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        using (var source = new Bitmap(3, 2))
        {
            source.SetPixel(0, 0, Color.Red);
            source.SetPixel(1, 0, Color.Green);
            source.SetPixel(2, 0, Color.Blue);
            source.SetPixel(0, 1, Color.Black);
            source.SetPixel(1, 1, Color.White);
            source.SetPixel(2, 1, Color.Magenta);

            using (var webp = new WebP())
            {
                var encoded = webp.EncodeLossless(source);
                using (var decoded = webp.Decode(encoded))
                {
                    if (decoded.Width != source.Width || decoded.Height != source.Height)
                    {
                        Console.Error.WriteLine("WebP probe: dimensions changed.");
                        return 2;
                    }
                }
            }
        }

        Console.WriteLine("WebP probe passed for " + (IntPtr.Size == 8 ? "x64" : "x86"));
        return 0;
    }
}
