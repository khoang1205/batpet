using System.Windows.Forms;

namespace batpet
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        private ComboBox cboWindows;
        private TextBox txtAssetsDir;
        private Button btnStart;
        private Button btnStop;
        private TextBox txtLog;
        private Label lblTitle;
        private CheckedListBox chkPets;
        TextBox txtMini1X, txtMini1Y, txtMini2X, txtMini2Y;
        Button btnPickMini1, btnPickMini2;
        TextBox txtBackupX, txtBackupY;
        Button btnPickBackup;
        private ComboBox cboConfig;
        private Button btnSaveConfig;
        private Button btnLoadConfig;


        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            // ==== FORM SETUP ====
            ClientSize = new Size(780, 460);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Text = "🐾 Auto Bắt Pet Tool";
            SuspendLayout();

            // ==== LABEL TITLE ====
            lblTitle = new Label();
            lblTitle.AutoSize = true;
            lblTitle.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            lblTitle.Location = new Point(20, 10);
            lblTitle.Text = "🐾 Auto Bắt Pet Tool";
            Controls.Add(lblTitle);

            // ==== LEFT SIDE ====
            // Game window combobox
            cboWindows = new ComboBox();
            cboWindows.DropDownStyle = ComboBoxStyle.DropDownList;
            cboWindows.Location = new Point(20, 45);
            cboWindows.Size = new Size(300, 23);
            Controls.Add(cboWindows);

            // Btn Start
            btnStart = new Button();
            btnStart.Location = new Point(330, 45);
            btnStart.Size = new Size(70, 28);
            btnStart.Text = "Start";
            btnStart.Click += btnStart_Click;
            Controls.Add(btnStart);

            // Btn Stop
            btnStop = new Button();
            btnStop.Location = new Point(410, 45);
            btnStop.Size = new Size(70, 28);
            btnStop.Enabled = false;
            btnStop.Text = "Stop";
            btnStop.Click += btnStop_Click;
            Controls.Add(btnStop);

            // Asset Path
            txtAssetsDir = new TextBox();
            txtAssetsDir.Location = new Point(20, 80);
            txtAssetsDir.Size = new Size(460, 23);
            txtAssetsDir.ReadOnly = true;
            Controls.Add(txtAssetsDir);

            // Log box (small)
            txtLog = new TextBox();
            txtLog.Location = new Point(20, 115);
            txtLog.Size = new Size(460, 120);
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            Controls.Add(txtLog);

            // ==== MINI POSITIONS ====

            // Row Y for mini positions
            int rowY = 250;

            // Mini 1
            txtMini1X = new TextBox() { Location = new Point(20, rowY), Size = new Size(50, 23) };
            txtMini1Y = new TextBox() { Location = new Point(75, rowY), Size = new Size(50, 23) };
            btnPickMini1 = new Button() { Location = new Point(130, rowY), Size = new Size(70, 23), Text = "vị trí 1" };
            btnPickMini1.Click += btnPickMini1_Click;

            Controls.Add(txtMini1X);
            Controls.Add(txtMini1Y);
            Controls.Add(btnPickMini1);

            // Mini 2
            txtMini2X = new TextBox() { Location = new Point(220, rowY), Size = new Size(50, 23) };
            txtMini2Y = new TextBox() { Location = new Point(275, rowY), Size = new Size(50, 23) };
            btnPickMini2 = new Button() { Location = new Point(330, rowY), Size = new Size(70, 23), Text = "vị trí 2" };
            btnPickMini2.Click += btnPickMini2_Click;

            Controls.Add(txtMini2X);
            Controls.Add(txtMini2Y);
            Controls.Add(btnPickMini2);

            // Backup
            rowY = 280;
            txtBackupX = new TextBox() { Location = new Point(20, rowY), Size = new Size(50, 23) };
            txtBackupY = new TextBox() { Location = new Point(75, rowY), Size = new Size(50, 23) };
            btnPickBackup = new Button() { Location = new Point(130, rowY), Size = new Size(70, 23), Text = "backup" };
            btnPickBackup.Click += btnPickBk_Click;

            Controls.Add(txtBackupX);
            Controls.Add(txtBackupY);
            Controls.Add(btnPickBackup);

            // ==== RIGHT SIDE ====

            // Search pet
            TextBox txtSearch = new TextBox();
            txtSearch.Location = new Point(510, 45);
            txtSearch.Size = new Size(240, 23);
            txtSearch.PlaceholderText = "Nhập tên pet ...";
            txtSearch.TextChanged += (s, e) => FilterPetList(txtSearch.Text);
            Controls.Add(txtSearch);

            // Pet list
            chkPets = new CheckedListBox();
            chkPets.Location = new Point(510, 80);
            chkPets.Size = new Size(240, 250);
            chkPets.CheckOnClick = true;
            chkPets.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(chkPets);

            // Fusion Threshold
            Label lblFusion = new Label();
            lblFusion.AutoSize = true;
            lblFusion.Location = new Point(510, 340);
            lblFusion.Text = "Số lần bắt → Dung hợp:";
            Controls.Add(lblFusion);

            txtFusionThreshold = new TextBox();
            txtFusionThreshold.Location = new Point(640, 336);
            txtFusionThreshold.Size = new Size(50, 23);
            txtFusionThreshold.Text = "50";
            Controls.Add(txtFusionThreshold);

            // === CONFIG ===
            cboConfig = new ComboBox();
            cboConfig.Location = new Point(510, 370);
            cboConfig.Size = new Size(240, 23);
            Controls.Add(cboConfig);

            btnSaveConfig = new Button();
            btnSaveConfig.Location = new Point(510, 400);
            btnSaveConfig.Size = new Size(110, 28);
            btnSaveConfig.Text = "Save";
            btnSaveConfig.Click += btnSaveConfig_Click;
            Controls.Add(btnSaveConfig);

            btnLoadConfig = new Button();
            btnLoadConfig.Location = new Point(640, 400);
            btnLoadConfig.Size = new Size(110, 28);
            btnLoadConfig.Text = "Load";
            btnLoadConfig.Click += btnLoadConfig_Click;
            Controls.Add(btnLoadConfig);

            ResumeLayout(false);
            PerformLayout();
        }


        #endregion
    }
}
