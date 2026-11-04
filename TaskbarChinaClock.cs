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
using System.Net;
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
            string[] a = args ?? new string[0];
            if (Array.IndexOf(a, "--selftest") >= 0)
            {
                SelfTest.Run();
                return;
            }
            if (Array.IndexOf(a, "--fetch-holidays") >= 0)
            {
                // 测试入口：同步拉取 holiday-cn 并把结果写入 fetch-result.txt（winexe 无控制台）
                Settings.Load();
                bool ok = HolidayFeed.FetchOnce();
                string report = "ok=" + ok + " count=" + (HolidayFeed.Data != null ? HolidayFeed.Data.Count : 0);
                if (HolidayFeed.Data != null)
                {
                    List<DateTime> ks = new List<DateTime>(HolidayFeed.Data.Keys);
                    ks.Sort();
                    foreach (DateTime k in ks)
                        report += "\r\n" + k.ToString("yyyy-MM-dd") + " " + HolidayFeed.Data[k];
                }
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fetch-result.txt"), report);
                return;
            }
            bool openPanel = Array.IndexOf(a, "--show-panel") >= 0;
            bool openSettings = Array.IndexOf(a, "--settings") >= 0;
            bool agendaDemo = Array.IndexOf(a, "--agenda-demo") >= 0;
            bool listDemo = Array.IndexOf(a, "--list-demo") >= 0;
            bool pickerDemo = Array.IndexOf(a, "--picker-demo") >= 0;
            bool dayDemo = Array.IndexOf(a, "--day-detail") >= 0;
            // 调试入口：--pretend-dock:left|top|right 强制按指定停靠边定位（无需真的挪任务栏）
            foreach (string x in a)
                if (x.StartsWith("--pretend-dock:", StringComparison.OrdinalIgnoreCase))
                {
                    string v = x.Substring("--pretend-dock:".Length).Trim();
                    if (v.Equals("left", StringComparison.OrdinalIgnoreCase)) TaskbarLayout.PretendDock = DockEdge.Left;
                    else if (v.Equals("right", StringComparison.OrdinalIgnoreCase)) TaskbarLayout.PretendDock = DockEdge.Right;
                    else if (v.Equals("top", StringComparison.OrdinalIgnoreCase)) TaskbarLayout.PretendDock = DockEdge.Top;
                    else TaskbarLayout.PretendDock = DockEdge.Bottom;
                }

            bool createdNew;
            using (new System.Threading.Mutex(true, "ChinaClock_SingleInstance", out createdNew))
            {
                if (!createdNew) return; // 已有实例
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ClockForm(openPanel, openSettings, agendaDemo, listDemo, pickerDemo, dayDemo));
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
            Check(lines, ref fails, "普通工作日无角标 2026-11-03", CalendarData.Get(new DateTime(2026, 11, 3)).Badge == null,
                CalendarData.Get(new DateTime(2026, 11, 3)).Badge ?? "(null)");
            Check(lines, ref fails, "周末默认休 2026-11-07(周六)", CalendarData.Get(new DateTime(2026, 11, 7)).Badge == "休",
                CalendarData.Get(new DateTime(2026, 11, 7)).Badge ?? "(null)");
            Check(lines, ref fails, "未公布年份无法定角标 2027-02-05(除夕,周五)", CalendarData.Get(new DateTime(2027, 2, 5)).Badge == null,
                CalendarData.Get(new DateTime(2027, 2, 5)).Badge ?? "(null)");
            Check(lines, ref fails, "未公布年份无法定角标 2027-02-08(初三,周一)", CalendarData.Get(new DateTime(2027, 2, 8)).Badge == null,
                CalendarData.Get(new DateTime(2027, 2, 8)).Badge ?? "(null)");
            Check(lines, ref fails, "未公布年份周末仍休 2027-02-06(春节,周六)", CalendarData.Get(new DateTime(2027, 2, 6)).Badge == "休",
                CalendarData.Get(new DateTime(2027, 2, 6)).Badge ?? "(null)");

            // holiday-cn 解析（含节日名）+ ISO 周数 + 日程行解析
            string sampleJson = "{\"year\":2027,\"days\":[{\"name\":\"元旦\",\"date\":\"2027-01-01\",\"isOffDay\":true},"
                + "{\"name\":\"春节\",\"date\":\"2027-02-08\",\"isOffDay\":true},{\"name\":\"调休上班\",\"date\":\"2027-02-06\",\"isOffDay\":false}]}";
            Dictionary<DateTime, string> names = new Dictionary<DateTime, string>();
            Dictionary<DateTime, string> parsed = CalendarData.ParseHolidayCn(sampleJson, names);
            Check(lines, ref fails, "holiday-cn 解析条数 = 3", parsed.Count == 3, parsed.Count.ToString());
            Check(lines, ref fails, "holiday-cn 2027-01-01 = 休", parsed.ContainsKey(new DateTime(2027, 1, 1)) && parsed[new DateTime(2027, 1, 1)] == "休", "");
            Check(lines, ref fails, "holiday-cn 2027-02-06 = 班", parsed.ContainsKey(new DateTime(2027, 2, 6)) && parsed[new DateTime(2027, 2, 6)] == "班", "");
            Check(lines, ref fails, "holiday-cn 名称解析 春节", names.ContainsKey(new DateTime(2027, 2, 8)) && names[new DateTime(2027, 2, 8)] == "春节", "");
            Check(lines, ref fails, "ISO 周数 2026-01-01 = 1", CalendarData.IsoWeek(new DateTime(2026, 1, 1)) == 1,
                CalendarData.IsoWeek(new DateTime(2026, 1, 1)).ToString());
            Check(lines, ref fails, "ISO 周数 2027-01-01 = 53", CalendarData.IsoWeek(new DateTime(2027, 1, 1)) == 53,
                CalendarData.IsoWeek(new DateTime(2027, 1, 1)).ToString());

            AgendaItem it1 = AgendaStore.ParseLine("农历 08-15|妈妈生日");
            Check(lines, ref fails, "日程解析 农历每年", it1 != null && it1.LunarMonth == 8 && it1.LunarDay == 15 && it1.Yearly,
                it1 == null ? "(null)" : it1.LunarMonth + "/" + it1.LunarDay);
            AgendaItem it2 = AgendaStore.ParseLine("每年 05-20|15:00 结婚纪念日");
            Check(lines, ref fails, "日程解析 公历每年+时间", it2 != null && it2.SolarMonth == 5 && it2.SolarDay == 20 && it2.Time == "15:00",
                it2 == null ? "(null)" : it2.SolarMonth + "/" + it2.SolarDay + " " + (it2.Time ?? ""));
            AgendaItem it3 = AgendaStore.ParseLine("2026-11-04|提交周报");
            Check(lines, ref fails, "日程解析 一次性", it3 != null && it3.Date == new DateTime(2026, 11, 4) && !it3.Yearly && it3.Time == null,
                it3 == null ? "(null)" : it3.Date.ToString("yyyy-MM-dd"));
            Check(lines, ref fails, "日程解析 非法行返回 null", AgendaStore.ParseLine("随便一行") == null, "");
            Check(lines, ref fails, "角标原因 国庆节(在线名)", CalendarData.BadgeReason(new DateTime(2026, 10, 1), "休", null) != null,
                CalendarData.BadgeReason(new DateTime(2026, 10, 1), "休", null) ?? "(null)");

            // ---- F1：黄历（Almanac 纯函数）----
            Check(lines, ref fails, "六十甲子序 甲子=0", Almanac.GanZhiIndex("甲子") == 0,
                Almanac.GanZhiIndex("甲子").ToString());
            Check(lines, ref fails, "六十甲子序 甲申=20", Almanac.GanZhiIndex("甲申") == 20,
                Almanac.GanZhiIndex("甲申").ToString());
            Check(lines, ref fails, "六十甲子序 庚辰=16", Almanac.GanZhiIndex("庚辰") == 16,
                Almanac.GanZhiIndex("庚辰").ToString());
            Check(lines, ref fails, "纳音 甲子=海中金", Almanac.Nayin("甲子") == "海中金", Almanac.Nayin("甲子"));
            Check(lines, ref fails, "纳音 甲申=泉中水", Almanac.Nayin("甲申") == "泉中水", Almanac.Nayin("甲申"));
            Check(lines, ref fails, "纳音 庚辰=白蜡金", Almanac.Nayin("庚辰") == "白蜡金", Almanac.Nayin("庚辰"));
            Check(lines, ref fails, "纳音 壬戌=大海水", Almanac.Nayin("壬戌") == "大海水", Almanac.Nayin("壬戌"));
            Check(lines, ref fails, "冲煞 甲申=冲虎（甲寅）·煞南", Almanac.ChongSha("甲申") == "冲虎（甲寅）· 煞南",
                Almanac.ChongSha("甲申") ?? "(null)");
            Check(lines, ref fails, "建星 甲子日/酉月=平", Almanac.Officer("甲子", "癸酉") == "平",
                Almanac.Officer("甲子", "癸酉") ?? "(null)");
            bool yl1949;
            string zs1949 = Almanac.ZhiShen("甲子", "癸酉", out yl1949);
            Check(lines, ref fails, "值神 甲子日/酉月=司命(黄道)", zs1949 == "司命" && yl1949, zs1949 + (yl1949 ? "/黄道" : "/黑道"));
            bool structOk = true; string structBad = "";
            string[] offNames = { "建", "除", "满", "平", "定", "执", "破", "危", "成", "收", "开", "闭" };
            string[] zsNames = { "青龙", "明堂", "天刑", "朱雀", "金匮", "天德", "白虎", "玉堂", "天牢", "玄武", "司命", "勾陈" };
            for (DateTime t = new DateTime(2026, 1, 1); t.Year == 2026 && structOk; t = t.AddDays(1))
            {
                CalendarData.DayInfo di2 = CalendarData.Get(t);
                string off2 = Almanac.Officer(di2.GanZhiDay, di2.GanZhiMonth);
                bool y2; string zs2 = Almanac.ZhiShen(di2.GanZhiDay, di2.GanZhiMonth, out y2);
                if (Array.IndexOf(offNames, off2) < 0 || Almanac.Yi(off2) == null || Almanac.Ji(off2) == null ||
                    Array.IndexOf(zsNames, zs2) < 0 || Almanac.Nayin(di2.GanZhiDay) == null)
                {
                    structOk = false; structBad = t.ToString("yyyy-MM-dd") + " " + di2.GanZhiDay + "/" + di2.GanZhiMonth;
                }
            }
            Check(lines, ref fails, "2026 全年黄历字段有效性", structOk, structBad);

            // ---- F1：任务栏停靠几何（合成矩形）----
            Rectangle scr = Rectangle.FromLTRB(0, 0, 1920, 1080);
            Check(lines, ref fails, "停靠检测 底部", TaskbarLayout.Detect(Rectangle.FromLTRB(0, 1040, 1920, 1080), scr) == DockEdge.Bottom, "");
            Check(lines, ref fails, "停靠检测 顶部", TaskbarLayout.Detect(Rectangle.FromLTRB(0, 0, 1920, 48), scr) == DockEdge.Top, "");
            Check(lines, ref fails, "停靠检测 左侧", TaskbarLayout.Detect(Rectangle.FromLTRB(0, 0, 58, 1080), scr) == DockEdge.Left, "");
            Check(lines, ref fails, "停靠检测 右侧", TaskbarLayout.Detect(Rectangle.FromLTRB(1862, 0, 1920, 1080), scr) == DockEdge.Right, "");
            Rectangle gLeft = TaskbarLayout.GuessClockRect(Rectangle.FromLTRB(0, 0, 58, 1080), DockEdge.Left);
            Check(lines, ref fails, "几何估算 左侧=底部锚定", gLeft.Bottom <= 1080 && gLeft.Top > 900 && gLeft.Left >= 0 && gLeft.Right <= 58, gLeft.ToString());
            Rectangle gTop = TaskbarLayout.GuessClockRect(Rectangle.FromLTRB(0, 0, 1920, 48), DockEdge.Top);
            Check(lines, ref fails, "几何估算 顶部=右端+垂直居中", gTop.Right <= 1920 && gTop.Top >= 0 && gTop.Bottom <= 48, gTop.ToString());
            Rectangle fLeft = TaskbarLayout.FitTarget(new Rectangle(6, 1000, 46, 60),
                Rectangle.FromLTRB(0, 0, 58, 1080), DockEdge.Left, 120, 64);
            Check(lines, ref fails, "覆盖定位 左侧=底锚+钳制", fLeft.Bottom <= 1079 && fLeft.Left >= 1, fLeft.ToString());
            Rectangle fBottom = TaskbarLayout.FitTarget(new Rectangle(1800, 1042, 100, 36),
                Rectangle.FromLTRB(0, 1040, 1920, 1080), DockEdge.Bottom, 110, 36);
            Check(lines, ref fails, "覆盖定位 底部=右锚+垂直居中",
                fBottom.Right <= 1919 && Math.Abs((fBottom.Top + fBottom.Height / 2) - 1060) <= 2, fBottom.ToString());
            DateTime nf; string nn;
            bool nfOk = CalendarData.NextFestival(new DateTime(2026, 11, 3), out nf, out nn);
            Check(lines, ref fails, "下一节日 2026-11-03 → 元旦", nfOk && nn == "元旦" && nf == new DateTime(2027, 1, 1),
                nn + " " + nf.ToString("yyyy-MM-dd"));

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

    // ============================ 设置 ============================

    // settings.ini（%APPDATA%\ChinaClock\settings.ini），零依赖 key=value；
    // 各窗体直接读静态字段，设置窗保存后立即生效
    internal static class Settings
    {
        public static bool ShowSeconds = true;
        public static bool ShowWeekday = true;
        public static bool ShowDateLine = true;
        public static string CustomTimeFormat = "";  // 非空时优先生效（DateTime 格式串）
        public static bool ShowSecondClock = false;  // 第二时区双时钟（A3）
        public static string SecondTzId = "UTC";     // Windows 时区 Id
        public static string SecondTzLabel = "UTC";
        public static bool ShowWeekNumbers = false;  // 日历左侧周数列（B2）
        public static bool ShowLocalClock = true;    // 本机时间行（F2）：电脑时区≠北京时间时显示
        public static int ThemeMode = 0;             // 0=跟随系统 1=浅色 2=深色（C1）
        public static string Accent = "#4472C4";     // 强调色（C3）
        public static bool AutoFetchHolidays = true; // B1：每日自动更新假日数据
        public static string HolidaysFetchedAt = "";

        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ChinaClock", "settings.ini");

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    int i = raw.IndexOf('=');
                    if (i <= 0) continue;
                    string k = raw.Substring(0, i).Trim();
                    string v = raw.Substring(i + 1).Trim();
                    switch (k)
                    {
                        case "ShowSeconds": ShowSeconds = GetBool(v, ShowSeconds); break;
                        case "ShowWeekday": ShowWeekday = GetBool(v, ShowWeekday); break;
                        case "ShowDateLine": ShowDateLine = GetBool(v, ShowDateLine); break;
                        case "CustomTimeFormat": CustomTimeFormat = v; break;
                        case "ShowSecondClock": ShowSecondClock = GetBool(v, ShowSecondClock); break;
                        case "SecondTzId": if (v.Length > 0) SecondTzId = v; break;
                        case "SecondTzLabel": SecondTzLabel = v; break;
                        case "ShowWeekNumbers": ShowWeekNumbers = GetBool(v, ShowWeekNumbers); break;
                        case "ShowLocalClock": ShowLocalClock = GetBool(v, ShowLocalClock); break;
                        case "ThemeMode": ThemeMode = GetInt(v, ThemeMode); break;
                        case "Accent": if (v.Length > 0) Accent = v; break;
                        case "AutoFetchHolidays": AutoFetchHolidays = GetBool(v, AutoFetchHolidays); break;
                        case "HolidaysFetchedAt": HolidaysFetchedAt = v; break;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                List<string> lines = new List<string>();
                lines.Add("ShowSeconds=" + ShowSeconds);
                lines.Add("ShowWeekday=" + ShowWeekday);
                lines.Add("ShowDateLine=" + ShowDateLine);
                lines.Add("CustomTimeFormat=" + CustomTimeFormat);
                lines.Add("ShowSecondClock=" + ShowSecondClock);
                lines.Add("SecondTzId=" + SecondTzId);
                lines.Add("SecondTzLabel=" + SecondTzLabel);
                lines.Add("ShowWeekNumbers=" + ShowWeekNumbers);
            lines.Add("ShowLocalClock=" + ShowLocalClock);
                lines.Add("ThemeMode=" + ThemeMode);
                lines.Add("Accent=" + Accent);
                lines.Add("AutoFetchHolidays=" + AutoFetchHolidays);
                lines.Add("HolidaysFetchedAt=" + HolidaysFetchedAt);
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch { }
        }

        private static bool GetBool(string v, bool fallback) { bool b; return bool.TryParse(v, out b) ? b : fallback; }
        private static int GetInt(string v, int fallback) { int i; return int.TryParse(v, out i) ? i : fallback; }
    }

    // ============================ 面板配色 ============================

    // C1 深色模式 / C3 强调色：浅色、深色两套基础色；选中/高亮底色由
    // 强调色与背景混合而来，换强调色自动跟随
    internal sealed class PanelPalette
    {
        public bool Dark;
        public Color Back, Border, Text, Sub, Red, Blue, Accent, Dim, DimSub, SubLunar,
            SelBg, Hover, BtnBg, BtnHot, RestBg, RestFg, WorkBg, WorkFg, Dot;

        public static PanelPalette Load()
        {
            bool dark;
            if (Settings.ThemeMode == 1) dark = false;
            else if (Settings.ThemeMode == 2) dark = true;
            else dark = !SystemThemeLight();

            PanelPalette p = new PanelPalette();
            p.Dark = dark;
            p.Accent = ParseAccent();
            if (!dark)
            {
                p.Back = Color.White;
                p.Border = Color.FromArgb(226, 226, 226);
                p.Text = Color.FromArgb(51, 51, 51);
                p.Sub = Color.FromArgb(107, 107, 107);
                p.Red = Color.FromArgb(224, 72, 62);
                p.Blue = Color.FromArgb(58, 123, 213);
                p.Dim = Color.FromArgb(205, 205, 205);
                p.DimSub = Color.FromArgb(222, 222, 222);
                p.SubLunar = Color.FromArgb(166, 166, 166);
                p.Hover = Color.FromArgb(228, 231, 236);
                p.BtnBg = Color.FromArgb(243, 244, 246);
                p.BtnHot = Color.FromArgb(228, 231, 236);
                p.RestBg = Color.FromArgb(253, 236, 234);
                p.RestFg = Color.FromArgb(224, 72, 62);
                p.WorkBg = Color.FromArgb(255, 243, 224);
                p.WorkFg = Color.FromArgb(230, 126, 34);
                p.Dot = Color.FromArgb(230, 126, 34);
            }
            else
            {
                p.Back = Color.FromArgb(32, 32, 32);
                p.Border = Color.FromArgb(64, 64, 64);
                p.Text = Color.FromArgb(235, 235, 235);
                p.Sub = Color.FromArgb(156, 156, 156);
                p.Red = Color.FromArgb(240, 98, 88);
                p.Blue = Color.FromArgb(110, 160, 225);
                p.Dim = Color.FromArgb(84, 84, 84);
                p.DimSub = Color.FromArgb(74, 74, 74);
                p.SubLunar = Color.FromArgb(150, 150, 150);
                p.Hover = Color.FromArgb(54, 56, 60);
                p.BtnBg = Color.FromArgb(48, 49, 52);
                p.BtnHot = Color.FromArgb(62, 64, 68);
                p.RestBg = Color.FromArgb(84, 46, 44);
                p.RestFg = Color.FromArgb(242, 122, 112);
                p.WorkBg = Color.FromArgb(92, 66, 34);
                p.WorkFg = Color.FromArgb(238, 168, 84);
                p.Dot = Color.FromArgb(238, 168, 84);
            }
            p.SelBg = Blend(p.Back, p.Accent, dark ? 0.32f : 0.14f);
            return p;
        }

        public static bool SystemThemeLight()
        {
            bool light = true;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("SystemUsesLightTheme") : null;
                    if (v is int) light = (int)v == 1;
                }
            }
            catch { }
            return light;
        }

        private static Color ParseAccent()
        {
            try { return ColorTranslator.FromHtml(Settings.Accent); }
            catch { return Color.FromArgb(68, 114, 196); }
        }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }
    }

    // ============================ 假日数据源 ============================

    // B1：从 NateScarlet/holiday-cn（依据国务院公告整理的调休数据）拉取当年+次年
    // 休/班数据。jsDelivr/raw 多镜像逐个尝试，成功后写本地缓存；离线时用缓存，
    // 连缓存都没有时退回内置规则。角标优先级：holidays.txt 手动 > 在线 > 内置。
    internal static class HolidayFeed
    {
        public static Dictionary<DateTime, string> Data;  // null=尚未加载
        public static DateTime? LastFetch;
        private static bool _fetching;

        public static readonly string CachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChinaClock", "holidays-cache.txt");

        private static readonly string[] Mirrors =
        {
            "https://cdn.jsdelivr.net/gh/NateScarlet/holiday-cn@master/{0}.json",
            "https://fastly.jsdelivr.net/gh/NateScarlet/holiday-cn@master/{0}.json",
            "https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/{0}.json",
        };

        // 启动时用缓存先行（离线也有角标）；缓存 v2 格式 "yyyy-MM-dd|休|节日名"
        public static void LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return;
                var dict = new Dictionary<DateTime, string>();
                var names = new Dictionary<DateTime, string>();
                foreach (string raw in File.ReadAllLines(CachePath))
                {
                    string line = raw.Trim();
                    string[] parts = line.Split('|');
                    if (parts.Length < 2) continue;
                    DateTime d;
                    if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd",
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                    dict[d.Date] = parts[1] == "班" ? "班" : "休";
                    if (parts.Length >= 3 && parts[2].Length > 0 && !names.ContainsKey(d.Date)) names[d.Date] = parts[2];
                }
                if (dict.Count > 0) { Data = dict; CalendarData.SetOnlineData(dict, names); }
            }
            catch { }
        }

        public static void FetchAsync(Action<bool> done)
        {
            if (_fetching) return;
            _fetching = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                try { ok = FetchOnce(); } catch { }
                _fetching = false;
                if (done != null) done(ok);
            });
        }

        public static bool FetchOnce()
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | ServicePointManager.SecurityProtocol; }
            catch { }
            int y = DateTime.Now.Year;
            var merged = new Dictionary<DateTime, string>();
            var mergedNames = new Dictionary<DateTime, string>();
            foreach (int yy in new[] { y, y + 1 })
                foreach (string tpl in Mirrors)
                {
                    string json = HttpGet(string.Format(tpl, yy), 8000);
                    if (json == null) continue;
                    var part = CalendarData.ParseHolidayCn(json, mergedNames);
                    if (part.Count == 0) continue;
                    foreach (KeyValuePair<DateTime, string> kv in part) merged[kv.Key] = kv.Value;
                    break; // 该年成功即尝试下一年
                }
            if (merged.Count == 0) return false;

            Data = merged;
            CalendarData.SetOnlineData(merged, mergedNames);
            LastFetch = DateTime.Now;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CachePath));
                List<string> lines = new List<string>();
                foreach (KeyValuePair<DateTime, string> kv in merged)
                    lines.Add(kv.Key.ToString("yyyy-MM-dd") + "|" + kv.Value + "|" +
                        (mergedNames.ContainsKey(kv.Key) ? mergedNames[kv.Key] : ""));
                lines.Sort();
                File.WriteAllLines(CachePath, lines.ToArray());
            }
            catch { }
            Settings.HolidaysFetchedAt = LastFetch.Value.ToString("yyyy-MM-dd HH:mm") + "（" + merged.Count + " 条）";
            Settings.Save();
            return true;
        }

        private static string HttpGet(string url, int timeoutMs)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.UserAgent = "ChinaClock/1.0";
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                    return sr.ReadToEnd();
            }
            catch { return null; }
        }
    }

    // ============================ 日程备忘 ============================

    // 单条日程。三种来源（agenda.txt 每行一条）：
    //   2026-11-05|提交周报      一次性
    //   每年 05-20|结婚纪念日     公历每年重复
    //   农历 08-15|妈妈生日       农历每年重复
    // 内容开头可带 "HH:mm "（如 "15:00 例会"）→ 当天到点弹提醒（N1）
    internal sealed class AgendaItem
    {
        public DateTime Date;                       // 一次性日程的日期；重复日程=发生日（运行时填充）
        public int LunarMonth = -1, LunarDay = -1;  // 农历每年重复（正月=1，闰月不匹配）
        public int SolarMonth = -1, SolarDay = -1;  // 公历每年重复
        public string Text;
        public string Time;                         // 行首 HH:mm 或 null
        public int Seq;                             // 排序辅助（同日多条保持文件顺序）

        public bool Yearly { get { return LunarMonth > 0 || SolarMonth > 0; } }

        public AgendaItem Copy(DateTime occur)
        {
            AgendaItem c = new AgendaItem();
            c.Date = occur;
            c.LunarMonth = LunarMonth; c.LunarDay = LunarDay;
            c.SolarMonth = SolarMonth; c.SolarDay = SolarDay;
            c.Text = Text; c.Time = Time;
            return c;
        }
    }

    internal static class AgendaStore
    {
        private static List<AgendaItem> _items;
        private static readonly Regex TimeRx = new Regex("^(\\d{1,2}:\\d{2})\\s*", RegexOptions.Compiled);

        public static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChinaClock", "agenda.txt");

        public static void EnsureFile()
        {
            try
            {
                if (File.Exists(FilePath)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new[]
                {
                    "# 日程备忘（每行一条）：",
                    "#   2026-11-05|提交周报        一次性日程",
                    "#   每年 05-20|结婚纪念日      公历每年重复",
                    "#   农历 08-15|妈妈生日        农历每年重复",
                    "# 内容开头可带时间：2026-11-05|15:00 例会 → 当天 15:00 弹提醒",
                });
            }
            catch { }
        }

        // 解析单行；格式不符返回 null（纯函数，自测覆盖）
        internal static AgendaItem ParseLine(string line)
        {
            if (line == null) return null;
            string t = line.Trim();
            int i = t.IndexOf('|');
            if (i <= 0) return null;
            string left = t.Substring(0, i).Trim();
            string text = t.Substring(i + 1).Trim();
            if (text.Length == 0) return null;

            AgendaItem it = new AgendaItem();
            if (left.StartsWith("农历"))
            {
                string md = left.Substring(2).Trim();
                int dash = md.IndexOf('-');
                int m, d;
                if (dash <= 0 || !int.TryParse(md.Substring(0, dash), out m) || !int.TryParse(md.Substring(dash + 1), out d)) return null;
                if (m < 1 || m > 12 || d < 1 || d > 30) return null;
                it.LunarMonth = m; it.LunarDay = d;
            }
            else if (left.StartsWith("每年") || left.StartsWith("公历"))
            {
                string md = left.Substring(2).Trim();
                int dash = md.IndexOf('-');
                int m, d;
                if (dash <= 0 || !int.TryParse(md.Substring(0, dash), out m) || !int.TryParse(md.Substring(dash + 1), out d)) return null;
                if (m < 1 || m > 12 || d < 1 || d > 31) return null;
                it.SolarMonth = m; it.SolarDay = d;
            }
            else
            {
                DateTime d;
                if (!DateTime.TryParseExact(left, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return null;
                it.Date = d.Date;
            }

            Match tm = TimeRx.Match(text);
            if (tm.Success)
            {
                it.Time = tm.Groups[1].Value;
                text = text.Substring(tm.Length).Trim();
                if (text.Length == 0) return null;
            }
            it.Text = text;
            return it;
        }

        private static void EnsureLoaded()
        {
            if (_items != null) return;
            _items = new List<AgendaItem>();
            try
            {
                EnsureFile();
                int seq = 0;
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    AgendaItem it = ParseLine(raw);
                    if (it != null) { it.Seq = seq++; _items.Add(it); }
                }
            }
            catch { }
        }

        public static void Reload() { _items = null; }

        // 某条日程是否发生在 d（农历条目不匹配闰月）
        private static bool OccursOn(AgendaItem it, DateTime d)
        {
            if (it.Yearly)
            {
                if (it.SolarMonth > 0) return d.Month == it.SolarMonth && d.Day == it.SolarDay;
                int ly, lm, ld; bool leap;
                CalendarData.LunarOf(d, out ly, out lm, out leap, out ld);
                return !leap && lm == it.LunarMonth && ld == it.LunarDay;
            }
            return it.Date == d.Date;
        }

        public static bool Has(DateTime d)
        {
            EnsureLoaded();
            foreach (AgendaItem it in _items) if (OccursOn(it, d)) return true;
            return false;
        }

        // 某天全部日程文本（含农历/公历重复条目），供圆点与悬停提示
        public static List<string> For(DateTime d)
        {
            EnsureLoaded();
            List<string> list = new List<string>();
            foreach (AgendaItem it in _items) if (OccursOn(it, d)) list.Add(it.Text);
            return list;
        }

        // 某天全部条目（含重复），供提醒
        public static List<AgendaItem> On(DateTime d)
        {
            EnsureLoaded();
            List<AgendaItem> list = new List<AgendaItem>();
            foreach (AgendaItem it in _items) if (OccursOn(it, d)) list.Add(it.Copy(d));
            return list;
        }

        // [from, from+days) 内的发生记录，按日期+文件顺序排序（近期日程列表 N2）
        public static List<AgendaItem> Upcoming(DateTime from, int days)
        {
            EnsureLoaded();
            List<AgendaItem> list = new List<AgendaItem>();
            int seq = 0;
            for (DateTime d = from.Date; d < from.Date.AddDays(days); d = d.AddDays(1))
                foreach (AgendaItem it in _items)
                    if (OccursOn(it, d)) { AgendaItem c = it.Copy(d); c.Seq = seq++; list.Add(c); }
            list.Sort(delegate(AgendaItem a, AgendaItem b)
            {
                int c = a.Date.CompareTo(b.Date);
                return c != 0 ? c : a.Seq.CompareTo(b.Seq);
            });
            return list;
        }

        // 覆盖 d 当天的一次性日程（空列表=清除）；农历/每年重复条目不受影响
        public static void Set(DateTime d, List<string> texts)
        {
            EnsureLoaded();
            for (int i = _items.Count - 1; i >= 0; i--)
                if (!_items[i].Yearly && _items[i].Date == d.Date) _items.RemoveAt(i);
            if (texts != null)
                foreach (string t in texts) { AgendaItem it = new AgendaItem(); it.Date = d.Date; it.Text = t; _items.Add(it); }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                List<string> lines = new List<string>();
                lines.Add("# 日程备忘（每行一条）：");
                lines.Add("#   2026-11-05|提交周报        一次性日程");
                lines.Add("#   每年 05-20|结婚纪念日      公历每年重复");
                lines.Add("#   农历 08-15|妈妈生日        农历每年重复");
                lines.Add("# 内容开头可带时间：2026-11-05|15:00 例会 → 当天 15:00 弹提醒");
                foreach (AgendaItem it in _items) if (it.Yearly)
                        lines.Add((it.LunarMonth > 0 ? "农历 " : "每年 ") +
                            (it.LunarMonth > 0 ? it.LunarMonth.ToString("00-") + it.LunarDay.ToString("00")
                                               : it.SolarMonth.ToString("00-") + it.SolarDay.ToString("00"))
                            + "|" + it.Text);
                List<AgendaItem> once = new List<AgendaItem>();
                foreach (AgendaItem it in _items) if (!it.Yearly) once.Add(it);
                once.Sort(delegate(AgendaItem a, AgendaItem b) { return a.Date.CompareTo(b.Date); });
                foreach (AgendaItem it in once)
                    lines.Add(it.Date.ToString("yyyy-MM-dd") + "|" + it.Text);
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch { }
        }
    }

    // ============================ 历法数据 ============================

    // 农历（.NET 内置 ChineseLunisolarCalendar，1901-2100）+ 节气（寿星公式，21 世纪）
    // + 干支纪年/月/日 + 节日 + 法定假日 + holidays.txt 自定义 休/班
    internal static class CalendarData
    {
        private static readonly ChineseLunisolarCalendar Lunar = new ChineseLunisolarCalendar();
        // 干支三表供 Almanac（黄历）复用（F1）
        internal static readonly string[] Stems = { "甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸" };
        internal static readonly string[] Branches = { "子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥" };
        internal static readonly string[] Animals = { "鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪" };
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
        private static Dictionary<DateTime, string> _online; // holiday-cn 在线假日数据（B1）

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
            // 角标优先级：holidays.txt 手动配置 > 在线假日数据(B1) > 法定假日 > 普通周末（周末也是休息日）
            bool weekend = d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;
            di.Badge = ov ?? (di.StatutoryRest || weekend ? "休" : null);
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
        // 只对"已公布安排"的年份自动标注：次年安排由国务院在前一年底发布，
        // 未公布年份（如明年）不预测——农历能算出春节是哪天，但除夕放不放、
        // 调休怎么调是每年单定的。公布后把 KnownUntilYear 加一并重新编译，
        // 或直接在 holidays.txt 里补 休/班（配置优先级更高，见 Get 的角标逻辑）。
        private const int StatutoryKnownFromYear = 2025; // 新放假办法施行年
        private const int StatutoryKnownUntilYear = 2026; // 已公布安排的最后一年
        public static bool IsStatutoryRest(DateTime d)
        {
            if (d.Year < StatutoryKnownFromYear || d.Year > StatutoryKnownUntilYear) return false;
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
            if (_overrides.TryGetValue(d.Date, out s)) return s;                    // 手动配置最高
            if (_online != null && _online.TryGetValue(d.Date, out s)) return s;    // 在线数据次之
            return null;
        }

        // holiday-cn 拉取的数据注入日历（B1/N4）：角标 + 节日名
        private static Dictionary<DateTime, string> _onlineNames;
        public static void SetOnlineData(Dictionary<DateTime, string> badges, Dictionary<DateTime, string> names)
        {
            _online = badges;
            _onlineNames = names;
        }

        // 某天农历（N3：农历生日判定用）。正月=1
        public static void LunarOf(DateTime d, out int y, out int m, out bool leap, out int day)
        {
            ToLunar(d, out y, out m, out leap, out day);
        }

        // 最近一个法定/传统节日（含 today 当天）。从 ClockForm 迁来供时钟提示与
        // 日历详情卡片共用（F1）。仅统计有固定日期的节日，不含普通周末。
        public static bool NextFestival(DateTime today, out DateTime date, out string name)
        {
            DateTime best = DateTime.MaxValue; string bestName = null;
            for (int y = today.Year; y <= today.Year + 1; y++)
            {
                AddCand(ref best, ref bestName, new DateTime(y, 1, 1), "元旦", today);
                AddCand(ref best, ref bestName, TermDate(y, "清明"), "清明", today);
                AddCand(ref best, ref bestName, new DateTime(y, 5, 1), "劳动节", today);
                AddCand(ref best, ref bestName, new DateTime(y, 10, 1), "国庆节", today);
                try { AddCand(ref best, ref bestName, LunarDate(y, 1, 1), "春节", today); } catch { }
                try { AddCand(ref best, ref bestName, LunarDate(y, 5, 5), "端午", today); } catch { }
                try { AddCand(ref best, ref bestName, LunarDate(y, 8, 15), "中秋", today); } catch { }
            }
            if (best == DateTime.MaxValue) { date = today; name = null; return false; }
            date = best; name = bestName; return true;
        }

        private static void AddCand(ref DateTime best, ref string bestName, DateTime d, string n, DateTime today)
        {
            if (d.Date < today || d >= best) return;
            best = d; bestName = n;
        }

        // 角标原因（N4）：在线数据携带的节日名 > 当天节日 > 内置法定名 > 周末/补班
        public static string BadgeReason(DateTime d, string badge, string festival)
        {
            if (badge == null) return null;
            string n;
            if (_onlineNames != null && _onlineNames.TryGetValue(d.Date, out n) && !string.IsNullOrEmpty(n)) return n;
            if (festival != null) return festival;
            if (badge == "休")
            {
                string s = StatutoryName(d);
                if (s != null) return s;
                if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday) return "周末";
            }
            return badge == "班" ? "调休补班" : null;
        }

        // 内置法定假日对应的节日名（仅对 IsStatutoryRest=true 的日子有意义）
        private static string StatutoryName(DateTime d)
        {
            if (!IsStatutoryRest(d)) return null;
            if (d.Month == 1 && d.Day == 1) return "元旦";
            if (d.Month == 5 && d.Day == 1) return "劳动节";
            if (d.Month == 10 && d.Day == 1) return "国庆节";
            if (d.Date == TermDate(d.Year, "清明")) return "清明";
            int y, m, day; bool leap;
            ToLunar(d, out y, out m, out leap, out day);
            if (leap) return null;
            if (m == 1 && day >= 1 && day <= 3) return "春节";
            if (d.Date == CnyDate(y + 1).AddDays(-1)) return "除夕";
            if (m == 5 && day == 5) return "端午";
            if (m == 8 && day == 15) return "中秋";
            return null;
        }

        // ISO-8601 周数（周一为一周起始、含首个周四的周为第 1 周）
        public static int IsoWeek(DateTime d)
        {
            return CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(d, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
        }

        // 解析 holiday-cn（NateScarlet/holiday-cn）年度 JSON：
        // {"days":[{"name":"元旦","date":"2027-01-01","isOffDay":true},...]}
        // 名称写入 names（N4 悬停提示用）
        private static readonly Regex HolidayCnRx = new Regex(
            "\"name\"\\s*:\\s*\"([^\"]*)\"\\s*,\\s*\"date\"\\s*:\\s*\"(\\d{4}-\\d{2}-\\d{2})\"\\s*,\\s*\"isOffDay\"\\s*:\\s*(true|false)",
            RegexOptions.Compiled);

        public static Dictionary<DateTime, string> ParseHolidayCn(string json, Dictionary<DateTime, string> names)
        {
            var dict = new Dictionary<DateTime, string>();
            if (json == null) return dict;
            foreach (Match m in HolidayCnRx.Matches(json))
            {
                DateTime d;
                if (!DateTime.TryParseExact(m.Groups[2].Value, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                dict[d.Date] = m.Groups[3].Value == "true" ? "休" : "班";
                if (names != null && !names.ContainsKey(d.Date)) names[d.Date] = m.Groups[1].Value;
            }
            return dict;
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

    // ============================ 黄历 ============================

    // 日期详情卡片（F1）的黄历内容：十二建星宜忌、冲煞、纳音五行、值神黄道黑道。
    // 全部由干支纯算法推得，零依赖、不联网；采用通行简明黄历口径，供日常参考。
    internal static class Almanac
    {
        // 六十甲子纳音表（两甲子一组，组序 = 干支序 / 2）
        private static readonly string[] NayinTable = {
            "海中金", "炉中火", "大林木", "路旁土", "剑锋金", "山头火", "涧下水", "城头土", "白蜡金", "杨柳木",
            "泉中水", "屋上土", "霹雳火", "松柏木", "长流水", "沙中金", "山下火", "平地木", "壁上土", "金箔金",
            "覆灯火", "天河水", "大驿土", "钗钏金", "桑柘木", "大溪水", "沙中土", "天上火", "石榴木", "大海水" };

        private static readonly string[] Officers = { "建", "除", "满", "平", "定", "执", "破", "危", "成", "收", "开", "闭" };

        // 十二建星通行简明宜忌
        private static readonly string[] OfficerYi = {
            "出行 上任 会友 上书 见工",   // 建
            "祭祀 祈福 嫁娶 出行 求医",   // 除
            "祈福 嫁娶 开市 移徙",         // 满
            "修饰垣墙 平治道涂",           // 平
            "祭祀 嫁娶 纳采 订盟",         // 定
            "捕捉 祈福 祭祀 嫁娶",         // 执
            "破屋 坏垣 求医 治病",         // 破
            "祭祀 祈福 安床",             // 危
            "开市 嫁娶 移徙 上任 修造",   // 成
            "纳财 开市 交易 入学",         // 收
            "开业 上任 修造 移徙",         // 开
            "安葬 修坟 筑堤 补垣",         // 闭
        };
        private static readonly string[] OfficerJi = {
            "动土 开仓 嫁娶 造船",         // 建
            "开市 动土 安葬",             // 除
            "上任 求医 安葬",             // 满
            "祈福 嫁娶 开市",             // 平
            "诉讼 出行 移徙",             // 定
            "出行 开市 移徙 安葬",         // 执
            "诸事不宜",                   // 破
            "登高 行船 出行 嫁娶",         // 危
            "诉讼 安葬",                   // 成
            "安葬 出行 破土",             // 收
            "安葬 破土 诉讼",             // 开
            "开市 出行 移徙 诉讼",         // 闭
        };

        // 十二值神（天将）；黄道：青龙 明堂 金匮 天德 玉堂 司命
        private static readonly string[] ZhiShenNames = { "青龙", "明堂", "天刑", "朱雀", "金匮", "天德", "白虎", "玉堂", "天牢", "玄武", "司命", "勾陈" };
        private static readonly bool[] ZhiShenYellow = { true, true, false, false, true, true, false, true, false, false, true, false };
        // 值神起诀：子午月起申、丑未月起戌、寅申月起子、卯酉月起寅、辰戌月起辰、巳亥月起午（下标=月支）
        private static readonly int[] ZhiShenStart = { 8, 10, 0, 2, 4, 6, 8, 10, 0, 2, 4, 6 };
        // 煞方按日支三合组：申子辰→南、巳酉丑→东、寅午戌→北、亥卯未→西（下标=日支%4）
        private static readonly string[] ShaDir = { "南", "东", "北", "西" };

        // 六十甲子序（甲子=0 … 癸亥=59）
        public static int GanZhiIndex(string ganzhi)
        {
            if (ganzhi == null || ganzhi.Length < 2) return -1;
            int s = Array.IndexOf(CalendarData.Stems, ganzhi[0].ToString());
            int b = Array.IndexOf(CalendarData.Branches, ganzhi[1].ToString());
            if (s < 0 || b < 0) return -1;
            for (int k = 0; k < 6; k++) { int i = s + 10 * k; if (i % 12 == b) return i; }
            return -1;
        }

        private static string Gz(int i) { return CalendarData.Stems[i % 10] + CalendarData.Branches[i % 12]; }

        // 纳音五行（如 庚辰 → 白蜡金）
        public static string Nayin(string ganzhiDay)
        {
            int i = GanZhiIndex(ganzhiDay);
            return i < 0 ? null : NayinTable[i / 2];
        }

        // 冲煞（如 甲申 → "冲虎（甲寅）· 煞南"）：冲=地支相隔六位（干支序+30），煞方由日支三合组定
        public static string ChongSha(string ganzhiDay)
        {
            int i = GanZhiIndex(ganzhiDay);
            if (i < 0) return null;
            int c = (i + 30) % 60;
            return "冲" + CalendarData.Animals[c % 12] + "（" + Gz(c) + "）· 煞" + ShaDir[(i % 12) % 4];
        }

        // 十二建星：日支对月支（月建）顺排（如 甲子日 酉月 → 平）
        public static string Officer(string ganzhiDay, string ganzhiMonth)
        {
            int db = Branch(ganzhiDay), mb = Branch(ganzhiMonth);
            if (db < 0 || mb < 0) return null;
            return Officers[Mod(db - mb, 12)];
        }

        public static string Yi(string officer)
        {
            int i = Array.IndexOf(Officers, officer ?? "");
            return i < 0 ? null : OfficerYi[i];
        }

        public static string Ji(string officer)
        {
            int i = Array.IndexOf(Officers, officer ?? "");
            return i < 0 ? null : OfficerJi[i];
        }

        // 值神（十二天将）+ 黄道/黑道（如 甲子日 酉月 → 司命，黄道）
        public static string ZhiShen(string ganzhiDay, string ganzhiMonth, out bool yellow)
        {
            yellow = false;
            int db = Branch(ganzhiDay), mb = Branch(ganzhiMonth);
            if (db < 0 || mb < 0) return null;
            int off = Mod(db - ZhiShenStart[mb], 12);
            yellow = ZhiShenYellow[off];
            return ZhiShenNames[off];
        }

        private static int Branch(string ganzhi)
        {
            return ganzhi != null && ganzhi.Length >= 2 ? Array.IndexOf(CalendarData.Branches, ganzhi[1].ToString()) : -1;
        }

        private static int Mod(int x, int m) { x %= m; return x < 0 ? x + m : x; }
    }

    // ============================ 本机时间 ============================

    // F2：电脑时区不是 UTC+8 时提供本机时间行；相同时区返回 Distinct=false，
    // 各显示处自动隐藏，避免主时钟与本机行显示两遍同样的时间。
    internal static class LocalClock
    {
        public static bool Distinct
        {
            get
            {
                try { return TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes != 480; }
                catch { return false; }
            }
        }

        // 面板头部大时间右侧（紧凑）
        public static string ShortText { get { return "本地 " + DateTime.Now.ToString("HH:mm"); } }

        // 悬停提示（完整）
        public static string PanelText { get { return "本地时间 " + DateTime.Now.ToString("HH:mm"); } }

        // 三处显示统一开关：设置开关 + 时区确实不同
        public static bool Enabled { get { return Settings.ShowLocalClock && Distinct; } }
    }

    // ============================ 任务栏停靠几何 ============================

    // 停靠边检测与时钟定位的纯几何函数（--selftest 用合成矩形校验四边）。
    // Windows 10 原生支持上下左右停靠；Windows 11 需 ExplorerPatcher 等工具，
    // 但 Shell_TrayWnd 的窗口矩形在任意停靠下都真实可用，检测即以此为准。
    internal enum DockEdge { Bottom = 0, Top = 1, Left = 2, Right = 3 }

    internal static class TaskbarLayout
    {
        // --pretend-dock:left|top|right 调试入口：强制按指定停靠边定位（null=自动检测）
        internal static DockEdge? PretendDock;

        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        // 当前任务栏矩形 + 停靠边；找不到任务栏返回 false（dock 回退 Bottom）
        internal static bool Current(out Rectangle tray, out DockEdge dock)
        {
            tray = Rectangle.Empty;
            dock = PretendDock ?? DockEdge.Bottom;
            IntPtr trayHwnd = FindWindow("Shell_TrayWnd", null);
            if (trayHwnd == IntPtr.Zero) return false;
            RECT tr;
            GetWindowRect(trayHwnd, out tr);
            tray = Rectangle.FromLTRB(tr.Left, tr.Top, tr.Right, tr.Bottom);
            if (PretendDock.HasValue) return true;
            dock = Detect(tray, Screen.PrimaryScreen.Bounds);
            return true;
        }

        // 纯函数：窄条为垂直停靠（左/右），再按贴哪条屏幕边定左右；横条同理分顶底
        public static DockEdge Detect(Rectangle tray, Rectangle screen)
        {
            bool vertical = tray.Width < tray.Height;
            if (vertical)
                return (tray.Left - screen.Left) <= (screen.Right - tray.Right) ? DockEdge.Left : DockEdge.Right;
            return (tray.Top - screen.Top) <= (screen.Bottom - tray.Bottom) ? DockEdge.Top : DockEdge.Bottom;
        }

        // 纯函数：无 UIA 时的系统时钟几何估算。
        // 水平停靠（顶/底）：右端锚定、垂直居中（公式对顶栏天然成立）；
        // 垂直停靠（左/右）：水平居中、底部锚定（系统时钟在侧边栏底部）。
        public static Rectangle GuessClockRect(Rectangle tray, DockEdge dock)
        {
            float k = Math.Min(tray.Width, tray.Height) / 48f;
            int w = (int)(92 * k), h = (int)(42 * k), m = (int)(10 * k);
            if (dock == DockEdge.Left || dock == DockEdge.Right)
            {
                // 窄栏：估算宽度超过栏宽时收窄并钳在栏内（仅兜底初值，UIA 结果稍后精调）
                int x = tray.Left + (tray.Width - w) / 2;
                if (w > tray.Width - 4) { w = Math.Max(20, tray.Width - 8); x = tray.Left + 4; }
                return new Rectangle(x, tray.Bottom - m - h, w, h);
            }
            return new Rectangle(tray.Right - m - w, tray.Top + (tray.Height - h) / 2, w, h);
        }

        // 纯函数：覆盖窗目标位置。水平停靠=右锚+垂直居中（原逻辑，加边钳制）；
        // 垂直停靠=底锚+水平居中；均尽量钳制在任务栏矩形内。
        public static Rectangle FitTarget(Rectangle r, Rectangle tray, DockEdge dock, int w, int h)
        {
            if (dock == DockEdge.Left || dock == DockEdge.Right)
            {
                int bottom = Math.Min(r.Bottom + 4, tray.Bottom - 1);
                int x = r.X + r.Width / 2 - w / 2;
                x = Math.Max(tray.Left + 1, Math.Min(x, tray.Right - w - 1));
                return new Rectangle(x, bottom - h, w, h);
            }
            int right = Math.Min(r.Right + 4, tray.Right - 1);
            int y = r.Top + r.Height / 2 - h / 2;
            y = Math.Max(tray.Top + 1, Math.Min(y, tray.Bottom - h - 1));
            return new Rectangle(right - w, y, w, h);
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
        private int _pad, _headerH, _titleH, _weekH, _cellH, _cellW, _footerH;
        private int _offDate, _offInfo; // 头部日期行/农历行距头部顶端的物理偏移（按字体行高算得）
        private Font _fTime, _fDate, _fInfo, _fWeek, _fDay, _fSmall, _fFoot;
        private Rectangle _gridRect, _btnTitlePrev, _btnTitleNext, _titleRect, _btnToday, _btnAgenda;
        private int _pickMode;      // 中部层级：0=月历 1=选月 2=选年（十年区）3=近期日程列表
        private int _pickYear;      // 选月/选年视图浏览的年份
        private int _hot = -1;      // 悬停：-1 无 1=‹ 2=› 3=标题 4=回到今天 5=近期日程 10+i=选择器格子 100+i=日格 200+i=列表行
        private int _wkCol;         // 周数列宽（B2，设置可关）
        private ToolTip _tipPanel;  // 日程/角标悬停提示（B4/N4）
        private DateTime _tipDay = DateTime.MinValue;
        private AgendaEditor _editor; // 日程编辑窗（B4）
        private bool _editOpen;       // 编辑期间面板不因失焦关闭
        private List<AgendaItem> _listItems; // 近期日程列表数据（N2）
        private int _listScroll;             // 列表滚过的行数
        private bool _hasLocal;              // 头部本机时间（F2，时区≠北京时间才有，画在大时间右侧）
        private int _calW;                   // 月历区宽度（卡片开合前的面板宽）
        private int _calX;                   // 月历区在窗内的 x 偏移（详情卡片开在另一侧时 > 0）
        private bool _cardOpen;              // 日期详情卡片（F1，优效日历式侧弹卡）
        private DateTime _cardDay;
        private int _cardSide = -1;          // 卡片展开方向：-1=左侧（默认） 1=右侧（面板停靠左下时）
        private int _cardW;                  // 卡片宽（Scale 后）
        private Rectangle _cardRect;         // 卡片矩形（窗内坐标；收起时 Empty）
        private Rectangle _btnCardPrev, _btnCardNext, _btnCardClose, _btnCardEdit;
        private int _cardHot;                // 卡片悬停：0无 1=‹ 2=› 3=× 4=编辑日程
        // 日格点击节奏：短时间重复点同一天 = 连点浏览，卡片保持打开不闪收
        // （>650ms 的从容第二次单击才收起）。
        private DateTime _lastGridClick = DateTime.MinValue;
        private DateTime _lastGridClickDay = DateTime.MinValue;

        // 配色由 PanelPalette 按主题/强调色生成（C1/C3）
        private PanelPalette P;
        private Color _cText, _cSub, _cRed, _cBlue, _cAccent, _cDim,
            _cDimSub, _cSubLunar, _cSelBg, _cHover, _cBtnBg, _cBtnHot,
            _cBorder, _cRestBg, _cRestFg, _cWorkBg, _cWorkFg, _cDot;

        public CalendarPanel()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;

            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) _s = g.DpiX / 96f;

            // 主题与强调色（C1/C3）——面板每次打开重建，改设置后下次打开即生效
            Settings.Load();
            P = PanelPalette.Load();
            BackColor = P.Back;
            _cText = P.Text; _cSub = P.Sub; _cRed = P.Red; _cBlue = P.Blue; _cAccent = P.Accent; _cDim = P.Dim;
            _cDimSub = P.DimSub; _cSubLunar = P.SubLunar; _cSelBg = P.SelBg; _cHover = P.Hover;
            _cBtnBg = P.BtnBg; _cBtnHot = P.BtnHot; _cBorder = P.Border; _cDot = P.Dot;
            _cRestBg = P.RestBg; _cRestFg = P.RestFg; _cWorkBg = P.WorkBg; _cWorkFg = P.WorkFg;

            // 字体先行创建：头部三行按字体实际行高排版，固定 y 偏移在换字体/DPI 下会失真
            // （与 kimi-cli 讨论结论：y时间=12，y日期=y时间+H时间+4，y农历=y日期+H日期+2，
            //   头部高=y农历+H农历+6，各间隙为常量、行高用 MeasureText 实测）
            _fTime = new Font("Segoe UI Semibold", 19F);
            _fDate = new Font("Microsoft YaHei UI", 10.5F);
            _fInfo = new Font("Microsoft YaHei UI", 9F);
            _fWeek = new Font("Microsoft YaHei UI", 9.5F);
            _fDay = new Font("Segoe UI", 10.5F, FontStyle.Bold);
            _fSmall = new Font("Microsoft YaHei UI", 8.5F);
            _fFoot = new Font("Microsoft YaHei UI", 9.5F);
            int hTime = TextRenderer.MeasureText("00:00:00", _fTime).Height;
            int hDate = TextRenderer.MeasureText("2026年11月4日 星期三", _fDate).Height;
            int hInfo = TextRenderer.MeasureText("农历 九月廿六", _fInfo).Height;
            _offDate = Scale(12) + hTime + Scale(4);
            _offInfo = _offDate + hDate + Scale(2);
            _headerH = _offInfo + hInfo + Scale(6);
            _hasLocal = LocalClock.Enabled; // F2：本机时间画在头部大时间右侧（时区不同才显示）

            _pad = Scale(14); _titleH = Scale(32); _weekH = Scale(28);
            _cellH = Scale(60); _cellW = Scale(58); _footerH = Scale(40);
            _wkCol = Settings.ShowWeekNumbers ? Scale(30) : 0;
            int w = _pad * 2 + _wkCol + _cellW * 7;
            _calW = w; // 月历区宽度
            if (_cardW == 0) _cardW = Scale(212); // 详情卡片宽度（窗内常驻预留）
            int gap = Scale(8);
            int fullW = _calW + gap + _cardW; // 窗口常驻全宽 = 月历 + 透明缝隙 + 卡片
            int h = _pad + _headerH + _titleH + _weekH + _cellH * 6 + _footerH + Scale(8);
            Size = new Size(fullW, h);

            // F1：窗口自创建起常驻全尺寸，月历固定占窗内一侧、卡片预留另一侧。
            // 开合只切换透明分区（Region），不改窗口位置/大小——反复 resize/移动
            // 正是此前"单击日期闪烁"的根源（卡片区域反复露底/覆盖）。
            // 关闭时卡片区不在 Region 内：不可见、鼠标点击穿透。
            Rectangle tray; DockEdge dock;
            TaskbarLayout.Current(out tray, out dock);
            _cardSide = dock == DockEdge.Left ? 1 : -1; // 卡片朝屏幕中央：左停靠→右侧，其余→左侧
            if (_cardSide < 0)
            {
                _calX = _cardW + gap;                          // 卡片在左：月历靠右
                _cardRect = new Rectangle(0, 0, _cardW, h);
            }
            else
            {
                _calX = 0;                                      // 卡片在右：月历靠左
                _cardRect = new Rectangle(_calW + gap, 0, _cardW, h);
            }
            Region = new Region(RoundedPath(new Rectangle(_calX, 0, _calW - 1, h - 1), Scale(10)));

            // 月历区锚到时钟所在角（窗口额外带上卡片区宽度，月历可见位置与旧版一致）
            var wa = Screen.PrimaryScreen.WorkingArea;
            Point loc;
            if (dock == DockEdge.Top) loc = new Point(wa.Right - fullW - Scale(10), wa.Top + Scale(8));
            else if (dock == DockEdge.Left) loc = new Point(wa.Left + Scale(10), wa.Bottom - h - Scale(8));
            else loc = new Point(wa.Right - fullW - Scale(10), wa.Bottom - h - Scale(8)); // 底/右停靠
            loc.X = Math.Max(wa.Left, Math.Min(loc.X, wa.Right - fullW));
            loc.Y = Math.Max(wa.Top, Math.Min(loc.Y, wa.Bottom - h));
            Location = loc;

            DateTime now = Now();
            _selected = now.Date;
            _viewMonth = new DateTime(now.Year, now.Month, 1);

            _tipPanel = new ToolTip { InitialDelay = 400, ReshowDelay = 300, ShowAlways = true };

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
            AgendaStore.Reload(); // 每次打开面板刷新日程/节假日配置
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode != Keys.Escape) return;
            if (_cardOpen) { CloseCard(); return; }              // 先收详情卡片（F1）
            if (_pickMode != 0) { _pickMode = 0; Invalidate(); } // 再收起年月选择器，最后关面板
            else Close();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (_editOpen) return; // 日程编辑窗持有焦点时面板保持打开
            Close();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            Point pRaw = e.Location;
            if (_cardOpen && _cardRect.Contains(pRaw)) { CardClick(pRaw); return; } // F1：卡片内点击
            Point p = pRaw; // 月历矩形全部为窗内坐标（含 _calX），命中无需再平移
            if (_btnTitlePrev.Contains(p)) { StepMonth(-1); return; }
            if (_btnTitleNext.Contains(p)) { StepMonth(1); return; }
            if (_titleRect.Contains(p))
            {
                if (_pickMode == 3) { _pickMode = 0; _hot = -1; Invalidate(); } // 列表视图：点标题返回月历
                else DrillTitle();
                return;
            }
            if (_btnAgenda.Contains(p)) { ToggleList(); return; } // N2：近期日程
            if (_btnToday.Contains(p)) { GoToday(); return; }
            if (_pickMode == 3)
            {
                // 列表：点条目跳转那天；点空白收起
                Rectangle a = PickArea();
                int row = (p.Y - a.Y - Scale(6)) / Scale(26);
                int idx = row + _listScroll;
                if (row >= 0 && idx >= 0 && idx < _listItems.Count &&
                    p.X >= a.X && p.X <= a.Right && a.Y + Scale(6) + row * Scale(26) + Scale(26) <= a.Bottom + Scale(26))
                {
                    DateTime d = _listItems[idx].Date;
                    _viewMonth = new DateTime(d.Year, d.Month, 1);
                    _selected = d;
                    _pickMode = 0;
                }
                else _pickMode = 0;
                _hot = -1;
                Invalidate();
                return;
            }
            if (_pickMode != 0) { PickClick(p); return; }
            if (_gridRect.Contains(p))
            {
                int col = Math.Max(0, Math.Min(6, (p.X - _gridRect.X) / _cellW));
                int row = Math.Max(0, Math.Min(5, (p.Y - _gridRect.Y) / _cellH));
                // 所有日期格点击（含双击的第二击）统一走浏览逻辑（F1：连点稳定跟随）
                HandleGridClick(GridStart().AddDays(row * 7 + col).Date);
            }
        }

        // 双击的第二击走 WM_LBUTTONDBLCLK → MouseDown(Clicks=2)，MouseClick 不一定触发，
        // 在此兜底汇入 HandleGridClick；与单击一样只做浏览跟随（幂等，无编辑路径）
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || e.Clicks < 2) return;
            Point p = e.Location;
            if (_pickMode == 0 && _gridRect.Contains(p))
            {
                int col = Math.Max(0, Math.Min(6, (p.X - _gridRect.X) / _cellW));
                int row = Math.Max(0, Math.Min(5, (p.Y - _gridRect.Y) / _cellH));
                HandleGridClick(GridStart().AddDays(row * 7 + col).Date);
            }
        }

        // 日格点击统一入口（含系统判定为"双击"的第二击）：全部按浏览处理——选中
        // 并让详情卡片跟随。日期格不响应双击编辑（优效日历同款交互）：连点的第一组
        // 两连击与"从容双击编辑"在系统层面不可区分，保留双击必然连点误弹编辑窗；
        // 编辑入口收敛到卡片上的"编辑日程…"按钮，任何点击节奏下都不闪编辑窗。
        private void HandleGridClick(DateTime d)
        {
            bool rapidRepeat = DateTime.Now.Subtract(_lastGridClick).TotalMilliseconds < 650
                && _lastGridClickDay == d;
            _lastGridClick = DateTime.Now;
            _lastGridClickDay = d;
            _selected = d;
            if (!rapidRepeat && _cardOpen && _cardDay == d) CloseCard();
            else OpenCard(d);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point pRaw = e.Location;
            if (_cardOpen && _cardRect.Contains(pRaw)) // F1：卡片按钮悬停
            {
                int hotCard = _btnCardPrev.Contains(pRaw) ? 1 : _btnCardNext.Contains(pRaw) ? 2
                    : _btnCardClose.Contains(pRaw) ? 3 : _btnCardEdit.Contains(pRaw) ? 4 : 0;
                if (hotCard != _cardHot) { _cardHot = hotCard; Invalidate(); }
                ClearDayTip();
                Cursor = hotCard > 0 ? Cursors.Hand : Cursors.Default;
                return;
            }
            if (_cardHot != 0) { _cardHot = 0; Invalidate(); }
            int hot = HitHot(pRaw); // 月历矩形为窗内坐标（含 _calX），直接命中
            if (hot != _hot) { _hot = hot; Invalidate(); }
            if (_pickMode == 0 && hot >= 100)
            {
                DateTime d = GridStart().AddDays(hot - 100);
                UpdateDayTip(d);
                Cursor = AgendaStore.Has(d) ? Cursors.Hand : Cursors.Default;
            }
            else
            {
                ClearDayTip();
                Cursor = hot >= 0 ? Cursors.Hand : Cursors.Default;
            }
        }

        // 日格悬停提示（N4 扩展）：日期 + 角标原因（节日名）+ 当天日程
        // 仅在有日程或角标原因不是"周末"时显示，避免每个周末都冒提示
        private void UpdateDayTip(DateTime d)
        {
            if (d == _tipDay) return;
            _tipDay = d;
            CalendarData.DayInfo di = CalendarData.Get(d);
            string reason = CalendarData.BadgeReason(d, di.Badge, di.Festival);
            List<string> agenda = AgendaStore.For(d);
            if (agenda.Count == 0 && (reason == null || reason == "周末"))
            {
                _tipPanel.SetToolTip(this, "");
                return;
            }
            List<string> lines = new List<string>();
            lines.Add(d.ToString("M月d日 ") + WeekNames[((int)d.DayOfWeek + 6) % 7]);
            if (reason != null) lines.Add(reason + (di.Badge == "班" ? "（补班）" : "（休）"));
            foreach (string t in agenda) lines.Add("· " + t);
            _tipPanel.SetToolTip(this, string.Join("\n", lines.ToArray()));
        }

        private void ClearDayTip()
        {
            if (_tipDay == DateTime.MinValue) return;
            _tipDay = DateTime.MinValue;
            _tipPanel.SetToolTip(this, "");
        }

        // B4：双击某天打开日程编辑窗（--agenda-demo 测试入口也会调用）
        public void OpenAgenda(DateTime d)
        {
            if (_editor != null && !_editor.IsDisposed) { _editor.Activate(); return; }
            _editOpen = true;
            ClearDayTip();
            _editor = new AgendaEditor(this, d);
            _editor.Show();
        }

        // 由 AgendaEditor.FormClosed 回调：恢复焦点
        public void EndAgendaEdit()
        {
            _editOpen = false;
            _editor = null;
            try { Activate(); }
            catch { }
        }

        // ---- F1：日期详情卡片（优效日历式侧弹卡）----

        // 打开/刷新某天的详情卡片（--day-detail 视觉入口也会调用）。
        // 观感是月历旁弹出一张独立卡片（透明缝隙分隔）；仍是同一窗口，卡片开合
        // 只切换透明分区，月历位置纹丝不动，焦点/失焦/Esc 行为不变。
        public void OpenCard(DateTime d)
        {
            _cardDay = d;
            _selected = d;
            if (_cardOpen) { Invalidate(); return; }
            _cardOpen = true;
            ApplyCardLayout();
        }

        private void CloseCard()
        {
            if (!_cardOpen) return;
            _cardOpen = false;
            _cardHot = 0;
            ApplyCardLayout();
        }

        // 卡片开合：只切换透明分区（Region）。窗口位置/大小自创建起永不变
        // （构造器已常驻预留卡片区），反复 resize/移动造成的露底闪烁随之根除。
        // 关闭时卡片区不在 Region 内：不可见、点击穿透。
        private void ApplyCardLayout()
        {
            int h = ClientSize.Height;
            using (GraphicsPath p1 = RoundedPath(new Rectangle(_calX, 0, _calW - 1, h - 1), Scale(10)))
            using (GraphicsPath p2 = _cardOpen
                ? RoundedPath(new Rectangle(_cardRect.X, _cardRect.Y, _cardRect.Width - 1, h - 1), Scale(10))
                : null)
            {
                Region r = new Region(p1);
                if (p2 != null) r.Union(p2);
                Region old = Region;
                Region = r;
                if (old != null) old.Dispose();
            }
            Invalidate();
        }

        private void CardClick(Point p)
        {
            if (_btnCardClose.Contains(p)) { CloseCard(); return; }
            if (_btnCardPrev.Contains(p)) { StepCard(-1); return; }
            if (_btnCardNext.Contains(p)) { StepCard(1); return; }
            if (_btnCardEdit.Contains(p)) { OpenAgenda(_cardDay); return; }
            _cardHot = 0; // 点卡片空白不关闭，避免误触
            Invalidate();
        }

        // 卡片 ‹ › 逐日翻看；跨月时月历跟着翻页
        private void StepCard(int dir)
        {
            DateTime t = _cardDay.AddDays(dir);
            if (t.Year < 1901 || t.Year > 2100) return;
            _cardDay = t;
            _selected = t;
            if (t.Year != _viewMonth.Year || t.Month != _viewMonth.Month)
                _viewMonth = new DateTime(t.Year, t.Month, 1);
            Invalidate();
        }

        // 假期行：当天在假期区间内 = "国庆节 第2天/共8天"；否则距下一个法定/传统节日倒计时
        private string HolidayLine(DateTime d)
        {
            Dictionary<DateTime, string> feed = HolidayFeed.Data;
            if (feed != null && feed.ContainsKey(d) && feed[d] == "休")
            {
                DateTime start = d, end = d;
                while (feed.ContainsKey(start.AddDays(-1)) && feed[start.AddDays(-1)] == "休")
                    start = start.AddDays(-1);
                while (feed.ContainsKey(end.AddDays(1)) && feed[end.AddDays(1)] == "休")
                    end = end.AddDays(1);
                int total = (int)(end - start).TotalDays + 1;
                int nth = (int)(d - start).TotalDays + 1;
                CalendarData.DayInfo si = CalendarData.Get(start);
                string name = CalendarData.BadgeReason(start, "休", si.Festival) ?? "假期";
                return name + " 第" + nth + "天 / 共" + total + "天";
            }
            DateTime hd; string hn;
            if (CalendarData.NextFestival(d, out hd, out hn))
            {
                int cd = (int)(hd - d).TotalDays;
                return cd == 0 ? "今天 " + hn : "距 " + hn + " 还有 " + cd + " 天";
            }
            return null;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hot != -1) { _hot = -1; Invalidate(); }
            if (_cardHot != 0) { _cardHot = 0; Invalidate(); }
            ClearDayTip();
            Cursor = Cursors.Default;
        }

        // 标题栏 / 按钮 / 选择器格子 / 日格 / 列表行 的悬停命中（驱动手型光标与高亮）
        private int HitHot(Point p)
        {
            if (_btnTitlePrev.Contains(p)) return 1;
            if (_btnTitleNext.Contains(p)) return 2;
            if (_titleRect.Contains(p)) return 3;
            if (_btnToday.Contains(p)) return 4;
            if (_btnAgenda.Contains(p)) return 5;
            if (_pickMode == 3)
            {
                Rectangle a = PickArea();
                int row = (p.Y - a.Y - Scale(6)) / Scale(26);
                int idx = row + _listScroll;
                if (row >= 0 && idx >= 0 && idx < _listItems.Count && p.X >= a.X && p.X <= a.Right)
                    return 200 + idx;
                return -1;
            }
            if (_pickMode == 0)
            {
                if (_gridRect.Contains(p))
                {
                    int col = Math.Max(0, Math.Min(6, (p.X - _gridRect.X) / _cellW));
                    int row = Math.Max(0, Math.Min(5, (p.Y - _gridRect.Y) / _cellH));
                    return 100 + row * 7 + col;
                }
                return -1;
            }
            for (int i = 0; i < 12; i++)
                if (PickCell(i).Contains(p)) return 10 + i;
            return -1;
        }

        // 滚轮：月历翻月；选月视图翻年；选年视图翻十年；列表视图滚动（下滚=向后）
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_pickMode == 3)
            {
                int rowH = Scale(26);
                int capacity = Math.Max(1, (PickArea().Height - Scale(12)) / rowH);
                _listScroll = Math.Max(0, Math.Min(Math.Max(0, _listItems.Count - capacity),
                    _listScroll + (e.Delta < 0 ? 2 : -2)));
                Invalidate();
                return;
            }
            StepMonth(e.Delta < 0 ? 1 : -1);
        }

        // ‹ › 按钮 / 滚轮共用的步进：按当前层级翻月/翻年/翻十年
        // （年月限制在农历支持范围 1901–2100 内，越界直接忽略）
        private void StepMonth(int dir)
        {
            if (_pickMode == 3) return; // 列表视图无翻页
            if (_pickMode == 0)
            {
                DateTime t = _viewMonth.AddMonths(dir);
                if (t.Year < 1901 || t.Year > 2100) return;
                _viewMonth = t;
            }
            else
            {
                int y = _pickYear + dir * (_pickMode == 2 ? 10 : 1);
                if (y < 1901 || y > 2100) return;
                _pickYear = y;
            }
            Invalidate();
        }

        // 点标题逐级切换：月历 → 选月 → 选年 → 返回选月
        // （Windows 自带日历 / WinUI CalendarView / v-calendar 等主流日历的通用做法：
        //   标题即当前层级名，点击上钻；从上层选中一项则下钻返回）
        private void DrillTitle()
        {
            if (_pickMode == 0) { _pickMode = 1; _pickYear = _viewMonth.Year; }
            else _pickMode = _pickMode == 1 ? 2 : 1;
            _hot = -1;
            Invalidate();
        }

        // 底部"近期日程"按钮（N2）：在月历与列表视图间切换（--list-demo 也会调用）
        public void ToggleList()
        {
            if (_pickMode == 3) _pickMode = 0;
            else
            {
                _pickMode = 3;
                _listItems = AgendaStore.Upcoming(Now().Date, 14);
                _listScroll = 0;
            }
            _hot = -1;
            Invalidate();
        }

        // 年月选择器视觉/测试入口（--picker-demo）：直接进入选月视图
        public void OpenPickerDemo()
        {
            _pickMode = 1;
            _pickYear = _viewMonth.Year;
            _hot = -1;
            Invalidate();
        }

        // 点击选择器格子以外（头部/底部/缝隙）视为放弃选择，收起回月历
        private void PickClick(Point p)
        {
            for (int i = 0; i < 12; i++)
            {
                if (!PickCell(i).Contains(p)) continue;
                if (_pickMode == 1) { _viewMonth = new DateTime(_pickYear, i + 1, 1); _pickMode = 0; }
                else
                {
                    int y = DecadeStart() - 1 + i;
                    if (y < 1901 || y > 2100) return; // 农历支持范围外不响应
                    _pickYear = y; _pickMode = 1;
                }
                _hot = -1;
                Invalidate();
                return;
            }
            _pickMode = 0; _hot = -1; Invalidate();
        }

        private DateTime GridStart()
        {
            DateTime first = new DateTime(_viewMonth.Year, _viewMonth.Month, 1);
            return first.AddDays(-(((int)first.DayOfWeek + 6) % 7)); // 周一起始
        }

        // ---- 年月选择视图几何（与 DrawPickCell 的绘制严格一致）----

        // 格子区占满星期表头 + 网格区（层级名由顶部标题栏承担，不再另设头部行）。
        // 窗内坐标含 _calX（详情卡片开着时月历区整体右移，F1）
        private Rectangle PickArea()
        {
            return new Rectangle(_calX + _pad, _pad + _headerH + _titleH, _cellW * 7, _weekH + _cellH * 6);
        }

        // 12 个格子（4 列 × 3 行）：选月视图=1~12月；选年视图=十年区前一年~后一年（两端灰显）
        private Rectangle PickCell(int i)
        {
            Rectangle a = PickArea();
            int cw = a.Width / 4, ch = a.Height / 3;
            return new Rectangle(a.X + cw * (i % 4), a.Y + ch * (i / 4), cw, ch);
        }

        private int DecadeStart() { return _pickYear - _pickYear % 10; }

        private void GoToday()
        {
            DateTime n = Now().Date;
            _selected = n;
            _viewMonth = new DateTime(n.Year, n.Month, 1);
            _pickMode = 0;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = _calW; // 月历区宽度（详情卡片开着时窗体更宽，月历坐标整体平移 _calX）

            // F1：月历与卡片各自画圆角边框，中间是透明缝隙（Region 已抠空）。
            // 注意：TextRenderer(GDI) 不吃 GDI+ 世界变换，所以月历内容一律用
            // "构造矩形时直接 + _calX" 的窗内坐标，不用 TranslateTransform。
            using (Pen pen = new Pen(P.Border))
            {
                g.DrawPath(pen, RoundedPath(new Rectangle(_calX, 0, w - 1, ClientSize.Height - 1), Scale(10)));
                if (_cardOpen)
                    g.DrawPath(pen, RoundedPath(
                        new Rectangle(_cardRect.X, _cardRect.Y, _cardRect.Width - 1, ClientSize.Height - 1), Scale(10)));
            }

            // 头部：中国时间（右侧附本机时间，F2）+ 选中日期 + 农历干支（行距按字体行高排布）
            CalendarData.DayInfo di = CalendarData.Get(_selected);
            int x = _calX + _pad, y = _pad;
            string timeText = Now().ToString("HH:mm:ss");
            TextRenderer.DrawText(g, timeText, _fTime, new Point(x, y + Scale(12)), _cText);
            if (_hasLocal)
            {
                // 本机时间贴在大时间右下角（底部基线对齐），小字弱化不抢焦点
                Size tsz = TextRenderer.MeasureText(timeText, _fTime);
                string localText = LocalClock.ShortText; // "本机 HH:mm"
                Size lsz = TextRenderer.MeasureText(localText, _fInfo);
                int lx = x + tsz.Width + Scale(10);
                int ly = y + Scale(12) + tsz.Height - lsz.Height - Scale(1);
                TextRenderer.DrawText(g, localText, _fInfo, new Point(lx, ly), _cSubLunar);
            }
            string dateLine = _selected.ToString("yyyy年M月d日 ") + WeekFull[(int)_selected.DayOfWeek]
                + (_selected == Now().Date ? "　今天" : "");
            TextRenderer.DrawText(g, dateLine, _fDate, new Point(x, y + _offDate), _cText);
            string infoLine = "农历 " + di.LunarFullText + " · " + di.GanZhiYear + di.Animal + "年 · "
                + di.GanZhiMonth + "月 · " + di.GanZhiDay + "日";
            if (di.Badge != null) infoLine += "　" + di.Badge;
            TextRenderer.DrawText(g, infoLine, _fInfo, new Point(x, y + _offInfo), _cSub);

            // 标题栏：‹ 标题 ›。标题即当前层级名（月历/选月/选年），点击逐级切换；
            // ‹ › 按当前层级翻页——与 Windows 自带日历、WinUI CalendarView、v-calendar 一致
            int ty = _pad + _headerH;
            _btnTitlePrev = new Rectangle(_calX + _pad, ty, Scale(32), _titleH);
            _btnTitleNext = new Rectangle(_calX + _pad + _wkCol + _cellW * 7 - Scale(32), ty, Scale(32), _titleH);
            _titleRect = new Rectangle(_btnTitlePrev.Right + Scale(2), ty,
                _btnTitleNext.Left - _btnTitlePrev.Right - Scale(4), _titleH);
            string title = _pickMode == 0 ? _viewMonth.ToString("yyyy年M月")
                : _pickMode == 1 ? _pickYear + "年"
                : _pickMode == 2 ? DecadeStart() + " - " + (DecadeStart() + 9)
                : "近期日程";
            if (_hot == 3) FillHover(g, _titleRect);
            TextRenderer.DrawText(g, title, _fDate, _titleRect, _cText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            DrawGlyphButton(g, _btnTitlePrev, "‹", _hot == 1);
            DrawGlyphButton(g, _btnTitleNext, "›", _hot == 2);

            // 中部：星期表头 + 月历网格；或年月选择视图（矩形均为窗内坐标，含 _calX）
            int gy = _pad + _headerH + _titleH;
            _gridRect = new Rectangle(_calX + _pad + _wkCol, gy + _weekH, _cellW * 7, _cellH * 6);
            if (_pickMode == 0)
            {
                // 星期表头（周一起始）
                for (int i = 0; i < 7; i++)
                {
                    Rectangle r = new Rectangle(_calX + _pad + _wkCol + _cellW * i, gy, _cellW, _weekH);
                    TextRenderer.DrawText(g, WeekNames[i], _fWeek, r, i >= 5 ? _cRed : _cSub,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                // 周数列（B2）：每行取该行周四的 ISO 周数
                if (_wkCol > 0)
                {
                    TextRenderer.DrawText(g, "周", _fSmall, new Rectangle(_calX + _pad, gy, _wkCol - Scale(6), _weekH), _cSub,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    DateTime start = GridStart();
                    for (int row = 0; row < 6; row++)
                    {
                        Rectangle rc = new Rectangle(_calX + _pad, _gridRect.Y + _cellH * row, _wkCol - Scale(6), _cellH);
                        TextRenderer.DrawText(g, CalendarData.IsoWeek(start.AddDays(row * 7 + 3)).ToString(),
                            _fSmall, rc, _cSub,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    }
                }
                DateTime gridStart = GridStart();
                for (int i = 0; i < 42; i++)
                    DrawCell(g, i % 7, i / 7, gridStart.AddDays(i));
            }
            else
            {
                DrawPicker(g);
            }

            // 底部：近期日程（N2） + 回到今天
            int fy = _gridRect.Bottom + Scale(6);
            _btnAgenda = new Rectangle(_calX + _pad, fy, Scale(84), Scale(28));
            _btnToday = new Rectangle(_calX + w - _pad - Scale(80), fy, Scale(80), Scale(28));
            DrawMiniButton(g, _btnAgenda, "近期日程", _hot == 5);
            DrawMiniButton(g, _btnToday, "回到今天", _hot == 4);

            if (_cardOpen) DrawCard(g); // F1：详情卡片（窗内坐标）
        }

        // F1：日期详情卡片主体。头部已有 时间/日期/农历干支，卡片不重复，内容：
        // 大字日期、节日/节气、休班原因、假期区间/倒计时、当天日程（可编辑）、
        // 黄历（宜忌/冲煞/纳音/建星/值神）、年度进度（第几周/第几天/进度%）
        private void DrawCard(Graphics g)
        {
            DateTime d = _cardDay;
            CalendarData.DayInfo di = CalendarData.Get(d);
            int x = _cardRect.X, cw = _cardRect.Width;
            int pad = Scale(14);
            int rx = x + pad, rw = cw - pad * 2;
            int y = _cardRect.Y + pad;

            // 右上角：‹ › ×（逐日翻看 / 关闭）
            _btnCardClose = new Rectangle(x + cw - Scale(26), _cardRect.Y + Scale(4), Scale(22), Scale(22));
            _btnCardNext = new Rectangle(_btnCardClose.Left - Scale(26), _btnCardClose.Y, Scale(22), Scale(22));
            _btnCardPrev = new Rectangle(_btnCardNext.Left - Scale(26), _btnCardClose.Y, Scale(22), Scale(22));
            DrawGlyphButton(g, _btnCardPrev, "‹", _cardHot == 1);
            DrawGlyphButton(g, _btnCardNext, "›", _cardHot == 2);
            DrawGlyphButton(g, _btnCardClose, "×", _cardHot == 3);

            // 大字日期（日 + 月 + 星期）
            y += Scale(4);
            TextRenderer.DrawText(g, d.Day.ToString(), _fTime, new Rectangle(rx, y, Scale(52), Scale(38)),
                _cText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, d.ToString("M月"), _fDate,
                new Rectangle(rx + Scale(58), y + Scale(5), rw - Scale(58), Scale(18)),
                _cText, TextFormatFlags.Left | TextFormatFlags.Top);
            TextRenderer.DrawText(g, WeekFull[(int)d.DayOfWeek], _fDate,
                new Rectangle(rx + Scale(58), y + Scale(23), rw - Scale(58), Scale(16)),
                _cSub, TextFormatFlags.Left | TextFormatFlags.Top);
            y += Scale(46);

            // 农历 + 生肖年
            TextRenderer.DrawText(g, "农历 " + di.LunarFullText + " · " + di.GanZhiYear + di.Animal + "年", _fInfo,
                new Rectangle(rx, y, rw, Scale(16)), _cSub,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            y += Scale(21);

            // 节日 / 节气
            if (di.Festival != null || di.TermName != null)
            {
                bool isFest = di.Festival != null;
                TextRenderer.DrawText(g, isFest ? di.Festival : di.TermName, _fInfo,
                    new Rectangle(rx, y, rw, Scale(16)), isFest ? _cRed : _cBlue);
                y += Scale(21);
            }

            // 休/班原因（"周末"这类日常角标不占行）
            string reason = CalendarData.BadgeReason(d, di.Badge, di.Festival);
            if (reason != null && reason != "周末")
            {
                bool work = di.Badge == "班";
                TextRenderer.DrawText(g, reason + (work ? "（补班）" : "（休）"), _fInfo,
                    new Rectangle(rx, y, rw, Scale(16)), work ? _cWorkFg : _cRestFg);
                y += Scale(21);
            }

            // 假期区间 / 倒计时
            string hol = HolidayLine(d);
            if (hol != null)
            {
                TextRenderer.DrawText(g, hol, _fInfo, new Rectangle(rx, y, rw, Scale(16)), _cAccent);
                y += Scale(21);
            }

            DrawSep(g, rx, y, rw);
            y += Scale(11);

            // 当天日程（最多 4 行，超出折叠）
            TextRenderer.DrawText(g, "日程", _fSmall, new Rectangle(rx, y, rw, Scale(15)), _cAccent);
            y += Scale(21);
            List<AgendaItem> items = AgendaStore.On(d);
            if (items.Count == 0)
            {
                TextRenderer.DrawText(g, "暂无日程，点下方按钮添加", _fSmall,
                    new Rectangle(rx, y, rw, Scale(15)), _cSubLunar);
                y += Scale(21);
            }
            else
            {
                int shown = 0;
                foreach (AgendaItem it in items)
                {
                    if (shown >= 4)
                    {
                        TextRenderer.DrawText(g, "… 共 " + items.Count + " 项", _fSmall,
                            new Rectangle(rx, y, rw, Scale(15)), _cSub);
                        y += Scale(20);
                        break;
                    }
                    string row = (it.Time != null ? it.Time + " " : "") + it.Text;
                    TextRenderer.DrawText(g, "· " + row, _fWeek, new Rectangle(rx, y, rw, Scale(18)),
                        _cText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    y += Scale(22);
                    shown++;
                }
            }
            _btnCardEdit = new Rectangle(rx, y, Scale(96), Scale(26));
            DrawMiniButton(g, _btnCardEdit, "编辑日程…", _cardHot == 4);
            y += Scale(36);
            DrawSep(g, rx, y, rw);
            y += Scale(11);

            // 黄历
            TextRenderer.DrawText(g, "黄历", _fSmall, new Rectangle(rx, y, rw, Scale(15)), _cAccent);
            y += Scale(21);
            string officer = Almanac.Officer(di.GanZhiDay, di.GanZhiMonth);
            TextRenderer.DrawText(g, "宜  " + Almanac.Yi(officer), _fInfo,
                new Rectangle(rx, y, rw, Scale(16)), _cText,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            y += Scale(21);
            TextRenderer.DrawText(g, "忌  " + Almanac.Ji(officer), _fInfo,
                new Rectangle(rx, y, rw, Scale(16)), _cRed,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            y += Scale(21);
            TextRenderer.DrawText(g, Almanac.ChongSha(di.GanZhiDay), _fInfo,
                new Rectangle(rx, y, rw, Scale(16)), _cSub);
            y += Scale(21);
            bool yellow;
            string zs = Almanac.ZhiShen(di.GanZhiDay, di.GanZhiMonth, out yellow);
            TextRenderer.DrawText(g, Almanac.Nayin(di.GanZhiDay) + " · " + officer + "日 · " + zs + (yellow ? "（黄道）" : "（黑道）"),
                _fInfo, new Rectangle(rx, y, rw, Scale(16)), _cSub,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            y += Scale(21);
            DrawSep(g, rx, y, rw);
            y += Scale(11);

            // 年度进度（优效日历同款信息）
            int daysInYear = DateTime.IsLeapYear(d.Year) ? 366 : 365;
            int pct = (int)Math.Round(d.DayOfYear * 100.0 / daysInYear);
            TextRenderer.DrawText(g, "第" + CalendarData.IsoWeek(d) + "周 · 全年第" + d.DayOfYear + "天 · 年进度 " + pct + "%",
                _fSmall, new Rectangle(rx, y, rw, Scale(15)), _cSubLunar,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        private void DrawSep(Graphics g, int x, int y, int w)
        {
            using (Pen p = new Pen(_cBorder))
                g.DrawLine(p, x, y + Scale(3), x + w, y + Scale(3));
        }

        // 年月选择视图：选月=当年 12 个月；选年=十年区 12 年（层级名在顶部标题栏）
        private void DrawPicker(Graphics g)
        {
            if (_pickMode == 3) { DrawAgendaList(g); return; } // N2：近期日程列表
            for (int i = 0; i < 12; i++) DrawPickCell(g, i);
        }

        // N2：近期日程列表（未来 14 天，滚轮滚动，点条目跳转当天）
        private void DrawAgendaList(Graphics g)
        {
            Rectangle a = PickArea();
            int rowH = Scale(26);
            int top = a.Y + Scale(6);
            if (_listItems.Count == 0)
            {
                Rectangle all = new Rectangle(a.X, a.Y, a.Width, a.Height);
                TextRenderer.DrawText(g, "未来 14 天没有日程", _fWeek, all, _cSub,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            g.SetClip(a);
            int skip = _listScroll;
            for (int i = skip; i < _listItems.Count; i++)
            {
                int row = i - skip;
                int y = top + row * rowH;
                if (y + rowH > a.Bottom) break;
                AgendaItem it = _listItems[i];
                Rectangle rr = new Rectangle(a.X + Scale(6), y, a.Width - Scale(12), rowH);
                if (_hot == 200 + i)
                    using (SolidBrush b = new SolidBrush(_cHover))
                        g.FillPath(b, RoundedPath(rr, Scale(4)));
                bool today = it.Date == Now().Date;
                string chip = today ? "今天" : it.Date == Now().Date.AddDays(1) ? "明天"
                    : it.Date.ToString("M/d ") + WeekNames[((int)it.Date.DayOfWeek + 6) % 7];
                TextRenderer.DrawText(g, chip, _fSmall,
                    new Rectangle(rr.X, rr.Y, Scale(64), rowH),
                    today ? _cAccent : _cSub,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, it.Text, _fWeek,
                    new Rectangle(rr.X + Scale(72), rr.Y, rr.Width - Scale(72), rowH),
                    _cText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            g.ResetClip();
        }

        private void DrawPickCell(Graphics g, int i)
        {
            Rectangle r = PickCell(i);
            string text; bool view, now, dim = false;
            if (_pickMode == 1)
            {
                text = (i + 1) + "月";
                view = _viewMonth.Year == _pickYear && _viewMonth.Month == i + 1;
                now = Now().Year == _pickYear && Now().Month == i + 1;
            }
            else
            {
                int y = DecadeStart() - 1 + i;
                text = y.ToString();
                dim = i == 0 || i == 11;
                view = _viewMonth.Year == y;
                now = Now().Year == y;
            }
            if (view) // 当前查看的月/年：强调色浅底圆角
                using (SolidBrush b = new SolidBrush(_cSelBg))
                    g.FillPath(b, RoundedPath(r, Scale(8)));
            else if (_hot == 10 + i)
                FillHover(g, r);
            Color c = dim ? _cDim : now ? _cAccent : _cText;
            TextRenderer.DrawText(g, text, _fWeek, r, c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // 悬停高亮底（圆角矩形）
        private void FillHover(Graphics g, Rectangle r)
        {
            using (SolidBrush b = new SolidBrush(_cHover))
                g.FillPath(b, RoundedPath(r, Scale(6)));
        }

        // 标题栏翻页按钮：平时仅 ‹ › 字形，悬停补圆角底（与主流日历一致）
        private void DrawGlyphButton(Graphics g, Rectangle r, string glyph, bool hot)
        {
            if (hot) FillHover(g, r);
            TextRenderer.DrawText(g, glyph, _fWeek, r, _cText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void DrawCell(Graphics g, int col, int row, DateTime d)
        {
            Rectangle r = new Rectangle(_gridRect.X + _cellW * col, _gridRect.Y + _cellH * row, _cellW, _cellH);
            bool other = d.Month != _viewMonth.Month;
            CalendarData.DayInfo di = CalendarData.Get(d);
            bool weekend = d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday;

            if (d == _selected)
                using (SolidBrush b = new SolidBrush(_cSelBg))
                    g.FillRectangle(b, r);

            bool workBadge = di.Badge == "班"; // 调休补班的周末按工作日着色
            Color numColor = other ? _cDim : (workBadge ? _cText : (di.StatutoryRest || weekend ? _cRed : _cText));
            string sub = di.Festival ?? di.TermName ?? di.LunarText;
            Color subColor = other ? _cDimSub
                : di.Festival != null ? _cRed
                : di.TermName != null ? _cBlue
                : _cSubLunar;

            Rectangle numRect = new Rectangle(r.X, r.Y + Scale(6), r.Width, Scale(22));
            if (d == Now().Date)
            {
                int dsz = Scale(24);
                Rectangle c = new Rectangle(r.X + (r.Width - dsz) / 2, r.Y + Scale(4), dsz, dsz);
                using (SolidBrush b = new SolidBrush(_cAccent)) g.FillEllipse(b, c);
                DrawInkCentered(g, d.Day.ToString(), _fDay, c, Color.White);
            }
            else
            {
                TextRenderer.DrawText(g, d.Day.ToString(), _fDay, numRect, numColor, TextFormatFlags.HorizontalCenter);
            }

            Rectangle subRect = new Rectangle(r.X, r.Y + Scale(32), r.Width, Scale(16));
            TextRenderer.DrawText(g, sub, _fSmall, subRect, subColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);

            if (!other && di.Badge != null)
            {
                bool work = di.Badge == "班";
                Color bg = work ? _cWorkBg : _cRestBg;
                Color fg = work ? _cWorkFg : _cRestFg;
                Rectangle br = new Rectangle(r.Right - Scale(20), r.Y + Scale(2), Scale(18), Scale(16));
                using (SolidBrush b = new SolidBrush(bg)) g.FillRectangle(b, br);
                TextRenderer.DrawText(g, di.Badge, _fSmall, br, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // B4：有日程的日子在格底画圆点
            if (!other && AgendaStore.Has(d))
            {
                int dsz = Scale(5);
                using (SolidBrush b = new SolidBrush(_cDot))
                    g.FillEllipse(b, r.X + (r.Width - dsz) / 2, r.Bottom - Scale(10), dsz, dsz);
            }
        }

        // 按字形墨水边界精确居中绘制（GDI TextRenderer 的 VerticalCenter/HorizontalCenter
        // 依据整行度量，单个数字会向左上偏 1~3px；这里用 GDI+ 实测墨水宽高后定位）
        private void DrawInkCentered(Graphics g, string s, Font f, Rectangle rect, Color color)
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                SizeF sz = g.MeasureString(s, f, rect.Size, sf);
                float x = rect.X + (rect.Width - sz.Width) / 2f;
                float y = rect.Y + (rect.Height - sz.Height) / 2f;
                using (SolidBrush b = new SolidBrush(color))
                    g.DrawString(s, f, b, x, y, sf);
            }
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;
        }

        private void DrawMiniButton(Graphics g, Rectangle r, string text, bool hot)
        {
            using (SolidBrush b = new SolidBrush(hot ? _cBtnHot : _cBtnBg))
                g.FillPath(b, RoundedPath(r, Scale(6)));
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
            _tipPanel.Dispose();
            if (_editor != null && !_editor.IsDisposed) _editor.Dispose();
            foreach (Font f in new[] { _fTime, _fDate, _fInfo, _fWeek, _fDay, _fSmall, _fFoot })
                if (f != null) f.Dispose();
            base.OnFormClosed(e);
        }
    }

    // ============================ 圆角按钮 ============================

    // 自绘圆角按钮：Primary=强调色实底白字；Neutral=中性底+边框。悬停加深。
    internal sealed class RoundButton : Control
    {
        public bool Primary;
        public Color Accent, TextC, NeutralBg, NeutralBorder;

        public RoundButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        private bool _hot, _pressed;
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = false; _pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _pressed = false; Invalidate(); }

        private static GraphicsPath Rounded(Rectangle r, int rad)
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Rounded(r, 6))
            {
                if (Primary)
                {
                    // kimi 规范：悬停加深 8%，按下加深 16%
                    Color fill = _pressed ? PanelPalette.Blend(Accent, Color.Black, 0.16f)
                        : _hot ? PanelPalette.Blend(Accent, Color.Black, 0.08f) : Accent;
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                }
                else
                {
                    using (SolidBrush b = new SolidBrush(_hot ? PanelPalette.Blend(NeutralBg, Color.Black, 0.08f) : NeutralBg))
                        g.FillPath(b, p);
                    using (Pen pen = new Pen(_hot ? PanelPalette.Blend(NeutralBorder, Color.Black, 0.12f) : NeutralBorder))
                        g.DrawPath(pen, p);
                }
            }
            TextRenderer.DrawText(g, Text, Font, r, TextC,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ============================ 日程编辑窗 ============================

    // 无边框圆角小窗，深浅色跟随面板；视觉规范来自 kimi-cli 讨论：
    // 窗体 344×264 圆角10；标题栏 48（标题 + 32×32 字形关闭钮）；
    // 编辑容器 312×144（底 #FAFAFA/#282828 圆角6，无边框 TextBox 内嵌，空内容显示占位）；
    // 底部 [取消 72×32 次按钮] [保存 80×32 主按钮=强调色]；Esc=取消、Ctrl+Enter=保存、Enter=换行
    internal sealed class AgendaEditor : Form
    {
        private readonly DateTime _day;
        private readonly CalendarPanel _owner;
        private readonly TextBox _box;
        private readonly Label _placeholder;
        private readonly PanelPalette P;
        private readonly Color _subC, _phC, _closeHotBg;
        private readonly Font _fTitle, _fClose, _fBtn;
        private bool _closeHot;
        private Rectangle _closeRect;

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public AgendaEditor(CalendarPanel owner, DateTime day)
        {
            _owner = owner;
            _day = day.Date;
            P = PanelPalette.Load();
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = P.Back;
            ClientSize = new Size(344, 264);
            using (GraphicsPath p = RoundedPath(new Rectangle(0, 0, 343, 263), 10))
                Region = new Region(p);

            // 深浅两套补充色（kimi 规范值）
            Color boxBg = P.Dark ? Color.FromArgb(40, 40, 40) : Color.FromArgb(250, 250, 250);
            Color boxBorder = P.Dark ? Color.FromArgb(72, 72, 72) : Color.FromArgb(216, 216, 216);
            _subC = P.Dark ? Color.FromArgb(168, 168, 168) : Color.FromArgb(107, 107, 107);
            _phC = P.Dark ? Color.FromArgb(133, 133, 133) : Color.FromArgb(154, 154, 154);
            _closeHotBg = P.Dark ? Color.FromArgb(56, 56, 56) : Color.FromArgb(228, 231, 236);
            _fTitle = new Font("Microsoft YaHei UI", 10.5F);
            _fClose = new Font("Segoe UI", 12F);
            _fBtn = new Font("Microsoft YaHei UI", 9F);

            // 编辑容器
            Panel box = new Panel();
            box.Location = new Point(16, 52);
            box.Size = new Size(312, 144);
            box.Paint += delegate(object s, PaintEventArgs e2)
            {
                Graphics g = e2.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rb = new Rectangle(0, 0, box.Width - 1, box.Height - 1);
                using (GraphicsPath p = RoundedPath(rb, 6))
                {
                    using (SolidBrush b = new SolidBrush(boxBg)) g.FillPath(b, p);
                    using (Pen pen = new Pen(boxBorder)) g.DrawPath(pen, p);
                }
            };
            Controls.Add(box);

            _box = new TextBox();
            _box.BorderStyle = BorderStyle.None;
            _box.Multiline = true;
            _box.ScrollBars = ScrollBars.Vertical;
            _box.Font = new Font("Microsoft YaHei UI", 10F);
            _box.BackColor = boxBg;
            _box.ForeColor = P.Text;
            _box.Location = new Point(12, 10);
            _box.Size = new Size(288, 124);
            _box.Text = string.Join("\r\n", AgendaStore.For(_day).ToArray());
            box.Controls.Add(_box);

            // 占位提示：空内容时显示，输入即消失；点击占位聚焦输入框
            _placeholder = new Label();
            _placeholder.Text = "输入日程内容…";
            _placeholder.Font = new Font("Microsoft YaHei UI", 9F);
            _placeholder.ForeColor = _phC;
            _placeholder.BackColor = boxBg;
            _placeholder.AutoSize = false;
            _placeholder.Size = new Size(200, 24);
            _placeholder.Location = new Point(13, 11);
            _placeholder.Cursor = Cursors.IBeam;
            _placeholder.Visible = _box.Text.Length == 0;
            _placeholder.MouseDown += delegate { _box.Focus(); };
            box.Controls.Add(_placeholder);
            _box.TextChanged += delegate { _placeholder.Visible = _box.Text.Length == 0; };

            // 底部按钮：保存（主）+ 取消（次）
            RoundButton btnCancel = new RoundButton();
            btnCancel.Text = "取消";
            btnCancel.Font = _fBtn;
            btnCancel.Size = new Size(72, 32);
            btnCancel.Location = new Point(168, 216);
            btnCancel.Primary = false;
            btnCancel.NeutralBg = P.Back;
            btnCancel.NeutralBorder = boxBorder;
            btnCancel.TextC = _subC;
            btnCancel.Accent = P.Accent;
            btnCancel.Click += delegate { Close(); };
            Controls.Add(btnCancel);

            RoundButton btnSave = new RoundButton();
            btnSave.Text = "保存";
            btnSave.Font = _fBtn;
            btnSave.Size = new Size(80, 32);
            btnSave.Location = new Point(248, 216);
            btnSave.Primary = true;
            btnSave.Accent = P.Accent;
            btnSave.TextC = Color.White;
            btnSave.Click += delegate { Save(); };
            Controls.Add(btnSave);

            _closeRect = new Rectangle(ClientSize.Width - 16 - 32, 8, 32, 32);

            // 定位在面板上方、右对齐；越界夹回工作区
            var wa = Screen.FromControl(owner).WorkingArea;
            Point tl = owner.PointToScreen(Point.Empty);
            int x = tl.X + owner.ClientSize.Width - Width - 12;
            int y = tl.Y - Height - 10;
            Location = new Point(
                Math.Max(wa.Left + 4, Math.Min(x, wa.Right - Width - 4)),
                Math.Max(wa.Top + 4, Math.Min(y, wa.Bottom - Height - 4)));

            Shown += delegate
            {
                _box.Focus();
                _box.SelectionStart = _box.TextLength; // 光标落末尾，避免预填内容呈全选蓝底
            };
            FormClosed += delegate { _owner.EndAgendaEdit(); };
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = RoundedPath(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 10))
            using (Pen pen = new Pen(P.Border))
                g.DrawPath(pen, p);
            // 标题（48px 标题栏，左 16 垂直居中）
            TextRenderer.DrawText(g, _day.ToString("M月d日") + " 日程备忘", _fTitle,
                new Rectangle(16, 0, 240, 48), P.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            // 关闭按钮：轻量字形 ×，悬停补圆角底（不用系统红）
            if (_closeHot)
                using (SolidBrush b = new SolidBrush(_closeHotBg))
                    g.FillPath(b, RoundedPath(_closeRect, 6));
            TextRenderer.DrawText(g, "×", _fClose, _closeRect, _closeHot ? P.Text : _subC,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hot = _closeRect.Contains(e.Location);
            if (hot != _closeHot) { _closeHot = hot; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_closeHot) { _closeHot = false; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (_closeRect.Contains(e.Location)) { Close(); return; }
            if (e.Y < 48)
            {
                // 标题栏拖动
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == (Keys.Control | Keys.Enter)) { Save(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Save()
        {
            List<string> items = new List<string>();
            foreach (string raw in _box.Lines)
            {
                string t = raw.Trim();
                if (t.Length > 0) items.Add(t);
            }
            AgendaStore.Set(_day, items);
            _owner.Invalidate();
            Close();
        }
    }

    // ============================ 设置窗口 ============================

    // C2：时钟格式 / 第二时区 / 周数 / 主题 / 强调色 / 假日数据 / 日程 / 自启
    internal sealed class SettingsForm : Form
    {
        private readonly ClockForm _clock;
        private CheckBox _cbSeconds, _cbWeekday, _cbDateLine, _cbLocal, _cbTz2, _cbWeekNum, _cbAutoFetch, _cbAutoStart;
        private TextBox _tbFormat, _tbTzLabel;
        private ComboBox _cboTz;
        private RadioButton _rbFollow, _rbLight, _rbDark;
        private Label _lblFetch;
        private string _accent;

        private static readonly SettingsForm.TzItem[] TzOptions =
        {
            new SettingsForm.TzItem("协调世界时", "UTC"),
            new SettingsForm.TzItem("伦敦", "GMT Standard Time"),
            new SettingsForm.TzItem("纽约", "Eastern Standard Time"),
            new SettingsForm.TzItem("洛杉矶", "Pacific Standard Time"),
            new SettingsForm.TzItem("东京", "Tokyo Standard Time"),
            new SettingsForm.TzItem("新加坡", "Singapore Standard Time"),
            new SettingsForm.TzItem("悉尼", "AUS Eastern Standard Time"),
            new SettingsForm.TzItem("曼谷", "SE Asia Standard Time"),
            new SettingsForm.TzItem("迪拜", "Arabian Standard Time"),
            new SettingsForm.TzItem("莫斯科", "Russia Time Zone 3"),
        };

        private class TzItem
        {
            public string Label, Id;
            public TzItem(string label, string id) { Label = label; Id = id; }
            public override string ToString() { return Label + "（" + Id + "）"; }
        }

        public SettingsForm(ClockForm clock)
        {
            _clock = clock;
            _accent = Settings.Accent;
            Text = "中国时间 · 设置";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(474, 598);
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 9F);
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Right - Width - 24, wa.Bottom - Height - 48);

            // ---- 时钟显示 ----
            GroupBox g1 = new GroupBox();
            g1.Text = "时钟显示";
            g1.Location = new Point(12, 10);
            g1.Size = new Size(450, 184);
            _cbSeconds = new CheckBox();
            _cbSeconds.Text = "显示秒"; _cbSeconds.Location = new Point(16, 26); _cbSeconds.AutoSize = true; _cbSeconds.Checked = Settings.ShowSeconds;
            _cbWeekday = new CheckBox();
            _cbWeekday.Text = "显示星期"; _cbWeekday.Location = new Point(112, 26); _cbWeekday.AutoSize = true; _cbWeekday.Checked = Settings.ShowWeekday;
            _cbDateLine = new CheckBox();
            _cbDateLine.Text = "显示日期行"; _cbDateLine.Location = new Point(214, 26); _cbDateLine.AutoSize = true; _cbDateLine.Checked = Settings.ShowDateLine;
            Label l1 = new Label();
            l1.Text = "自定义时间格式（留空用预设，示例：HH:mm、HH:mm:ss、yyyy-MM-dd）";
            l1.Location = new Point(16, 58); l1.AutoSize = true;
            _tbFormat = new TextBox();
            _tbFormat.Location = new Point(16, 80); _tbFormat.Size = new Size(416, 24); _tbFormat.Text = Settings.CustomTimeFormat;
            _cbTz2 = new CheckBox();
            _cbTz2.Text = "第二时区"; _cbTz2.Location = new Point(16, 114); _cbTz2.AutoSize = true; _cbTz2.Checked = Settings.ShowSecondClock;
            _cboTz = new ComboBox();
            _cboTz.Location = new Point(106, 110); _cboTz.Size = new Size(206, 24); _cboTz.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (SettingsForm.TzItem z in TzOptions) _cboTz.Items.Add(z);
            _cboTz.SelectedIndex = IndexOfTz(Settings.SecondTzId);
            _tbTzLabel = new TextBox();
            _tbTzLabel.Location = new Point(322, 110); _tbTzLabel.Size = new Size(110, 24); _tbTzLabel.Text = Settings.SecondTzLabel;
            _cbLocal = new CheckBox();
            _cbLocal.Text = "显示本地时间（电脑时区≠北京时间时，面板顶部与悬停提示显示）"; _cbLocal.Location = new Point(16, 148); _cbLocal.AutoSize = true; _cbLocal.Checked = Settings.ShowLocalClock;
            g1.Controls.AddRange(new Control[] { _cbSeconds, _cbWeekday, _cbDateLine, l1, _tbFormat, _cbTz2, _cboTz, _tbTzLabel, _cbLocal });
            Controls.Add(g1);

            // ---- 日历外观 ----
            GroupBox g2 = new GroupBox();
            g2.Text = "日历外观";
            g2.Location = new Point(12, 202);
            g2.Size = new Size(450, 132);
            _cbWeekNum = new CheckBox();
            _cbWeekNum.Text = "显示周数列"; _cbWeekNum.Location = new Point(16, 26); _cbWeekNum.AutoSize = true; _cbWeekNum.Checked = Settings.ShowWeekNumbers;
            _rbFollow = new RadioButton();
            _rbFollow.Text = "深浅跟随系统"; _rbFollow.Location = new Point(130, 25); _rbFollow.AutoSize = true;
            _rbLight = new RadioButton();
            _rbLight.Text = "浅色"; _rbLight.Location = new Point(266, 25); _rbLight.AutoSize = true;
            _rbDark = new RadioButton();
            _rbDark.Text = "深色"; _rbDark.Location = new Point(336, 25); _rbDark.AutoSize = true;
            if (Settings.ThemeMode == 2) _rbDark.Checked = true;
            else if (Settings.ThemeMode == 1) _rbLight.Checked = true;
            else _rbFollow.Checked = true;
            Label l2 = new Label();
            l2.Text = "强调色"; l2.Location = new Point(16, 66); l2.AutoSize = true;
            string[] swatches = { "#4472C4", "#C94F4F", "#2E8B57", "#D97941", "#7A5FB5", "#0F8A8A" };
            Button[] sw = new Button[swatches.Length];
            for (int i = 0; i < swatches.Length; i++)
            {
                string c = swatches[i];
                sw[i] = new Button();
                sw[i].Size = new Size(28, 28);
                sw[i].Location = new Point(78 + i * 36, 58);
                sw[i].FlatStyle = FlatStyle.Flat;
                sw[i].BackColor = ColorTranslator.FromHtml(c);
                sw[i].Click += delegate { _accent = c; };
                g2.Controls.Add(sw[i]);
            }
            Button btnCustom = new Button();
            btnCustom.Text = "自定义…";
            btnCustom.Location = new Point(300, 57);
            btnCustom.Size = new Size(88, 28);
            btnCustom.Click += delegate
            {
                ColorDialog cd = new ColorDialog();
                cd.FullOpen = true;
                try { cd.Color = ColorTranslator.FromHtml(_accent); } catch { }
                if (cd.ShowDialog(this) == DialogResult.OK) _accent = "#" + cd.Color.R.ToString("X2") + cd.Color.G.ToString("X2") + cd.Color.B.ToString("X2");
            };
            Label l3 = new Label();
            l3.Text = "主题与强调色在下次打开日历面板时生效";
            l3.Location = new Point(16, 100); l3.AutoSize = true; l3.ForeColor = Color.FromArgb(130, 130, 130);
            g2.Controls.AddRange(new Control[] { _cbWeekNum, _rbFollow, _rbLight, _rbDark, l2, btnCustom, l3 });
            Controls.Add(g2);

            // ---- 假日与日程数据 ----
            GroupBox g3 = new GroupBox();
            g3.Text = "假日与日程数据";
            g3.Location = new Point(12, 342);
            g3.Size = new Size(450, 132);
            _cbAutoFetch = new CheckBox();
            _cbAutoFetch.Text = "自动更新法定假日/调休数据（holiday-cn 数据源，每日一次）";
            _cbAutoFetch.Location = new Point(16, 26); _cbAutoFetch.AutoSize = true; _cbAutoFetch.Checked = Settings.AutoFetchHolidays;
            Button btnFetch = new Button();
            btnFetch.Text = "立即更新";
            btnFetch.Location = new Point(16, 56); btnFetch.Size = new Size(92, 27);
            _lblFetch = new Label();
            _lblFetch.Text = FetchStateText();
            _lblFetch.Location = new Point(118, 61); _lblFetch.AutoSize = true;
            btnFetch.Click += delegate
            {
                btnFetch.Enabled = false;
                _lblFetch.Text = "正在更新…";
                HolidayFeed.FetchAsync(delegate(bool fetchOk)
                {
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            btnFetch.Enabled = true;
                            _lblFetch.Text = fetchOk ? "更新完成：" + Settings.HolidaysFetchedAt : "更新失败（离线时使用缓存/内置规则）";
                        });
                    }
                    catch { }
                });
            };
            Button btnHol = new Button();
            btnHol.Text = "节假日配置…";
            btnHol.Location = new Point(16, 92); btnHol.Size = new Size(110, 27);
            btnHol.Click += delegate
            {
                CalendarData.EnsureOverrideFile();
                try { Process.Start("notepad.exe", CalendarData.OverrideFilePath); }
                catch (Exception ex) { MessageBox.Show("打开配置失败：" + ex.Message, "中国时间"); }
            };
            Button btnAgenda = new Button();
            btnAgenda.Text = "日程文件…";
            btnAgenda.Location = new Point(136, 92); btnAgenda.Size = new Size(110, 27);
            btnAgenda.Click += delegate
            {
                AgendaStore.EnsureFile();
                try { Process.Start("notepad.exe", AgendaStore.FilePath); }
                catch (Exception ex) { MessageBox.Show("打开日程失败：" + ex.Message, "中国时间"); }
            };
            g3.Controls.AddRange(new Control[] { _cbAutoFetch, btnFetch, _lblFetch, btnHol, btnAgenda });
            Controls.Add(g3);

            // ---- 通用 ----
            GroupBox g4 = new GroupBox();
            g4.Text = "通用";
            g4.Location = new Point(12, 482);
            g4.Size = new Size(450, 58);
            _cbAutoStart = new CheckBox();
            _cbAutoStart.Text = "开机自启（登录时启动）";
            _cbAutoStart.Location = new Point(16, 24); _cbAutoStart.AutoSize = true; _cbAutoStart.Checked = ClockForm.IsAutoStart();
            g4.Controls.Add(_cbAutoStart);
            Controls.Add(g4);

            // ---- 保存/取消 ----
            Button ok = new Button();
            ok.Text = "保存";
            ok.Size = new Size(88, 30);
            ok.Location = new Point(286, 552);
            ok.Click += delegate { SaveAndClose(); };
            Button cancel = new Button();
            cancel.Text = "取消";
            cancel.Size = new Size(88, 30);
            cancel.Location = new Point(382, 552);
            cancel.Click += delegate { Close(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private static int IndexOfTz(string id)
        {
            for (int i = 0; i < TzOptions.Length; i++)
                if (TzOptions[i].Id == id) return i;
            return 0;
        }

        private static string FetchStateText()
        {
            return Settings.HolidaysFetchedAt.Length > 0 ? "上次更新：" + Settings.HolidaysFetchedAt : "尚未更新（离线时使用缓存/内置规则）";
        }

        private void SaveAndClose()
        {
            Settings.ShowSeconds = _cbSeconds.Checked;
            Settings.ShowWeekday = _cbWeekday.Checked;
            Settings.ShowDateLine = _cbDateLine.Checked;
            Settings.ShowLocalClock = _cbLocal.Checked;
            Settings.CustomTimeFormat = _tbFormat.Text.Trim();
            Settings.ShowSecondClock = _cbTz2.Checked;
            SettingsForm.TzItem tz = _cboTz.SelectedItem as SettingsForm.TzItem;
            if (tz != null)
            {
                Settings.SecondTzId = tz.Id;
                if (_tbTzLabel.Text.Trim().Length > 0) Settings.SecondTzLabel = _tbTzLabel.Text.Trim();
                else Settings.SecondTzLabel = tz.Label;
            }
            Settings.ShowWeekNumbers = _cbWeekNum.Checked;
            Settings.ThemeMode = _rbDark.Checked ? 2 : (_rbLight.Checked ? 1 : 0);
            Settings.Accent = _accent;
            Settings.AutoFetchHolidays = _cbAutoFetch.Checked;
            Settings.Save();
            ClockForm.SetAutoStart(_cbAutoStart.Checked);
            if (_clock != null && !_clock.IsDisposed) _clock.RefreshSettings();
            Close();
        }
    }

    // ============================ 任务栏时钟 ============================
    internal sealed class ClockForm : Form
    {
        private const int HWND_TOPMOST = -1;
        private const int SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10, SWP_ASYNCWINDOWPOS = 0x4000, SWP_FRAMECHANGED = 0x20;
        private const int EVENT_SYSTEM_FOREGROUND = 0x0003; // 前台窗口切换
        private const int EVENT_OBJECT_REORDER = 0x8004;    // 窗口 Z 序变化
        private const int WINEVENT_OUTOFCONTEXT = 0x0;

        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, int after, int x, int y, int cx, int cy, int flags);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);

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
        // 保层不用 WinForms 定时器：WM_TIMER 是低优先级消息，点击任务栏的激活事件风暴中
        // 会被饿死。改用后台线程心跳（ElevenClock 同款策略）：每 100ms 把窗口显示出来
        // （无论被什么原因隐藏）并声明置顶。
        private volatile bool _keepTopRunning;

        private Rectangle _nativeRect;   // 系统时钟区域（物理像素）
        private Rectangle _trayRect;     // 任务栏区域
        private DockEdge _dock = DockEdge.Bottom; // 任务栏停靠边（F1，随 EnsureTrayRect 刷新）
        private Color _bg = SystemColors.Control;
        private bool _lightTaskbar = true;
        private string _timeText = "", _dateText = "", _tzText = "", _lastStamp = "";
        private bool _hideDate, _hideTz; // 侧边窄栏放不下的行（F1 宽度自适应；本机时间不上任务栏，见 F2）
        private float _fontScale = 1f, _fontScaleCache = -1f;       // 窄栏时间行缩字号
        private Font _f1, _f2, _f3;
        private ToolTip _tip;            // A2：悬停显示 农历/节日/倒计时
        private string _lastTipText = "";
        private TimeZoneInfo _tz2;       // A3：第二时区
        private string _tz2Id = "";
        private int _fontH = -1;
        private DateTime _lastToggle = DateTime.MinValue;
        private DateTime _panelClosedAt = DateTime.MinValue;
        private CalendarPanel _panel;
        private SettingsForm _settings;                 // C2：设置窗（非模态，单例）
        private DateTime _lastFetchDay = DateTime.MinValue; // B1：每日一次自动更新
        private NotifyIcon _icon;                       // N1：托盘图标（提醒气泡 + 备用菜单入口）
        private DateTime _reminderDay = DateTime.MinValue;
        private List<string> _fired = new List<string>(); // 已提醒过的 "日期|内容"
        private WinEventDelegate _hookProc;  // 保持引用防 GC 回收，否则钩子会失效
        private IntPtr _hookFore = IntPtr.Zero, _hookReorder = IntPtr.Zero;
        private IntPtr _trayHwnd = IntPtr.Zero;
        private readonly bool _openPanel;
        private readonly bool _openSettings;
        private readonly bool _openAgendaDemo;
        private readonly bool _openListDemo;
        private readonly bool _openPickerDemo;
        private readonly bool _openDayDetail;

        public ClockForm(bool openPanel, bool openSettings, bool openAgendaDemo, bool openListDemo, bool openPickerDemo, bool openDayDetail)
        {
            _openPanel = openPanel;
            _openSettings = openSettings;
            _openAgendaDemo = openAgendaDemo;
            _openListDemo = openListDemo;
            _openPickerDemo = openPickerDemo;
            _openDayDetail = openDayDetail;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;

            Settings.Load(); // 全局设置（settings.ini）
            BuildMenu();
            _tip = new ToolTip { InitialDelay = 400, ReshowDelay = 1000, ShowAlways = true };
            UpdateTime();

            // N1：托盘图标（提醒气泡、点击气泡打开面板、右键复用主菜单）
            _icon = new NotifyIcon();
            _icon.Icon = MakeAppIcon();
            _icon.Text = "中国时间 · 日历";
            _icon.Visible = true;
            _icon.ContextMenuStrip = ContextMenuStrip;
            _icon.BalloonTipClicked += delegate
            {
                if (_panel == null || _panel.IsDisposed || !_panel.Visible) TogglePanel();
            };

            _uiTimer.Interval = 200;
            _uiTimer.Tick += delegate { UpdateTime(); };
            _fitTimer.Interval = 4000;
            _fitTimer.Tick += delegate { FitToNativeClock(Rectangle.Empty); };
            _bgTimer.Interval = 15000;
            _bgTimer.Tick += delegate { SampleBackgroundAsync(); MaybeDailyFetch(); CheckReminders(); };
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
            HolidayFeed.LoadCache(); // 离线先用缓存
            MaybeDailyFetch();

            if (!TaskbarLayout.PretendDock.HasValue) // 调试模式（--pretend-dock）跳过 UIA，纯几何验证
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

            // 保层线程（ElevenClock 同款策略）：每 100ms 把窗口 show 出来（防任何原因
            // 导致的隐藏）并声明置顶。后台线程调度、UI 线程执行，不受消息风暴影响。
            _keepTopRunning = true;
            System.Threading.Thread t = new System.Threading.Thread(KeepTopLoop);
            t.IsBackground = true;
            t.Start();
        }

        private void KeepTopLoop()
        {
            while (_keepTopRunning)
            {
                System.Threading.Thread.Sleep(100);
                try
                {
                    if (!IsHandleCreated) continue;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (IsDisposed || Disposing || !IsHandleCreated) return;
                        if (!Visible) Show(); // 无论被什么原因隐藏，100ms 内拉回显示
                        AssertTopmost();
                    });
                }
                catch { }
            }
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
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_openPanel) TogglePanel();
            if (_openSettings) ShowSettings();
            if (_openAgendaDemo)
            {
                TogglePanel(); // 日程编辑窗视觉测试入口：面板 + 当天编辑器
                if (_panel != null)
                    _panel.OpenAgenda(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ChinaTz).Date);
            }
            if (_openListDemo)
            {
                TogglePanel(); // 近期日程列表视觉测试入口
                if (_panel != null) _panel.ToggleList();
            }
            if (_openPickerDemo)
            {
                TogglePanel(); // 年月选择器视觉测试入口
                if (_panel != null) _panel.OpenPickerDemo();
            }
            if (_openDayDetail)
            {
                TogglePanel(); // 日期详情卡片视觉测试入口（F1）
                if (_panel != null) _panel.OpenCard(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ChinaTz).Date);
            }
            CheckReminders(); // 启动即检查一次当天日程（带时间的到点后由 15s 定时器补发）
            SampleBackgroundAsync(); // 首次显示后立刻按"可见状态"重新采底色（不等 15s 定时器）
        }

        // N1：当天日程提醒。无时间的条目合并成一条气泡（启动/跨天首次检查时）；
        // 行首带 HH:mm 的条目到点单独提醒。跨天重置，重启同日会重发（当作提醒）
        private void CheckReminders()
        {
            try
            {
                DateTime now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ChinaTz);
                DateTime today = now.Date;
                if (today != _reminderDay) { _reminderDay = today; _fired.Clear(); }
                List<AgendaItem> items = AgendaStore.On(today);
                if (items.Count == 0) return;
                string hhmm = now.ToString("HH:mm");
                List<string> banner = new List<string>();
                foreach (AgendaItem it in items)
                {
                    string key = today.ToString("yyyyMMdd") + "|" + it.Text;
                    if (_fired.Contains(key)) continue;
                    if (it.Time != null)
                    {
                        if (string.Compare(hhmm, it.Time) < 0) continue; // 还没到点
                        _fired.Add(key);
                        ShowReminder("日程提醒", it.Text.Length > 180 ? it.Text.Substring(0, 180) : it.Text);
                    }
                    else
                    {
                        _fired.Add(key);
                        banner.Add(it.Text);
                    }
                }
                if (banner.Count > 0)
                {
                    string text = banner.Count <= 4 ? string.Join("\n", banner.ToArray())
                        : banner[0] + "\n" + banner[1] + "\n… 共 " + banner.Count + " 项";
                    ShowReminder("今日日程 " + banner.Count + " 项", text.Length > 200 ? text.Substring(0, 200) : text);
                }
            }
            catch { }
        }

        private void ShowReminder(string title, string text)
        {
            // 提醒历史（勿扰模式下横幅不弹，但通知中心/此日志可查）
            try
            {
                File.AppendAllText(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChinaClock", "reminder-log.txt"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + title + "] " + text.Replace("\n", " / ") + "\r\n");
            }
            catch { }
            try
            {
                _icon.BalloonTipTitle = title;
                _icon.BalloonTipIcon = ToolTipIcon.Info;
                _icon.BalloonTipText = text;
                _icon.ShowBalloonTip(6000);
            }
            catch { }
        }

        // N1：托盘图标——强调色圆点
        private static Icon MakeAppIcon()
        {
            using (Bitmap b = new Bitmap(32, 32))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush br = new SolidBrush(PanelPalette.Load().Accent))
                    g.FillEllipse(br, 2, 2, 28, 28);
                return Icon.FromHandle(b.GetHicon());
            }
        }

        // B1：每个自然日自动拉一次假日数据（后台线程，失败静默）
        private void MaybeDailyFetch()
        {
            if (!Settings.AutoFetchHolidays) return;
            DateTime today = DateTime.Now.Date;
            if (_lastFetchDay == today) return;
            _lastFetchDay = today;
            HolidayFeed.FetchAsync(null);
        }

        // C2：设置窗（非模态单例——模态会卡住 UI 线程上的秒级时钟刷新）
        private void ShowSettings()
        {
            if (_settings == null || _settings.IsDisposed)
            {
                _settings = new SettingsForm(this);
                _settings.Show();
            }
            else
            {
                _settings.Activate();
            }
        }

        // ---- 定位：找到系统时钟并覆盖 ----

        // r 为空时使用缓存位置或几何估算
        private void FitToNativeClock(Rectangle r)
        {
            EnsureTrayRect();
            if (TaskbarLayout.PretendDock.HasValue) r = GeometricGuess(); // 调试模式：纯几何定位，不用 UIA 实测
            else if (r.IsEmpty) r = _nativeRect.IsEmpty ? GeometricGuess() : _nativeRect;
            else _nativeRect = r;
            if (r.IsEmpty || _trayRect.IsEmpty) return;

            bool vertical = _dock == DockEdge.Left || _dock == DockEdge.Right;
            int maxW = vertical ? _trayRect.Width - 6 : int.MaxValue; // 侧边窄栏的可用宽度

            // F1：窄栏宽度自适应——放不下的行隐藏（时间行必须保留），仍超宽则缩字号
            _hideDate = _hideTz = false;
            _fontScale = 1f;
            RecalcFonts(r.Height + 4, 1f);
            Size s1 = TextRenderer.MeasureText(_timeText, _f1);
            while (s1.Width + 14 > maxW && _fontScale > 0.55f)
            {
                _fontScale = Math.Max(0.55f, _fontScale - 0.08f);
                RecalcFonts(r.Height + 4, _fontScale);
                s1 = TextRenderer.MeasureText(_timeText, _f1);
            }
            Size s2 = TextRenderer.MeasureText(_dateText, _f2);
            Size s4 = TextRenderer.MeasureText(_tzText, _f3);
            if (_dateText.Length > 0 && s2.Width + 14 > maxW) _hideDate = true;
            if (_tzText.Length > 0 && s4.Width + 14 > maxW) _hideTz = true;

            int w = Math.Max(r.Width + 10,
                Math.Max(Math.Max(s1.Width, _hideDate ? 0 : s2.Width), _hideTz ? 0 : s4.Width) + 16);
            int h = r.Height + 4;
            if (vertical)
            {
                // 侧边栏有多行叠放时按需加高（钳在任务栏内）
                int total = 0, lines = 0;
                if (s1.Height > 0) { total += s1.Height + 1; lines++; }
                if (_dateText.Length > 0 && !_hideDate) { total += s2.Height + 1; lines++; }
                if (_tzText.Length > 0 && !_hideTz) { total += s4.Height + 1; lines++; }
                if (lines > 0) total -= 1;
                h = Math.Max(h, Math.Min(total + 4, _trayRect.Height - 8));
            }

            var target = TaskbarLayout.FitTarget(r, _trayRect, _dock, w, h);
            if (Bounds != target) { Bounds = target; Invalidate(); }
            if (IsHandleCreated) AssertTopmost();
        }

        // 刷新任务栏矩形与停靠边（FindWindow/GetWindowRect 很快，可在 UI 线程调用；
        // explorer 重启、用户挪任务栏后 fitTimer 4 秒内自动跟上）
        private void EnsureTrayRect()
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return;
            _trayHwnd = tray;
            RECT tr;
            GetWindowRect(tray, out tr);
            _trayRect = Rectangle.FromLTRB(tr.Left, tr.Top, tr.Right, tr.Bottom);
            _dock = TaskbarLayout.PretendDock ?? TaskbarLayout.Detect(_trayRect, Screen.PrimaryScreen.Bounds);
        }

        // 仅在后台线程调用：用 UI Automation 在任务栏里找时钟元素（名字以 HH:mm 开头、位于托盘区）。
        // F1：过滤条件按停靠轴向泛化——水平停靠时钟贴任务栏右端，垂直停靠时钟贴底端。
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
                DockEdge dock = TaskbarLayout.PretendDock ??
                    TaskbarLayout.Detect(trayRect, Screen.PrimaryScreen.Bounds);
                bool vertical = dock == DockEdge.Left || dock == DockEdge.Right;

                AutomationElement root = AutomationElement.FromHandle(tray);
                if (root == null) return Rectangle.Empty;
                AutomationElementCollection els = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
                Rectangle best = Rectangle.Empty;
                int bestEnd = int.MinValue; // 水平停靠比右缘，垂直停靠比下缘
                foreach (AutomationElement el in els)
                {
                    try
                    {
                        string n = el.Current.Name;
                        if (string.IsNullOrEmpty(n) || !TimeNameRx.IsMatch(n)) continue;
                        System.Windows.Rect br = el.Current.BoundingRectangle;
                        if (br.IsEmpty) continue;
                        if (vertical)
                        {
                            if (br.Bottom < trayRect.Bottom - 320) continue;
                            if (br.Left < trayRect.Left - 4 || br.Right > trayRect.Right + 4) continue;
                            if (br.Bottom > bestEnd)
                            {
                                bestEnd = (int)br.Bottom;
                                best = Rectangle.Truncate(new RectangleF((float)br.X, (float)br.Y, (float)br.Width, (float)br.Height));
                            }
                        }
                        else
                        {
                            if (br.Right < trayRect.Right - 320) continue;
                            if (br.Top < trayRect.Top - 4 || br.Bottom > trayRect.Bottom + 4) continue;
                            if (br.Right > bestEnd)
                            {
                                bestEnd = (int)br.Right;
                                best = Rectangle.Truncate(new RectangleF((float)br.X, (float)br.Y, (float)br.Width, (float)br.Height));
                            }
                        }
                    }
                    catch { }
                }
                return best;
            }
            catch { return Rectangle.Empty; }
        }

        // UIA 不可用时的兜底：按任务栏几何位置估算时钟区域（F1：按停靠边分派）
        private Rectangle GeometricGuess()
        {
            if (_trayRect.IsEmpty) return Rectangle.Empty;
            return TaskbarLayout.GuessClockRect(_trayRect, _dock);
        }

        // ---- 外观 ----

        private void RecalcFonts(int h, float scale)
        {
            if (h == _fontH && scale == _fontScaleCache && _f1 != null) return;
            _fontH = h;
            _fontScaleCache = scale;
            if (_f1 != null) _f1.Dispose();
            if (_f2 != null) _f2.Dispose();
            if (_f3 != null) _f3.Dispose();
            // 窄栏缩字号（F1）：scale<1 时下限放宽到 7px，否则维持原下限
            int min1 = scale < 1f ? 7 : 10, min2 = scale < 1f ? 6 : 8, min3 = scale < 1f ? 5 : 7;
            _f1 = new Font("Segoe UI", Math.Max(min1, (int)Math.Round(h * 0.30f * scale)), FontStyle.Regular, GraphicsUnit.Pixel);
            _f2 = new Font("Segoe UI", Math.Max(min2, (int)Math.Round(h * 0.22f * scale)), FontStyle.Regular, GraphicsUnit.Pixel);
            _f3 = new Font("Segoe UI", Math.Max(min3, (int)Math.Round(h * 0.17f * scale)), FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // 任务栏底色采样：在后台线程用 CopyFromScreen 抓取任务栏像素条，取"均匀条"
        // （条内极差小 = 无图标/边框/窗口干扰）的平均色作为背景。
        // 两条铁律：
        //  1. 优先在紧贴任务栏上/下边缘的细条采样——托盘图标都画在垂直居中一带，
        //     贴边细条永远没有图标；旧版在垂直中线采样，图标密集时众数被带偏成
        //     深色，覆盖窗就"变黑"了。
        //  2. 干扰判定用"条内极差"，不能用"量化众数占比"：任务栏是 Mica 半透明
        //     材质，颜色随壁纸横向缓慢渐变，8 级量化会把渐变切进相邻桶、众数占比
        //     被稀释到阈值以下（实测只剩 52%），导致所有候选被误判失败、退回注册
        //     表兜底色，时钟底色变成刺眼的纯白。极差判定对渐变免疫。
        // 全部失败时退回注册表系统主题对应的任务栏标准色。
        // 注意：绝不能在 UI 线程逐点 GetPixel，会卡住界面。
        private void SampleBackgroundAsync()
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return;
            RECT tr;
            GetWindowRect(tray, out tr);
            bool vertical = TaskbarLayout.PretendDock.HasValue
                ? TaskbarLayout.PretendDock.Value == DockEdge.Left || TaskbarLayout.PretendDock.Value == DockEdge.Right
                : tr.Right - tr.Left < tr.Bottom - tr.Top;
            int trShort = vertical ? tr.Right - tr.Left : tr.Bottom - tr.Top;
            if (trShort < 20) return;
            // 任务栏自动隐藏收起后坐标已移出屏幕，采样会抓到无关窗口（如深色终端），直接跳过
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            if (tr.Top >= screen.Bottom || tr.Bottom <= screen.Top || tr.Left >= screen.Right || tr.Right <= screen.Left) return;
            bool vis = IsHandleCreated && Visible;
            int wl = vis ? Bounds.Left : 0, wrt = vis ? Bounds.Right : 0;
            int wt = vis ? Bounds.Top : 0, wb = vis ? Bounds.Bottom : 0;

            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                Action<Color> apply = delegate(Color c)
                {
                    if (IsHandleCreated)
                        BeginInvoke((MethodInvoker)delegate { ApplyBackground(c); });
                };

                Color col;
                if (!vertical)
                {
                    // 水平停靠（顶/底）：贴上/下边缘横条 + 沿中线从时钟往左横扫（原逻辑）
                    int off = Math.Max(5, Math.Min(10, trShort / 8)); // 贴边 5~10px：避开顶/底边框线与图标带
                    int yb = tr.Bottom - off, yt = tr.Top + off, ym = tr.Top + (tr.Bottom - tr.Top) / 2;
                    int[][] xsNear =
                    {
                        new[] { wl - 90, wl - 6 },       // 本窗口左侧附近（Mica 横向渐变时这里最接近）
                        new[] { wl - 180, wl - 90 },     // 稍远一点的左侧
                        new[] { wrt + 4, tr.Right - 8 }, // 右侧缝隙（避开最右"显示桌面"细条）
                    };
                    // 1) 上/下贴边细条（永远没有图标，最可靠）
                    foreach (int y in new[] { yb, yt })
                    {
                        if (y <= screen.Top || y >= screen.Bottom) continue;
                        foreach (int[] s in xsNear)
                        {
                            int x0 = Math.Max(tr.Left + 2, s[0]);
                            int x1 = Math.Min(tr.Right - 8, s[1]);
                            if (x1 - x0 + 1 < 30) continue;
                            if (TrySampleStrip(x0, y, x1 - x0 + 1, 1, out col)) { apply(col); return; }
                        }
                    }
                    // 2) 垂直中线从时钟往左按 48px 步进扫描（同一水平线的 Mica 色最一致；
                    //    有图标/按钮的条极差巨大自动跳过，扫到第一个均匀条为止）
                    if (ym > screen.Top && ym < screen.Bottom)
                    {
                        for (int x1 = Math.Min(wl - 6, tr.Right - 8); x1 > tr.Left + 60; x1 -= 48)
                        {
                            int x0 = Math.Max(tr.Left + 2, x1 - 48);
                            if (TrySampleStrip(x0, ym, x1 - x0 + 1, 1, out col)) { apply(col); return; }
                        }
                    }
                }
                else
                {
                    // 垂直停靠（左/右，F1）：贴左/右边缘竖条 + 沿水平中线从时钟往上竖扫，
                    // 与水平逻辑轴对称——图标带都在截面的中部，贴边条永远没有图标
                    int off = Math.Max(5, Math.Min(10, trShort / 8));
                    int xr = tr.Right - off, xl = tr.Left + off, xm = tr.Left + (tr.Right - tr.Left) / 2;
                    int[][] ysNear =
                    {
                        new[] { wt - 90, wt - 6 },       // 本窗口上方附近（Mica 纵向渐变时这里最接近）
                        new[] { wt - 180, wt - 90 },     // 稍远一点的上方
                        new[] { wb + 4, tr.Bottom - 8 }, // 下方缝隙
                    };
                    foreach (int x in new[] { xl, xr })
                    {
                        if (x <= screen.Left || x >= screen.Right) continue;
                        foreach (int[] s in ysNear)
                        {
                            int y0 = Math.Max(tr.Top + 2, s[0]);
                            int y1 = Math.Min(tr.Bottom - 8, s[1]);
                            if (y1 - y0 + 1 < 30) continue;
                            if (TrySampleStrip(x, y0, 1, y1 - y0 + 1, out col)) { apply(col); return; }
                        }
                    }
                    if (xm > screen.Left && xm < screen.Right)
                    {
                        for (int y1 = Math.Min(wt - 6, tr.Bottom - 8); y1 > tr.Top + 60; y1 -= 48)
                        {
                            int y0 = Math.Max(tr.Top + 2, y1 - 48);
                            if (TrySampleStrip(xm, y0, 1, y1 - y0 + 1, out col)) { apply(col); return; }
                        }
                    }
                }
                // 3) 采样全部失败（如截屏被策略阻挡）时按系统主题取标准任务栏色
                apply(ThemeBaseColor());
            });
        }

        // 读注册表系统主题（SystemUsesLightTheme），返回对应的任务栏标准底色（采样失败时兜底）
        private static Color ThemeBaseColor()
        {
            bool light = true;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k != null ? k.GetValue("SystemUsesLightTheme") : null;
                    if (v is int) light = (int)v == 1;
                }
            }
            catch { }
            return light ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32);
        }

        // 采样一条 w×h 像素细条（水平停靠 w>1,h=1；垂直停靠 w=1,h>1）：条内颜色
        // 极差 ≤20 视为"均匀"（没有图标/边框线/窗口边缘等干扰），输出条内平均色。
        // Mica 渐变在几十 px 内变化只有几个色阶，极差判定完全免疫；
        // 而穿过图标的条极差动辄 200+，必然被拒。
        private static bool TrySampleStrip(int x0, int y0, int w, int h, out Color col)
        {
            col = Color.Empty;
            try
            {
                int minR = 255, maxR = 0, minG = 255, maxG = 0, minB = 255, maxB = 0;
                long sumR = 0, sumG = 0, sumB = 0;
                int n = w * h;
                using (var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                        g.CopyFromScreen(x0, y0, 0, 0, new Size(w, h));
                    var bd = bmp.LockBits(new Rectangle(0, 0, w, h),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    var buf = new int[n];
                    Marshal.Copy(bd.Scan0, buf, 0, n);
                    bmp.UnlockBits(bd);
                    foreach (int px in buf)
                    {
                        int r = (px >> 16) & 255, gg = (px >> 8) & 255, b = px & 255;
                        if (r < minR) minR = r;
                        if (r > maxR) maxR = r;
                        if (gg < minG) minG = gg;
                        if (gg > maxG) maxG = gg;
                        if (b < minB) minB = b;
                        if (b > maxB) maxB = b;
                        sumR += r; sumG += gg; sumB += b;
                    }
                }
                int range = Math.Max(maxR - minR, Math.Max(maxG - minG, maxB - minB));
                if (range > 20) return false; // 条内有干扰，不可信
                col = Color.FromArgb((int)(sumR / n), (int)(sumG / n), (int)(sumB / n));
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

            // A1：时间行格式——自定义格式串 > 秒/星期预设
            string tf = Settings.CustomTimeFormat;
            if (tf == null || tf.Length == 0) tf = Settings.ShowSeconds ? "HH:mm:ss" : "HH:mm";
            string t;
            try { t = now.ToString(tf); }
            catch { t = now.ToString("HH:mm:ss"); }
            if (Settings.ShowWeekday) t += "  " + Weekdays[(int)now.DayOfWeek];
            _timeText = t;
            _dateText = Settings.ShowDateLine ? now.ToString(_datePattern) : "";
            // F2：本机时间不上任务栏（字号太小看不清）——悬停提示与日历面板展示
            _tzText = Settings.ShowSecondClock ? SecondClockText() : "";
            UpdateTip(now);
            Invalidate();
        }

        // A3：第二时区行 "标签 HH:mm"
        private string SecondClockText()
        {
            try
            {
                if (_tz2 == null || _tz2Id != Settings.SecondTzId)
                {
                    _tz2 = TimeZoneInfo.FindSystemTimeZoneById(Settings.SecondTzId);
                    _tz2Id = Settings.SecondTzId;
                }
                string label = Settings.SecondTzLabel.Length > 0 ? Settings.SecondTzLabel : Settings.SecondTzId;
                return label + " " + TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz2).ToString("HH:mm");
            }
            catch { return Settings.SecondTzLabel + " --:--"; }
        }

        // A2：悬停时钟气泡 = 完整日期 + 农历干支 + 今日节日/节气 + 假期倒计时；
        // 内容只按分钟变化刷新，避免每秒重置气泡
        private void UpdateTip(DateTime now)
        {
            string text = BuildTipText(now);
            if (text == _lastTipText) return;
            _lastTipText = text;
            _tip.SetToolTip(this, text);
        }

        private string BuildTipText(DateTime now)
        {
            CalendarData.DayInfo di = CalendarData.Get(now);
            List<string> lines = new List<string>();
            lines.Add(now.ToString("yyyy年M月d日 ") + Weekdays[(int)now.DayOfWeek]);
            string info = "农历 " + di.LunarFullText + " · " + di.GanZhiYear + di.Animal + "年 · "
                + di.GanZhiMonth + "月 · " + di.GanZhiDay + "日";
            if (di.Badge != null) info += "　" + di.Badge;
            lines.Add(info);
            if (di.Festival != null) lines.Add("今天 " + di.Festival);
            else if (di.TermName != null) lines.Add("今天 " + di.TermName);
            DateTime hd; string hn;
            if (CalendarData.NextFestival(now.Date, out hd, out hn))
            {
                int cd = (int)(hd - now.Date).TotalDays;
                lines.Add(cd == 0 ? "今天 " + hn : "距 " + hn + " 还有 " + cd + " 天");
            }
            if (LocalClock.Enabled) lines.Add(LocalClock.PanelText); // F2：本机时间
            return string.Join("\n", lines.ToArray());
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Color c1 = _lightTaskbar ? Color.FromArgb(26, 26, 26) : Color.FromArgb(242, 242, 242);
            Color c2 = _lightTaskbar ? Color.FromArgb(96, 96, 96) : Color.FromArgb(197, 197, 197);

            // 行集合按设置动态组装（A1/A3）：时间行 + 日期行 + 第二时区行，
            // 整体垂直居中；侧边窄栏放不下的行已由 FitToNativeClock 置隐藏标记（F1）。
            // 本机时间不上任务栏（F2）——字号太小看不清，悬停提示/日历面板展示。
            List<Line> ls = new List<Line>();
            if (_timeText.Length > 0) ls.Add(new Line(_timeText, _f1, c1));
            if (_dateText.Length > 0 && !_hideDate) ls.Add(new Line(_dateText, _f2, c2));
            if (_tzText.Length > 0 && !_hideTz) ls.Add(new Line(_tzText, _f3, c2));

            int total = 0;
            foreach (Line ln in ls) total += TextRenderer.MeasureText(ln.Text, ln.Font).Height + 1;
            if (ls.Count > 0) total -= 1;
            int padX = 7;
            int y = Math.Max(1, (ClientSize.Height - total) / 2);
            foreach (Line ln in ls)
            {
                Size sz = TextRenderer.MeasureText(ln.Text, ln.Font);
                TextRenderer.DrawText(g, ln.Text, ln.Font,
                    new Rectangle(padX, y, ClientSize.Width - padX - 2, sz.Height), ln.Color,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine);
                y += sz.Height + 1;
            }

            base.OnPaint(e);
        }

        private struct Line
        {
            public string Text; public Font Font; public Color Color;
            public Line(string t, Font f, Color c) { Text = t; Font = f; Color = c; }
        }

        // 设置窗保存后调用：重置缓存并立即刷新
        public void RefreshSettings()
        {
            _tz2 = null; _tz2Id = "";
            _lastStamp = ""; // 强制 UpdateTime 重算文本
            UpdateTime();
            Invalidate();
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
            // 时钟本体是"不抢焦点"窗口（WS_EX_NOACTIVATE），点它不会改变前台焦点，
            // 面板的 Activate 可能被系统拒绝 → 面板开着但没有键盘焦点，滚轮翻月/Esc
            // 全部失效。这里显式把前台焦点交给面板（与系统日历 Win+N 弹出行为一致）。
            SetForegroundWindow(_panel.Handle);
        }

        private ToolStripMenuItem _miAutoStart;

        private void BuildMenu()
        {
            var miSettings = new ToolStripMenuItem("设置…");
            miSettings.Click += delegate { ShowSettings(); };

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
            menu.Items.Add(miSettings);
            menu.Items.Add(new ToolStripSeparator());
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

        internal static bool IsAutoStart()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }

        internal static void SetAutoStart(bool on)
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
            _keepTopRunning = false;
            if (_hookFore != IntPtr.Zero) UnhookWinEvent(_hookFore);
            if (_hookReorder != IntPtr.Zero) UnhookWinEvent(_hookReorder);
            _hookProc = null;
            if (_panel != null && !_panel.IsDisposed) _panel.Close();
            if (_tip != null) _tip.Dispose();
            if (_icon != null) { _icon.Visible = false; _icon.Dispose(); }
            if (_f1 != null) _f1.Dispose();
            if (_f2 != null) _f2.Dispose();
            if (_f3 != null) _f3.Dispose();
            base.OnFormClosed(e);
        }
    }
}
