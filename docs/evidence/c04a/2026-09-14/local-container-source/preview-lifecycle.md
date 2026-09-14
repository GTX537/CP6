# 共享预览生命周期只读观察

2026-09-14 核对 `D:/CP6-local-publish/20260910`。未停启进程、访问 SQL、构建或执行迁移。详情与文件摘要见 `preview-lifecycle-observation.json`；私有 JSON 和命令行原文未导出。

当前父子链：supervisor 35812 → PreviewHost 12944 → Core 3128 / CRM 26884 / Web 33072 / dispatcher 23676。六项 PID 与 UTC 启动时间均匹配私有进程记录。Relay 在 PreviewHost 内运行。

正常停启入口（本次未执行）：

```powershell
& 'D:/CP6-local-publish/20260910/stop-preview.ps1'
# 等待六个原 PID+启动时间身份全部退出，并确认无新同入口进程及 supervisor 自有 transport 停止完成。
# 在完全停稳后，私下精确替换活动 run.json 的 crmApiAssembly 和 crmSourceSha；保留其他字段。
& 'D:/CP6-local-publish/20260910/start-preview.ps1'
& 'D:/CP6-local-publish/20260910/status-preview.ps1'
```

停止脚本仅写标志后返回。PreviewHost 依次停止 Web、dispatcher、CRM、Core、relay；子应用使用进程树 Kill/Wait，不能称为优雅排空。Supervisor 最后停止原 manifest 指定的自有 compose transport。单独停止 CRM 会引起整个共享预览退出。

恢复 Host 不调用 InitializeAsync，继续复用原 Core/CRM content root、Web 配置、密钥路径及 dispatcher。CRM 路径来自活动私有 run.json，不在 Host 中写死；crmSourceSha 仅是追踪元数据。路径替换本身不证明数据库兼容或允许实际启用。

停止标志不是持久禁止重启：start-preview 会删标志，直接 runner/Host 也先启动子进程才检查标志。两份私有 run.json 均引用旧 crm-api 路径；crm-api、recovery/bin/Release/net8.0、harness/bin/Release/net8.0 各有一套相同旧 CRM DLL/exe/runtimeconfig。需要在执行包中明确限制这些旧入口。recovery 副本也是 Host 的项目依赖，不能未经验证直接删除。现有 CrmWindowsLocalDrainGuard 提供绑定入口移走并留下持久失败标记的机制，但本次没有调用，也没有证明覆盖所有副本。

不要用 harness/invoke-published-smoke.ps1 或 PublishedSmoke.dll 恢复既有预览：它们会初始化、运行场景并清理测试资源。源码依据为 harness/Program.cs:10、LiveEnvironment.cs:245、invoke-published-smoke.ps1:159。

仅在数据库尚未提交退休/采用前，原 CRM 路径可作为应用入口恢复点。新 schema 已退休 crm 四张快照表后，不能将 0.1 DLL 改回当作恢复；Core 的 dbo.Crm_* 来源回退是另一条需要实时目标终止证明的协议，不解除 CRM 0.1 退休。参见 CRM02-LOCAL-ACTIVATION-PREPARATION.md:40、46。

未知项：本目录外的旧副本或自启动入口；当前 HTTP/SQL/transport 状态；实际停稳、重启与新发布依赖兼容。命令仅作为执行包输入，不表示已执行或已批准。
