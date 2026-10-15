# WP02-02 第七轮诊断边界与失败分析

本目录只交付 PREPARATION_ONLY 证据。WP02-02 仍为 BLOCKED / NOT_CHECKED；Main consumption 为 NOT_RUN，formal package trust 为 BLOCKED；17 PGT 不执行、不更新。最终运行结果及证据索引见本目录交付报告。

## 原四裸 DLL 探针的 MSB3277

第六轮四裸 DLL 探针没有恢复实际 Platform 项目的完整传递依赖，使用框架 DiagnosticSource 8 与依赖 DiagnosticSource 10 的 Platform DLL，产生 MSB3277。此结果只证明那个探针的依赖闭包不完整。Main 固定源原锁已经记录 System.Diagnostics.DiagnosticSource 10.0.8；不能以探针冲突为依据将 Main 降级，也不能据此证明 Main 已采用新版 Platform。

本轮从两个固定 Git 对象树生成独立快照，保留真实 ProjectReference 闭包和外部包源/签名配置，分别恢复并构建 WebApi、DatabaseCompatibility.Probe 和私有诊断宿主。每次实际 build 都带 `-warnaserror:MSB3277`。实际 assets、deps、私有锁差异和包归档身份用于判断依赖责任；没有覆盖的路径保持 Unknown。

## 第一次真实构建：Windows 长路径

Owner `65a0ae32a96d4f2f92d92f623849b34d` 的 WebApi restore 退出 0；期间发生网络超时，NuGet 自身重试后恢复成功。随后 build 退出 1，包含 MSB3106 和 DataProtection 类型无法解析错误。证据扫描还在超过常规 Windows 路径限制的 HTTP cache 文件上遇到 WinError 3，未能继续另外两个独立构建根；它们不得记为已执行。

在只读诊断中，同一 DLL 的普通路径检查返回不存在，Windows `\\?\` 扩展路径能读取真实文件：

| DLL | 路径字符数 | 实际字节数 | SHA-256 |
|---|---:|---:|---|
| Microsoft.Extensions.DependencyInjection.Abstractions.dll | 260 | 64848 | `392e9b29bd2fda4426de916c7e3ed71d3e10984599d83ad96f4bbd6a13f0e7f6` |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore.dll | 264 | 24400 | `13f4fd79adad4c0b4bbd0e6d65dd36bf9998217a707fe99c0000b1883d82d0af` |

独立诊断 owner 为 `1b0e3e6d489d4ce19f58c15f2e35e01c`，报告 `windows-path-diagnosis.json` SHA-256 为 `b6dff491aa0f846ac73a5aec0f3652a1c4e139d151db26ba307c4d36420320b6`。这些观察说明 DLL 已下载；单凭上述编译错误不能认定固定生产源码缺少 API。原失败 owner、命令、日志及输出全部保留。

修正范围仅限任务私有工具对 Windows 长路径的处理，在新 owner 内重新生成和恢复。禁止借用其他缓存、创建路径重定向、修改机器全局长路径开关，或改变 NuGet 信任规则来绕过问题。后续实际运行结果决定修正是否有效。

## 第二次真实尝试：包构建路径与清理预算

Owner `84981a57453d4841adb6cb0e02a52875` 使用短目录 `n`，但给 NuGet/MSBuild 传入 `\\?\` 扩展路径。两个 Main restore 均退出 1：Scriban 7.2.4 的 build 导入路径含重复反斜杠，在扩展路径语法下无效。私有 Probe restore/build 均退出 0。三根结果分别保存，没有把部分成功记为总体通过。

对首次 owner 的真实 assets 进行只读分析后，1,298 条实际选中资产路径映射到普通 `owner/n`，最长 254 字符，无超过 259 的路径。此前更长的示例来自未选中的目标框架或资源，不能据此断言实际图必须使用扩展 NuGet 参数。分析保存在 owner `48e1c85a9c314596a38d5da6732652de`，报告 SHA-256 `1cd89d9a553ee858c526e600ba54a4678a7541301a0fd4fb020cd9c855db17ea`。后续 runner 为 NuGet/MSBuild 使用普通短路径，Python 文件扫描仍使用扩展路径。

第二次私有宿主实际完成 HTTP 200/401/403/503、上下文执行器和 SDK trace/metric/log 检查，但总结果为 FAIL：停止和释放均成功后，端口拒绝连接检查在 2 秒预算内取消。独立后续检查在 2.0595 秒取得真实 Windows `10061 / ConnectionRefused`，保存在 owner `2d6bb5ffd97842cb8237a50323dff840`。原 runtime FAIL 不改写；新探针将检查预算改为 10 秒并记录耗时和 SocketError，仍只接受真实 ConnectionRefused，不把超时当关闭。

该次报告的重复 CLI 验证退出 2，原报告 hash 前后均为 `a9f0b53602b4f698ffa9376aea1ac9d014dcb2e8dae368e019a9a50b907f7e74`，没有再次启动宿主；证据 owner 为 `4b9a455498bf44108ccb1e79a8338156`。运行策略对该次总体 UNKNOWN 构建保持 REJECTED。

## 身份与测试的实际含义

`capture_build.py` 在私有宿主运行前读取 PE 元数据，独立记录 DLL 的 SHA-256、版本与 MVID；它不加载或执行这些程序集。运行时报告再观察实际加载位置和身份，并由 `runtime_policy.py` 对照。此清单是本地构建观察，不是签名授权文件。

策略反例使用明确标注的合成文件与报告，验证缺依赖、错版本、错来源、重复程序集、Unknown、跨 owner/快照混用及清理失败会被拒绝。这些反例不能冒充真实认证、正式信任验证或业务集成验收。真实 HTTP、上下文与 OpenTelemetry SDK 信号另以实际宿主报告为准，认证和授权 authority 仍明确标为 synthetic。

新源码编译及私有宿主通过也不能使历史 `[0.10.2]`、未来候选版本、WP02-28 或 Main 采用获得准入。Main Program、数据库连接、17 PGT 和生产发布均在本窗口之外。
