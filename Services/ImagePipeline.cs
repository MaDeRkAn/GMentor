using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace GMentor.Services
{
    public static class ImagePipeline
    {
        public static byte[] TransparentPng1x1 => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9W2w6z8AAAAASUVORK5CYII=");
        public static byte[] Jpeg1x1
        {
            get
            {
                using var bmp = new Bitmap(1, 1);
                bmp.SetPixel(0, 0, System.Drawing.Color.Black);
                return EncodeJpeg(bmp, 75);
            }
        }

        public static System.Windows.Media.Imaging.BitmapImage ToBitmapImage(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = ms; img.EndInit();
            img.Freeze();
            return img;
        }

        public static byte[] ToJpeg720(Bitmap src, long quality)
        {
            var (w, h) = FitWithin(src.Width, src.Height, 1280, 720);
            using var dst = new Bitmap(w, h);
            using (var g = Graphics.FromImage(dst))
            {
                g.CompositingQuality = CompositingQuality.HighSpeed;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.SmoothingMode = SmoothingMode.HighSpeed;
                g.DrawImage(src, 0, 0, w, h);
            }
            return EncodeJpeg(dst, quality);
        }

        private static (int, int) FitWithin(int w, int h, int maxW, int maxH)
        {
            var scale = Math.Min((double)maxW / w, (double)maxH / h);
            if (scale >= 1) return (w, h);
            return ((int)(w * scale), (int)(h * scale));
        }

        private static byte[] EncodeJpeg(Bitmap bmp, long quality)
        {
            using var ms = new MemoryStream();
            var enc = GetEncoder(ImageFormat.Jpeg);
            var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            bmp.Save(ms, enc, ep);
            return ms.ToArray();
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            var codecs = ImageCodecInfo.GetImageDecoders();
            foreach (var c in codecs) if (c.FormatID == format.Guid) return c;
            throw new InvalidOperationException("JPEG encoder not found");
        }
    }
}
