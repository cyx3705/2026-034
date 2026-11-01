# HistoryStrenua 代码组件

| 目录或文件 | 内容 |
| --- | --- |
| `HistoryStrenua/` | 模块本体（权威源）。产物 `HistoryStrenua.dll` 由宿主装载。根目录只放模块入口与身份（`HistoryStrenuaModule`、`ModuleInfo`、`StrenuaIdentity`）。 |
| `HistoryStrenua/Planning/` | 纯规则与数据（不碰 SolidWorks）：几何类型、各 `*Planner`、SVG 写法。 |
| `HistoryStrenua/SolidWorks/` | 附着运行中的 SolidWorks、运行时加载官方 Interop、按接口名调用并统一释放 COM 对象。 |
| `HistoryStrenua/Runtime/` | 指令切面：指令定义、上下文、参数、执行器、开关、笔记。 |
| `HistoryStrenua/Sheet/` | 几类共用的读图与改图动作；`Feedback/` 是 SVG、截图、本步反馈。 |
| `HistoryStrenua/Commands/<类>/` | 指令执行体，文件夹名就是指令名第二段（`strenua.hole.*` 在 `Commands/Hole/`）。 |
| `HistoryStrenua/Page/` | 登记表、页面描述、`strenua.quick.*`、页面协议与开关指令。 |
| `HistoryStrenua/eng/` | 发布候选打包脚本（本地用；正式候选由宿主管线构建）。 |
| `HistoryStrenua.Tests/` | 离线自动验证。可执行文件，全部 PASS 时退出码 0；不需要 SolidWorks。测试同样按类分文件（`HoleTests.cs`……），登记表与共用断言在 `TestKit.cs`，分层门禁在 `ArchitectureTests.cs`（分层规则见 `b-Office/现行约定.md`）。 |
| `Test-ProjectContract.ps1` | 只读项目合同检查：版本三处一致、模块身份、页面 owner、目录级别、失效链接、z-Publish 按字节入库。 |

`bin/`、`obj/` 是可重建生成物，不入库；发布候选写到根目录 `z-Publish/`。

模块只依赖 HistoryVulcan 5.1.2 的冻结接入面（`IModuleContext.Bus` 与 `RegisterCommands`），
引用宿主发布快照且 `Private=false`——部署时由宿主提供，包里不带副本。
SolidWorks Interop 不在编译期引用，运行时从本机安装目录加载。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\b-Code\Test-ProjectContract.ps1 -Instantiation
```
