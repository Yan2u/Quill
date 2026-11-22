# Quill Architecture Documentation / Quill 架构文档

This directory contains the formal engineering and architectural documentation for **Quill**, written directly from the codebase structure and organized bottom-up from pure domain data structures to the multi-window WinUI 3 desktop application shell.

---

## English Documentation

- **[System Architecture Overview (`en/architecture.md`)](en/architecture.md)** — End-to-end architectural tour covering layer boundaries, immutable block tree, reversible transaction algebra, lossless Markdown serialization, two-phase memoizing projection, `VariableXamlRoot` incremental UI reconciliation, Win2D offscreen ClearType rendering, IME/input pipelines, performance bounds, themes, localization, and the unpackaged Windows 11 app shell.

---

## 简体中文深度文档（自底向上系统解析）

建议按顺序阅读，理解每一层的抽象边界与不变量约束：

| 章节 | 主题 | 核心涉及模块 |
| :--- | :--- | :--- |
| **[01 - 全局概览、依赖约束与工程结构](01-overview.md)** | 非 WebView 原生定位、单向数据流铁律、9 个源码工程 + 2 个测试工程 + Rust sidecar 的严格依赖拓扑与统一 Visual Studio 构建结构。 | `Quill.slnx`, `native/quill-native` |
| **[02 - 不可变文档模型、字素边界与零损耗 Markdown I/O](02-document-model-and-markdown-io.md)** | 块树 + 叶子源码双层结构、语法元数据、UTF-16 字素簇安全边界、Markdig 隔离导入及字节级保真 `MarkdownSerializer`。 | `Quill.Editor/Documents`, `Markdown`, `Files`, `Text` |
| **[03 - EditorState、光标/选区模型、命令与可逆事务](03-editor-state-and-operations.md)** | 单一真相 `EditorState`、`Position`/`Affinity`/BlockCaret、`BehaviorRegistry`、可逆 `Step` 与 `UndoHistory` 合并与段内提交规则。 | `Quill.Editor/State`, `Commands`, `Transactions`, `Behaviors`, `Editing` |
| **[04 - 双阶段增量投影与 VariableXaml UI 协调引擎](04-projection-and-variable-xaml.md)** | `DocumentProjector` 双阶段备忘录投影、`PositionMap` 双向偏移映射、`VNode` 与 `IStructuralHash`、`Reconciler` 就地打补丁与虚拟列表桥接。 | `Quill.Editor/Projection`, `Quill.VariableXaml.Model`, `Quill.VariableXaml` |
| **[05 - Win2D 自绘排版、公式/图表/高亮与块级控件](05-rendering-and-elements.md)** | `BlockTextLayout` half-leading 基线对齐、`CanvasVirtualControl` + 整像素 `CanvasRenderTarget` 消抖渲染、`RaTeX` 公式、`quill_native` SVG、TextMate 高亮。 | `Quill.Editor.WinUI/Text`, `Rendering`, `Elements`, `Quill.Highlighting` |
| **[06 - 输入管线、IME 组合、查找替换与性能工程](06-input-ime-and-performance.md)** | `MarkdownBox` 事件路由、`CoreTextEditContext` 防重入调和与延迟重排、源码双模式转换、`SearchController`、`BlockStackLayout` 估算与滚内存控制。 | `Quill.Editor.WinUI`, `Layout`, `Input`, `Search`, `Quill.Editor/Diagnostics` |
| **[07 - 配置存储、85 槽主题、双语热切换与应用壳体](07-settings-themes-and-app-shell.md)** | `JsonStore<T>` 多窗口同步、图片宏与拖放策略、`MdStyleSheet` 85 个样式槽、`UiStrings` 双语热切换、`Quill.Shell` 页面及非打包发布架构。 | `Quill.Settings`, `Quill.Shell`, `Quill`, `Quill.Editor.WinUI/Theming`, `Localization` |
