# BUG751 — ITER阻塞应用的原授权连续性

[Issue751](https://github.com/GTX537/CP6/issues/751)，Main基线`6789d33ae78da74781ea6cda3441f81e29a21cfc`，固定DEV1、`codex/bug-751-iteration-application-grant`，COORD单人。由[W301原卡审计](../W301-01/DELIVERY.md)发现并立即优先修复。

ITER普通BLOCKED应用重试采用原回执的实际人、委托人及准确业务grant版本；替换/修订grant或改变本人/受托方式必须显式再授权。

根因是ApplyCore仅比较ActionActorId，BusinessAct重新选择当前grant，未采用原receipt.audit_json中已保存的behalf/grantBody。现在只对普通BLOCKED继续读取该不可变原审计，比较原principal并把原grant作为准确选择条件；旧grant撤销或版本变化会明确拒绝，不会换用别的新grant。本人/受托方式改变同样要求显式新请求。实际人改变继续沿既有专门错误拒绝。

早期W200版本没有委派字段，已从其源提交`3afb844bb17cae0c320d72e031c62ec6f8f98990`核对其审计结构，合法含义是原实际人的本人请求。读取兼容该格式，不迁移或重写历史。当前实际授权/字段/Scope、业务条件和最终fence仍在原权威入口检查。

四项PG18原步骤修复前均失败（旧代码未拒绝换授权），修复后通过；覆盖grant撤销后替换、同grant版本修订、受托转本人、本人转受托，每项验证新明确授权连续回执、相同新授权后续真正APPLIED及旧回执/原结果不变。替换grant场景通过实际Main Program认证HTTP验证普通403与显式再授权成功。既有不同实际人必须显式再授权的PG回归通过。

单一实际证据索引`D:/CP6-local-test/bug751/foundation/final-validation.json`保存RED/GREEN JSON、TRX及日志。真实PostgreSQL18、Main内部Owner/Workflow与认证HTTP执行；用户、项目、问题、审批、授权与文件为明确模拟业务输入。旧Chrome/UI未变证据按W278复用，本窗没有新跑浏览器或全仓回归。数据授权变更不是生产部署。

一次完整diff集中审查：原审计已由Owner同事务持久化，读取直接信任内部合同，不增加哈希/序列化往返或资格表。普通重试只沿原principal与准确grant选择，仍由当前授权和最终fence校验；显式再授权、原操作读取和终态返回路径保持。无数据库迁移、公共wire、UI、依赖或workflow改动。

没有检查或修改既有环境；历史已接受的换授权回执保持不可变，不追溯伪造新授权，也不声称已自动纠正历史操作。业务原卡的最终核销在远端采用后由下一总控窗口引用本报告与W301审计；本提交不把未合入修复当远端完成。

原37530DONE/2IN_PROGRESS/343PLANNED、原59余53及125/247本提交保持原状态。24小时目标继续，下一总控核销WP12-19后W302隐藏字段受控处置。Issue在远端main准确采用核对后关闭。

分支处置、工作区状态和官方finish席位释放以`C:/Users/tt/CP6-planning-reports/20261017-foundation/bug751-final-verification.json`核销。无需新增Handoff席位，下一总控窗口统一同步。无Actions触发/取消、触发配置改变、部署或数据库迁移。
