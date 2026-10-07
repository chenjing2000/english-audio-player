# EnglishAudioPlayer

当前版本：**v0.1.7**

一个简单的 C# WPF 英语教材音频播放器。程序使用固定 RMS 能量阈值检测 MP3 中的持续低能量区间，左右方向键跳转到前一个或后一个停顿中点。

## 技术与依赖

- .NET 10 / WPF
- NAudio 2.2.1：音频解码、播放、暂停、停止和 Seek
- TagLibSharp 2.3.0：读取 MP3 的 Title、Album 和 Duration

程序不使用 ASR、Whisper、VAD、MVVM 框架、依赖注入、事件总线或日志框架。

## 项目结构

```text
EnglishAudioPlayer/
├── App.xaml / App.xaml.cs
├── MainWindow.xaml / MainWindow.xaml.cs
├── WindowStyleHelper.cs
├── AudioPlayer.cs
├── SilenceDetector.cs
├── SilenceSettings.cs
├── SilenceSettingsWindow.xaml / .cs
├── SilenceRange.cs
├── AudioLibrary.cs
├── AudioTrack.cs
└── ui/
    ├── Icons.xaml
    └── main.ico

docs/
└── program-overview.md

tests/
└── AudioPausePlayer.Tests/
```

各模块职责：

| 文件 | 职责 |
|---|---|
| `AudioPlayer.cs` | 封装 NAudio，负责打开、播放、暂停、停止、Seek、播放状态和资源释放 |
| `SilenceDetector.cs` | 独立读取 PCM，计算 RMS/dBFS，识别停顿并提供前后停顿导航 |
| `SilenceSettings.cs` | 保存停顿检测与导航参数、默认值和基本范围校验 |
| `SilenceSettingsWindow.xaml/.cs` | 编辑参数、恢复默认值，不参与音频分析 |
| `SilenceRange.cs` | 保存停顿起点、终点和中点 |
| `AudioLibrary.cs` | 扫描当前文件夹中的 MP3、自然排序并读取标签 |
| `AudioTrack.cs` | 保存列表项数据及播放/暂停显示状态 |
| `MainWindow.xaml/.cs` | WPF 布局、用户输入、后台分析和各模块协调 |
| `WindowStyleHelper.cs` | 仅负责禁用系统最大化按钮，同时保留窗口手动缩放能力 |

`MainWindow` 不处理 PCM、RMS 或 MP3 解码；`SilenceDetector` 不依赖播放器或 UI；`AudioPlayer` 不知道播放列表和停顿检测逻辑。

## 运行

开发环境需要 Windows 和 .NET 10 SDK。

```powershell
dotnet restore EnglishAudioPlayer/AudioPausePlayer.csproj
dotnet build EnglishAudioPlayer/AudioPausePlayer.csproj -c Release --no-restore
dotnet run --project EnglishAudioPlayer/AudioPausePlayer.csproj --no-restore
```

启动后点击“打开文件夹”，选择包含 MP3 的目录。程序只扫描该目录本身，不递归子目录。

仓库不包含测试 MP3。`.gitignore` 全局忽略 `*.mp3`，所以可以在项目根目录自行建立 `audio/` 并放入本地测试音频，不会被 Git 提交。

## 操作

- **双击列表行**：播放该音频。
- **Enter**：只有当某个表格行拥有键盘焦点时，播放该行；不是全局快捷键。
- **Space**：全局播放/暂停。尚未加载文件时播放当前选中项，没有选中项时播放第一项。
- **Left / Right**：跳到上一个 / 下一个有效停顿中点。
- **停止按钮**：停止并归零，保留当前已加载文件。
- **进度条**：点击或拖动后 Seek，保持原播放/暂停状态。
- **停止按钮右侧的圆形设置按钮**：打开停顿检测设置。修改后点“确定”立即生效；当前已加载音频会在后台重新分析，播放状态不变。
- **恢复默认**：将设置页中的四个参数恢复为默认值；点“确定”后应用。

分析期间普通播放、暂停、停止和拖动定位仍可使用；为避免多个分析任务重叠，分析完成前不允许切换播放文件或重新打开文件夹。

## 停顿检测参数

设置窗口可以修改 4 个参数。默认值：

```text
分析帧长度                 10 ms
最短停顿时长               300 ms
静音阈值                   -45 dBFS
前后跳转容差               200 ms
```

允许范围：

```text
分析帧长度                 1 - 100 ms
最短停顿时长               50 - 5000 ms
静音阈值                   -100 - 0 dBFS
前后跳转容差               1 - 5000 ms
```

前三项决定停顿识别，导航容差决定左右方向键在当前位置附近跳过多大的范围。停顿位置取静音区间中点。设置仅在当前程序运行期间保存，不写配置文件。

参数输入框只接受 ASCII 数字 `0-9` 和位于首位的正负号；分析帧长度、最短停顿时长、前后跳转容差不允许负号，静音阈值允许负号。小数点、字母、空格和其它特殊字符均不能通过键盘输入或粘贴进入输入框。

该算法只使用声音能量，不判断语义句界；背景音乐或持续高噪声录音可能不适用。

## 界面

- 默认宽度：主屏宽度的 50%，计算结果取整数
- 默认高度：可用屏幕高度的 60%，计算结果取整数
- 不支持窗口最大化；窗口仍可通过边缘或角部手动调整大小
- 最小尺寸：`520 × 420`
- 最大尺寸：`900 × 600`
- 初始尺寸按屏幕工作区约 `50% × 60%` 计算，并限制在上述最小/最大范围内；尺寸和位置计算结果取整数
- 字体：12
- 三个主按钮：`36 × 36`，圆角 7
- 按钮图标：`34 × 34`，水平、垂直居中
- 设置按钮：位于停止按钮右侧，`36 × 36` 圆形按钮
- 设置窗口尺寸：`324 × 350`；4 个参数输入框宽度：`80`，数值水平、垂直居中
- 设置窗口按钮宽度：恢复默认 `76`，取消/确定 `54`
- 不显示控件焦点虚线，但保留正常键盘焦点和键盘操作

## 测试

不需要 MP3 即可运行 PCM、停顿导航、损坏文件处理等核心测试：

```powershell
dotnet restore tests/AudioPausePlayer.Tests/AudioPausePlayer.Tests.csproj
dotnet run --project tests/AudioPausePlayer.Tests --no-restore
```

如果项目根目录存在 `audio/*.mp3`，还会额外执行有效 MP3 标签测试。以下集成测试同样需要至少一份本地 MP3：

```powershell
# 生成真实音频停顿报告到 docs/test-results/
dotnet run --project tests/AudioPausePlayer.Tests --no-restore -- --report

# 使用实际音频输出设备测试播放生命周期
dotnet run --project tests/AudioPausePlayer.Tests --no-restore -- --playback

# WPF 窗口集成测试
dotnet run --project tests/AudioPausePlayer.Tests --no-restore -- --ui
```

`docs/test-results/` 是测试生成目录，已加入 `.gitignore`。

## 发布

采用普通 framework-dependent 文件夹发布：

```powershell
dotnet publish EnglishAudioPlayer/AudioPausePlayer.csproj -c Release -p:DebugType=None -p:DebugSymbols=false -o artifacts/app
```

`artifacts/` 已加入 `.gitignore`。发布目录中的主程序和依赖 DLL 需要一起保留。
