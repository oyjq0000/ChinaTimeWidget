# 中国时间 · 任务栏时钟接管 (TaskbarChinaClock)

让 Windows 11 任务栏右下角的时钟显示**中国标准时间（北京时间 UTC+8）**。
**不修改系统时区，不改任何系统设置**，退出程序立即恢复原样。

## 原理

Windows 不开放任务栏时钟的渲染内容（由 explorer.exe 绘制，无法注入），
所以程序用外观一致的置顶小窗精确覆盖在系统时钟上方：

- 位置：先按任务栏几何位置立即定位，再用 UI Automation（后台线程）精确定位系统时钟元素微调；
- 背景：沿任务栏中线取一排像素做众数采样，纯色背景必占多数，
  深色/浅色主题、Mica 材质自动适配（实测色差 ≤2 个色阶，肉眼不可见）；
- 文字：第一行 `HH:mm:ss 星期X`（中国时间），第二行日期沿用系统短日期格式（但为中国日期）；
- explorer 重启、分辨率变化后 4 秒内自动重新定位。

## 使用

双击 `TaskbarChinaClock.exe` 即可（单实例）。

| 操作 | 功能 |
|---|---|
| 左键单击时钟 | 打开系统日历面板（等同 Win+N，那里仍显示本地时间） |
| 右键 → 重新校准位置 | 立即重新对位 |
| 右键 → 开机自启 | HKCU Run 注册表方式，无需管理员 |
| 右键 → 退出（恢复系统时钟） | 移除覆盖，原生时钟即刻回来 |

Win+D、切换全屏程序都不会让时钟消失；点击不抢焦点。

## 说明与限制

- 系统日历面板、锁屏界面等处的时间无法替换，仍为本地时间；
- 若安装了其它"任务栏时钟增强"类软件（如某些带任务栏时钟的日历程序），
  可能出现两层时间叠加，请在对方设置里关掉它的任务栏时钟显示；
- 独占全屏（部分游戏）可能暂时盖住时钟；
- 任务栏为自动隐藏且处于隐藏状态时，时钟会悬浮在原位置。

## 编译（零依赖，Windows 自带 .NET Framework）

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe -nologo -target:winexe ^
  -out:TaskbarChinaClock.exe -r:System.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll ^
  -r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll ^
  -r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll ^
  -r:C:\Windows\Microsoft.NET\assembly\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll ^
  TaskbarChinaClock.cs
```

## 文件

- `TaskbarChinaClock.cs` / `TaskbarChinaClock.exe` — 任务栏时钟接管版（推荐）
- `ChinaTimeWidget.cs` — 早期悬浮卡片版源码（备用；用类似命令把引用减为 System/System.Drawing/System.Windows.Forms 即可编译）
- `shot.ps1` — 辅助：截取屏幕右下角，用于验证显示效果
