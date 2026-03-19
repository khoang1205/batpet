using OpenCvSharp;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using DSize = System.Drawing.Size;

namespace batpet.Auto
{
    public static class ImageHelper
    {
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll", SetLastError = true)] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr hWnd, out POINT lpPoint);

        const uint PW_CLIENTONLY = 0x00000001;

        // ==========================================
        // CAPTURE WINDOW
        // ==========================================
        public static Bitmap CaptureWindow(IntPtr hwnd)
        {
            if (!GetClientRect(hwnd, out var cr))
                throw new Exception("GetClientRect failed");

            int w = cr.Right - cr.Left;
            int h = cr.Bottom - cr.Top;

            var bmp = new Bitmap(w, h);

            using (var g = Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                bool ok = PrintWindow(hwnd, hdc, PW_CLIENTONLY);
                g.ReleaseHdc(hdc);

                if (!ok)
                {
                    // fallback: dùng CopyFromScreen dựa trên client top-left
                    if (!ClientToScreen(hwnd, out var tl))
                        throw new Exception("ClientToScreen failed");

                    g.CopyFromScreen(tl.X, tl.Y, 0, 0, new DSize(w, h));
                }
            }
            return bmp;
        }

        public static Bitmap CaptureSafe(IntPtr hwnd)
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var bmp = CaptureWindow(hwnd);

                    // valid frame?
                    if (bmp != null && bmp.Width > 5 && bmp.Height > 5)
                        return bmp;
                }
                catch
                {
                    // ignore, thử lại
                }

                Thread.Sleep(30);
            }

            // return ảnh dummy, tránh crash OpenCV
            return new Bitmap(20, 20);
        }

        // ==========================================
        // BITMAP -> MAT
        // ==========================================
        public static Mat ToMat(Bitmap bmp)
        {
            try
            {
                // CASE 1: Null hoặc không hợp lệ
                if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0)
                    return new Mat();        // → Mat rỗng nhưng không crash

                // CASE 2: Clone bitmap sang 32bpp (ảnh từ Flash đôi khi không đúng pixel format)
                Bitmap clone = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(clone))
                    g.DrawImage(bmp, 0, 0);

                var rect = new Rectangle(0, 0, clone.Width, clone.Height);
                var bmpData = clone.LockBits(rect, ImageLockMode.ReadOnly, clone.PixelFormat);

                Mat output;

                unsafe
                {
                    byte* ptr = (byte*)bmpData.Scan0.ToPointer();
                    output = Mat.FromPixelData(
                        clone.Height,
                        clone.Width,
                        MatType.CV_8UC4,
                        (nint)ptr,
                        bmpData.Stride
                    ).Clone();
                }

                clone.UnlockBits(bmpData);
                clone.Dispose();

                return output;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ToMat EXCEPTION: {ex.Message}");
                return new Mat();
            }
        }

        // ==========================================
        // TEMPLATE MATCHING — SINGLE SCALE
        // ==========================================
        public static (OpenCvSharp.Point? p, double score) MatchOnce(Bitmap hayBmp, Bitmap tplBmp, double threshold)
        {
            using var hay = ToMat(hayBmp);
            using var tpl = ToMat(tplBmp);

            // nếu 1 trong 2 rỗng thì bỏ luôn
            if (hay.Empty() || tpl.Empty())
                return (null, 0);

            using var hayGray = new Mat();
            using var tplGray = new Mat();

            Cv2.CvtColor(hay, hayGray, ColorConversionCodes.BGR2GRAY);
            Cv2.CvtColor(tpl, tplGray, ColorConversionCodes.BGR2GRAY);

            if (hayGray.Cols < tplGray.Cols || hayGray.Rows < tplGray.Rows)
                return (null, 0);

            using var result = new Mat(
                hayGray.Rows - tplGray.Rows + 1,
                hayGray.Cols - tplGray.Cols + 1,
                MatType.CV_32FC1);

            Cv2.MatchTemplate(hayGray, tplGray, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

            if (maxVal >= threshold)
            {
                var center = new OpenCvSharp.Point(
                    maxLoc.X + tplGray.Cols / 2,
                    maxLoc.Y + tplGray.Rows / 2);
                return (center, maxVal);
            }
            return (null, maxVal);
        }

        // ==========================================
        // TEMPLATE MATCHING — MULTI SCALE
        // ==========================================
        public static (OpenCvSharp.Point? p, double score) MatchMultiScale(
            Bitmap hayBmp,
            Bitmap tplBmp,
            double threshold)
        {
            using var hay = ToMat(hayBmp);
            using var tpl = ToMat(tplBmp);

            // nếu 1 trong 2 rỗng thì bỏ luôn
            if (hay.Empty() || tpl.Empty())
                return (null, 0);

            using var hayGray = new Mat();
            Cv2.CvtColor(hay, hayGray, ColorConversionCodes.BGR2GRAY);

            double bestScore = 0;
            OpenCvSharp.Point? bestPt = null;

            foreach (double scale in new[] { 1.0, 0.9, 0.8, 0.75, 0.7 })
            {
                int newW = (int)(tplBmp.Width * scale);
                int newH = (int)(tplBmp.Height * scale);
                if (newW < 10 || newH < 10) continue;

                using var resized = new Mat();
                Cv2.Resize(tpl, resized, new OpenCvSharp.Size(newW, newH));

                using var tplGray = new Mat();
                Cv2.CvtColor(resized, tplGray, ColorConversionCodes.BGR2GRAY);

                if (hayGray.Cols < tplGray.Cols || hayGray.Rows < tplGray.Rows)
                    continue;

                using var result = new Mat();
                Cv2.MatchTemplate(hayGray, tplGray, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

                if (maxVal > bestScore)
                {
                    bestScore = maxVal;
                    bestPt = new OpenCvSharp.Point(maxLoc.X + newW / 2, maxLoc.Y + newH / 2);
                }
            }

            return (bestPt, bestScore);
        }

        // ==========================================
        // CLICK UTIL
        // ==========================================
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        const int WM_LBUTTONDOWN = 0x0201;
        const int WM_LBUTTONUP = 0x0202;

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        public static void ClickClient(IntPtr hwnd, int x, int y, Action<string>? log = null)
        {
            int lParam = (y << 16) | (x & 0xFFFF);

            PostMessage(hwnd, WM_LBUTTONDOWN, 1, lParam);
            Thread.Sleep(25);
            PostMessage(hwnd, WM_LBUTTONUP, 0, lParam);

            log?.Invoke($"🖱️ ClickClient ({x},{y})");
        }

        // ==========================================
        // POPUP / IMAGE HELPERS
        // ==========================================
        public static bool IsPopupVisible(IntPtr hwnd, string popupImg, double threshold = 0.8)
        {
            using var frame = CaptureSafe(hwnd);
            using var tpl = (Bitmap)Image.FromFile(popupImg);
            var (pt, score) = MatchOnce(frame, tpl, threshold);
            return pt.HasValue && score >= threshold;
        }

        public static bool ClickImage(
            IntPtr hwnd,
            string imgPath,
            double threshold,
            Action<string>? log = null)
        {
            using var frame = CaptureSafe(hwnd);
            using var tpl = (Bitmap)Image.FromFile(imgPath);

            var (pt, score) = MatchMultiScale(frame, tpl, threshold);

            if (!pt.HasValue || score < threshold)
            {
                log?.Invoke($"🙈 Không thấy ảnh {Path.GetFileName(imgPath)} (score={score:F2})");
                return false;
            }

            ClickClient(hwnd, pt.Value.X, pt.Value.Y, log);
            log?.Invoke($"✅ Click hình {Path.GetFileName(imgPath)} tại ({pt.Value.X},{pt.Value.Y}) score={score:F2}");

            return true;
        }
    }
}
