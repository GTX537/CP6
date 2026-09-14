# 实际来源只读检查交付证据

本任务交付独立 `inspect-actual` API/CLI，源码 `4f043e2e8f78b6a323609661be804a3823e363b9`，从远端 main `6c11dbb6` 创建独立分支。原本地 main 中两项无关提交及根工作区未提交内容保留。[操作与边界](../../../../../tools/CP6.Crm.SourceFence/ACTUAL-INSPECTION.md)、[验证索引](local-validation.json)、[执行原件](local-execution.zip)。

新增 11 个不同自动化场景分三批通过，零跳过；第二批只补新边界并重测被改为普通库名的场景，第三批只验证新增 view 摘要覆盖。原 30/10 项源端围栏测试、39 项真实恢复生命周期、C01/C02/C03 等已通过且未受影响的结果按原来源复用。最初缺少新 API 的编译失败保留，不计通过。

集中审查唯一 P2 是 CLR 对象的方法绑定未进入摘要；已加入 `sys.assembly_modules`，并捕获全部 SQL modules（含 view）。修复定向复核关闭；没有启用全局 CLR 或执行 CLR 运行时场景。构建 CLI 测试执行测试输出程序集，后续独立 Release 发布另在真实源库执行三项检查，两者分别记录。

## 实际现场结果

[脱敏摘要](actual-source-summary.json)绑定最终独立发布的逐文件长度/摘要。实际 `CP6DB` 的 20 张旧 CRM 表仍全空，来源 scope 两次一致；原 `freeze` 命令对实际库名在连接前返回 `C04A_DATABASE_IDENTITY`。检查未安装控制 schema，原配置摘要未变。[清理观察](cleanup.json)确认自有测试数据库及临时登录为零，真实来源控制 schema 为零。

检查观察到实例中 5 个启用的 sysadmin 主体、1 个启用 SQL Agent 作业、53 个其他用户会话；当时没有会话以 CP6DB 为当前库。这不能证明该来源无写入方，其他库会话可跨库访问，作业启用也不等同调度器正在运行。原始主体、SID、host/program 清单保留本机受控检查记录，没有收入公共归档；模块和作业命令正文从未输出。

PowerShell 辅助显示曾把有真实内容的 ordered dictionary 直接交给 Select-Object，导致控制台打印 null；存盘报告回读正常。已修正显示与 UTC 序列化，未重复执行已通过 SQL。原始 CLI 输出及错误记录保留本机，公开摘要时间取原始 UTC。

## 后续范围

继续分类真实应用/服务/SQL Agent 与特权身份，尤其包括动态 GDPR tenant purge 路径；再接实际冻结、排空、目标首写及路由恢复。摘要匹配不是批准，也不是完整 schema/writer 验收或持续围栏。CRM02 0.2 的变化字节仍须按实际执行范围签收；C04A/C04B 保留原前置。整体目标 **60%（3/5）**，本次只完成实际来源检查组件。没有执行生产部署、修改业务启动或触发 GitHub Actions。
