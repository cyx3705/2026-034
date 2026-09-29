# HistoryStrenua 模块 API

模块版本：**1.0.0**；宿主基线：**HistoryVulcan 5.1.2**。

本文件是**总线面**合同：模块消费方的权威合同，随发布候选进包（`docs/模块API.md`）。
模块内部类型见 `b-Office/current/技术合同.md`；AI 面（MCP 工具）由 MCP 服务封装，本文件不重复。

## 这个模块提供什么

PowerSW：作用于用户**正在用**的 SolidWorks 的快捷指令。每条快捷指令是 Aurora 页面上的一个按钮，
也是一条可以在控制台、脚本或别的模块里经总线调用的指令。

指令域：`strenua`。模块 `ui=true`，`mcpExposure=readonly`（只有 `strenua.quick.list` 投影到 MCP）。

**共同前提**：SolidWorks 必须已经在运行。本模块只附着，不启动、不隐藏、不退出它；
一次只执行一条快捷指令，第二条直接失败回执「还在执行」。

## 指令

### strenua.hole.callout

| 格 | 内容 |
| --- | --- |
| 能做什么 | 在当前工程图里取一个视图，视图里看得见、正对图纸的孔**每种标一次**（数量由 SolidWorks 的「N×」带出）；已有孔标注的种跳过 |
| 谁会用 | PowerSW 页面「孔标注」按钮；控制台直接敲 |
| 怎么调 | `strenua.hole.callout`（无参数）。调用前若 SolidWorks 里已选中视图（或视图里的任何对象）就用它，否则等用户点选，最多 60 秒 |
| `Data` 的确切类型 | 无（`null`）。结论只在 `Message`：`视图「X」：H 个孔共 T 种，新加 N 个孔标注，M 种已有标注跳过，K 个 SolidWorks 没有接受。` |
| 失败与边界 | 失败：SolidWorks 未运行 / 未注册；没有打开文档或活动文档不是工程图；60 秒内没点选视图；视图不引用模型；已被取消；确有要加的孔但一个都没加上。成功但无改动：视图里没有正对图纸的孔、或孔都已有标注 |

判孔规则：整圈圆边 + 旁边一张内凹且同半径的圆柱面 + 轴线正对图纸。异形孔向导的各类孔与手工切孔都算；
槽口端部半圆、凸台外圆、侧视投影不算。同心的圆边（沉头与底孔、通孔上下口）算一个孔。
一种 = 同一组件、同一特征、同一孔径；标在这一种里最靠左上的孔上。

执行中会经 `CommandContext.Progress` 逐步报告（等待点选、识别孔、加标注），消费方不必从结论里复述过程。

### strenua.quick.cancel

| 格 | 内容 |
| --- | --- |
| 能做什么 | 取消正在执行的快捷指令，包括正在等用户点视图的那一条 |
| 谁会用 | PowerSW 页面「取消」按钮 |
| 怎么调 | `strenua.quick.cancel` |
| `Data` 的确切类型 | 无 |
| 失败与边界 | 不失败：没有在跑的指令时也回成功，只说明没有可取消的 |

被取消的那条指令本身回执失败「已取消」。已经加上的标注不回滚。

### strenua.quick.list（只读）

| 格 | 内容 |
| --- | --- |
| 能做什么 | 列出全部快捷指令、当前状态与上次结果 |
| 谁会用 | AI（MCP）、控制台 |
| 怎么调 | `strenua.quick.list` |
| `Data` 的确切类型 | `IReadOnlyList<IReadOnlyDictionary<string,string>>`，每行键：`id`、`title`、`usage`、`state`、`result`、`time`（`HH:mm:ss`，未执行过为空） |
| 失败与边界 | 不失败 |

`state` 取值：`就绪`、`附着 SolidWorks`、`等待点选视图`、`识别孔`、`加标注`、`完成`、`失败`、`已取消`。

## 界面内部协议（不要跨模块调）

`strenua.ui.describe` / `strenua.ui.actions` / `strenua.ui.data` 是 Aurora 页面协议，声明了 `HiddenReason`。
页面 owner 与场景 id 为 `HistoryStrenua`；页面 id `powersw`，状态表节点 `quick-commands`。
