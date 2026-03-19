using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Drawing;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;
using System.Text;

namespace batpet.Auto
{
    public static class AutoPetController
    {
        // ===== SETTINGS =====
        const int SLEEP_UI = 2500;
        const int SLEEP_SHORT = 1000;
        static double TH_PET = 0.78;
        static double TH_BTN = 0.8;
        const double TH_PET_LAM = 0.70;
        static Rectangle PetGridRect = new Rectangle(470, 255, 390, 300);
        static bool bagOpened = false;

        // ===== STATE =====
        static int CatchCount = 0;
        public static bool IsFusing = false;


        // =======================
        // CLICK UTILS
        // =======================
        public static bool ClickByImage(IntPtr hwnd, string imgPath, double th, Action<string> log)
        {
            using var frame = ImageHelper.CaptureWindow(hwnd);
            using var tpl = (Bitmap)Image.FromFile(imgPath);
            var (pt, score) = ImageHelper.MatchMultiScale(frame, tpl, th);

            if (pt.HasValue && score >= th)
            {
                ImageHelper.ClickClient(hwnd, pt.Value.X, pt.Value.Y, log);
                log?.Invoke($"🖱 Click {Path.GetFileName(imgPath)} @({pt.Value.X},{pt.Value.Y}) score={score:F2}");
                return true;
            }
            log?.Invoke($"🙈 Không thấy {Path.GetFileName(imgPath)} (score={score:F2})");
            return false;
        }


        // =======================
        // ENSURE OUT OF COMBAT
        // =======================
        public static bool EnsureOutOfCombat(
            IntPtr hwnd,
            string playerImg,
            Action<string> log,
            CancellationToken token)
        {
            int retry = 0;
            while (!PlayerDetector.IsPlayerVisible(hwnd, playerImg) && retry < 12)
            {
                if (token.IsCancellationRequested) return false;
                log($"⏳ Chờ thoát combat...({retry + 1}/12)");
                Thread.Sleep(800);
                retry++;
            }

            if (PlayerDetector.IsPlayerVisible(hwnd, playerImg))
            {
                log("✅ Đã ở ngoài combat");
                return true;
            }

            log("❌ Không thấy player → FAIL");
            return false;
        }


        // =======================
        // COMBAT LOOP
        // =======================
        public static void HandleCombat(
            IntPtr hwnd,
            string playerImg,
            string batBtn,
            string boChayBtn,
            string coBtn,
            IReadOnlyList<string> wantedPets,
            Action<string> log,
            CancellationToken token)
        {
            bool caughtSomething = false;
            if (IsFusing) return;

            log("⚔️ Bắt đầu combat...");
            if (token.IsCancellationRequested) return;

            // 1) chờ player biến mất
            if (!PlayerDetector.WaitDisappear(hwnd, playerImg, log, 0.85, 8000))
            {
                log("⚠️ Không vào combat");
                return;
            }
            var sw = Stopwatch.StartNew();

            while (true)
            {
                if (token.IsCancellationRequested || IsFusing) return;

                // ra trận
                if (PlayerDetector.IsPlayerVisible(hwnd, playerImg))
                {
                    if (caughtSomething)
                    {
                        CatchCount++;
                        log($"✅ Thoát combat → +1 Count = {CatchCount}");

                        if (CatchCount >= Form1.Instance.FusionThreshold)
                        {
                            log($"⚡ Count đạt limit ({CatchCount}) → tiến hành Dung Hợp!");
                            CatchCount = 0;
                            FusionProcess(hwnd, Form1.Instance.WantedPetsOrigin, log, token);
                        }
                    }
                    return;
                }

                // timeout
                if (sw.Elapsed.TotalSeconds > 15)
                {
                    log("⏱ Quá 15s → bỏ chạy");
                    ForceExitCombat(hwnd, boChayBtn, coBtn, playerImg, log, token);

                    // ↙ kiểm tra lại player
                    if (PlayerDetector.IsPlayerVisible(hwnd, playerImg) && caughtSomething)
                    {
                        CatchCount++;
                        log($"✅ Thoát combat (timeout) → +1 Count = {CatchCount}");

                        if (CatchCount >= Form1.Instance.FusionThreshold)
                        {
                            log($"⚡ Count đạt limit ({CatchCount}) → tiến hành Dung Hợp!");
                            CatchCount = 0;
                            FusionProcess(hwnd, wantedPets, log, token);
                        }
                    }

                }

                // gửi G
                // gửi G để mở popup mục tiêu
                // chỉ G nếu còn trong combat
                if (!PlayerDetector.IsPlayerVisible(hwnd, playerImg))
                {
                    log(" Gửi G (vẫn đang combat)");
                    Form1.PressKey((int)'G', hwnd);
                    Thread.Sleep(500);
                }
                else
                {
                    log(" Đã thấy player → thoát combat loop");
                    if (caughtSomething)
                    {
                        CatchCount++;
                        log($" Thoát combat  Count = {CatchCount}");

                        if (CatchCount >= Form1.Instance.FusionThreshold)
                        {
                            log(" Count đạt limit → Fusion");
                            CatchCount = 0;
                            FusionProcess(hwnd, wantedPets, log, token);
                        }
                    }
                    return;
                }


                var popupImg = Path.Combine(Path.GetDirectoryName(batBtn)!, "ChonMucTieu.png");
                bool popupOpened = ImageHelper.IsPopupVisible(hwnd, popupImg, TH_BTN);
                if (!popupOpened) continue;   // chưa mở được popup → thử lại vòng while

                Thread.Sleep(SLEEP_SHORT);

                // ============== QUÉT POPUP HIỆN TẠI ==============
                // CHỤP FRAME MỚI MỖI LẦN QUÉT
                using var frame = ImageHelper.CaptureWindow(hwnd);

                bool foundThisPopup = false;

                foreach (var img in wantedPets)
                {
                    if (token.IsCancellationRequested) return;

                    using var tpl = (Bitmap)Image.FromFile(img);
                    var (pt, score) = ImageHelper.MatchMultiScale(frame, tpl, 0.8);

                    log($" check {Path.GetFileNameWithoutExtension(img)} score={score:F2}");

                    if (pt.HasValue && score >= 0.8)
                    {
                        string name = Path.GetFileNameWithoutExtension(img);
                        log($" Bắt pet: {name}");

                        ImageHelper.ClickClient(hwnd, pt.Value.X, pt.Value.Y, log);
                        Thread.Sleep(120);
                        ImageHelper.ClickClient(hwnd, pt.Value.X, pt.Value.Y, log);
                        Thread.Sleep(250);
                        caughtSomething = true;

                        foundThisPopup = true;

                        // mở lại popup → cần chụp lại frame ở vòng lặp tiếp theo
                        Form1.PressKey((int)'G', hwnd);
                        Thread.Sleep(350);
                        break;  // BREAK để sang vòng while → chụp frame mới
                    }
                }



                // Không bắt được con nào TRONG popup hiện tại → BỎ CHẠY
                if (!foundThisPopup)
                {
                    log("⚠ Popup không còn pet phù hợp → bỏ chạy");
                    ForceExitCombat(hwnd, boChayBtn, coBtn, playerImg, log, token);

                    // Nếu đã về ngoài trận, +Count nếu trong TRẬN này đã từng bắt >=1 con
                    if (PlayerDetector.IsPlayerVisible(hwnd, playerImg) && caughtSomething)
                    {
                        CatchCount++;
                        log($" Thoát combat  → +1 Count = {CatchCount}");

                        if (CatchCount >= Form1.Instance.FusionThreshold)
                        {
                            log($" Count đạt limit ({CatchCount}) → tiến hành Dung Hợp!");
                            CatchCount = 0;
                            FusionProcess(hwnd, wantedPets, log, token);
                        }
                    }
                    return; // kết thúc trận này
                }

            NEXT_LOOP:
                continue;
            }

        }


        // =======================
        // EXIT COMBAT
        // =======================
        private static void ForceExitCombat(
            IntPtr hwnd,
            string boChayBtn,
            string coBtn,
            string playerImg,
            Action<string> log,
            CancellationToken token)
        {
            int retry = 0;
            while (!PlayerDetector.IsPlayerVisible(hwnd, playerImg) && retry < 5)
            {
                if (token.IsCancellationRequested) return;

                log($"➡ Thử thoát trận {retry + 1}");

                // đóng popup
                ImageHelper.ClickClient(hwnd, 500, 300, log);
                Thread.Sleep(200);

                // click bỏ chạy
                if (!ImageHelper.ClickImage(hwnd, boChayBtn, TH_BTN, log))
                {
                    log(" Không thấy Bỏ chạy → fallback");
                    ImageHelper.ClickClient(hwnd, 826, 328, log);
                }
                Thread.Sleep(350);

                ImageHelper.ClickImage(hwnd, coBtn, TH_BTN, log);
                Thread.Sleep(500);

                retry++;
            }

            if (PlayerDetector.WaitAppear(hwnd, playerImg, log))
                log(" Thoát combat!");
            else
                log(" Thoát combat fail");
        }



        // ==================================================================
        // ============   TAB SELECT HELPERS   ==============================
        // ==================================================================

        private static bool ClickTabLuc(
     IntPtr hwnd,
     string lucImg,
     Action<string> log)
        {
            using var frame = ImageHelper.CaptureWindow(hwnd);
            using var tpl = (Bitmap)Image.FromFile(lucImg);

            var (pt, score) = ImageHelper.MatchMultiScale(frame, tpl, TH_BTN);

            if (pt.HasValue && score >= TH_BTN)
            {
                log($" Click TAB LỤC bằng ảnh ({pt.Value.X},{pt.Value.Y}) score={score:F2}");
                ImageHelper.ClickClient(hwnd, pt.Value.X, pt.Value.Y, log);
                Thread.Sleep(200);
                return true;
            }

            // fallback
            var pos = (x: 821, y: 136);
            log($" Không match Luc.png → fallback TAB LỤC @({pos.x},{pos.y}) score={score:F2}");

            ImageHelper.ClickClient(hwnd, pos.x, pos.y, log);
            Thread.Sleep(200);
            return true;
        }


        private static void ClickTabLam(
      IntPtr hwnd,
      Action<string> log)
        {
            var lam = (x: 790, y: 136);
            log($" Click Tab LAM @({lam.x},{lam.y})");
            ImageHelper.ClickClient(hwnd, lam.x, lam.y, log);
            Thread.Sleep(500);
        }




        // ==================================================================
        // ============   FUSION PROCESS   ==================================
        // ==================================================================
        private static void FusionProcess(
       IntPtr hwnd,
       IReadOnlyList<string> wantedPets,
       Action<string> log,
       CancellationToken token)
        {
            if (IsFusing) return;
            IsFusing = true;

            try
            {
                log("⚡ Bắt đầu dung hợp...");

                if (!EnsureOutOfCombat(hwnd, Form1.CurrentPlayerAvatar, log, token))
                {
                    log("❌ Không thoát combat → hủy");
                    return;
                }

                if (!TryOpenFusionUI(hwnd, log, token))
                {
                    log("❌ Không mở được UI luyện pet → hủy");
                    return;
                }

                ClickDungButton(hwnd, log);
                Thread.Sleep(1000);

                OpenBagOnce(hwnd, log);
                Thread.Sleep(800);

                // ============================================================
                // 1) LỤC FIRST — LOOP QUA HẾT TEMPLATE
                // ============================================================
                log("🔵 Bắt đầu LỤC FIRST...");
                bool lucHasFusion = TryFusion_LucDeepLoop(hwnd, wantedPets, log, token);

                log($"LỤC kết thúc (HasFusion={lucHasFusion})");

                // ============================================================
                // 2) LAM FIRST — LOOP QUA HẾT TEMPLATE
                // ============================================================
                log("🟣 Chuyển sang LAM...");

                bool lamHasFusion = TryFusion_LamDeepLoop(hwnd, wantedPets, log, token);

                if (lamHasFusion)
                {
                    log("✨ Fusion thành công tại LAM → reset UI");
                    Form1.PressKey((int)'V', hwnd);
                    Thread.Sleep(700);
                    TryOpenFusionUI(hwnd, log, token);
                    return;
                }

                log("❌ TAB LAM cũng không đủ → STOP FUSION");
            }
            finally
            {
                IsFusing = false;
            }
        }


        private static bool TryFusion_LamLoop(
    IntPtr hwnd,
    IReadOnlyList<string> wantedPets,
    Action<string> log,
    CancellationToken token)
        {
            bool hasFusion = false;

            foreach (var petImg in wantedPets)
            {
                if (token.IsCancellationRequested) return hasFusion;

                string petName = Path.GetFileNameWithoutExtension(petImg);

                // RESET UI
                Form1.PressKey((int)'V', hwnd);
                Thread.Sleep(400);
                if (!TryOpenFusionUI(hwnd, log, token)) return hasFusion;

                ClickDungButton(hwnd, log);
                Thread.Sleep(300);
                OpenBagOnce(hwnd, log);
                Thread.Sleep(300);

                ClickTabLam(hwnd, log);
                Thread.Sleep(400);

                int selected = 0;

                // ==== PAGE 1 ====
                log($"🔍 LAM SCAN PAGE 1 — TEMPLATE: {petName}");
                selected += CountPetInPage_Lam(hwnd, petImg, log, token);

                if (selected >= 5)
                {
                    ClickHopConfirm(hwnd, log);
                    hasFusion = true;
                    log($"✨ Fusion LAM thành công template {petName}");
                    continue;
                }

                // ==== PAGE 2 ====
                log("➡ Không đủ → thử PAGE 2");

                Form1.PressKey((int)'V', hwnd);
                Thread.Sleep(400);
                if (!TryOpenFusionUI(hwnd, log, token)) return hasFusion;

                ClickDungButton(hwnd, log);
                Thread.Sleep(300);
                OpenBagOnce(hwnd, log);
                Thread.Sleep(300);

                ClickTabLam(hwnd, log);
                Thread.Sleep(400);

                ImageHelper.ClickClient(hwnd, 829, 250, log); // next page
                Thread.Sleep(800);

                log($"🔍 LAM SCAN PAGE 2 — TEMPLATE: {petName}");
                selected += CountPetInPage_Lam(hwnd, petImg, log, token);

                if (selected >= 5)
                {
                    ClickHopConfirm(hwnd, log);
                    hasFusion = true;
                    log($"✨ Fusion LAM thành công template {petName}");
                    continue;
                }

                log($"❌ LAM không đủ template {petName}");
            }

            return hasFusion;
        }



        private static bool TryFusion_LucDeepLoop(
      IntPtr hwnd,
      IReadOnlyList<string> wantedPets,
      Action<string> log,
      CancellationToken token)
        {
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            string lucImg = Path.Combine(assets, "Luc.png");

            bool hasFusionAtLeastOnce = false;   // KẾT QUẢ CUỐI CÙNG

            foreach (var petImg in wantedPets)
            {
                string petName = Path.GetFileNameWithoutExtension(petImg);
                bool fusedThisTemplate = false;   // RESET MỖI TEMPLATE

                while (true)
                {
                    if (token.IsCancellationRequested)
                        return hasFusionAtLeastOnce;

                    // ===== RESET UI =====
                    Form1.PressKey((int)'V', hwnd);
                    Thread.Sleep(500);
                    if (!TryOpenFusionUI(hwnd, log, token))
                        return hasFusionAtLeastOnce;

                    ClickDungButton(hwnd, log);
                    Thread.Sleep(300);
                    OpenBagOnce(hwnd, log);
                    Thread.Sleep(300);

                    ClickTabLuc(hwnd, lucImg, log);
                    Thread.Sleep(300);

                    int selected = 0;

                    // ===== PAGE 1 =====
                    log($"🔍 LỤC PAGE 1 — TEMPLATE: {petName}");
                    selected += CountPetInPage(hwnd, petImg, log, token);

                    if (selected >= 5)
                    {
                        ClickHopConfirm(hwnd, log);
                        fusedThisTemplate = true;
                        hasFusionAtLeastOnce = true;

                        log($"✨ Fusion LỤC {petName} (Page1)");
                        Thread.Sleep(800);
                        continue; // QUAY LẠI CHẠY TIẾP TEMPLATE NÀY
                    }

                    // ===== PAGE 2 =====
                    log("➡ Không đủ → sang PAGE 2");

                    // Kiểm tra PAGE 2 có tồn tại không
                    using var before = ImageHelper.CaptureWindow(hwnd);
                    ImageHelper.ClickClient(hwnd, 829, 250, log);
                    Thread.Sleep(600);
                    using var after = ImageHelper.CaptureWindow(hwnd);

                    // so sánh 1 vùng nhỏ để xem có đổi trang không
                    var rect = new Rectangle(684 - 20, 165 - 20, 40, 40);
                    using var cropBefore = before.Clone(rect, before.PixelFormat);
                    using var cropAfter = after.Clone(rect, after.PixelFormat);

                    double diff = 0;
                    for (int y = 0; y < cropBefore.Height; y += 5)
                        for (int x = 0; x < cropBefore.Width; x += 5)
                        {
                            var p1 = cropBefore.GetPixel(x, y);
                            var p2 = cropAfter.GetPixel(x, y);
                            diff += Math.Abs(p1.R - p2.R) +
                                    Math.Abs(p1.G - p2.G) +
                                    Math.Abs(p1.B - p2.B);
                        }

                    if (diff < 5000)
                    {
                        log("❌ Không có PAGE 2 → bỏ qua PAGE 2");
                        break;  // KẾT THÚC TEMPLATE NÀY → QUA TEMPLATE KHÁC
                    }

                    log($"🔍 LỤC PAGE 2 — TEMPLATE: {petName}");
                    selected += CountPetInPage(hwnd, petImg, log, token);

                    if (selected >= 5)
                    {
                        ClickHopConfirm(hwnd, log);
                        fusedThisTemplate = true;
                        hasFusionAtLeastOnce = true;

                        log($"✨ Fusion LỤC {petName} (Page2)");
                        Thread.Sleep(800);
                        continue; // LÀM TIẾP CHÍNH TEMPLATE NÀY
                    }

                    log($"❌ LỤC không đủ template {petName} → qua template khác");
                    break; // KẾT THÚC TEMPLATE NÀY → QUA TEMPLATE MỚI
                }
            }

            return hasFusionAtLeastOnce;
        }

        private static bool TryFusion_LamDeepLoop(
            IntPtr hwnd,
            IReadOnlyList<string> wantedPets,
            Action<string> log,
            CancellationToken token)
        {
            bool hasFusion = false;

            foreach (var petImg in wantedPets)
            {
                string petName = Path.GetFileNameWithoutExtension(petImg);

                while (true) // ⭐ LOOP sâu: còn đủ ≥5 thì fusion liên tục
                {
                    if (token.IsCancellationRequested) return hasFusion;

                    // RESET UI
                    Form1.PressKey((int)'V', hwnd);
                    Thread.Sleep(400);
                    if (!TryOpenFusionUI(hwnd, log, token)) return hasFusion;

                    ClickDungButton(hwnd, log);
                    Thread.Sleep(300);
                    OpenBagOnce(hwnd, log);
                    Thread.Sleep(300);

                    ClickTabLam(hwnd, log);
                    Thread.Sleep(400);

                    int selected = 0;

                    // PAGE 1
                    log($"🔍 LAM PAGE 1 — TEMPLATE: {petName}");
                    selected += CountPetInPage_Lam(hwnd, petImg, log, token);

                    if (selected >= 5)
                    {
                        ClickHopConfirm(hwnd, log);
                        hasFusion = true;
                        log($"✨ Fusion LAM {petName} (Page1)");
                        Thread.Sleep(800);
                        continue; // 🔁 quay lại chính template này
                    }

                    // PAGE 2
                    log("➡ Không đủ → sang PAGE 2");



                    // LẤY FRAME TRƯỚC KHI NEXT
                    using var frameBefore = ImageHelper.CaptureWindow(hwnd);

                    // CLICK NEXT
                    ImageHelper.ClickClient(hwnd, 829, 250, log);
                    Thread.Sleep(600);

                    // LẤY FRAME SAU KHI NEXT
                    using var frameAfter = ImageHelper.CaptureWindow(hwnd);

                    // SO SÁNH 1 VÙNG NHỎ Ở SLOT 1 (không thêm hàm mới)
                    var rect = new Rectangle(684 - 20, 165 - 20, 40, 40); // slot đầu tiên LAM
                    using var cropBefore = frameBefore.Clone(rect, frameBefore.PixelFormat);
                    using var cropAfter = frameAfter.Clone(rect, frameAfter.PixelFormat);

                    double diff = 0;
                    for (int y = 0; y < cropBefore.Height; y += 5)
                        for (int x = 0; x < cropBefore.Width; x += 5)
                        {
                            var p1 = cropBefore.GetPixel(x, y);
                            var p2 = cropAfter.GetPixel(x, y);
                            diff += Math.Abs(p1.R - p2.R) +
                                    Math.Abs(p1.G - p2.G) +
                                    Math.Abs(p1.B - p2.B);
                        }

                    // Nếu KHÔNG thay đổi → tức là không có page 2
                    if (diff < 1000)
                    {
                        log("❌ Không có PAGE 2 → bỏ qua PAGE 2");
                        break; // → template kế
                    }

                        log($"🔍 LAM PAGE 2 — TEMPLATE: {petName}");
                    selected += CountPetInPage_Lam(hwnd, petImg, log, token);

                    if (selected >= 5)
                    {
                        ClickHopConfirm(hwnd, log);
                        hasFusion = true;
                        log($"✨ Fusion LAM {petName} (Page2)");
                        Thread.Sleep(800);
                        continue; // 🔁 quay lại chính template này
                    }

                    log($"❌ LAM không đủ template {petName} → qua template khác");
                    break; // ❌ qua template khác
                }
            }

            return hasFusion;
        }




        // ==================================================================
        // TRY OPEN UI
        // ==================================================================
        private static bool TryOpenFusionUI(
      IntPtr hwnd,
      Action<string> log,
      CancellationToken token)
        {
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            string uiImg = Path.Combine(assets, "LuyenPet.png");

            var fallback = (x: 862, y: 223);
            int retry = 0;
            const int MAX_RETRY = 8;

            while (retry < MAX_RETRY && !token.IsCancellationRequested)
            {
                retry++;

                // --- thử bấm V ---
                Form1.PressKey((int)'V', hwnd);
                Thread.Sleep(900);

                if (IsFusionOpened(hwnd, uiImg))
                {
                    log($" UI Luyện pet đã mở (retry={retry})");
                    return true;
                }
                // sau khi bấm V — check xem có lỡ vào combat không
                if (!PlayerDetector.IsPlayerVisible(hwnd, Form1.CurrentPlayerAvatar))
                {
                    log(" Vừa vào combat → Force exit rồi mở UI lại");

                    ForceExitCombat(
                        hwnd,
                        Path.Combine(assets, "Pet", "BoChay.png"),
                        Path.Combine(assets, "Pet", "Co.png"),
                        Form1.CurrentPlayerAvatar,
                        log,
                        token);

                    Thread.Sleep(800);

                    // thử mở lại UI
                    Form1.PressKey((int)'V', hwnd);
                    Thread.Sleep(900);

                    if (IsFusionOpened(hwnd, uiImg))
                    {
                        log(" UI mở lại sau khi thoát combat");
                        return true;
                    }

                    // nếu chưa mở được → tiếp tục retry
                    continue;
                }


                // --- fallback click icon ---
                log($" V chưa mở → fallback click @({fallback.x},{fallback.y})");
                ImageHelper.ClickClient(hwnd, fallback.x, fallback.y, log);
                Thread.Sleep(900);

                if (IsFusionOpened(hwnd, uiImg))
                {
                    log($" UI Luyện pet đã mở sau fallback (retry={retry})");
                    return true;
                }

                // ========== CHECK AVATAR ==========
                if (!PlayerDetector.IsPlayerVisible(hwnd, Form1.CurrentPlayerAvatar))
                {
                    log(" Không thấy avatar → có thể đang combat → xử lý...");

                    ForceExitCombat(
                        hwnd,
                        Path.Combine(assets, "Pet", "BoChay.png"),
                        Path.Combine(assets, "Pet", "Co.png"),
                        Form1.CurrentPlayerAvatar,
                        log,
                        token);

                    Thread.Sleep(900);

                    // thử mở lại UI ngay
                    Form1.PressKey((int)'V', hwnd);
                    Thread.Sleep(900);

                    if (IsFusionOpened(hwnd, uiImg))
                    {
                        log($" UI mở lại sau khi xử combat (retry={retry})");
                        return true;
                    }
                }
            }

            log(" Không mở được UI Luyện pet sau tất cả retry → FAIL");
            return false;
        }
        private static bool ClickDungButton(IntPtr hwnd, Action<string> log)
        {
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            string dungImg = Path.Combine(assets, "Dung.png");

            // thử match ảnh Dung.png
            if (ImageHelper.ClickImage(hwnd, dungImg, TH_BTN, log))
            {
                log("✅ Click nút DUNG bằng ảnh");
                Thread.Sleep(600);
                return true;
            }

            // fallback nếu không tìm thấy
            var fallback = (x: 299, y: 143);   // toạ độ bạn từng log
            log($"⚠ Không match Dung.png → fallback @({fallback.x},{fallback.y})");
            ImageHelper.ClickClient(hwnd, fallback.x, fallback.y, log);
            Thread.Sleep(600);
            return true;
        }

        static bool IsFusionOpened(IntPtr hwnd, string img)
            => ImageHelper.IsPopupVisible(hwnd, img, TH_BTN);



        // ==================================================================
        // chọn 5 pet + click dung hợp
        // ==================================================================
        // =========================
        // SELECT 5 PET BY SLOT + IMAGE
        // =========================
        // ─── PET SLOT: TAB LỤC ─────────────────────────────────
        private static readonly (int x, int y)[] PetSlotsLuc = new[]
        {
    (685,167),(723,167),(756,163),(798,164),(832,164),(867,161),
    (688,201),(714,202),(754,209),(797,206),(831,210),(864,207),
};

        // ─── PET SLOT: TAB LAM ─────────────────────────────────
        private static readonly (int x, int y)[] PetSlotsLam = new[]
  {
    (684,165),(717,167),(752,167),(793,170),(838,170),(864,168),
    (685,204),(716,203),(747,203),(794,199),(822,199),(869,204),
};





     

        public static Bitmap Crop(Bitmap src, Rectangle rect)
        {
            // clamp rectangle
            rect = Rectangle.Intersect(rect, new Rectangle(0, 0, src.Width, src.Height));

            if (rect.Width <= 0 || rect.Height <= 0)
                return new Bitmap(1, 1);

            return src.Clone(rect, src.PixelFormat);
        }
        private static void OpenBagOnce(IntPtr hwnd, Action<string> log)
        {
            if (bagOpened) return;

            var pos = (x: 647, y: 263);
            log(" Mở túi lần đầu @ (647,263)");
            ImageHelper.ClickClient(hwnd, pos.x, pos.y, log);
            Thread.Sleep(600);

            bagOpened = true;
        }
   

        private static void ClickHopConfirm(IntPtr hwnd, Action<string> log)
        {
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            string hop = Path.Combine(assets, "Hop.png");
            string co = Path.Combine(assets, "Co.png");

            if (!ImageHelper.ClickImage(hwnd, hop, TH_BTN, log))
                ImageHelper.ClickClient(hwnd, 410, 400, log);

            Thread.Sleep(1000);
            ImageHelper.ClickImage(hwnd, co, TH_BTN, log);
            Thread.Sleep(1000);
        }


      

      

     
       

        private static int CountPetInPage(
      IntPtr hwnd,
      string petImg,
      Action<string> log,
      CancellationToken token)
        {
            int found = 0;

            // Load template 1 lần
            using var tpl = (Bitmap)Image.FromFile(petImg);

            // Capture cả 12 slot 1 lần duy nhất
            using var frame = ImageHelper.CaptureWindow(hwnd);

            foreach (var (cx, cy) in PetSlotsLuc)
            {
                if (token.IsCancellationRequested) break;

                // Crop từ HÌNH FRAME duy nhất
                var rect = new Rectangle(cx - 32, cy - 32, 70, 70);
                using var crop = Crop(frame, rect);

                var (_, score) = ImageHelper.MatchOnce(crop, tpl, TH_PET);

                if (score < TH_PET)
                    continue;

                log($" Lấy PET slot ({cx},{cy}) score={score:F2}");

                // CLICK SLOT
                ImageHelper.ClickClient(hwnd, cx, cy, log);
                Thread.Sleep(120);
                ImageHelper.ClickClient(hwnd, cx, cy, log);
                Thread.Sleep(120);

                found++;
            }

            return found;
        }

        private static int CountPetInPage_Lam(
    IntPtr hwnd,
    string petImg,
    Action<string> log,
    CancellationToken token)
        {
            int found = 0;

            using var tpl = (Bitmap)Image.FromFile(petImg);
            using var frame = ImageHelper.CaptureWindow(hwnd);  // CAPTURE 1 LẦN

            foreach (var (cx, cy) in PetSlotsLam)
            {
                if (token.IsCancellationRequested) break;

                var rect = new Rectangle(cx - 32, cy - 32, 70, 70);
                using var crop = Crop(frame, rect);

                var (_, score) = ImageHelper.MatchOnce(crop, tpl, TH_PET);

                if (score < TH_PET)
                    continue;

                log($" Lấy PET (LAM) slot ({cx},{cy}) score={score:F2}");

                // CLICK SLOT
                ImageHelper.ClickClient(hwnd, cx, cy, log);
                Thread.Sleep(120);
                ImageHelper.ClickClient(hwnd, cx, cy, log);
                Thread.Sleep(120);

                found++;
            }

            return found;
        }



    }



}
