// ChinaTimeWidget — Windows 桌面时间小组件
// 默认显示在屏幕右下角（任务栏上方），始终显示中国标准时间（UTC+8），与系统时区无关。
// 交互：左键拖动移动位置（自动记忆）、右键菜单（置顶 / 开机自启 / 重置位置 / 退出）。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ChinaTimeWidget
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (new System.Threading.Mutex(true, "ChinaTimeWidget_SingleInstance", out createdNew))
            {
                if (!createdNew) return; // 已有实例在运行，直接退出
                try { SetProcessDPIAware(); } catch { } // 高分屏下文字不模糊
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new WidgetForm());
            }
        }
    }

    internal sealed class WidgetForm : Form
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private static readonly TimeZoneInfo ChinaTz =
            TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        private static readonly string[] WeekdayNames =
            { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

        // ---- 外观参数（可按需调整后重新编译）----
        private readonly Font _timeFont = new Font("Segoe UI Semibold", 14F);
        private readonly Font _dateFont = new Font("Segoe UI", 9.5F);
        private const int PadX = 16, PadTop = 9, PadBottom = 9, LineGap = 2;
        private const int Radius = 6;     // 圆角半径
        private const int EdgeMargin = 8; // 与屏幕右下边缘的间距
        private static readonly Color TimeColor = Color.FromArgb(31, 31, 31);
        private static readonly Color DateColor = Color.FromArgb(101, 101, 101);
        private static readonly Color BorderColor = Color.FromArgb(222, 222, 222);
        private const string DateFormat = "yyyy/M/d"; // 第二行日期格式

        private readonly Timer _timer = new Timer();
        private readonly string _configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChinaTimeWidget");
        private string _configPath;
        private string _timeText = "", _dateText = "", _lastStamp = "";
        private int _timeH, _dateH;

        private ToolStripMenuItem _miTopmost;
        private ToolStripMenuItem _miAutoStart;

        public WidgetForm()
        {
            _configPath = Path.Combine(_configDir, "config.ini");

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            DoubleBuffered = true;
            Font = new Font("Segoe UI", 9F);

            BuildMenu();
            ComputeSize();
            LoadConfig();

            _timer.Interval = 200;
            _timer.Tick += delegate { Tick(); };
            Tick();
            _timer.Start();
        }

        // 启动时不抢占前台焦点
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW：不出现在任务栏和 Alt+Tab
                cp.ClassStyle |= 0x20000;  // CS_DROPSHADOW：类 tooltip 的浅阴影
                return cp;
            }
        }

        private void BuildMenu()
        {
            _miTopmost = new ToolStripMenuItem("窗口置顶") { CheckOnClick = true, Checked = TopMost };
            _miTopmost.Click += delegate { TopMost = _miTopmost.Checked; SaveConfig(); };

            _miAutoStart = new ToolStripMenuItem("开机自启") { CheckOnClick = true, Checked = IsAutoStart() };
            _miAutoStart.Click += delegate { SetAutoStart(_miAutoStart.Checked); };

            var miReset = new ToolStripMenuItem("重置位置（右下角）");
            miReset.Click += delegate { PlaceBottomRight(); SaveConfig(); };

            var miExit = new ToolStripMenuItem("退出");
            miExit.Click += delegate { SaveConfig(); Close(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add(_miTopmost);
            menu.Items.Add(_miAutoStart);
            menu.Items.Add(miReset);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExit);
            ContextMenuStrip = menu;
        }

        private void ComputeSize()
        {
            var szT = TextRenderer.MeasureText("00:00:00 星期三", _timeFont);
            var szD = TextRenderer.MeasureText("2026/11/22", _dateFont);
            _timeH = szT.Height;
            _dateH = szD.Height;
            Size = new Size(Math.Max(szT.Width, szD.Width) + PadX * 2 + 2,
                            PadTop + _timeH + LineGap + _dateH + PadBottom + 2);
            using (var path = RoundedPath(ClientRectangle, Radius))
                Region = new Region(path); // 圆角裁剪
        }

        private void Tick()
        {
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ChinaTz);
            string stamp = now.ToString("yyyyMMddHHmmss");
            if (stamp == _lastStamp) return; // 秒没变不重绘
            _lastStamp = stamp;
            _timeText = now.ToString("HH:mm:ss") + "  " + WeekdayNames[(int)now.DayOfWeek];
            _dateText = now.ToString(DateFormat);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (var pen = new Pen(BorderColor))
                g.DrawPath(pen, path);

            var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, _timeText, _timeFont,
                new Rectangle(PadX, PadTop, ClientSize.Width - PadX * 2, _timeH), TimeColor, flags);
            TextRenderer.DrawText(g, _dateText, _dateFont,
                new Rectangle(PadX, PadTop + _timeH + LineGap, ClientSize.Width - PadX * 2, _dateH), DateColor, flags);

            base.OnPaint(e);
        }

        // 左键按下即可拖动整个窗口
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                SaveConfig(); // 拖动结束后记住位置
            }
        }

        // ---- 位置与配置持久化 ----

        private void PlaceBottomRight()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - EdgeMargin, wa.Bottom - Height - EdgeMargin);
        }

        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var kv = new Dictionary<string, string>();
                    foreach (var line in File.ReadAllLines(_configPath))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) kv[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                    }

                    string tm;
                    if (kv.TryGetValue("TopMost", out tm))
                    {
                        TopMost = tm == "1";
                        _miTopmost.Checked = TopMost;
                    }

                    string xs, ys;
                    if (kv.TryGetValue("X", out xs) && kv.TryGetValue("Y", out ys))
                    {
                        int x = int.Parse(xs), y = int.Parse(ys);
                        var wa = Screen.PrimaryScreen.WorkingArea;
                        bool onScreen = x >= wa.Left - Width + 40 && x <= wa.Right - 40 &&
                                        y >= wa.Top && y <= wa.Bottom - 40;
                        if (onScreen)
                        {
                            Location = new Point(x, y);
                            return;
                        }
                    }
                }
            }
            catch { }
            PlaceBottomRight(); // 无配置或位置越界时，回到默认右下角
        }

        private void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(_configDir);
                File.WriteAllText(_configPath,
                    "X=" + Location.X + "\r\nY=" + Location.Y + "\r\nTopMost=" + (TopMost ? 1 : 0));
            }
            catch { }
        }

        // ---- 开机自启（注册表 HKCU Run）----

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "ChinaTimeWidget";

        private static bool IsAutoStart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }

        private static void SetAutoStart(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置开机自启失败：" + ex.Message, "中国时间",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static GraphicsPath RoundedPath(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            if (d < 2 || r.Width < d || r.Height < d) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d - 1, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d - 1, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timeFont.Dispose();
            _dateFont.Dispose();
            base.OnFormClosed(e);
        }
    }
}
