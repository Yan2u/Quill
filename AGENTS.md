## Principles of Developing Quill Project

### 编码规则

遵守基本的编码规范，例如

- Microsoft CSharp 的命名规范，文件名规范，导入规范；
- python 的命名规范、文件名规范，导入规范；
- typescript 的命名规范等；
- **代码注释、硬编码的提示文本请全部使用英语，避免编码问题**；
- 初版 App 用英语，后续接入多语言 localization 再考虑中文。

### 项目结构

- 需要拆分文件时无需犹豫，不要把大量代码挤在一个文件中。
- 每当你完成一次较大改动，在 `.codex/history` 中创建一个 markdown 文件，描述你此次改动的内容、目的、涉及到的模块、代码等。

### 开发环境

**用以下命令进入 Visual Studio 开发环境**，在执行编译等命令。

```
# 用 powershell
$cwd = $PWD.Path; & "D:\Visual Studio\18\insiders\Common7\Tools\Launch-VsDevShell.ps1" -Arch amd64 -HostArch amd64; cd $cwd; <command to execute>

# 用 cmd
"D:\Visual Studio\18\insiders\VC\Auxiliary\Build\vcvars64.bat" && <command to execute>
```

进入开发环境后，可以使用 ninja + MSVC + cmake 构建 C++ 项目，或者 dotnet 构建 C# 项目等等。

**使用代理加速访问 github 等站点**

```
# cmd
set http_proxy=http://127.0.0.1:7890 && set https_proxy=http://127.0.0.1:7
890

# powershell
$env:http_proxy="http://127.0.0.1:7890";$env:https_proxy="http://127.0.0.1:7890"
```

### 项目结构

遵循如下的项目结构

```
Quill/                           # 解决方案，工作区根目录
  - src/                         # 存放项目代码
    - Quill                      # 主 App
    - Quill.Playground
    - Quill.XXX
    - Quill.YYY                  # 后续需要新建项目时，放在 src/ 下面
 - tests/                        # 存放测试用项目，测试用例等，此处放项目代码，测试用物件不要放这里
 - scripts/                      # 存放编译，debug，自动化测试脚本
 - .codex
   - temp                        # 存放临时产物，例如日志、debug截图、临时数据等，需要及时清理
   - artifacts                   # 存放一些非临时产物，例如测试用 markdown 文档，测试用图片等
```

### Human in the Loop

GUI 上的问题，请你优先通过自动化测试脚本进行测试，如果难以测试，请及时叫我介入进行手动测试，我会实时反馈。

### 资源与参考

请你利用好下面这些官方或社区的资源和参考教程。

#### Search & Fetch

优先使用这个技能: /skill:exa-relay-research. 包含搜索与网页 fetch 工具。

exa skill 不可用时，可回退到你内置的 search 和 fetch 工具。

#### WinUI Gallery

官方提供的 WinUI3 控件与教程

- 主仓库：[microsoft/WinUI-Gallery: This app demonstrates the controls available in WinUI and the Fluent Design System.](https://github.com/microsoft/WinUI-Gallery)
- Samples：[WinUI-Gallery/WinUIGallery/Samples at main · microsoft/WinUI-Gallery](https://github.com/microsoft/WinUI-Gallery/tree/main/WinUIGallery/Samples)

#### CommunityToolkit.Windows

社区提供的控件资源与教程，有一些样式的控件需要用到这个

- 主仓库：[CommunityToolkit/Windows](https://github.com/CommunityToolkit/Windows/tree/main)
- 组件 samples：[Windows/components at main · CommunityToolkit/Windows](https://github.com/CommunityToolkit/Windows/tree/main/components)

#### DevWinUI

社区第三方开发者开发的控件资源与教程

[Welcome to DevWinUI | DevWinUI](https://ghost1372.github.io/QuickStart/DevWinUIQuickStart)

#### winapp & winui skills

你的技能列表中，`winui-*` 和 `winapp-*` 这些 skills 可用于帮助你熟悉 WinUI App 的开发流程和进一步了解代码规范。

如果你想查阅 WinUI 或者 win32 的一些 api，控件，样式等，可以使用 winapp skills 对应的 winapp-cli 程序进行查询。
