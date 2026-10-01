# HistoryStrenua

> PowerSW：SolidWorks 运行时快捷指令，一次手工操作变成一下点击

## 定位

HistoryStrenua 是 HistoryVulcan 的 SolidWorks 易用性模块，面向用户的名字叫 **PowerSW**。
它附着到你**正在用**的那个 SolidWorks，把重复的手工操作做成一排快捷按钮；每个按钮同时是一条宿主指令，
控制台里也能直接敲。

- 它只作用于用户已打开的 SolidWorks：不启动、不隐藏、不接管、不退出。
- 批量转换、离线处理文件属于 HistoryMinerva（`2026-024`），不在这里。

## 概况

| 项 | 值 |
| --- | --- |
| 编号 | `2026-034` |
| 角色 | 宿主模块（`kind=module`） |
| 指令域 | `strenua` |
| 界面 | Aurora 描述式页面「PowerSW」（场景 `HistoryStrenua`） |
| MCP 投影 | `readonly` |
| 版本与宿主下限 | [`HistoryStrenuaVersion.props`](./b-Code/HistoryStrenua/HistoryStrenuaVersion.props) |

## 能力

| 按钮 | 指令 | 用途 |
| --- | --- | --- |
| 孔标注 | `strenua.hole.callout` | 点一个工程图视图，视图里每种孔标一次（数量由 SW 的「N×」带出），标在孔左上方；已标过的种跳过 |
| 中心符号线 | `strenua.hole.centermark` | 点一个工程图视图，视图里全部的孔删掉旧中心符号线后重标：每种孔一组「线性中心符号线 + 连接线」，单孔用单个 |
| 孔位尺寸 | `strenua.hole.position` | 点一个工程图视图，以零件左侧、上侧为基准重标全部孔的位置尺寸：同种孔接着标、不同种从基准标，同种超过 4 个等距标「(N-1) x 间距 =总长」；按中心线标，并排的孔不重复 |
| 取消 | `strenua.quick.cancel` | 取消正在执行的快捷指令（包括正在等你点视图的那一条） |
| — | `strenua.quick.list` | 列出全部快捷指令及上次结果（只读） |

三条孔类指令认孔、分种的规则相同；选视图两种顺序都行：先在 SolidWorks 里点视图再按按钮，或者按完按钮 60 秒内去点视图。
参数读注册自描述：`diana.docs.read domain=strenua`（宿主 6.1.0 起没有消费文档）；回执与 Data 形状见 [技术合同](./b-Office/current/技术合同.md)「对外约定」。

## 入口

| 入口 | 用途 |
| --- | --- |
| [`AGENTS.md`](./AGENTS.md) | AI 工作合同：读取顺序、真值判定、边界 |
| [`project.manifest.json`](./project.manifest.json) | 项目身份、活动目录、文档与命令 |
| [文档中心](./b-Office/文档中心.md) | 文档索引与读取顺序 |
| [项目概览](./b-Office/current/项目概览.md) | 目标、范围与状态 |
| [技术合同](./b-Office/current/技术合同.md) | 现行需求与架构 |
| [有效决策](./b-Office/current/有效决策.md) | 仍然有效的关键决策 |
| [验证合同](./b-Office/current/验证合同.md) | 验证层级、命令与证据 |

## 目录

| 路径 | 职责 |
| --- | --- |
| `b-Code/HistoryStrenua/` | 模块源码、manifest 与 `eng/` 构建脚本 |
| `b-Code/HistoryStrenua.Tests/` | 离线自动验证 |
| `b-Code/` | 项目合同检查 |
| `b-Office/` | 项目文档：`current/` 现行合同、`history/` 只读归档 |
| `z-Publish/` | 正式快照与 `history/` 归档，由宿主管线写入 |

## 构建与验证

```powershell
dotnet build .\b-Code\HistoryStrenua\HistoryStrenua.csproj -c Release -p:NuGetAudit=false
dotnet run --project .\b-Code\HistoryStrenua.Tests\HistoryStrenua.Tests.csproj -c Release -p:NuGetAudit=false
powershell -NoProfile -ExecutionPolicy Bypass -File .\b-Code\Test-ProjectContract.ps1 -Instantiation
```

自动验证全程离线，不需要也不会碰 SolidWorks。真机验收步骤见验证合同的 VERIFY-LIVE。

## 开发与发布

改动只进 `vulcan.dev.start` 创建的工作区，经宿主 Console CLI 走
`vulcan.dev.start` → `vulcan.dev.submit`（候选构建并热装送审）→ `vulcan.dev.finish`（批准后并回并写入 `z-Publish`）。
本仓不自行发布；`eng/Build-HistoryStrenuaPackage.ps1` 只用于本地候选构建。

**加一个快捷按钮**：写一个 `QuickCommand`（在已附着 SolidWorks 的 STA 线程上执行），
登记进 `QuickCommands.All`。按钮、动作声明、指令注册和状态表都由登记表生成。

## 要点

- SolidWorks Interop 不在编译期引用，运行时从本机安装目录加载；没装 SolidWorks 的机器照样能构建和跑测试。
- 一次只跑一条快捷指令：它们共用同一个 SolidWorks 的选择集。
- 页面 owner 由指令域推出（`strenua` → `HistoryStrenua`），合同检查守着这条。

## 保留内容
- 本模板项目介绍：此为最初的准备的项目模板
    每个分支项目都会由他去继承
- 作者：Pinavia - 2025

![logo](./Logo.png)
