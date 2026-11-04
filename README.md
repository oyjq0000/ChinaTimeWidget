# 中国时间 · 任务栏时钟 + 日历面板 (ChinaTimeWidget)

**A tiny Windows taskbar clock replacement that always shows China Standard Time (UTC+8), with a Chinese lunar calendar flyout** — 农历、节气、节日、干支、法定假日调休、日程备忘与提醒。

把 Windows 11 任务栏右下角的系统时钟替换成**中国标准时间（北京时间 UTC+8）**，点击弹出月历面板，
单击日期还可查看**当日详情卡片**（农历/节日/假期/日程/黄历）。
任务栏停靠在 下/左/顶/右 均自动适配；本机时区≠北京时间时，悬停提示与日历面板顶部自动附显**本地时间**。
单文件 C# / WinForms，零第三方依赖，**不修改系统时区、不改任何系统设置**，退出程序立即恢复原样。

## 截图

| | |
|---|---|
| ![任务栏时钟](docs/clock.png) | ![浅色面板](docs/panel-light.png) |
| 任务栏时钟：秒级中国时间 | 日历面板：农历/节气/节日/干支/休班角标 |
| ![深色面板](docs/panel-dark.png) | ![年月选择器](docs/picker.png) |
| 深色模式跟随系统 | 标题栏下钻：月历 → 选月 → 十年选年 |
| ![近期日程](docs/agenda-list.png) | ![日程编辑器](docs/agenda-editor.png) |
| 近期日程：未来 14 天清单 | 双击日期编辑日程（支持每年/农历重复） |
| ![日期详情卡片](docs/day-detail.png) | |
| 单击日期：旁边弹出当日详情卡片（含黄历） | |

## 功能

### 任务栏时钟（替代原系统时钟）
- 时间行格式可配置：秒/星期/日期行显隐，支持自定义 DateTime 格式串
- **任务栏四边适配**：停靠在 下/左/顶/右 自动检测（Win10 原生四边；Win11 需 ExplorerPatcher 等），定位/找钟/底色采样/面板锚点全部跟随
- **本地时间**：电脑时区≠北京时间时，悬停时钟提示与日历面板顶部（大时间右侧）自动显示本地时间（可在设置关闭；相同时区自动隐藏防重复；不上任务栏——字号太小看不清）
- **第二时区双时钟**：时钟下追加一行其他时区（UTC/东京/纽约等，标签可改）
- **悬停提示**：完整日期 + 农历 + 干支 + 今日节日/节气 + 距最近假期倒计时
- 背景色自动适配任务栏（深色/浅色主题、Mica 材质采样）；explorer 重启自动对位；点击不抢焦点

### 日历面板（左键单击时钟打开/关闭）
- 农历、节气、节日、干支纪年月日；今天强调色圆圈；周数列（可选）
- **当日详情卡片**（优效日历式）：单击某天，面板旁边弹出独立圆角卡片——大字日期、节日/节气、休/班原因、假期区间（"第X天/共Y天"）或倒计时、当天日程（可一键编辑）、
  **黄历**（宜/忌·十二建星、冲煞、纳音五行、值神黄道/黑道）、年度进度（第几周/第几天/百分比）；卡片上 `‹` `›` 逐日翻看，再点同天/Esc/× 收起
- **年月选择器**：点标题逐级下钻（月历 → 选月 → 十年选年），`‹` `›`/滚轮按当前层级翻页，
  点空白/Esc 收起——与 Windows 自带日历 / WinUI CalendarView / v-calendar 同款交互
- **日程备忘**：详情卡片上"编辑日程…"按钮编辑（日期格不响应双击，连点浏览不误弹编辑窗），
  格底圆点标记，悬停显示清单；支持一次性、`每年 MM-DD`（公历每年）、`农历 MM-DD`（农历每年）三种格式
- **近期日程**：底部按钮打开未来 14 天清单，点条目跳转那天
- **日程提醒**：当日日程弹托盘气泡；内容开头带 `HH:mm ` 的到点单独提醒（按中国时间）
- **休/班角标**：法定假日与周末标"休"、调休补班标"班"；悬停显示节日名
- 深色模式跟随系统（可强制），强调色自定义；面板各视图均支持失焦回退

### 假日数据
- 角标优先级：**手动配置 > 在线数据（自动更新）> 内置规则**，都没有时周末标"休"
- 自动接入 [holiday-cn](https://github.com/NateScarlet/holiday-cn)（依据国务院公告的开源数据集）：
  每日拉取当年+次年数据（jsDelivr/raw 多镜像），公布次年安排后**无需更新程序**角标自动跟上
- 内置 13 天法定假日兜底；`holidays.txt` 手动配置优先级最高

## 安装与使用

要求：Windows 10/11，.NET Framework 4.8（系统自带），无需管理员权限。

1. 从 [Releases](https://github.com/oyjq0000/ChinaTimeWidget/releases) 下载 `TaskbarChinaClock.exe`，或按下方命令自行编译
2. 双击运行（单实例），时钟即刻覆盖系统时钟；左键单击打开日历面板
3. 右键时钟 →「设置…」调整全部选项；「开机自启」可一键注册（HKCU Run）

| 操作 | 功能 |
|---|---|
| 左键单击时钟 | 打开/关闭日历面板 |
| 悬停时钟 | 农历/节日/假期倒计时/本机时间提示 |
| 单击面板某天 | 打开/收起当日详情卡片（连点浏览时卡片稳定跟随；卡片 `‹` `›` 逐日翻看，Esc 逐级收起） |
| 点击面板标题（年月） | 逐级切换 月历 → 选月 → 选年 |
| 卡片"编辑日程…"按钮 | 编辑当日日程备忘 |
| 面板底部"近期日程" | 未来 14 天日程清单 |
| 右键 → 设置… | 打开设置窗口 |
| 右键 → 退出（恢复系统时钟） | 移除覆盖，原生时钟即刻回来 |

## 配置文件（均在 `%APPDATA%\ChinaClock\`）

| 文件 | 说明 |
|---|---|
| `settings.ini` | 全部设置（时钟格式/第二时区/本地时间/周数/主题/强调色等），由设置窗口维护 |
| `holidays.txt` | 手动休/班配置，**优先级最高**：`休 2026-10-01 2026-10-08`、`班 2026-09-27` |
| `agenda.txt` | 日程：`日期\|内容` / `每年 MM-DD\|内容` / `农历 MM-DD\|内容`，内容开头可带 `HH:mm` |
| `holidays-cache.txt` | holiday-cn 在线数据缓存（自动维护） |
| `reminder-log.txt` | 日程提醒历史 |

## 构建与自测

零第三方依赖，使用 Windows 自带 .NET Framework 编译：

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe ^
  -out:TaskbarChinaClock.exe -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll ^
  -r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll ^
  -r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll ^
  -r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll ^
  TaskbarChinaClock.cs
```

内置 59 项历法/黄历/停靠几何自测：`TaskbarChinaClock.exe --selftest`（结果写入 `selftest-result.txt`）。
另有无鼠标视觉验证入口：`--show-panel` / `--settings` / `--agenda-demo` / `--list-demo` / `--picker-demo` / `--day-detail`（日期详情卡片），
以及调试入口 `--pretend-dock:left|top|right`（强制按指定停靠边定位，无需真的挪任务栏）。

## 数据来源与致谢

- [holiday-cn](https://github.com/NateScarlet/holiday-cn) — 法定假日/调休数据集（MIT）
- 农历：.NET 内置 `ChineseLunisolarCalendar`（1901–2100，天文级准确）；节气：寿星通用公式
- 黄历（宜忌/冲煞/纳音/值神）：由干支纯算法推算的通行简明口径，供日常参考，非权威择日
- 交互设计参考：[优效日历](https://www.youxiaocalendar.com/)（日期详情卡片）、Windows 自带日历 / WinUI CalendarView、[v-calendar](https://github.com/nathanreyes/v-calendar)、[ElevenClock](https://github.com/marticliment/ElevenClock)

## 已知限制

- 系统日历面板（Win+N）、锁屏界面等处的时间无法替换，仍为本地时间
- 仅覆盖主显示器任务栏；任务栏停靠于 下/左/顶/右 均自动适配（Win11 原生仅支持底部，
  其余停靠需 ExplorerPatcher/StartAllBack 等工具）；`--pretend-dock` 为近似模拟
- 与优效日历等"任务栏时钟增强"类软件共存会互相干扰
- 独占全屏（部分游戏）可能暂时盖住时钟；任务栏自动隐藏收起时时钟悬浮在原位置
- 农历数据支持 1901–2100 年；节气按寿星通用公式，个别年份可能偏差一天
- 勿扰模式开启时提醒横幅不弹（仍会进通知中心，并记录到 `reminder-log.txt`）

## License

[MIT](LICENSE)
