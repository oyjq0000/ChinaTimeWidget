// TaskbarChinaClock — 任务栏右下角中国时间时钟 + 点击弹出中国日历面板
// 原理：Windows 11 不允许修改任务栏时钟的渲染内容，所以用一个外观一致、
//       置顶且不抢焦点的小窗口精确覆盖在系统时钟上方，背景色取自任务栏
//       本身，视觉上就像系统时钟被换成了中国时间（北京时间 UTC+8）。
//       不修改系统时区、不改任何系统设置；本程序退出即恢复原样。
// 交互：左键单击时钟 = 打开/关闭日历面板（月历：农历、节气、节日、
//       干支纪年月日、法定假日"休"角标）；右键 = 菜单。
// 自测：命令行 --selftest 可校验农历/干支/节气算法，结果写入 selftest-result.txt。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
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
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && args[0] == "--selftest")
            {
                SelfTest.Run();
                return;
            }
            bool openPanel = args != null && args.Length > 0 && args[0] == "--show-panel";

            bool createdNew;
            using (new System.Threading.Mutex(true, "ChinaClock_SingleInstance", out createdNew))
            {
                if (!createdNew) return; // 已有实例
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ClockForm(openPanel));
            }
        }
    }

    // ============================ 自测 ============================

    // 校验农历/干支/节气算法。锚点数据来源：
    //  - 2026-11-02 = 农历九月廿四、丙午马年、戊戌月、庚辰日（用户日历软件截图）
    //  - 2026 年节气：立冬 11-07、清明 04-05、立春 02-04
    //  - 2026 春节 02-17、2027 春节 02-06、2026 端午 06-19、2026 中秋 09-25
    //  - 1949-10-01 = 甲子日（史实）
    internal static class SelfTest
    {
        public static void Run()
        {
            List<string> lines = new List<string>();
            int fails = 0;

            DateTime d = new DateTime(2026, 11, 2);
            CalendarData.DayInfo di = CalendarData.Get(d);

            Check(lines, ref fails, "干支日 1949-10-01 = 甲子",
                CalendarData.Get(new DateTime(1949, 10, 1)).GanZhiDay == "甲子",
                CalendarData.Get(new DateTime(1949, 10, 1)).GanZhiDay);
            Check(lines, ref fails, "干支日 2026-11-02 = 庚辰", di.GanZhiDay == "庚辰", di.GanZhiDay);
            Check(lines, ref fails, "干支月 2026-11-02 = 戊戌", di.GanZhiMonth == "戊戌", di.GanZhiMonth);
            Check(lines, ref fails, "干支年 2026-11-02 = 丙午(马)", di.GanZhiYear == "丙午" && di.Animal == "马",
                di.GanZhiYear + di.Animal);
            Check(lines, ref fails, "农历 2026-11-02 = 九月廿四", di.LunarFullText == "九月廿四", di.LunarFullText);
            Check(lines, ref fails, "农历短格式 2026-11-02 = 廿四", di.LunarText == "廿四", di.LunarText);
            Check(lines, ref fails, "农历 2026-11-09 = 十月(初一显示月名)", CalendarData.Get(new DateTime(2026, 11, 9)).LunarText == "十月",
                CalendarData.Get(new DateTime(2026, 11, 9)).LunarText);

            Check(lines, ref fails, "节气 立冬 2026 = 11-07",
                CalendarData.TermDate(2026, "立冬") == new DateTime(2026, 11, 7),
                CalendarData.TermDate(2026, "立冬").ToString("yyyy-MM-dd"));
            Check(lines, ref fails, "节气 清明 2026 = 04-05",
                CalendarData.TermDate(2026, "清明") == new DateTime(2026, 4, 5),
                CalendarData.TermDate(2026, "清明").ToString("yyyy-MM-dd"));
            Check(lines, ref fails, "节气 立春 2026 = 02-04",
                CalendarData.TermDate(2026, "立春") == new DateTime(2026, 2, 4),
                CalendarData.TermDate(2026, "立春").ToString("yyyy-MM-dd"));
            Check(lines, ref fails, "节气 冬至 2026 = 12-22",
                CalendarData.TermDate(2026, "冬至") == new DateTime(2026, 12, 22),
                CalendarData.TermDate(2026, "冬至").ToString("yyyy-MM-dd"));

            Check(lines, ref fails, "春节 2026 = 02-17",
                CalendarData.LunarDate(2026, 1, 1) == new DateTime(2026, 2, 17),
                CalendarData.LunarDate(2026, 1, 1).ToString("yyyy-MM-dd"));
            Check(lines, ref fails, "春节 2027 = 02-06",
                CalendarData.LunarDate(2027, 1, 1) == new DateTime(2027, 2, 6),
                CalendarData.LunarDate(2027, 1, 1).ToString("yyyy-MM-dd"));
            Check(lines, ref fails, "除夕(丙午年前夜) = 2026-02-16",
                CalendarData.CnyDate(2026).AddDays(-1) == new DateTime(2026, 2, 16),
                CalendarData.CnyDate(2026).AddDays(-1).ToString("yyyy-MM-dd"));
            Check(lines, ref fails, "端午 2026 = 06-19",
                CalendarData.LunarDate(2026, 5, 5) == new DateTime(2026, 6, 19),
                CalendarData.LunarDate(2026, 5, 5).ToString("yyyy-MM-dd"));
            Check(lines, ref fails, "中秋 2026 = 09-25",
                CalendarData.LunarDate(2026, 8, 15) == new DateTime(2026, 9, 25),
                CalendarData.LunarDate(2026, 8, 15).ToString("yyyy-MM-dd"));

            Check(lines, ref fails, "法定休 2026-10-01", CalendarData.IsStatutoryRest(new DateTime(2026, 10, 1)), "");
            Check(lines, ref fails, "法定休 2026-02-17(春节)", CalendarData.IsStatutoryRest(new DateTime(2026, 2, 17)), "");
            Check(lines, ref fails, "法定休 2026-02-16(除夕)", CalendarData.IsStatutoryRest(new DateTime(2026, 2, 16)), "");
            Check(lines, ref fails, "法定休 2026-04-05(清明)", CalendarData.IsStatutoryRest(new DateTime(2026, 4, 5)), "");
            Check(lines, ref fails, "非法定 2026-10-08 无休标记", !CalendarData.IsStatutoryRest(new DateTime(2026, 10, 8)), "");
            Check(lines, ref fails, "角标 2026-10-01 = 休", CalendarData.Get(new DateTime(2026, 10, 1)).Badge == "休",
                CalendarData.Get(new DateTime(2026, 10, 1)).Badge ?? "(null)");

            lines.Add("");
            lines.Add(fails == 0 ? "RESULT: ALL PASS (" + lines.Count + " checks)" : "RESULT: " + fails + " FAILURES");
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-result.txt");
            File.WriteAllText(path, string.Join("\r\n", lines.ToArray()));
            Environment.ExitCode = fails == 0 ? 0 : 1;
        }

        private static void Check(List<string> lines, ref int fails, string name, bool ok, string detail)
        {
            if (!ok) fails++;
            lines.Add((ok ? "[PASS] " : "[FAIL] ") + name + (ok || detail.Length == 0 ? "" : "  -> 实际: " + detail));
        }
    }

    // ============================ 历法数据 ============================

    // 农历（.NET 内置 ChineseLunisolarCalendar，1901-2100）+ 节气（寿星公式，21 世纪）
    // + 干支纪年/月/日 + 节日 + 法定假日 + holidays.txt 自定义 休/班
    internal static class CalendarData
    {
        private static readonly ChineseLunisolarCalendar Lunar = new ChineseLunisolarCalendar();
        private static readonly string[] Stems = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
        private static readonly string[] Branches = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };
        private static readonly string[] Animals = { "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪" };
        private static readonly string[] LunarMonths = { "正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月" };
        private static readonly string[] LunarDays = {
            "初一","初二","初三","初四","初五","初六","初七","初八","初九","初十",
            "十一","十二","十三","十四","十五","十六","十七","十八","十九","二十",
            "廿一","廿二","廿三","廿四","廿五","廿六","廿七","廿八","廿九","三十" };

        // 节气：按 小寒(1月)→冬至(12月) 顺序；day = floor(y*0.2422 + C) - floor(y/4)，y=年份%100
        private static readonly string[] TermNames = {
            "小寒","大寒","立春","雨水","惊蛰","春分","清明","谷雨",
            "立夏","小满","芒种","夏至","小暑","大暑","立秋","处暑",
            "白露","秋分","寒露","霜降","立冬","小雪","大雪","冬至" };
        private static readonly double[] TermC = {
            5.4055,20.12,3.87,18.73,5.63,20.646,4.81,20.1,
            5.52,21.04,5.678,21.37,7.108,22.83,7.5,23.13,
            7.646,23.042,8.318,23.438,7.438,22.36,7.18,21.94 };
        private static readonly int[] TermMonth = { 1,1,2,2,3,3,4,4,5,5,6,6,7,7,8,8,9,9,10,10,11,11,12,12 };

        private static readonly Dictionary<string, DateTime> _termCache = new Dictionary<string, DateTime>();
        private static readonly Dictionary<int, DateTime> _cnyCache = new Dictionary<int, DateTime>();
        private static readonly Dictionary<int, DateTime[]> _termYearCache = new Dictionary<int, DateTime[]>();
        private static Dictionary<DateTime, string> _overrides;

        public sealed class DayInfo
        {
            public string LunarText;      // 农历日短格式（网格小字）：廿四 / 十月（初一显示月名）
            public string LunarFullText;  // 农历日完整格式（面板头部）：九月廿四
            public string TermName;       // 节气名或 null
            public string Festival;       // 节日名或 null
            public bool StatutoryRest;    // 法定假日
            public string Badge;          // "休"/"班"/null（含 holidays.txt 覆盖）
            public string GanZhiYear, GanZhiMonth, GanZhiDay, Animal;
        }

        public static DayInfo Get(DateTime d)
        {
            d = d.Date;
            DayInfo di = new DayInfo();
            int ly, lm, ld; bool leap;
            ToLunar(d, out ly, out lm, out leap, out ld);
            di.LunarText = leap ? "闰" + LunarMonths[lm - 1]
                                : (ld == 1 ? LunarMonths[lm - 1] : LunarDays[ld - 1]);
            di.LunarFullText = (leap ? "闰" : "") + LunarMonths[lm - 1] + LunarDays[ld - 1];
            di.TermName = TermOfDay(d);
            di.Festival = SolarFestivalName(d) ?? LunarFestivalName(d);
            di.StatutoryRest = IsStatutoryRest(d);

            int gz = Mod((int)(d - new DateTime(1949, 10, 1)).Days, 60); // 1949-10-01 = 甲子日
            di.GanZhiDay = Stems[gz % 10] + Branches[gz % 12];
            di.GanZhiYear = Stems[Mod(ly - 4, 10)] + Branches[Mod(ly - 4, 12)];
            di.Animal = Animals[Mod(ly - 4, 12)];
            di.GanZhiMonth = GanZhiMonthName(d);

            string ov = OverrideFor(d);
            di.Badge = ov ?? (di.StatutoryRest ? "休" : null);
            return di;
        }

        // ---- 农历 ----

        private static void ToLunar(DateTime d, out int year, out int month, out bool leap, out int day)
        {
            year = Lunar.GetYear(d);
            month = Lunar.GetMonth(d);
            day = Lunar.GetDayOfMonth(d);
            int lm = Lunar.GetLeapMonth(year); // 闰月的“月序号”(1~13)，0=无闰月
            leap = lm != 0 && month == lm;
            if (lm != 0 && month > lm) month--;
        }

        // 农历 lunarYear 年 m 月 day 日对应的公历日期（不支持闰月查询）
        public static DateTime LunarDate(int lunarYear, int m, int day)
        {
            DateTime d = CnyDate(lunarYear);
            for (int i = 0; i < 400; i++)
            {
                int yy, mm, dd; bool leap;
                ToLunar(d, out yy, out mm, out leap, out dd);
                if (yy > lunarYear) break;
                if (yy == lunarYear && mm == m && dd == day && !leap) return d;
                d = d.AddDays(1);
            }
            throw new Exception("未找到农历日期 " + lunarYear + "-" + m + "-" + day);
        }

        // lunarYear 年正月初一（春节）公历日期
        public static DateTime CnyDate(int lunarYear)
        {
            DateTime hit;
            if (_cnyCache.TryGetValue(lunarYear, out hit)) return hit;
            DateTime d = new DateTime(lunarYear, 1, 15);
            while (d <= new DateTime(lunarYear, 3, 10))
            {
                int y, m, day; bool leap;
                ToLunar(d, out y, out m, out leap, out day);
                if (y == lunarYear && m == 1 && day == 1 && !leap) { _cnyCache[lunarYear] = d; return d; }
                d = d.AddDays(1);
            }
            throw new Exception("未找到春节 " + lunarYear);
        }

        // ---- 节气 ----

        public static DateTime TermDate(int year, string termName)
        {
            string key = year + termName;
            DateTime dt;
            if (_termCache.TryGetValue(key, out dt)) return dt;
            int idx = Array.IndexOf(TermNames, termName);
            int y = year % 100;
            int day = (int)(y * 0.2422 + TermC[idx]) - y / 4;
            dt = new DateTime(year, TermMonth[idx], day);
            _termCache[key] = dt;
            return dt;
        }

        private static string TermOfDay(DateTime d)
        {
            DateTime[] arr;
            if (!_termYearCache.TryGetValue(d.Year, out arr))
            {
                arr = new DateTime[24];
                for (int i = 0; i < 24; i++) arr[i] = TermDate(d.Year, TermNames[i]);
                _termYearCache[d.Year] = arr;
            }
            for (int i = 0; i < 24; i++)
                if (arr[i] == d.Date) return TermNames[i];
            return null;
        }

        // ---- 干支 ----

        // 干支月以节气为界（寅月从立春起），月干按“五虎遁”由年干推得；
        // 干支年以立春为界。参考：2026-11-02（立冬前）= 丙午年 戊戌月。
        public static string GanZhiMonthName(DateTime d)
        {
            // 12 节：小寒、立春、惊蛰、清明、立夏、芒种、小暑、立秋、白露、寒露、立冬、大雪
            string[] jie = { "小寒", "立春", "惊蛰", "清明", "立夏", "芒种", "小暑", "立秋", "白露", "寒露", "立冬", "大雪" };
            int[] jieMonthIndex = { 11, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 }; // 寅=0
            DateTime best = DateTime.MinValue;
            int jieIdx = -1;
            for (int y = d.Year - 1; y <= d.Year; y++)
                for (int i = 0; i < 12; i++)
                {
                    DateTime t = TermDate(y, jie[i]);
                    if (t <= d.Date && t > best) { best = t; jieIdx = i; }
                }
            int m = jieMonthIndex[jieIdx];
            int gzYear = d.Date >= TermDate(d.Year, "立春") ? d.Year : d.Year - 1;
            int ystem = Mod(gzYear - 4, 10);
            int first = (ystem % 5) * 2 + 2; // 五虎遁：甲己→丙寅、乙庚→戊寅、丙辛→庚寅…
            int stem = (first + m) % 10;
            int branch = (2 + m) % 12;
            return Stems[stem] + Branches[branch];
        }

        // ---- 节日 ----

        private static string SolarFestivalName(DateTime d)
        {
            switch (d.Month * 100 + d.Day)
            {
                case 101: return "元旦";
                case 214: return "情人节";
                case 308: return "妇女节";
                case 312: return "植树节";
                case 501: return "劳动节";
                case 504: return "青年节";
                case 601: return "儿童节";
                case 701: return "建党节";
                case 801: return "建军节";
                case 910: return "教师节";
                case 1001: return "国庆节";
                case 1224: return "平安夜";
                case 1225: return "圣诞节";
            }
            if (d.Month == 11 && d.DayOfWeek == DayOfWeek.Thursday && (d.Day - 1) / 7 == 3) return "感恩节";
            return null;
        }

        private static string LunarFestivalName(DateTime d)
        {
            int y, m, day; bool leap;
            ToLunar(d, out y, out m, out leap, out day);
            if (leap) return null;
            if (m == 1 && day == 1) return "春节";
            if (m == 1 && day == 15) return "元宵";
            if (m == 2 && day == 2) return "龙抬头";
            if (m == 5 && day == 5) return "端午";
            if (m == 7 && day == 7) return "七夕";
            if (m == 7 && day == 15) return "中元";
            if (m == 8 && day == 15) return "中秋";
            if (m == 9 && day == 9) return "重阳";
            if (m == 12 && day == 8) return "腊八";
            if (d.Date == CnyDate(y + 1).AddDays(-1)) return "除夕";
            return null;
        }

        // ---- 法定假日（固定 13 天，不包含每年宣布的调休；调休请用 holidays.txt）----
        // 依据 2025 年起施行的放假办法：元旦1、春节4(除夕+初一~初三)、清明1、
        // 劳动节2(5.1~5.2)、端午1、中秋1、国庆3。
        public static bool IsStatutoryRest(DateTime d)
        {
            if (d.Month == 1 && d.Day == 1) return true;   // 元旦
            if (d.Month == 5 && d.Day == 1) return true;   // 劳动节
            if (d.Month == 10 && d.Day == 1) return true;  // 国庆
            if (d.Date == TermDate(d.Year, "清明")) return true; // 清明
            int y, m, day; bool leap;
            ToLunar(d, out y, out m, out leap, out day);
            if (leap) return false;
            if (m == 1 && day >= 1 && day <= 3) return true;     // 春节
            if (d.Date == CnyDate(y + 1).AddDays(-1)) return true; // 除夕
            if (m == 5 && day == 5) return true;                 // 端午
            if (m == 8 && day == 15) return true;                // 中秋
            return false;
        }

        // ---- holidays.txt 自定义 休/班 ----

        public static string OverrideFilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ChinaClock", "holidays.txt");
            }
        }

        public static void EnsureOverrideFile()
        {
            try
            {
                if (File.Exists(OverrideFilePath)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(OverrideFilePath));
                File.WriteAllText(OverrideFilePath,
                    "# 节假日自定义配置（每年国务院公布调休安排后，在此补充即可）\r\n" +
                    "# 内置规则已自动标注法定假日（元旦/春节/清明/五一/端午/中秋/国庆），无需在此重复。\r\n" +
                    "# 格式：每行一条，以 休 或 班 开头，后跟日期，可带结束日期表示范围：\r\n" +
                    "#   休 2026-10-01 2026-10-08\r\n" +
                    "#   班 2026-09-27\r\n");
            }
            catch { }
        }

        private static void LoadOverrides()
        {
            var dict = new Dictionary<DateTime, string>();
            try
            {
                EnsureOverrideFile();
                foreach (string raw in File.ReadAllLines(OverrideFilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    string tag = parts[0] == "班" ? "班" : (parts[0] == "休" ? "休" : null);
                    if (tag == null) continue;
                    DateTime start, end;
                    if (!DateTime.TryParse(parts[1], out start)) continue;
                    end = parts.Length >= 3 && DateTime.TryParse(parts[2], out end) ? end : start;
                    if (end < start || (end - start).TotalDays > 60) continue;
                    for (DateTime t = start.Date; t <= end.Date; t = t.AddDays(1))
                        dict[t] = tag;
                }
            }
            catch { }
            _overrides = dict;
        }

        private static string OverrideFor(DateTime d)
        {
            if (_overrides == null) LoadOverrides();
            string s;
            return _overrides.TryGetValue(d.Date, out s) ? s : null;
        }

        // 面板每次打开时刷新一次配置文件
        public static void ReloadOverrides()
        {
            _overrides = null;
        }

        private static int Mod(int a, int b)
        {
            int r = a % b;
            return r < 0 ? r + b : r;
        }
    }

    // ============================ 日历面板 ============================

    internal sealed class CalendarPanel : Form
    {
        private static readonly TimeZoneInfo Tz = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        private static readonly string[] WeekNames = { "一", "二", "三", "四", "五", "六", "日" };
        private static readonly string[] WeekFull = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };

        private DateTime _selected;   // 头部展示的日期
        private DateTime _viewMonth;  // 当前查看的月份
        private readonly Timer _timer = new Timer();
        private float _s = 1f;
        private int _pad, _headerH, _weekH, _cellH, _cellW, _footerH;
        private Font _fTime, _fDate, _fInfo, _fWeek, _fDay, _fSmall, _fFoot;
        private Rectangle _gridRect, _btnPrev, _btnNext, _btnToday;

        private readonly Color _cText = Color.FromArgb(51, 51, 51);
        private readonly Color _cSub = Color.FromArgb(130, 130, 130);
        private readonly Color _cRed = Color.FromArgb(224, 72, 62);
        private readonly Color _cBlue = Color.FromArgb(58, 123, 213);
        private readonly Color _cAccent = Color.FromArgb(68, 114, 196);
        private readonly Color _cDim = Color.FromArgb(205, 205, 205);

        public CalendarPanel()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Color.White;

            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _s = g.DpiX / 96f;
            _pad = Scale(14); _headerH = Scale(88); _weekH = Scale(26);
            _cellH = Scale(60); _cellW = Scale(58); _footerH = Scale(42);
            int w = _pad * 2 + _cellW * 7;
            int h = _pad + _headerH + _weekH + _cellH * 6 + _footerH + Scale(8);
            Size = new Size(w, h);
            using (GraphicsPath p = RoundedPath(new Rectangle(0, 0, w - 1, h - 1), Scale(10)))
                Region = new Region(p);

            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - w - Scale(10), wa.Bottom - h - Scale(8));

            DateTime now = Now();
            _selected = now.Date;
            _viewMonth = new DateTime(now.Year, now.Month, 1);

            _fTime = new Font("Segoe UI Semibold", 19F);
            _fDate = new Font("Microsoft YaHei UI", 10.5F);
            _fInfo = new Font("Microsoft YaHei UI", 8.5F);
            _fWeek = new Font("Microsoft YaHei UI", 9.5F);
            _fDay = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            _fSmall = new Font("Microsoft YaHei UI", 8F);
            _fFoot = new Font("Microsoft YaHei UI", 9.5F);

            _timer.Interval = 500;
            _timer.Tick += delegate { Invalidate(); };
            _timer.Start();
        }

        private int Scale(int v) { return (int)Math.Round(v * _s); }

        private static DateTime Now()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tz);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80;  // WS_EX_TOOLWINDOW
                cp.ClassStyle |= 0x20000;
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            CalendarData.ReloadOverrides();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            Point p = e.Location;
            if (_btnPrev.Contains(p)) { _viewMonth = _viewMonth.AddMonths(-1); Invalidate(); return; }
            if (_btnNext.Contains(p)) { _viewMonth = _viewMonth.AddMonths(1); Invalidate(); return; }
            if (_btnToday.Contains(p)) { GoToday(); return; }
            if (_gridRect.Contains(p))
            {
                int col = (p.X - _gridRect.X) / _cellW;
                int row = (p.Y - _gridRect.Y) / _cellH;
                _selected = GridStart().AddDays(row * 7 + col).Date;
                Invalidate();
            }
        }

        private DateTime GridStart()
        {
            DateTime first = new DateTime(_viewMonth.Year, _viewMonth.Month, 1);
            return first.AddDays(-(((int)first.DayOfWeek + 6) % 7)); // 周一起始
        }

        private void GoToday()
        {
            DateTime n = Now().Date;
            _selected = n;
            _viewMonth = new DateTime(n.Year, n.Month, 1);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = ClientSize.Width;

            using (Pen pen = new Pen(Color.FromArgb(226, 226, 226)))
                g.DrawPath(pen, RoundedPath(new Rectangle(0, 0, w - 1, ClientSize.Height - 1), Scale(10)));

            // 头部：中国时间 + 选中日期 + 农历干支
            CalendarData.DayInfo di = CalendarData.Get(_selected);
            int x = _pad, y = _pad;
            TextRenderer.DrawText(g, Now().ToString("HH:mm:ss"), _fTime, new Point(x, y), _cText);
            string dateLine = _selected.ToString("yyyy年M月d日 ") + WeekFull[(int)_selected.DayOfWeek]
                + (_selected == Now().Date ? "　今天" : "");
            TextRenderer.DrawText(g, dateLine, _fDate, new Point(x, y + Scale(36)), _cText);
            string infoLine = "农历 " + di.LunarFullText + " · " + di.GanZhiYear + di.Animal + "年 · "
                + di.GanZhiMonth + "月 · " + di.GanZhiDay + "日";
            if (di.Badge != null) infoLine += "　" + di.Badge;
            TextRenderer.DrawText(g, infoLine, _fInfo, new Point(x, y + Scale(60)), _cSub);

            // 星期表头（周一起始）
            int gy = _pad + _headerH;
            _gridRect = new Rectangle(_pad, gy + _weekH, _cellW * 7, _cellH * 6);
            for (int i = 0; i < 7; i++)
            {
                Rectangle r = new Rectangle(_pad + _cellW * i, gy, _cellW, _weekH);
                TextRenderer.DrawText(g, WeekNames[i], _fWeek, r, i >= 5 ? _cRed : _cSub,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            DateTime start = GridStart();
            for (int i = 0; i < 42; i++)
                DrawCell(g, i % 7, i / 7, start.AddDays(i));

            // 底部：月份切换 + 回到今天
            int fy = _gridRect.Bottom + Scale(6);
            _btnPrev = new Rectangle(x, fy, Scale(28), Scale(24));
            _btnNext = new Rectangle(x + Scale(32), fy, Scale(28), Scale(24));
            _btnToday = new Rectangle(w - _pad - Scale(76), fy, Scale(76), Scale(24));
            DrawMiniButton(g, _btnPrev, "‹");
            DrawMiniButton(g, _btnNext, "›");
            DrawMiniButton(g, _btnToday, "回到今天");
            Rectangle labelRect = new Rectangle(_btnNext.Right + Scale(10), fy,
                _btnToday.Left - _btnNext.Right - Scale(20), _btnToday.Height);
            TextRenderer.DrawText(g, _viewMonth.ToString("yyyy年M月"), _fFoot, labelRect, _cText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void DrawCell(Graphics g, int col, int row, DateTime d)
        {
            Rectangle r = new Rectangle(_gridRect.X + _cellW * col, _gridRect.Y + _cellH * row, _cellW, _cellH);
            bool other = d.Month != _viewMonth.Month;
            CalendarData.DayInfo di = CalendarData.Get(d);
            bool weekend = d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;

            if (d == _selected)
                using (SolidBrush b = new SolidBrush(Color.FromArgb(238, 243, 252)))
                    g.FillRectangle(b, r);

            Color numColor = other ? _cDim : (di.StatutoryRest || weekend ? _cRed : _cText);
            string sub = di.Festival ?? di.TermName ?? di.LunarText;
            Color subColor = other ? Color.FromArgb(222, 222, 222)
                : di.Festival != null ? _cRed
                : di.TermName != null ? _cBlue
                : Color.FromArgb(166, 166, 166);

            Rectangle numRect = new Rectangle(r.X, r.Y + Scale(6), r.Width, Scale(22));
            if (d == Now().Date)
            {
                int dsz = Scale(24);
                Rectangle c = new Rectangle(r.X + (r.Width - dsz) / 2, r.Y + Scale(4), dsz, dsz);
                using (SolidBrush b = new SolidBrush(_cAccent)) g.FillEllipse(b, c);
                TextRenderer.DrawText(g, d.Day.ToString(), _fDay, c, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            else
            {
                TextRenderer.DrawText(g, d.Day.ToString(), _fDay, numRect, numColor, TextFormatFlags.HorizontalCenter);
            }

            Rectangle subRect = new Rectangle(r.X, r.Y + Scale(30), r.Width, Scale(16));
            TextRenderer.DrawText(g, sub, _fSmall, subRect, subColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);

            if (!other && di.Badge != null)
            {
                bool work = di.Badge == "班";
                Color bg = work ? Color.FromArgb(255, 243, 224) : Color.FromArgb(253, 236, 234);
                Color fg = work ? Color.FromArgb(230, 126, 34) : _cRed;
                Rectangle br = new Rectangle(r.Right - Scale(20), r.Y + Scale(3), Scale(17), Scale(13));
                using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, br);
                TextRenderer.DrawText(g, di.Badge, _fSmall, br, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        private void DrawMiniButton(Graphics g, Rectangle r, string text)
        {
            using (SolidBrush b = new SolidBrush(Color.FromArgb(243, 244, 246)))
                g.FillRectangle(b, r);
            TextRenderer.DrawText(g, text, _fFoot, r, _cText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
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
            _timer.Stop();
            _timer.Dispose();
            foreach (Font f in new[] { _fTime, _fDate, _fInfo, _fWeek, _fDay, _fSmall, _fFoot })
                if (f != null) f.Dispose();
            base.OnFormClosed(e);
        }
    }

    // ============================ 任务栏时钟 ============================

    internal sealed class ClockForm : Form
    {
        private const int HWND_TOPMOST = -1;
        private const int SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;
        private const int EVENT_SYSTEM_FOREGROUND = 0x0003; // 前台窗口切换
        private const int EVENT_OBJECT_REORDER = 0x8004;    // 窗口 Z 序变化
        private const int WINEVENT_OUTOFCONTEXT = 0x0;

        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, int after, int x, int y, int cx, int cy, int flags);

        private delegate void WinEventDelegate(IntPtr hHook, int evt, IntPtr hwnd, int idObject, int idChild, int dwEventThread, int dwmsEventTime);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(int eventMin, int eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, int idProcess, int idThread, int dwFlags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

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
        private DateTime _lastToggle = DateTime.MinValue;
        private DateTime _panelClosedAt = DateTime.MinValue;
        private DateTime _lastAssert = DateTime.MinValue;
        private CalendarPanel _panel;
        private WinEventDelegate _hookProc;  // 保持引用防 GC 回收，否则钩子会失效
        private IntPtr _hookFore = IntPtr.Zero, _hookReorder = IntPtr.Zero;
        private IntPtr _trayHwnd = IntPtr.Zero;
        private readonly bool _openPanel;

        public ClockForm(bool openPanel)
        {
            _openPanel = openPanel;
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

            // 点击任务栏时 Windows 会把任务栏窗口顶到置顶层最前，盖住本时钟（原生时钟
            // 露出，要等定位定时器 4 秒后才能顶回来）。挂系统事件钩子，前台切换或任务
            // 栏 Z 序变化一到就立刻顶回最上层，让原生时钟来不及露出来。
            _trayHwnd = FindWindow("Shell_TrayWnd", null);
            _hookProc = WinEventProc;
            _hookFore = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _hookProc, 0, 0, WINEVENT_OUTOFCONTEXT);
            _hookReorder = SetWinEventHook(EVENT_OBJECT_REORDER, EVENT_OBJECT_REORDER, IntPtr.Zero, _hookProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        }

        private void WinEventProc(IntPtr hHook, int evt, IntPtr hwnd, int idObject, int idChild, int dwEventThread, int dwmsEventTime)
        {
            // 前台切换（点任务栏、开关托盘浮层都会触发）或任务栏自身 Z 序变化
            if (evt != EVENT_SYSTEM_FOREGROUND && hwnd != _trayHwnd) return;
            AssertTopmost();
        }

        private void AssertTopmost()
        {
            if (!IsHandleCreated) return;
            if ((DateTime.Now - _lastAssert).TotalMilliseconds < 150) return; // 事件风暴节流
            _lastAssert = DateTime.Now;
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_openPanel) TogglePanel();
            SampleBackgroundAsync(); // 首次显示后立刻按"可见状态"重新采底色（不等 15s 定时器）
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
            _trayHwnd = tray;
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

        // 任务栏底色采样：在后台线程用 CopyFromScreen 抓取任务栏中线像素，众数统计出背景色。
        // Win11 任务栏是半透明材质，颜色随背后壁纸平滑渐变，整条任务的众数可能取到
        // 远离本窗口的颜色（比如壁纸左白右蓝时整条众数是白、本窗口处却是蓝），造成色差。
        // 因此优先取"紧贴本窗口的相邻像素条"，只有其众数占比不足（被托盘图标占据）时
        // 才逐级放宽到整条。注意：绝不能在 UI 线程逐点 GetPixel，会卡住界面。
        private void SampleBackgroundAsync()
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return;
            RECT tr;
            GetWindowRect(tray, out tr);
            if (tr.Bottom - tr.Top < 20) return;
            int y = tr.Top + (tr.Bottom - tr.Top) / 2;
            bool vis = IsHandleCreated && Visible;

            var strips = new List<int[]>();
            if (vis)
            {
                int l = Bounds.Left, r = Bounds.Right;
                // 左侧相邻条逐级收窄：紧邻区域可能挤着托盘图标，窄条更容易避开图标、
                // 且颜色最接近色块底下（半透明任务栏随壁纸渐变）
                strips.Add(new[] { l - 100, l - 8 });
                strips.Add(new[] { l - 60, l - 8 });
                strips.Add(new[] { l - 36, l - 8 });
                strips.Add(new[] { r + 2, tr.Right - 2 });   // 右侧边缘缝隙
                strips.Add(new[] { l - 20, l - 8 });
            }
            strips.Add(new[] { tr.Left + 4, vis ? Bounds.Left - 8 : tr.Right - 4 }); // 整条兜底

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (int[] s in strips)
                {
                    int x0 = Math.Max(tr.Left + 2, s[0]);
                    int x1 = Math.Min(tr.Right - 2, s[1]);
                    int w = x1 - x0 + 1;
                    if (w < 4) continue;
                    Color col;
                    if (!TrySampleStrip(x0, y, w, out col)) continue;
                    if (IsHandleCreated)
                        BeginInvoke((MethodInvoker)delegate { ApplyBackground(col); });
                    return;
                }
            });
        }

        private static bool TrySampleStrip(int x0, int y, int w, out Color col)
        {
            col = Color.Empty;
            try
            {
                var counts = new Dictionary<int, long[]>(); // key -> {数量, R和, G和, B和}
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
                        // 8 级量化分组：半透明任务栏的渐变（±2/通道）会稳定落在同一桶，
                        // 保证众数占比可信；组内再取精确平均，消除量化误差带来的色差
                        int key = ((px >> 16) & 0xF8) << 16 | ((px >> 8) & 0xF8) << 8 | (px & 0xF8);
                        long[] acc;
                        if (!counts.TryGetValue(key, out acc)) acc = new long[4];
                        acc[0]++; acc[1] += (px >> 16) & 255; acc[2] += (px >> 8) & 255; acc[3] += px & 255;
                        counts[key] = acc;
                    }
                }

                long bestKey = 0, bestN = 0;
                foreach (var kv in counts)
                    if (kv.Value[0] > bestN) { bestN = kv.Value[0]; bestKey = kv.Key; }
                if (bestN * 5 < w * 3) return false; // 背景色占比不足 60%，采样条不可信
                long[] a = counts[(int)bestKey];
                col = Color.FromArgb((int)(a[1] / a[0]), (int)(a[2] / a[0]), (int)(a[3] / a[0]));
                return true;
            }
            catch { return false; }
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
            if (e.Button == MouseButtons.Left && (DateTime.Now - _lastToggle).TotalMilliseconds > 600)
            {
                _lastToggle = DateTime.Now;
                TogglePanel();
            }
        }

        // 左键 = 打开/关闭日历面板
        private void TogglePanel()
        {
            if (_panel != null && !_panel.IsDisposed && _panel.Visible)
            {
                _panel.Close();
                return;
            }
            // 点击时钟的瞬间，面板可能先因"失焦自动关闭"关掉、随后本点击又把它
            // 重新打开，导致看起来永远关不掉；忽略紧随关闭之后的这次点击。
            if ((DateTime.Now - _panelClosedAt).TotalMilliseconds < 600) return;
            _panel = new CalendarPanel();
            _panel.FormClosed += delegate { _panelClosedAt = DateTime.Now; };
            _panel.Show();
            _panel.Activate();
        }

        private ToolStripMenuItem _miAutoStart;

        private void BuildMenu()
        {
            var miPanel = new ToolStripMenuItem("打开日历面板");
            miPanel.Click += delegate { TogglePanel(); };

            _miAutoStart = new ToolStripMenuItem("开机自启") { CheckOnClick = true, Checked = IsAutoStart() };
            _miAutoStart.Click += delegate { SetAutoStart(_miAutoStart.Checked); };

            var miHoliday = new ToolStripMenuItem("打开节假日配置");
            miHoliday.Click += delegate
            {
                CalendarData.EnsureOverrideFile();
                try { Process.Start("notepad.exe", CalendarData.OverrideFilePath); }
                catch (Exception ex)
                {
                    MessageBox.Show("打开配置失败：" + ex.Message, "中国时间",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            var miFit = new ToolStripMenuItem("重新校准位置");
            miFit.Click += delegate { FitToNativeClock(Rectangle.Empty); };

            var miExit = new ToolStripMenuItem("退出（恢复系统时钟）");
            miExit.Click += delegate { Close(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add(miPanel);
            menu.Items.Add(miHoliday);
            menu.Items.Add(_miAutoStart);
            menu.Items.Add(miFit);
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
            if (_hookFore != IntPtr.Zero) UnhookWinEvent(_hookFore);
            if (_hookReorder != IntPtr.Zero) UnhookWinEvent(_hookReorder);
            _hookProc = null;
            if (_panel != null && !_panel.IsDisposed) _panel.Close();
            if (_f1 != null) _f1.Dispose();
            if (_f2 != null) _f2.Dispose();
            base.OnFormClosed(e);
        }
    }
}
