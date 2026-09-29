# HistoryStrenua 代码组件

| 目录或文件 | 内容 |
| --- | --- |
| `HistoryStrenua/` | 模块本体（权威源）。产物 `HistoryStrenua.dll` 由宿主装载。 |
| `HistoryStrenua/SolidWorks/` | 附着运行中的 SolidWorks、运行时加载官方 Interop、按接口名调用并统一释放 COM 对象。 |
| `HistoryStrenua/eng/` | 发布候选打包脚本（本地用；正式候选由宿主管线构建）。 |
| `HistoryStrenua.Tests/` | 离线自动验证。可执行文件，全部 PASS 时退出码 0；不需要 SolidWorks。 |
| `Test-ProjectContract.ps1` | 只读项目合同检查：版本三处一致、模块身份、页面 owner、目录级别、失效链接、z-Publish 按字节入库。 |

`bin/`、`obj/` 是可重建生成物，不入库；发布候选写到根目录 `z-Publish/`。

模块只依赖 HistoryVulcan 5.1.2 的冻结接入面（`IModuleContext.Bus` 与 `RegisterCommands`），
引用宿主发布快照且 `Private=false`——部署时由宿主提供，包里不带副本。
SolidWorks Interop 不在编译期引用，运行时从本机安装目录加载。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\b-Code\Test-ProjectContract.ps1 -Instantiation
```
