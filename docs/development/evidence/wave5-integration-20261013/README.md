# 第五轮协调交付

四项候选已完成实际消息接收、集中审查、必要修复、正常 PR 合并及远端 main 逐文件核对，登记 DONE 并关闭原写入窗口。累计 **20/375** 个细分任务，**S01 12/12**；这是基础任务计数，不是工时百分比。真实业务目标仍 **0/125**。

| 任务 | 已交付与本地验证 | 证据 |
|---|---|---|
| S01-12 | 828引用比较通过；122向量、19修订、六组消费者；原30+2比较器检查分别复用 | [远端合并](S01-12/remote-main.json)、[审查](S01-12/review.md) |
| WP02-27 | 新Release/Schema127、架构98、七个本地包检查通过；原验签7包按旧时点复用 | [远端合并](WP02-27/remote-main.json)、[审查](WP02-27/review.md) |
| WP02-07 | 原136独立用例与4+2定向重复证据复用；同事务Inbox/原结果库能力 | [远端合并](WP02-07/remote-main.json)、[审查](WP02-07/review.md) |
| WP02-11 | 修复两个HTTP边界；新HttpV2198/198通过，原98+98按未变范围复用 | [远端合并](WP02-11/remote-main.json)、[审查](WP02-11/review.md) |

## 本轮修正

WP02-27 的两项架构断言使用精确资产列表，原列表缺少本任务已获准新增的 capabilities/v1。COORD 在[窄化窗口决定](coordinator-window-decisions.json)中只批准补齐两个预期数组；原依赖、资产归属和包边界断言全部保留。原96通过/2失败记录不改。另发现单字符相对路径 `x` 在 C# 可接受、Schema 却拒绝，登记 Platform Issue60；真实 RED 后修正 Schema 并补充边界回归，新本地包归档重新构建核对。原包验签与新本地候选身份分别保留，新包仍 NOT_PUBLISHED / NOT_FORMALLY_TRUSTED。

WP02-11 发现收到401/403/409/422后正文故障可能误重试，以及权限适配器故障丢失 typed outcome，分别登记 Platform Issue62/63。两个缺陷各有修复分支和真实 RED/GREEN，最终在同一候选任务集中整合、复核。保留 NotSent/Unknown、原操作身份、调用方取消与截止时间优先级；实际 Owner 接入仍未验收。初审 REQUEST_CHANGES、原失败及最终定向复查均保留，不将最初候选写作无问题通过。关闭凭据见[Issues](issues-closed.json)。

## 证据范围

[实际接收](intake.json)固定四任务 START/QUESTION/DELIVERY 的提交、路径与字节哈希；worker_ack_at 是原消息时间，coordinator_received_at 是本次真实接收时间。WP02-27 QUESTION 文件时间标记与传输提交时间的区别保持，不回填过去的协调确认。

[验证汇总](validation-summary.json)明确新执行与复用。S01-12 新比较828引用、122向量、16profile、56组三端词法断言；原30项比较器检查与后续2项回归是分别执行。19修订仍18 PARTIAL、1 NOT_COVERED，完整业务 NOT_RUN；六组消费者共56项后续责任任务保持。原S07 DLL哈希和S05逐项reporter缺口保留。

Inbox 原18单元+20真实隔离PG+98架构是136个独立测试，后续4+2是重复定向运行。源码、PDB、依赖、产物及真实TRX核对后复用。原60项业务AC全部NOT_RUN；所有三组专属PG资源已停止，协调阶段不重开数据库。HTTP 与 Release 的修复运行及其原证据复用详见汇总和对应修复报告。

三项 Platform 合入后，只对共同主线补做[架构冒烟](combined-architecture-validation.json)，原因是本轮增加包资产、修改对应共享门禁并汇合三个独立候选；它不证明完整业务组合或真实宿主已经采用。没有重跑未受影响的原PG或全仓业务测试。各任务 workflow-pre-merge.json 检查当时候选及受保护主线的事件、过滤和相关依赖，无 Actions 运行、取消或触发配置更改。

## 下一步边界

S01 基础合同十二项已收口，业务 Owner/writer/HTTP/存储/页面采用由后续任务负责。**WP02-02 仍 BLOCKED**：Main 当前信任的 Platform [0.10.2] 不包含新能力，需先确定受信任、不可变的正式包或另行审查的可复现源码采用方案，并核准17项PG验收。WP02-27 的校验器及本地测试包不自动解除此门。

125目标、247运行NOT_RUN、41历史REFERENCE_ONLY、123/125原件盘点、ITER/REL不可恢复缺口及WP05 PROPOSED均保持。本次没有分配下一轮、正式发包、迁移或部署；原候选分支和工作区保留。Handoff 回执另行发布并回读，发布不会唤醒休眠对话。本地开发手册随最终 main 同步。
