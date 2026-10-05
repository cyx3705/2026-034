# HistoryStrenua

> PowerSW：SolidWorks 运行时快捷指令，一次手工操作变成一下点击

## 定位

HistoryStrenua 是 HistoryVulcan 的 SolidWorks 易用性模块，面向用户的名字叫 **PowerSW**。
它附着到你**正在用**的那个 SolidWorks，把重复的手工操作做成一键快捷指令；每条同时是一条宿主指令，
控制台里也能直接敲。

- 它只作用于用户已打开的 SolidWorks：不启动、不隐藏、不接管、不退出；新建的工程图不替用户保存。
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

| 页面上 | 指令 | 用途 |
| --- | --- | --- |
| 孔标注全流程 | `strenua.hole.flow` | 不用点视图：当前图纸页全部视图（轴测图跳过）逐个做「销钉符号 → 中心符号线 → 孔位尺寸 → 外轮廓 → 孔标注 → 销孔标注」，全部视图做完才结束 |
| 销钉符号 | `strenua.hole.dowel` | 点一个工程图视图，异形孔向导做的销钉孔没有销钉符号的各加一个；已有的跳过 |
| 孔标注 | `strenua.hole.callout` | 点一个工程图视图，视图里每种孔标一次（数量由 SW 的「N×」带出），标在孔左上方；已标过的种跳过；「避障」开着时文字压在别的孔的标注线条上就换到孔的另一个角 |
| 中心符号线 | `strenua.hole.centermark` | 点一个工程图视图，视图里全部的孔删掉旧中心符号线后重标：每种孔一组「线性中心符号线 + 连接线」，单孔用单个 |
| 孔位尺寸 | `strenua.hole.position` | 点一个工程图视图，以零件左侧、上侧为基准重标全部孔的位置尺寸：同种孔接着标、不同种从基准标，同种超过 4 个等距标「(N-1) x 间距 =总长」；按中心线标，并排的孔不重复；「尺寸链」开着时改用 SW 尺寸链（坐标尺寸），每个方向一组、0 点在零件左侧 / 上侧直边；「避障」开着时数字压在别的孔的标注线条上就沿尺寸线滑开 |
| 销孔标注 | `strenua.hole.dowelfit` | 点一个工程图视图，销孔（异形孔向导销钉孔）的孔标注带 H7 与偏差值；同种相邻销孔之间的尺寸带 ±0.02，已有的改、没有的补；与别的孔同段被去重时后面写「(仅销孔)」 |
| 外轮廓 | `strenua.hole.outline` | 点一个工程图视图，以零件左侧、上侧直边为基准标外轮廓每个台阶（含总长总宽）；「尺寸链」关时一站一层线性尺寸，开时加进孔的那组坐标尺寸；这一页别的视图标过的（如高度）不再重复标 |
| 一键出图（出图） | `strenua.drawing.auto` | 打开零件（或在装配体里选中零件）按一下：依次做下面的「新建工程图 → 投影视图 → 轴测图 → 技术要求 → 排版」，再「孔标注全流程」、「全图圆角」、「全图倒角」，出一张基本标好的图，不保存 |
| 新建工程图（出图） | `strenua.drawing.create` | 按「零件」工程图模板建新图、放主视图：图幅（A4 横 / A3 / A2）与比例按零件大小、孔的疏密和整套视图排不排得下自动选，主视图取孔、窗口和圆弧最多的那一面；标题栏由模板的属性链接带出；不保存 |
| 投影视图（出图） | `strenua.drawing.project` | 当前工程图：从主视图（选了视图就从它）投影出看侧面孔、窗口的视图，都没有就加一个看厚度的；那个方向已有视图就不加；贴着主视图放、别的不动 |
| 轴测图（出图） | `strenua.drawing.iso` | 当前工程图：从主视图（或选中的视图）斜投影一个轴测图，放右边的空地（放不下缩一两档比例）；已有就不加 |
| 排版（出图） | `strenua.drawing.arrange` | 当前图纸页的主视图、四边投影视图、轴测图、技术要求按实际大小重新排开不重叠、给尺寸留地方，躲开标题栏等；排不下把图纸比例降一档；别的视图（剖视、局部等）原地不动 |
| 全图圆角（出图） | `strenua.drawing.filletall` | 不用点视图：当前图纸页全部视图（轴测图跳过）逐个做「圆角标注」 |
| 全图倒角（出图） | `strenua.drawing.chamferall` | 不用点视图：当前图纸页全部视图（轴测图跳过）逐个做「倒角标注」 |
| 圆角标注（出图） | `strenua.drawing.fillet` | 点一个视图，每个圆角加一个 R（R1 按技术要求不标，同半径 3 个以上合标「N x R」），文字放零件外的空处，已标的跳过；视图里的圆先减去孔（认得出是孔的一律不管，孔归孔类指令），剩下的整圆（凸台、轴端）标 Ø，同直径 3 个以上合标「N x Ø」 |
| 倒角标注（出图） | `strenua.drawing.chamfer` | 点一个视图，侧着看成斜线的倒角每种尺寸标一个线性尺寸（量一条直角边，文字写「C5」），同尺寸 2 个以上合标「N x C5」，尺寸先放下边、右边；C1 按技术要求不标，已标的、别的视图标过的跳过；轴端的锥面倒角暂不认 |
| 技术要求（技术要求） | `strenua.tech.note` | 当前工程图：技术要求放进图框（标题栏正上方优先，躲开视图），内容取模块数据目录「技术要求.txt」，没有就用默认 8 条；已有就不加。下一步在这一类接 SW 的技术要求模板 |
| 未标尺寸（检查） | `strenua.check.dimension` | 不用点视图：当前图纸页全部视图（轴测图除外）查孔标注、孔位、销孔 H7 / ±0.02、外轮廓台阶有没有漏标，列进控制台并在 SW 里选中，不改图 |
| 悬空标注（检查） | `strenua.check.dangling` | 不用点视图：当前图纸页全部视图与图纸上附着丢了的尺寸、注解（模型改过、中心符号线删过以后常见），列进控制台并在 SW 里选中，不改图 |
| 注解重叠（检查） | `strenua.check.overlap` | 不用点视图：当前图纸页全部视图里尺寸、孔标注、注释的文字有没有互相压、压别的注解的线、压视图轮廓线与孔、压技术要求 / 标题栏、出图框，判法同「避障」，列进控制台并在 SW 里选中，不改图 |
| 图纸截图（检查） | `strenua.check.snapshot` | 不用点视图：当前图纸页整页存成 PNG（模块数据目录 snapshots，只留最近 30 张），回执给出路径——AI 经 MCP 调完指令读这张图就能自己验收 |
| 开关「避障」 | `strenua.option.clearance` | 所有往图上加东西的指令都躲开已有的：孔标注、孔位尺寸、销孔标注、外轮廓加完后把压线的文字挪开；圆角、倒角文字找不压线也不压已有标注的地方；技术要求、轴测图找空地，投影视图压到别的就往外让；关着时都放默认位置（默认开，记住上次） |
| 开关「尺寸链」 | `strenua.option.chain` | 孔位尺寸与外轮廓改用 SW 尺寸链（坐标尺寸）：每个方向一组，0 点在零件左侧 / 上侧直边，文字排一列自动折弯，不分种、不用阵列写法（默认关，记住上次） |
| 工具条「取消」 | `strenua.quick.cancel` | 取消正在执行的快捷指令（包括正在等你点视图的那一条） |
| — | `strenua.quick.list` | 列出全部快捷指令及上次结果、两个开关的状态（只读） |
| — | `strenua.quick.run` | 按 key 执行一条快捷指令（控制台用） |
| 工具条「浮动」 | `aurora.ui.float name=powersw` | 整页浮成置顶小窗，操作 SolidWorks 时也点得到，按住工具条中间的文字拖动；再点还原（需 Aurora 1.30.1+） |

页面是一行工具条（浮动 / 占位文字 / 类 / 取消）加按类切换的控制面板：窗口最下面固定一行「避障」「尺寸链」两个开关，切到哪一类都在、都管用（1.11.0，需 Aurora 1.30.2）；每类控制面板只有一行按钮，窗口窄了由前端自己折行、拖宽拖窄均匀伸缩（1.12.0）：「孔」七个、「出图」九个、「技术要求」一个、「检查」四个。结果写进控制台。
孔类指令认孔、分种的规则相同；单条指令选视图两种顺序都行：先在 SolidWorks 里点视图再点指令名，或者点完 60 秒内去点视图。
「孔标注全流程」「未标尺寸」「悬空标注」「注解重叠」「图纸截图」「技术要求」「排版」「全图圆角」「全图倒角」不用点视图，直接做当前图纸页；「投影视图」「轴测图」选了视图就从它投影，没选就从主视图；
「一键出图」「新建工程图」作用于当前零件（或装配体里选中的零件）。出图类每一步都能单独按，也能在手工建的图上接着按。
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

**加一条快捷指令**：写一个 `QuickCommand`（在已附着 SolidWorks 的 STA 线程上执行），
登记进 `QuickCommands.All`。按钮、类选项、类面板、动作声明和指令注册都由登记表生成；新类在 `QuickCommands` 的类名表里登记中文名，页面自动多一块面板。

## 要点

- SolidWorks Interop 不在编译期引用，运行时从本机安装目录加载；没装 SolidWorks 的机器照样能构建和跑测试。
- 一次只跑一条快捷指令：它们共用同一个 SolidWorks 的选择集。
- 页面 owner 由指令域推出（`strenua` → `HistoryStrenua`），合同检查守着这条。

## 保留内容
- 本模板项目介绍：此为最初的准备的项目模板
    每个分支项目都会由他去继承
- 作者：Pinavia - 2025

![logo](./Logo.png)
