// TaskbarChinaClock — 让任务栏右下角的系统时钟显示中国时间（北京时间 UTC+8）
// 原理：Windows 11 不允许修改任务栏时钟的渲染内容，所以用一个外观一致、
//       置顶且不抢焦点的小窗口精确覆盖在系统时钟上方，背景色取自任务栏
//       本身，视觉上就像系统时钟被换成了中国时间。
//       不修改系统时区、不改任何系统设置；本程序退出即恢复原样。
// 交互：左键单击 = 打开系统日历面板；右键 = 菜单（重新校准 / 开机自启 / 退出）。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ChinaClock
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            using (new System.Threading.Mutex(true, "ChinaClock_SingleInstance", out createdNew))
            {
                if (!createdNew) return; // 已有实例
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ClockForm());
            }
        }
    }

    internal sealed class ClockForm : Form
    {
        private const int HWND_TOPMOST = -1;
        private const int SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;
        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_DISPLAYCHANGE = 0x007E;

        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, int after, int x, int y, int cx, int cy, int flags);
        [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, int flags, IntPtr extra);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private static readonly TimeZoneInfo ChinaTz = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        private static readonly string[] Weekdays = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
        // 时钟元素的名字以时间开头（如 “19:06” “19:06:13 星期一”）
        private static readonly Regex TimeNameRx = new Regex(@"^\s*\d{1,2}:\d{2}", RegexOptions.Compiled);

        // 日期行沿用系统短日期格式（与原时钟显示格式一致）
        private readonly string _datePattern = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;

        private readonly System.Windows.Forms.Timer _uiTimer = new System.Windows.Forms.Timer();  // 秒级刷新文字
        private readonly System.Windows.Forms.Timer _fitTimer = new System.Windows.Forms.Timer(); // 定期重新定位/保持置顶
        private readonly System.Windows.Forms.Timer _bgTimer = new System.Windows.Forms.Timer();  // 低频任务栏底色采样

        private Rectangle _nativeRect;   // 系统时钟区域（物理像素）
        private Rectangle _trayRect;     // 任务栏区域
        private Color _bg = SystemColors.Control;
        private bool _lightTaskbar = true;
        private string _timeText = "", _dateText = "", _lastStamp = "";
        private Font _f1, _f2;
        private int _fontH = -1;
        private DateTime _lastInvoke = DateTime.MinValue;

        public ClockForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;

            BuildMenu();
            UpdateTime();

            _uiTimer.Interval = 200;
            _uiTimer.Tick += delegate { UpdateTime(); };
            _fitTimer.Interval = 4000;
            _fitTimer.Tick += delegate { FitToNativeClock(Rectangle.Empty); };
            _bgTimer.Interval = 15000;
            _bgTimer.Tick += delegate { SampleBackgroundAsync(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;        // WS_EX_TOOLWINDOW：不进任务栏/Alt+Tab，Win+D 也不影响
                cp.ExStyle |= 0x8;         // WS_EX_TOPMOST
                cp.ExStyle |= 0x08000000;  // WS_EX_NOACTIVATE：点击不抢焦点
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // 先按几何估算立即定位显示；UIA 精确结果稍后在后台线程补一次微调
            FitToNativeClock(GeometricGuess());
            _uiTimer.Start();
            _fitTimer.Start();
            _bgTimer.Start();
            SampleBackgroundAsync();

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                Rectangle r = TryUiaRect();
                if (r.IsEmpty || !IsHandleCreated) return;
                BeginInvoke((MethodInvoker)delegate { FitToNativeClock(r); });
            });
        }

        // ---- 定位：找到系统时钟并覆盖 ----

        // r 为空时使用缓存位置或几何估算
        private void FitToNativeClock(Rectangle r)
        {
            EnsureTrayRect();
            if (r.IsEmpty) r = _nativeRect.IsEmpty ? GeometricGuess() : _nativeRect;
            else _nativeRect = r;
            if (r.IsEmpty || _trayRect.IsEmpty) return;

            RecalcFonts(r.Height + 4);
            Size s1 = TextRenderer.MeasureText(_timeText, _f1);
            Size s2 = TextRenderer.MeasureText(_dateText, _f2);
            int w = Math.Max(r.Width + 10, Math.Max(s1.Width, s2.Width) + 16);
            int h = r.Height + 4;
            int right = Math.Min(r.Right + 4, _trayRect.Right - 1);
            var target = new Rectangle(right - w, r.Top + r.Height / 2 - h / 2, w, h);
            if (Bounds != target)
            {
                Bounds = target;
                Invalidate();
            }
            if (IsHandleCreated)
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        // 刷新任务栏矩形（FindWindow/GetWindowRect 很快，可在 UI 线程调用；explorer 重启后也能自恢复）
        private void EnsureTrayRect()
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return;
            RECT tr;
            GetWindowRect(tray, out tr);
            _trayRect = Rectangle.FromLTRB(tr.Left, tr.Top, tr.Right, tr.Bottom);
        }

        // 仅在后台线程调用：用 UI Automation 在任务栏里找时钟元素（名字以 HH:mm 开头、位于托盘区）。
        // 注意：explorer 偶尔会让 UIA 调用长时间阻塞，绝不能在 UI 线程上执行；
        // 阻塞只会卡住这个后台线程，不影响小程序正常显示。
        private Rectangle TryUiaRect()
        {
            try
            {
                IntPtr tray = FindWindow("Shell_TrayWnd", null);
                if (tray == IntPtr.Zero) return Rectangle.Empty;

                RECT tr;
                GetWindowRect(tray, out tr);
                Rectangle trayRect = Rectangle.FromLTRB(tr.Left, tr.Top, tr.Right, tr.Bottom);
                if (trayRect.IsEmpty) return Rectangle.Empty;

                AutomationElement root = AutomationElement.FromHandle(tray);
                if (root == null) return Rectangle.Empty;
                AutomationElementCollection els = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                Rectangle best = Rectangle.Empty;
                int bestRight = int.MinValue;
                foreach (AutomationElement el in els)
                {
                    try
                    {
                        string n = el.Current.Name;
                        if (string.IsNullOrEmpty(n) || !TimeNameRx.IsMatch(n)) continue;
                        System.Windows.Rect br = el.Current.BoundingRectangle;
                        if (br.IsEmpty) continue;
                        if (br.Right < trayRect.Right - 320) continue;
                        if (br.Top < trayRect.Top - 4 || br.Bottom > trayRect.Bottom + 4) continue;
                        if (br.Right > bestRight)
                        {
                            bestRight = (int)br.Right;
                            best = Rectangle.Truncate(new RectangleF((float)br.X, (float)br.Y, (float)br.Width, (float)br.Height));
                        }
                    }
                    catch { }
                }
                return best;
            }
            catch { return Rectangle.Empty; }
        }

        // UIA 不可用时的兜底：按任务栏几何位置估算时钟区域
        private Rectangle GeometricGuess()
        {
            if (_trayRect.IsEmpty) return Rectangle.Empty;
            float k = _trayRect.Height / 48f;
            int w = (int)(92 * k), h = (int)(42 * k);
            return new Rectangle(_trayRect.Right - (int)(10 * k) - w,
                                 _trayRect.Top + (_trayRect.Height - h) / 2, w, h);
        }

        // ---- 外观 ----

        private void RecalcFonts(int h)
        {
            if (h == _fontH && _f1 != null) return;
            _fontH = h;
            if (_f1 != null) _f1.Dispose();
            if (_f2 != null) _f2.Dispose();
            _f1 = new Font("Segoe UI", Math.Max(10, (int)Math.Round(h * 0.30f)), FontStyle.Regular, GraphicsUnit.Pixel);
            _f2 = new Font("Segoe UI", Math.Max(8, (int)Math.Round(h * 0.22f)), FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // 任务栏底色采样：在后台线程用 CopyFromScreen 一次性抓取任务栏中线一整条
        // 像素（避开本窗口区域），众数统计出纯色背景，托盘图标等干扰自动被排除。
        // 注意：绝不能在 UI 线程逐点 GetPixel —— 几百次跨 DWM 同步读取会卡住
        // 界面线程，秒刷新定时器的消息被合并，表现为时间 5 秒才跳一次。
        private void SampleBackgroundAsync()
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return;
            RECT tr;
            GetWindowRect(tray, out tr);
            int y = tr.Top + (tr.Bottom - tr.Top) / 2;
            int x0 = tr.Left + 4;
            int x1 = (IsHandleCreated && Visible) ? Bounds.Left - 8 : tr.Right - 4;
            int w = x1 - x0;
            if (w < 100 || tr.Bottom - tr.Top < 20) return;

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    var counts = new Dictionary<int, int>();
                    using (var bmp = new Bitmap(w, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (var g = Graphics.FromImage(bmp))
                            g.CopyFromScreen(x0, y, 0, 0, new Size(w, 1));
                        var bd = bmp.LockBits(new Rectangle(0, 0, w, 1),
                            System.Drawing.Imaging.ImageLockMode.ReadOnly,
                            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                        var buf = new int[w];
                        Marshal.Copy(bd.Scan0, buf, 0, w);
                        bmp.UnlockBits(bd);
                        foreach (int px in buf)
                        {
                            int key = ((px >> 16) & 0xF8) << 16 | ((px >> 8) & 0xF8) << 8 | (px & 0xF8); // 量化到 8 级
                            int n;
                            counts[key] = counts.TryGetValue(key, out n) ? n + 1 : 1;
                        }
                    }

                    int bestKey = 0, bestN = 0;
                    foreach (var kv in counts)
                        if (kv.Value > bestN) { bestN = kv.Value; bestKey = kv.Key; }
                    if (bestN * 2 < w) return; // 背景色占比不足一半，采样不可信
                    var col = Color.FromArgb((bestKey >> 16) & 255, (bestKey >> 8) & 255, bestKey & 255);
                    if (IsHandleCreated)
                        BeginInvoke((MethodInvoker)delegate { ApplyBackground(col); });
                }
                catch { }
            });
        }

        private void ApplyBackground(Color col)
        {
            if (col == _bg) return;
            _bg = col;
            BackColor = col;
            double lum = 0.299 * col.R + 0.587 * col.G + 0.114 * col.B;
            _lightTaskbar = lum > 140;
            Invalidate();
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_SETTINGCHANGE || m.Msg == WM_DISPLAYCHANGE)
                SampleBackgroundAsync(); // 主题/壁纸/分辨率变化后重采底色（后台线程，不卡界面）
        }

        // ---- 时间文字 ----

        private void UpdateTime()
        {
            DateTime now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ChinaTz);
            string stamp = now.ToString("yyyyMMddHHmmss");
            if (stamp == _lastStamp) return;
            _lastStamp = stamp;
            _timeText = now.ToString("HH:mm:ss") + "  " + Weekdays[(int)now.DayOfWeek];
            _dateText = now.ToString(_datePattern);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color c1 = _lightTaskbar ? Color.FromArgb(26, 26, 26) : Color.FromArgb(242, 242, 242);
            Color c2 = _lightTaskbar ? Color.FromArgb(96, 96, 96) : Color.FromArgb(197, 197, 197);

            Size s1 = TextRenderer.MeasureText(_timeText, _f1);
            Size s2 = TextRenderer.MeasureText(_dateText, _f2);
            int gap = 1;
            int y = Math.Max(1, (ClientSize.Height - (s1.Height + gap + s2.Height)) / 2);

            var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine;
            int padX = 7;
            TextRenderer.DrawText(g, _timeText, _f1, new Rectangle(padX, y, ClientSize.Width - padX - 2, s1.Height), c1, flags);
            TextRenderer.DrawText(g, _dateText, _f2, new Rectangle(padX, y + s1.Height + gap, ClientSize.Width - padX - 2, s2.Height), c2, flags);

            base.OnPaint(e);
        }

        // ---- 交互 ----

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && (DateTime.Now - _lastInvoke).TotalMilliseconds > 600)
            {
                _lastInvoke = DateTime.Now;
                OpenNativeFlyout();
            }
        }

        // 左键 = 打开系统自带日历面板（等价 Win+N，不依赖 UIA，稳定可靠）
        private void OpenNativeFlyout()
        {
            keybd_event(0x5B, 0, 0, IntPtr.Zero); // Win
            keybd_event(0x4E, 0, 0, IntPtr.Zero); // N
            keybd_event(0x4E, 0, 2, IntPtr.Zero);
            keybd_event(0x5B, 0, 2, IntPtr.Zero);
        }

        private ToolStripMenuItem _miAutoStart;

        private void BuildMenu()
        {
            _miAutoStart = new ToolStripMenuItem("开机自启") { CheckOnClick = true, Checked = IsAutoStart() };
            _miAutoStart.Click += delegate { SetAutoStart(_miAutoStart.Checked); };

            var miFit = new ToolStripMenuItem("重新校准位置");
            miFit.Click += delegate { FitToNativeClock(Rectangle.Empty); };

            var miExit = new ToolStripMenuItem("退出（恢复系统时钟）");
            miExit.Click += delegate { Close(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add(miFit);
            menu.Items.Add(_miAutoStart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExit);
            ContextMenuStrip = menu;
        }

        // ---- 开机自启（HKCU Run，无需管理员）----

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "ChinaClock";

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

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _uiTimer.Stop();
            _fitTimer.Stop();
            _bgTimer.Stop();
            if (_f1 != null) _f1.Dispose();
            if (_f2 != null) _f2.Dispose();
            base.OnFormClosed(e);
        }
    }
}
