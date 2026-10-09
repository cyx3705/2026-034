# HistoryStrenua 代码组件

| 目录或文件 | 内容 |
| --- | --- |
| `HistoryStrenua/` | 模块本体（权威源）。产物 `HistoryStrenua.dll` 由宿主装载。根目录只放模块入口与身份（`HistoryStrenuaModule`、`ModuleInfo`、`StrenuaIdentity`）。 |
| `HistoryStrenua/OneKey/` `Hole/` `Drawing/` `Fillet/` `Tech/` `Check/` | 按页面上的类分（一键、孔、基础、倒圆、要求、检查），文件夹名就是指令名的第二段（`strenua.hole.*` 在 `Hole/`）：每类的快捷指令执行体与它们的 `*Planner`。 |
| `HistoryStrenua/Shared/` | 几类共用的：读视图与零件（`HoleScan`、`PartScan`）、读尺寸（`DimensionScan`、`DimensionGeometry`）、避障（`Clearance`）、删注解、视图朝向与模型面（`OutlineCoverage`）、对称判定。 |
| `HistoryStrenua/Page/` | 快捷指令登记表与执行器、页面描述、开关存档。 |
| `HistoryStrenua/SolidWorks/` | 附着运行中的 SolidWorks、运行时加载官方 Interop、按接口名调用并统一释放 COM 对象。 |
| `HistoryStrenua/eng/` | 发布候选打包脚本（本地用；正式候选由宿主管线构建）。 |
| `HistoryStrenua.Tests/` | 离线自动验证。可执行文件，全部 PASS 时退出码 0；不需要 SolidWorks。测试同样按类分文件（`HoleTests.cs`……），登记表与共用断言在 `TestKit.cs`。 |
| `Test-ProjectContract.ps1` | 只读项目合同检查：版本三处一致、模块身份、页面 owner、目录级别、失效链接、z-Publish 按字节入库。 |

`bin/`、`obj/` 是可重建生成物，不入库；发布候选写到根目录 `z-Publish/`。

模块只依赖 HistoryVulcan 5.1.2 的冻结接入面（`IModuleContext.Bus` 与 `RegisterCommands`），
引用宿主发布快照且 `Private=false`——部署时由宿主提供，包里不带副本。
SolidWorks Interop 不在编译期引用，运行时从本机安装目录加载。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\b-Code\Test-ProjectContract.ps1 -Instantiation
```
