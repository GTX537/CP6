# B：#253唯一修正，并接续C候选局部复查

原候选 `e2bfcc0b9b34f94163e8b1a63463997b7abb35b5`；原C [REVIEW-W13-B](https://github.com/GTX537/CP6.AI-Handoff/blob/5c9191f04bc0576fadabd1b059afc311f64e88a2/handoffs/CP6-DEV-20261012/tasks/REVIEW-W13-B/REVIEW.md)。固定Issue253是原文在DateTime反序列化前丢失精度的单一阻断。新产品工作树 `D:/CP6-worktrees/wave17-b-tenant-time-20261018`，分支 `codex/bug253-tenant-time-20261018`；COORD承接c098e29a+e2bfcc0b后交你，不从零重做control功能。

允许写：Program.cs、CLI README、ServiceTokenTenantDirectoryControlPostgresTests.cs和原单份DELIVERY（准确路径见dispatch）。Core control/profile/seal/helper/安装/模型/迁移和公共依赖只读。使用原JsonDocument在DateTime转换前验证Expected/Desired原文，或端点输入专用严格converter；不能根据转换后ticks判断曾被丢失的位数。

保留显式null、有限无offset本地时间及现有合法输入，拒绝无法无损表示为PG微秒的值；不能仅因文本超过6位就拒绝等值尾零，亦不得静默舍弃非零尾数。initialize/Expected/Desired均核对，禁止改变原CAS、事务、角色绑定、UNKNOWN或自动恢复边界。

必要回归使用正常已安装实例/身份及真实redirected CLI stdin，先复现`.12345601`或等价非零后位漏洞，再证明Expected/Desired拒绝且整行资格未变；合法微秒正对照须能成功。可复用原7项未变证据，只运行新增/受影响stdin与期限用例及必要编译。独占PostgreSQL55436，全新 `D:/CP6-local-test/WP02-02/bug253-<owner>/` 根；原fixture按marker/data_directory/PID安全启停，禁止连接5432/CP6DB/原已停止根。不得以其他无效字段提前拒绝或跳过数据库代替回归。

同一原DELIVERY补修正、失败→成功、日志来源与未验证范围，交原C/COORD固定候选。C只局部复查。你收到C的#251/#252固定交付后，在原 `REVIEW-W13-C/REVIEW.md` 接续一次修改范围复查，资料分支 `codex/dev-handoff-wave14-b-review-20261017`；不重复完整审查/START，不因为等待C而延迟自己的交付。产品补证消息仍用 `codex/dev-handoff-wp02-02` 的tasks/WP02-02。

## 共同执行规则

读取根AGENTS、本轮README/dispatch、最新roadmap和协调Handoff `handoffs/CP6-DEV-20261012/waves/20261018-wave17.md`。总控在准入合入后准备新产品worktree并在Handoff固定实际main/承接提交；开始前核对这些引用和工作树，没有最新main准备记录时先只读。旧候选/旧工作树保留，不在那里并写。Handoff发布不自动唤醒聊天。

继续同一任务的B/C/D不再做第二START或等待运输ACK；实际开始时一次PROGRESS记录本修正/补证开始即可。A的新审查任务实际开始发布一次START，固定交付到达后在同一报告接续，不重复START。所有人均不把派工/承接提交称作实际实现、修复通过或准入依赖已完成。

只验证受影响行为和故障边界，复用原有效证据及单份DELIVERY/REVIEW。失败原件保留，不因SHA/文档变化重跑。代码、依赖/生成资产或资源确需超出写集时，先交总控具体原因与路径。执行者不改共享台账/四状态、main、其他人报告或Issue状态；总控统一共享状态、逐任务正常PR及远端核对后关闭Issue。

push/PR前核对候选和受保护分支workflow、依赖及路径过滤，未变内容复用结论；不带tag、不启动Actions或修改分支保护。Handoff先核私有、Actions disabled、默认/资料分支无workflow，正常push并远端回读。无既有数据库/服务操作、迁移、正式发包或部署授权。后续数据库唯一PostgreSQL，不加SQL Server门禁。
