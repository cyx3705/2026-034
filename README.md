# HistoryStrenua

> PowerSW：SolidWorks 扩展 MCP 指令集，让 AI 和人一起直接操作正在用的那个 SolidWorks

## 定位

HistoryStrenua 是 HistoryVulcan 的宿主模块，指令域 `strenua`，面向用户的名字叫 **PowerSW**。
它附着到用户**正在用**的 SolidWorks，把建模、装配、标准件选型、出图、标注、公差、检查做成一组粒度清楚的宿主指令：

- **给 AI**：每条指令都经宿主投影成 MCP 工具，AI 可以从零建零件、摆总装、插外购件，再出图、逐个修标注、自检验收；
  回执带文字、数据和「本步改了什么」的图纸 SVG，AI 读文字就能核对。原 SolidWorks MCP 已弃用（连旋转零部件都做不到），缺的能力都补在这里（1.38.0）。
- **给人**：常用的出图、标注、检查同时是 Aurora 页面「PowerSW」上的按钮，一下点击代替一串手工操作；控制台里也能直接敲。
- **边界**：只附着，不启动、不隐藏、不退出 SolidWorks；除「新建零件」与点名的 `doc.save` 外不替用户保存。
  批量转换、离线处理文件归 HistoryMinerva（`2026-024`），不在这里。

## 概况

| 项 | 值 |
| --- | --- |
| 编号 | `2026-034` |
| 角色 | 宿主模块（`kind=module`） |
| 指令域 | `strenua`（约 90 条） |
| 调用方式 | MCP 工具 / 宿主控制台 / Aurora 页面「PowerSW」（场景 `HistoryStrenua`） |
| MCP 投影 | `readonly`（见 [`module.manifest.json`](./b-Code/HistoryStrenua/module.manifest.json)） |
| 目标环境 | SolidWorks 2025 SP5（运行时加载官方 Interop） |
| 版本与宿主下限 | [`HistoryStrenuaVersion.props`](./b-Code/HistoryStrenua/HistoryStrenuaVersion.props) |

## AI 怎么用

1. **查说明书**：`diana.docs.catalog` → `diana.docs.read domain=strenua`。参数、例子、回执字段都在注册自描述里（宿主 6.1.0 起没有消费文档），本 README 只列指令做什么。
2. **先看后改**：出图前 `model.info` / `asm.list` 读模型；改图前 `check.svg`（图纸读成带编号的 SVG）、`view.list`、`dim.list` 拿到视图名、注解名和边 / 孔编号，再按编号寻址修改。
3. **大粒度起稿，细粒度修**：`onekey.*` 出初稿（回执末尾带本类修正要点，必须照着修完）→ `dim.*`、`tol.*`、`tech.*`、`view.move` 逐处修 → `check.*` 验收。
4. **长指令**：经 MCP 调用最多 40 秒先回「还在执行」，指令接着跑；用 `strenua.quick.last` 取完整回执（完成即返回，最多等 40 秒）。
5. **笔记**：经 MCP 的每条回执最上面带通用区与本类区的注意点；发现指令本身的坑用 `note.write` 记下（每条 ≤ 100 字，不记项目内容）。

所有指令共用一个执行器：在附着 SolidWorks 的 STA 线程上**一次只跑一条**（它们共用同一个选择集），可用 `strenua.quick.cancel` 取消。
写进尺寸、孔标注、技术要求的 Ø ↧ ⌴ ± ° 等符号自动换成 SolidWorks 符号码，不会乱码。

## 指令集

下表省略前缀 `strenua.`。「页面」列为按钮所在类；空着的是只给 AI / 控制台的指令。

### 建模与装配（1.38.0）

| 指令 | 页面 | 用途 |
| --- | --- | --- |
| `model.new` | | 用默认模板新建零件或装配体并**立即存盘**到给定路径（文件在才能被总装引用） |
| `model.sketch` | | 在基准面或零件表面上一次画完一张草图：矩形、圆、多孔、多边形、腰型槽（分号隔开） |
| `model.extrude` | | 草图拉伸或切除（盲孔 / 两向贯穿 / 中面对称），切除方向不对自动换向；回执给包围盒与体积变化 |
| `model.plane` | | 从基准面沿法向平移建新基准面 |
| `model.delete` | | 按名字删特征（删拉伸连带它的草图） |
| `model.info` | | 读活动零件：包围盒、体积、材料、特征树、全部孔（按几何认），供摆位与接口核对 |
| `asm.list` | | 列总装顶层零部件：名字、文件、原点、轴朝向、包围盒、是否固定 |
| `asm.insert` / `asm.move` | | 按「零件原点落点 `at=` + 三根轴朝向 `axes=X,-Z,Y`」直接写变换摆位，默认固定、不加配合 |
| `asm.delete` | | 按名字删零部件，不删文件 |
| `asm.interference` | | 实体干涉检查：每对零部件的干涉体积与包围盒；贴合面不算 |

### 外购件：蚂蚁工场标准件（1.37.0）

| 指令 | 用途 |
| --- | --- |
| `mymooo.category` | 第 1 层：分类树、名字片段找分类、关键字搜商品（不碰 SolidWorks） |
| `mymooo.product` | 第 2 层：某分类的筛选项、品牌与商品（不碰 SolidWorks） |
| `mymooo.insert` | 第 3 层：按编码参数选型，选全了下载 3D、另存进总装目录「外购件\料号」、按零件原点插进当前总装；没选全回还缺的参数 |
| `mymooo.remove` | 按零部件名删顶层零部件；`files=是` 再删「外购件」里不再被引用的文件 |

### 文档与属性

| 指令 | 用途 |
| --- | --- |
| `doc.list` / `doc.open` / `doc.activate` | 列出、打开、切换 SolidWorks 里的文档 |
| `doc.save` / `doc.close` | 只碰工程图、要点名才动：新图默认存到引用模型旁同名；有未保存改动默认拒绝关闭，`discard=是` 才丢 |
| `prop.read` / `prop.write` / `prop.default` | 读写零件属性（图号、名称、材料、表面处理、热处理、日期、设计），默认不存盘；`prop.default` 记本机默认值 |

### 出图（工程图）

| 指令 | 页面 | 用途 |
| --- | --- | --- |
| `onekey.sheetmetal` / `frame` / `machined` | 一键 | 钣金 / 铝型材框架 / 加工件出初稿：基础出图（中途插技术要求）→ 按类别标注 → 整理标注，回执带修正要点，不保存 |
| `drawing.basic` | 一键 | 新建工程图 → 投影视图 → 轴测图 → 排版 → 对称轴，不插技术要求、不标注 |
| `drawing.finish` | 一键 | 整理标注：按整页真实文字、尺寸线、轮廓和图框避让并重排复核，剩余冲突列入失败 |
| `drawing.symmetry` | 一键 | 当前页对称视图加对称轴（中心线），插入后核对贯穿 |
| `drawing.outline` / `outlineall` | 一键 | 指定视图 / 全页标外轮廓台阶（含总长总宽），跨视图不重复 |
| `drawing.create` | 视图 | 按模板建图、放主视图，图幅比例与主视图方向自动选；新图名与零件一致 |
| `drawing.project` / `drawing.iso` | 视图 | 投影出看侧面孔、窗口或厚度的视图 / 轴测图，已有方向不再加 |
| `drawing.arrange` | 视图 | 视图与技术要求按实际大小重新排开、给尺寸留地方，排不下降一档比例 |
| `view.list` / `view.move` | 视图 | 列出视图（名字、种类、比例、外框，图纸 mm）/ 按名字挪视图，读回核对 |

### 孔与倒圆

| 指令 | 页面 | 用途 |
| --- | --- | --- |
| `hole.flow` | 孔 | 全页逐视图：销钉符号 → 中心符号线 → 孔位尺寸 → 方形槽 → 孔标注 → 销孔标注 |
| `hole.dowel` / `centermark` / `position` / `squareslot` / `callout` / `dowelfit` | 孔 | 单视图分步：补销钉符号、重标中心符号线、重标孔位尺寸、方形槽、每种孔标一次孔标注、销孔 H7 与 ±0.02 |
| `hole.place` | | 全页孔标注按整页线、字、图框重新选位，可换挂到同种另一个孔 |
| `fillet.all` | 倒圆 | 全页依次做圆心位置、圆弧 R / Ø、倒角 C |
| `fillet.arccenter` / `arc` / `chamfer`（及 `*all` 全页版） | 倒圆 | 圆弧圆心定位、R 与非孔整圆 Ø（同值合标）、倒角写「C5」（同值合标） |

孔按几何认，不按特征认：阵列、镜像、手工切、导入件里的孔都认；同一页上别的视图标过的种 / 孔不再标。

### 标注与公差（细粒度，按名字或 SVG 编号寻址）

| 指令 | 页面 | 用途 |
| --- | --- | --- |
| `dim.list` | 标注 | 列出视图（或整页）的尺寸与注解：名字、种类、显示文字、值、位置、外框 |
| `dim.delete` / `dim.move` / `dim.edit` | 标注 | 删（其余尺寸放回原处）、挪文字（线性尺寸即换层）、改前缀 / 后缀 / 下一行 / 对称公差 |
| `dim.add` | | 按 SVG 编号在两头（线、端点、孔、圆弧圆心）之间加水平或竖直尺寸，读回值核对 |
| `dim.hole` / `dim.center` / `dim.thread` | | 按孔编号加孔标注、中心符号线；把孔标注改写成螺纹孔标法（4 x M6↧12） |
| `tol.dim` | | 尺寸公差：对称、双向、极限、基本、最小、最大、配合，或去掉 |
| `tol.gtol` / `tol.datum` | | 形位公差框（14 种符号，可多行）/ 基准符号；只查写法，不判值 |

### 技术要求

| 指令 | 页面 | 用途 |
| --- | --- | --- |
| `tech.apply` / `tech.default` | 要求 | 从 2026-025 设计库的「通用技术要求」插一份（换掉原有的）/ 设本机默认 |
| `tech.ai` | 要求 | 截图 + 图纸信息交给 AI（经 Ratio 识图）选一份模板，只许删最多 3 条，不许新增或改写 |
| `tech.list` / `tech.add` / `tech.remove` | | 按编号列、加、删技术要求条目，原处替换 |

技术要求不论从哪进来，都只放在标题栏正上方或左侧。

### 检查（只读，不改图）

| 指令 | 页面 | 用途 |
| --- | --- | --- |
| `check.svg` | 检查 | 当前页（或一个视图）读成 SVG 代码：图纸 mm，边与孔带编号，标注画框加字；超 1 万字依次降级，最后回截图 |
| `check.snapshot` | 检查 | 当前纸面黑白 200 DPI PNG，给 AI 看图验收 |
| `check.dimension` | 检查 | 尺寸网查漏标的位置、孔径、R、斜边 |
| `check.decimal` / `check.dangling` / `check.overlap` | 检查 | 两位小数、悬空标注、注解互压 / 压线 / 出框 |
| `check.chain` / `check.redundant` | | 不在尺寸链上的尺寸；能由别的尺寸算出的重标尺寸（带算式与建议删哪个） |

### 运行与页面

| 指令 | 用途 |
| --- | --- |
| `quick.list` / `quick.run` / `quick.last` / `quick.perf` / `quick.cancel` | 列出快捷指令与状态、按 key 执行、取回上次完整回执（`back=` 往前取）、耗时明细、取消正在执行的 |
| `note.read` / `note.write` | 读写指令注意点笔记（MCP 回执自动带上） |
| `option.clearance` / `option.chain` / `option.techai` | 开关「避障」「尺寸链」「AI 填写技术要求」，记住上次 |
| `page.float`、`ui.data` | 页面浮动与页面数据（Aurora 用） |

页面是一行工具条（浮动 / 占位文字 / 类 / 取消）加按类切换的控制面板：一键、孔、倒圆、视图、标注、要求、检查；
窗口最下面固定一行开关。单视图指令选视图两种顺序都行：先在 SolidWorks 里点视图再点按钮，或者点完 60 秒内去点视图。
取舍、真机结论与已知缺口见 [现行约定](./b-Office/现行约定.md)。

## 入口

| 入口 | 用途 |
| --- | --- |
| [`AGENTS.md`](./AGENTS.md) | AI 工作合同：读取顺序、真值判定、边界 |
| [`project.manifest.json`](./project.manifest.json) | 项目身份、活动目录、文档与命令 |
| [现行约定](./b-Office/现行约定.md) | 唯一的长期文档：真相在哪、架构、取舍、真机才知道的、已知缺口、工程约定 |

## 目录

| 路径 | 职责 |
| --- | --- |
| `b-Code/HistoryStrenua/` | 模块源码（`Planning` / `SolidWorks` / `Runtime` / `Sheet` / `Commands` / `Page` 六层）、manifest 与 `eng/` 构建脚本 |
| `b-Code/HistoryStrenua.Tests/` | 离线自动验证（含架构门禁 `ArchitectureTests`） |
| `b-Code/` | 项目合同检查 |
| `b-Office/` | `现行约定.md`，唯一的长期文档 |
| `z-Publish/` | 正式快照与 `history/` 归档，由宿主管线写入 |

## 构建与验证

```powershell
dotnet build .\b-Code\HistoryStrenua\HistoryStrenua.csproj -c Release -p:NuGetAudit=false
dotnet run --project .\b-Code\HistoryStrenua.Tests\HistoryStrenua.Tests.csproj -c Release -p:NuGetAudit=false
powershell -NoProfile -ExecutionPolicy Bypass -File .\b-Code\Test-ProjectContract.ps1 -Instantiation
```

自动验证全程离线，不需要也不会碰 SolidWorks。还没真机跑过的见现行约定「已知缺口」。

## 开发与发布

改动只进 `vulcan.dev.start` 创建的工作区，经宿主 Console CLI 走
`vulcan.dev.start` → `vulcan.dev.submit`（候选构建并热装送审）→ `vulcan.dev.finish`（批准后并回并写入 `z-Publish`）。
本仓不自行发布；`eng/Build-HistoryStrenuaPackage.ps1` 只用于本地候选构建。submit 前 `diana.docs.audit domain=strenua` 须零缺陷。

**加一条指令**：在 `Commands/<类>/` 写一个 `QuickCommand`（在已附着 SolidWorks 的 STA 线程上执行），登记进 `QuickCommands.All`；
指令注册、MCP 自描述、按钮、类面板和动作都由登记表生成。只给 AI 的指令不设页面类即可；新类在 `QuickCommands` 的类名表里登记中文名，页面自动多一块面板。

## 要点

- SolidWorks Interop 不在编译期引用，运行时从本机安装目录加载；没装 SolidWorks 的机器照样能构建和跑测试。
- 一次只跑一条指令：它们共用同一个 SolidWorks 的选择集。
- 坐标一律图纸 mm、左下角为原点，与 SVG、截图对得上；视图与注解按名字寻址，几何按 SVG 编号寻址。
- 页面 owner 由指令域推出（`strenua` → `HistoryStrenua`），合同检查守着这条。

## 保留内容

### logo与作者

- 作者：pinavia
- ![logo](./Logo.png)

### 学习许可与权利保留

- 本项目允许个人、学校和研究者进行非商业性的学习、研究和引用。
- 未经著作权人书面许可，不得将项目内容用于商业开发、产品制造、商业服务、商业宣传、投标交付、毕业设计成品交付或再授权。
- 不得删除作者、来源、版本历史和第三方许可证声明，不得将本项目或其衍生成果宣称为与作者无关的原创项目。
- 软件源码适用 [PolyForm Noncommercial 1.0.0](https://polyformproject.org/licenses/noncommercial/1.0.0/)；机械模型、PCB、原理图、图片、文档、Unity 工程和其他非软件资料适用 [CC BY-NC-ND 4.0](https://creativecommons.org/licenses/by-nc-nd/4.0/)。
- SE、SW、STEP/STP 及其他三维模型仅允许个人或学校进行非商业学习、评审和研究；未经著作权人书面许可，不得用于加工、打样、制造、销售、量产、投标、产品交付、商业宣传、改作或再授权。
- 第三方组件、模板、元件库、外购件模型和工具仍受其原始许可证约束；本声明不扩大作者对第三方内容的权利。

商业使用、产品化、公司内部长期使用、转让或排他性授权需另行取得著作权人的书面许可。
