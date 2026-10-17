# B：交叉审查 C 的实际 publish 身份候选

> 唯一窗口 REVIEW-W13-C；一次合并 SPEC/QUALITY 审查，产品与原候选工作树只读。不要继续实施自己的租户control候选。

固定候选 `b9f3f30b5bf079074ecfa404db618a20d9ee7a84`，原交付 `D:/CP6-worktrees/wave13-c-published-host-20261017/docs/development/evidence/WP02-28/published-host/DELIVERY.md`，原产品构建输入4704592f。先读 docs/devops/README.md、docs/client/r2/README.md、原WP02-28窗口和已接受正式包来源；新开发仍PostgreSQL，不扩大旧R2历史门禁为当前本地工具的交付条件。

- [ ] 读取固定候选相对4704592f的完整diff；与最新main检查相关代码/依赖变化，不改他人分支，不用merge或cherry-pick取得审查上下文。
- [ ] 核对 published_artifact.py / verify_published_host.py / test_published_host.py：实际publish与复制bin的区别、独立正式包期望、收据pin/完整闭包/source绑定、loader环境、准确进程/监听者和HTTP身份、失败资源清理、Unknown是否诚实。只按既定本地信任边界评估，不臆造生产签名要求。
- [ ] 直接核对原收据、实际host-results、负向记录、6项工具回归及resource-stop与报告一致；旧36项/授权矩阵按真实输入变化判断复用。候选SHA不同不要求重建原publish。证据显示4704592f构建就如实记该SHA，不能改称最新main宿主已验。
- [ ] 给出Ready to merge Yes/No，阻断需准确文件/行、触发路径、实际后果及最小修正；区分非阻断建议和原NOT_RUN。只有发现明确未覆盖风险才提出最小探针，不默认重跑PG、publish或宿主；需要新运行资源时先交COORD安排。

只维护新Handoff任务目录的一份REVIEW.md，记录固定候选、当前main、已读原证据、复用理由、未运行范围和结论。审查通过不授权普通集成；P0处理后的合并与状态由COORD决定。不要自行修复C代码、给自己的原候选签发审查结论或再派重复审查者。

## 执行与通信

读取根 AGENTS、[本轮入口](README.md)、[资源登记](dispatch.json)及唯一台账 `docs/development/roadmap.json`。本轮使用原四个用户对话，不另起代理实施同一任务。原 wave13 分支、工作树和报告保留；不能把本轮审查视为原候选已准入合并。

自己的新 Handoff 分支和工作树由 COORD 在本轮准入合并后建立，名称见 dispatch。先读取协调分支 `codex/dev-coordination-20261012` 中 `handoffs/CP6-DEV-20261012/waves/20261017-wave14.md` 的固定提交，核对实际 main。自己的回复只写 `handoffs/CP6-DEV-20261012/tasks/<本窗口ID>/`，不改 README/CURRENT/协调消息或他人文件。START 写入 `messages/<UTC>-start/MESSAGE.md`，列明原聊天身份、窗口、固定输入、允许范围和实际开始时间；安全 push、远端回读后即可连续工作，不等运输 ACK。A 的候选交付发 `messages/<UTC>-delivery/MESSAGE.md`，审查者只维护本窗口一份 REVIEW.md，最终消息引用其固定提交；不要复制原测试日志和哈希清单。

Git 一律带 `-c core.longpaths=true`。push/PR 前核对候选和受保护分支 workflow/依赖/事件及路径；相同内容复用已有分析，无新授权不得触发/重跑 Actions。Handoff 私有、Actions disabled、默认及自己的分支无 workflow 后才推送分支，不推 tag。普通文档不做产品测试。不要改全局配置、force-push、删除远端分支或使用管理员绕过。

PostgreSQL 是唯一新增数据库目标。项目精简规则优先：相关行为验证与一次合并的 SPEC/QUALITY 审查；修正只复查受影响部分，代码/依赖/环境未变就引用有效证据。没有因为固定 SHA、提交或文档变化重跑全仓的要求。数据库、并发、身份或安全风险保留必要专项检查。

执行者不写共享台账/生成页/四状态、不合 main、不关闭 Issue。COORD 统一协调范围、共享状态、正常 PR 合并、远端核对及满足条件后的 Issue 关闭。读到新的实质阻断立即通过 Handoff 报 COORD；禁止自行扩大公共写集或另开实施者。没有生产/DEV部署、既有数据库迁移/覆盖、正式发包或新 Actions 授权。Handoff 发布不自动唤醒聊天，也不等于真实 START 或验收。
