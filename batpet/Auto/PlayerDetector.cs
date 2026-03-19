using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace batpet.Auto
{
    public static class PlayerDetector
    {
        public static bool IsPlayerVisible(IntPtr hwnd, string playerImg, double threshold = 0.85)
        {
            using var frame = ImageHelper.CaptureWindow(hwnd);
            using var tpl = (Bitmap)Image.FromFile(playerImg);
            var (pt, score) = ImageHelper.MatchOnce(frame, tpl, threshold);
            return pt.HasValue && score >= threshold;
        }

        public static bool WaitDisappear(IntPtr hwnd, string playerImg, Action<string> log, double th = 0.85, int timeoutMs = 8000)
        {
            var start = Environment.TickCount;
            while (Environment.TickCount - start < timeoutMs)
            {
                if (!IsPlayerVisible(hwnd, playerImg, th)) { log("🫥 Player mất → vào trận"); return true; }
                Thread.Sleep(350);
            }
            log("⚠️ Hết giờ mà vẫn thấy player (chưa vào trận?)");
            return false;
        }

        public static bool WaitAppear(IntPtr hwnd, string playerImg, Action<string> log, double th = 0.85, int timeoutMs = 5000)
        {
            var start = Environment.TickCount;
            while (Environment.TickCount - start < timeoutMs)
            {
                if (IsPlayerVisible(hwnd, playerImg, th)) { log("✅ Player về lại (kết thúc trận)"); return true; }
                Thread.Sleep(350);
            }
            log("⏰ Hết giờ chờ player xuất hiện lại");
            return false;
        }
    }
}
