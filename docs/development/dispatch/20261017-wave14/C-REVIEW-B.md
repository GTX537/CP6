# C：交叉审查 B 的租户资格 control 候选

> 唯一窗口 REVIEW-W13-B；一次合并 SPEC/QUALITY 审查，产品与原候选工作树只读。不要继续实施自己的publish候选。

固定候选 `e2bfcc0b9b34f94163e8b1a63463997b7abb35b5`，原交付 `D:/CP6-worktrees/wave13-b-tenant-control-20261017/docs/development/evidence/WP02-02/tenant-eligibility-control/DELIVERY.md`。原实现/测试c098e29a，基线4704592f。输入是原真实安装/profile/seal与service-token租户资格合同，不扩成全租户生命周期。

- [ ] 阅读固定候选完整5文件diff、原control安装/seal/helper/签发锁链及最新main相关变化；不修改任何候选工作树。
- [ ] 核对准确实例/database/OID/control身份/原profile/seal/role映射以及输入严格解析、参数化、字段容量/时间语义；不可缩小已接受合同或靠请求自报信任元数据。
- [ ] 核对缺行初始化竞争、Code唯一冲突、FOR UPDATE与发行FOR SHARE串行、其他租户并行、准确写集与单物理事务；明确并发superuser DDL等原排除范围，不借此引入不相关全库设计。
- [ ] 核对CAS/null/微秒/本地时间、ABA与非幂等限制、precommit rollback和commit后输出丢失的NOT_APPLIED/UNKNOWN分类；不能把inspect或CAS当请求结果查询或自动重试依据。
- [ ] 直接读取原7项真实PostgreSQL/CLI日志、TRX、源码及resource-stop来源。新回归/复跑仅针对确切缺口，默认不启动原55436实例或重跑旧套件；如需新probe，先给COORD具体风险和独立资源需求。
- [ ] 输出一份Ready to merge Yes/No，阻断项带准确行、可达场景、实际后果与最小建议；原未验收范围保持明确。纯风格建议不得变相阻止本范围交付。

只在新Handoff任务目录维护REVIEW.md，不复制原日志/清单。B仍是原候选实施者，本轮C没有修复写权限；收到问题由COORD登记修正窗口。审查通过也不改变P0普通集成暂停、完整WP02-02或业务验收状态。

## 执行与通信

读取根 AGENTS、[本轮入口](README.md)、[资源登记](dispatch.json)及唯一台账 `docs/development/roadmap.json`。本轮使用原四个用户对话，不另起代理实施同一任务。原 wave13 分支、工作树和报告保留；不能把本轮审查视为原候选已准入合并。

自己的新 Handoff 分支和工作树由 COORD 在本轮准入合并后建立，名称见 dispatch。先读取协调分支 `codex/dev-coordination-20261012` 中 `handoffs/CP6-DEV-20261012/waves/20261017-wave14.md` 的固定提交，核对实际 main。自己的回复只写 `handoffs/CP6-DEV-20261012/tasks/<本窗口ID>/`，不改 README/CURRENT/协调消息或他人文件。START 写入 `messages/<UTC>-start/MESSAGE.md`，列明原聊天身份、窗口、固定输入、允许范围和实际开始时间；安全 push、远端回读后即可连续工作，不等运输 ACK。A 的候选交付发 `messages/<UTC>-delivery/MESSAGE.md`，审查者只维护本窗口一份 REVIEW.md，最终消息引用其固定提交；不要复制原测试日志和哈希清单。

Git 一律带 `-c core.longpaths=true`。push/PR 前核对候选和受保护分支 workflow/依赖/事件及路径；相同内容复用已有分析，无新授权不得触发/重跑 Actions。Handoff 私有、Actions disabled、默认及自己的分支无 workflow 后才推送分支，不推 tag。普通文档不做产品测试。不要改全局配置、force-push、删除远端分支或使用管理员绕过。

PostgreSQL 是唯一新增数据库目标。项目精简规则优先：相关行为验证与一次合并的 SPEC/QUALITY 审查；修正只复查受影响部分，代码/依赖/环境未变就引用有效证据。没有因为固定 SHA、提交或文档变化重跑全仓的要求。数据库、并发、身份或安全风险保留必要专项检查。

执行者不写共享台账/生成页/四状态、不合 main、不关闭 Issue。COORD 统一协调范围、共享状态、正常 PR 合并、远端核对及满足条件后的 Issue 关闭。读到新的实质阻断立即通过 Handoff 报 COORD；禁止自行扩大公共写集或另开实施者。没有生产/DEV部署、既有数据库迁移/覆盖、正式发包或新 Actions 授权。Handoff 发布不自动唤醒聊天，也不等于真实 START 或验收。
