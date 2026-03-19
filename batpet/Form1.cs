using batpet.Auto;
using batpet.Model;
using LeoThap;
using Microsoft.VisualBasic.Devices;
using Microsoft.VisualBasic.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace batpet
{
    public partial class Form1 : Form
    {
        CancellationTokenSource? _cts;
        IntPtr _hwnd = IntPtr.Zero;
        static bool isDragging = false;
        static (int x, int y) dragStart;
        // ===== MINI PICK VARIABLES =====
        const int HOTKEY_ID_1 = 0xA001;
        const int HOTKEY_ID_2 = 0xA002;
        const int HOTKEY_ID_3 = 0xA003;
        static Form1 _instance;

        static bool isPickingBackup = false;
        public static Form1 Instance => _instance;
        static bool isPickingMini1 = false;
        static bool isPickingMini2 = false;
        HashSet<string> _checkedPets = new();
        TextBox txtFusionThreshold;
        public List<string> WantedPetsOrigin { get; private set; } = new();

        public Form1()
        {
            _instance = this;
            InitializeComponent();
            Load += async (s, e) =>
            {
                ToggleUI(false);
                Append("⏳ Đang kiểm tra bản quyền...");
                bool isValid = await CheckLicenseAsync();

                if (!isValid)
                {
                    string myHwid = HWIDHelper.GetHWID();

                    // Tự tạo một Form cảnh báo chuyên nghiệp
                    Form alert = new Form()
                    {
                        Text = "Cảnh báo Bản Quyền",
                        Size = new Size(420, 220),
                        StartPosition = FormStartPosition.CenterScreen,
                        FormBorderStyle = FormBorderStyle.FixedDialog,
                        MaximizeBox = false,
                        MinimizeBox = false,
                        TopMost = true // Luôn nổi lên trên
                    };

                    Label lbl = new Label()
                    {
                        Text = "Máy của bạn chưa được cấp phép sử dụng Tool này.\n\nVui lòng copy mã bên dưới và gửi cho Admin để kích hoạt:",
                        Location = new Point(20, 20),
                        AutoSize = true,
                        Font = new Font("Segoe UI", 9, FontStyle.Regular)
                    };

                    TextBox txtHwid = new TextBox()
                    {
                        Text = myHwid,
                        Location = new Point(20, 80),
                        Width = 360,
                        ReadOnly = true, // Không cho sửa, chỉ cho copy
                        Font = new Font("Consolas", 10, FontStyle.Bold)
                    };

                    Button btnCopy = new Button()
                    {
                        Text = "📋 Copy Mã Máy",
                        Location = new Point(130, 120),
                        Size = new Size(140, 35),
                        Font = new Font("Segoe UI", 9, FontStyle.Bold),
                        Cursor = Cursors.Hand
                    };

                    // Sự kiện khi bấm nút Copy
                    btnCopy.Click += (senderObj, args) =>
                    {
                        Clipboard.SetText(myHwid);
                        MessageBox.Show("✅ Đã copy mã vào bộ nhớ tạm!\nBây giờ bạn có thể dán ",
                                        "Thành công", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    };

                    alert.Controls.Add(lbl);
                    alert.Controls.Add(txtHwid);
                    alert.Controls.Add(btnCopy);

                    // Hiện Form và dừng code tại đây cho đến khi user tắt Form
                    alert.ShowDialog();

                    // Đóng tool sau khi tắt hộp thoại
                    Environment.Exit(0);
                    return;
                }

                // Đã hợp lệ -> Mở tool
                LoadWindows();
                txtAssetsDir.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                Append("✅ Tool đã sẵn sàng");
                ToggleUI(true); // Mở khóa giao diện
                LoadWindows();
                txtAssetsDir.Text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                LoadPetList();
                LoadConfigList();
                Append("Ready.");
            };
            RegisterHotKey(this.Handle, HOTKEY_ID_1, 0, (int)Keys.F1);
            RegisterHotKey(this.Handle, HOTKEY_ID_2, 0, (int)Keys.F2);
            RegisterHotKey(this.Handle, HOTKEY_ID_3, 0, (int)Keys.F3);
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        public static void PressKey(int vk, IntPtr hwnd)
        {
            const int WM_KEYDOWN = 0x0100;
            const int WM_KEYUP = 0x0101;

            PostMessage(hwnd, WM_KEYDOWN, vk, 0);
            Thread.Sleep(30);
            PostMessage(hwnd, WM_KEYUP, vk, 0);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x;
            public int y;
        }
        void btnPickMini1_Click(object? sender, EventArgs e)
        {
            Append("🔥 Kéo-thả lên map để chọn Mini #1");
            isPickingMini1 = true;
            isPickingMini2 = false;
        }
        void btnPickMini2_Click(object? sender, EventArgs e)
        {
            Append("🔥 Kéo-thả lên map để chọn Mini #2");
            isPickingMini1 = false;
            isPickingMini2 = true;
        }
        public static string CurrentPlayerAvatar = "";
        void LoadPetList()
        {
            var dir = Path.Combine(txtAssetsDir.Text, "Pet");
            if (!Directory.Exists(dir)) return;

            // lưu trạng thái
            SyncCheckedPets();

            chkPets.Items.Clear();

            var allPets = Directory.GetFiles(dir, "*.png")
                .Where(f => !Path.GetFileName(f).StartsWith("player_", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).Contains("BatPet", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).Contains("Auto", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).Contains("BoChay", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetFileNameWithoutExtension(f))
                .ToList();

            foreach (var path in allPets)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                bool isChecked = _checkedPets.Contains(name);
                chkPets.Items.Add(name, isChecked);
            }

            Append($"🐾 Đã nạp {allPets.Count} ảnh pet.");
        }
        void SyncCheckedPets()
        {
            for (int i = 0; i < chkPets.Items.Count; i++)
            {
                var name = chkPets.Items[i].ToString();
                if (chkPets.GetItemChecked(i))
                    _checkedPets.Add(name!);
                else
                    _checkedPets.Remove(name!);
            }
        }

        void FilterPetList(string keyword)
        {
            keyword = keyword.Trim().ToLower();

            SyncCheckedPets();

            chkPets.Items.Clear();

            var dir = Path.Combine(txtAssetsDir.Text, "Pet");
            if (!Directory.Exists(dir)) return;

            var allPets = Directory.GetFiles(dir, "*.png")
                .Where(f => !Path.GetFileName(f).StartsWith("player_", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).Contains("BatPet", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).Contains("Auto", StringComparison.OrdinalIgnoreCase)
                         && !Path.GetFileName(f).Contains("BoChay", StringComparison.OrdinalIgnoreCase))
                .Where(f => string.IsNullOrWhiteSpace(keyword) ||
                            Path.GetFileNameWithoutExtension(f).ToLower().Contains(keyword))
                .OrderBy(f => Path.GetFileNameWithoutExtension(f))
                .ToList();

            foreach (var path in allPets)
            {
                var name = Path.GetFileNameWithoutExtension(path);
                bool isChecked = _checkedPets.Contains(name);
                chkPets.Items.Add(name, isChecked);
            }
        }


        void LoadWindows()
        {
            cboWindows.Items.Clear();
            foreach (var p in Process.GetProcesses().OrderBy(p => p.ProcessName))
            {
                try
                {
                    string name = p.ProcessName.ToLower();
                    string title = p.MainWindowTitle?.Trim() ?? "";

                    // Lọc các process có thể là game Flash
                    if (name.Contains("flash") || title.Contains("flash", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("dy") || title.Contains("dy", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("magic") || title.Contains("magic", StringComparison.OrdinalIgnoreCase))
                    {
                        string display = !string.IsNullOrWhiteSpace(title)
                            ? $"{title}  ({p.Id})"
                            : $"{name}  ({p.Id})";   // nếu không có tiêu đề thì hiện theo tên process
                        cboWindows.Items.Add(display);
                    }
                }
                catch { }
            }

            if (cboWindows.Items.Count > 0)
                cboWindows.SelectedIndex = 0;

            Append($"🔍 Đã tìm thấy {cboWindows.Items.Count} tiến trình có thể là Flash/Game.");
        }


     
        string BatBtn => Path.Combine(txtAssetsDir.Text,  "BatPet.png");
        string BoChayBtn => Path.Combine(txtAssetsDir.Text, "Pet", "BoChay.png");          // bạn sẽ thêm file này
        (int x, int y) ConfirmRun => (620, 390); // điền tọa độ confirm của bạn

        List<string> LoadWantedPets()
        {
            var dir = Path.Combine(txtAssetsDir.Text, "Pet");
            var result = new List<string>();

            foreach (var item in chkPets.CheckedItems)
            {
                string file = Path.Combine(dir, $"{item}.png");
                if (File.Exists(file))
                    result.Add(file);
            }

            return result;
        }


        void btnStart_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtMini1X.Text) ||
    string.IsNullOrWhiteSpace(txtMini1Y.Text) ||
    string.IsNullOrWhiteSpace(txtMini2X.Text) ||
    string.IsNullOrWhiteSpace(txtMini2Y.Text))
            {
                MessageBox.Show("⚠️ Bạn chưa chọn đủ tọa độ Mini #1 và Mini #2!", "Thiếu tọa độ");
                return;
            }
            var title = cboWindows.Text.Trim();

          
            int pidIndex = title.IndexOf(" (");
            if (pidIndex > 0)
                title = title[..pidIndex].Trim();

            // 🔍 1️⃣ Tìm cửa sổ cha
            _hwnd = FindWindow(null, title);
            if (_hwnd == IntPtr.Zero)
            {
                MessageBox.Show($"Không tìm thấy cửa sổ: \"{title}\"", "Lỗi");
                return;
            }
            Append($"🔍 Main window: 0x{_hwnd.ToInt64():X}");

            // 🔍 2️⃣ Tìm cửa sổ con chứa Flash thật
            IntPtr flashHwnd = FindDeepFlashWindow(_hwnd);
            if (flashHwnd != IntPtr.Zero)
            {
                _hwnd = flashHwnd;
                Append($"✅ Flash window found: 0x{_hwnd.ToInt64():X}");
            }
            else
            {
                Append("⚠️ Không tìm thấy Flash child window — dùng handle cha.");
            }

            // 🔍 3️⃣ Kiểm tra file ảnh
            CurrentPlayerAvatar = DetectPlayerAvatar(_hwnd, txtAssetsDir.Text, 0.85, Append);
            if (string.IsNullOrEmpty(CurrentPlayerAvatar))
            {
                MessageBox.Show("Không phát hiện được nhân vật nào. Vui lòng để nhân vật hiển thị rõ trong khung.", "Lỗi nhận diện");
                return;
            }
            Append($"✅ Đã chọn ảnh nhân vật: {Path.GetFileName(CurrentPlayerAvatar)}");
            if (!File.Exists(BatBtn)) { MessageBox.Show("Thiếu BatPet.png"); return; }

            WantedPetsOrigin = LoadWantedPets();
            if (WantedPetsOrigin.Count == 0) { MessageBox.Show("Chưa chọn Pet"); return; }

            // 🔹 4️⃣ Khởi động loop
            ToggleUI(false);
            _cts = new CancellationTokenSource();
            Task.Run(() => LoopCatch(_cts.Token));
        }



        void btnStop_Click(object sender, EventArgs e) { _cts?.Cancel(); ToggleUI(true); }

        void LoopCatch(CancellationToken tk)
        {
            Append("🐾 Bắt đầu…");
            var autoBtn = Path.Combine(txtAssetsDir.Text, "AutoInGame.png");
            var coBtn = Path.Combine(txtAssetsDir.Text, "Pet", "Co.png");

            while (!tk.IsCancellationRequested)
            {
                // nếu đang ngoài trận → đi qua lại 2 điểm cho spawn
                if (PlayerDetector.IsPlayerVisible(_hwnd, CurrentPlayerAvatar))
                {
                    var p1 = (int.Parse(txtMini1X.Text), int.Parse(txtMini1Y.Text));
                    var p2 = (int.Parse(txtMini2X.Text), int.Parse(txtMini2Y.Text));

                    MoveMini(p1);
                    MoveMini(p2);

                    if (!PlayerDetector.IsPlayerVisible(_hwnd, CurrentPlayerAvatar))
                        AutoPetController.HandleCombat(
     _hwnd,
     CurrentPlayerAvatar,
     BatBtn,
     BoChayBtn,
     coBtn,
     WantedPetsOrigin,
     Append,
     tk
 );

                }
                else
                {
                    AutoPetController.HandleCombat(
     _hwnd,
     CurrentPlayerAvatar,
     BatBtn,
     BoChayBtn,
     coBtn,
    WantedPetsOrigin,
     Append,
     tk
 );

                }

                Thread.Sleep(500);
            }

            Append("🛑 Dừng.");
        }


        void MoveMini((int x, int y) p)
        {
            // mở mini map
            SendTilde();
            Thread.Sleep(200);

            // click target chính
            ImageHelper.ClickClient(_hwnd, p.x, p.y, Append);
            Append($"➡️ Move mini → client({p.x},{p.y})");
            Thread.Sleep(800);

            // click backup
            if (int.TryParse(txtBackupX.Text, out int bx) &&
                int.TryParse(txtBackupY.Text, out int by))
            {
                ImageHelper.ClickClient(_hwnd, bx, by, Append);
                Append($" Backup → client({bx},{by})");
                Thread.Sleep(500); // Flash cần delay để nhận click
            }

            Thread.Sleep(1500);

            // đóng mini map
            SendTilde();
        }


        [DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        IntPtr FindDeepFlashWindow(IntPtr parent)
        {
            IntPtr found = IntPtr.Zero;

            EnumChildWindows(parent, (child, l) =>
            {
                var sb = new StringBuilder(256);
                GetClassName(child, sb, sb.Capacity);
                string cls = sb.ToString();

                if (cls.Contains("ShockwaveFlash", StringComparison.OrdinalIgnoreCase) ||
                    cls.Contains("MacromediaFlashPlayerActiveX", StringComparison.OrdinalIgnoreCase))
                {
                    Append($"✅ Found Flash class: {cls} (0x{child.ToInt64():X})");
                    found = child;
                    return false; // dừng lại
                }
                return true;
            }, IntPtr.Zero);

            return found;
        }
        public static string DetectPlayerAvatar(IntPtr hwnd, string folder, double threshold, Action<string> log)
        {
            using var frame = ImageHelper.CaptureWindow(hwnd);
            string bestFile = "";
            double bestScore = 0;

            foreach (var f in Directory.GetFiles(folder, "player_*.png", SearchOption.TopDirectoryOnly))
            {
                using var tpl = (Bitmap)Image.FromFile(f);
                var (pt, score) = ImageHelper.MatchOnce(frame, tpl, threshold);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestFile = f;
                }
            }

            if (!string.IsNullOrEmpty(bestFile) && bestScore >= threshold)
            {
                log($"🧍 Phát hiện nhân vật: {Path.GetFileNameWithoutExtension(bestFile)} (score={bestScore:F2})");
                return bestFile;
            }

            log($"⚠️ Không phát hiện được nhân vật (best={bestScore:F2})");
            return string.Empty;
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
           
            _hookID = SetHook(_proc);
        }
        private async Task<bool> CheckLicenseAsync()
        {
            string myHwid = HWIDHelper.GetHWID();

            try
            {
                string licenseUrl = "https://raw.githubusercontent.com/khoang1205/batpet/main/keys.txt";

                using (HttpClient client = new HttpClient())
                {
                    // Tải toàn bộ nội dung file text về
                    string validHwidsText = await client.GetStringAsync(licenseUrl);

                    // Tách nội dung thành từng dòng riêng biệt
                    string[] lines = validHwidsText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                    foreach (string line in lines)
                    {
                        // Cắt lấy phần mã HWID đứng trước dấu "|" và xóa khoảng trắng dư thừa
                        string hwidInFile = line.Split('|')[0].Trim();

                        // So sánh chính xác tuyệt đối 2 mã với nhau (bỏ qua viết hoa/thường)
                        if (string.Equals(hwidInFile, myHwid, StringComparison.OrdinalIgnoreCase))
                        {
                            return true; // Trùng khớp hoàn toàn -> Cho chạy!
                        }
                    }
                }
            }
            catch
            {
                Append("❌ Lỗi kết nối máy chủ bản quyền.");
                return false;
            }

            return false;
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnhookWindowsHookEx(_hookID);
            UnregisterHotKey(this.Handle, HOTKEY_ID_1);
            UnregisterHotKey(this.Handle, HOTKEY_ID_2);
            UnregisterHotKey(this.Handle, HOTKEY_ID_3);
            base.OnFormClosing(e);
        }
        static IntPtr SetHook(LowLevelMouseProc proc)
        {
            using (var curProcess = Process.GetCurrentProcess())
            using (var curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_MOUSE_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }
        static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // Bắt đầu kéo
                if (wParam == (IntPtr)WM_LBUTTONDOWN)
                {
                    dragStart = (info.pt.x, info.pt.y);
                    isDragging = true;
                }

                // Kết thúc kéo
                if (wParam == (IntPtr)WM_LBUTTONUP)
                {
                    int endX = info.pt.x;
                    int endY = info.pt.y;

                    if (isDragging && (isPickingMini1 || isPickingMini2))
                    {
                        isDragging = false;

                        // Convert screen → client
                        POINT p = new POINT { x = endX, y = endY };
                        ScreenToClient(_instance._hwnd, ref p);

                        if (isPickingMini1)
                        {
                            _instance.txtMini1X.Text = p.x.ToString();
                            _instance.txtMini1Y.Text = p.y.ToString();
                            _instance.Append($"✅ Mini #1 → client({p.x},{p.y})");
                        }
                        else
                        {
                            _instance.txtMini2X.Text = p.x.ToString();
                            _instance.txtMini2Y.Text = p.y.ToString();
                            _instance.Append($"✅ Mini #2 → client({p.x},{p.y})");
                        }

                        isPickingMini1 = false;
                        isPickingMini2 = false;
                    }
                    if (isDragging && isPickingBackup)
                    {
                        isDragging = false;

                        POINT p = new POINT { x = endX, y = endY };
                        ScreenToClient(_instance._hwnd, ref p);

                        _instance.txtBackupX.Text = p.x.ToString();
                        _instance.txtBackupY.Text = p.y.ToString();
                        _instance.Append($" Backup → client({p.x},{p.y})");


                        isPickingBackup = false;
                    }
                }
            }

            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
        void btnPickBk_Click(object? sender, EventArgs e)
        {
            Append("🔥 Kéo-thả lên map để chọn Backup");
            isPickingBackup = true;

            // Tắt mini picking
            isPickingMini1 = false;
            isPickingMini2 = false;
        }

        public static void OpenFusionUI(IntPtr hwnd, Action<string> log)
        {
            // 1) Bấm V
            PressKey((int)'V', hwnd);
            Thread.Sleep(500);

            // 2) Nếu hiện UI → return
            if (IsFusionUIOpened(hwnd))
            {
                log("📂 Mở luyện pet bằng V → OK");
                return;
            }

            // 3) Nếu chưa → fallback click
            log("⚠️ V không mở được → click fallback");
            ImageHelper.ClickClient(hwnd, 862, 223, log);
            Thread.Sleep(400);

            // 4) Bấm V lại
            PressKey((int)'V', hwnd);
            Thread.Sleep(500);
        }
        public static bool IsFusionUIOpened(IntPtr hwnd)
        {
            // TODO: check bằng ảnh (ví dụ: Dung... nút)
            return false;
        }

        public int FusionThreshold
        {
            get
            {
                if (int.TryParse(txtFusionThreshold.Text, out int v))
                    return v;
                return 50;   // fallback
            }
        }
        protected override void WndProc(ref Message m)
        {
            const int WM_HOTKEY = 0x0312;

            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();

                // Lấy vị trí chuột hiện tại (screen)
                POINT p;
                GetCursorPos(out p);

                // convert screen → client
                ScreenToClient(_hwnd, ref p);

                if (id == HOTKEY_ID_1)
                {
                    txtMini1X.Text = p.x.ToString();
                    txtMini1Y.Text = p.y.ToString();
                    Append($"✅ Mini #1 = client({p.x},{p.y})");
                }
                else if (id == HOTKEY_ID_2)
                {
                    txtMini2X.Text = p.x.ToString();
                    txtMini2Y.Text = p.y.ToString();
                    Append($"✅ Mini #2 = client({p.x},{p.y})");
                }
                else if (id == HOTKEY_ID_3)      
                {
                    txtBackupX.Text = p.x.ToString();
                    txtBackupY.Text = p.y.ToString();
                    Append($"✅ Backup = client({p.x},{p.y})");
                }
            }

            base.WndProc(ref m);
        }
        void btnSaveConfig_Click(object? sender, EventArgs e)
        {
            SyncCheckedPets();

            var cfg = new PetConfig
            {
                Name = cboConfig.Text.Trim(),
                AssetsDir = txtAssetsDir.Text,
                Mini1X = int.TryParse(txtMini1X.Text, out var m1x) ? m1x : 0,
                Mini1Y = int.TryParse(txtMini1Y.Text, out var m1y) ? m1y : 0,
                Mini2X = int.TryParse(txtMini2X.Text, out var m2x) ? m2x : 0,
                Mini2Y = int.TryParse(txtMini2Y.Text, out var m2y) ? m2y : 0,
                BackupX = int.TryParse(txtBackupX.Text, out var bx) ? bx : 0,
                BackupY = int.TryParse(txtBackupY.Text, out var by) ? by : 0,
                SelectedPets = chkPets.CheckedItems.Cast<string>().ToList()
            };

            if (string.IsNullOrWhiteSpace(cfg.Name))
            {
                MessageBox.Show("Nhập tên cấu hình trước khi lưu (gõ vào ô combobox)");
                return;
            }

            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs", $"{cfg.Name}.json");
            File.WriteAllText(file, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));

            Append($"💾 Đã lưu cấu hình: {cfg.Name}");
            LoadConfigList(); // reload lại danh sách combobox
        }
        void btnLoadConfig_Click(object? sender, EventArgs e)
        {
            string name = cboConfig.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs", $"{name}.json");
            if (!File.Exists(file))
            {
                MessageBox.Show("Không tìm thấy file cấu hình!");
                return;
            }

            var cfg = JsonSerializer.Deserialize<PetConfig>(File.ReadAllText(file));
            if (cfg == null) return;

            txtAssetsDir.Text = cfg.AssetsDir;
            txtMini1X.Text = cfg.Mini1X.ToString();
            txtMini1Y.Text = cfg.Mini1Y.ToString();
            txtMini2X.Text = cfg.Mini2X.ToString();
            txtMini2Y.Text = cfg.Mini2Y.ToString();
            txtBackupX.Text = cfg.BackupX.ToString();
            txtBackupY.Text = cfg.BackupY.ToString();

            // tick lại pets
            for (int i = 0; i < chkPets.Items.Count; i++)
            {
                var nameItem = chkPets.Items[i].ToString();
                chkPets.SetItemChecked(i, cfg.SelectedPets.Contains(nameItem!));
            }

            Append($"✅ Đã tải cấu hình: {cfg.Name}");
        }
        void LoadConfigList()
        {
            cboConfig.Items.Clear();
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs");
            Directory.CreateDirectory(dir);

            var files = Directory.GetFiles(dir, "*.json");
            foreach (var f in files)
            {
                cboConfig.Items.Add(Path.GetFileNameWithoutExtension(f));
            }

            if (cboConfig.Items.Count > 0)
                cboConfig.SelectedIndex = 0;
        }



        const int WM_LBUTTONUP = 0x0202;


        const int WH_MOUSE_LL = 14;
        const int WM_LBUTTONDOWN = 0x0201;

        static LowLevelMouseProc _proc = HookCallback!;
        static IntPtr _hookID = IntPtr.Zero;

        delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        // --- UI helpers & P/Invoke ---
        [DllImport("user32.dll")]
        static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn,
    IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
            IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, int vk);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] static extern IntPtr FindWindow(string? cls, string? title);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);
        const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101; const int VK_OEM_3 = 0xC0; // ~
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string? className, string? windowTitle);
        void SendTilde() { PostMessage(_hwnd, WM_KEYDOWN, VK_OEM_3, 0); Thread.Sleep(30); PostMessage(_hwnd, WM_KEYUP, VK_OEM_3, 0); }

        void Append(string s) => BeginInvoke(() => txtLog.AppendText($"{DateTime.Now:HH:mm:ss} {s}\r\n"));
        void ToggleUI(bool enabled) { BeginInvoke(() => { btnStart.Enabled = enabled; btnStop.Enabled = !enabled; cboWindows.Enabled = enabled; }); }
    }
}