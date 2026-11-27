# 外设电量

## 直接运行

不需要安装 .NET 8 SDK，也不需要安装 .NET 运行时。

1. 打开 [v0.6.0 Release](https://github.com/62909/Peripheral-battery-display/releases/tag/v0.6.0)，下载 [PeripheralBattery.exe](https://github.com/62909/Peripheral-battery-display/releases/download/v0.6.0/PeripheralBattery.exe)。
2. 这一版是单个文件，不用解压。如果下载到的是 zip，先解压再运行里面的程序。
3. 双击运行 `PeripheralBattery.exe`。

需要 Windows 10 或 Windows 11 的 64 位系统。程序出现在托盘里，没有控制台窗口。

Windows 10/11 x64 上的托盘小工具。它列出已连接的键盘、鼠标、耳机和其他蓝牙 / HID 外设，并显示系统能读到的电量。

整个界面是简体中文。读不到名称或电量时显示 **未知**，不会编一个百分比或型号。

## 界面

托盘里只有一个图标。图标上的数字是当前**已经读到**的电量里最低的一档。一台设备都没有读数时，图标画成「?」：32 像素放不下「未知」两个字。鼠标悬停和设备列表里仍然写「未知」。

- 左键打开设备列表：名称、类型（键盘 / 鼠标 / 耳机 / 其他 / 未知）、电量百分比或未知。
- 右键菜单：刷新、打开列表、放到任务栏、调整位置、完成放置、从任务栏移除、开机自动启动、演示模式、退出。

开机自动启动默认关闭。打开后写入当前用户的 `Run` 注册表项，关掉就删掉这一项。

## 放到任务栏

电量块可以拖到任务栏的空白处，并且会跟着任务栏走。默认不放上去，需要自己从菜单里选「放到任务栏」。

电量块把当前设备列表里的每一台并排画出来，顺序和列表相同。每一台平常是设备轮廓加上百分比，不再画电池轮廓。这一台自己的读数带了充电标志时，百分比后面再加一个闪电；没有标志，或者几个来源的标志对不上，就不画。设备之间留出一段比图标和数字之间更宽的间隔；只有一台时，末尾不另加这段间隔。轮廓跟着类型：鼠标、键盘或耳机。未知或其他画通用设备标记。颜色仍按这一台的电量分档（绿、琥珀、红、灰），数字和轮廓用同一种颜色。读不到电量就写「未知」，不编百分比。列表还是空的时候，电量块画一个通用标记和「未知」，方便先放到任务栏上。

设备变多时电量块变宽，并按任务栏 DPI 缩放，仍然可以拖到任务栏上。宽到任务栏长边放不下时，整条一起缩小，每一台都还在，不隐藏，也不做滚动。左边或右边的任务栏会把这一条转 90 度，顺着任务栏排。托盘图标不变，仍然只画已经读到的电量里最低的数字。

做法：

1. 右键托盘图标，选「放到任务栏」。电量块出现在任务栏里，边框变蓝，可以拖动。
2. 把它拖到空白处。松手后位置会记下。
3. 右键选「完成放置」。之后左键电量块会打开设备列表，不会再跟着鼠标跑。
4. 想再挪一次，选「调整位置」。不想要这块显示，选「从任务栏移除」。去掉之后再放，会回到默认的偏右位置（大约在长边的 82% 处），可以再拖。

记下的不是屏幕像素，而是任务栏长边上的比例：横条从左到右，竖条从上到下。任务栏改到另一边、换显示器、变高或者 DPI 变了，都会按**当时**的任务栏矩形重算位置。

主任务栏用公开的 `SHAppBarMessage(ABM_GETTASKBARPOS)` 取矩形。程序同时用 `ABM_NEW` 接收 `ABN_POSCHANGED`、`ABN_STATECHANGE`、`ABN_FULLSCREENAPP`，但**不**调用 `ABM_SETPOS`，所以不会另外占一条屏幕边缘，也不会把工作区挤窄。另外每秒再读一次位置，避免有的系统不发通知。

副显示器上的任务栏，这个 API 只返回主任务栏。副屏改为只读 `Shell_SecondaryTrayWnd` 的窗口矩形，不往 Explorer 里注入代码。

这个窗口是盖在任务栏上的，不会把开始菜单或图标挤开。

使用时会碰到这些限制：

- 放在图标上面会挡住图标，需要自己拖到空白处。Windows 11 居中任务栏时，默认位置多半落在右侧空白，图标很多时仍可能重叠。
- 自动隐藏的任务栏收起后，电量块一起隐藏；任务栏再出现时它再出现。
- 全屏应用在前台时电量块隐藏，避免盖住画面。独占全屏时，普通窗口本来也画不到游戏上面。
- 副屏任务栏没有单独的“完整槽位”接口。自动隐藏收起时只能看到一条很窄的窗口，这段时间先隐藏电量块。
- Windows 11 的任务按钮不是独立窗口，电量块不会跟着图标滚动。
- 不使用 DeskBand。Windows 11 的任务栏已经不加载这种扩展。

## 能读到什么

刷新时几路一起读，同一台设备会合并。电量优先级是：厂商读取器、蓝牙 GATT、标准 HID 电量页、Windows 已经记下的蓝牙电量属性。更高优先级有读数就用它；它没有，才用下一级。同一优先级两个读数对不上，显示未知。

1. **蓝牙 GATT Battery Service（0x180F）** 的 Battery Level（0x2A19）。规范是一个字节，只接受 0–100。设备需要已连接。
2. **HID Battery System 页（0x85）**，来自 USB HID Power Device 规范：
   - `RelativeStateOfCharge`（0x64）的单位就是百分比。
   - `AbsoluteStateOfCharge`（0x65）也是百分比。大于 100 时不截成 100，这一项作废。
   - `RemainingCapacity`（0x66）只在 `CapacityMode` 标明百分比，或者描述符的逻辑范围正好是 0–100 时采用。毫安时、毫瓦时不会被换算成百分比。
   - 厂商自定义页（0xFF00–0xFFFF）不解释。
3. **Windows 写在蓝牙设备节点上的电量字节** `{104EA319-6EE2-4701-BD47-8DDBF425BBE5}, 2`。头戴耳机通过免提协议上报后，系统经常把电量写在这里；一部分 BLE 设备也有。这个属性不在公开 SDK 文档里。没有这个属性、类型不是字节、或数值超出 0–100，都显示未知。同一地址的多个节点里如果同时出现 0 和一个正数，采用那个正数，因为这个属性常用 0 表示“没有上报”。两个不同的正数则显示未知。

名称按这个顺序决定：手动覆盖、Windows 给出的有意义名称、核对过的厂商+产品 ID、仅厂商 ID 时的品牌。通用名（例如 `HID-compliant mouse`）、纯 MAC 地址、蓝牙无线电，都不当成型号。类型可以来自 HID 用法、蓝牙设备类、名称里的关键词。判断不了就是未知。

有标准电量页的 HID 会列入，包括少数走标准页的接收器。没有电量页的普通有线键鼠不列入，避免把笔记本内置键盘算进来。蓝牙 HID 的键盘、鼠标、手柄集合会列入，电量读不到就是未知。手机、电脑、网络接入点类蓝牙设备不列入。

## 厂商协议

`38EE:0022` 走 hub.akkogear.com 的华兮页面（`infortech-sdk.esm.js`）。只打开厂商集合 `FFA8:0002`（这台机器上是 `MI_02` `Col03`）。标准鼠标集合 `0001:0002`（`MI_00`）不查询。报告号是 11，命令是 `DevGetInfo`（129，负载 `81 00`）。应答第 0 字节必须是 129，第 2 字节必须是 0，然后丢掉前 3 字节，第 14 字节才是电量。只有 0–100 显示为百分比，不把更大的数截成 100。充电标志和充满标志不拿来当百分比。这个 USB ID 不写成型号；名称用 HID 产品字符串，没有就写未知。

ATK 走官方网页驱动 [hub.atk.pro](https://hub.atk.pro/)（对照的是 2026-11-25 的 hub-v3 **3.2.34**，脚本在 `bpcdn.atkgear.com`）。型号不共用一套电量协议，所以按官方过滤器分开读。名称仍用 HID 产品字符串，不用驱动里的型号表；没有字符串就写未知。大于 100 的电量不截成 100。

- **BITMOUSE**（`filter-CWFKFZdv.js`）：`373B`，用法页 `FF05` 用法 `0001`，报告号 8，63 字节。先发 `GetDeviceType`（26）。类型 0、1、4、5 是接收器，电量改向 target 1；2、3、6、7 是有线，target 保持 0。然后是 `GetBatteryLevel`（7）和 `GetBatteryChargingStatus`（15）。成功应答的状态字节是 0，电量是数据区第 0 字节，充电标志 1 才算在充。官方列表里走这一套的 ATK / VXE 鼠标和 2.4G 接收器都用它，包括一部分 A9 NK、ZERO、F1 V3、X1 Ultimate。
- **COMPX**（`filter-DZcjCGve.js`）：`373B`，用法页 `FF04` 用法 `0002`，报告号 8，16 字节。命令是 `GetBatteryLevel`（4）。校验是 `0x55` 减去（报告号 8 加前 15 字节）的低 8 位。`commandStatus` 必须是 0。电量在第 5 字节，充电字节在第 6 字节。
- **HECHI**（`useDevice` 里的 `getMouseInfo`）：`373B`，用法页 `FFA0` 用法 `0002`，报告号 11，63 字节，命令 19。电量在偏移 17。官方读取函数会把大于 100 的数改成 100，这里不改。这是 A9 Pro Max 那一支。
- **HFD 键盘**（控制器 `ts`，产品 ID `4382`、`4383`、`4657`、`4429`、`4430`、`4433`、`4434`）：`373B`，用法页 `FF60` 用法 `0061`，报告号 0，32 字节。命令是 `2 150 36`，后面依次是电量字节和充电字节。充电字节为 0 时，电量字节才当作百分比；不是 0 时官方驱动不给出百分比，这里也不编。

BITYUAN 键盘（RS6、RS7、ATK68、ATK75、EDGE 等）用法页也是 `FF60:0061`，但官方 `getPowerInfo` 是空的，不会拿 HFD 的命令去套。HOLTEK 鼠标（用法页 `FF10`，例如一部分 X1、U2 Pro、A9 Pro）的 `getPowerInfo` 在这份驱动里没有实现，也不读。

## 读不到什么

- 罗技 HID++、雷蛇，以及上面这只鼠标以外的 AKKO 型号。ATK 只读上一节列出的四套官方协议，BITYUAN 键盘和 HOLTEK 鼠标不在内。扩展点在 `src/PeripheralBattery.App/Sources/VendorSourceRegistry.cs`。读不到就显示未知。
- 未配对、未连接，或驱动不报告电量的设备。
- 电脑自己的电池。
- Windows 有时会在设备断开后仍保留节点和上一次的电量字节。列表可能短暂留下这台设备。

手动改名不能用来填写电量。覆盖文件是：

`%LOCALAPPDATA%\PeripheralBattery\identity-overrides.json`

```json
{
  "bt:AA:BB:CC:DD:EE:01": { "displayName": "客厅键盘", "kind": "键盘" }
}
```

`kind` 可以是 `keyboard`、`mouse`、`headset`、`other`、`unknown`，或中文「键盘 / 鼠标 / 耳机 / 其他 / 未知」。点「刷新」会重新读这个文件。键是列表内部用的稳定 ID：蓝牙设备一般是 `bt:` 加地址。

## 演示模式

没有蓝牙外设时也可以看界面。菜单里打开「演示模式」，或：

```powershell
dotnet run --project src/PeripheralBattery.App -- --demo
```

演示数据里有电量 72%、18%、55%，也有名称未知和电量未知。图标应显示 **18**。任务栏电量块按列表顺序把这几台并排画出来，最前面是电量最低的鼠标「18%」，读不到的仍写「未知」。`--demo` 只影响这一次运行；从菜单切换会写入设置。

程序版本是 **0.6.0**。安装包是 [PeripheralBattery.exe](https://github.com/62909/Peripheral-battery-display/releases/download/v0.6.0/PeripheralBattery.exe)，源码仓库里不附带 exe。

## 环境、构建、运行

日常使用请按上面的步骤下载 Release 里的 `PeripheralBattery.exe`，不需要安装 .NET 8 SDK。从源码编译才需要 Windows 10 或 Windows 11 x64，以及 .NET 8 SDK。运行自包含发布包时不需要另外装运行时。

```powershell
dotnet test
dotnet build src/PeripheralBattery.App/PeripheralBattery.App.csproj -c Release
dotnet run --project src/PeripheralBattery.App
```

在 Windows 上打一个单文件：

```powershell
dotnet publish src/PeripheralBattery.App/PeripheralBattery.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

或执行 `.\publish.ps1`。产物是 `publish\PeripheralBattery.exe`。

工程打开了 `EnableWindowsTargeting`，所以在 macOS / Linux 上也能编译。不带 `-r win-x64` 的 `dotnet build` 在 Linux 上不会产出 Windows 程序。上面的 `dotnet publish -r win-x64` 会打出 GUI 子系统的 `PeripheralBattery.exe`。托盘和任务栏仍然要在 Windows 上才能看到。

设置保存在 `%LOCALAPPDATA%\PeripheralBattery\settings.json`。

## 这次怎么验证的

验证环境是 Linux，.NET SDK 8.0.428，没有蓝牙外设，也没有 Windows 桌面，所以托盘图标和任务栏拖动没有在真机上点过。

- `dotnet test -c Release`：140 项通过。覆盖 GATT / HID / Windows 电量字节的解析、未知和冲突、设备分类和 VID/PID 命名、多来源合并、最低电量、任务栏比例在四条边上的位置、自动隐藏和全屏时是否显示、设置默认关闭开机启动、身份覆盖文件，以及 ATK 四套官方电量帧（BITMOUSE、COMPX、HECHI、HFD）的命令、校验和 0–100 边界。
- `dotnet build src/PeripheralBattery.App/PeripheralBattery.App.csproj -c Release`：成功，0 个警告。WPF 工程、托盘图标、GATT / HID / 系统电量属性读取和任务栏跟随窗口都在这个工程里。
- 同一条 `dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true` 在这台机器上打出了 `PeripheralBattery.exe`。`file` 的结果是 `PE32+ executable (GUI) x86-64, for MS Windows`。没有启动它。
