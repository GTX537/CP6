# 项目当前状态

## 2026-11-13 UTC · W534 财务、采购及计划主数据写入

资产分类、手工供应商价表、品目策略的 7 条写者接入共同根并实测。正式 PostgreSQL 26/27，PGT-017 仍有余项。 [本任务](../development/evidence/W534-01/DELIVERY.md)

## 2026-11-13 UTC · W533 旧单任务审批与撤权

旧审批共用 PostgreSQL 事务根，真实 IAM 撤权两种提交顺序通过。PGT-017 仍待核销其他写者，正式 PostgreSQL 26/27，原询盘 60/60。 [本任务](../development/evidence/W533-01/DELIVERY.md)

## 2026-11-13 UTC · W532 旧菜单和 2FA 权威写入

6 个入口采用 PostgreSQL 共同事务并完成实际撤权回归。PGT-017 尚有其他写者缺口，正式 PostgreSQL 26/27，原询盘 60/60。 [本任务](../development/evidence/W532-01/DELIVERY.md)

## 2026-11-13 UTC · W531 PGT-017 写入口核对

已实测库存暂停、订单撤权和日历写入的两种提交顺序，修复 9 个旧工作流写入口。PGT-017 仍待核销已定位旁路，正式 PostgreSQL 26 PASS / 1 NOT_RUN，原询盘 60/60。 [本任务](../development/evidence/W531-01/DELIVERY.md)

## 2026-11-13 UTC · W530 原生合同与精度

PGT-025 PASS，正式 PostgreSQL 26 PASS / 1 NOT_RUN，原询盘 60/60。CRM 真实配置 Owner 在物理租户分区写入，并从共享回执恢复原 201、UTF-8 和 ETag。 [本任务](../development/evidence/W530-01/DELIVERY.md)

## 2026-11-13 UTC · W529 后台重试与平台管理权限

PGT-011 PASS，正式 PostgreSQL 25 PASS / 2 NOT_RUN，原询盘 60/60。Main 在本机 PostgreSQL 18.6 实测 A/B 的已认领原任务、当前服务与原申请人、暂停后原回执、管理员和模拟会话权限、Stock 技术派送。 [本任务](../development/evidence/W529-01/DELIVERY.md)

## 2026-11-13 UTC · W528 混合租户模型与回退

PGT-024 PASS，正式 PostgreSQL 24 PASS / 3 NOT_RUN，原询盘 60/60。Main 同库 A v2、B v1 实测，缺共享版本和旧写者拒绝。回退后 A 可读、B 可读写，数据库保留前向迁移。 [本任务](../development/evidence/W528-01/DELIVERY.md)

## 2026-11-13 UTC · W527 旧备份与消息恢复

PGT-022 PASS，正式 PostgreSQL 23 PASS / 4 NOT_RUN，原询盘 60/60。Stock 同发布者终态队列完成纪元 1→2，旧包与 UNKNOWN 恢复均实测回滚，现行撤权和 B 原件保留。 [本任务](../development/evidence/W527-01/DELIVERY.md)

## 2026-11-13 UTC · W526 同库恢复验收完成

PGT-021 PASS；正式 PostgreSQL 22 PASS / 5 NOT_RUN，原询盘60/60。A 原业务、当前权限和消息事实保持，B 并发读写通过。已正式采用签名 Platform 0.10.4，Stock 原 Pending 正常派送；18项相关测试通过。生产部署未执行。 [本任务](../development/evidence/W526-01/DELIVERY.md)

## 2026-11-13 UTC · W525 出库原件与候选 RMA 写入

出库头、明细、原 RowVersion 和四份 v11 投影对齐。候选普通角色实测草稿、提交、同键回放和认证锁等待，源端 B 保持自己的访问。正式 PostgreSQL 21 PASS / 6 NOT_RUN，原询盘 60/60。 [本任务](../development/evidence/W525-01/DELIVERY.md)

## 2026-11-13 UTC · W524 候选身份处置重施与 RMA 绑定

源处置推进隐私纪元，候选先关闭，再批准后撤销原候选会话。A 专用候选 RMA 绑定、B 源读取及 B 缺绑定拒绝通过。正式 PostgreSQL 21 PASS / 6 NOT_RUN，原询盘 60/60。 [本任务](../development/evidence/W524-01/DELIVERY.md)

## 2026-11-13 UTC · W523 现行 IAM 和投递对账

源端普通 IAM 实际拒绝已签发访问和刷新凭据，B 保持。ERP 同键识别当前 Published，Stock 源端遇 #844，候选保持零派送。正式 PostgreSQL 21 PASS / 6 NOT_RUN，原询盘 60/60。 [本任务](../development/evidence/W523-01/DELIVERY.md)

## 2026-11-13 UTC · W522 组合候选受控本地准入

源端实际独立批准后普通角色读取原 RMA，源隐私纪元更新后原请求被拒，组合回执和队列保持。正式 PostgreSQL 21 PASS / 6 NOT_RUN，原询盘 60/60。 [本任务](../development/evidence/W522-01/DELIVERY.md)

## 2026-11-13 UTC · W521 同一关闭候选四域恢复

Stock、RMA、ERP Partner 和 FileContent 在同一原生候选恢复，五份原回执和文件原字节通过，普通 B 跨租读取 42501。正式 PostgreSQL 21 PASS / 6 NOT_RUN，原询盘 60/60。 [本任务](../development/evidence/W521-01/DELIVERY.md)

## 2026-11-13 UTC · W520 Stock 原调整关闭恢复

单笔 Stock ADJ 四张原生表和原队列同源导出，并在 PostgreSQL 一个事务导入。保留原流水、回执和 B 数据，关闭候选 worker 拒绝派送。正式 PostgreSQL 21 PASS / 6 NOT_RUN，原询盘 60/60。 [本任务](../development/evidence/W520-01/DELIVERY.md)

## 2026-11-13 UTC · W519 ERP 订单失败原件恢复

真实 ERP Owner 原图在 PostgreSQL 关闭候选中按原键和原字节回读，B 保持。正式 PostgreSQL 21 PASS / 6 NOT_RUN，原询盘验收 60/60。 [本任务](../development/evidence/W519-01/DELIVERY.md)

## 2026-11-13 UTC · W518 PGT-024 同库真实新旧宿主

旧宿主实际读取 B、新宿主实际写入 A。旧写入被拒且无普通业务 SQL，停新版后旧版能读原数据，四份迁移历史保持。正式 PostgreSQL 21 PASS / 6 NOT_RUN。 [本任务](../development/evidence/W518-01/DELIVERY.md)

## 2026-11-13 UTC · W517 PGT-021 同源快照与普通 B 并发

实际普通 B 在 A 快照持有期间写入原草稿、版本及回执。A 归档和独立关闭暂存核验通过。正式 PostgreSQL 21 PASS / 6 NOT_RUN。 [本任务](../development/evidence/W517-01/DELIVERY.md)

## 2026-11-12 UTC · W516 正式 PGT-026 认证与公共路由

Main 认证、普通角色目录限制、实际租户 schema 启用及 CRM 共享公开路由已采用。正式 PostgreSQL 21 PASS / 6 NOT_RUN。 [本任务](../development/evidence/W516-01/DELIVERY.md)

## 2026-11-12 UTC · W514 正式 PGT-004 可信入口

Main 的缺失/伪造身份与存活探针、CRM 的地区、正文、查询及外部请求头均按真实证据采用。正式 PostgreSQL 20 PASS / 7 NOT_RUN。 [本任务](../development/evidence/W514-01/DELIVERY.md)

## 2026-11-12 UTC · W515 PGT-026 Main 存储切换

已实测实际 PostgreSQL 共享空布局到已启用租户 schema 的启动切换。旧令牌原撤销继续生效，正常身份可读取。 [本任务](../development/evidence/W515-01/DELIVERY.md)

## 2026-11-12 UTC · W513 PGT-004 CRM 可信入口

未登记地区实测 403、零业务连接、零权限请求。正式 PostgreSQL 19 PASS / 8 NOT_RUN。 [本任务](../development/evidence/W513-01/DELIVERY.md)

## 2026-11-12 UTC · W512 正式 PGT-013 Owner 原子性

真实询盘配置 Owner 正向同事务、四阶段 42501 回滚及异连接拒绝已采用。正式 PostgreSQL 19 PASS / 8 NOT_RUN。 [本任务](../development/evidence/W512-01/DELIVERY.md)

## 2026-11-12 UTC · W510 正式 PGT-016 真实提交不明

实际询盘 Owner 提交回包中断、客户端取消、锁冲突和原结果恢复已采用。正式 PostgreSQL 18 PASS / 9 NOT_RUN。 [本任务](../development/evidence/W510-01/DELIVERY.md)

## 2026-11-12 UTC · W511 Main 认证目录

实际 Main Kestrel、OIDC 和 PostgreSQL 验证同名租户/用户、未知 slug、跨租拒绝及全局刷新源绑定。PGT-026 仍有共享路由和存储绑定切换。 [本任务](../development/evidence/W511-01/DELIVERY.md)

## 2026-11-12 UTC · W509 正式 PGT-014 CRM 身份接收

采用 PGT-014。实际普通角色的身份接收、锁序、Published/DRIFT 和 ACK 通过，正式 PostgreSQL 17 PASS / 10 NOT_RUN。 [本任务](../development/evidence/W509-01/DELIVERY.md)

## 2026-11-12 UTC · W508 询盘正式 PostgreSQL 盘点与 Main 可信入口

原询盘 60/60。正式 PostgreSQL 16 PASS / 11 NOT_RUN。Main liveness 零业务库连接及可信入口四类拒绝实测，PGT-004 收录定向证据。 [本任务](../development/evidence/W508-01/DELIVERY.md)

## 2026-11-12 UTC · W507 询盘错误合同与恢复

CRM #198 已采用。精确 Problem、零写及显式浏览器恢复通过。采纳 AC-INQ-034，原询盘 60 PASS / 0 NOT_RUN。 [本任务](../development/evidence/W507-01/DELIVERY.md)

## 2026-11-12 UTC · W506 询盘租户外键与事务行

CRM #197 已采用。目录、普通角色负例、历史政策激活零写和真实下载事务通过。采纳 AC-INQ-047/057，原询盘 59 PASS / 1 NOT_RUN。 [本任务](../development/evidence/W506-01/DELIVERY.md)

## 2026-11-12 UTC · W505 询盘锁序与有界恢复

CRM #195 已采用。真实 PostgreSQL 三类已确认回滚重新获取当前权限并完整重试，耗尽零写，原键可恢复。采纳 AC-INQ-019，原询盘 57 PASS / 3 NOT_RUN。 [本任务](../development/evidence/W505-01/DELIVERY.md)

## 2026-11-12 UTC · W504 三维限流与政策

CRM #193 已采用。三维 429、原键恢复和同键冲突实测，旧新政策各驱动实际状态及期限。采纳 AC-INQ-002/027，原询盘 56 PASS / 4 NOT_RUN。 [本任务](../development/evidence/W504-01/DELIVERY.md)

## 2026-11-12 UTC · W503 H01 下游与授权边界

CRM #192 已采用。H01 生成 New Lead，实际无商务交接或额外效果。七种签名和字段拒绝均为 403，持久零写。采纳 AC-INQ-031/032，原询盘 54 PASS / 6 NOT_RUN。 [本任务](../development/evidence/W503-01/DELIVERY.md)

## 2026-11-12 UTC · W502 审核赢家事件

CRM #191 已采用。导出后放行及拒绝的原生 Outbox 版本为 1、2、3，显式旧守卫升级和原结果恢复通过。采纳 AC-INQ-040，原询盘 52 PASS / 8 NOT_RUN。 [本任务](../development/evidence/W502-01/DELIVERY.md)

## 2026-11-12 UTC · W501 合成夹具完整性

具名派生修订的 28 个夹具家族、136 步通过静态验证，两条负例实际失败并更新回执。采纳 AC-INQ-035/060，原询盘 51 PASS / 9 NOT_RUN。 [本任务](../development/evidence/W501-01/DELIVERY.md)

## 2026-11-12 UTC · W500 询盘读取防枚举

CRM #189 已合入 `58ececc8`。真实 HTTP/PostgreSQL 同形 404 及零业务写验证通过，结合既有公开回执证据采纳 AC-INQ-029。原询盘 49 PASS / 11 NOT_RUN。 [本任务](../development/evidence/W500-01/DELIVERY.md)

## 2026-11-12 UTC · W499 询盘原接口与采用边界

已核对 P01、H01、SC01、DP08/10、固定入口、ACT 已接受对象及生产采用证据，采纳 AC-INQ-005/033/036。原询盘 48 PASS / 12 NOT_RUN。 [本任务](../development/evidence/W499-01/DELIVERY.md)

## 2026-11-12 UTC · W498 询盘 C# 集合所有权

CRM #188 已合入 `42fea57e`。补齐源事件扩展集合的快照、空项拒绝及合法扩展往返，采纳 AC-INQ-054。原询盘 45 PASS / 15 NOT_RUN，关联修订 2/2，正式 PostgreSQL 16/27。 [本任务](../development/evidence/W498-01/DELIVERY.md)

## 2026-11-12 UTC · W497 询盘隐私导出拒绝终态

CRM #186 已合入 `8ff5820e`。合法模拟询盘在隔离 PostgreSQL 上验证导出 20→90、审计和原 202 恢复，AC-INQ-051 已采纳。原询盘 44 PASS / 16 NOT_RUN，关联修订 2/2，正式 PostgreSQL 16/27。375 项中 38 DONE、1 IN_PROGRESS、336 PLANNED。 [本任务](../development/evidence/W497-01/DELIVERY.md)

## 2026-11-12 UTC · W496 询盘四处置隐私链

CRM #185 已合入 `ae09fcd3`，四种处置的活体回执重放、签名撤回和实际导出通过。结合前序到期实测，AC-INQ-041 已采纳。原询盘 43 PASS / 17 NOT_RUN，关联修订 2/2，正式 PostgreSQL 16/27。375 项中 38 DONE、1 IN_PROGRESS、336 PLANNED。 [本任务](../development/evidence/W496-01/DELIVERY.md)

## 2026-11-12 UTC · W495 询盘 FX25 期限

CRM #184 已合入 `ed637f1b`。PostgreSQL 物理缓存索引与限流桶按各自行期限清理，现行保留单元仍负责权限、hold 和事务恢复。AC-INQ-030/058 已采纳，原询盘 42 PASS / 18 NOT_RUN，关联修订 2/2，正式 PostgreSQL 16/27。375 项中 38 DONE、1 IN_PROGRESS、336 PLANNED。 [本任务](../development/evidence/W495-01/DELIVERY.md)

## 2026-11-12 UTC · W494 询盘 FX24 投递

CRM #183 已合入 `b682af723`，DEV-2 已释放。原生接收器在本地 HTTP 和 PostgreSQL 完成租户去重、确定拒绝隔离及恢复。原询盘 40 PASS / 20 NOT_RUN，关联修订 2/2，正式 PostgreSQL 16/27。375 项中 38 DONE、1 IN_PROGRESS、336 PLANNED。 [本任务](../development/evidence/W494-01/DELIVERY.md)

## 2026-11-12 UTC · W491 四处置原生保留

W491 使用用户确认的隔离历史装载，实际完成四处置五样本、hold、回滚和原任务恢复。CRM #182 已合入 e9e209797，DEV-2 已释放。询盘原验收 39/60，关联修订 2/2，正式 PostgreSQL 16/27。375 项中 38 DONE、1 IN_PROGRESS、336 PLANNED。 [本任务](../development/evidence/W491-01/DELIVERY.md)

## 2026-11-12 UTC · W490本轮结算与下一业务出口

本轮真实Next/.NET/PG询盘→H01/审核→Lead→隐私导出/一次下载及原结果恢复工作；六个CRM BUG已修并采用。原AC39 PASS/21 NOT_RUN，CRM关联运行修订2/2 PASS；全项目运行修订2/247不是询盘的245个前置。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16/11保持。 [本任务](../development/evidence/W490-01/DELIVERY.md) 原生12h00m10s已达到12h预算，执行计划结算；Main最终采用/官方finish及含收尾读数见PR #958，完整业务目标未关闭。

## 2026-11-11 UTC · W489配置预览一致性

W489配置预览已与公开固定必填一致，合法政策原四项完整保存不扩展；两种实际React/BFF/PG保存/预览/生效及默认流程通过，9项API成功证据复用、Next实际重建。CRM #181采用21239ca95、Issue #180关闭、DEV2准确分支清理/干净/官方释放；Main协调继续，完整原卡/验收状态保持。 [本任务](../development/evidence/W489-01/DELIVERY.md)

## 2026-11-11 UTC · W488隐私消息恢复

W488隐私导出原事件经真实Owner/PG/HTTP确认及丢回执恢复两项通过，实际跨原一分钟租约后保持原Request/发送1次、原202/Job状态版本及业务/H01零重复。CRM #179采用65993e5e4，DEV2准确分支清理/干净/官方释放；Main协调继续，原完整FX24/验收状态保持。 [本任务](../development/evidence/W488-01/DELIVERY.md)

## 2026-11-11 UTC · W487集合合同核验

W487以真实别名/with/null RED修复12类20列表的构造/init所有权，权威签名证据直接复用不可变合同，相关合同/签名及实际Next/PG链通过。CRM #178采用526817f7a，Issue #177关闭、准确分支清理和DEV2干净释放；Main协调采用继续。全054其他集合/原AC37/60/修订2/247/375和PG16/11保持。 [本任务](../development/evidence/W487-01/DELIVERY.md)

## 2026-11-11 UTC · W486公开输入合法组合

W486修复已接受政策组合在前后端阻断固定必填输入的问题；后端27/真实HTTP-PG2/Web9/重建实际Next整链1通过，原optional/导出字段和指纹/权限保持。CRM #176正常采用1d952a579、Issue #175关闭、准确分支清理和DEV2干净释放；Main协调采用继续。原AC37/60、修订2/247、375及PG16/11保持，12h继续。 [本任务](../development/evidence/W486-01/DELIVERY.md)

## 2026-11-11 UTC · W485实际Rejected保留

W485三项独立实际验收通过：Next生产模式页面整链、Rejected原60分钟联合清除不复活、换版/撤回后的原政策导出。CRM #174已采用d0183afd2，Issue #173关闭，准确分支清理/DEV2干净释放；Main协调采用继续。原AC37/60，修订2/247，375仍38 DONE及PG16/11；12h原生8h01m09检查点已记录并滚动安排余4h。 [本任务](../development/evidence/W485-01/DELIVERY.md)

## 2026-11-11 UTC · W484保留原图与原验收

W484 CRM #172/Issue #171已核销并准确清理/DEV2释放，修复含子图Lead擦除后实际版本封存；正常持久清除、末段I/O整体回滚/重试及15项相关回归通过。R1 POS/NEG按全部原义务采纳PASS，正式修订2/247；原60AC、原375、PG16/11和完整月度范围不提升，Main协调采用/finish待核。 [本任务](../development/evidence/W484-01/DELIVERY.md)

## 2026-11-11 UTC · 4h检查点与W483

W483 原导出刷新/实际新登录恢复、当前主体/租户/撤权、已消费后明确新意图及原五分钟R1终态共11项PG/HTTPS检查与6+14项Web行为、类型检查及一次集中审查通过。CRM #170 已合入远端 8edb41fee2a300a5177eb142ced397253aeeeb07，Issue #169关闭，准确分支清理/DEV2干净释放；Main本轮协调采用/finish待核，12h目标继续。 [本任务](../development/evidence/W483-01/DELIVERY.md)

## 2026-11-11 UTC · 首条询盘链W482

W482 原生导出与 Main v2 独立服务当前权限已实现，真实 TLS/HTTPS/PG/React、原 202 恢复/并发/失权/回滚、五分钟清除及原 60 分钟保留/hold/故障共同回滚和联合清除通过；一次集中审查通过。CRM #168 已采用 f58688e0→8c78760e，准确任务分支清理、DEV-2 干净释放；Main 本提交正常采用/finish 继续。原卡/60AC/R1 与正式 PG 状态分别核销，12h 目标仍 ACTIVE。 [本任务](../development/evidence/W482-01/DELIVERY.md)

## 2026-11-11 UTC · 首条业务链12h执行

W481同一v11的ERP/Core/RMA原journal/FileContent关闭候选、四类恢复回执和独立文件通过；现行撤权/全局键实测。真实epoch变化未运行，原375和正式PG基线不变；集中审查通过，待远端采用。 [本任务](../development/evidence/W481-01/DELIVERY.md) · [12h计划](../development/planning/20261111-codex-12h-business-chain.md)

## 2026-11-11 UTC · 48 小时执行结算

本期原生时钟已达 172,802 秒，目标容量 172,800 秒。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。WP02-02 为在途项。 [48 小时结算](../development/evidence/W480-01/DELIVERY.md)

## 2026-11-11 UTC 实际 v11 缺件拒绝和闭合回执

同一实际 v11 对仅 schema、缺共享 ERP、缺文件和源库别名在暂存前拒绝。完整包闭合暂存，错键无法取回回执，正确键原样恢复。源 Published、归档 Pending 及 B 原件保持。 原 375 项仍为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。 [W479交付](../development/evidence/W479-01/DELIVERY.md)

## 2026-11-11 UTC 同源 v11 的 ERP 实际租约与发布对账

同一实际四域 v11 的 ERP 原 Pending 在隔离 PostgreSQL 上由真实派送器取得租约并经一次模拟确认转为 Published。三次只读对账保留归档原值、当前状态与 B 原八表。 原 375 项仍为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。 [W478交付](../development/evidence/W478-01/DELIVERY.md)

## 2026-11-11 UTC 同源 v11 Stock 原件暂存与当前对账

同源 v11 纳入 A/B 真实 Stock Owner 原库存操作。A 的 14 份 Stock CSV 与 PostgreSQL 源及中立暂存逐字节一致，复核 W476 的 16 份原 CSV、45 字节文件及待发送原状态。 原 375 项仍为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。 [W477交付](../development/evidence/W477-01/DELIVERY.md)

## 2026-11-11 UTC 同源 v11 原件暂存与文件落盘

同源 v11 保留两条实际 RMA 原流水，含 A/B 原生 FileContent 与 ERP C03 验证数据。选定租户 14 份 PostgreSQL 暂存 CSV、45 字节原件和两份恢复回执精确一致。 原 375 项仍为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 16 PASS、11 NOT_RUN。 [W476交付](../development/evidence/W476-01/DELIVERY.md)

## 2026-11-11 UTC 同源 v11 原 RMA Owner 流水导入

同一真实W474 v11在关闭PostgreSQL候选导入RMA原1 head/2 version及2原COMMITTED，原3443字节、双回执、真实晚失败回滚及普通A/B隔离通过。 原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，容量172800。 [W475交付](../development/evidence/W475-01/DELIVERY.md)

## 2026-11-11 UTC 实际 RMA Owner 原流水的同源完整 v11

W372停机源独立副本以真实草稿和提交原流水生成同源完整v11；仅补模拟环境及空Stock路由，两租户原流水不变，原A两COMMITTED共3443字节与W372同。 原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，容量172800。 [W474交付](../development/evidence/W474-01/DELIVERY.md)

## 2026-11-11 UTC 完整 v11 ERP C03 原图导入

完整W429 v11的实际ERP C03原图在关闭PostgreSQL候选导入，A/B各八原成员逐字节一致；23505原生晚失败回滚、序列11、同原回执及源49关系保持。 原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，容量172800。 [W473交付](../development/evidence/W473-01/DELIVERY.md)

## 2026-11-11 UTC 完整 v11 RMA 两表导入

完整W429 v11的RMA head及version各一行在隔离PostgreSQL 18.6关闭Core候选逐字节导入，真实晚失败回滚，回执可原样恢复。W429的模拟RMA流水保持按原图校验拒绝。 原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，容量172800。 [W472交付](../development/evidence/W472-01/DELIVERY.md)

## 2026-11-11 UTC 关闭候选实际 FileContent Owner

实际.NET FileContent Owner在W469关闭候选读取A/B原快照、Engineering父版本和同键原流水，A原45字节真实下载、B错摘要拒绝，15关系及候选回执不变。 原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，容量172800。 [W471交付](../development/evidence/W471-01/DELIVERY.md)

## 2026-11-11 UTC 完整 v11 原源只读对账

完整W429 v11在同一物理身份的私有副本通过四个只读入口，IAM 17、Identity Priority 1、ERP 2、Main Stock 2，49源关系及原件不变；recoveryReady=false。 原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，44h158458、容量172800。 [W470交付](../development/evidence/W470-01/DELIVERY.md)

## 2026-11-11 UTC v11 FileContent 原历史导入

沿W429 v11与W468候选原45字节，PostgreSQL 18.6关闭候选导入FileContent三表及原Owner流水，引用Engineering父版本，五原CSV与回执逐字节一致，B同键原记录不变，真实晚段故障全回滚。 原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，44h158458、容量172800。 [W469交付](../development/evidence/W469-01/DELIVERY.md)

## 2026-11-11 UTC v11 候选文件原字节落盘

消费W467真实v11暂存回执及原manifest pin，核对同一历史元数据，原LocalFileStore 45字节按原键写入独立私有候选，可原样恢复回执，旧41字节v10保留；recoveryReady=false。 原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，44h158458、容量172800。 [W468交付](../development/evidence/W468-01/DELIVERY.md)

## 2026-11-11 UTC v11 文件原历史隔离暂存

已采用W429实际v11原件显式写入隔离暂存，五个FileContent原历史成员按原类型、键和租户落库及COPY逐字节回读，旧v10、晚段真实回滚和暂存回执保持；recoveryReady=false。 原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN；容量172800，44h158458。 [W467交付](../development/evidence/W467-01/DELIVERY.md)

## 2026-11-11 UTC 巷道和货架逐个停用与最终重查

巷道/货架原逐个停用采用原space-floor:edit、当前授权及实际当前归属，每项独立按同步WMS库存和两侧提交确认后通知；保留先成功和后拒绝，最后默认删除在当前事务重查子级。 实际Main/PostgreSQL18.6 12项及47既有通过；原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN。44h158458和容量/下一172800保持。 [W466交付](../development/evidence/BUG-933-SPACE-TOPOLOGY-DEACTIVATE-FENCE/DELIVERY.md)

## 2026-11-11 UTC 巷道和货架默认删除及改挂

现存巷道和货架默认删除及改挂使用原space-floor:edit、签名会话及同Context当前事务，实际子库位、目标、几何、墓碑与重发布一致决定。先发布则原402/403限制保持，已知提交与未知结果区分；逐个停用模式保持。 实际Main/PostgreSQL18.6及已有删除回归按交付报告。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN；44h158458，容量及下一172800。 [W465交付](../development/evidence/BUG-931-SPACE-TOPOLOGY-DELETE-FENCE/DELIVERY.md)

## 2026-11-11 UTC 单库位停用库存与两侧原子决定

现存独立HTTP停用用原space-publish:deactivate、当前签名会话和同Context事务稳定真实库存和层级、库位及WMS投影；两侧、LPB与原DEACTIVATE事件按已知提交处理，确认后才发成功通知。原库存、墓碑、同版本跳过和409合同保持。 实际44小时158458秒、下一172800；正式PG16 PASS/11 NOT_RUN，原375仍38 DONE/1 IN_PROGRESS/336 PLANNED。 [W464交付](../development/evidence/BUG-929-SPACE-LOCATION-DEACTIVATE-FENCE/DELIVERY.md)

## 2026-11-11 UTC 库位采纳并发跳过合同

现存库位采纳使用原space-publish:adopt、同Context当前授权事务及实际库位写锁；先提交的同码按原合同跳过，后到批次其余行可导入。保留原顺序、历史占码、租户隔离和影子位置字段，无回发、通知、LPB或物理库存变化。 实际Main PostgreSQL18.6三新和四项受影响发布、29项既有服务通过。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，容量172800。 [W463交付](../development/evidence/BUG-927-SPACE-LOCATION-ADOPT-FENCE/DELIVERY.md)

## 2026-11-11 UTC 库位发布当前编码闸门

现存整层/库区发布采用原space-publish:publish、签名会话和同Context授权事务；编码闸门在当前规则和拓扑内决定，真实库位、LPB、WMS投影及桥事件沿原合同提交。保留空批次、原409和旁路行为，确认提交后才发成功通知，未知结果明确。 实际Main PostgreSQL 18.6四项，既有发布、通知和观测链兼容按交付报告。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN；48h172800、40h144047及下一158400保持。 [W462交付](../development/evidence/BUG-925-SPACE-LOCATION-PUBLISH-FENCE/DELIVERY.md)

## 2026-11-11 UTC 场景差量当前事务与重发布

现存场景差量采用同Context当前space-floor:edit、签名会话、认证共同根和实际当前目标楼层；实际层级/WMS投影表锁，原差量、坐标、墓碑、重发布、LPB和集成事件参与同一父事务。原输入、既有版本冲突、投影版本判定及桥旁路策略保持，未知提交明确。 9项实际Main/PostgreSQL（6新、3既有日志）和16项既有InMemory通过、零跳过；模拟空间/身份/故障输入，实际宿主/业务/本地WMS投影及数据库。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，172800秒及40h144047/下一158400保持。 [W461交付](../development/evidence/BUG-923-SPACE-SCENE-DELTA-FENCE/DELIVERY.md)

## 2026-11-10 UTC 操作日志降级保存隔离

PostgreSQL操作日志降级使用独立DI作用域及新Context，传递原已确认请求租户，只保存日志。拒绝后未提交业务不被日志写入带入数据库，正常/日志故障及原字段、异常状态映射、Kafka路径和历史非PG保持。 3项实际Main/PostgreSQL和26项既有过滤器通过、零跳过；模拟场景/身份/故障，真实宿主/业务/日志和数据库。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，172800秒、40h144047/下一158400保持。 [W460交付](../development/evidence/BUG-921-OPERLOG-FALLBACK-ISOLATION/DELIVERY.md)

## 2026-11-10 UTC 场景导入与D7绑码当前事务

现存场景导入与D7绑码采用同Context当前space-floor:edit、签名会话及认证根。导入确认当前目标站点、真实几何骨架和库位两次保存同事务；绑码用实际货架/库位及原已发布未落位采纳资格。原导出/映射/算法、码/ID/状态/版本和其他场景路径保持。 9项实际Main/PostgreSQL和17项既有InMemory通过、零跳过。模拟场景/身份/故障输入，实际宿主、认证、数据库。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，172800秒、40h144047/下一158400保持。 [W459交付](../development/evidence/BUG-919-SPACE-SCENE-INGRESS-FENCE/DELIVERY.md)

## 2026-11-10 UTC 空间层级七写与货架坐标原子性

七个既有库区/巷道/货架写入采用同Context当前space-floor:edit、签名会话及认证根；锁定真实楼层/库区/巷道/货架/库位引用与写集。当前父项/同库区可选巷道决定、货架保存与坐标重算同父事务；原字段、坐标算法、码/状态/版本、RowVersion409和其余接口保持。 16项实际Main/PostgreSQL和39项既有InMemory通过、零跳过；模拟空间/身份/故障输入，实际宿主/认证/数据库执行。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，172800秒及实际40h144047/下一158400保持。 [W458交付](../development/evidence/BUG-917-SPACE-HIERARCHY-GEOMETRY-FENCE/DELIVERY.md)

## 2026-11-10 UTC 站点与楼层当前写入及父项决定

现存站点/楼层增改删六写采用同Context当前space-site/space-floor动作、签名会话及认证共同根；真实站点/楼层和原子项/锚引用锁，查询与写入同父事务。楼层创建只采用当前租户实际未删除站点，缺失/其他租户及删除先行拒绝；原坐标精度、编辑不重挂、查询及原护栏保持。 16项实际Main/PostgreSQL和39项既有InMemory通过、零跳过；模拟空间/身份输入，实际宿主/认证/数据库执行。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，172800秒与实际40h144047/下一158400保持。 [W457交付](../development/evidence/BUG-915-SPACE-SITE-FLOOR-FENCE/DELIVERY.md)

## 2026-11-10 UTC 空间编码规则当前写入与两阶段重建

现存空间编码规则增改删、批量/单格生成五写采用同Context当前space-code-rule:add/edit/delete/generate、签名会话及认证根；真实规则写锁与生成来源读锁/库位写锁，重建清旧码和赋新码同父事务。并发默认与默认迁入作用域互斥，原布局和算法、列表/预览/预检、旧构造路径保持。 14项实际Main/PostgreSQL和26项既有InMemory通过，零跳过；模拟规则/空间/身份/故障输入，实际宿主/认证/数据库执行。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，172800秒与实际40h144047/下一158400保持。 [W456交付](../development/evidence/BUG-913-SPACE-CODE-WRITE-FENCE/DELIVERY.md)

## 2026-11-10 UTC 字典写入当前授权与提交后缓存失效

现存字典六项写入采用同Context当前dict:add/edit/delete、签名会话和认证根，两张实际全局字典表SREX；查重/查行/级联写入与最终会话同父事务，确认提交后原缓存失效。保持全局共享模型/读取/cache键和既有响应；并发新增同码与编辑撞码明确拒绝。 15项实际Main/PostgreSQL和8项既有InMemory通过，零跳过；模拟字典/身份/故障/缓存输入，实际宿主/认证/数据库/缓存服务。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，172800秒与实际40h144047/下一158400保持。 [W455交付](../development/evidence/BUG-911-DICT-MUTATION-FENCE/DELIVERY.md)

## 2026-11-10 UTC 字段审计摘要当前查询

现存字段审计摘要列表采用同Context当前sys-field-audit query与实际签名会话，认证根先于实际Sys_FieldAuditLogs SHARE，Count/分页与最终会话/取消处同父事务；完整原筛选、1..200分页约束、双键排序和摘要解析保持，原record业务Owner/映射/安全投影/游标不变。 5项实际Main/PostgreSQL通过、零跳过，模拟审计/身份/原生控制输入，实际服务/签名/数据库执行。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，172800秒与实际40h144047/下一158400保持。 [W454交付](../development/evidence/BUG-909-FIELD-AUDIT-LIST-FENCE/DELIVERY.md)

## 2026-11-10 UTC 操作日志当前授权与清理父事务

现存业务操作日志Get/Clear在PostgreSQL采用同Context当前query/delete及签名会话，共同认证根先于实际Sys_OperLogs读取/清理锁，计数/分页与清理处同父事务，最终会话/取消及明确故障/未知处理；原关键词、分页、排序、返回实体/total/count及租户过滤保持，历史非PostgreSQL完整路径与构造器保留。 7项实际Main/PostgreSQL通过、零跳过。模拟日志和身份/控制输入，真实宿主/认证/SQL执行；未清理用户环境日志。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，预算172800秒，40h实采144047/下一158400保持。 [W453交付](../development/evidence/BUG-907-OPERLOG-AUTHORITY-FENCE/DELIVERY.md)

## 2026-11-10 UTC 有效功能配置当前查询

现存有效功能配置GET移入既有功能控制Owner，复用当前device-manage/签名会话和认证父事务，读取实际配置表SHARE；完整保留原租户/删除过滤、排序、五字段和版本编码，直接PUT410保持；申请路径默认表选择及其余授权/事务逻辑经精确逆向比较保持，并实际复测一个申请完整正常周期。 本窗5项实际Main/PostgreSQL通过、零跳过；明确模拟身份、仓库和配置。此前W451的12项及兼容5项按未变路径和原来源复用，不改称本窗新执行。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN，48小时172800秒。 [W452交付](../development/evidence/BUG-905-WMS-FEATURE-QUERY-FENCE/DELIVERY.md)

## 2026-11-10 UTC 功能变更申请当前授权与流程父事务

既有功能变更申请Get/Submit/Cancel采用当前wms-mobile:device-manage及实际签名会话，同Context认证父事务拥有申请和实际OA/WF提交/撤回，故障回滚清理跟踪、无孤立申请；按当前仓库和绑定/定义引用决定，保留原OperationId历史回放、保留天数/证据/版本校验及内部审批回调，旧办理410保持。 实际Main/PostgreSQL12项和既有兼容5项通过，零跳过，身份/业务/旧流程定义为明确模拟输入。原375项仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PostgreSQL16 PASS/11 NOT_RUN，48小时172800秒。 [W451交付](../development/evidence/BUG-903-WMS-FEATURE-REQUEST-FENCE/DELIVERY.md)

## 2026-11-10 UTC 仓库角色范围当前授权与引用事务

既有仓库角色范围Get/Replace采用当前pub-data-scope query/edit及实际签名会话；共同认证根后锁定范围与角色/仓库/区域引用，替换及失败清理同父事务，晚期故障不被日志重刷；并发替换保存整组结果，实际托盘HTTP采用撤销旧仓库/授予新仓库；范围引用由逐项查询改为至多两次集中查询，200项合法输入实际通过，保留规范化、通配符、清空、200上限及角色1语义，无新权限点。 实际Main/PostgreSQL11项及相关兼容2项通过，零跳过；权限/角色/仓库/托盘业务输入明确构造。原375项仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PostgreSQL16 PASS/11 NOT_RUN，48小时预算172800秒。 [W450交付](../development/evidence/BUG-901-WMS-ROLE-SCOPE-FENCE/DELIVERY.md)

## 2026-11-10 UTC 余料当前授权与输入边界

现存余料十入口采用当前actor/动作及共同认证根；view仅登记目录不自动授权；编号与记录共父事务，原生INSERT/UPDATE故障不残留业务；Create/Update共用已有正尺寸/数量及仓库库位输入边界，保留结束态错误优先级、合法预留态修改、四位余料数量及既有版本检查；并发预留按当前状态拒绝第二个用途。 实际Main/PostgreSQL24项及相关兼容/种子4项通过，零跳过；业务输入明确构造。原375项仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PostgreSQL16 PASS/11 NOT_RUN，48小时预算172800秒。 [W449交付](../development/evidence/BUG-899-REMNANT-LIFECYCLE-FENCE/DELIVERY.md)

## 2026-11-10 UTC 卷材当前授权与分切原子性

现存卷材七入口采用当前actor/动作及共同认证根；view仅登记目录不自动授权；创建/分切编号与父子卷同父事务；全部子宽先校验并用足够的整数累计，移除循环内重复校验，非法后项或原生故障不残留子卷/编号；并发消费在最终决定点读取当前剩长，八位长度及既有版本检查保持。 实际Main/PostgreSQL19项及相关兼容/种子6项通过，零跳过；业务输入明确构造。原375项仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PostgreSQL16 PASS/11 NOT_RUN，48小时预算172800秒。 [W448交付](../development/evidence/BUG-897-PAPER-ROLL-FENCE/DELIVERY.md)

## 2026-11-10 UTC 承运元数据当前授权与并发追加

现存承运元数据八入口采用当前actor/动作及共同认证根；view仅登记目录不自动授权；编号与运单共父事务，原生INSERT/UPDATE故障完整回滚；读取事件前围栏使两次合法追加均完成并保留原事件及EF版本检查，原手工状态/错误与重量费用精度保持。 实际Main/PostgreSQL16项及相关兼容/种子5项通过，零跳过；业务输入明确构造。原375项仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PostgreSQL16 PASS/11 NOT_RUN，48小时预算172800秒。 [W447交付](../development/evidence/BUG-895-CARRIER-LIFECYCLE-FENCE/DELIVERY.md)

## 2026-11-10 UTC IoT当前授权与最新记录投影

现存IoT九入口采用当前actor/动作及共同认证根；view仅登记目录不自动授权；编号与业务同父事务，原生传感器/读数/批次故障完整回滚；从实际历史修复最新记录投影并保留迟到及同时间合法输入，模拟值/时间一致且先检查类型和1–100×最多100边界。 实际Main/PostgreSQL26项及兼容/种子5项通过，零跳过，实插10000条；业务输入明确构造。原375项仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PostgreSQL16 PASS/11 NOT_RUN，48小时预算172800秒。 [W446交付](../development/evidence/BUG-893-IOT-OBSERVATION-FENCE/DELIVERY.md)

## 2026-11-10 UTC WCS手工任务当前授权与编号事务

现存WCS手工任务八个读写入口采用当前actor/动作及共同认证根；新view仅登记目录不自动授予；创建编号与任务同父事务，晚期原生故障后不再残留编号或被日志重刷失败任务，保留原手工状态、八位数量及无库存效果。 Main/PostgreSQL14项与相关兼容/种子5项实际通过，零跳过；设备/业务输入明确构造。原375项仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PostgreSQL仍16 PASS/11 NOT_RUN，48小时预算172800秒。 [W445 交付](../development/evidence/BUG-891-WCS-TASK-FENCE/DELIVERY.md)

## 2026-11-10 UTC 标签当前授权与持久冷恢复

现存标签七个HTTP入口采用当前动作/actor及共同认证根，设备当前资格先于回放，作业与版本化原结果回执共父事务；网关修正PENDING查询，持久化分区/原键/打印阶段，实际凭据与源绑定，未知打印不自动fail或重打，两个实际客户端进程在Main完成响应丢失后用原键恢复且只模拟打印一次。 Main/PostgreSQL 13项和客户端27项实际通过，零跳过；打印明确模拟。原375项仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PostgreSQL仍16 PASS/11 NOT_RUN。48小时预算172800秒，下次原生129600。 [W444 交付](../development/evidence/BUG-889-LABEL-RECOVERY-FENCE/DELIVERY.md)

## 2026-11-10 UTC 条码配置与整批恢复

条码 profile/alias 两种 upsert 与 XLSX 实际提交采用当前 actor、barcode-manage 和共同认证根；整批共父事务，第二行失败后不再被日志重刷出一条别名；三条暂停、实际撤权、预览/更新/恢复、原错误及八位转换精度完成真实验收。 原 375 项仍为 38 DONE、1 IN_PROGRESS、336 PLANNED；正式 PostgreSQL 16 PASS、11 NOT_RUN。48小时预算172800秒，下次原生129600。[W443 交付](../development/evidence/BUG-887-BARCODE-CONFIG-FENCE/DELIVERY.md)

## 2026-11-10 UTC 托盘元数据与编号事务

托盘六个元数据写者采用同一认证根、当前动作/库区/已有字段和实际 actor；编号与托盘同父事务，原生插入故障后无编号或记录残留；六条暂停及真实撤权竞争均拒绝，保留原状态、三位重量和无库存效果。 原 375 项仍为 38 DONE、1 IN_PROGRESS、336 PLANNED；正式 PostgreSQL 16 PASS、11 NOT_RUN。48 小时预算 172800 秒，32小时原生样本 115413，下次 129600。[W442 交付](../development/evidence/BUG-885-PALLET-WRITE-FENCE/DELIVERY.md)

## 2026-11-10 UTC 序列生命周期与库存事务

序列转换与七种生命周期在实际 Mobile Owner 内决定并组合库存父事务；原五种库存请求从 400 恢复共同提交，收货错误后遗留的跟踪对象不再被日志保存刷入；八条真实暂停路径均拒绝，保留序列/库存对账、实际部门继承、原回执与完整七步周期。 原 375 项保持 38 DONE、1 IN_PROGRESS、336 PLANNED；正式 PostgreSQL 16 PASS、11 NOT_RUN。48 小时总预算 172800 秒，下次原生校准 115200 秒。[W441 交付](../development/evidence/BUG-883-SERIAL-LIFECYCLE-FENCE/DELIVERY.md)

## 2026-11-10 UTC 设备生命周期与原生身份

设备票据、更新/禁用、快捷 PIN、匿名激活和签名心跳在实际身份共同根中提交；保留合法自失效，数据库 nonce 裁决双宿主重放，设备范围变更使旧客户端身份失效，已有数据升级保留设备原行并要求重新登录。 原 375 项保持 38 DONE、1 IN_PROGRESS、336 PLANNED；正式 PostgreSQL 16 PASS、11 NOT_RUN。48 小时总预算 172800 秒，下次原生校准 115200 秒。[W440 交付](../development/evidence/BUG-881-DEVICE-LIFECYCLE-FENCE/DELIVERY.md)

## 2026-11-10 UTC LPN Move 库存事务组合

LPN Move 在既有 Mobile Owner 中组合库存父事务原语；真实非空树由原 HTTP 500 恢复完整原子移动，保留八位数量、货主估值质量和四腿实际部门继承。空/非空树均在暂停先提交后 401、十三类原行保持，晚期回执故障完整回滚，范围、回放和原错误协议通过。 原 375 项保持 38 DONE、1 IN_PROGRESS、336 PLANNED；正式 PostgreSQL 16 PASS、11 NOT_RUN。已保存 28 小时校准，48 小时总预算 172800 秒，下次原生校准 115200 秒。[W439 交付](../development/evidence/BUG-879-LPN-MOVE-COMPOSITION/DELIVERY.md)

## 2026-11-10 UTC LPN 树和内容命令当前授权

装箱、拆箱、拆分、合并及回放接入既有 Owner 当前授权事务；四条真实 HTTP/PostgreSQL 路径均复现旧请求在暂停后写入，修复后 401 REAUTH_REQUIRED、九类原行逐字节保持，正常树变化、序列号、精确数量、回放和原业务错误通过。 原 375 项保持 38 DONE、1 IN_PROGRESS、336 PLANNED；正式 PostgreSQL 16 PASS、11 NOT_RUN。原生实测 100915 秒，48 小时总预算 172800 秒，下次校准 115200 秒。[W438 交付](../development/evidence/BUG-877-LPN-TREE-FENCE/DELIVERY.md)

## 2026-11-10 UTC LPN 创建当前授权

LPN 创建与回放通过现有 Owner 当前授权事务写入，实际 HTTP 与 PostgreSQL 复现旧请求在暂停后写入，修复后返回 401 REAUTH_REQUIRED，LPN、闭包、事件和回执原行逐字节保持。 原 375 项仍为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 为 16 PASS、11 NOT_RUN。[W437 交付](../development/evidence/BUG-875-LPN-CREATE-FENCE/DELIVERY.md)

## 2026-11-10 UTC LPN 策略当前授权

LPN 混装策略通过现有 Owner 当前授权事务写入，实际 HTTP 与 PostgreSQL 复现旧请求在暂停后写入，修复后返回 401 REAUTH_REQUIRED，策略原行逐字节保持。 原 375 项仍为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 为 16 PASS、11 NOT_RUN。[W436 交付](../development/evidence/BUG-873-LPN-POLICY-FENCE/DELIVERY.md)

## 2026-11-10 UTC 租户暂停与普通库存提交

W435 在 PostgreSQL 18.6 实测普通库存 V1 调整与版本化租户暂停的两种顺序。正式 PGT-017 仍待全 writer 登记和共同栅栏采用。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 为 16 PASS、11 NOT_RUN。 [W435](../development/evidence/W435-01/DELIVERY.md)

## 2026-11-10 UTC W372 同源 ERP 与文件

W423 在同一 PostgreSQL 18.6 快照核对 A 的 ERP 八表与 W420 原 45 字节文件。两个实际普通角色的 RMA 原数据和跨租户边界通过。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED，正式 PostgreSQL 为 16 PASS、11 NOT_RUN。 [W423](../development/evidence/W423-01/DELIVERY.md)

## 2026-11-10 UTC 同源 FileContent v11

W429 在 PostgreSQL 18.6 的同一快照导出原 RMA、既有共享成员及五个 FileContent 历史成员。正式 PostgreSQL 为 16 PASS、11 NOT_RUN，原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。执行预算为 48 小时（172,800 秒）。 [W429](../development/evidence/W429-01/DELIVERY.md) · [执行计划](../development/planning/20261109-codex-72h-execution.md)

## 2026-11-10 UTC PostgreSQL PGT-020

W433 实测平台创建原回执接首次 Core 初始化及真实 RMA。DDL 后终止后端全回滚，原键恢复，未 Ready 时业务拒绝。正式 PostgreSQL 为 16 PASS、11 NOT_RUN，原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。[W433](../development/evidence/W433-01/DELIVERY.md)

## 2026-11-10 UTC PostgreSQL PGT-027

W434 正常 Core EF 将隔离库迁移历史从 225 条推进到 226 条，IAM 与模拟登录通过。普通 SCRAM COPY 实测用户、角色错属各自全回滚，同号合法 A 行提交。正式 PostgreSQL 为 15 PASS、12 NOT_RUN，原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。[W434](../development/evidence/W434-01/DELIVERY.md)

## 2026-11-10 UTC IAM 用户成员引用

W428 正常 Core EF 将隔离库迁移历史从 224 条推进到 225 条，两个 IAM 用例和实际模拟登录 HTTP 通过。原 PGT-027 待成员关系的角色复合引用。正式 PostgreSQL 为 14 PASS、13 NOT_RUN，原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W428](../development/evidence/W428-01/DELIVERY.md)

## 2026-11-10 UTC PostgreSQL 物理连接错借

W425 使用实际普通 A 后端触发 B 绑定拒绝，业务命令计数为零，双方原行摘要一致。正式 PostgreSQL 为 14 PASS、13 NOT_RUN。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W425](../development/evidence/W425-01/DELIVERY.md)

## 2026-11-10 UTC PostgreSQL 连接取消和保存点

W432 实测约束及语句超时后保存点恢复、A 原池再借和 B 独立写入，合用 W344 的真实取消与坏连接替换。正式 PostgreSQL 为 13 PASS、14 NOT_RUN。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W432](../development/evidence/W432-01/DELIVERY.md)

## 2026-11-10 UTC ERP C03 关闭候选实测

W422 在实际 PostgreSQL 导入 W419 的 C03 原图，验证晚段全回滚、714 字节原回执、A/B 原字节和实际行版本序列。正式 PostgreSQL 为 12 PASS、15 NOT_RUN。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W422](../development/evidence/W422-01/DELIVERY.md)

## 2026-11-09 UTC 原全局消息键验收

W431 实测五组原键竞争、完整回滚和独立键域，并复用原受控接收实证。正式 PostgreSQL 为 12 PASS、15 NOT_RUN。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W431](../development/evidence/W431-01/DELIVERY.md)

## 2026-11-09 UTC RMA 同 schema 归属更新

W430 实测普通 A 的 ACL 42501、固定 CHECK 23514、两次无半写及合法 A 提交。正式 PostgreSQL 为 11 PASS、16 NOT_RUN，原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W430](../development/evidence/W430-01/DELIVERY.md)

## 2026-11-09 UTC 共享 RLS 与原密钥列

W427 实测普通 DELETE 0/1 和原密钥列 42501。正式 PostgreSQL 为 11 PASS、16 NOT_RUN。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W427](../development/evidence/W427-01/DELIVERY.md)


## 2026-11-09 UTC Stock 可信绑定

W426 采用 PGT-001 实测。正式 PostgreSQL 为 10 PASS、17 NOT_RUN。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W426](../development/evidence/W426-01/DELIVERY.md)


## 2026-11-09 UTC 容量与凭据轮换

W420 随 #858 采用并释放。W424 采用 PGT-023 实测，正式 PostgreSQL 为 9 PASS、18 NOT_RUN。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W424](../development/evidence/W424-01/DELIVERY.md)


## 2026-11-09 UTC RMA 与 FileContent 同源 Owner

W421 随 #857 采用并释放。W420 在同一 W372 安装真实生成 A/B 文件及父版本，原 RMA 保持。原台账为 38 DONE、1 IN_PROGRESS、336 PLANNED。正式 PostgreSQL 为 8 PASS、19 NOT_RUN。 [W420](../development/evidence/W420-01/DELIVERY.md)


## 2026-11-09 UTC 原 PostgreSQL 五项实证

W419 随 #856 采用并释放。W421 复核五项已执行证据，原 PostgreSQL 矩阵为 8 PASS、19 NOT_RUN。原台账为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W421](../development/evidence/W421-01/DELIVERY.md)


## 2026-11-09 UTC ERP 原件切面

W418 随 #855 采用并释放。W419 实测 A/B C03 原终态、重复原报文和同源 v10。原台账为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W419](../development/evidence/W419-01/DELIVERY.md)


## 2026-11-09 UTC FileContent 原生候选

W417 随 #854 采用并释放。W418 实测真实 FileContent 导入、原生回滚及同键恢复。W419 核实际 C03 Owner。原台账为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W418](../development/evidence/W418-01/DELIVERY.md)


## 2026-11-09 UTC PostgreSQL 原 PGT 三个直接场景

原 PGT-006、007、009 在真实隔离 RMA Core 候选范围通过。其余原 PGT 保持各自状态。原台账为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W417](../development/evidence/W417-01/DELIVERY.md)


## 2026-11-09 UTC FileContent 原生历史切面

W414 随 #852 采用并释放。W416 实测文件事件、原回执和父版本。W417 继续直接租户权限验收。原台账为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W416](../development/evidence/W416-01/DELIVERY.md)


## 2026-11-09 UTC 候选外部文件落盘

W413 随 #851 采用并释放。W414 实际复制同源 v10 的原文件，W415 继续验证已批准候选的原操作 HTTP。原台账为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W414](../development/evidence/W414-01/DELIVERY.md)


## 2026-11-09 UTC RMA 候选原流水

W411 随 #850 采用并释放。W410 验证 v2 批准开放，W412 验证同源非空 v10 中立暂存。W413 完成关闭态原流水本地验证，正式 PGT-021/022 待执行。原台账为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W413](../development/evidence/W413-01/DELIVERY.md) · [W412](D:/CP6-local-test/CP6-72H-20261109/W412-VERIFY/DELIVERY.md)


## 2026-11-09 UTC v10 隔离 Core 导入

W406 随 #849 采用并释放。W411 实测 v10 原两表导入、故障回滚和原回执恢复。Platform 0.10.4 正式版本登记已采用，签发及 Main 采用仍待执行。原台账为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W411](../development/evidence/W411-01/DELIVERY.md) · [Platform #81](https://github.com/GTX537/CP6.Platform/pull/81)


## 2026-11-09 UTC 独立 Core 导入与普通读取

W409 随 #848 采用并释放。W406 实测原两表的完整独立 Core 导入、回滚与 COMMIT 原回执。W401 实测普通登录、当前和历史、ETag、B 404 及撤权后 403，集成席位释放。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W406](../development/evidence/W406-01/DELIVERY.md) · [W401](/D:/CP6-local-test/CP6-72H-20261109/W401-VERIFY/DELIVERY.md)


## 2026-11-09 UTC v9/v10 多域只读对账

W408 随 #847 采用并释放。W409 用两个原包实测 IAM、Identity Priority 和 ERP 六次只读调用及受限角色。W406 恢复准确分支后通过完整独立 Core 实库验证。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W409](../development/evidence/W409-01/DELIVERY.md) · [CP6 #847](https://github.com/GTX537/CP6/pull/847)


## 2026-11-09 UTC v10 Stock 当前对账

W407 随 #846 采用并释放。W408 核准同源 v10 的当前状态并验证真实历史 Pending 对当前 Published。Platform #80 已采用守卫源码，正式包与 Main 采用待验。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W408](../development/evidence/W408-01/DELIVERY.md) · [Platform #80](https://github.com/GTX537/CP6.Platform/pull/80)


## 2026-11-09 UTC v10 Stock 中立暂存

W407 在隔离 PostgreSQL 实测原 v10 及 14 个 Stock 原成员的原子暂存和原回执。W405 随 #845 采用并释放。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W407](../development/evidence/W407-01/DELIVERY.md) · [PR #845](https://github.com/GTX537/CP6/pull/845)


## 2026-11-09 UTC RMA 与 Main Stock 同快照归档

W405 实测同一切面、36 个成员及晚到的真实库存和隐私事务。W404 随 #843 采用并释放。原 375 项为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W405](../development/evidence/W405-01/DELIVERY.md) · [PR #843](https://github.com/GTX537/CP6/pull/843)


## 2026-11-09 UTC v9 历史中立落表

W404 实测原 v9 两份原件、原约束和回执。W403 随 #842 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W404](../development/evidence/W404-01/DELIVERY.md) · [PR #842](https://github.com/GTX537/CP6/pull/842)


## 2026-11-09 UTC Main Stock 原发件箱切面

W403 实测原生 13 表及关联原日志。W402 随 #841 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W403](../development/evidence/W403-01/DELIVERY.md) · [PR #841](https://github.com/GTX537/CP6/pull/841)


## 2026-11-09 UTC v9 安全审计和主体保留历史

W402 实测同一 PostgreSQL 切面导出原记录。W400 随 #840 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W402](../development/evidence/W402-01/DELIVERY.md) · [PR #840](https://github.com/GTX537/CP6/pull/840)


## 2026-11-09 UTC v8 IAM 与 Identity Priority 对账

W400 原 v8 两入口已实库核实。W399 随 #839 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W400](../development/evidence/W400-01/DELIVERY.md) · [PR #839](https://github.com/GTX537/CP6/pull/839)


## 2026-11-09 UTC ERP 原键对账与双进程池峰值

W399 已在专属 PostgreSQL 实证。W398 随 #838 采用并释放。W397 的两个真实动态组件进程同时持有 64 个原生普通事务。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W399](../development/evidence/W399-01/DELIVERY.md) · [PR #838](https://github.com/GTX537/CP6/pull/838)


## 2026-11-09 UTC ERP v8 原子暂存

W398 原八表和原回执实际验证，W396 随 #837 采用并释放。12 小时原生 43,201 秒。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W398](../development/evidence/W398-01/DELIVERY.md) · [PR #837](https://github.com/GTX537/CP6/pull/837)


## 2026-11-09 UTC 原统一包现行队列

W396 原 v6/v7 队列对账，W395 随 #836 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W396](../development/evidence/W396-01/DELIVERY.md) · [PR #836](https://github.com/GTX537/CP6/pull/836)


## 2026-11-09 UTC ERP 原共享八表

W395 v8 原键与字节，W394 随 #835 采用并释放，W392 真 200 轮已核。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W395](../development/evidence/W395-01/DELIVERY.md) · [PR #835](https://github.com/GTX537/CP6/pull/835)


## 2026-11-09 UTC FileContent 原资产落表

W394 原 v7 资产 staging 与原回执实测，W393 随 #834 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W394](../development/evidence/W394-01/DELIVERY.md) · [PR #834](https://github.com/GTX537/CP6/pull/834)


## 2026-11-09 UTC 现行 IAM 原图

W393 v6/v7 原权威对账通过，W391 随 #833 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W393](../development/evidence/W393-01/DELIVERY.md) · [PR #833](https://github.com/GTX537/CP6/pull/833)


## 2026-11-09 UTC 历史 IAM 原子落表

W391 原 v6 staging 真回滚与原回执恢复通过，W389 随 #832 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W391](../development/evidence/W391-01/DELIVERY.md) · [PR #832](https://github.com/GTX537/CP6/pull/832)


## 2026-11-09 UTC 原外部资产切面

W389 FileContent 原件同快照核验，W387 随 #831 采用并释放。W390 同物理库两版真实宿主的范围实证已保存。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W389](../development/evidence/W389-01/DELIVERY.md) · [PR #831](https://github.com/GTX537/CP6/pull/831)


## 2026-11-09 UTC 历史 IAM 同快照归档

W387 原历史 IAM v6 实库通过，W384 随 #830 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W387](../development/evidence/W387-01/DELIVERY.md) · [PR #830](https://github.com/GTX537/CP6/pull/830)


## 2026-11-09 UTC 身份队列当前对账

W384 实际当前原键、终态、租约及冲突。W385 随 #829 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W384](../development/evidence/W384-01/DELIVERY.md) · [#829](https://github.com/GTX537/CP6/pull/829)


## 2026-11-09 UTC 身份优先隔离落表

W385 真实四表、原键及空成员，W382 随 #828 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W385](../development/evidence/W385-01/DELIVERY.md) · [#828](https://github.com/GTX537/CP6/pull/828)


## 2026-11-09 UTC 原可见性隔离落表

W382 原图和原数据实库落表，W380 随 #827 采用并释放。W383 实测外部原件失败与准入隔离。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W382](../development/evidence/W382-01/DELIVERY.md) · [#827](https://github.com/GTX537/CP6/pull/827)


## 2026-11-09 UTC 身份优先队列归档

W380 四张原生表的同快照、原字节、租约和全局键实测。W379 随 #826 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W380](../development/evidence/W380-01/DELIVERY.md) · [#826](https://github.com/GTX537/CP6/pull/826)


## 2026-11-09 UTC 原可见性事实归档

W379 实测 v4 原可见性事实同切面。W377 随 #825 采用并释放，W378/W381 的实库结果保留。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W379](../development/evidence/W379-01/DELIVERY.md) · [#825](https://github.com/GTX537/CP6/pull/825)


## 2026-11-09 UTC v3 原来源隔离落表

W377 实测原来源、原日志、后段回滚与原回执。W375 随 #824 采用并释放，W376 实测原模型和池。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W377](../development/evidence/W377-01/DELIVERY.md) · [#824](https://github.com/GTX537/CP6/pull/824)


## 2026-11-09 UTC 普通 RMA 原操作

W375 实测原查询、关闭、当前权限和原竞争。v3 归档随 #823 采用并释放。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W375](../development/evidence/W375-01/DELIVERY.md) · [#823](https://github.com/GTX537/CP6/pull/823)


## 2026-11-09 UTC 原来源同快照

原 RMA 提交随 #822 正常采用并释放。W374 实测可选 v3 归档来源与原日志同切面，旧 v1/v2 兼容。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W374](../development/evidence/W374-01/DELIVERY.md) · [#822](https://github.com/GTX537/CP6/pull/822)


## 2026-11-09 UTC 普通 RMA 原提交

实际普通租户事务原子推进草稿版本并保存原操作。W370 远端 #821 与分支处置、工作区和席位已核。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W372 交付](../development/evidence/W372-01/DELIVERY.md) · [#821](https://github.com/GTX537/CP6/pull/821)


## 2026-11-09 UTC 可操作的隔离 RMA staging

实际 v1、v2 和写后 v2 在精确空库原子落 schema、共享片和原回执。W364 随 #820 正常采用并正式收尾。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W370 交付](../development/evidence/W370-01/DELIVERY.md) · [#820](https://github.com/GTX537/CP6/pull/820)


## 2026-11-09 UTC 普通 RMA 草稿 POST

真实 A/B 登录同键各自提交，原 head、version 和原日志同普通事务。W366 正常采用、分支处置及释放已核。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W364 交付](../development/evidence/W364-01/DELIVERY.md) · [#819](https://github.com/GTX537/CP6/pull/819)


## 2026-11-09 UTC 同快照 RMA 原日志

真实已采用 W361 的 A/B 源库按本租户和原 Owner 输出原日志原件。W361 远端 #818 及分支处置与固定席位已核。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W366 交付](../development/evidence/W366-01/DELIVERY.md) · [#818](https://github.com/GTX537/CP6/pull/818)


## 2026-11-09 UTC 全 Core RMA 原操作日志

真实 PostgreSQL 的 RMA 原日志按实际登录和 Owner 隔离，原认证锁与受控追加同事务。W363 完整 1,168 秒且两端分支和席位收尾，W365 实库 703 秒。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W361 交付](../development/evidence/W361-01/DELIVERY.md) · [#817](https://github.com/GTX537/CP6/pull/817)


## 2026-11-09 UTC 真实单快照 RMA 源归档

从准确物理库和已采用租户 Ready 导出 RMA 与共享出库同一快照，独立 staging 验证。W359 随 #816 正常采用并回收。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W363 交付](../development/evidence/W363-01/DELIVERY.md) · [#816](https://github.com/GTX537/CP6/pull/816)


## 2026-11-09 UTC RMA 分页及八小时实测校准

W359 原 HTTP 受权列表通过真实 A/B。W358 随 #815 正常采用，完整 1,711 秒。累计实际 28,846 秒，原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W359 交付](../development/evidence/W359-01/DELIVERY.md) · [#815](https://github.com/GTX537/CP6/pull/815)


## 2026-11-09 UTC RMA 读采用与非事务索引恢复

W356 真实 RMA 单条及历史读随 #814 采用。W358 通过 PG 真正并发建索引和无效索引恢复。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W358 交付](../development/evidence/W358-01/DELIVERY.md) · [#814](https://github.com/GTX537/CP6/pull/814)


## 2026-11-09 UTC 原 RMA 真实 GET 与 Stock 容量采用

新隔离全 Core 的原 RMA 单条及历史读取通过真实 HTTP 和 EF 验证。Stock 容量实测已随 #813 正常采用。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W356 交付](../development/evidence/W356-01/DELIVERY.md) · [#813](https://github.com/GTX537/CP6/pull/813)


## 2026-11-09 UTC W353 采用及 Stock 实际容量

W353 随 #812 正常采用，准确分支已清理，DEV-1 已复用给 RMA 真实 HTTP。Stock 10/100 容量实测完成，原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W355 交付](../development/evidence/W355-01/DELIVERY.md) · [#812](https://github.com/GTX537/CP6/pull/812)


## 2026-11-09 UTC W353 全 Core RMA 启用与 #808 关闭

真实全 Core 的 RMA 启用回归已通过，原业务表和该 Owner 的旧 journal 写栅栏、原子故障回滚及原键恢复均已实证。#808 随 #811 正常采用并关闭，DEV-2 已释放后转 W355。原 375 为 38 DONE、1 IN_PROGRESS、336 PLANNED。 [W353 交付](../development/evidence/W353-01/DELIVERY.md) · [#811](https://github.com/GTX537/CP6/pull/811)


## 2026-11-09 UTC #808 恢复候选派送拦截

真实恢复候选在认领和发送授权处均拒绝，原候选队列及原件不变。相关 PostgreSQL 回归已通过，待远端收尾。 [交付证据](../development/evidence/BUG-808-STOCK-RESTORE-OUTBOX/DELIVERY.md)

## 2026-11-09 UTC W350 物理租户 RMA 与 WP02-28 核销

RMA 首版在实际 PostgreSQL A/B schema 通过并发、中断、原回执与普通登录验证。WP02-28 的本地采用经 #809 正常远端采用及回收已核销。 [RMA 交付](../development/evidence/W350-01/DELIVERY.md) · [WP02-28 交付](../development/evidence/W351-01/DELIVERY.md)

## 2026-11-09 UTC W351 全 Core 实际前向和应用回退

同库运行旧版和含 #801 修复的当前版。Core 1→224→224，旧宿主重启保持新迁移、原 C02 和 Outbox。 [交付报告](../development/evidence/W351-01/DELIVERY.md)

## 2026-11-09 UTC W349 本地回退判定

固定双版本、原始运行观察、真实物理库和驱动决定唯一已观察的本地回退方向。DEV-1 实施首个 RMA 租户业务模板和真实逐租户锁。 [交付报告](../development/evidence/W349-01/DELIVERY.md)

## 2026-11-09 UTC W346 库存安装回执可恢复

库存首装输出丢失后，新 CLI 在同一物理 PostgreSQL 的只读快照取回原 UUID。完整目录、读取边界和既有写入扩展经实际验证。 [交付报告](../development/evidence/W346-01/DELIVERY.md)

## 2026-11-09 UTC W347 两版实际 service-token 兼容

固定旧、新宿主在同一隔离 PostgreSQL 布局完成两方向签发和撤销，旧版重启后重放原结果，Outbox 字节、快照和 generation 保持。当前生产推广沿独立门禁。 [交付报告](../development/evidence/W347-01/DELIVERY.md)

## 2026-11-09 UTC PostgreSQL 首管理员初始化修复

#801 的真实空库、同库重跑、旧角色续跑和故障恢复回归通过。首次建立默认角色和导航与首管理员同事务，菜单编号冲突按实际身份处理。 [交付报告](../development/evidence/BUG-801-PG-FIRST-ADMIN/DELIVERY.md) · [Issue #801](https://github.com/GTX537/CP6/issues/801)

## 2026-11-09 UTC W345 两份实际本地制品

两份固定 Main 源码的 Release publish 和五包运行身份已在隔离 PostgreSQL 分别实测。当前版完整初始化发现 P0 #801，真实登录诊断另有通过证据。 [交付报告](../development/evidence/W345-01/DELIVERY.md) · [Issue #801](https://github.com/GTX537/CP6/issues/801)

## 2026-11-09 UTC W344 实际 Stock 故障恢复

真实取消和精确终止普通 PostgreSQL 后端均回滚，原键查回 UNKNOWN 后可安全提交。同一 B 普通池在授权根释放后继续。DEV-2 并行验证两份本地制品。 [交付报告](../development/evidence/W344-01/DELIVERY.md)

## 2026-11-09 UTC W343 实际 Stock 凭据轮换

旧宿主普通连接归零后轮换同一角色。旧凭据返回 28P01，新宿主分别查回 A/B 原结果并提交 A 调整。用户授权加速使用子代理，COORD 统一回收。 [交付报告](../development/evidence/W343-01/DELIVERY.md)

## 2026-11-09 UTC W342 真实 Stock 错绑定

B 的真实写请求绑定 A 普通 LOGIN 时返回 503，正确宿主查回原操作为 UNKNOWN。原生角色切换及跨租户读写同时取得隔离证据。 [交付报告](../development/evidence/W342-01/DELIVERY.md)

## 2026-11-09 UTC W341 实际 Stock A/B 同键

同一宿主的两个普通 LOGIN 使用相同业务键和原操作键，实际库存、原结果、物理池和 Outbox 路由分别落本租户。下一步核错绑定与凭据轮换。 [交付报告](../development/evidence/W341-01/DELIVERY.md)

## 2026-11-09 UTC W340 Stock 写入对象和实际 Owner

实际 PostgreSQL 目录已核租户业务键、全表原标识、原操作与 Outbox 外键及普通写者列授权。公开入口、父 Owner、质检和召回分别归属。后续实测两租户同键。 [交付报告](../development/evidence/W340-01/DELIVERY.md)

## 2026-11-09 UTC W339 直接库存移库的普通事务

已启用普通移库的宿主让公开直接移库沿准确用途执行。原双流水号、货权、两个部门和当前库位权限实测。下一步核库存写者与两租户。 [交付报告](../development/evidence/W339-01/DELIVERY.md)

## 2026-11-09 UTC W338 直接库存调整的普通事务

已启用普通调整的宿主让原公开调整沿自己的可信用途执行。原流水号、当前授权和库存精度实测。下一步接公开直接移库。 [交付报告](../development/evidence/W338-01/DELIVERY.md)

## 2026-11-09 UTC W337 真实 V2 移库目标创建

原 V2 移库上下文和目标创建可显式采用普通租户事务。两腿归属、原货权、实际目标故障与原结果已验证。后续接直接库存兼容入口。 [交付报告](../development/evidence/W337-01/DELIVERY.md)

## 2026-11-09 UTC W336 真实库存 V2 创建调整

原创建调整上下文和单笔调整可显式采用普通租户读写事务。现行部门版本、失败回滚、八位小数和原键查回已有实际 HTTP/PG 证据。后续接原 V2 移库目标。 [交付报告](../development/evidence/W336-01/DELIVERY.md)

## 2026-11-09 UTC W335 库存 V2 创建原语

普通库存事务可沿原创建资格记录显式部门和安全审计。历史库存、同事务审计重放及保存点提交均有实际 PostgreSQL 边界验证。下一步接实际 V2 创建调整 HTTP。 [交付报告](../development/evidence/W335-01/DELIVERY.md)

## 2026-11-09 UTC W334 真实库存 V1 两腿移库

原 V1 移库可显式采用普通事务，沿原两端权限和实际库存元数据执行。后续接 V2 明确创建的部门及审计原语。 [交付报告](../development/evidence/W334-01/DELIVERY.md)

## 2026-11-09 UTC W333 库存原操作关闭与数据库确认中断

已启用普通库存调整时，原关闭使用普通事务和原图锁。真实数据库确认中断通过原操作键恢复。后续接实际 V1 两腿移库。 [交付报告](../development/evidence/W333-01/DELIVERY.md)

## 2026-11-09 UTC W332 库存原命令只读恢复

显式采用普通库存调整的宿主沿原 Owner 读取原操作。普通只读事务与原 journal 权限和行隔离绑定。后续接显式关闭和数据库提交故障。 [交付报告](../development/evidence/W332-01/DELIVERY.md)

## 2026-11-09 UTC W331 原库存 V1 调整的可信事务

原 V1 HTTP 调整可显式采用普通 PostgreSQL LOGIN。实际普通事务取得原认证根和业务图锁，当前用户、部门、库位与字段授权在真实宿主验证。后续接原操作恢复和关闭。 [交付报告](../development/evidence/W331-01/DELIVERY.md)

## 2026-11-09 UTC W330 库存普通登录的业务写集

实际库存移动原语已接库存、流水、编号、部门继承、安全审计、原 journal 和 Outbox 同事务。前向迁移保持仓库草稿和部门映射的原生校验。下一步接原 V1 HTTP 调整的真实用户授权和宿主。 [交付报告](../development/evidence/W330-01/DELIVERY.md)

## 2026-11-09 UTC W329 库存 Outbox 同事务入队

普通库存 LOGIN 可将已有原 journal 与原生通知入队放进同一事务。两租户隔离、原消息键、回滚及延迟源证明已实测。后续接真实库存调整。 [交付报告](../development/evidence/W329-01/DELIVERY.md)

## 2026-11-09 UTC W328 库存原命令与审计

现有库存 journal 已在普通 LOGIN 的同一事务验证提交、回滚、原结果和审计隔离。真实库位撤回通过前向触发器适配回归。继续按 72 小时目标验证 Stock Outbox 入队。 [交付报告](../development/evidence/W328-01/DELIVERY.md)

## 2026-11-09 UTC W327 库存写入授权前置

普通租户 LOGIN 在自己的 PostgreSQL 事务取得原认证根，并与现有租户政策写入串行。实际安装命令和两种竞争顺序已验证。后续按原卡接实际业务写集，72 小时目标继续。 [交付报告](../development/evidence/W327-01/DELIVERY.md)

## 2026-11-09 UTC 三天实际执行目标

开启72小时实际Codex执行目标。W326已正常采用和释放，按原375真实余项单人串行推进。 [执行计划](../development/planning/20261109-codex-72h-execution.md)

## 2026-11-09 UTC 总控续接和 W326 库存读取

按用户交接接任总控。四仓远端及固定席位已核对，W325和原24h实际86,238秒终态已确认。W326完成库存列表、历史和流水的可信 PostgreSQL 普通登录读取验证。原375仍为37 DONE、2 IN_PROGRESS、336 PLANNED，原59仍为9闭环、50余项。 [交付报告](../development/evidence/W326-01/DELIVERY.md)。

## 2026-11-08：原24h目标终态登记

原CP6-24H-20261104在本源正常两仓采用、准确分支清理/官方finish及wave325-final-verification.json实际PASS后DONE生效；当前源是此前快照，尚不冒称已完成这些外部动作。已完成受限实现/实际验收及原59核销，37537DONE/2IN_PROGRESS/336PLANNED、原59为9闭环/50余项（35实现/15待核）、125/247不变。全轮实际秒数以最终证明为准，未新开周期。[交付](../development/evidence/W325-01/DELIVERY.md)。

## 2026-11-08：原24h最后完整W325验证收齐

W325完成内部准确可选成员排除：当前SET排除决定与源确认权限、全部原条款OPTIONAL/允许来源决定、准确原Source/适用范围；保存原成员/原件/冲突，连续继承原决定人/版本/时间，变更适用范围重新决定。实际FLOW与Owner批准分开，原操作恢复、回滚/竞争及真实Chrome采用。 10项新增实际PostgreSQL18场景（含真实Chrome、完整FLOW到Owner批准、混合必需条款、双连接/回滚/当前权限/原键和直接创建），2项既有SET审批/冲突回归、23项唯一Vue行为及vue-tsc通过。 原37537DONE/2IN_PROGRESS/336PLANNED，原59为9闭环/50余项（35明确实现/15待核）；125/247不变。WP05-14仍余商业原Owner、ECM/DEV真实完成与其他实际消费者；本模式不接纳必需/禁止/未知来源或涉及当前冲突的成员排除，历史冲突义务不能被排除或删除草稿清掉。涉及已解决冲突的更广排除及其他来源政策未由本受限模式完成；当前Main ITER后继影响和本次内部可选条款不重做。 [报告与计时](../development/evidence/W325-01/DELIVERY.md)。当前正常远端采用、准确分支清理/官方finish与最终有效时钟收妥后结算原24h；本源提交前仍为生命周期待完成快照，最终PASS见wave325-final-verification.json，不自动开新周期。

## 2026-11-08：W310闭环，W325预算末尾实施

Main `7dbc37aa00d1075a0dde22117c8d47a8550defdc`、Handoff `a53d7709ac58734bf56679ef2a306d7fbfb7cc59`；COORD单人DEV1/DEV2官方登记。W310 Main780/Handoff301已收妥，准确任务分支清理/席位释放，实际1504秒（25分04秒），低于文档45–75分钟估算。本窗累计83282秒，约24h余3118秒。 原WP05-14准确可选成员排除：以明确OPTIONAL/允许来源决定的准确条款、当前SET排除与源确认权限，保存原成员/原件/冲突和真实原决定追溯；原事务/CAS/回执/页面恢复直接采用，未知/必须/禁止/错误来源及不利冲突继续阻断，其他商业/ECM/DEV仍余。 [窗口](../development/dispatch/20261108-wave325/README.md)。37537DONE/2IN_PROGRESS/336PLANNED、原59余50、125/247不变；原24h仍active。

## 2026-11-08：W310原59核销与WP03-03闭环

W310逐原59核销：新增按有效已有执行关闭WP03-03；现原37537DONE/2IN_PROGRESS/336PLANNED，原59为9闭环/50余项（35明确实现、15证据/适用待核），125/247不变。W324旧47校正51与本次新闭环分开，四张集合外合法完成保留。 本轮累计实际有效82534秒（约22.93h），仍余3866秒；本次没有新跑产品，复用W120/121/122/253/256/259真实PG/HTTP/React。 [逐卡余项与原验收](../development/evidence/W310-01/DELIVERY.md)。本轮目标继续，下一准确成员排除窗口仅在当前预算内实施；分支/席位收妥按最终proof，不能先称生命周期完成。

## 2026-11-08：W324闭环，W310预算末尾核销

Main `fe4e66aaa124f43630dbc5c97b8a9f049742ea24`、CRM `0cd83eb09826552fc1dc8d631a7c82484ec40363`、Platform `a89e90eb4f078f37a6f8610882079073185fbcf8`、Handoff `4a2e0975a29a1e187f465f3e2a35c621c3acbf90`。COORD单对话，DEV1/DEV2官方登记；W324两仓分支已准确清理、席位已释放，实际3520秒（58分40秒）。本窗开始累计81636秒，24h余4764秒。 W310预算末尾核销：以原CSV及W250相同59个ID逐卡对照原定义、当前源码/采用和既有有效执行；保留8已闭环、51真实余项，结算本轮全部窗口实测与24h口径。只更新文档/台账，不重跑未变产品，不提前宣布预算或全项目完成。 [本窗](../development/dispatch/20261108-wave310/README.md)。

## 2026-11-08：原59卡计数纠正为8闭环/51剩余

统计校正：原始20261102-001406Z库存CSV的“已局部交付，整卡未关闭”59项与W250逐卡ID完全一致。当前仅其中8项DONE，剩51；WP12-03/04/08/10虽已完整DONE，却不在原59集合。旧followup把这4项一并从59扣减，造成47，现纠正51。375的36DONE/2IN_PROGRESS/337PLANNED和这4项合法完成均不回退，125/247不变；后续原卡逐项核销继续按原合同，不用新窗口数代替原59。此前47数字为旧计数错误，历史报告保留并由本校正说明取代当前统计。 [依据](../development/evidence/W324-01/DELIVERY.md)。

## 2026-11-08：W324租户载荷实际处置 / 24h继续

W324完成Main.Privacy.TenantExportPayload.v1真实恢复采用：独立无subject的tenant Unit，准确原制品元数据/revision/实际SHA一次外部核对，真实删除与载荷缺失/整制品缺失0分别记录；当前源政策/全目标hold和独立恢复批准，父回执/实际效果/墓碑/fence/开放标记同候选commit，保持两次commit与原结果责任。其他Owner/WAL/外部副本及完整环境恢复仍PARTIAL。 [交付](../development/evidence/W324-01/DELIVERY.md)。

14项新增实际PostgreSQL18场景、8项受影响live处置与既有身份/主体载荷/安全字段恢复回归（含新增恢复与既有处置Chrome）、25项Vue行为及vue-tsc通过。 原375=36DONE/2IN_PROGRESS/337PLANNED，原59=12闭环/47剩余，125目标/247正式验收不变。WP02-15仍余未分类安全内容/不可归属事件、授权/组织引用、OA/PLM/CRM历史、FILE及其他来源副本、整租户全部Owner导出/处置、WAL/备份/外部副本与完整环境恢复。当前新租户载荷此Owner的真实恢复已采用，不再列为未实现。W306/W249/七天及已闭环FORM不重复；下一W310预算末尾实际原375/59及工时核销，不另起周期。 最终分支/工作区/席位见wave324-final-verification.json。

## 2026-11-08：W323闭环，W324连续实施

W324采用Main.Privacy.TenantExportPayload.v1真实恢复副本：独立无subject的tenant恢复Unit、准确原制品元数据/revision/实际SHA、真实删除字节与缺失0分别记录，当前源保留政策/整租户hold和独立恢复批准，父回执/原生效果/墓碑/fence/候选开放同候选commit；继续SourceRoot→CandidateRoot与两次提交UNKNOWN责任，其他Owner及WAL/外部副本仍PARTIAL。

Main `be6ec3bea15d3e0a6d54f1960f9f7eaabb13f641`，Handoff `7ffdab2f4f8a6b2ad36c99b20c00e87b66b987d9`，COORD单人固定DEV1/DEV2；W323 Main778/Handoff299已核两仓采用、准确清理和官方释放，实测2143秒。本窗累计77945秒，余约2.35h。 [本窗](../development/dispatch/20261108-wave324/README.md)。原24h仍active/incomplete。

## 2026-11-08：W323租户载荷实际处置 / 24h继续

W323完成Main.Privacy.TenantExportPayload.v1当前库准确租户载荷处置：明确保留政策/独立申请批准/当前最终执行、全目标租户所有主体hold与ABA、真实字节删除/撤访问/最小原回执/墓碑同commit及原生防回填。无伪subject。新墓碑推进restore_epoch，未采用的新Owner恢复明确503并保持候选隔离，历史已提交原结果仍可恢复；不冒充备份或外部副本已清。 [交付](../development/evidence/W323-01/DELIVERY.md)。

14项新增实际PostgreSQL18场景、13项受影响租户导出及主体载荷/安全字段恢复回归（含新增处置与既有租户下载Chrome）、23项Vue行为及vue-tsc通过。 原375=36DONE/2IN_PROGRESS/337PLANNED，原59=12闭环/47剩余，125目标/247正式验收不变。WP02-15仍余租户载荷恢复实际适配、未分类安全内容/不可归属事件、授权/组织引用、OA/PLM/CRM历史、FILE及其他来源副本、整租户全部Owner导出/处置、WAL/备份/外部副本和完整环境恢复。W306/W249/七天及已闭环FORM不重复；24h继续相邻真实余项，W310预算末尾。 最终分支/工作区/席位见wave323-final-verification.json。

## 2026-11-08：W322闭环，W323连续实施

W323独立Main.Privacy.TenantExportPayload.v1实际处置：准确租户制品、明确保留政策、独立申请/批准/执行、目标租户所有主体有效hold屏障及ABA；真实删除字节、撤访问、最小原回执/墓碑同commit，原生无所属删除/回填拒绝。无伪subject；本窗不实现备份恢复适配，有该Owner墓碑时现恢复入口明确失败并保持隔离，旧批准随source epoch失效。

Main `2f4eaab416eac5eff12cda327cdb9f76262ac290`，Handoff `34fcd5372b3280a1961f55f96fdf2945b20c3b57`，COORD单人固定DEV1/DEV2；W322 Main777/Handoff298已核两仓采用、准确清理和官方释放，实测3254秒。本窗累计75502秒，余约3.03h。 [本窗](../development/dispatch/20261108-wave323/README.md)。原24h仍active/incomplete。

## 2026-11-08：W322租户已分类导出 / 24h继续

W322完成Main.Privacy.TenantExport.v1：准确target tenant下已分类identity/security/roles及白名单租户元数据，明确用途/接收人、独立tenant能力、不可变制品/原操作/TTL；当前披露重查收件人/来源权限/绑定/期限/撤销和最终会话，逐源真实Manifest。没有伪subject，旧直接租户HTTP散流退役410；其他Owner全量导出和新租户载荷物理保留/处置及恢复仍为真实余项，整体PARTIAL。 [交付](../development/evidence/W322-01/DELIVERY.md)。

11项新增实际PostgreSQL18场景、5项受影响主体导出回归（含新增两人Chrome实际下载与既有主体Chrome）、18项Vue行为及vue-tsc通过。 原375=36DONE/2IN_PROGRESS/337PLANNED，原59=12闭环/47剩余，125目标/247正式验收不变。WP02-15仍余未分类安全内容/不可归属事件、授权/组织引用、OA/PLM/CRM业务历史、FILE及其他来源副本、整租户全部Owner导出、新租户制品受控物理保留/处置及恢复、WAL/备份/外部副本和完整环境恢复。W306、W249及已闭环FORM卡不重复；24h继续相邻真实余项，W310仅预算末尾。 最终分支/工作区/席位见wave322-final-verification.json。

## 2026-11-08：W321已闭环，W322连续实施

原WP02-15租户受控导出采用：准确target tenant的已分类identity/security/roles来源，显式用途、接收人、独立tenant能力、TTL和不可变制品/原操作；当前下载重判身份/范围/撤销/期限，真实Manifest含数量/读取窗/字段排除/未接Owner。独立租户模型无伪subject，整体仍PARTIAL，旧直接租户HTTP散流退役410；其他Owner全量导出与新租户制品物理保留/处置仍列真实余项。

Main `48420d71829daeb955198351696cc29aec33545a`，Handoff `bfaaf8edfc15f3085680fd7451d8a2ce9c616ca6`，COORD单人固定DEV1/DEV2；W321 Main776/Handoff297已核两仓采用、准确清理和官方释放，实测5775秒。本窗累计72040秒，剩余约3.99h；估1.75–3h低置信实际执行。原375/59及125/247不变。 [本窗](../development/dispatch/20261108-wave322/README.md)。原24h仍active/incomplete，不复做既有已完范围。

## 2026-11-08：W321安全字段处置 / 24h继续

W321完成Main.SecurityLog.Fields.v1：准确安全事件已分类UserName/ClientIp的显式UTC政策、独立申请/批准/实际置空、全主体hold、最小原回执/墓碑与原生防回填；恢复候选重施准确字段处置，已空及事件缺失分别据实记录，两库UNKNOWN责任和原键恢复直接复用。事件身份、原时间及未知内容保留，仅此Owner，整体PARTIAL。 [交付](../development/evidence/W321-01/DELIVERY.md)。

20项新增实际PostgreSQL18场景、14项受影响认证/身份/载荷恢复回归（含真实认证HTTP、两条新增和一条既有Chrome）、24项Vue/API行为及vue-tsc通过。 原375=36DONE/2IN_PROGRESS/337PLANNED，原59=12闭环/47剩余，125目标/247正式验收不变。WP02-15仍余安全日志未分类上下文/原因/设备与操作者信息、不可归属事件、授权/组织引用、OA/PLM与CRM业务历史、FILE及其他来源副本、整租户导出与受控处置、WAL/备份/外部副本及完整环境恢复。W306和WP12-03/04已闭环，不重复；24h继续相邻真实余项，W310仅预算末尾。 正常采用/finish后继续，最终分支/工作区/席位见wave321-final-verification.json。

## 2026-11-08：W320已闭环，W321连续实施

原WP02-15真实余项：Main.SecurityLog.Fields.v1按准确事件原件及已接受的UserName/ClientIp字段分类实施独立政策/预览/申请/批准/实际清理、全主体hold、原结果/墓碑和恢复采用。保留事件/stable subject/租户身份及其余未核字段，不宣称整个安全事件或整个隐私任务已匿名化。旧CreatedAt无时区，资格只按明确UTC NotBefore政策，不推算法定期限；单事件准确边界，不暗含批量或按同名归属。

Main `90bf9e9f23cab088e9e5dd7c82cc2de58190a7c6`，Handoff `f2c29f43e8e46425255f79bf0aafc17773150613`，COORD单人固定DEV1/DEV2，W320两仓实际采用/准确清理/官方释放已核销。18h采样见wave321-18h-calibration.json；本窗登记累计65842秒，尚余约5.71h；2.5–4h低置信实际Codex执行。原375/59及125/247不变。 [本窗](../development/dispatch/20261108-wave321/README.md)。不复做W306/FORM/七天/W249，原24h仍active/incomplete，下一72000秒校准。

## 2026-11-08：W320原生载荷处置 / 24h继续

W320完成Main.Privacy.ExportPayload.v1：准确原生导出载荷的显式UTC保留政策、独立申请/批准/实际删除、全主体hold、最小不可变原回执/墓碑与原生防复活；已接恢复候选实际删除旧载荷或据实记录缺失并安装屏障，两库UNKNOWN责任与原键恢复继续复用。仅此Owner，整体PARTIAL。 [交付](../development/evidence/W320-01/DELIVERY.md)。

20项新增实际PostgreSQL18场景、17项受影响旧导出/hold/身份恢复回归（含真实认证HTTP、两条新增和一条既有Chrome）、22项Vue/API行为及vue-tsc通过。 原375=36DONE/2IN_PROGRESS/337PLANNED，原59=12闭环/47剩余，125目标/247正式验收不变。WP02-15仍余安全日志内容、授权/组织引用、OA/PLM与CRM业务历史、FILE及其他来源副本处置、整租户导出/受控处置、WAL/备份/外部副本及完整环境恢复。原生载荷缺失而无处置事实时仍明确阻断，不冒删除成功。W306及WP12-03/04已闭环，不再当作阻塞；继续核对其他真实Owner，W310仅预算末尾。 正常采用/finish后继续，最终分支/工作区/席位见wave320-final-verification.json。

## 2026-11-08：W306已收尾 / W320载荷处置开工

W306 Main774/Handoff295正常采用、准确清理/官方finish核销，实测1577秒；原37536DONE/2IN_PROGRESS/337PLANNED、59余47。16h实测校准后[W320](../development/evidence/W320-01/DELIVERY.md)推进原生导出载荷实际处置/准确恢复副本，WP02-15整体仍PARTIAL。24h继续。

## 2026-11-08：WP12-08原卡核销 / 24h继续

W306在原WP12-04/07/10整卡前置满足后，按WP12-08三项范围及正反例核销当前动作上下文、真实双身份/原grant、双/三CAS、SFS复算/APP冻结TxD与轮次；补真实两人all/any重入新轮和旧原命令回放的业务验证，原WP12-08正式DONE。 [报告](../development/evidence/W306-01/DELIVERY.md)。

原375现36DONE/2IN_PROGRESS/337PLANNED，原59局部12闭环/47剩余；125完整目标0关闭、247正式验收NOT_RUN不变。全OA-TASK批量办理/管理转单、时限通知和完整目标验收不由本卡连带完成。下一核WP02-15既有导出载荷的真实保留/处置Owner，当前撤销下载只撤访问而不删除载荷；W310预算末尾。 正常采用/分支/工作区/席位以wave306-final-verification.json最终PASS为准。

## 2026-11-08：W319已收尾，W306任务轮次推进

W319原FORM两卡正常采用及官方finish核销，原37535DONE/2IN_PROGRESS/338PLANNED、59余48；[W306](../development/evidence/W306-01/DELIVERY.md)在整卡前置满足后核待办办理/多人新轮，已有W291抄送不重复。24h继续。

## 2026-11-08：WP12-03/04原卡核销，24h继续

W319按原WP12-03/04实施范围及FORM R1直接25条AC完成整卡核销；旧端点统一适配、合法数据限额、准确重基前恢复与历史内容当前授权采用均已有真实交付，两张原卡正式DONE。 [报告](../development/evidence/W319-01/DELIVERY.md)。

原375现35DONE/2IN_PROGRESS/338PLANNED；原59局部11闭环/48剩余。125完整目标0关闭、247正式验收NOT_RUN保持。W306所需WP12-04/10整卡前置已满足，原WP12-08仍待本卡范围核对；历史批量转换、物理purge、普通FORM服务节点业务Owner及正式部署保持各自范围。 本窗只做原范围/当前适用性/证据核销，无新产品运行。分支/工作区/席位以wave319-final-verification.json最终PASS为准。

## 2026-11-08：W318已收尾，W319原FORM核销中

W318 Main772/Handoff293正常采用、准确分支清理及官方finish已核销，实测4353秒。[按原WP12-03/04实施范围、](../development/evidence/W319-01/DELIVERY.md)逐原AC核实，当前尚未关闭原卡。原37533DONE/2IN_PROGRESS/340PLANNED、原59余50；24h继续。

## 2026-11-08：W318旧来源受控采用 / 24h继续

W318完成旧FORM准确版本到真实原生发布的显式当前来源映射、本人旧内容按当前字段受控披露，以及明确确认后复制为新本人原生草稿；旧行、精确值和UNVERIFIED_LEGACY历史标记保留。源/映射CAS及ABA、FILE实际资格、当前窗口/权限、最终会话和不可变来源/历史/原回执同事务；未知提交只恢复原命令。 [交付](../development/evidence/W318-01/DELIVERY.md)。

12项新增实际PostgreSQL18场景、13项受影响旧适配/快照恢复回归（含真实认证HTTP及两条Chrome流程）、16项Vue及vue-tsc通过。 原375=33DONE/2IN_PROGRESS/340PLANNED，原59=9闭环/50剩余，125目标/247正式验收不变。W305历史身份卡已DONE，原FORM汇总中仍称W305未完是旧记录，下一窗口按原AC统一核销WP12-03/04及W306前置；不以本局部窗口直接关闭整卡。无法准确映射的旧值/文件继续明确阻断并保留，历史转换执行与服务节点业务Owner按各原卡范围处理。隐私其他Owner仍有真实余项，W310预算末尾。 正常采用/finish后继续，最终分支/工作区/席位见wave318-final-verification.json。

## 2026-11-08：W315收尾 / W318旧历史采用

W315 Main771/Handoff292远端采用、准确分支删除与两席官方finish已核销，实测5837秒。累计有效约14h，24h目标继续。[旧草稿准确内容披露与显式向前采用](../development/evidence/W318-01/DELIVERY.md)按原WP12-03/04真实余项推进，不能把未核实历史当可信发布；原375/59计数不变。

## 2026-11-08：W315墓碑与受控新库恢复 / 24h继续

W315完成已接Main身份处置的不可变墓碑与原生写入屏障，真实新库备份恢复候选经独立源权威、冻结范围/版本、独立审批和当前能力重施准确处置；双库提交未知保留责任并显式接回原回执，候选隐私门在源结果及当前来源未核实前失败关闭。只覆盖Main.Identity.Profile.v1，整体PARTIAL。 [交付](../development/evidence/W315-01/DELIVERY.md)。

13项当前隔离PostgreSQL18恢复/墓碑场景、17项受影响原处置/批准回归（含真实认证HTTP和Chrome）、15项Vue行为及vue-tsc通过。 原375=33DONE/2IN_PROGRESS/340PLANNED，原59=9闭环/50剩余，125目标/247正式验收不变。WP02-15仍余导出载荷/安全审计/OA与PLM业务历史/FILE/CRM等其他Owner处置、整租户导出及受控处置、外部副本及完整环境恢复；W306仍待WP12-03/04历史准确内容披露/显式采用。 正常采用/finish后继续，最终分支/工作区/席位见wave315-final-verification.json。

## 2026-11-08：W314已收尾 / W315开工

W314两仓远端采用、准确分支删除和官方finish已核销。用户明确继续目标；暂停间隔不计。[W315](../development/evidence/W315-01/DELIVERY.md)推进准确墓碑/写入屏障和受控新库恢复，原375/59计数不变。

## 2026-11-08：W314身份实际处置 / 24h继续

W314完成Main身份Owner实际匿名化：准确已批准计划与当前execute/hold/政策/目标/审批人共同核对，原用户字段及认证材料处置、身份快照/优先Outbox和CRM授权撤销同事务，最小安全原回执、UNKNOWN查询/关闭迟到与真实页面执行。仅此Owner APPLIED，整体保持PARTIAL。 [交付](../development/evidence/W314-01/DELIVERY.md)。

17项当前独立PostgreSQL18场景（8项执行含真实认证HTTP/Chrome、9项受影响W313回归）、9项Vue交互及vue-tsc通过。 原375=33DONE/2IN_PROGRESS/340PLANNED、原59=9闭环/50剩余，125目标/247正式验收不变。WP02-15仍余W315墓碑/恢复及后续写入屏障、导出载荷/审计/业务历史/FILE/CRM/备份等Owner受控处置、整租户导出与受控处置和外部副本边界；W306原FORM历史前置仍单列。 正常采用/finish后W315；最终分支、工作区及席位见wave314-final-verification.json。

## 2026-11-08：恢复24h目标 / W314开工

W313两仓远端采用、准确分支删除和官方finish已核销。用户明确继续目标；暂停间隔不计。[W314](../development/evidence/W314-01/DELIVERY.md)推进已批准Main身份处置实际执行和原结果恢复，原375/59计数不变。

## 2026-11-04：W313处置计划/hold；收尾后暂停

W313完成Main主体资料的版本化政策、精确预览/冻结计划、独立批准/拒绝/撤销与全目标hold；原操作同事务恢复及关闭迟到，真实页面采用。旧主体/租户直接擦除入口410，批准本身不修改资料。 [交付](../development/evidence/W313-01/DELIVERY.md)。

10项独立PostgreSQL18场景（含真实认证HTTP、Chrome双身份申请/审批/hold及原结果恢复）、7项Vue交互、vue-tsc通过。 用户明确要求当前小任务完成后暂停，额度重置后再开工。W313正常合并、准确分支处置和官方finish后将24h目标设为PAUSED；本轮不登记或启动W314，不把暂停记为24h完成。 原375=33DONE/2IN_PROGRESS/340PLANNED，原59=9闭环/50剩余，125目标/247正式验收不变。WP02-15仍余W314逐Owner实际执行/UNKNOWN、W315墓碑与恢复、外部收件和通用FILE/CRM/备份适配、旧整租户导出及整租户受控处置采用。 最终分支/席位处置见wave313-final-verification.json。

## 2026-11-04：W312已收尾 / W313开工

W312 Main768/Handoff289正常合并，真实远端采用、准确分支删除、固定工作区干净、官方席位释放。[W313](../development/evidence/W313-01/DELIVERY.md)推进精确主体处置预览、独立批准和hold。24h目标ACTIVE，原375/59计数不变。

## 2026-11-04：W312主体持久导出 / 24h继续

W312完成Main主体持久导出：精确用途/来源/内部收件人、同事务原操作与独立载荷、当前分权下载及撤销、UNKNOWN原结果查询和迟到关闭；真实页面分开生成与下载。 [交付](../development/evidence/W312-01/DELIVERY.md)。

14项独立PostgreSQL18场景（含实际认证HTTP、Chrome完整生成/恢复/下载/撤销及W311归属回归）、11项Vue/API行为、vue-tsc通过。 原375=33DONE/2IN_PROGRESS/340PLANNED，原59=9闭环/50剩余，125目标/247正式验收不变。WP02-15仍余精确处置计划/独立批准/hold、逐Owner实际处置及UNKNOWN、tombstone与恢复、外部收件和通用FILE/CRM/备份适配、旧整租户导出及擦除入口采用。 分支/工作区/席位最终见wave312-final-verification.json。

## 2026-11-04：W311已收尾 / W312开工

W311 Main767/Handoff288真实远端采用、分支删除、固定席位释放。Handoff502后PR元数据CLOSED，真实正常合并9308d2已通过官方完整补丁证明核销。[W312](../development/evidence/W312-01/DELIVERY.md)推进持久导出与当前下载。24h目标ACTIVE，原375/59计数不变。

## 2026-11-04：W311主体逐Owner清单 / 24h继续

W311在实际主体导出中采用用户、安全事件及稳定角色关联的显式字段投影、逐来源Manifest、未知覆盖与排除说明；页面展示真实部分覆盖，缺源直接失败。 [交付](../development/evidence/W311-01/DELIVERY.md)。

5项独立PostgreSQL18场景（含真实认证HTTP及Chrome下载）、2项既有主体导出单测、4项Vue行为及vue-tsc通过。 原375保持33DONE/2IN_PROGRESS/340PLANNED；原59保持9闭环/50剩余，125目标与247正式验收不变。WP02-15仍有持久导出/当前下载、用途/接收方/批准、逐Owner处置/hold/UNKNOWN/墓碑恢复及各域实际适配。 分支/工作区/席位最终见wave311-final-verification.json。

## 2026-11-04：W309已交付 / W311开工

W309 Main766/Handoff287已采用；两分支删除、固定工作区干净、席位释放。Handoff删除曾遇GitHub500，官方finish准确重试已恢复。[W311](../development/evidence/W311-01/DELIVERY.md)补主体逐Owner清单及分类；原37533DONE/2IN_PROGRESS/340PLANNED、59余50；24h继续。

## 2026-11-04：W309后继需求影响 / 24h继续

W309实现需求集准确后继差异、实际Main ITER采用影响请求、负责人评估、晚到采用原子登记及批准/迭代推进门禁；旧输入与历史保留。 [交付](../development/evidence/W309-01/DELIVERY.md)。

12项独立PostgreSQL18场景（含实际FLOW、真实Chrome和2项既有迭代/应用回归）、26项Vue行为及vue-tsc通过。 原375保持33DONE/2IN_PROGRESS/340PLANNED；原59保持9闭环/50剩余；125目标/247正式验收不变。WP05-14仍有成员排除、商业原Owner和ECM/DEV完成及其他消费者适配；WP05-18其他原卡余项保留。 分支/工作区/席位见wave309-final-verification.json。

## 2026-11-04：W308已交付 / W309开工

W308 Main765/Handoff286已准确采用，两任务分支删除、固定工作区干净、席位释放。 [W309](../development/evidence/W309-01/DELIVERY.md)处理实际迭代采用链/后继影响。原37533DONE/2IN_PROGRESS/340PLANNED、59余50，24h继续。

## 2026-11-04：W308来源范围与裁决 / 24h继续

W308实现内部来源准确条款的有权范围/必须属性确认、需求集真实交集与澄清/范围分离裁决，保ECM/DEV待专业评估与不利冲突历史；原键和真实页面恢复闭环。 [交付](../development/evidence/W308-01/DELIVERY.md)。

12项独立PostgreSQL18场景（含真实Chrome及4项既有需求/评价/集合回归）、1项准确时间/条款并集行为测试、20项Vue行为与vue-tsc通过。 原37533DONE/2IN_PROGRESS/340PLANNED；原59局部9闭环/50剩余。WP05-16依据W307最终远端及finish证明正式核销；WP05-14保持PLANNED，W309后继逐条差异/实际消费者影响仍未完成，成员排除、商业原Owner及ECM/DEV实际完成结果独立；125/247保持。 分支/工作区/席位见wave308-final-verification.json。

## 2026-11-04：W307已交付 / W308核销中

W307 Main764/Handoff285完整采用、分支准确删除、固定工作区干净、两席位释放；[W308](../development/evidence/W308-01/DELIVERY.md)按W307远端证明核销WP05-16；本窗处理需求集原余项，WP05-14保持未完。原37533DONE/2IN_PROGRESS/340PLANNED、59余50，24h继续。

## 2026-11-04：W307准确修订独立可用性 / 24h继续

W307补齐受控文档准确修订的独立暂停/恢复/撤回、当前消费阻断与原操作恢复；内容、批准事实和历史引用不改写。 [交付](../development/evidence/W307-01/DELIVERY.md)。

11项独立PostgreSQL18场景通过（9项新增含真实Chrome，2项既有原件/完整性回归），22项Vue行为及vue-tsc通过；原FILE和专业评审证据按准确适用范围复用，不称本轮重跑。 原375暂32DONE/2IN_PROGRESS/341PLANNED，原59余51；WP05-16原范围逐项已具备核销证据，但等待本窗远端采用和finish后再正式改DONE。完整PLM-DOC目标、隐私全域编排、现场副本召回、LCM和环境推广不连带完成；125/247不变。 分支/工作区/席位见wave307-final-verification.json。

## 2026-11-04：W305已交付 / W307核销中

W305 Main763/Handoff284完整采用、分支准确删除、固定工作区干净、两席位释放；[W307](../development/evidence/W307-01/DELIVERY.md)沿原PLM-DOC合同实现独立可用性，尚未关闭原卡。原37532DONE/2IN_PROGRESS/341PLANNED、59余51，24h继续。

## 2026-11-04：WP12-10原卡核销，24h继续

W305按原WP12-10三项范围及正反验收核销委派修订、准确实际身份和历史缺证边界，原卡正式DONE；历史转换按原TASK R2 §5.1仍为独立迁移设计，不新增执行入口。 [报告](../development/evidence/W305-01/DELIVERY.md)。

原375现32DONE/2IN_PROGRESS/341PLANNED；原59局部8闭环/51剩余。125完整目标0关闭、247正式验收NOT_RUN保持，其他卡不连带关闭。 复用有效产品执行，本窗不跑重复测试。分支/工作区/席位见wave305-final-verification.json；下一核FORM历史兼容真实余项与W306前置。

## 2026-11-04：W317已交付 / W305核销中

W317 Main762/Handoff283完整采用、分支准确删除、固定工作区干净、两席位释放；[W305](../development/evidence/W305-01/DELIVERY.md)沿原TASK历史迁移设计边界核对真实剩余，尚未关闭原卡。原37531DONE/2IN_PROGRESS/342PLANNED、59余52，24h继续。

## 2026-11-04：W317准确前快照恢复 / 24h继续

W317重基前准确快照显式恢复为新本人草稿，保旧发布版本、精确值、源草稿及完整动作链；当前字段投影、留存、FILE可用性和同事务来源/回执约束，页面可确认与恢复未知原命令。 [交付](../development/evidence/W317-01/DELIVERY.md)。

9项独立实际PostgreSQL18场景通过：准确认证HTTP恢复、权限/字段投影/CAS、留存与回执回滚、未知提交与缺guard、缺来源提交约束、FILE两代恢复/原件绑定/提交，以及原FILE回归、实际Chrome新恢复场景及BUG759刷新恢复回归。22项有效Vue行为结果与vue-tsc通过；每项采用最近有效结果，未受影响的成功证据准确复用。 原375仍31DONE/2IN_PROGRESS/342PLANNED，原59仍余52；125/247不变。原WP12-03/04的旧历史准确来源/内容披露与采用余项仍未关闭，下一W305实际历史身份链核对及相邻真实缺口；24h目标继续ACTIVE。 分支/工作区/席位见wave317-final-verification.json。

## 2026-11-04：W316已远端交付 / W317执行中

W316旧入口采用及BUG759恢复入口修复已完整采用、准确清理分支及释放席位；[W317](../development/evidence/W317-01/DELIVERY.md)从最新main实现准确前快照恢复，尚未验证或交付。原37531DONE/2IN_PROGRESS/342PLANNED、原59余52，24h单人目标继续。

## 2026-11-04：BUG759原命令刷新恢复

个人草稿原命令恢复独立于活动列表行挂载；删除／提交丢答后刷新仍可查询准确原结果，坏存储和未知状态继续阻止新意图。 [修复与验证](../development/evidence/BUG-759/DELIVERY.md)。

2项实际PostgreSQL18/登录HTTP/Chrome场景通过：删除与提交成功丢答、刷新查询、未到服务端NOT_OBSERVED后明确关闭，以及既有隐藏字段处置重基的原命令恢复。16项受影响编辑器行为测试和vue-tsc通过，无跳过冒充通过。

原卡余项继续W317/W305，24h目标继续。分支清理、工作区和席位以bug759-final-verification.json核对。

## 2026-11-04：W316旧表单入口采用 / 24h继续

W316旧创建/保存/重基/提交/删除HTTP入口共用实际PersonalFormOwnerV2，保旧DTO成功形状、准确前驱与固定命令映射；历史目录只读且来源明确，补取消/冲突保行/删除刷新实际页面。 [交付](../development/evidence/W316-01/DELIVERY.md)。

9项独立实际PostgreSQL18场景通过：6项旧接口/映射/数据边界场景、1项实际登录Chrome全链路、2项受影响原生提交原子性/并发回归。历史目录3项Vue行为通过（按各场景最近有效结果复用），主入口更新后的vue-tsc通过。模拟业务输入、实际内部Owner/HTTP/PG/Chrome，无内部API替身。 原37531DONE/2IN_PROGRESS/342PLANNED，原59余52；125/247状态不改。下一独立BUG759待复现/修复，再W317准确前快照新草稿恢复及W305后续；24h目标持续ACTIVE。 分支/工作区/席位见wave316-final-verification.json。

## 2026-11-04：W304已远端交付 / W316执行中

W304原卡核对及BUG756容量修复已完整采用、准确清理分支及释放席位；[W316](../development/evidence/W316-01/DELIVERY.md)从最新main采用旧个人表单入口，尚未验证或交付。原37531DONE/2IN_PROGRESS/342PLANNED、原59余52，24h单人目标继续。

## 2026-11-04：BUG756个人FORM数据预算修复

个人FORM的1 MiB业务数据预算与命令外层开销分离；内部存储JSON仅作必要转义，中文、补充平面和私用区Unicode不再被二次转义挤出合法范围。 [修复与验证](../development/evidence/BUG-756/DELIVERY.md)。

本地PostgreSQL18和实际Main Program认证HTTP通过：原生/HTTP满额提交、双预览、草稿创建/保存/提交、退回编辑和SFS决策；超出1字节明确拒绝且无额外效果，原意图回放、原摘要、精确数值及退回前快照保留。另验证中文满额HTTP、补充平面/私用字符经merge/patch/rules/projection、合并后真实超限拒绝、嵌套数组/控制字符/空值/布尔和精确数字。当前隐藏/只读mask、重基规则及原结果、五上下文closed DTO和canonical回归均通过。

原卡余项继续W316/W317，24h目标继续。分支清理、工作区和席位以bug756-final-verification.json核对。

## 2026-11-04：W304原表单卡核对 / 24h继续

W304核清WP12-03/04原25条直接AC和前置，补3项真实提交原子性/五上下文验证；确认#756合法data限额缺陷、旧入口adapter和准确重基前快照恢复仍未完，两原卡保持未关闭。 [交付](../development/evidence/W304-01/DELIVERY.md)。

3项新增实际PostgreSQL18场景通过：真实首人工任务写失败全事务回滚/同意图恢复；两连接不同提交命令只一原生提交和原回执；五expected上下文逐项不匹配在DIRECT/DRAFT均拒绝且保原草稿。原生业务行为未修改，其他有效PG/HTTP/Chrome证据按原窗口复用，本窗未新跑HTTP/浏览器/UI或全仓。 原37531DONE/2IN_PROGRESS/342PLANNED，原59余52，125/247正式状态不改。下一独立BUG756，随后W316旧入口适配、W317准确前快照恢复，再续W305及原队列；24h目标持续ACTIVE。 分支/工作区/席位见wave304-final-verification.json。

## 2026-11-04：W303已远端交付 / W304执行中

W303实际字段处置Owner已完整采用、准确清理分支及释放席位；[W304](../development/evidence/W304-01/DELIVERY.md)从最新main核对原提交卡和草稿直接前置，尚未验证或交付。原37531DONE/2IN_PROGRESS/342PLANNED、原59余52，24h单人目标继续。

## 2026-11-04：24小时目标 / W303受控字段处置到真实重基

W303：真实隐藏损失请求、合格处置事实到本人原生重基的准确一次消费，以及当前资格/政策/hold校验、原件留存和原命令恢复完成本地业务验收。 [交付](../development/evidence/W303-01/DELIVERY.md)。

10项独立PostgreSQL18场景通过，含真实Main认证HTTP/Chrome、留存到实际重基消费、双连接hold竞争、原回执失败全回滚、提交丢答原命令恢复、混合损失及政策/源/目标/资格变化；3项既有映射单测、13项Vue行为测试及vue-tsc通过。

原375为31DONE/2IN_PROGRESS/342PLANNED，原59局部7闭环/52余项；125完整目标0闭环、247正式NOT_RUN不变。W304继续原个人表单卡及直接前置全范围核销，不能以本次子能力提前关闭整卡。WP02-15逐Owner导出/擦除、下载、UNKNOWN责任与墓碑编排继续按原范围推进。 两仓采用/清理/官方finish后继续W304。分支、工作区与席位以wave303-final-verification.json核销。

## 2026-11-04：W302已远端交付 / W303执行中

W302实际字段处置Owner已完整采用、准确清理分支及释放席位；[W303](../development/evidence/W303-01/DELIVERY.md)从最新main接入真实重基消费，尚未验证或交付。原37531DONE/2IN_PROGRESS/342PLANNED、原59余52，24h单人目标继续。

## 2026-11-04：24小时目标 / W302实际字段受控处置

W302：实际FORM字段受控处置Owner、准确历史原件、独立当前读取/批准/留存/撤销/hold资格、原操作恢复和处理工作台完成本地业务验收。W303重基消费尚未完成。 [交付](../development/evidence/W302-01/DELIVERY.md)。

9项独立PostgreSQL18场景通过，含Main真实认证HTTP、实际Chrome、双连接竞争、丢失提交回应、日志失败回滚与一项既有重基行为回归；6项受影响Vue行为测试和vue-tsc通过。

原375为31DONE/2IN_PROGRESS/342PLANNED；原59局部7闭环/52余项，125完整目标0闭环及247正式NOT_RUN保持原范围。WP12-03还需W303消费与W304原卡/直接前置核销；WP02-15的全域逐Owner导出/擦除、UNKNOWN责任、墓碑及下载编排继续未完。 两仓采用/清理/官方finish后继续W303。分支、工作区及席位以wave302-final-verification.json核销。

## 2026-11-04：WP12-19闭环 / W302执行中

WP12-19按原范围核销DONE：W301逐项审计识别的BUG751已在远端main修复并关闭，当前五Owner撤回/共同控制、原授权连续性、回执链和终态边界均有准确证据。原375变31DONE/2IN_PROGRESS/342PLANNED，原59局部7闭环/52余项；125/247保持原状态。 [核销与当前窗口](../development/evidence/W302-01/DELIVERY.md)。

W302实际受控字段处置Owner开工，尚未验证/交付；约24h单人目标继续。前窗分支已清理、工作区干净、席位已释放后复用；当前DEV1/DEV2由W302独占。

## 2026-11-04：BUG751原授权连续性修复

ITER普通BLOCKED应用重试采用原回执的实际人、委托人及准确业务grant版本；替换/修订grant或改变本人/受托方式必须显式再授权。 [修复与验证](../development/evidence/BUG-751/DELIVERY.md)。

四项PG18原步骤修复前均失败（旧代码未拒绝换授权），修复后通过；覆盖grant撤销后替换、同grant版本修订、受托转本人、本人转受托，每项验证新明确授权连续回执、相同新授权后续真正APPLIED及旧回执/原结果不变。替换grant场景通过实际Main Program认证HTTP验证普通403与显式再授权成功。既有不同实际人必须显式再授权的PG回归通过。

原卡待远端采用后的总控核销，24h目标继续W302。分支清理、工作区和席位以bug751-final-verification.json核对。

## 2026-11-04：W301审计与BUG751优先

W301原WP12-19审计发现BUG751：ITER普通BLOCKED重试只锁实际人，未锁原业务grant/授权方式；原卡暂不关闭，下一独立BUG修复后重新核销。 [审计报告](../development/evidence/W301-01/DELIVERY.md)。

原375仍30DONE/2IN_PROGRESS/343PLANNED；原59局部6闭环/53余项，125完整目标及247正式验收不变。五Owner撤回已采用，当前明确原卡缺口为BUG751；版模实物终结、规则激活属于后续业务卡。 24h目标继续；原卡仍未完成，BUG独立分支先复现修复后再核销。分支/工作区/席位以wave301-final-verification.json核对。

## 2026-11-04：24小时目标 / W300库位规则审批撤回

W300：库位规则实际Owner可信审批撤回、原提案共同控制版本/应用竞争、独立业务委派与BLOCKED明确接续、原命令和真实页面恢复完成本地验收；旧提案入口、原件和APPROVED_PENDING未激活边界保留。 [交付](../development/evidence/W300-01/DELIVERY.md)。

14项独立PostgreSQL18场景通过，含Main认证HTTP、真实Chrome和3项旧库位审批/提案竞争回归；18项受影响Vue/API行为测试、vue-tsc通过。相同未变输入直接复用，最终回归覆盖存储回执读取的内部契约精简。

原375保持30DONE/2IN_PROGRESS/343PLANNED；原59局部6闭环/53余项，125完整目标0闭环及247正式NOT_RUN保持真实范围。WP12-19待W301逐项整卡核销，本窗不提前关闭原卡；规则激活/库存效果继续属于原业务任务。 正常远端采用/finish后W301逐项核销。分支/工作区/席位以wave300-final-verification.json核销。

## 2026-11-04：退回审批合法撤回 / BUG #747

DOC、需求集和迭代的撤回运输接收合法RETURNED状态，仍在外部响应边界拒绝未知状态；原body、原命令、原人员和当前工作流版本保持不变。 [修复与证据](../development/evidence/BUG-747/DELIVERY.md)。

3项实际PostgreSQL18场景修复前均在合法RETURNED上下文处失败，修复后通过：实际任务退回发起人、实际Owner冻结撤回事实、运输提交后丢响应、原键查询恢复且只POST一次、重复运行不重投、业务原命令准确回读、不可变outbox字节不变。每项同时验证未知状态明确拒绝。

历史CONFLICT保持原终态，未检查或迁移既有环境。原375/59/125/247不变；24h目标继续，下一W300。分支清理和席位释放以bug747-final-verification.json为准。

## 2026-11-04：24小时目标 / W299版模审批撤回

W299：版模处置实际Owner审批撤回、独立业务委派/BLOCKED显式接续、共同控制版本、原操作与运输恢复及真实页面完成本地验收；保原处置请求、物理停用和全部未清义务。 [交付](../development/evidence/W299-01/DELIVERY.md)。

11项独立实际PostgreSQL18场景通过（含Main认证HTTP、Chrome与三项原版模应用回归）；11项Vue行为单测、vue-tsc通过。相同未变输入复用已通过结果；审查修改的当前读取上下文复用与合法RETURNED输入追加3项相关回归通过。

原375保持30DONE/2IN_PROGRESS/343PLANNED；原59局部6闭环/53余项，125完整目标与247正式验收不变。WP12-19仍待库位规则等实际采用与整卡核销。版模现应用仅BLOCKED/STALE/CONFLICT，缺少真实终结参与方；本窗没有APPLIED或实际报废/库存/财务效果。 2小时校准已落计划，新增原WP02-15滚动储备。正常远端采用/finish后修BUG747，再W300。分支/工作区/席位以wave299-final-verification.json核销。

## 2026-11-04：需求集审批详情直达 / BUG #744

需求集审批详情URL已注册并独立加载准确对象，submission传给既有评审面板；页面直接刷新保持轮次，切换对象丢弃旧响应，当前权限拒绝清理私有内容。UUID大小写保持同一身份。 [修复与证据](../development/evidence/BUG-744/DELIVERY.md)。

33项相关Vue组件及实际应用路由启动行为通过，vue-tsc通过；真实Chrome直接打开需求集/准确送审URL，创建本轮授权、实际提交后丢响应、直接刷新URL并恢复原结果，实际Main API/PostgreSQL18核对原事实未改写。

原375/局部卡/125/247不变，24h目标继续；远端采用、Issue关闭、分支清理及席位释放以bug744-final-verification.json为准。

## 2026-11-04：24小时目标 / W298需求集可信撤回

W298：需求集实际Owner可信撤回、原审批应用共control、准确本轮业务委派/BLOCKED显式接续、不可变原结果及实际页面恢复完成本地验收。 [交付](../development/evidence/W298-01/DELIVERY.md)。

11个不同的实际PostgreSQL18场景有成功执行，含认证HTTP、两个连接竞争/回滚、准确授权、前向采用和真实Chrome原结果恢复；22项受影响Vue行为及vue-tsc通过。逐方法复用有效来源，早期失败记录保留。

原375保持30DONE/2IN_PROGRESS/343PLANNED；原59局部6闭环/53余项，125完整目标与247正式验收不变。WP12-19仍待版模处置、库位规则等实际Owner采用及整卡核销，本窗不关闭原卡。 正常远端采用/finish后先修[BUG744](https://github.com/GTX537/CP6/issues/744)，再W299。分支/工作区/席位以wave298-final-verification.json核销。

## 2026-11-04：新24小时目标 / W297 DOC可信撤回

W297：DOC实际Owner可信撤回、同控制头应用互斥、独立本轮业务委派/BLOCKED明确接续、原结果恢复与运输已完成相关本地验收。 [交付](../development/evidence/W297-01/DELIVERY.md)。

15个不同的实际PostgreSQL18测试场景有对应成功执行，含真实认证HTTP、两连接竞争、旧提交前向采用、真实Chrome明确再授权/丢响应刷新恢复；21项相关Vue行为及最终vue-tsc通过。复用逐方法有效来源，不把早期失败/未执行改称通过。

原375保持30DONE/2IN_PROGRESS/343PLANNED；原59局部6闭环/53余项，125完整目标与247正式验收状态不变。WP12-19仍待需求集、版模处置、库位规则等实际Owner采用及整卡核销；本项不关闭原卡。 最终分支/工作区/席位状态按wave297-final-verification.json核销。

## 2026-11-03：原APP提交卡与24h执行收口

W296核销原WP12-06可信审批绑定、冻结提交TxS/TxE；原合同合法Key修复已采用，三层去重/alias、旧回调隔离及无实例当前业务读面证据完整。 [本轮结果、53卡余项与工时](../development/evidence/W296-01/DELIVERY.md)。

原375为30DONE/2IN_PROGRESS/343PLANNED；原59局部6闭环/53剩余。本24h周期新增整卡闭环WP12-01、WP12-02、WP12-06，起点为27DONE及56剩余。125完整目标0关闭、247正式验收NOT_RUN不变。 29个串行窗口（含W296核销）和9个BUG闭环；最终提交前截至2026-11-03T15:06:36Z，累计有效执行86041秒（23小时54分1秒），进入用户所定约24小时的预算末尾；已排除2小时5分58秒调度空档。随后当前任务必要的正常合并、清理和席位释放继续按实计入最终证明，不再开下一开发窗口，也不为整数24小时空等或重复验证；最终实际时长如实报告。 最终时间与远端采用、分支删除、工作区和席位以wave296-final-verification.json核销。下一优先其他实际业务Owner撤回、隐藏字段处置Ref及任务前置核销。

## 2026-11-03：APP业务Key原合法域 / BUG #740

APP绑定与提交入口恢复原oa.app/2.0允许的100字符bizType/subjectKind，按Unicode字符计数，101明确拒绝，原字符串无截断或规范化。 [修复与证据](../development/evidence/BUG-740/DELIVERY.md)。

6项相关本地行为通过、0跳过：真实Main登录HTTP→PostgreSQL保存100个补充字符业务Key及100个中文主体Key，原件/重放/历史完整；101分别拒绝且无新命令。两个wire边界与既有封闭输入、三层去重/alias、跨Scope/伪造冻结事实拒绝回归通过。

原37529DONE/2IN_PROGRESS/344PLANNED，原59余54；24h目标继续，下一末轮真实余项盘点。远端采用、Issue关闭、分支与席位以bug740-final-verification.json核销。

## 2026-11-03：WP12-02整卡核销，24h目标继续

W295按原WP12-02逐项核销发布预览、准确依赖pin与独立启停；BUG736已修复并远端采用，原卡正式DONE。 [报告](../development/evidence/W295-01/DELIVERY.md)。

原375现29DONE/2IN_PROGRESS/344PLANNED；原59局部5闭环/54剩余。125完整业务目标0关闭、247正式验收NOT_RUN保持；其他卡不连带关闭。 复用原有效产品执行，后续原WP12-06真实余项；分支/工作区/席位以wave295-final-verification.json最终核销。

## 2026-11-03：FLOW发布未知结果恢复 / BUG #736

FLOW发布或创建下一草稿结果未知后，权限拒绝、冲突重试与页面重开保留同一原命令；存储失败先阻止发送，损坏恢复记录保留并阻止新操作。父页面仅响应身份相关storage事件。 [修复与证据](../development/evidence/BUG-736/DELIVERY.md)。

23项相关Vue行为通过，包含真实父子组件联动：原结果查询403清除私有展示但保留恢复锁，随后原结果成功只重读当前定义、不重发命令；vue-tsc应用检查通过。初次明确拒绝可释放意图，未知后的拒绝不能证明未提交；缓存写失败不发HTTP，损坏缓存不伪造成功。

原375/局部卡/125/247状态不变，24h目标继续；远端采用、Issue关闭、分支清理和席位释放按bug736-final-verification.json核销。

## 2026-11-03：24小时目标 / W294FLOW发布预览引用影响

W294：FLOW发布预览复用真实当前引用影响统计，纠正旧未采用标记并在既有页面展示；当前租户/权限、原pin/启停和发布原事务保持。 [交付](../development/evidence/W294-01/DELIVERY.md)。

3项实际PostgreSQL18行为通过，含新增真实Main HTTP/跨租户影响统计、原pin/activation保持与当前撤权，以及既有提交未知原结果和最终预览到期回滚；8项相关Vue行为及应用vue-tsc --build通过，0跳过。未执行新浏览器到数据库全链。

原375仍28DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项，125完整目标与247正式验收状态不变。WP12-02待独立BUG736原命令恢复修复与最终整卡核销。 最终分支/工作区/席位状态按wave294-final-verification.json核销。

## 2026-11-03：24小时目标 / W293重基预览分类与准确诊断

W293：重基预览准确版本/摘要、可见路径分类和权威动态必填/逐行超限诊断，页面明确损失确认及编辑失效已实现；全部原行/精度/原事务和原命令保留。 [交付](../development/evidence/W293-01/DELIVERY.md)。

5项唯一后端行为通过（2项实际PostgreSQL18，其中新增Main HTTP预览到重基/阻止提交，3项精确值/权限规则纯行为），10项相关Vue行为、应用vue-tsc --build通过，0跳过。准确目标摘要、动态必填、逐行超限、无隐式转换、全部原行和原命令/历史保留均实际验证；未执行新的浏览器到数据库全链。

原375仍28DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项，125完整目标与247正式验收状态不变。WP12-03隐藏损失受控处置Ref及原卡/直接前置核销尚待完成。 最终分支/工作区/席位状态按wave293-final-verification.json核销。

## 2026-11-03：24小时目标 / W292历史身份缺证披露

W292：历史任务读面明确身份缺证与准确原FormTo引用，保留原字段/旧链并区分未核实身份；受控LegacyPrincipalMapping迁移设计边界已记录，本轮不执行转换。 [交付](../development/evidence/W292-01/DELIVERY.md)。

本地隔离PostgreSQL18/真实Main HTTP旧引擎身份场景通过，另2项既有详情行为通过；1项Vue生命周期/原记录展示行为通过，显式应用vue-tsc --build通过。旧任务v2入口404、无权读403、无原历史改写/无v2投票均实际验证；没有执行迁移或新浏览器到数据库链。

原375仍28DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项，125完整目标与247正式验收状态不变。WP12-10及其定义发布前置尚待整卡核销；WP12-04/08原依赖保留。 最终分支/工作区/席位状态按wave292-final-verification.json核销。

## 2026-11-03：WP12-08范围核对，正式前置继续

W291按原卡逐项核销WP12-08任务办理：SFS三版本/APP双版本、服务器实际分型、当前双身份与SOD、轮次计票、原命令/TxD、稳定TaskId及CC拒绝审批均有实际证据；正式整卡前置WP12-04/10尚未核销，保持PLANNED。 [报告](../development/evidence/W291-01/DELIVERY.md)。

原375仍28DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项。125完整业务目标0关闭、247正式运行验收NOT_RUN保持；其他OA卡与部署状态不连带完成。 本轮补2项真实CC HTTP权限边界，其他证据复用原执行；分支/工作区/席位按wave291-final-verification.json最终核销。

## 2026-11-03：SFS意见完整保存 / BUG #730

SFS意见按Unicode字符计数，1000个补充字符不截断，1001保留全文并禁提交；三张工作流运行记录的PostgreSQL容量同步为1000个字符，保留尾空格。 [修复与证据](../development/evidence/BUG-730/DELIVERY.md)。

真实Chrome先复现1000个🙂被截为500；UI修复后真实POST进一步发现旧PG UTF16约束503。最终Chrome/登录HTTP/PG完整1000字符保存通过；定向PG行为验证1001拒绝且无票、三张运行表精确1000含尾空格保存与1001数据库约束拒绝、原命令回放；原APP500字符/命令兼容回归通过。9项相关Vue行为、显式应用vue-tsc通过。

原375/局部卡/125/247状态不变，24h目标继续；最终远端采用、Issue关闭、分支清理及席位释放按bug730-final-verification.json核销。

## 2026-11-03：24小时目标 / W290统一动作与APP原命令

W290：统一任务动作上下文与服务端SFS/APP分型、APP accepted原命令五字段与同事务WFC alias、真实APP办理页及未知结果恢复已闭环。 [交付](../development/evidence/W290-01/DELIVERY.md)。

实际隔离PostgreSQL18及HTTP通过统一SFS/APP服务端分型、真实版本/固定引用/主体、封闭输入、APP五字段与原命令身份、WFC alias不可变、同事务回滚和未知提交/撤权后恢复；7项既有APP行为复用本窗通过结果。3条真实Chrome链通过（APP办理恢复、SFS三版本编辑、目录发布与代理运行），新APP链500个补充Unicode字符无损到数据库；24项相关Vue行为通过，显式应用vue-tsc通过。未跑全仓。

原375仍28DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项，125完整目标与247正式验收状态不变。WP12-08仍需原卡逐项核证，不由本项自动关整卡；独立BUG730待修复并实际复现回归。 最终分支/工作区/席位状态按wave290-final-verification.json核销。

## 2026-11-03：24小时目标 / W289任务编辑三版本

W289：SFS待办已采用字段补丁与任务/实例/表单数据三版本，固定节点与当前IAM/委派权限相交，表单/运行变量/决定/历史/文件绑定/原结果同事务；真实编辑页面、选项与附件及未知结果恢复闭环。 [交付](../development/evidence/W289-01/DELIVERY.md)。

实际隔离PostgreSQL18/HTTP验证SFS三版本、精确补丁/重算、节点/IAM/委派权限、当前字典、文件绑定、两连接竞争及故障原子回滚；15项既有任务动作回归通过。3条实际Chrome链通过（新编辑/代理办理/退回重送），文件链使用真实Defender。46项相关Vue行为通过，新增清空补丁行为通过，显式应用vue-tsc通过。各次执行及复用范围见final-validation.json，未跑全仓。

原375仍28DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项，125完整目标与247正式验收状态不变。WP12-08仍需原卡逐项核证及统一action-context/APP WFC适配等真实余项，不能由本项自动关整卡。 最终分支/工作区/席位状态按wave289-final-verification.json核销。

## 2026-11-03：WP12-01整卡已核销，24h目标继续

W288按原卡核销WP12-01不可变流程表单草稿与设计器版本：全部卡内范围、正反验收和Main共同事务/审计直接前置已有实际采用与有效证据；正式卡状态改DONE。 [报告](../development/evidence/W288-01/DELIVERY.md)。

原375现28DONE/2IN_PROGRESS/345PLANNED；原59局部4闭环/55余项。125完整业务目标0关闭、247正式运行验收NOT_RUN保持，WP12-02发布/运行与实际服务Owner、WP12-03/04个人表单、WP12-08任务三版本编辑等其他原卡不被连带关闭。 本轮复用原实际测试，文档检查不称新业务执行；下一SFS任务字段补丁/三版本。分支/工作区/席位按wave288-final-verification.json最终核销。

## 2026-11-03：24小时目标 / W287共享字典选项

W287：版本化共享字典选项来源已被FORM设计、发布/激活、当前候选读取、草稿/直接提交/退回/重基及真实页面采用；最终服务端复核当前来源与准确字符串值，保留历史原值且不制造历史字典快照。 [交付](../development/evidence/W287-01/DELIVERY.md)。

19项唯一PostgreSQL/HTTP行为、1条实际Chrome/Vue→Main→PostgreSQL完整链及7项schema合同用例通过，0跳过；23项相关Vue行为与vue-tsc --build通过。相同输入证据按实际来源复用。

原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3闭环/56余项，125/247正式状态不改。下一步按原WP12-01整卡逐项核证，再读原OA-TASK/SFS编辑合同；实际业务Owner专用选项与服务节点仍需独立实际接入。 最终分支/工作区/席位状态按wave287-final-verification.json核销。

## 2026-11-03：直接提交附件权限修复 / BUG #725

普通FORM附件准备接受当前已登记的新增草稿或直接提交权限；仅有query/submit的发起人可上传后直接提交，不创建草稿、不取得add权限。 [修复与证据](../development/evidence/BUG-725/DELIVERY.md)。

35项唯一受影响自动化行为通过：33项实际PostgreSQL/HTTP行为（包含真实Defender扫描和3条HTTP场景），2项纯重基行为；0跳过。34项集中回归通过后，仅新登记动作边界及修正实际v2草稿计数的3项定向复验通过，未重复未受影响结果。

原375与局部卡状态保持；远端采用/Issue关闭/准确分支清理/席位释放由bug725-final-verification.json核销，24h目标继续。

## 2026-11-03：24小时目标 / W286定义并发与旧入口

W286：FLOW两真实作者同token/下一草稿竞争、旧定义写入与页面调用面处置、当前权限下的明确未核实历史来源已完成限定验收；缺版本绑定不再推导历史pin，办理在修改票据前快速失败。 [交付](../development/evidence/W286-01/DELIVERY.md)。

33项唯一相关PostgreSQL/HTTP行为与2条实际Chrome/Vue→Main→PostgreSQL链通过，0跳过；52项相关Vue行为及vue-tsc --build通过。历史运行/规则行为489项通过或按真实来源复用；6项W283已记录的SQLite排序规则基线失败保留，不宣称全套通过。

原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3闭环/56余项，125/247正式状态不改。WP12-01外部选项Owner、WP12-02实际服务节点Owner及其他整卡余项保留；下一窗口先核原卡和当前源码，不能重复W280/W281/W284/W285。 最终分支/工作区/席位状态按wave286-final-verification.json核销。

## 2026-11-03：24小时目标 / W285普通FORM文件

W285：普通FORM不可变内容/扫描/原操作、草稿与提交及退回的同事务引用绑定、当前业务对象和字段权限下载已贯通实际页面；真实PG/HTTP/Chrome/Defender完成限定验收。 [交付](../development/evidence/W285-01/DELIVERY.md)。

11项唯一相关PostgreSQL/HTTP行为和1条实际Chrome→Vue→Main→PostgreSQL/Defender链通过，0跳过；42项相关Vue行为及vue-tsc --build通过。覆盖同事务绑定与整事务回滚、未知原操作与当前撤权、并发认领、退回替换、隐藏值保留/重基、扫描/保留/损坏状态、当前对象字段授权、精确原件下载及刷新恢复不重传。

原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3闭环/56余项，125/247正式状态不变。WP12-03/05及WP02-14整卡不关闭；全Owner引用图/物理清理、部门文件管理、历史任意快照附件与外部选项范围未覆盖；W286定义原竞争和legacy来源/完整旧caller接续。 最终分支/工作区/席位状态按wave285-final-verification.json核销。

## 2026-11-03：字段默认行为修复 / BUG #721

普通表单字段默认提供“移除填写”和“设为空值”两个明确动作；退回patch显式allowUnset=false仍仅提供设为空值。 [修复与证据](../development/evidence/BUG-721/DELIVERY.md)。

实际Vue组件挂载的15项相关行为通过：既有精确数字/明细/缺省与null原回归恢复，新增明确false和只读无按钮检查；当前PersonalFormEditor与NativeTaskOperations相关行为同时通过。vue-tsc --build通过。

本次不改变原375与局部卡状态；远端采用/Issue关闭/分支清理/席位释放以bug721-final-verification.json核销，随后恢复W285继续24h目标。


## 2026-11-03：24小时目标 / W284定义定位与规则预览

W284：FLOW/FORM保存、发布、读取和规则求值的准确结构诊断已贯通现有设计器；FORM准确草稿的纯顺序规则预览、服务端精确结果与浏览器体验对照完成真实PG/HTTP/Chrome限定验收。 [交付](../development/evidence/W284-01/DELIVERY.md)。

35项唯一纯校验/规则相关行为、6项唯一隔离PostgreSQL/HTTP行为及1条真实登录→Vue/Chrome→HTTP→PG路径通过；47项唯一相关Vue行为及vue-tsc --build（包含app/node两项目）通过。覆盖节点/边/字段/列/规则精确定位，重复字段/复杂子表/200行上限，循环/深度/未注册服务、原字段读权投影，准确顺序计算与大数、纯预览零业务写入、过期/拒权/迟到响应、首次校验失败保留可编辑输入和未知原保存不能被后续拒绝抹除。

原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3闭环/56余项，125/247正式状态保持。WP12-01整卡不关闭；普通FORM文件/外部选项Owner准入、FLOW双作者/下一草稿原竞争、全部legacy调用面与来源仍有余项。W285普通FORM文件及最终下载、W286原竞争/旧caller顺序继续。 分支/工作区/席位最终状态见wave284-final-verification.json。

## 2026-11-03：24小时目标 / W283原生截止与执行器

W283：原生APP/SFS固定UTC截止事实、Owner内租约/epoch、实际服务授权、提醒/升级/错误边与明确系统决定、原结果及只读运行诊断已实现；真实Host、PG、HTTP与Chrome完成限定验收。 [交付](../development/evidence/W283-01/DELIVERY.md)。

17项唯一相关PostgreSQL后端行为及1条真实Host→PostgreSQL→HTTP→Vue→Chrome路径通过；7项既有Inbox组件行为与最新vue-tsc通过。覆盖固定UTC小时政策、旧版本与时区变化、实际服务授权、两Worker领取竞争、租约过期、人工与系统两连接竞争、挂起恢复原槽、系统决定零人工票、APP人工政策阻断、升级目标撤权、原结果未知恢复、错误边兄弟分支隔离及缺原结果整事务回滚。执行前任务/实例版本与epoch补充有定向回归。浏览器复用其已运行的扫描/提醒/次数推进/刷新原结果证据，最后新增版本文本只做类型检查与后台真实版本断言，不称截图覆盖了新增文本。

原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3闭环/56余项，125/247正式状态不变。WP12-11、WP02-21整卡不关闭；版本化工作日日历/管理重算、全业务Owner系统决定采用、完整legacy调用面仍有余项。W284字段图定位/纯规则预览、W285普通FORM文件准入/最终下载、W286原竞争及旧caller处置顺序继续。 分支/工作区/席位最终状态见wave283-final-verification.json。

## 2026-11-03：24小时目标 / W282原生任务恢复

W282：APP/SFS原生退回预览与确认、准确转交/前后加签、SFS同实例退回编辑重送、显式政策无人审批恢复及管理批量转交已实现；真实Chrome、HTTP、PostgreSQL和故障恢复通过。 [交付](../development/evidence/W282-01/DELIVERY.md)。

本地31项唯一相关后端行为通过，来源逐项列明，包含隔离PostgreSQL18实际迁移/Owner/HTTP、APP/SFS任务办理、准确FormTo与旧票隔离、返回编辑与双连接重送、当前授权/SOD、显式政策无人审批恢复、管理批量固定名单/部分结果/同实例版本链/断响应恢复及授权后500项上限。含2条真实Chrome→Vue→Main→PostgreSQL路径：退回编辑重送至通过、管理批量转交丢响应刷新恢复。18项前端组件行为与应用vue-tsc通过，四张截图已查看。复用未受后续改动影响的成功来源；失败日志保留，不把混合运行改称全绿。模拟用户、业务、政策和服务登记，产品Owner/HTTP/数据库/浏览器实际运行。无Actions、生产部署或既有数据库迁移。

原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3闭环/56余项，125/247正式状态不变。WP12-09整卡保持原状态；期限/Worker原生采用留W283，完整字段/图诊断及纯FORM规则预览留W284，普通FORM实际文件准入与最终下载留W285，FLOW原竞争及legacy来源/旧caller处置留W286。 分支/工作区/席位最终状态见wave282-final-verification.json。

## 2026-11-03：BUG715权限目录修复

补齐单价订正原操作query能力目录；纠正权限互锁漏读WMS/MES仅目录OwnerActions及MES313锚定，不增加角色授权。 [修复与证据](../development/evidence/BUG-715/DELIVERY.md)。

本地20项相关行为通过、0跳过：ERP/MES实际种子跨租户登记/幂等/授权保持，六领域贴点与能力目录互锁，以及实际Program登录→HTTP→PG的未授权拒绝、显式query授权读取/关闭原意图、CSRF拒绝和撤权再拒绝。新增种子期望先红后绿；原W281的3项互锁失败在修复后通过，原日志保留。

原整卡状态保持；下一W282，24h目标继续。分支/工作区/席位及Issue最终处置见bug715-final-verification.json。

## 2026-11-03：24小时目标 / W281收藏与历史

W281：原生幂等收藏、本人失效收藏取消与安全占位、当前授权统一提交/流程历史已实现；准确FORM/FLOW版本、租户时区、分页截点、只读字段投影及实际连续业务回执，真实Chrome/Vue→Main→PostgreSQL采用。 [交付](../development/evidence/W281-01/DELIVERY.md)。

本地29项唯一相关后端行为通过，含真实PostgreSQL/HTTP收藏并发、原命令恢复、原子性、当前租户/资源/字段权限、未绑定与已绑定历史、准确发布版本、租户日历/DST、分页截点、真实连续业务回执、SFS/APP子流程回归、OA权限种子与互锁，以及实际Chrome→Vue→Main→PostgreSQL收藏断响应恢复和历史详情。前端20项相关组件行为与应用vue-tsc通过；浏览器截图已查看。各来源中的失败仍保留，不将混合运行改称全绿。跨领域3项权限互锁失败登记独立P1 #715，相关源码与开工主线一致，尚未做这些端点的默认管理员HTTP复测。无Actions、生产部署或既有环境迁移。

原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3闭环/56余项，125/247正式状态不变。普通FORM文件准入及最终下载留W285，完整字段/图诊断留W284，FLOW原竞争与legacy来源/旧caller留W286；退回/转交/加签W282、期限/Worker W283及其他业务Owner采用仍待实施。新发现跨领域权限种子P1 #715按规则作为下一独立任务，之后接续W282。 分支/工作区/席位最终状态见wave281-final-verification.json。

## 2026-11-03：24小时持续目标 / W280准确发布版本比较

W280：FLOW/FORM准确Published GUID版本比较已实现，按稳定字段/节点/连线身份分类差异，保留顺序和精确数字词元；当前资源与字段权限裁剪后比较，实际Chrome/Vue→Main→PostgreSQL采用。 [交付](../development/evidence/W280-01/DELIVERY.md)。

本地后端7个唯一行为通过：4个纯比较测试、2个真实PostgreSQL/HTTP权限与分页、1个真实Chrome→Vue→Main→PostgreSQL双工作区比较；0跳过。前端19个相关组件行为通过（原发布面板15项+新比较面板4项，最终新面板4项定向复验），应用vue-tsc --noEmit -p tsconfig.app.json通过。真实失败保留；最终源码6个后端成功行为复用004504Z记录，浏览器定位修正后只重跑对应1项。无Actions、生产部署或既有环境迁移。

原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3闭环/56余项，125/247正式状态不改。W281目录/收藏/历史下载、W282退回等任务操作、W283期限/Worker接续；完整字段/图诊断、FLOW双作者与下一稿竞争原场景、legacy重建来源与全部旧caller、文件/选项Owner、SFS三版本及其他业务Owner采用仍保留。 总目标IN_PROGRESS；分支/工作区/席位最终状态见wave280-final-verification.json。

## 2026-11-03：24小时持续目标 / W279原验收与准确草稿预演

W279：原OA-DEF27项验收逐项核对，并补准确草稿/当前授权/无业务写入的EXPLANATION_ONLY预演，实际页面到PostgreSQL已验证。 [交付](../development/evidence/W279-01/DELIVERY.md)。

12项唯一后端行为通过（4项隔离PostgreSQL18，其中1条实际Chrome/Vue→Main→PG链；8项纯预测规则），后端0跳过；12项唯一相关Vue行为和显式应用vue-tsc通过。模拟变量/IAM/业务数据，真实内部Owner、数据库、登录会话和HTTP，无Actions、既有环境迁移或生产部署。

原375仍27DONE/2IN_PROGRESS/346PLANNED；59局部3闭环/56余项，125/247正式状态不改。W280准确版本比较接续；完整字段/图诊断、FLOW双作者与下一稿竞争原场景、旧重建来源标识/全部旧caller、文件/选项Owner、正式下载、SFS patch/三版本、退回等任务操作、日历Worker及其他业务Owner采用仍保留。 总目标IN_PROGRESS；分支/工作区/席位最终状态见wave279-final-verification.json。

## 2026-11-02：24小时持续目标 / W278实际页面业务链

W278：当前发布表单目录，以及实际Vue/Chrome经Main Program到PostgreSQL的定义发布、填单恢复、代理办理、迭代BLOCKED应用与撤回显式新授权链已验证。 [交付](../development/evidence/W278-01/DELIVERY.md)。

6项唯一相关PostgreSQL18行为通过（3条真实Vue/Chrome→Main Program→PG业务链、3条目录Owner边界），0跳过；3项目录Vue行为和显式应用vue-tsc通过。模拟业务输入，内部Owner及HTTP真实执行；撤回新授权由页面产生，最终Workflow投递由同一PG场景的真实接收器执行。

原375保持27DONE/2IN_PROGRESS/346PLANNED；59局部卡3闭环/56仍有余项，125/247正式状态保持。W279核原AC；预演/历史比较、收藏下载、完整SFS patch/三版本、退回重送/转交加签/无人审批、准确日历与期限Worker，以及其他业务Owner采用仍按真实范围保留。 总目标IN_PROGRESS；分支/工作区/席位最终状态见wave278-final-verification.json。

## 2026-11-02：W278依赖修复 / BUG #710

修复迭代评审成功后父页面刷新被误判为身份变化：临时UI禁用只阻止新动作，不清除已确认结果；真实认证、权限、租户及轮次检查保留。 [修复与实际证据](../development/evidence/BUG-710/DELIVERY.md)。

组件红/绿、27项相关行为和应用类型检查通过；原真实浏览器/HTTP/PG应用恢复复测通过。完整W278与24h目标继续；分支/工作区/席位按bug710-final-verification.json核销。

## 2026-11-02：W278依赖修复 / BUG #708

修复已发布流程载入画布即变脏并阻断历史/下一草稿的问题；展示初始化不再回写，只把真实图编辑合入原始schema，保留未编辑扩展、null及高精度数值。 [修复与实际证据](../development/evidence/BUG-708/DELIVERY.md)。

真实画布先失败后通过，35项相关行为与类型检查通过；真实PG/HTTP/页面原流程历史、下一草稿及发布复测通过。完整W278与24h目标继续，生命周期按bug708-final-verification.json核销。

## 2026-11-02：24小时持续目标 / W277委派和显式重新授权

W277：TASK-R2封闭范围委派、当前双身份任务读写，以及迭代Owner独立业务委派、BLOCKED显式重新授权和连续原事实已实现。 [交付](../development/evidence/W277-01/DELIVERY.md)。

本地33项唯一PostgreSQL18行为通过，0跳过；35项Vue组件/恢复行为通过，应用vue-tsc通过。实际Main登录/CSRF/HTTP、两连接授权撤销及撤回/应用竞争、延迟提交失败回滚均有定向证据。模拟业务数据；内部Owner、事务、原生Workflow和历史原件真实执行。

原375仍27DONE/2IN_PROGRESS/346PLANNED，59局部仍3闭环/56余项，125/247正式状态保持。W278接续浏览器到真实PG完整链，W279原AC核对；历史比较/目录收藏下载、完整SFS patch/三版本、退回转交/无审批人、日历与期限Worker仍在后续窗口。其他业务Owner的委派撤回/应用未采用，不把迭代采用推广为全Owner完成。 总目标IN_PROGRESS；最终分支/工作区/席位见wave277-final-verification.json。

## 2026-11-02：24小时持续目标 / W276当前任务读面

W276：原生APP/普通FORM本人当前授权信箱、准确根子pin、独立已读原事实及实际定义在途影响已实现。 [交付](../development/evidence/W276-01/DELIVERY.md)。

本地31项唯一后端行为通过（11项隔离PostgreSQL18、20项既有读面/内核），0跳过；25项Vue组件行为通过，应用vue-tsc通过。实际Main登录/CSRF/HTTP、两连接已读竞争、当前权限和租户隔离均有定向证据。模拟业务数据，内部Owner/PG事务/原生内核真实执行。

原375仍27DONE/2IN_PROGRESS/346PLANNED，59局部仍3闭环/56余项，125/247正式状态保持。W277接续封闭代理授权/当前代理读面、代理撤回及BLOCKED新授权；完整SFS patch/三版本、历史下载/目录、浏览器到真实PG、退回等操作和定时Worker仍在后续窗口。 总目标IN_PROGRESS；最终分支/工作区/席位见wave276-final-verification.json。

## 2026-11-02：24小时持续目标 / W275普通FORM

W275：普通FORM本人草稿、精确字段/规则/五上下文、同一PG事务提交/真实根子流程/人工任务/通知、墓碑恢复与显式重基、原意图恢复闭环。 [交付](../development/evidence/W275-01/DELIVERY.md)。

本地55项唯一后端行为通过（36项隔离PostgreSQL18、19项内核/旧读面/纯规则），0跳过；15项唯一Vue/精确JSON行为通过；显式应用vue-tsc通过。含实际Main登录/CSRF/HTTP和两连接竞争。模拟业务数据/政策，真实内部Owner、数据库事务、内核和通知表。

WP12-03/04/08仍余完整原AC：W276当前授权读面/统计/在途披露，W278浏览器到真实HTTP/PG，W279原AC核对，W281目录/收藏/历史/正式下载，W282退回等任务操作，W283定时Worker。上传FileRef及服务任务尚无本普通FORM入口的实际Owner采用，已在发布/准入边界明确拒绝，不能称已验收。原375仍27DONE/2IN_PROGRESS/346PLANNED，59局部3闭环/56余项，125/247正式状态不变。 总目标IN_PROGRESS，继续W276，下一累计8h校准；最终分支/工作区/席位见wave275-final-verification.json。

## 2026-11-02：24小时持续目标 / W274子流程运行

W274：APP采用精确FORM/子FLOW版本，真实子任务、同事务父恢复、仅根业务决定及整树撤回闭环；按有效分支计算审批职责，保留全部审计票。 [交付](../development/evidence/W274-01/DELIVERY.md)。

本地70项唯一后端行为通过（45项PG18、25项内核InMemory/纯规则），0跳过；8项Vue组件行为通过。含真实Main登录/CSRF/HTTP、并发/未知提交/权限、精确版本及全树原子性。业务输入、IAM政策和文件存储为模拟，数据库及Owner执行真实。前端仅文案变更，复用W273显式应用vue-tsc通过证据（3328文件），本次未重复类型检查。

WP12-02仍余完整原AC及实际在途影响披露；普通FORM个人草稿/提交与其运行入口按W275–276继续，服务任务须独立Owner协议，不能因本窗口关闭WP12整卡。原375仍27DONE/2IN_PROGRESS/346PLANNED，59局部3闭环/56余项，125/247正式状态保持。 总目标IN_PROGRESS继续W275，下一校准累计6h。正式采用/清理/finish见wave274-final-verification.json。

## 2026-11-02：W274依赖修复 / BUG #702

修复受控文档绑定可用文件时丢失存储事务门的问题：同一FileContentStore读取状态及验证原始字节，保留当前权限、暂扣状态和内容完整性检查。 [修复与证据](../development/evidence/BUG-702/DELIVERY.md)。

6项相关PG18行为通过、0跳过：新增回归先在未修复主线失败，再通过；原迭代撤回故障注入、文档创建/历史/原重放、错误引用/权限撤销及文件损坏隔离回归均通过。业务数据、身份及文件存储为模拟，数据库与Owner事务真实执行。

24小时持续目标仍IN_PROGRESS，继续W274子流程运行/撤回；本次不改变原375及56局部余项。远端采用、Issue关闭、准确分支清理与席位释放见bug702-final-verification.json。

## 2026-11-02：24小时持续目标 / W273唯一绑定

W273：同FORM唯一启用FLOW绑定由准确Published源派生，PG条件唯一约束/CAS裁决竞争；保历史、未知恢复与回滚，管理页准确表单展示。24h目标第六窗口，继续W274真实运行采用。 [交付](../development/evidence/W273-01/DELIVERY.md)。

本地47项唯一后端行为通过（45项PG18、2项纯规则合同）、0跳过，含真实Main登录/HTTP、唯一绑定/并发/迁移与受影响激活/依赖/APP绑定及提交；8项Vue行为通过，显式vue-tsc --noEmit -p tsconfig.app.json --extendedDiagnostics通过（3328文件）。政策/业务数据模拟；实际隔离PG/Main。组件API模拟，无浏览器到PG整链、Actions、既有环境迁移或生产部署。

WP12-02仍余实际根/子流程/任务pin与旧运行入口同门、完整原AC；本次启停绑定不是引擎启动或业务Owner应用。375仍27DONE/2IN_PROGRESS/346PLANNED，59局部3闭环/56余项，125/247正式状态保持。 总目标IN_PROGRESS继续W274，下一校准4h。采用/清理/finish见wave273-final-verification.json。

## 2026-11-02：24小时持续目标 / W272准确依赖

W272：发布预览冻结准确FORM/子FLOW版本，确认及激活递归复核当前资格，保留历史pin；实际PG/HTTP、审批绑定/受控提交及页面采用。24h持续目标第五窗口，继续W273唯一FORM-FLOW绑定。 [交付](../development/evidence/W272-01/DELIVERY.md)。

本地49项唯一后端行为通过（47项PG18、2项纯规则合同）、0跳过，含真实Main登录/HTTP、精确递归依赖与受影响发布/激活/APP绑定及提交；7项Vue行为通过，显式vue-tsc --noEmit -p tsconfig.app.json --extendedDiagnostics通过（3328文件）。政策/业务数据模拟；实际隔离PG/Main。组件API模拟，无浏览器到PG整链、Actions、既有环境迁移或生产部署。

WP12-02仍余唯一FORM-FLOW绑定及其并发/CAS、实际运行根与子流程/任务pin、旧运行入口同门和完整原AC；现APP Binding采用不是FORM-FLOW绑定或运行实例交付。375仍27DONE/2IN_PROGRESS/346PLANNED，59局部3闭环/56余项，125/247正式状态不变。 总目标IN_PROGRESS继续W273，下一校准4h。采用/清理/finish见wave272-final-verification.json。

## 2026-11-02：24小时持续目标 / W271固定发布源克隆

W271：FLOW/FORM从准确Published版本克隆新停用草稿，清流程身份码、不可变clonedFromRef、当前读源/创建权限与原命令恢复；Main HTTP及版本面板采用。24h持续目标第四窗口，继续W272。 [交付](../development/evidence/W271-01/DELIVERY.md)。

本地48项唯一PG18行为通过、0跳过（16克隆行为含实际Main HTTP/并发/权限/回滚，32受影响草稿与版本回归）；22项Vue行为通过；显式vue-tsc --noEmit -p tsconfig.app.json --extendedDiagnostics通过，实际3328文件。政策/业务数据模拟、真实隔离PG与Main登录会话；组件API模拟，无浏览器到PG整链、Actions、既有环境迁移或生产部署。

WP12-01仍余准确版本比较/完整原AC；WP12-02仍余FORM/子流程依赖pin、递归资格、唯一绑定、实际运行和旧入口采用。克隆仅v2 managed定义路径，不称旧Designer.CloneAsync已退役，不复制运行/审批/外呼事实。125/247和原375整卡状态保持，59局部仍3闭环/56余项。 首个2h已校准，扩展W280–283仍PLANNED；总目标IN_PROGRESS继续W272，下一校准4h。采用/清理/finish见wave271-final-verification.json。

## 2026-11-02：24小时持续目标 / W270 FORM发布和启停

W270：FORM准确发布预览/不可变版本、显式准确来源下一草稿、独立激活CAS/历史/原件及设计器/管理页采用已实现验证；24h持续目标第三窗口，继续W271。 [交付](../development/evidence/W270-01/DELIVERY.md)。

本地36项唯一PG18行为通过、0跳过（15 FORM草稿回归、19 FORM发布/激活、2既有FLOW实际HTTP回归）；24项Vue组件行为通过；显式应用配置vue-tsc --noEmit -p tsconfig.app.json --extendedDiagnostics通过，实际检查3325文件。当前Main真实登录/CSRF/HTTP与隔离PG执行，政策/业务数据模拟；无Actions/工作流变更/既有环境迁移/生产部署。 先前根配置类型命令未检查应用，历史报告已更正，本次显式应用检查独立有效。

WP12-01仍余FLOW/FORM固定sourceVersion克隆、完整原AC与比较等原范围；WP12-02仍余FORM/子流程精确依赖、唯一绑定和实际运行/旧入口采用。文件Owner准入、提交字段/规则权限、个人草稿及跨窗口浏览器到PG全链按W272–279推进。新FORM启用配置不代表运行入口已接入，未知影响数不写0；125/247及原375整卡状态保持，59局部仍3闭环/56余项。 总目标IN_PROGRESS，继续W271。采用/分支清理/官方finish以wave270-final-verification.json核销。

## 2026-11-02：24小时持续目标 / W269 FORM草稿

W269：FORM独立草稿Owner/显式政策、当前会话/资源/字段权限、WFC原命令及CAS、旧身份互斥、完整表单设计器工作区已实现验证。24小时持续目标第二窗口，下一步W270。 [交付](../development/evidence/W269-01/DELIVERY.md)。

本地16项PG18行为通过、0跳过（15项新增FORM，1项既有FLOW实际HTTP回归）；20项Vue组件行为通过（最后13项FORM复查，7项未变FLOW复用同任务成功结果）；最终vue-tsc退出0。实际Main登录会话/CSRF/HTTP和真实隔离PG；业务政策/IAM/数据为明确模拟。无Actions、工作流调整、既有环境迁移或生产部署。

WP12-01仍余FORM不可变发布/独立启停与固定sourceVersion克隆、完整原AC；WP12-02余FORM/子流程精确依赖、唯一绑定及实际运行/旧入口采用；表单个人草稿/提交字段规则授权、文件/选项Owner资格、跨窗口浏览器到PG全链按后续窗口执行。原59局部仍3闭环/56余项，不以本切片关闭整卡。 总目标仍IN_PROGRESS，下一步W270。采用/清理/官方finish以wave269-final-verification.json核销。

## 2026-11-02：24小时实际Codex目标 / W268首窗口

W268：FORM完整schema与原数字片段往返、逐编辑精确撤销/重做，保存/发布同一服务端schema边界；FLOW致命结构与未完草图诊断分开。24小时目标仅首窗口，继续W269。 [交付](../development/evidence/W268-01/DELIVERY.md)。

本地64项后端唯一行为通过/0跳过：63项相关（16项PG18含实际Main会话HTTP，47项单元/InMemory）及宽筛选附带1项既有Space OpenAPI检查；前端22项通过（最后11项复查，未变的11项复用首轮）；最终vue-tsc退出0。业务输入模拟；无Actions、工作流调整、既有环境迁移或部署。

WP12-01仍余FORM完整Owner当前资源/字段权限、原命令/CAS/显式草稿、固定sourceVersion克隆和设计器正式采用；WP12-02余FORM/子流程精确依赖、唯一绑定、实际运行及旧写入口；文件/选项Owner实际资格、提交专业精度/权限与原AC全范围在后续窗口。当前不标整卡完成。 总目标保持IN_PROGRESS，每2小时按实际吞吐重估；原375和56局部余项保持。采用/清理/finish以wave268-final-verification.json核销。

## 2026-11-02：W267第九期三天共享菜单树

第九期三天W267菜单展示配置V1：名称/图标/Enable共享树CAS、全tenant引用预览、强审计/原件同事务、当前平台及menu:edit实际HTTP、集成页面冲突与未知原请求恢复。 [交付](../development/evidence/W267-01/DELIVERY.md)。

12项真实PG18/实际Main会话HTTP通过/0跳过（5项展示配置、7项受影响菜单树回归）；34项Vue行为通过，最终展示面板5项定向复查通过；Vue类型检查退出0，实际Chrome集成页面API夹具PASS。业务数据模拟，PG/HTTP真实执行。无Actions、取消项、工作流调整、既有环境迁移或部署。

W266排序层级和W267名称/图标/Enable展示配置已限定交付。WP02-18仍余RoutePath/MenuKey及路由/权限命名空间治理、个性配置、KPI Owner与scoped授权缓存、全部用户导航传播、旧接口/旧节点编辑全面CAS与原命令采用及原20AC整体；隐藏菜单不是动作撤权。命令历史保留/擦除归治理任务，原WP02-03整卡前置地位不变。 原375状态与56局部余项保持。正常采用/清理/finish由wave267-final-verification.json核销。

## 2026-11-02：W266第八期三天共享菜单树

第八期三天W266共享菜单树V1：PG原生版本与CAS、全部租户角色引用预览、准确原命令和强审计同事务；当前平台管理员加menu:edit的真实会话HTTP，实际拖拽页面冲突差异与未知恢复。 [交付](../development/evidence/W266-01/DELIVERY.md)。

24项相关本地后端测试通过/0跳过：7项新增PG18/真实Main会话HTTP、8项原菜单/IAM动作回归、9项当前权限PG读取回归；业务数据模拟。29项MenuView行为测试、Vue类型检查退出0、实际Chrome/Element Plus原生拖拽与API夹具PASS。首次编译、NUL审计jsonb、测试约束、夹具目录冲突、旧UI夹具及类型断言失败均保留并修正；仅定向复跑。无Actions或部署。

W266仅闭合共享树排序/层级V1及当前浏览器读取恢复。WP02-18仍余其他菜单字段/路由注册、个性配置、KPI Owner定义与scoped查询/授权缓存、全部相关导航传播及20条原业务AC整体验收；旧菜单/删除/PUT tree兼容入口仍未全面采用CAS/原命令和全局政策，不因V1交付宣称旧入口治理完成。命令历史保留/擦除归治理任务。WP02-03原整卡前置地位不变，本期直接复用已交付IAM共同门/当前权限。 原375状态与56局部余项保持。正常采用/清理/finish由wave266-final-verification.json核销。

## 2026-11-02：W265第七期三天租户创建

第七期三天W265租户创建V1：固定现有IAM模板修订，原命令恢复与Tenant/角色/菜单/首用户/强审计同事务；当前平台会话HTTP和创建页面，密码仅首次成功响应，未知原请求可恢复。 [交付](../development/evidence/W265-01/DELIVERY.md)。

42项相关本地PG18行为通过/0跳过：8项创建V1、11项W264管理V1及23项既有权限/审计回归。真实Main Program/登录cookie与事务执行，业务数据模拟；Vue类型检查退出0、实际Chrome/Vue页面夹具PASS。首次LINQ排序翻译失败及默认模板夹具初始化顺序失败保留，修正后按影响定向复跑。无Actions或部署。

W264配置/启停与W265创建V1已交付。WP02-04仍有其他配置Owner、旧兼容入口全面版本/原命令采用、全部业务writer共同fence、跨系统映射/传播、worker恢复及整卡验收；命令历史保留/擦除归相应治理任务。旧CreateAsync不具备原命令恢复，未知不得自动重放；现有默认模板Owner管理不在本期新增审批流程。 原375状态与56局部余项保持。正常采用/清理/finish由wave265-final-verification.json核销。

## 2026-11-02：W264第六期三天租户配置与启停

第六期三天W264完成进度盘点与租户配置/启停V1：PG单调版本识别旧写和ABA，CAS与原命令/审计同事务，当前平台会话HTTP，页面保留完整字段、草稿和未知原请求。 [交付](../development/evidence/W264-01/DELIVERY.md)。

34项相关实际本地PG18行为通过/0跳过（11项V1含实际Main HTTP、23项既有租户权限/审计回归）；Vue类型检查退出0，实际Chrome/Vue页面夹具PASS。按受影响范围复跑并复用未变证据；早期编译/迁移/HTTP绑定失败及一次错误401预期保留。无Actions或部署。

原375卡及原59局部余56保持；租户创建原命令/模板版本、旧接口全面CAS和整卡验收仍余项。WP02-06切组织已有W253实际证据，不重复。Main/Handoff待正常采用/finish，由wave264-final-verification.json核销分支、工作区和席位。

## 2026-11-02：W263第五期三天旧元数据显式接入

第五期三天W263完成旧CodeGen元数据显式接入：保留原ID/表列/审计原件，原件指纹CAS，同事务建立V1与原操作恢复，接入后数据库禁止旧写；真实会话HTTP及Vue接入/未知恢复已验证。 [交付](../development/evidence/W263-01/DELIVERY.md)。

13 actual PostgreSQL18 checks PASS/0 skipped (6 adoption cases and7 affected W262 regressions);Vue type-check exit0;new adoption and W262 normal-save actual Chrome/Vue checks PASS with API/identity fixtures. No failed tests in W263. Owned PG/browser stopped;no Actions/deployment.

原375卡及原59局部余56保持；未做存量批量迁移、文件应用或编号消费者切换。Main/Handoff待正常采用/finish，wave263-final-verification.json核销分支、工作区和席位。

## 2026-11-02：W262第四期三天元数据交付

第四期三天W262完成CodeGen新元数据V1：PostgreSQL租户版本保存/CAS、不可变历史与原操作恢复、真实当前会话HTTP、Vue冲突和未知结果恢复。旧数据不自动接管，生成文件未应用。 [交付](../development/evidence/W262-01/DELIVERY.md)。

7 actual PostgreSQL 18 checks PASS/0 skipped:6 Owner/real-session HTTP cases plus1 nonempty legacy upgrade/idempotence case. Vue type-check exit0;actual Chrome/Vue behavior PASS with API/identity fixtures. Unchanged W261 comparison evidence reused. No test failures in W262. No Actions/deployment.

原375卡与原59局部余56保持；旧元数据接入、实际文件应用和编号业务消费者仍余项。Main/Handoff本提交待正常采用/finish，以wave262-final-verification.json核销分支、工作区和席位。

## 2026-11-02：W261 CodeGen差异预览

CodeGen新增固定模板差异与覆盖范围纯预览：精确建议路径、用户提供原文件、无损自定义区保留、线性差异及逐文件覆盖白名单，实际API和Vue页面已验证。没有文件应用或元数据持久化。 [交付](../development/evidence/W261-01/DELIVERY.md)。

42 affected CodeGen checks PASS,0 skipped (including new comparison behavior and real MVC routing/auth/permission with identity and in-memory metadata fixtures);Vue type-check exit0;actual Chrome/Vue page behavior PASS with simulated API/identity. No PostgreSQL instance required or started for pure text comparison. Initial missing-route RED,two test compilation failures,and first page-selector timeout retained;fixed and relevant checks passed. No Actions or deployment.

第三期三天按最终proof归档完成；375原卡与原59局部余56保持。实际文件应用、元数据CAS及编号业务消费者仍余项。Main/Handoff本提交待正常采用/finish，以wave261-final-verification.json核销分支、工作区和席位。

## 2026-11-02：W259第三期D2与整期收尾

第三期D2完成真实HTTP提交/撤回、实际60分钟保留期匿名化、已匿名化原件进入Expired、失败回滚及丢响应原结果恢复；原审查截止和擦除墓碑保持、PII不复活。D1菜单极值修复及D3受权编号管理API已先完成，第三期限定实现/验收收齐，整期最终生命周期按proof核销。 [交付](../development/evidence/W259-01/DELIVERY.md)。

Two unique actual PostgreSQL18/HTTP checks PASS,0 skipped:complete elapsed anonymized-to-Expired lifecycle and real browser re-login/read preserving the existing result;3611.6 seconds from original receipt to final verification,at least60 real minutes before erasure. Original review business time alone is controlled;IAM/publication/lease/erasure/commit clocks remain real. Actual retained-to-erased-to-Expired graph,HTTP receipt/intake,rollback/lost-response recovery and five original-row comparisons executed. Initial full run failed at final HTTP401 because its ten-minute fixture browser token had expired;failure retained,real login/callback added and the complete lifecycle rerun. W256 unchanged other Owner boundary evidence referenced,not rerun. No Actions/deployment.

D1/D3与CRM/Handoff已合并、准确分支删除和finish；Main W259本提交待正常采用/释放，最终以wave260-final-verification.json为准。原375卡真实范围保持。

## 2026-11-02：W260第三期D3

第三期D3已实现新登记Main编号域的受权规则管理HTTP：当前/历史版本、格式CAS、原修改恢复及无副作用预览；实际Main登录、CSRF、当前会话与租户生效。D2真实60分钟保留期验收仍在运行，不能称三天全部完成。 [交付](../development/evidence/W260-01/DELIVERY.md)。

4 actual PostgreSQL18/Program HTTP cases PASS,0 skipped:format history/CAS/3000-character NUL original recovery/preview;current query-edit grants/cross-tenant/logout;two in-flight revoke cases after waiting for the shared authentication gate. Original missing-entry RED and MVC record metadata failure retained;annotation placement corrected and all4 rerun PASS. W257 unchanged Owner/store behavior evidence referenced,not rerun. No Actions/deployment/new migration.

Main本提交待正常采用/finish；Handoff待CRM席位释放后串行发布，D2/Main准备分支已保存供恢复。原375卡及其真实余项保持。

## 2026-11-02：W259开工与真实等待

第三期D2已登记并在CRM独立分支codex/w259-02执行真实匿名化后Expired链路。实际公开提交及撤回已完成，60分钟合法保留期截止UTC 2026-11-02T05:44:02.095099；完整验收仍在运行，不能称通过。为利用等待，Main协调分支保存后官方deferred释放DEV-1，串行切到独立D3/W260；CRM继续占DEV-2，COORD单人执行，2+1配额不变。D2结果将回来本分支收尾，必要时正常合并新主线，保留D3已采用事实。 [当前记录](../development/evidence/W259-01/DELIVERY.md)。

## 2026-11-02：W258第三期D1

第三期D1修复Issue684：四处待执行PostgreSQL迁移在历史菜单ID达到int.MaxValue时选择正整数空闲ID，保留历史菜单、授权、工程原件及编号。第二期已按最终证明归档；D2/D3接续，原375卡状态不变。 [交付](../development/evidence/W258-01/DELIVERY.md)。

Original EngineeringRevision forward-migration regression reproduced SQLSTATE22003 before fix, then passed with added max/max-1, all8 new menus, original role grant/product/numbering preservation and repeat-migration assertions. Actual owned PostgreSQL18 and EF migrations; simulated business/IAM inputs. No Actions/deployment/existing environment migration.

Handoff已采用/清理/释放，Main本提交待正常采用/finish与Issue684关闭。D2真实匿名化过期链路、D3受权编号管理HTTP入口继续。

## 2026-11-02：W257第二期D3

第二期D3新登记Main编号域格式规则CAS、不可变版本历史和原修改恢复已交付；旧号码/周期计数保持、当前权限及旧写者拒绝通过真实PG。第二期三天限定必做均已实现验证；完整375卡/旧消费者/全局域/CodeGen和CRM后续范围保留。 [交付](../development/evidence/W257-01/DELIVERY.md)。

26 relevant PostgreSQL cases PASS,0 skipped:9 rule-change cases,9 allocation cases,8 legacy Seq regressions. Final2 strengthened original-command concurrency/deferred-integrity cases executed;24 unchanged successes reused from this window. Actual PG18 migrations,current permission facts,independent connections,rollback/unknown-commit recovery;mock business/IAM data and host-session resolver. Historical unrelated menu-max migration failure retained as open Issue684;not claimed passed. No Actions/deployment.

D1/D2已正常采用并释放；Handoff本轮已清理/释放，Main本提交待正常采用/finish。最终三天完成由wave257-final-verification.json核销。375仍27DONE/2IN_PROGRESS/346PLANNED，原59局部卡余56；后续消费者及真实剩余范围保留。

## 2026-11-02：W256第二期D2

第二期D2交付原生审核Expired持久终态、原截止派生保留、当前维护/编辑范围、原结果恢复和实际HTTP竞争；完整WP03-03及新事件消费者仍独立。继续D3/W257编号规则CAS。 [交付](../development/evidence/W256-01/DELIVERY.md)。

26 unique relevant checks PASS,0 skipped:18 final expiry/hosted/PG/HTTP/migration and prior-decision/retention checks,including1 explicitly in-memory anonymous-state projection;8 unchanged W253 review-boundary cases reused from this window. Signed mock business/IAM inputs and controlled review time;actual native HTTP/PG/Outbox and real IAM/lease/commit clocks. No production deployment or Actions.

CRM/Handoff已合并、准确分支删除、DEV-2释放；Main最终以同轮PR/finish为准。

## 2026-11-02：W255第二期三天D1

第二期D1核清Main共享连接尚未采用WP02-02新租户连接profile；WP03-01按原完整配置范围核销DONE，WP03-02冻结公开路径NUM/FILE不适用但整卡未来共同服务前置保留。375卡27DONE，原59局部卡余56。继续W256审核Expired持久终态。 [交付](../development/evidence/W255-01/DELIVERY.md) / [计划](../development/planning/20261102-three-day-cycle2.md)。

82 related local roadmap tests PASS,including4 cross-repository adoption regressions;2 positive cases failed before implementation. CRM pinned product receipts now resolve their own repository,never Main coordination adoption;mutable/wrong/unrelated refs rejected. Unchanged W102/W105/W247 product PG/HTTP/UI evidence reused;no product tests rerun. One author SPEC/QUALITY review;no Actions/deployment.

Handoff已采用/finish；Main最终分支/工作区/席位由同轮PR及wave255-final-verification.json核销。

## 2026-11-02：W254三天目标D3

D3编号原命令提供者已实现并通过真实PG验证：新登记Main租户域独立持久分配/原结果恢复、不回收、当前权限和同域唯一写者。旧域保留原Owner；完整规则CAS、业务调用者切换、全局域和CodeGen仍为WP02-16余项。三天必做限定交付进入最终采用/释放核销。 [单份交付](../development/evidence/W254-01/DELIVERY.md)。

17 unique relevant PostgreSQL cases PASS/0skip:9 allocation/current-authority scenarios and8 unchanged legacy Seq regressions. Sources allocation-related-20261102T020936Z(16),final-authority-20261102T021202Z(1). Actual fresh PG migrations/transactions/connections/permission data;simulated business actor and host-session resolver,no actual consumer adoption claimed. One author SPEC/QUALITY review;no Actions/deployment.

D1/D2已正常采用释放；Handoff本轮已清理/释放，Main本提交待正常采用/finish。最终三天完成由wave254-final-verification.json核销。375仍26DONE；WP02-10、CRM Expired终态、编号规则/全消费者/CodeGen明确后续。

## 2026-11-02：W253三天目标D2

D2实际CRM登录/切组织/旧会话失效通过；七天审核期限前、整点、之后及写入越界回滚通过，修复CRM Issue164。读取到期禁用动作；Expired持久终态与对应保留策略仍未实现，不关闭整卡。继续D3编号原命令提供者。 [单份交付](../development/evidence/W253-01/DELIVERY.md)。

11 unique relevant PostgreSQL/HTTP scenarios passed,0 skipped: final8 review boundary/rollback cases,final1 browser login/switch case,and2 unchanged original decision cases from acceptance batch. Signed mock IdP/policy/business inputs;actual CRM OIDC/cookie/session/Owner/PG graph executed. Review/capability clocks controlled;IAM/retention/disclosure use real clocks. One author full SPEC/QUALITY review;no UI changes,Actions or deployment.

CRM/Handoff已准确合并、清理并释放，Main本提交待正常采用/finish；最终见wave253-final-verification.json。

## 2026-11-02：W252三天目标D1

D1准确交付依赖规则已实现；WP09-11与WP12-07按原范围核销，WP02-10保留租户连接采用关系待核对。原375卡变为26 DONE，125目标/247正式验收不变；继续D2 CRM两条验收。 [单份交付](../development/evidence/W252-01/DELIVERY.md)。

78 local roadmap behavioral tests passed, including7 new scoped-adoption regressions;initial2 RED failures observed. Existing adopted PG/HTTP/UI evidence explicitly reused for original provider scope;no new product execution claimed. Definitions/generated views/links checked;one author SPEC/QUALITY review. No Actions/deployment.

Handoff已准确合并清理并释放，Main本提交待正常采用/finish；最终状态见同轮PR及wave252-final-verification.json。

## 2026-11-02：W251三天执行计划

三天单人执行计划：D1三张提供者整卡核销/台账依赖对齐，D2 CRM切组织与审核到期验收补项，D3编号原命令分配/恢复限定实现；候补为公开询盘适用核销与租户CAS细化。计划未开工，原375/125/247状态不变。 [执行计划](../development/planning/20261102-three-day-execution.md)。每天约6小时主任务+2小时机动，实际耗时和整卡关闭以执行结果为准；本轮不执行计划中的产品任务。

计划包含原卡映射、源码/测试入口、正反行为、候补与延期规则。Main固定DEV-1，DEV-2串行CRM/Handoff；单人、PostgreSQL、模拟业务验收、精简验证、Trust Internal Contracts/Fail-Fast及准确合并/清理/finish沿用。

本提交Handoff已合并清理释放，Main待正常采用/finish；终态见同轮PR及wave251-final-verification.json。无产品测试、Actions或部署。

## 2026-11-02：W250原59张局部卡范围核对

原59张局部卡逐项核对：37张明确实现余项、19张合同/验收待核、3张优先整卡核销（WP02-10/WP09-11/WP12-07）。375原卡状态不变；七天一期/W249已完成，不重做。 [逐卡结论与下一步](../development/evidence/W250-01/DELIVERY.md)。这是本轮审计交付，不是59张整卡完成；125完整目标/247正式验收保持。七天原持续目标已按W249终态归档COMPLETE。

下一步先核销三张提供者原卡，按已交付准确合同对齐旧台账依赖规则；然后核CRM切组织/审核到期执行证据和NUM原命令/租户CAS等实际缺项。模拟资料可用于生产业务验收，部署独立。仅文档/引用/生成页核对及一次作者集中审查，未运行产品测试或Actions。

本提交快照Handoff已合并清理/释放，Main待正常采用与finish；最终分支/席位状态见同轮PR及wave250-final-verification.json。

## 2026-11-01：W249原七天最终集成与总交付

原七天D1–D7业务与集成出口全部核销；生产业务验收采用明确模拟输入，实际Owner/PostgreSQL/HTTP/页面与原键恢复执行。最终完成以本轮正常合并和官方finish为准。 [总报告](../development/evidence/W249-01/DELIVERY.md)。本轮8项实际PG/HTTP/宿主通过、零失败跳过；其余未变输入成功证据复用。模拟业务资料可用于生产验收，生产部署未执行。

原七天业务/集成出口7/7通过；本提交快照Handoff已合并清理释放，Main待正常采用/finish，终态见同轮PR及wave249-final-verification.json。完整375任务仍24DONE/2IN_PROGRESS/349PLANNED，125/247状态保持；全产品余项与发布部署继续独立登记，不等待真实企业数据。

## 2026-11-01：W248退货商业/财务与原D2出口

W248退货商业/财务独立接线：冻结请求、全租户原发运行范围占用、外部原键派发/查询、不可变结果与独立财务轴、当前权限和页面恢复；原D2限定出口核销。 7 distinct PostgreSQL18/actual HTTP scenarios PASS, zero skipped (5 new commercial scenarios including five outcome variants,2 affected authorization/physical regressions);19 affected Vue scenarios and app vue-tsc PASS; original canonical commercial/financial output validation PASS. Native Windows/.NET8.0.30, isolated PostgreSQL18, explicit durable simulated external ERP/Finance. No Actions or deployment. [报告](../development/evidence/W248-01/DELIVERY.md)。Handoff已合并清理释放，Main待PR/finish。外部发票、批准、ERP/Finance结果及HTTP服务明确模拟并持久化，用于生产业务验收；实际RMA/Stock/PG/HTTP/UI执行。未部署生产、未迁移既有环境，完整WP10/11与财务账本/TRANSIT/通用RSV/H30全消费者独立。

原七天目标ACTIVE；D1-D6限定出口收口后进入D7，6/7不是工程量百分比。下一步：进入原D7：核对本期六阶段已采用主线/合同，执行缺少的定向跨仓集成，处理实际失败，完成总台账与最终生命周期审计；不扩大为完整125目标或等待企业数据。

## 2026-11-01：W247询盘运行链路生产业务验收

W247公开询盘原日历/时区、换版Attempt、NFC重放和实际后台到持久模拟消费者的生产业务验收通过；原D6限定出口核销。 6项不同有效结果、0跳过。[报告](../development/evidence/W247-01/DELIVERY.md)。CRM/Handoff已合并清理释放，Main待PR/finish。原D1/D3/D4/D5/D6限定出口闭合，D2/D7继续。5/7只是阶段数，不是工程完成百分比；不以此外推工时。H01仍按冻结合同仅Asia/Shanghai，完整WP03/隐私导出及运行/发布资格独立未完成；ops身份Unproven与正式ready不提升，生产部署未执行。

下一步：核对原D2补货/退货剩余出口和直接前置，实施实际缺项；缺失业务资料及外部服务直接模拟。随后完成D7本期相关跨仓/页面最终集成和总收尾，不扩展全部125目标。

## 2026-11-01：W246 身份与实际Owner生产业务验收

W246身份HTTP基线/实际Owner/原生撤权的模拟生产业务验收通过；两种提交顺序、原键重放与撤权后披露拒绝已验证，原D5限定出口核销。 实际PG/HTTP最终2/2通过、0跳过。[报告](../development/evidence/W246-01/DELIVERY.md)。CRM/Handoff已合并清理释放，Main待PR/finish。本轮正常合并finish后D1/D3/D4/D5原限定出口闭合；D2/D6/D7未关闭。完整WP02-29、正式运行/发布身份与生产部署独立，ops identity仍Unproven且正式新效果ready不提升。4/7仅为阶段数，不是工程完成百分比。

下一步：继续D6目标宿主规范化/时区和适用传播时限的证据映射与缺项场景；随后完成原D2补货/退货余项和D7最终集成。业务资料及外部输入直接模拟，不等待企业数据，不扩展全125目标。

## 2026-11-01：W245 排程操作链与原D3出口

W245排程操作链收口：UTC微秒拖拽草稿/确认、准确独立操作/计划/预览/队列/工作入口、原工作只读核对、当前能力和Owner链接；原七天D3限定出口核销。 12项相关后端、64项页面场景及类型检查通过。[报告](../development/evidence/W245-01/DELIVERY.md)。Handoff已合并清理释放，Main待PR/finish。模拟资料用于生产验收，实际本地隔离环境，未部署生产。原七天目标ACTIVE，原D3限定出口完成后转D5核销，完整WP08不关闭。

下一步：先核对原D5宿主采用的实际剩余与W130–143证据，以模拟身份/服务/传输执行适用集成；原D2/D6/D7按原出口核销，不继续扩成全APS或125目标。

## 2026-11-01：W244 统一排程看板与同捕获统计

W244统一排程看板：明确Site、完整引用筛选、同捕获KPI与稳定分页、未知/未排/历史状态、工厂日周时间轴和准确操作详情。 9项相关后端、23项页面场景及类型检查通过。[报告](../development/evidence/W244-01/DELIVERY.md)。Handoff已合并清理释放，Main待PR/finish。模拟资料用于生产验收，实际本地隔离环境，未部署生产。原七天目标ACTIVE，不关闭完整D3。

下一步：按原MES-SCHED-01 AC01–36核对D3综合验收，补齐拖拽草稿/独立详情路由等真实差距；继续原D2/D5/D6/D7直接剩余范围。生产业务验收缺失输入直接模拟，不等待企业数据。

## 2026-11-01：W243 资源换机与准确队列迁移

W243资源换机：完整上下文/原计划与六阶段固定键、持久Owner请求/回执、自身五域、停止与未知恢复、WO/RES/SCHED共同绑定采用、准确assignment一次队列迁移及页面。 39项相关后端、23项页面场景及类型检查通过。[报告](../development/evidence/W243-01/DELIVERY.md)。Handoff已合并清理释放，Main待PR/finish。模拟资料用于生产验收，实际本地隔离环境，未部署生产。原七天目标ACTIVE，不关闭完整D3。

下一步：继续原D3统一BoardCapture/筛选/KPI/分页；保留原D2/D5/D6/D7剩余范围。生产业务验收缺少输入直接模拟，不等待真实企业数据。

## 2026-11-01：W242 资源队列与EXEC投影

W242同资源执行队列：完整捕获/明确位置/服务端清单、CAS共同事务与原结果、当前执行锁和迟到/重复事实投影、页面确认与恢复。 27项相关后端、23项页面场景及类型检查通过。[报告](../development/evidence/W242-01/DELIVERY.md)。Handoff已合并清理释放，Main待PR/finish。模拟资料用于生产验收，实际本地隔离环境，未部署生产。原七天目标ACTIVE，不关闭完整D3。

下一步：继续原D3资源换机六阶段及准确assignment唯一队列迁移，然后统一BoardCapture/筛选KPI；保留原D2/D5/D6/D7剩余范围。缺少生产验收输入直接模拟，不等待企业数据。

## 2026-11-01：W241 自动排列与完整Root批次

W241自动排列与完整Root批次：稳定优先级/交期/路线/ID排序、完整500项清单、明确时长来源与未知约束、每Root原子组、跨RootDAG/部分完成/原键恢复及页面。 28项不同后端、23项页面场景及类型检查通过。[报告](../development/evidence/W241-01/DELIVERY.md)。Handoff已合并清理释放，Main待PR/finish。模拟资料用于生产验收，实际本地隔离环境，未部署生产。原七天目标ACTIVE，不关闭完整D3。

下一步：继续原D3资源队列、资源更换六阶段与统一看板接线；D2通用Transfer/RSV/ActualFact和退货商业财务、D5/D6/D7原剩余范围保留。缺少生产验收输入直接模拟，不等待企业数据或新增资格证明链。

## 2026-11-01：W240 完整Root操作改期

W240完整Root TIME_ONLY改期：准确Snapshot/Occurrence与五域、保留扩展根包络、WO/SCHED共同事务、单写范围、不可变计划及原结果恢复、独立权限与页面。 19项不同后端、15项页面场景及类型检查通过。[报告](../development/evidence/W240-01/DELIVERY.md)。Handoff已合并清理释放，Main待PR/finish。模拟资料用于生产验收，实际本地隔离环境，未部署生产。原七天目标ACTIVE，不关闭完整D3。

下一步：继续原D3自动排列的完整Root批次、资源变更与队列及实际看板接线；D2通用Transfer/RSV/ActualFact和退货商业财务、D5/D6/D7原剩余范围保留。缺失输入直接模拟，不等待企业数据或新增资格证明链。

## 2026-11-01：W239 补货实际直接移动与停止

W239补货实际直接移动与停止：Stock准确Root/Slice及既有Claim Binding、共享容量、Transfer原SourceDemand/完整Stage、独立批准和设备、REP共同事务及同cut未到转实存，原键恢复与页面。 20项不同PG/HTTP、18项页面场景通过。[报告](../development/evidence/W239-01/DELIVERY.md)。Handoff已合并、分支清理并释放；Main待PR/finish。模拟输入用于生产业务验收，部署单列未执行。原七天目标ACTIVE，不关闭完整D2。

下一步：推进原D3计划写入；继续D2 ActualFact/Case有权处置、通用RSV/MASTER/质量接线、退货商业/财务衔接、历史任务族与原D5/D7集成。验收输入缺失直接模拟，不等待企业数据。

## 2026-11-01：W238 人工草稿与准备登记

W238人工补货草稿与准备登记：完整创建/编辑及版本历史，准确Root/目标预算、prepare原子登记、持久原键及发送知识，实际API/页面和模拟生产验收。 14项PG/HTTP、15项页面实际场景通过。[报告](../development/evidence/W238-01/DELIVERY.md)。Handoff已合并并释放；Main待PR/finish。原七天目标继续，生产验收缺少数据直接模拟。

下一步：继续REP实际Transfer准备/派发、SourceDemand全Stage共享采用、实际物理最终门及停止；然后推进原D3计划写入。缺少验收资料直接模拟。

## 2026-11-01：W237 补货建议与登记

W237补货建议与批量登记：完整cut、准确维度及Root分配、类型化容量、原子批量和提交即计目标义务，实际API/页面及模拟生产验收。 11项PG/HTTP、18项页面实际场景通过。[报告](../development/evidence/W237-01/DELIVERY.md)。Handoff已合并并释放；Main待PR/finish。原七天目标继续，生产验收缺少数据直接模拟。

下一步：继续REP人工DRAFT/prepare、SourceDemand与Transfer全Stage共享采用、实际物理最终门及停止；然后推进原D3计划写入。缺少验收资料直接模拟。

## 2026-11-01：W236 调拨实际执行

W236原生调拨Stage及页面：准确请求份额、实际Stock双腿同事务移动、预留与属性保持、当前容量/权限、永久关闭和原操作恢复。 11项PG/HTTP、10项页面实际场景通过。[报告](../development/evidence/W236-01/DELIVERY.md)。Handoff已合并并释放；Main待PR/finish。原七天目标继续，生产验收缺少数据直接模拟。

下一步：继续补货规则、目标义务与SourceDemand共享采用，衔接完整调拨执行/取消；原生Stage已能实际移动，完整Root/Slice/Claim和typed容量仍保留。随后继续D3及其余原阶段，生产验收资料直接模拟。

## 2026-11-01：W235 调拨请求

W235原生调拨请求Owner和页面：创建/编辑/送审/独立批准或驳回/撤回，实际需求预算、历史与原操作事务，以及部门/双库位权限。 15项PG/HTTP、8项页面/映射实际场景通过。[报告](../development/evidence/W235-01/DELIVERY.md)。Handoff已合并并释放；Main待PR/finish。原七天目标继续，生产验收缺少数据直接模拟。

下一步：继续补货目标义务/SourceDemand共享采用、正式审批衔接及Transfer Stage/Stock同事务实际移动，然后D3规划写入；缺少生产验收资料直接模拟。

## 2026-11-01：W234 询盘模拟数据生产验收

询盘公开配置/Attempt/提交/H01与原回执，经原生Outbox到持久模拟消息服务和消费者的生产验收已通过；回滚、重建、并发去重、原文冲突与响应丢失分别验证。 CRM161已采用并释放；Handoff已释放，Main待正常PR/finish。[报告](../development/evidence/W234-01/DELIVERY.md)。原七天目标继续，生产验收允许模拟输入，无真实数据等待。

## 2026-10-29：W233 退货记录纠错与实际冲正

退货实收和五种已执行处置已接入准确冲正：版本草稿、送审、独立批准绑定、冻结方案、实际逆向库存与撤回；准确预算、源范围关闭、实收VOID、质量后继及RMA后继与原结果共同事务。 RMA-CORRECTION/1按Stock SX生命周期实现限定退货Owner，不接受客户端任意正负库存腿。模拟记录错误事实和独立审批用于生产验收，原业务/库存/QA历史保留，实际当前权限、源与目标范围、库存根去向、后继、预留与重复冲正预算检查。商业财务、非RMA通用库存更正、完整返修/NCR及外部事件派发继续原目标，不等待真实企业数据，不把局部验收写成完整WP10-22或七天目标完成。 [报告](../development/evidence/W233-01/DELIVERY.md)。Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W232 五个独立退货处置Owner

五个独立退货处置Owner已采用实际Stock：可售/内部返修MOVE、报废ADJUST LOSS、退供应商/退客户SHIP；准确实收范围占用、独立QC数量放行、业务回执和原请求恢复已接入页面。 RMA-DISPOSITION/2是历史R3 REQUIRED_FENCED处置目标的可执行后继，原归档保留。显式模拟业务批准、交易方与交接资料用于生产验收，实际库存/Owner/原命令/outbox共同事务执行。当前质量观察、批准版本、数量/容量/召回、源与目标当前权限、租户隔离和原有预留保持均实际检查；无效果封存才释放范围。实收VOID/冲正、已执行处置更正/SX、完整返修/NCR、商业财务与外部派发仍继续，不等待企业数据，不关闭完整WP10-22或七天目标。 [报告](../development/evidence/W232-01/DELIVERY.md)。Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W231 独立退货质量Owner

独立Main Quality退货检验Owner已接通实际实收来源、四态精确分区、版本更正与撤回；RMA由服务端采用真实质量原结果，页面支持判定与原请求恢复。 用户允许生产验收使用显式模拟政策、检验证据和业务来源。独立QA权限、登记人与签署人分离政策、当前RMA/IN读取范围、完整无重叠分区、并发CAS、不可变判定/outbox/原命令共同事务均由实际PostgreSQL实现。更正和撤回令旧RMA观察失效，质量本身不移动库存。当前交付为QA-NCR-01.RMA-QUALITY/1适配；完整NCR API、实收冲正/VOID、五终端处置、商业/财务和外部派发仍待实现，后续使用模拟数据继续，不等待企业数据。 [报告](../development/evidence/W231-01/DELIVERY.md)。Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W230 正式退货实物入库

正式退货已接入实物登记、部分收货、WMS-IN准备/提交/查询/关闭，以及实际Stock和RMA共同事务；实际页面提供登记、确认和原请求恢复。 用户允许生产验收使用显式模拟来源、库存根和质量政策。原库存根RETURN/RETURN与新来源RECEIPT/RECEIVE均实际生成原生收货单、Stock流水、精确申领消耗和不可变结果；全部同一PostgreSQL事务。实际账号身份完整保留，PG收货/流水操作人容量从20无损扩到100。冲正、质量判定和五种最终处置、商业/财务、特批、自动到期与待发事实发送器仍待实施；不将本轮等同完整RMA。 [报告](../development/evidence/W230-01/DELIVERY.md)。Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W229 额度与独立授权

正式退货申请已接入同来源坐标额度、独立批准/拒绝/撤销、不可变批准份额和原命令恢复，实际页面已采用。 明确模拟签收/发票/历史占用供生产验收，实际PostgreSQL事务、当前权限、政策和独立审批逻辑运行。来源版本不重置占量，撤销保留已收货/未知段；没有真实数据不再构成等待。特批、正式实物入库、下游处置与商业/财务仍待实现。授权待发事件已同事务保存，尚未接入发送器。 [报告](../development/evidence/W229-01/DELIVERY.md)。Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W228 签收证据与验收口径

退货页面已接入签收证据登记、附件选择/下载、不可变原件、撤回历史与原请求恢复。 复用现有RMA事务/权限和发运/FileContent Owner，保留精确数量及原发运行，不累计重叠证据为已签收量，不写库存/财务。用户明确允许开发及生产验收使用模拟业务数据，已同步AGENTS与总目标，不再等待真实企业输入。可退额度、独立批准和正式入库仍待实现。 [报告](../development/evidence/W228-01/DELIVERY.md)。Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W227 退货政策发布

RMA实际页面已采用退货政策发布、停用、不可变历史与原命令恢复；政策由具备动作权限的人明确填写，不生成默认企业政策。 [报告](../development/evidence/W227-01/DELIVERY.md)。可退额度、独立批准、正式IN及原七天出口继续。Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W226 原商业退货来源

真实发运事务已冻结准确原订单商业行；退货申请详情通过独立来源Owner查询原价格/金额与当前变化，保留十进制字符串、原null和精确行身份。 [报告](../development/evidence/W226-01/DELIVERY.md)。完整商业可退依据、独立批准及IN仍待采用，不关闭WP04-34/WP10-22/D2。Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W225 正式退货申请页面

正式退货申请页面已接入真实来源、精确数量、草稿编辑、提交/撤回、稳定列表及原请求恢复；发货页面提供显式正式来源保存，早期申请入口保留。 [报告](../development/evidence/W225-01/DELIVERY.md)。字段权限修正见Issue #649，待远端采用后关闭。权益、独立审批及完整D2继续；Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W224 正式退货申请意图

正式退货申请已采用真实履约来源：可信范围、不可变选择、准确数量区间、创建/编辑/提交/撤回、版本CAS和原命令恢复；申请意图不产生退货授权、库存或财务效果。 [报告](../development/evidence/W224-01/DELIVERY.md)。页面、权益/独立审批及完整D2仍未完成；Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W223 真实发货与履约来源

真实发货可显式保存退货履约来源：准确订单行、实际产品/单位版本、无损基准数量范围和发货批次事实，与库存出库、包装及流水同一事务提交；按当前退货权限回读不可变来源。 [报告](../development/evidence/W223-01/DELIVERY.md)。正式退货权益/独立审批及D2仍未完成，七天目标继续；Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W222 准确订单行来源

订单生成出库单已在同一Owner事务保存准确订单行ID和不可变数量/单位快照；编辑按原行身份保留来源，退货来源直接采用该事实。修正生成时把计价单位当作数量单位的问题（Issue #645）。 [报告](../development/evidence/W222-01/DELIVERY.md)。正式退货授权及D2仍未完成，七天目标继续；Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W221 退货申请页面入口

退货申请页面已接入真实来源选择、精确数量文本、保存/提交、列表和历史版本，以及按当前身份隔离的原操作恢复。2项PostgreSQL/HTTP、18项相关页面测试、类型检查和Chrome渲染检查通过。 [交付报告](../development/evidence/W221-01/DELIVERY.md)。正式授权/权益及D2完整出口仍未完成，七天目标继续；Handoff已释放，Main待正常PR/finish。

## 2026-10-29：W220 原生退货申请与授权分离

原生退货申请API独立保存DRAFT/SUBMITTED不可变版本，不再将申请视为授权；实际出库来源、完整21,8数量、当前权限、同事务原操作与审计已落地。16项相关PostgreSQL/HTTP验证通过，0跳过。 [交付报告](../development/evidence/W220-01/DELIVERY.md)。API限定交付；前端、正式履约/权益和授权审批未完成。七天目标继续，Handoff已释放、Main待正常PR/finish。

## 2026-10-29：W219 内部契约与快速失败规则

按用户最新指示，将Trust Internal Contracts、Fail-Fast和精简门禁写入AGENTS：内部契约直接采用，边界负责必要校验，缺前置立即明确失败；减少重复证明、兜底与重跑。仅文档差异/引用及台账结构检查，产品测试0次。 [报告](../development/evidence/W219-01/DELIVERY.md)。W218已完整收尾；本轮Handoff已释放，Main待正常PR/finish，七天原目标继续。

## 2026-10-29：W218 出库分配库位控制待Main收尾

出库分配采用当前库位控制：选择合法替代，拒绝封锁/未知候选，提交前复核；整单失败回滚，已存预留可取消释放。25 distinct actual PostgreSQL/Program HTTP PASS;0skip;frontend unchanged。[报告](../development/evidence/W218-01/DELIVERY.md)。登记24.8分钟，预计45–70；Handoff已释放，Main待正常PR/finish。原七天出口不变；此前65–70%无加权依据，不作为实际完成率。

## 2026-10-29：W217 独立库存库位控制待Main收尾

独立库存普通新使用采用当前库位封锁，两端和提交前复核；保留原结果恢复、纯预留释放和内部盘点事实路径。69 distinct actual PostgreSQL/Program HTTP PASS;0skip;frontend unchanged。[报告](../development/evidence/W217-01/DELIVERY.md)。登记18.8分钟，预计45–70；Handoff已释放，Main待正常PR/finish。正式MASTER/Transfer/Stock及原七天出口不变。

## 2026-10-29：W216 库位本域控制待Main收尾

库位本域封锁、准确解除决定、原结果恢复及真实API/页面完成。10 actual PostgreSQL/Program HTTP (8local-control +2initialization);28Vue/type/build PASS;0skip。[报告](../development/evidence/W216-01/DELIVERY.md)。登记25.55分钟，预计60–100；Handoff已释放，Main待正常PR/finish。初始化与其他来源限制保留；正式MASTER/Transfer/Stock及原七天出口不变。

## 2026-10-29：W215 库存事实评估待Main收尾

准确候选绑定的真实库存事实、独立当前权限、精确数量、覆盖边界及API/页面已完成。7 actual PostgreSQL/Program HTTP;29Vue/type/build PASS;0skip。[报告](../development/evidence/W215-01/DELIVERY.md)。登记14.15分钟，预计60–100；Handoff已释放，Main待正常PR/finish。真实Owner覆盖/源映射/库存采用、保护与08E、完整MASTER/Transfer/Stock及七天出口保留。

## 2026-10-29：W214 执行意图底座待Main收尾

内部执行意图/准备原键/动作恢复、提交封闭竞争、事务回滚与全部必要动作持久化已完成。13 actual isolated PostgreSQL execution intent behavior/migration tests PASS;0skip;no UI change。[报告](../development/evidence/W214-01/DELIVERY.md)。登记11.75分钟，预计50–80；Handoff已释放，Main待正常PR/finish。真实Owner覆盖/源映射/库存采用、保护与08E、完整MASTER/Transfer/Stock及七天出口保留。

## 2026-10-28：W213 容量份额底座待Main收尾

内部容量域/准确份额/别名去重、同事务部分实现、原结果恢复和精确数量验证已完成。12 actual isolated PostgreSQL capacity behavior/migration tests PASS;0skip;no UI change。[报告](../development/evidence/W213-01/DELIVERY.md)。登记11.37分钟，预计50–75；Handoff已释放，Main待正常PR/finish。真实Owner覆盖/源映射/库存采用、保护与08E、完整MASTER/Transfer/Stock及七天出口保留。

## 2026-10-28：W212 时钟政策与时间评估待Main收尾

原生时钟政策、实际数据库观察、来源/当前权限、有效期边界、回拨/超时拒绝和原键恢复已验证。9 distinct PG18/Program HTTP; 0 ancillary checks; 30 distinct Vue;types/build PASS;0skip。[报告](../development/evidence/W212-01/DELIVERY.md)。登记13.58分钟，预计75–110；Handoff已释放，Main待正常PR/finish。实际规则生效、占用保护与激活、完整MASTER/Transfer/Stock及七天出口保留。

## 2026-10-28：W211 库位规则送审与真实审批绑定待Main收尾

准确候选送审、真实OA人类任务/决定、当前政策/角色/来源、退回重审/撤回迟到决定和原键恢复已验证。30 distinct PG18/Program HTTP; 0 ancillary checks; 40 distinct Vue;types/build PASS;0skip。[报告](../development/evidence/W211-01/DELIVERY.md)。登记27.96分钟，预计100–150；Handoff已释放，Main待正常PR/finish。实际规则生效、时钟占用与激活、完整MASTER/Transfer/Stock及七天出口保留。

## 2026-10-28：W210 库位规则字典与来源绑定待Main收尾

原生仓库规则字典、真实UOM/BASE来源、候选不可变绑定、当前独立权限、原键恢复及独立页面已验证。46 distinct PG18/Program HTTP; 0 ancillary checks; 44 distinct Vue;types/build PASS;0skip。[报告](../development/evidence/W210-01/DELIVERY.md)。登记26.69分钟，预计75–120；Handoff已释放，Main待正常PR/finish。真实批准/应用、时钟占用与激活、完整MASTER/Transfer/Stock及七天出口保留。

## 2026-10-28：W209 库位规则候选待Main收尾

库位类型化规则候选、不可变历史、独立字段/范围权限、差分更新及撤回、原键恢复与独立页面已验证。74 distinct PG18/Program HTTP; 5 ancillary checks; 44 distinct Vue;types/build PASS;0skip。[报告](../development/evidence/W209-01/DELIVERY.md)。登记17.52分钟，预计50–85；Handoff已释放，Main待正常PR/finish。真实规则字典来源/批准/应用及激活、完整MASTER/Transfer/Stock及七天出口保留。

## 2026-10-28：W208 库位隔离草稿待Main收尾

原生库位隔离草稿、实际旧读取/写入屏障、当前父身份/层级/权限、原键恢复与独立页面已验证。61 distinct PG18/Program HTTP; 36 distinct Vue;types/build PASS;0skip。[报告](../development/evidence/W208-01/DELIVERY.md)。登记20.73分钟，预计60–100；Handoff已释放，Main待正常PR/finish。旧仓库Site/条码来源、typed规则/激活、完整MASTER/Transfer/Stock及七天出口保留。

## 2026-10-28：W207 库位层级策略待Main收尾

显式库位层级策略、实际原生树兼容/结构版本、稳定父ID预检、当前权限与原键恢复及独立页面已验证。45 distinct PG18/Program HTTP; 58 distinct Vue;types/build PASS;0skip。[报告](../development/evidence/W207-01/DELIVERY.md)。登记15.75分钟，预计90–150；Handoff已释放，Main待正常PR/finish。正式Location初始化隔离/创建与完整MASTER/Transfer/Stock及七天出口保留。

## 2026-10-28：W206 仓库草稿待Main收尾

仓库DRAFT创建、真实Site/类型、当前权限/部门绑定、原键恢复、代码锚与旧业务使用隔离及独立页面已验证。33 PG18/Program HTTP; 45 Vue;types/build PASS;0skip。[报告](../development/evidence/W206-01/DELIVERY.md)。登记21.64分钟，预计120–200；Handoff已释放，Main待正常PR/finish。据点/负责人关系、Location和生命周期与完整Transfer/Stock及七天出口继续保留。

## 2026-10-28：W205 受信调拨来源待Main收尾

受信环境/模式/沙箱、数据库不可变归属、租户原生Owner注册和来源命名空间、同事务准确产品消费及独立管理页面已验证。14 PG18/Program HTTP; 43 Vue;types/build PASS;0skip。[报告](../development/evidence/W205-01/DELIVERY.md)。登记26.56分钟，预计100–170；Handoff已释放，Main待正常PR/finish。原件不回填，旧未知环境不能冒充生产；正式MASTER/T01/T02/T04/Stock及完整WP10-03/WP10-10与七天出口不关闭。

## 2026-10-28：W204 库存身份待Main收尾

独立货权/用途/准确产品身份与原件、真实单位/客户/工程映射/冻结基线来源、当前独立确认/双Owner技术依据、共同事务与原操作恢复已验证。14 PG18/Program HTTP; 60 Vue;types/build PASS;0skip。[报告](../development/evidence/W204-01/DELIVERY.md)。登记55.31分钟，预计90–170；Handoff已释放，Main待正常PR/finish。自有资料不是外部法律登记核验；目录不是Stock实际效果或完整TransferT02/T04，完整WP04-33/WP10-03/WP10-10与七天出口不关闭。

## 2026-10-28：W203 资源能力目录待Main收尾

原生机器/工作中心能力目录、准确单位/工时与来源原件、独立不可变版本、当前授权/共同事务、原操作恢复及工艺准确引用已验证。31 PG18/Program HTTP; 41 Vue;types/build PASS;0skip。[报告](../development/evidence/W203-01/DELIVERY.md)。登记28.83分钟，预计160–230；Handoff已释放，Main待正常PR/finish。能力是有权人员声明，现场资格、RES变更预检/Claim及生产许可仍未完成，完整WP08-16/WP05-08与七天出口不关闭。

## 2026-10-28：W202 原生工程基线待Main收尾

原生工程基线、当前来源与已应用专业评审共同事务、准确原件/成员/结果意图、取消竞争及后继工作版本已验证。21 PG18/Program HTTP; 25 Vue;types/build PASS;binding mapper/shared schema PASS;0skip。[报告](../development/evidence/W202-01/DELIVERY.md)。登记82.48分钟，预计180–300；Handoff已释放，Main待正常PR/finish。成功冻结范围为真实批准政策下的外购成品；INTERNAL资格和下游投递/REL/VER/WO/SCHED仍待，绑定证据限映射器。完整WP05-12与七天出口不关闭。

## 2026-10-28：W201 原生输出关系待Main收尾

原生完整输入/工序/输出关系、准确规格和受控证据、独立专业确认/撤回、当前与最终共同事务来源门、来源检查及独立入口已验证。19 PG18/Program HTTP; 14 Vue;types/build PASS;actual wire/shared schema PASS;0skip。[报告](../development/evidence/W201-01/DELIVERY.md)。登记73.09分钟，预计150–240；Handoff已释放，Main待正常PR/finish。完整WP05-10/WP05-11及七天出口保留，设备/工装实际资格、外部提供者和正式验收、Baseline/OutputBinding/REL/VER仍待。

## 2026-10-28：W200 迭代评审处理与可信撤回待Main收尾

原生专业决定、独立问题答复/验收、同事务应用回执与撤回竞争、真实原人Workflow撤回、后继轮次和HTTPS原结果恢复已验证。12 PG18/Program HTTP/C02 TLS;1 Int64 boundary unit;34 Vue;types/build PASS;0skip。[报告](../development/evidence/W200-01/DELIVERY.md)。登记105.01分钟，预计180–300；Handoff已释放，Main待正常PR/finish。完整WP05-20/WP12-19及七天出口保留，委托撤回/换人重新授权和其他Owner采用仍待。

## 2026-10-28：W199 完整候选评审包与持久OA送审待Main收尾

完整原生候选全集/评审包/绑定、当前规则与独立评审人、同事务评审阶段/双原操作/Outbox、实际OA接收/FLOW启动及HTTPS丢回执恢复已验证。决定/问题/Owner批准应用/撤回、冻结/WO/SCHED仍待。9 distinct PG18/actual HTTP/TLS;1 canonical unit;20 Vue;types/build PASS;0skip。[报告](../development/evidence/W199-01/DELIVERY.md)。登记72.6分钟，预计150–240；Handoff已释放，Main待正常PR/finish。原七天全部出口保留。

## 2026-10-28：W198 迭代候选方案与准确技术来源待Main收尾

原生多候选/独立版本、准确ENG技术原件、固定父方案与差异、当前成员/字段/来源授权、原子回滚与未知结果恢复及独立API/UI已验证。完整候选全集/OA/Owner采用、冻结/WO/SCHED仍待。10 distinct PG18/actual Program HTTP;28 Vue;types/build PASS;0skip。[报告](../development/evidence/W198-01/DELIVERY.md)。登记36.87分钟，预计90–150；Handoff已释放，Main待正常PR/finish。原七天全部出口保留。

## 2026-10-28：W197 工程项目与迭代任务待Main收尾

原生项目/成员/场地/编号、准确已批准迭代输入、计划/输入独立版本、暂停恢复、精确加权任务/实际交付物与独立确认退回、当前权限及原操作恢复已验证。完整WP05-18外部来源与豁免、候选/OA/关闭、冻结/WO/SCHED仍待。15 distinct PG18/actual Program HTTP;44 Vue;types/build PASS;0skip。[报告](../development/evidence/W197-01/DELIVERY.md)。登记55.25分钟，预计120–210；Handoff已释放，Main待正常PR/finish。原七天全部出口保留。

## 2026-10-28：W196 准确需求集FLOW评审与Owner批准待Main收尾

准确需求集/原文成员、实际专业FLOW签署、独立Owner采用与原生回执、C02/TLS丢失响应恢复、当前字段/对象/部门及页面已验证；WP05-14排除/冲突权威裁决、撤回/实际采用影响链和ITER/冻结/WO/SCHED仍待。36 distinct PG18/actual Program HTTP/C02/TLS;70 Vue;types/build PASS;0skip。[报告](../development/evidence/W196-01/DELIVERY.md)。登记56.1分钟，预计240–360；Handoff已释放，Main待正常PR/finish。原七天全部出口保留。

## 2026-10-28：W195 准确需求专业评价与批准来源待Main收尾

准确需求评价输入/native单位与无界、真实专业政策/固定包/独立签署/批准撤回、历史控制和同UoW来源、原键恢复/API/页面已验证；完整需求集FLOW、ITER/冻结/WO/SCHED及商业原Owner接线仍待。24 PG18/actual Program HTTP (12 professional evaluation,12 draft regression);25 Vue;types/build PASS;0skip。[报告](../development/evidence/W195-01/DELIVERY.md)。登记47.53分钟，预计180–300；Handoff已释放，Main待正常PR/finish。原七天全部出口保留。

## 2026-10-28：W194 独立工程需求草稿待Main收尾

独立工程需求来源包、准确版本/原文、关系防自证循环、精确判据与未定项、当前权限及原键恢复/API/页面已验证；正式关系/判据评审、批准需求集、ITER/冻结/WO/SCHED仍待。15 PG18/actual Program HTTP (12 requirement,3 department regression);39 Vue;types/build PASS;0skip。[报告](../development/evidence/W194-01/DELIVERY.md)。登记53.77分钟，预计100–160；Handoff已释放，Main待正常PR/finish。原七天全部出口保留。

## 2026-10-27：W193 工程准确来源与完整性检查待 Main 收尾

工程准确候选/政策、完整来源原文/水位、持久四态检查、同事务最终核对、原键恢复及API/页面已验证；正式评审/冻结与WO/SCHED仍待。39 local checks: 37 PG18/HTTP and 2 affected kernel/unit;30 Vue;types/build PASS;0skip。[报告](../development/evidence/W193-01/DELIVERY.md)。登记49.4分钟，预计240–420；Handoff已释放，Main待正常PR/finish。实际业务效果及七天全部出口保留。

## 2026-10-27：W192 独立质量规格与工程准确采用待 Main 收尾

独立质量规格、完整要求、准确专业签署/撤回、同事务源锁定、原结果恢复及API/页面/工程引用已验证；正式冻结与WO/SCHED仍待。13 local checks: 13 PG18/HTTP and 0 affected kernel/unit;25 Vue;types/build PASS;0skip。[报告](../development/evidence/W192-01/DELIVERY.md)。登记61.31分钟，预计180–300；Handoff已释放，Main待正常PR/finish。实际业务效果及七天全部出口保留。

## 2026-10-27：W191 文档准确送审与专业批准待 Main 收尾

准确文档送审、专业政策与角色、原生FLOW评审、Owner批准及回执可靠投递、原结果恢复与页面接线已验证；质量规范、正式冻结与WO/SCHED仍待。50 local checks: 49 PG18/HTTP and 1 affected kernel/unit;34 Vue;types/build PASS;0skip。[报告](../development/evidence/W191-01/DELIVERY.md)。登记124.5分钟，预计240–420；Handoff已释放，Main待正常PR/finish。实际业务效果及七天全部出口保留。

## 2026-10-27：W190 文档原件与准确内容绑定待 Main 收尾

文档独立Owner、原件与派生关系、不可变工作版本、同事务文件绑定、当前权限下载、工程组成与原结果恢复已验证；专业批准、质量规范与正式冻结仍待。36 local checks: 36 PG18/HTTP and 0 affected kernel/unit;67 Vue;types/build PASS;0skip。[报告](../development/evidence/W190-01/DELIVERY.md)。登记20.3分钟，预计90–150；Handoff已释放，Main待正常PR/finish。实际业务效果及七天全部出口保留。

## 2026-10-27：W189 不可变文件内容与当前授权读取待 Main 收尾

文件独立Owner、有界上传与原字节、真实Defender检查、当前父对象/字段权限、持久隔离、保留与历史恢复已验证；正式文档绑定、用途批准和处置图仍待。27 local checks: 12 PG18/HTTP and 15 affected kernel/unit;40 Vue;types/build PASS;0skip。[报告](../development/evidence/W189-01/DELIVERY.md)。登记25.23分钟，预计120–180；Handoff已释放，Main待正常PR/finish。实际业务效果及七天全部出口保留。

## 2026-10-27：W188 制造映射与逐产出关系待 Main 收尾

制造关系独立Owner、多对多位置映射、逐产出生成/投入/中间流、准确产品/单位/配置、历史差异与原结果恢复已验证；专业原证、正式关系集及冻结仍待。53 local checks: 53 PG18/HTTP and 0 affected kernel/unit;38 Vue;types/build PASS;0skip。[报告](../development/evidence/W188-01/DELIVERY.md)。登记23.85分钟，预计70–115；Handoff已释放，Main待正常PR/finish。实际业务效果及七天全部出口保留。

## 2026-10-27：W187 版本化BOM与准确材料来源待 Main 收尾

BOM独立Owner、多层稳定位置、准确材料/单位、实际ProductMaterial原件、历史/使用位置、工程组成与原结果恢复已验证；草稿不冒充冻结或生产资格。43 local checks: 43 PG18/HTTP and 0 affected kernel/unit;44 Vue;types/build PASS;0skip。[报告](../development/evidence/W187-01/DELIVERY.md)。登记19.62分钟，预计120–210；Handoff已释放，Main待正常PR/finish。实际业务效果及七天全部出口保留。

## 2026-10-27：W186 版本化工艺与准确来源待 Main 收尾

工艺独立Owner、稳定步骤与依赖、准确资源/工具/单位、实际ProductProcess原件、工程组成采用与原结果恢复已验证；草稿不冒充冻结或生产资格。42 local checks: 42 PG18/HTTP and 0 affected kernel/unit;45 Vue;types/build PASS;0skip。[报告](../development/evidence/W186-01/DELIVERY.md)。登记35.7分钟，预计120–210；Handoff已释放，Main待正常PR/finish。实际业务效果及七天全部出口保留。

## 2026-10-27：W185 业务回执可靠返回与缺链恢复待 Main 收尾

业务回执可靠返回、原事件查询、连续投影和固定快照缺链恢复已验证；不重复执行业务，缺失义务保持BLOCKED。26 local checks: 26 PG18/HTTP and 0 affected kernel/unit;0skip;no frontend changes。[报告](../development/evidence/W185-01/DELIVERY.md)。登记55.45分钟，预计120–210；Handoff已释放，Main待正常PR/finish。实际业务效果及七天全部出口保留。

## 2026-10-27：W184 业务应用回执与原结果待 Main 收尾

当前人类Owner应用资格、不可变回执/原命令/审计/待回报消息共同事务、固定快照查询与页面恢复已验证；缺失义务关闭来源保持BLOCKED。16 local checks: 16 PG18/HTTP and 0 affected kernel/unit;22 UI;0skip;typecheck/build passed。[报告](../development/evidence/W184-01/DELIVERY.md)。登记29.33分钟，预计90–150；Handoff已释放，Main待正常PR/finish。实际义务关闭/业务效果、回执投递摄取及七天全部出口保留。

## 2026-10-27：W183 实际审批决定可靠投递待 Main 收尾

原决定可靠投递、先查原事件、持久租约/epoch与当前服务授权、原确认恢复已接入实际TLS/C02/HTTP；运输接收不提升业务应用。19 local checks: 19 PG18/HTTP and 0 affected kernel/unit;0skip;no UI change。[报告](../development/evidence/W183-01/DELIVERY.md)。登记18.65分钟，预计60–100；Handoff已释放，Main待正常PR/finish。业务应用/页面及七天全部出口保留。

## 2026-10-27：W182 业务审批事实接收待 Main 收尾

实际服务与准确Subject当前授权、原决定/证据和Inbox/应用流共同提交已接入实际HTTP；确认丢失可原键恢复，不执行业务报废。17 local checks: 17 PG18/HTTP and 0 affected kernel/unit;0skip;no UI change。[报告](../development/evidence/W182-01/DELIVERY.md)。登记29.83分钟，预计120–210；Handoff已释放，Main待正常PR/finish。业务应用/页面及七天全部出口保留。

## 2026-10-27：W181 实际审批决定共同事务待 Main 收尾

实际原生票决、双版本、原命令与终态证据/Outbox共同事务已接入既有HTTP；冻结政策和当前权限保留。40 local checks: 24 PG18/HTTP and 16 affected kernel/unit;0skip;no UI change。[报告](../development/evidence/W181-01/DELIVERY.md)。登记42.81分钟，预计120–210；Handoff已释放，Main待正常PR/finish。业务应用/页面及七天全部出口保留。

## 2026-10-27：W180 任务原职责与当前委派授权待 Main 收尾

真实任务原Principal/规则/候选与原启动同事务；委派不覆盖原职责，当前权限/委派范围/SOD提供同事务参与检查。33 local checks: 17 PG18 and 16 affected kernel/unit;0skip;no UI change。[报告](../development/evidence/W180-01/DELIVERY.md)。登记26.36分钟，预计90–150；Handoff已释放，Main待正常PR/finish。TxD/业务回执和七天全部出口保留。

## 2026-10-27：W179 审批实例原子启动待 Main 收尾

原固定发布版本的实际实例/任务/token/history/读模型及启动完成回执同事务；原提交查询与原command回执分离。109 local checks: 22 PG18/HTTP and 87 affected kernel/unit;0skip;no UI change。[报告](../development/evidence/W179-01/DELIVERY.md)。登记34.94分钟，预计120–210；Handoff已释放，Main待正常PR/finish。TxD/业务回执和七天全部出口保留。

## 2026-10-27：W178 审批可靠投递待 Main 收尾

Owner原包可靠服务投递、持久租约/回执与原命令恢复已验证；28项后端（27PG/HTTP、1unit，其中19项明确复用）、6Vue及类型检查/构建通过。[报告](../development/evidence/W178-01/DELIVERY.md)。登记32.6分钟，预计120–210；Handoff已释放，Main待正常PR/finish。TxE/TxD/业务回执及七天全部出口保留。

## 2026-10-27：W177 审批接收待 Main 收尾

受信服务/原Owner证据、三层比较/别名、准确版本与同事务启动意图已验证；19本地检查（18PG/HTTP、1unit）通过，0skip。[报告](../development/evidence/W177-01/DELIVERY.md)。登记46.4分钟，预计120–210；Handoff已释放，Main待正常PR/finish。实际投递/TxE/TxD/业务回执及七天全部出口保留。

## 2026-10-27：W176 处置审批冻结待 Main 收尾

准确处置政策、冻结意图、原Outbox与命令同事务及页面恢复已验证；18后端、15UI、类型检查和构建通过，0skip。[报告](../development/evidence/W176-01/DELIVERY.md)。登记25.68分钟，预计90–150；Handoff已释放，Main待正常PR/finish。依赖来源/运行消费者/审批和七天全部出口保留。

## 2026-10-27：W175 审批绑定待 Main 收尾

审批绑定、不可变时间线、准确版本校验、原命令恢复和管理入口已验证；23后端、15UI、类型检查和构建通过，0skip。[报告](../development/evidence/W175-01/DELIVERY.md)。登记26.26分钟，预计90–150；Handoff已释放，Main待正常PR/finish。依赖来源/运行消费者/审批和七天全部出口保留。

## 2026-10-27：W174 独立启停待 Main 收尾

FLOW独立启停、准确版本校验、历史、原命令恢复和管理入口已验证；35后端、22UI、类型检查和构建通过，0skip。[报告](../development/evidence/W174-01/DELIVERY.md)。登记19.0分钟，预计60–100；Handoff已释放，Main待正常PR/finish。依赖来源/运行消费者/审批和七天全部出口保留。

## 2026-10-27：W173 准确发布待 Main 收尾

FLOW准确发布预览、不可变版本、原命令恢复、指定版本下一草稿和设计器已验证；26后端、20UI、类型检查和构建通过，0skip。[报告](../development/evidence/W173-01/DELIVERY.md)。登记25.1分钟，预计90–150；Handoff已释放，Main待正常PR/finish。依赖来源/启用/审批和七天全部出口保留。

## 2026-10-27：W172 流程草稿采用待 Main 收尾

FLOW草稿Owner新建/读取/保存、当前Scope/资源/字段/设计期策略、原命令与审计同事务、旧入口身份隔离及设计器已验证；42后端、14UI、类型检查和构建通过，0skip。[报告](../development/evidence/W172-01/DELIVERY.md)。登记64.09分钟，预计150–240；Handoff已释放，Main待正常PR/finish。发布/启用/审批和七天全部出口保留。

## 2026-10-27：W171 Scope授权门待 Main 收尾

Scope环境/租户/权限域登记、真实epoch/事务锁、14张当前权限源表writer参与、原操作恢复已验证；31本地检查（28PG18/HTTP、3unit），0skip。[报告](../development/evidence/W171-01/DELIVERY.md)。登记33.93分钟，预计90–150；Handoff已释放、Main待收尾。正式Workflow消费者/审批与七天全部出口仍开放。

## 2026-10-27：W170 流程定义只读与显式草稿待 Main 收尾

流程读取无副作用、显式准确来源草稿/原操作恢复及两个设计器已验证；27本地检查（12PG18/HTTP、15复用）、12组件及type/Vite通过。[报告](../development/evidence/W170-01/DELIVERY.md)。登记30.33分钟，预计45–75；Handoff已释放、Main待收尾。完整Workflow授权/发布/审批与七天全部出口仍开放。

## 2026-10-27：W169 报废申请与停新使用待 Main 收尾

不可变处置申请/显式停新使用/准确义务视图及恢复保护已验证；24本地检查（24PG18/HTTP）、22组件、type/Vite通过。[报告](../development/evidence/W169-01/DELIVERY.md)。登记21.14分钟，预计60–90；Handoff已释放、Main待收尾。正常使用资格、报废与七天全部出口仍开放。

## 2026-10-27：W168 维护实际结果认定与正式完成待 Main 收尾

维护原报告/发生/观察/独立认定及一次正式完成已验证；75本地检查（75PG18/HTTP）、29组件、type/Vite通过。[报告](../development/evidence/W168-01/DELIVERY.md)。登记28.53分钟，预计70–110；Handoff已释放、Main待收尾。正常使用资格、报废与七天全部出口仍开放。

## 2026-10-27：W167 实际使用认定与计数应用待 Main 收尾

实际使用原件/发生/观察/独立认定及一次性周期/终身计数应用已验证；95本地检查（88PG18/HTTP）、22组件、type/Vite通过。[报告](../development/evidence/W167-01/DELIVERY.md)。登记44.61分钟，预计90–150；Handoff已释放、Main待收尾。正常使用资格、报废与七天全部出口仍开放。

## 2026-10-27：W166 实物维护与周期恢复待 Main 收尾

实际维护开始/报告/完成、有效政策、MASTER维护恢复资格和独立周期恢复已验证；82本地检查（75PG18/HTTP）、16组件、type/Vite通过。[报告](../development/evidence/W166-01/DELIVERY.md)。登记48.84分钟，预计180–300；Handoff已释放、Main待收尾。正常使用/报废、技术批准与七天全部出口仍开放。

## 2026-10-27：W165 工程实物关联待 Main 收尾

准确工程/实物提案、独立双方Owner决定和受保护的同轮当前投影已验证；51PG/HTTP、16组件、type/Vite通过。[报告](../development/evidence/W165-01/DELIVERY.md)。登记34.62分钟，预计150–240；Handoff已释放、Main待收尾。技术批准/实际生命周期与七天原完整出口保持开放。

## 2026-10-27：W164 可信来源关联登记待 Main 收尾

客户/产品/库位真实来源已接入登记；v3独立请求保留旧v1/v2重放，当前来源权限覆盖分页/历史/恢复。48PG/HTTP、12组件、type/Vite通过。[报告](../development/evidence/W164-01/DELIVERY.md)。登记25.46分钟，预计90–150；Handoff已释放、Main待收尾。技术候选/生命周期/实际双Role及原七天完整出口保留。

## 2026-10-27：W163 客户原身份来源待 Main 收尾

客户原身份来源、当前权限/不可变版本/同事务精确采用能力已验证：17PG/HTTP、5种子单元、26UI及type/Vite通过。[报告](../development/evidence/W163-01/DELIVERY.md)。登记17.4分钟，预计60–90；Handoff已释放，Main待收尾。WMS真实关联采用及原七天完整出口保留。

## 2026-10-27：W162 登记原码点修复待 Main 收尾

BUG582已验证：新登记保留原码点，历史v1保持原字节/恢复语义并明确来源；37PG/HTTP、9UI、type/Vite通过。[报告](../development/evidence/W162-01/DELIVERY.md)。登记26.02分钟，预计25–45；Handoff已释放，Main待收尾。原七天及完整WP10-48范围保留。

## 2026-10-27：W161 实物登记待 Main 收尾

真实实物新建、旧记录准确 Site/周期观察起点、未知寿命及原操作恢复通过28PG/HTTP、45UI、type/Vite。[单份报告](../development/evidence/W161-01/DELIVERY.md)。登记21.11分钟，预计50–80；Handoff已释放，Main待收尾。非空可信关联、实际生命周期/工程双Role和完整原目标仍未完成。

## 2026-10-27：W160实物实例来源待Main收尾

实物实例准确来源/不可变历史、有限资料编辑、同事务HoldExact及实际页面通过PG14、InMemory7、UI62、type/Vite。[单份报告](../development/evidence/W160-01/DELIVERY.md)。登记20.43分钟，预计80–130；Handoff已释放，Main待收尾。Site/周期/终身/资格及完整原任务保持未完成。

## 2026-10-26：W159工具技术草稿待Main收尾

独立工具技术规格DRAFT Owner、不可变原件/版本、旧写者真实技术控制及实际页面恢复通过PG12/旧写者13/UI47/type/Vite。[单份报告](../development/evidence/W159-01/DELIVERY.md)。登记18.73分钟，预计90–150；Handoff已释放，Main待正常PR/finish。专业批准冻结、实物DOC与原目标全部出口保留。

## 2026-10-26：W158旧写者与原订单请求待Main收尾

全部旧PostgreSQL版型写者版本/同事务、原Order变更取消耐久请求和旧页面恢复通过PG13/原关联回归15/UI47/type/Vite。[单份报告](../development/evidence/W158-01/DELIVERY.md)。登记35.43分钟，预计90–150；Handoff已释放，Main待正常PR/finish。专业控制未证实、请求未应用、PE未执行；原七天全部剩余出口保留。

## 2026-10-26：W157原订单关联待Main收尾

原订单Owner共同事务CREATE_OR_LINK、准确原关联/原键恢复和来源页采用通过PG15、旧Owner兼容2、UI/router/auth37及type/Vite。[单份报告](../development/evidence/W157-01/DELIVERY.md)。登记26.16分钟，预计70–120；Handoff已释放、Main待正常PR/finish。PE未执行；旧写者完整收敛、正式Sales来源和原七天剩余出口未完成。

## 2026-10-26：W156版型来源待Main收尾

版型工具完整原身份/观察历史与当前Owner读取通过PG/HTTP17、目录5、UI/IAM/router52及type/Vite。[单份报告](../development/evidence/W156-01/DELIVERY.md)。登记22.01分钟，预计60–100；Handoff已释放、Main待正常PR/finish。旧writer/订单共同事务、正式技术与实物资格及原七天剩余出口未完成。

## 2026-10-26：W155实际时间单位待Main收尾

原Owner时间单位定义与资源工时读取通过PG/HTTP40、UI/IAM43及type/Vite。[单份报告](../development/evidence/W155-01/DELIVERY.md)。登记23.08分钟，预计45–70；Handoff已释放，Main待正常PR/finish。完整资源资格/计划写入和原七天剩余出口未完成，正式状态不变。

## 2026-10-26：W154实际资源来源待Main收尾

实际资源来源目录/普通资料原操作通过PG/HTTP46、UI/IAM29及type/Vite。[单份报告](../development/evidence/W154-01/DELIVERY.md)。登记25.73分钟，原估60–90；Handoff已释放，Main待正常PR/finish。完整资源资格/计划写入及原七天剩余出口未完成，正式状态不变。

## 2026-10-26：W153来源复核待Main收尾

BUG572嵌套来源最终复核修复通过65个不同PG/HTTP用例，原4个失败已复测；UI未改未重跑。[单份报告](../development/evidence/W153-01/DELIVERY.md)。登记12.62分钟，原估35–55；Handoff已释放，Main待正常PR/finish和Issue核销。七天原出口及所有正式状态保持。

## 2026-10-26：W152 CFG选项规则待Main收尾

CFG工程选项规则、批准替换/撤回、ENG准确规则同事务采用及独立页面通过PG/HTTP26、UI50不同用例、type/Vite。[单份报告](../development/evidence/W152-01/DELIVERY.md)。登记28.02分钟，原估75–120；Handoff已释放，Main待正常PR/finish。仅ENGINEERING_OPTIONS_ONLY，完整适用/替代/来源/rebase/评审冻结与原七天全部出口保持未完成。

## 2026-10-26：W151工程修订待Main收尾

工程修订/编号共同事务、工作快照/配置组成和独立页面通过PG/HTTP19去重项、UI35与type/Vite。[单份报告](../development/evidence/W151-01/DELIVERY.md)。登记35.85分钟，原估90–150；Handoff已释放，Main待正常PR/finish。仅WP05-03限定DRAFT工作面，真实父基线/变更/legacy、完整来源/规则、rebase/评审冻结与制造采用未完成；原七天ACTIVE，其余出口保留。

## 2026-10-26：W150工程映射待Main收尾

工程身份Owner/原ERP及实际Site双Owner映射/独立页面通过PG/HTTP16、UI33和类型/Vite验证。[单份报告](../development/evidence/W150-01/DELIVERY.md)。Main START至登记36.99分钟，原估90–150；Handoff已释放，Main待正常PR/finish。仅WP04-33/WP05-02有限前置；确认范围IDENTITY_SITE_ONLY，法人/技术Revision/Configuration/制造采用继续保留。原七天ACTIVE，正式D3及D2/D5/D6/D7全部出口不缩减。

## 2026-10-26：W149币种政策待Main收尾

原生币种政策Owner/准确来源/独立页面已通过相关PG/HTTP29、UI30及类型/Vite验证。[单份报告](../development/evidence/W149-01/DELIVERY.md)。Main START至登记26.08分钟，原估120–180；Handoff已释放，Main待正常PR/finish。仅WP04-33有限前置，原七天ACTIVE，工程/正式D3及D2/D5/D6/D7全部出口保留。

## 2026-10-26：W148登录权限修复待Main收尾

P1 Issue565冷登录普通页面权限初始化已验证：最终60项、类型/Vite与真实API浏览器回归通过。[单份报告](../development/evidence/W148-01/DELIVERY.md)。Main START至登记36.5分钟，原估80–120；Handoff已释放，Main待正常PR/Issue关闭/finish。仅WP02-03有限修复，原七天ACTIVE及币种/工程/正式D3与D2/D5/D6/D7出口不变。

## 2026-10-26：W147单位Owner及原产品绑定待Main收尾

原生单位配置/版本/独立权限/精确换算和原产品显式单位绑定已完成本地定向验证：实际PG/Kestrel43与最后迁移1（44去重），页面/恢复/IAM32及类型/Vite通过。[单份报告](../development/evidence/W147-01/DELIVERY.md)。Main START至登记72.28分钟，预计180–240分钟；Handoff已释放，Main待正常PR采用和finish。仅WP04-33有限记录，币种/双Owner工程/冻结/正式WO与SCHED及D2/D5/D6/D7仍保留，原七天ACTIVE。

## 2026-10-26：W146产品刷新/深链修复待Main收尾

[Issue556](https://github.com/GTX537/CP6/issues/556)首次路由挂载竞态最小修复；50项去重相关行为、类型/Vite构建及真实demo浏览器刷新/深链验证通过。Handoff已finish，Main仍待采用/Issue关闭。[单份报告](../development/evidence/W146-01/DELIVERY.md)。原计划80–120分钟，原七天ACTIVE与单位/工程范围不变。

## 2026-10-26：W145 P0产品绑定类型修复待Main收尾

[Issue562](https://github.com/GTX537/CP6/issues/562)最小类型修复：原vue-tsc构建和19项当前UI回归通过。单位Owner功能保留后继，原七天ACTIVE。Handoff已释放，Main继续。[单份交付](../development/evidence/W145-01/DELIVERY.md)。

## 2026-10-26：W144原产品身份与源版本提供者待Main收尾

原ProductMaster头身份来源已具有原生持久版本、不可变历史、当前授权读取及显式部门绑定；正式UOM/币种/双Owner工程映射和最终业务准入仍未证。 31项PG/HTTP及19项UI通过；Handoff已释放，Main正常收尾继续。[单份交付](../development/evidence/W144-01/DELIVERY.md)。完整七天仍ACTIVE。

## 2026-10-26：W143 CRM托管载荷启动门禁采用，Main收尾

CRM实际宿主托管载荷固定清单核验及启动拒绝已合并；观察为启动时静态CP6引用闭包的托管事实，完整运行/发布身份仍Unproven。 26项当前相关验证通过，CRM PR160与Handoff已采用、准确删除分支并释放；Main收尾继续。[总控交付](../development/evidence/W143-01/DELIVERY.md)。完整七天与发布资格状态不提升。

## 2026-10-26：W142调拨本地需求预算前置待Main收尾

调拨内部本地需求与准确来源区间持久保护已验证；正式T04主数据、当前权限和请求生命周期接线仍待，REP/RMA围栏不变。 11项PG组件检查完成；正式HTTP接线待。Handoff已清理分支并释放，Main收尾继续。[单份报告](../development/evidence/W142-01/DELIVERY.md)。原D2正式REP/RMA和七天剩余出口不提升。

## 2026-10-26：W141补货／退货服务器分页待Main收尾

补货／退货实际Owner已增加有权总数及服务器分页，两个页面可访问第501条后记录，保持当前授权、字段隐藏、原API兼容及迟到响应隔离。 31项相关PG/HTTP/Owner、21项页面及类型检查完成。Handoff已清理分支并释放，Main收尾继续。[单份报告](../development/evidence/W141-01/DELIVERY.md)。原D2正式REP/RMA和七天剩余出口不提升。

## 2026-10-26：W140库存创建归属与v2采用待Main收尾

库存调整/移库v2已采用明确新Stock归属，新Stock与流水映射、数量/序号/审计/原结果/Outbox同事务；实际库存页增加新建入口并保持身份隔离、原键恢复和v1兼容。 89项相关PG/HTTP/来源资格、28项页面/恢复及类型检查完成。Handoff已清理分支并释放，Main收尾继续。[单份报告](../development/evidence/W140-01/DELIVERY.md)。原D2其余创建/REP/RMA和七天剩余出口不提升。

## 2026-10-25：W139工单创建归属与恢复待Main收尾

无订单工单明确选择健康部门，新UUID、工序/材料、序号、映射及原结果同Owner事务；关联订单保持原来源范围，实际页面采用原操作恢复和当前身份保护。 37项相关PG/HTTP、19项页面/恢复及类型检查完成。Handoff已清理分支并释放，Main收尾继续。[单份报告](../development/evidence/W139-01/DELIVERY.md)。原D2其余创建/REP/RMA和七天剩余出口不提升。

## 2026-10-25：W138盘点创建归属与恢复待Main收尾

盘点创建明确选择当前健康部门；真实新单UUID、完整库存快照、明细/序号、归属映射、审计及不可变原结果同Owner事务保存。页面采用原操作恢复，未知结果不重发，原库存/字段/仓区及最终权限门保留。 95项相关PG/HTTP、19项页面/恢复及类型检查完成。Handoff已清理分支并释放，Main收尾继续。[单份报告](../development/evidence/W138-01/DELIVERY.md)。原D2其余创建/REP/RMA和七天剩余出口不提升。

## 2026-10-25：W137库存流水可信继承待Main收尾

本次真实库存流水从实际修改的Stock UUID当前健康映射继承部门，同事务保存精确来源审计；部门用户可恢复本次调整原结果。移库两腿、盘点、收货/品质/成品、出库、反冲及订单取消均接入；历史流水不按业务键回填。 相关实际验证171项通过；精确来源、当前权限、共同事务及订单取消保存点保留。Handoff已清理分支并释放，Main收尾继续。[单份报告](../development/evidence/W137-01/DELIVERY.md)。其余D2创建/REP/RMA及原七天出口不提升。

## 2026-10-25：W136新设备可信创建待Main收尾

新设备创建已接入明确部门选择、同事务新UUID映射及Main不可变原操作；当前权限/组织最终门保留，未知结果按原编号查询或关闭。页面不保存设备草稿，不自动重发；旧合法本人/全租户与add-only创建保留。 实际隔离PostgreSQL/HTTP验证覆盖明确部门/跨租户/过期、同事务额外映射与撤权回滚、并发重复、确认丢失原回执、当前query/原真人/精确UUID及原路径；相关96项通过，页面16项通过，Vue/TS检查通过。成功范围按真实输入复用，不计为正式生产验收。 Handoff已清理分支并释放；Main收尾继续。[单份报告](../development/evidence/W136-01/DELIVERY.md)。下一窗口D2其余创建/新流水继承，七天ACTIVE。

## 2026-10-25：W135 D6核定待Main收尾

按原INQ允许LeadNo为空与当前R1闭合schema核定公开/H01没有NUM/FILE调用，不冒充完整共同服务采用；原生日历发布、配置批准和Main→CRM当前隐私来源已经存在。企业政策/专业日历、真实身份/路由/密钥、normalizer与目标宿主资格、真实broker/消费者/SLO未证，D6保持PARTIALLY_ADOPTED。 当前精确源/原文/四个schema根与引用、有效schema hash、既有发布/权限调用已核对；九轮所引CRM成果为当前主线祖先，历史验证只按原范围引用。文档差异/链接与台账一致性，无产品测试或运行配置变化。 Handoff已清理分支并释放；Main收尾继续。[单份报告](../development/evidence/W135-01/DELIVERY.md)。下一窗口D2新对象可信归属实现，七天ACTIVE。

## 2026-10-25：W134审计来源修复待Main收尾

[CRM #157](https://github.com/GTX537/CP6.CRM/issues/157)已完成修复、真实复验、远端采用及finish后关闭。原实现两个自动受理场景均Expected10/Actual20；修复后相同3项真实HTTP/PG检查通过，必需0跳过，含宽合法输入、待审核无Lead、原请求重放的原回执/审计/事件/数量和异体冲突。 CRM/Handoff已清理分支并释放；Main收尾继续。[交付](../development/evidence/W134-01/DELIVERY.md)。继续原D6 NUM/FILE/PRIV在冻结公开提交/H01路径的准确适用与版本边界、真实政策/日历来源核定；D2新对象可信归属及REP/RMA共同门、D3计划写协议、D5实际宿主/企业运输和D7最终集成保留。已有日历原生发布与显式Owner批准入口须按实际源码复用，不重复构造；企业资料与真实运输不由夹具代替。

## 2026-10-25：W133原生事件发布恢复待Main收尾

20项唯一相关CRM本地检查通过、必需0跳过：真实原生Owner/HTTP/PG、五类原事件、原run/提交收据恢复、并发/当前权限及角色隔离、取消停止、保护漂移，含真实60分钟匿名化及最终代码原文复核。 CRM/Handoff已正常合并、准确分支删除及finish；Main正常收尾继续。[交付](../development/evidence/W133-01/DELIVERY.md)。下一独立任务先处理[CRM #157](https://github.com/GTX537/CP6.CRM/issues/157)，再核D6 NUM/FILE/PRIV在冻结公开提交/H01路径的准确适用、版本接口和真实政策/日历来源继续核对；当前真实Dapr/Kafka/企业TLS/ACL/凭据/登记/消费者/SLO仍未证。D2新对象归属及REP/RMA写Owner共同门、D3计划写入口、D5实际宿主独立资格和D7最终集成保留，不扩展全部隐私导出或125目标。

## 2026-10-25：W132原生操作面与观测待Main收尾

17项唯一相关CRM本地检查通过，必需0跳过：真实原生HTTP/PG、操作权限隔离、有界探针及取消、多宿主传播、Owner重放、未知COMMIT消息恢复、后台采集和最终持锁撤权。 CRM156/Handoff108已正常合并、准确分支删除及finish；Main正常收尾继续。[交付](../development/evidence/W132-01/DELIVERY.md)。D5实际宿主镜像/加载来源的适用验证器及独立资格仍待；当前Dapr/Kafka、企业TLS/ACL/凭据/登记和生产SLO未证。D2/D3写入口前置、D6原生Outbox发布/恢复及必要公共支撑、D7最终集成继续原出口，完整阶段/正式验收不提升。

## 2026-10-25：W131原生身份增量宿主待Main收尾

12项唯一相关本地检查通过：CRM11、Main1；必需0跳过。实际原生HTTP、普通PostgreSQL写入、提交后确认、最终持锁门撤权及实际Main原文跨进程采用均通过；未变成功来源复用，原失败保留。 CRM155/Handoff107已正常合并、准确分支删除和finish；Main正常收尾继续。[交付](../development/evidence/W131-01/DELIVERY.md)。D5原生API可观测性仍待；本轮实际Dapr/Kafka链路NOT_RUN，真实企业传输ACL/凭据/登记、协调撤销和生产传播SLO未证明。D2、D3、D6、D7继续原计划必要出口，完整D5与正式验收不提升。

## 2026-10-25：W130原生身份基线宿主待Main收尾

34项唯一相关本地检查通过：CRM29、Main5；必需0跳过，原失败及针对性复测保留，未变化成功证据按原适用范围复用。 CRM154/Handoff交付已正常合并、准确分支删除和finish；Main前置BUG543已修复关闭，Main正常收尾继续。[交付](../development/evidence/W130-01/DELIVERY.md)。D5可信delta入口、提交后ACK、及时跨仓撤权和原生API可观测性仍待；基线轮询不等于即时撤权。完整D5及正式目标/验收不提升；D2、D3、D6、D7继续原计划必要出口。

## 2026-10-25：BUG543身份目录当前迁移兼容

[Issue543](https://github.com/GTX537/CP6/issues/543)精确补齐新版独立对象映射和询盘范围函数准入，保留旧digest和安全拒绝。Main四项及CRM原始基线子进程一项相关本地验证通过。[报告](../development/evidence/BUG-543/DELIVERY.md)。正常PR后恢复W130原生后台联通；Issue待该原步骤通过后关闭，七天ACTIVE。

## 2026-10-25：W129原七天出口审计

D1保持DONE，D4按原首个Owner/可靠投递出口核对W43/44/50/96/98后关闭；2/7=28.6%仅为阶段关闭率。D2/D3实际写协议、D5原生PG身份宿主与观测、D6必要消息/共同服务/真实来源及D7最终集成仍未完。剩余42–70h为低置信净执行估算，外部等待另列，替代30–50h粗估。[单份报告](../development/evidence/W129-01/DELIVERY.md)。正式24/375、0/125和247NOT_RUN保持；七天ACTIVE，先推进D5基线/发布宿主。W128三仓已完整清理释放，W129文档审查通过，Handoff105已采用并准确清理/释放；Main正常PR/finish继续，最终以wave129-final-verification.json核销。

## 2026-10-25：W128导出下载当前来源待Main收尾

72项唯一相关本地检查通过：Main45项当前来源/兼容/PostgreSQL/真实登录OIDC/HTTP，CRM27项保护会话/当前来源/原始Main互操作；必需0跳过。 CRM153/Handoff104已正常合并、准确分支删除和finish；Main正常收尾继续。[交付](../development/evidence/W128-01/DELIVERY.md)。原生导出任务、加密制品/生成权限、当前单次下载与实际内容、页面和Outbox发布仍未交付；七天原计划出口先逐项审计，再决定必需下一窗口，不以来源完成冒充隐私业务完成。

## 2026-10-25：W127原生人工撤回同意待Main收尾

79项唯一相关本地检查通过：27项PostgreSQL/HTTP/实际浏览器、52项Web及相关页面类型/浏览器包构建；必需0跳过。包括真实原60分钟保留期，不改业务期限或时钟。 CRM/Handoff已合并、准确分支删除与finish；Main待正常合并。两个PG停止，正式总数不变、七天ACTIVE。[交付](../development/evidence/W127-01/DELIVERY.md)。下一窗口落实privacy:export/download的独立当前真人来源与明确字段用途映射，再实施原生导出任务/加密制品/单次下载及页面。原生Outbox发布、真实授权配置/既有环境迁移/Lead月跨度到期和正式业务验收仍未完成。

## 2026-10-25：W126人工撤回同意权限来源待Main收尾

48项唯一相关本地检查通过：Main17项PostgreSQL/真实登录OIDC/HTTP，CRM29项具体保护会话/HTTP/当前来源，跨仓实际签名/原始快照到CRM PostgreSQL2项；必需0跳过。 CRM151/Handoff102已合并、准确分支删除与finish；Main候选待正常合并。PG停止，正式总数不变、七天ACTIVE。[交付](../development/evidence/W126-01/DELIVERY.md)。原生撤回Owner、精确Submission/Consent资源、ETag/幂等槽、原事务即时事实和目标本地版本事件、页面入口紧随后继；撤回不改已有保留deadline。隐私导出/FILE当前下载/嵌套分类与原生Outbox发布仍另行采用。

## 2026-10-25：W125主体导出归属修复待合并

23项唯一相关本地检查通过：1项真实PostgreSQL主体归属回归、2项原主体导出检查、20项现有平台权限过滤检查，必需0跳过。Vue SFC/TypeScript编译与五语SSR通过（UI壳组件替身，不称浏览器或HTTP验证）。 [交付](../development/evidence/W125-01/DELIVERY.md)。Issue537基线失败已复现并修复；语言全仓1054既存缺项保留，扫描输入未变。自有PG停止，Main/Handoff最终合并及finish待，正式总数不变，七天ACTIVE。

## 2026-10-24：W124原生询盘保留Owner

92项唯一相关本地成功检查：44项PG/HTTP/实际浏览器及Worker、48项Web；必需0跳过。真实60分钟到期验证原子匿名化、hold阻断、原记录独立期限、正式outbox失败回滚和单次事件；长测在当前编译版本完成，显示期限、Lead投影及负例分别引用定向证据，不称全套重跑。 [交付](../development/dispatch/20261024-wave124/README.md)。CRM已合并finish，原生保留Owner/独立hold/lease/Worker与显示期限已采用。
Handoff [PR100](https://github.com/GTX537/CP6.AI-Handoff/pull/100)采用 `684da578537f6d91035562070ef2b05173b012cb`，准确分支已删除、DEV-2已finish释放。 Main最终PR/finish尚待；START后墙钟174.07分钟，原估120–180分钟，最终按Main finish计，不叠加各仓；不含先前只读调查。
人类隐私/导出及实际发布仍待，24/375、0/125、247NOT_RUN保持，七天ACTIVE。

## 2026-10-24：W123原生保留维护权限

29项唯一相关本地成功检查：Main13、CRM16；真实Main TLS控制器/服务认证/PG来源经实际CRM捕获、持久信任和当前事务，原H01提交回归、完整角色/当前动作、用途/租户/nonce/epoch/期限、撤权锁/恢复互斥和跨事务误用通过，必需0跳过。 [交付](../development/dispatch/20261024-wave123/README.md)。CRM已合并finish，原生维护用途和当前服务证明已采用。
Handoff [PR99](https://github.com/GTX537/CP6.AI-Handoff/pull/99)采用 `566e6e56acc5814b7252e1dcd1f1829befbb253f`，准确分支已删除、DEV-2已finish释放。 Main最终PR/finish尚待；START后墙钟27.14分钟，原估110–160分钟，最终按Main finish计，不叠加各仓；不含先前只读调查。
隐私/保留及实际发布仍待，24/375、0/125、247NOT_RUN保持，七天ACTIVE。

## 2026-10-24：W122原生询盘页面与动作

101项唯一相关本地成功检查：Main28、CRM26（PostgreSQL/HTTP/9种实际浏览器场景）、Web47；实际Main读取用途HTTP原字节通过CRM持久PG信任，当前权限/CSRF、原命令恢复、412、访问失效、截止时钟、并发/CAS和原读取回归通过，必需0跳过。 [交付](../development/dispatch/20261024-wave122/README.md)。CRM已合并finish，原生队列/详情、当前动作及稳定重试已采用；Handoff [PR98](https://github.com/GTX537/CP6.AI-Handoff/pull/98)采用 `fa331f5750c6155d2d5a74908a8b3c9708aa562f`，准确分支已删除、DEV-2已finish释放。
Handoff [PR98](https://github.com/GTX537/CP6.AI-Handoff/pull/98)采用 `fa331f5750c6155d2d5a74908a8b3c9708aa562f`，准确分支已删除、DEV-2已finish释放。 Main最终PR/finish尚待；START后墙钟24.05分钟，原估100–150分钟，最终按Main finish计，不叠加各仓；不含先前只读调查。
隐私/保留及实际发布仍待，24/375、0/125、247NOT_RUN保持，七天ACTIVE。

## 2026-10-24：W121原生询盘决策Owner

68项唯一相关本地成功检查：24项PostgreSQL/HTTP、44项Web；实际H01/拒绝图、幂等/CAS/并发、真实HTTP响应丢失、晚期回滚/期限/撤回、实际Node BFF、公开原操作重试与存储保护覆盖，必需0跳过。 [交付](../development/dispatch/20261024-wave121/README.md)。CRM已合并finish，原生人工放行/拒绝Owner和BFF已采用；Handoff [PR97](https://github.com/GTX537/CP6.AI-Handoff/pull/97)采用 `e30af151185e316255233a275ff5d12bf49a857a`，准确分支已删除、DEV-2已finish释放。 Main最终PR/finish尚待；START后墙钟42.12分钟，原估80–130分钟，最终按Main finish计，不叠加各仓；不含先前只读调查。
页面/动作展示和后继隐私/发布仍待，24/375、0/125、247NOT_RUN保持，七天ACTIVE。

## 2026-10-24：W120原生询盘读取Owner

64项唯一相关本地成功检查：22项游标/PostgreSQL/HTTP、42项Web；实际公开提交完整图→独立读取Owner→Node BFF，权限/租户/字段/期限/行锁变化及传输拒绝覆盖，必需范围0跳过。 [交付](../development/dispatch/20261024-wave120/README.md)。CRM PR146已合并finish，原生列表/详情读取Owner和BFF已采用。
Handoff [PR96](https://github.com/GTX537/CP6.AI-Handoff/pull/96)采用 `b28ac649bad6a0a6e1bab9bcd4301cd851519448`，准确分支已删除、DEV-2已finish释放。 Main最终PR/finish尚待；START后墙钟45.45分钟，原估110–170分钟，最终按Main finish计，不叠加各仓；不含先前只读调查。
人工放行/拒绝、页面和后继隐私/发布仍待，24/375、0/125、247NOT_RUN保持，七天ACTIVE。

## 2026-10-24：W119人员询盘来源绑定

54项唯一相关本地成功检查：Main12、CRM42，实际Main login/OIDC/HTTP原签名→CRM PostgreSQL持久信任互通；身份/完整角色源/epoch/nonce/用途/期限/撤权/会话CAS及传输拒绝覆盖，必需范围0跳过。 [交付](../development/dispatch/20261024-wave119/README.md)。CRM PR145已合并finish，Main实际Bearer来源签发与原冻结投影绑定。
Handoff [PR95](https://github.com/GTX537/CP6.AI-Handoff/pull/95)采用 `13bfc6a61e3da7a6cf6c5d4b632a4e12cde3b609`，准确分支已删除、DEV-2已finish释放。 Main最终PR/finish尚待；START后墙钟27.31分钟，原估70–110分钟，最终按Main finish计，不叠加各仓；不含先前只读调查。
队列/详情业务Owner和后继管理/隐私/发布仍待，24/375、0/125、247NOT_RUN保持，七天ACTIVE。

## 2026-10-24：W118公开提交BFF与原生页面

48项唯一相关本地成功检查：38 Web、10真实浏览器/原生HTTP与PG场景；最大Unicode/精确版本、原请求重放/坏响应、限流、迟到清理、回执及显式恢复覆盖，无必需跳过。UI过期/更新/拒绝注入范围明确，不冒充数据库过期验收。 [交付](../development/dispatch/20261024-wave118/README.md)。CRM PR144已合并finish，真实浏览器公开提交/回执及稳定原请求重试；Handoff [PR94](https://github.com/GTX537/CP6.AI-Handoff/pull/94)已采用 `c7fc5bdfb99bf8b13f7d8b89dd40861a784fde3a`，准确分支删除、DEV-2 finish释放。
Handoff [PR94](https://github.com/GTX537/CP6.AI-Handoff/pull/94)已采用 `c7fc5bdfb99bf8b13f7d8b89dd40861a784fde3a`，准确分支删除、DEV-2 finish释放。 Main最终合并/finish尚待；当前START后墙钟24.63分钟，原估145–220分钟，最终按Main finish计，不叠加各仓。前期只读调查不包含在此登记计时内。
Main正常PR/finish继续；后续管理/隐私/投递Owner仍待，24/375、0/125、247NOT_RUN保持，七天ACTIVE。

## 2026-10-24：W117公开提交/H01真实Owner

122项唯一相关本地成功检查：Main4、CRM118。真实Main PostgreSQL/TLS控制器到独立CRM完整提交；正常/待审/最大Unicode与精确版本、稳定重放/竞争/响应丢失/限额、权限来源、到期/取消/晚期失败回滚均验证。相关读取和Node BFF回归通过；无必需跳过。 [交付](../development/dispatch/20261024-wave117/README.md)。CRM PR143已合并finish，完整提交/H01事务及稳定重放，无用户会话伪装；Handoff [PR93](https://github.com/GTX537/CP6.AI-Handoff/pull/93)已采用 `eb641466f17e818b009f37e1fbd18b0ab83d9fb1`，准确分支删除、DEV-2 finish释放。
Handoff [PR93](https://github.com/GTX537/CP6.AI-Handoff/pull/93)已采用 `eb641466f17e818b009f37e1fbd18b0ab83d9fb1`，准确分支删除、DEV-2 finish释放。 Main最终合并/finish尚待；当前START后墙钟97.50分钟，原估205–325分钟，最终按Main finish计，不叠加各仓。前期只读调查不包含在此登记计时内。
Main正常PR/finish继续；公开POST BFF/页面与后续管理/隐私/投递Owner仍待，24/375、0/125、247NOT_RUN保持，七天ACTIVE。

## 2026-10-24：W116真实服务来源与当前持锁检查

231项唯一相关本地成功检查：Main118、CRM持锁39/来源基线发布26/兼容单元48；原始Main PostgreSQL服务快照实际跨仓接收、发布与持锁。必需范围无跳过；另1项历史整数角色导出检查未执行且不计通过；误选整类探索批次中止，不宣称全量通过。 [交付](../development/dispatch/20261024-wave116/README.md)。CRM PR142已合并finish，独立服务源/原生当前检查无用户会话伪装；Handoff [PR92](https://github.com/GTX537/CP6.AI-Handoff/pull/92)已采用 `d43a26d8f43a463bad58d60d72dc4bb92a59fcf7`，准确分支删除、DEV-2 finish释放。
Handoff [PR92](https://github.com/GTX537/CP6.AI-Handoff/pull/92)已采用 `d43a26d8f43a463bad58d60d72dc4bb92a59fcf7`，准确分支删除、DEV-2 finish释放。 Main最终合并/finish尚待；当前START后墙钟46.40分钟，原估110–170分钟，最终按Main finish计，不叠加各仓。前期只读调查不包含在此登记计时内。
Main正常PR/finish继续；H01完整授权与公开提交仍待，24/375、0/125、247NOT_RUN保持，七天ACTIVE。

## 2026-10-24：W115实际H01服务权限生产

Main显式服务UUID/角色登记与实际IAM冻结intake:release签名生产入口，本地84项唯一相关PG/HTTP检查通过、0跳过。[单份交付](../development/evidence/W115-01/DELIVERY.md)。Handoff [PR91](https://github.com/GTX537/CP6.AI-Handoff/pull/91)已采用740e7700c22c563a8b43858cb98bbe58de190e47，准确分支删除、DEV-2 finish释放；Main功能PR/finish继续。当前START后墙钟18.64min（原估75–115），最终按Main finish计一次。 PostgreSQL隔离实例停止、缓存保留，无Actions/既有迁移/部署。CRM服务当前身份/撤权及最终事务门禁、完整H01与公开提交继续；24/375、0/125、247NOT_RUN不变，七天ACTIVE。

## 2026-10-24：W114实际配置Owner与原图共同事务

CRM [PR141](https://github.com/GTX537/CP6.CRM/pull/141)已采用 `d69bf0ec98613b0d00b7f546eaa241fc0c631235`；受控Serializable原图事务及实际配置Owner同事务采用、源Head行锁保留身份七锁协议。83项唯一相关本地PG/HTTP检查通过、0跳过：7项受控事务成功复用、39项实际配置Owner、10项隔离v3、9项隔离v2、18项v1/v4原图与回执；锁协议冲突和测试版本混用均已定位修正，失败日志保留。 [单份交付](../development/dispatch/20261024-wave114/README.md)。CRM准确分支删除、DEV-2释放；Handoff [PR90](https://github.com/GTX537/CP6.AI-Handoff/pull/90)已采用 `cb37e979bda63af27c3ad43d94d93cc82acfb8c7`，准确分支删除、固定DEV-2 finish释放。 Main文档PR/finish继续，墙钟31.86min（原估65–100）。自有PG停止/缓存保留，无Actions/迁移/部署。消费重放、service/H01最终权限、完整提交与页面继续；24/375、0/125、247NOT_RUN不变，七天ACTIVE。

## 2026-10-24：W113公开回执独立Owner

CRM [PR140](https://github.com/GTX537/CP6.CRM/pull/140)已采用 `f5afba3e9bfb91119ca1d62bc5ff7bec3d121a51`；独立公开回执HTTP Owner与受保护Cookie、原PGv4读取事务及实际BFF接入。92项唯一相关本地检查通过、0跳过：62项PG/HTTP、30项Web与相关类型检查；真实Node BFF→API→PG回执和原表单，以及原配置写入/重放通过。首次缺失Node产物及新增Web夹具合同错误已修复并定向重验，其他有效成功复用，失败记录保留。 [单份交付](../development/dispatch/20261024-wave113/README.md)。CRM准确分支删除、DEV-2释放；Handoff [PR89](https://github.com/GTX537/CP6.AI-Handoff/pull/89)已采用 `5aac8893ba0917e623459b8f97fb17ea597b030e`，准确分支删除、固定DEV-2 finish释放。 Main文档PR/finish继续，墙钟26.13min（原估50–80）。自有PG停止/缓存保留，无Actions/迁移/部署。消费重放、service/H01最终权限、完整提交事务接入、完整公开提交待后继；24/375、0/125、247NOT_RUN不变，七天ACTIVE。

## 2026-10-24：W112原同意与回执图

CRM [PR139](https://github.com/GTX537/CP6.CRM/pull/139)已采用 `3f12f94215c1b7beec182312959ee4e39c77bd4f`；新库PGv4原图同意/回执绑定、延迟完整性、验证读取和不可逆隐私/撤销，原父行串行化两种竞争顺序。82项唯一相关本地检查通过、0跳过：32项真实PostgreSQL（19项本轮事实/边界、13项原v1图回归），50项旧模型/回执codec回归。一次集中审查发现回执输出合同风险，修复后定向复验5项；其他有效结果复用，失败记录保留。 [单份交付](../development/dispatch/20261024-wave112/README.md)。CRM准确分支删除、DEV-2释放；Handoff [PR88](https://github.com/GTX537/CP6.AI-Handoff/pull/88)已采用 `bceba80f0bcc2c5370c50b6069a981426dcfb27d`，准确分支删除、固定DEV-2 finish释放。 Main文档PR/finish继续，墙钟25.94min（原估60–100）。自有PG停止/缓存保留，无Actions/迁移/部署。消费重放、service/H01最终权限、宿主隔离、完整公开提交待后继；24/375、0/125、247NOT_RUN不变，七天ACTIVE。

## 2026-10-24：W111原日历与Intake图无损采用

CRM [PR138](https://github.com/GTX537/CP6.CRM/pull/138)已采用 `0323e3760c6638b48e5b0990968a574ceeb948aa`；显式新库PGv3保留原日历发布事实、原Intake全图和精确生效时间/假期，验证源内容、回滚和父子行并发，不构造业务成功。154项唯一相关本地检查通过、0跳过：23项真实PostgreSQL（10项本轮采用/边界、13项原v1图回归），131项旧Intake/Calendar/模型回归；生成器检查和受影响项目构建通过。 [单份交付](../development/dispatch/20261024-wave111/README.md)。CRM准确分支删除、DEV-2释放；Handoff [PR87](https://github.com/GTX537/CP6.AI-Handoff/pull/87)已采用 `a3b252cb3d307767bce3ef8349b991a9f8c7a36f`，准确分支删除、固定DEV-2 finish释放。 Main文档PR/finish继续，当前墙钟19.61min（原估40–70）。自有PG停止/缓存保留，无Actions/部署；真实宿主/隔离衔接、消费/回执、service/H01最终权限及完整公开提交待后继。24/375、0/125、247NOT_RUN不变，七天ACTIVE。

## 2026-10-24：W110原图无损版本与实际Bundle绑定

CRM [PR137](https://github.com/GTX537/CP6.CRM/pull/137)已采用 `6c181d249745a9fb1b77aaef3d19da931ab7f4ab`；显式PGv2原完整图保存任意精度FormVersion，真实Bundle与既有Site/Form同事务绑定及原生并发保护，不构造业务成功。75项唯一相关本地检查通过、0跳过：25项真实PostgreSQL原图/绑定与50项旧模型和attempt codec回归；生成器检查及受影响测试项目构建通过。 [单份交付](../development/dispatch/20261024-wave110/README.md)。CRM准确分支删除、DEV-2释放；Handoff [PR86](https://github.com/GTX537/CP6.AI-Handoff/pull/86)已采用 `c0af8315db75e64024919b15b43ba56f9b43154e`，准确分支删除、固定DEV-2 finish释放。 Main文档PR/finish继续，当前墙钟18.58min（原估70–110）。自有PG停止/缓存保留，无Actions/部署；原日历/Intake、消费/回执、service/H01最终权限及完整公开提交待后继。24/375、0/125、247NOT_RUN不变，七天ACTIVE。

## 2026-10-24：W109人工受理权限源与实际CRM互通

Main实际OIDC签发新增四人工intake用途，当前主管/健康部门/精确SiteForm和crm-intake完整字段源；缺失/撤销/不可表达范围拒绝，签名不是最终业务授权。CRM [PR136](https://github.com/GTX537/CP6.CRM/pull/136)已采用 `c5657a3918d589475a25e5dd4d932af9f11e7c97`。49项最终适用的唯一相关本地检查通过、0跳过：Main47项实际权限源/PG/HTTP与局部回归，CRM2项实际签名互通；受影响项目编译通过。 [单份交付](../development/dispatch/20261024-wave109/README.md)。CRM准确分支删除、DEV-2释放；Handoff [PR85](https://github.com/GTX537/CP6.AI-Handoff/pull/85)已采用 `6f21e118e8b6e0c54b3d637aec430ba34168fdb9`，准确分支删除、固定DEV-2 finish释放。 Main产品PR/finish继续，当前墙钟13.44min（原估50–80）。自有PG停止/缓存保留，无Actions/部署；服务身份/H01/无损版本桥/消费/公开提交及正式验收待后继。24/375、0/125、247NOT_RUN不变，七天ACTIVE。

## 2026-10-24：W108原完整CRM图PostgreSQL存储采用

CRM [PR135](https://github.com/GTX537/CP6.CRM/pull/135)已采用 `dfeba8e44d58a668b8f721b605a425f1bc5279b7`，原32组织表/codec/GraphStore使用明确PG profile，精度、租户复合FK、主行版本锁及全事务回滚验证。19项最终适用的唯一相关本地检查通过、0跳过：13项真实PostgreSQL行为、6项原模型元数据回归；受影响编译通过。 [单份交付](../development/dispatch/20261024-wave108/README.md)。CRM准确分支删除、DEV-2释放；Handoff [PR84](https://github.com/GTX537/CP6.AI-Handoff/pull/84) 已采用 `e10cab1497d1341d53a8538a5ab26eb93734a353`，原准确分支删除、固定DEV-2 finish释放。 Main最终合并/finish继续，当前墙钟24.52min（原估90–150）。仅显式新库/ReadCommitted，自有PG停止、缓存保留，无Actions或部署。原签发/新bundle身份桥接、消费/H01/编号/公开提交回执及正式验收待后继；24/375、0/125、247NOT_RUN不变，七天ACTIVE。

## 2026-10-24：W107原Attempt与三维原子限流前置采用

CRM [PR134](https://github.com/GTX537/CP6.CRM/pull/134)已采用 `a69098129ef64d1bd6ae0a96df8831890220ffce`，原IssuedAttempt/browser/CSRF/origin/ETag/DB时限、原政策与精确指纹、调用方同事务PG三桶计数采用。24项最终适用的唯一相关本地检查通过、0跳过：21项原Attempt/内容/PG三维限流，3项既有公开GET回归；受影响编译通过。 [单份交付](../development/dispatch/20261024-wave107/README.md)。CRM准确分支删除、DEV-2释放；Handoff [PR83](https://github.com/GTX537/CP6.AI-Handoff/pull/83) 已采用 `88364f6475e7ab58cf09eac933922b264f2bf8a8`，原准确分支删除、固定DEV-2 finish释放。 Main最终合并/finish继续，当前墙钟15.13min（原估45–75）。自有PG/HTTP停止、缓存保留，无Actions或部署。HTTP提交未开放，消费/原图/H01/编号/回执及正式验收待后继；24/375、0/125、247NOT_RUN不变，七天ACTIVE。

## 2026-10-24：W106公开表单与持久浏览器绑定Attempt采用

CRM [PR133](https://github.com/GTX537/CP6.CRM/pull/133)已采用 `c3e94a89e0c542ba3c5a8872ecb7bea55087cb89`，实际已激活配置公开GET、不可变PG IssuedAttempt、浏览器/CSRF摘要绑定及严格Node BFF接通。82项唯一相关本地检查通过、0跳过：26公开Owner/PG/HTTP/cookie/配置绑定（含实际Node BFF互通）、28相关配置/BFF回归、28Node，另含受影响编译与类型验证。 [单份交付](../development/dispatch/20261024-wave106/README.md)。CRM准确分支删除、DEV-2释放；Handoff [PR82](https://github.com/GTX537/CP6.AI-Handoff/pull/82) 已采用 `08789af72ad034874782164f1c314aa38a6665af`，原准确分支删除、固定DEV-2 finish释放。 Main最终合并/finish继续，当前墙钟26.93min（原估75–120）。自有PG/HTTP/Node停止，缓存保留，无Actions或部署。提交/回执/H01/公开页面、企业发布、正式发送和即时跨仓撤权待后继；24/375、0/125、247NOT_RUN不变，七天ACTIVE。

## 2026-10-24：W105配置预览、激活与数据库时间生效采用

CRM [PR132](https://github.com/GTX537/CP6.CRM/pull/132)已采用 `14ca04883e5aae7b856b13674d00b6777d1567d5`，实际当前权限、actor绑定短时proof、原结果/审批引用/审计/outbox共同事务、精确DB时间区间与页面接通。85项唯一相关本地检查通过、0跳过：20生命周期PG/HTTP、11真实浏览器、28相关会话/Owner、24Node、2实际Main/CRM互通。 [单份交付](../development/dispatch/20261024-wave105/README.md)。CRM准确分支删除、DEV-2释放；Handoff [PR81](https://github.com/GTX537/CP6.AI-Handoff/pull/81) 已采用 `53cddda6f0bbc6dd0db3d5481613b9f937772f41`，原准确分支删除、固定DEV-2 finish释放。 Main最终合并/finish继续，当前墙钟39.64min（原估180–270）。自有PG/HTTP/浏览器停止，缓存保留，无Actions或部署。公开表单/Attempt/提交、正式发送和即时跨仓撤权待后继；24/375、0/125、247NOT_RUN不变，七天ACTIVE。

## 2026-10-24：W104冻结配置页面与可信上下文采用

CRM [PR131](https://github.com/GTX537/CP6.CRM/pull/131)已采用 `77dd02db308c5a746fe0ae672e0a8fc13e108185`，原会话/当前授权上下文、冻结配置页面、显式政策、原键恢复/撤权清空及原生登录落点已接通。63项最终适用的唯一相关本地检查通过、0跳过：9上下文PG/HTTP、26原会话/Owner回归、22Node、6真实React浏览器到Node BFF及原生HTTP/PG Owner。 [单份交付](../development/dispatch/20261024-wave104/README.md)。CRM准确分支删除、DEV-2释放；Handoff [PR80](https://github.com/GTX537/CP6.AI-Handoff/pull/80) 已采用 `6cbbde316a20d7b5eb85f6180d59b9743e420ecc`，原准确分支删除、固定DEV-2 finish释放。 Main最终合并/finish继续，当前墙钟60.84min（原估120–180）。PG/HTTP/浏览器停止，缓存保留且未建junction；无Actions或部署。预览/激活、公开受理和即时跨仓撤权待后继；24/375、0/125、247NOT_RUN不变，七天ACTIVE。

## 2026-10-24：W103真实权限与Web BFF配置采用

CRM [PR130](https://github.com/GTX537/CP6.CRM/pull/130)采用 `0045bff214909b5282c975b583f79e8247beee2d`：原保护会话向实际Main签发请求权限，显式Node BFF到PG配置Owner，最终授权及原结果/业务/正式outbox共同事务保持；181项唯一相关本地检查通过、0跳过，包含实际Main与CRM两进程互通。[单份交付](../development/dispatch/20261024-wave103/README.md)。CRM分支删除、DEV-2已finish释放；Handoff [PR79](https://github.com/GTX537/CP6.AI-Handoff/pull/79) 已采用 `c1978b0a92fd81164751c6c5791e20a4c946ca06`，准确分支删除、固定席位finish释放。目录连接清理曾被自动审批拒绝；用户本机仅移除连接后，已核对目标缓存完整，官方bind/resume成功。 Main最后合并/finish继续，当前墙钟62.05min（原估90–150min，含审批等待）。冻结UI、预览/激活/公开受理及即时跨仓撤权待后继；24/375、0/125、247NOT_RUN不变，七天ACTIVE。自有PG/HTTP/Node停止，无Actions或部署。

## 2026-10-24：W102实际配置Owner与PG宿主交付

配置GET/保存真实Owner、当前精确授权、原结果/日历/内容/Bundle/审计/正式outbox同事务，默认关闭的PG API宿主已由CRM [PR129](https://github.com/GTX537/CP6.CRM/pull/129)采用 `d3615db81675ca6cdad2d9c777505d451a552931`。347项唯一相关本地检查通过、0跳过，[单份交付](../development/dispatch/20261024-wave102/README.md)。CRM/Handoff分支删除、固定席位finish释放；Main本次最后合并/finish，当前63.70min（原估120–180min）。Web BFF v2、预览/激活/公开受理与即时跨仓撤权仍待后继；24/375、0/125、247NOT_RUN不变，七天ACTIVE。自有PG/HTTP停止，无Actions或部署。

## 2026-10-24：W101真实Main配置权限签发交付

Main实际CP6.Web会话/当前PG动作/精确pair签发、纯Ed25519和默认关闭配置完成验证；CRM [PR128](https://github.com/GTX537/CP6.CRM/pull/128)已采用 `dc09f558d6070a807c59316adce50b35641d8ba6`。152项相关本地检查通过、0跳过，[单份交付](../development/dispatch/20261024-wave101/README.md)。CRM/Handoff分支删除、固定席位finish释放；Main本次最后合并/finish。配置Owner/跨仓当前授权和真实业务验收仍待继续，CanAuthorize=false；24/375、0/125、247NOT_RUN不变，七天ACTIVE。自有PG/HTTP已停止，无Actions或部署；当前22.45min，最终按Main finish计。

## 2026-10-24：W100精确Site/Form来源交付

Main实际IAM范围管理、原操作/审计/原C2 v3事件同事务完成相关验证，准备本次Main合并。CRM [PR127](https://github.com/GTX537/CP6.CRM/pull/127)已采用 `cab16a031c93d52da674f20984a2d503d35fc2a8`；173项唯一相关本地检查通过、0跳过，[本轮交付](../development/dispatch/20261024-wave100/README.md)。CRM/Handoff准确分支删除、固定席位finish释放；Main本次最后收尾。旧合同/注册兼容，CanAuthorize=false，真实签名生产者和配置Owner、明确环境Source升级仍待继续。24/375整卡、0/125目标、247正式NOT_RUN不变；七天ACTIVE。自有PG已停止保留，无Actions、既有库迁移或部署；当前约24.21min，最终按Main finish计。

## 2026-10-24：W99受保护命令会话宿主已采用

CRM [PR126](https://github.com/GTX537/CP6.CRM/pull/126)已采用main `e86ca195470f8e4fad29aab474cfbef696dde132`；54项相关本地检查通过，API/必要依赖编译通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/e86ca195470f8e4fad29aab474cfbef696dde132/docs/development/evidence/W99-01/DELIVERY.md)。CRM/Handoff准确分支删除、固定席位finish释放；Main协调PR/finish本次收尾。原会话固定、不续期、单次捕获、锁内重核已接线；CanAuthorize=false，真实Site/Form范围与业务Owner仍待接。整卡24/375、目标0/125、247正式运行NOT_RUN不变；七天ACTIVE。自有PG已停止保留，无Actions、迁移或部署。当前约12.77min，最终按finish计，不重复CRM/Handoff。

## 2026-10-24：W98实际MOVE页面已采用

Main [产品PR](https://github.com/GTX537/CP6/pull/509) source `98a4d468c264bcc050e2272ec798c6b3b6ab4072`进入远端main `0b8a3ee014e7bcfb1fe58adf5b2792f4e61a7992`，15路径一致；1原生PG/HTTP＋29Vue/client和类型检查通过，[单份交付](../development/evidence/W98-01/DELIVERY.md)。Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/74)已采用并删除准确分支、finish释放席位；Main同任务最后状态PR/finish待完成。1个PG停止保留，无Actions／既有库迁移／部署。当前11.47min，原估25–35min＋前置约2min，最终据finish计。实际ADJ/MOVE均已采用，正式Stock/TRANSFER验收保持未完成；七天ACTIVE，继续Site/Form Owner直接前置。

## 2026-10-24：W97 MOVE保管属性修复已采用

Main [产品PR](https://github.com/GTX537/CP6/pull/507) source `d05bce95ec9591a532d009847f6b5a38db216562`进入远端main `906bcaef5974c0bc3305c89387044e5b396496d6`，14路径一致；44原生PG＋26逻辑回归去重70项通过，[单份交付](../development/evidence/BUG-503/DELIVERY.md)。Issue503已关闭，Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/73)已采用并删除准确分支、finish释放席位；Main同任务最后状态PR/finish待完成。3个PG停止保留，无Actions／既有库迁移／部署。当前12.71min，原估25–40min＋独立前置约5min，最终据finish计。新MOVE页面后续采用，七天ACTIVE，全局计数不变。

## 2026-10-24：W89身份会话围栏与BUG-123已采用

CRM PR124/125均已采用，最新main `59845f28539d89582884db442e25e95cd29ce328`；Issue123已关闭，59个去重相关本地用例通过（W89 42，BUG-123本次18，交集1；未变输入按真实来源复用）。[身份围栏](https://github.com/GTX537/CP6.CRM/blob/59845f28539d89582884db442e25e95cd29ce328/docs/development/evidence/W89-01/DELIVERY.md)；[签名回执修复](https://github.com/GTX537/CP6.CRM/blob/59845f28539d89582884db442e25e95cd29ce328/docs/development/evidence/BUG-123/DELIVERY.md)。两个CRM任务及Handoff分支准确删除、固定树干净detached、finish释放；Main协调PR/finish待本次收尾。两个PG已停止保留，无Actions/既有库迁移/生产部署。CanAuthorize=false，真实Site/Form Owner仍待接，整卡24/375、目标0/125、247运行NOT_RUN不变。七天ACTIVE；下一独立优先Main Issue503。原准备21.33min＋恢复执行至此20.36min，等待和W90–W96不计，最终见wave89-final-verification.json。

## 2026-10-24：W96库存调整与恢复已采用

Main [PR504](https://github.com/GTX537/CP6/pull/504) source `c1d05fe9140afc240e1a1289a09c963083fe6433`进入远端main `87c491b36cf2694f017764a0f6ebe1c43c635f04`，19路径逐blob一致；1原生HTTP＋28Vue/client及类型检查通过。[单份交付](../development/evidence/W96-01/DELIVERY.md)。Handoff [PR71](https://github.com/GTX537/CP6.AI-Handoff/pull/71)已采用、准确分支删除、固定树干净detached、finish释放；Main同任务最后状态PR/finish待完成。新增MOVE未采用，已证保管属性失败留Issue503 Open。2个PG停止保留；当前约13.05min，原估60–90min＋独立前置6min，最终按finish计。用户明确授权W89重绑并继续，当前收尾后优先恢复；七天ACTIVE，整体目标与验收不提升。

## 2026-10-24：W95样品历史独立Owner已采用

Main [PR501](https://github.com/GTX537/CP6/pull/501) source `44f937ff8438912fb920d6771e639d51ead5333c`进入远端main `603b9d51c6853315bd33aa4e7561368cdd20a934`，29任务路径逐blob核对；17.NET/10Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W95-01/DELIVERY.md)。Handoff [PR70](https://github.com/GTX537/CP6.AI-Handoff/pull/70)采用、准确分支删除、固定树干净detached、finish释放。Main原任务最后状态PR/finish及Issue500关闭待完成，未提前声明释放。2个PG停止保留；当前约16.98min，原估40–65min＋独立前置5min，最终按finish计。完整Sample/Stock、125目标与247验收不提升，七天ACTIVE。W89跨仓重绑确认仍待答复，两原分支保存不清理、不绕过绑定限制。

## 2026-10-24：W94出货包装独立Owner已采用

Main [PR498](https://github.com/GTX537/CP6/pull/498) source `5612c1d99298f485e5c691970f2a8d16bb8b8227`进入远端main `88bea56ed8d52304d0117c97b0310e4e66cdba03`，26任务路径逐blob核对；29.NET/15Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W94-01/DELIVERY.md)。Handoff [PR69](https://github.com/GTX537/CP6.AI-Handoff/pull/69)采用、准确分支删除、固定树干净detached、finish释放。Main原任务最后状态PR/finish及Issue497关闭待完成，未提前声明释放。3个PG停止保留；当前约14.52min，原估40–65min＋独立前置4min，最终按finish计。完整OUT、125目标与247验收不提升，七天ACTIVE。W89跨仓重绑确认仍待答复，两原分支保存不清理、不绕过绑定限制。

## 2026-10-24：W93出库路由独立Owner已采用

Main [PR495](https://github.com/GTX537/CP6/pull/495) source `b4dd45c3c2823767b3bcdc5cd1b919c501aed642`进入远端main `384305513c9d1c8afbf22e84c044d742a8c62efd`，33任务路径逐blob核对；25.NET/42Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W93-01/DELIVERY.md)。Handoff [PR68](https://github.com/GTX537/CP6.AI-Handoff/pull/68)采用、准确分支删除、固定树干净detached、finish释放。Main原任务最后状态PR/finish及Issue494关闭待完成，未提前声明释放。3个PG停止保留；当前约17.56min，原估45–70min＋独立前置4min，最终按finish计。完整RT/RSV、125目标与247验收不提升，七天ACTIVE。W89跨仓重绑确认仍待答复，两原分支保存不清理、不绕过绑定限制。

## 2026-10-24：W92仓库库位独立Owner已采用

Main [PR492](https://github.com/GTX537/CP6/pull/492) source `78213f208946fefa7f24e88c51fb9055d888bf2e`进入远端main `41cbbe6cdbd564557ead95aca7fab2d96bf99a42`，33任务路径逐blob核对；19.NET/31Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W92-01/DELIVERY.md)。Handoff [PR67](https://github.com/GTX537/CP6.AI-Handoff/pull/67)采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue491关闭待完成，未提前声明释放。3个PG停止保留；当前约23.48min，原估60–90min＋独立前置4min，最终按finish计。正式MASTER WP10-01、125目标与247验收不提升，七天目标ACTIVE。W89跨仓重绑确认仍待答复，两原分支保存不清理、不绕过绑定限制。

## 2026-10-24：W91报表中心独立Owner已采用

Main [PR489](https://github.com/GTX537/CP6/pull/489) source `121dc62f27288eca82ad1ca455560e280eaa3d5a`进入远端main `5dfa31efd70989d645162964e70ee5d3f208a1aa`，27任务路径逐blob核对；24.NET/9Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W91-01/DELIVERY.md)。Handoff [PR66](https://github.com/GTX537/CP6.AI-Handoff/pull/66)采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue488关闭待完成，未提前声明释放。3个PG停止保留；当前约13.41min，原估45–70min＋独立前置4min，最终按finish计。正式Analytics WP10-44/45、125目标与247验收不提升，七天目标ACTIVE。W89跨仓重绑确认仍待答复，已保存两原分支不清理、不绕过绑定限制。

## 2026-10-24：W90套件独立Owner已采用

Main [PR486](https://github.com/GTX537/CP6/pull/486) source `5f3f648854a8d201d934d0a6e19bbdbf39094167`进入远端main `6f19fde5e3d9ea4483c8c04c9713c6743f835007`，30任务路径逐blob核对；24.NET/25Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W90-01/DELIVERY.md)。Handoff [PR65](https://github.com/GTX537/CP6.AI-Handoff/pull/65)采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue485关闭待完成，未提前声明释放。3个PG停止保留；当前约17.92min，原估45–65min＋独立前置4min，最终按finish计。正式KIT Composition、125目标与247验收不提升，七天目标ACTIVE。W89跨仓重绑确认仍待答复，已保存的两原分支不清理、不绕过绑定限制。

## 2026-10-24：W88全部收尾 / W89同事务当前身份

W89状态补记：CRM实现尚未开工。跨仓切换组合命令被自动审批以 `blocked by policy` 拒绝；已改为成功执行不删除目录/分支的Handoff保存释放，原分支远端保留，DEV-2仍为Handoff干净detached。固定目录重绑确认待答复，Main协调/只读设计继续，七天目标ACTIVE。

W88 Main482/483、Handoff64已采用，Issue481关闭，40独立相关用例通过，实际26.33min＋前置3min，准确分支删除和席位释放已核对。[W89](../development/dispatch/20261024-wave89/README.md)补CRM当前身份/会话在调用方业务事务内的原生持锁检查与最终重核，预计40–60min＋独立前置约4min；实际Site/Form权限生产者与业务Owner仍待接。七天目标ACTIVE。

## 2026-10-24：W88 VMI独立Owner已采用

Main [PR482](https://github.com/GTX537/CP6/pull/482) source `e23c73c0c3eb941a258fb59c2a7cd50dc6a9634c`进入远端main `73395fa783435c2b2bf6f09b218e135bf51177fe`，30任务路径逐blob核对；18.NET/22Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W88-01/DELIVERY.md)。Handoff [PR64](https://github.com/GTX537/CP6.AI-Handoff/pull/64)采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue481关闭待完成，未提前声明释放。5个PG停止保留；当前约24.48min，原估50–75min＋独立前置3min，最终按finish计。正式WP10-39/40正式计费/Finance、125目标与247验收不提升，七天目标ACTIVE。

## 2026-10-23：W87持久签名信任已采用

CRM PR122/source `f87d5dab2049bed802f77d1ee4e29e1a1122cd81`已采用远端main `1a7f0838c3db87d0b736fbe1c88d4ba9a0b1841f`；33独立相关用例通过（16PG＋17已有签名），实际数据库重启恢复与两个方向锁争用通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/1a7f0838c3db87d0b736fbe1c88d4ba9a0b1841f/docs/development/evidence/W87-01/DELIVERY.md)。持久材料/撤销、原操作重放和同事务当前验签已采用，实际权限生产者/Site/Form Owner及业务最终门待接。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W86库位优化历史观察Owner已采用

Main [PR478](https://github.com/GTX537/CP6/pull/478) source `593b54bdf494d7de949475804677dc694dee15e1`进入远端main `4246689a04513633ee4bd0cb5e29623bbbfb9722`，30任务路径逐blob核对；19.NET/22Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W86-01/DELIVERY.md)。Handoff [PR62](https://github.com/GTX537/CP6.AI-Handoff/pull/62)采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue477关闭待完成，未提前声明释放。3个PG停止保留；当前约11.09min，原估35–55min＋独立前置2min，最终按finish计。正式WP10-28/29库位优化执行/Finance、125目标与247验收不提升，七天目标ACTIVE。

## 2026-10-23：W85越库历史观察Owner已采用

Main [PR475](https://github.com/GTX537/CP6/pull/475) source `a8e2e8a098c3a8ad9e713cd5f8f8c789270631cc`进入远端main `bbf9ae10322d880f68380bdaddc8435280ff7895`，30任务路径逐blob核对；18.NET/21Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W85-01/DELIVERY.md)。Handoff [PR61](https://github.com/GTX537/CP6.AI-Handoff/pull/61)采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue474关闭待完成，未提前声明释放。2个PG停止保留；当前约14.70min，原估35–55min＋独立前置2min，最终按finish计。正式WP10-32越库执行/Finance、125目标与247验收不提升，七天目标ACTIVE。

## 2026-10-23：W84批次追溯与召回标记Owner已采用

Main [PR472](https://github.com/GTX537/CP6/pull/472) source `9607b710b6b926dbeb7735482f474cd2083b127f`进入远端main `4cbf720a21c783cd16fc0d8ca84857494463c2a1`，26任务路径逐blob核对；23.NET/12Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W84-01/DELIVERY.md)。Handoff [PR60](https://github.com/GTX537/CP6.AI-Handoff/pull/60)采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue471关闭待完成，未提前声明释放。4个PG停止保留；当前约17.80min，原估35–55min＋独立前置2min，最终按finish计。正式召回执行/Finance、125目标与247验收不提升，七天目标ACTIVE。

## 2026-10-23：W83物料缺料跟进Owner已采用

Main [PR469](https://github.com/GTX537/CP6/pull/469) source `778338a1048ca9bc41752ac691a27c68b6604013`进入远端main `3b3855ebd2b8125a90e7e1674ada216c8ae95a73`，29任务路径逐blob核对；37.NET/9Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W83-01/DELIVERY.md)。Handoff [PR59](https://github.com/GTX537/CP6.AI-Handoff/pull/59)采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue468关闭待完成，未提前声明释放。5个PG停止保留；当前约16.56min，原估60–90min＋独立前置2min，最终按finish计。正式procurement/Finance、125目标与247验收不提升，七天目标ACTIVE。

## 2026-10-23：W82告知摘要夹具修订已采用

CRM PR121/source `350d4d4261144607985cfb33770f630d09859b0f`已采用远端main `3e69fa188a43d261660fe871103bc2b7b400a674`；21项相关本地用例通过，无跳过，原S01/父/新revision只读校验通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/3e69fa188a43d261660fe871103bc2b7b400a674/docs/development/evidence/W82-01/DELIVERY.md)。告知/内容/展示及状态摘要一致修订，原件/父赢家事件/负例/重放及NOT_RUN保持，Issue109关闭。无PG启动/Actions/部署；真实Owner与公开受理继续待接。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调收尾后继续七天目标。

## 2026-10-23：W81库龄与效期读取Owner已采用

Main [PR465](https://github.com/GTX537/CP6/pull/465) source `4f73c3787ba74f9499b503c342d5fae8d8932966`进入远端main `0252d9e85450f6f2f46d0bc6639fff2cbdde10cc`，38任务路径逐blob核对；23.NET/27Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W81-01/DELIVERY.md)。Handoff [PR57](https://github.com/GTX537/CP6.AI-Handoff/pull/57)采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue464关闭待完成，未提前声明释放。4个PG停止保留；当前约17.48min，原估60–90min＋独立前置3min，最终按finish计。正式DISP/Finance、125目标与247验收不提升，七天目标ACTIVE。

## 2026-10-23：W80 FX26夹具因果链修订已采用

CRM PR120/source `2241b288c8a43a2f0293b0384e8e58d5ed8e224c`已采用远端main `82d726fbc4ae3d90a0b9cfcbf24b78516829fa17`；14项相关本地用例通过，无跳过，原S01及显式revision只读校验通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/82d726fbc4ae3d90a0b9cfcbf24b78516829fa17/docs/development/evidence/W80-01/DELIVERY.md)。六条事件及携带副本按实际赢家审计修订，原件/负例/重放及NOT_RUN保持，Issue118关闭、109开放。无PG启动/Actions/部署；真实Owner与公开受理继续待接。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调收尾后继续七天目标。

## 2026-10-23：W79工作中心与工序费率当前Owner已采用

Main [PR461](https://github.com/GTX537/CP6/pull/461) source `119e3659d5f773595693a776c7322954b704c92d`进入远端main `c7964dd87ce374aaf4f74b9ea29b50380aab2744`，36任务路径逐blob核对通过；37.NET/25Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W79-01/DELIVERY.md)。Handoff [PR55](https://github.com/GTX537/CP6.AI-Handoff/pull/55)采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue460关闭待完成，未提前声明席位释放。六个PG停止保留；当前约46.18min，原估120–180min＋独立前置6min，最终按finish计。完整工艺/成本/Finance、125目标和247验收不提升；七天目标ACTIVE。

## 2026-10-23：W78配置事件原子入队已采用

CRM PR119/source `fcef696c1e955770255efac07d789e6437f338e4`已采用远端main `5297a8e641f21b089858078cabe4c11779984aad`；37项独立本地PG用例通过，无跳过；相关API/必要依赖构建通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/5297a8e641f21b089858078cabe4c11779984aad/docs/development/evidence/W78-01/DELIVERY.md)。实际审计到Platform原生Outbox的共同事务已采用，完整tenant事件身份、原生提交绑定/规范字节和Blocked路由通过；CRM Issue109/118开放，真实Owner/投递、激活与公开受理待接。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W77配置保存审计已采用

CRM PR117/source `6ac69e5e33c2ab078ecaef03e256a557728efe65`已采用远端main `49d06e17bc1d4547ef0d6dd9a934cd1a735ddb3e`；31项独立本地PG用例通过，无跳过；相关API/必要依赖构建通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/49d06e17bc1d4547ef0d6dd9a934cd1a735ddb3e/docs/development/evidence/W77-01/DELIVERY.md)。实际Bundle派生的保存审计及延迟原槽/Resource/ETag提交约束已采用，共同回滚/重放、原生竞争、精确时间和原始失败记录均核对，CRM Issue109保持开放；真实Owner/Outbox、激活生命周期与公开受理待接。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W76不可变Draft Bundle已采用

CRM PR116/source `631734a0ac4c620226c989b71a4ce1b710db55f3`已采用远端main `5deac62fc4d00a731b5d2dea79877572dc39a49b`；25项独立本地PG用例通过，无跳过；相关API/必要依赖构建通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/5deac62fc4d00a731b5d2dea79877572dc39a49b/docs/development/evidence/W76-01/DELIVERY.md)。不可变Draft Bundle和完整身份/视图/规范内容已持久化，混合隔离原生Gate及稳定槽回滚/原结果重放零再分配已实测；继任历史和告知原文边界通过，CRM Issue109保持开放；真实Owner/audit/outbox、激活生命周期与公开受理待接。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W75精确来源版本联结已采用

CRM PR115/source `01c40f6d2e0d47ff80fa88af2ac39c99e1e3137f`已采用远端main `318661183a8c9e72c4a5b1f2bfa1e5512d4495cf`；33项独立本地PG用例通过，无跳过；相关API/必要依赖构建通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/318661183a8c9e72c4a5b1f2bfa1e5512d4495cf/docs/development/evidence/W75-01/DELIVERY.md)。显式精确Intake/Calendar来源版本及实际三版本父图已采用，混合隔离原生Gate和三版本/W68原结果共同事务已实测；就绪自调用失败已修复并保留记录，CRM Issue109保持开放；完整Owner/Bundle/audit/outbox与公开受理待接。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W74精确表单版本身份已采用

CRM PR114/source `c04300a5b1c68c4aad78e4a45a29dd3f1b0af8b9`已采用远端main `67f8b28fd9b08b0be67060716e4729e0ba547c93`；29项独立本地PG用例通过，无跳过；相关API/必要依赖构建通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/67f8b28fd9b08b0be67060716e4729e0ba547c93/docs/development/evidence/W74-01/DELIVERY.md)。精确表单版本及真实准备内容绑定已持久化，混合隔离下原生Gate与W68原结果共同事务已实测；新旧内容历史不覆盖，CRM Issue109保持开放；完整Owner/Bundle/audit/outbox与公开受理待接。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W73原生配置对象绑定已采用

CRM PR113/source `7da2543b996806c53f5fa68831b2b61923e2b965`已采用远端main `dc8784583860b78635a89924ad2576ba24a21e78`；27项独立本地PG用例通过，无跳过；相关API/必要依赖构建通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/dc8784583860b78635a89924ad2576ba24a21e78/docs/development/evidence/W73-01/DELIVERY.md)。原生Site/Form/准备Intake及真实租户父关系已持久化，和W68原结果/W69内容共同事务已实测；fixture失败已修复并保留原记录，CRM Issue109保持开放；完整Owner/Bundle/audit/outbox与公开受理待接。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W72独立移动任务当前Owner已采用

Main [PR452](https://github.com/GTX537/CP6/pull/452) source `b3b38164e1d3732053f8241b86580a5d0f184f13`进入远端main `41cef2ffc77464745c18576e0f60108d59a8ec37`，36任务路径逐blob核对通过；50.NET/23Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W72-01/DELIVERY.md)。Handoff [PR48](https://github.com/GTX537/CP6.AI-Handoff/pull/48)采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue451关闭待完成，未提前声明席位释放。三个PG停止保留；当前约25.85min，原估120–180min，最终按finish计。完整S1/Source/Device/Claim、125目标和247验收不提升；七天目标ACTIVE。

## 2026-10-23：W71补充指示当前Owner已采用

Main [PR449](https://github.com/GTX537/CP6/pull/449) source `338c61c084e4e1a73c2e91c12807c307272c580f`进入远端main `f35ad97a42fdc4686b143a54c0c3b0f6c3ae106c`，27任务路径逐blob核对通过；25.NET/16Vue及类型检查通过，无跳过。[单份交付](../development/evidence/W71-01/DELIVERY.md)。Handoff [PR47](https://github.com/GTX537/CP6.AI-Handoff/pull/47)采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue448关闭待完成，未提前声明席位释放。三个PG停止保留；当前约27.09min，原估90–130min，最终按finish计。完整REP/独立移动Owner、125目标和247验收不提升；七天目标ACTIVE。

## 2026-10-23：W70退货当前Owner已采用

Main [PR446](https://github.com/GTX537/CP6/pull/446) source `748477dfa7a67d8b5cb8f4b6ba688b07585b2d8e`已进入远端main `8080bd7dda999bc2f8593866458a534f469b9631`，24任务路径逐blob核对通过；20.NET/7UI及类型检查通过（19.NET成功复用、1项异常包装断言修正复验），无跳过。[单份交付](../development/evidence/W70-01/DELIVERY.md)。Handoff [PR46](https://github.com/GTX537/CP6.AI-Handoff/pull/46)已采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue445关闭待完成，未提前声明席位释放。四个PG停止保留；当前约58.00min，原估90–140min，最终按finish计。完整RMA、125目标和247验收不提升，七天目标ACTIVE继续。

## 2026-10-23：W69配置草稿持久化已采用

CRM PR112/source `4477a42e3bc271461570cad4733f4c368be1a95c`已采用远端main `6c049934bad09ba230cb47cf8e6606db19331f18`；21项最终本地PG用例通过，无跳过；相关API/必要依赖构建通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/6c049934bad09ba230cb47cf8e6606db19331f18/docs/development/evidence/W69-01/DELIVERY.md)。不可变原文与实际租户日历绑定已持久化，和W68原结果共同事务已实测；FK执行状态缺口已修正，CRM Issue109保持开放；完整Owner/Bundle/audit/outbox与公开受理待接。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W68配置保存原结果已采用

CRM PR111/source `645da4a73a0cfde56f330d41291250c14b9f18da`已采用远端main `10985b5ca7f1f127f32768dec3983ee2e2d29571`；22项有效本地PG用例通过，无跳过；相关API/必要依赖构建通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/10985b5ca7f1f127f32768dec3983ee2e2d29571/docs/development/evidence/W68-01/DELIVERY.md)。原生稳定槽与原结果参与调用方事务，失败恢复及并发已验证；CRM Issue109保持开放；真实Owner最终授权、Bundle/audit/outbox和隐私保留待采用。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W67冻结表单内容已采用

CRM PR110/source `ab4a41c828d614058a04dc185299a284be542a56`已采用远端main `969bcc0660d96b41dcad102e1ef555b1c3bc6103`；20项本地相关用例通过，无跳过；相关API/Worker构建通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/969bcc0660d96b41dcad102e1ef555b1c3bc6103/docs/development/evidence/W67-01/DELIVERY.md)。完整表单内容与摘要、原告知及hash分别保留；CRM Issue109原fixture矛盾保持开放；结果不授权/不激活，真实Owner与Bundle保存/公开表单待采用。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W66附件引用与内容保留已采用

Main [PR440](https://github.com/GTX537/CP6/pull/440) / source `6aa764609839a2be32fa91d90d1668c3fbb59b7b`已进入远端main `f5bbbb08312170f6eda34f9d8d21582bb515a7bd`，20任务路径exact核对通过；19.NET/5UI通过（18项.NET首轮复用、1项夹具修正复验），无跳过。[单份交付](../development/evidence/W66-01/DELIVERY.md)。Handoff [PR42](https://github.com/GTX537/CP6.AI-Handoff/pull/42)已采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue439关闭待完成，未提前声称席位释放。三个PG停止保留；当前约29.88min，原估60–90min，最终按finish计。完整WP02-14及业务验收不提升，七天目标ACTIVE继续。

## 2026-10-23：W65采番串行推进已采用

Main [PR437](https://github.com/GTX537/CP6/pull/437) / source `d721ee65118084b798d66184c3e2ae00e209fdaf`已进入远端main `9d40f65cfc332e1543b4c0d762dcf7807042c99b`，13任务路径exact核对通过；8PG通过/4未改历史用例复用，无跳过。[单份交付](../development/evidence/W65-01/DELIVERY.md)。Handoff [PR41](https://github.com/GTX537/CP6.AI-Handoff/pull/41)已采用、准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue436关闭待完成，未提前声称席位释放。三个PG停止保留；当前约35.39min，原估50–80min，最终按finish计。完整WP02-16及业务验收不提升，七天目标ACTIVE继续。

## 2026-10-23：W64字段审计Owner已采用

Main [PR434](https://github.com/GTX537/CP6/pull/434) / source `183750bc45f39ce81499a11411914d3cf46de3aa`已进入远端main `a8a31bccdb57fb3a9a87da5aadb99be7b1673653`，22任务路径exact核对通过；27.NET/6UI通过、无跳过。[单份交付](../development/evidence/W64-01/DELIVERY.md)。Handoff [PR40](https://github.com/GTX537/CP6.AI-Handoff/pull/40)已采用，准确分支删除、固定树干净detached、finish释放。Main同任务最后状态PR/finish及Issue433最终关闭待完成，未提前宣布席位释放。两个PG已停止保留；当前约27.46min，原预算60–90min，实际以finish为准。完整WP02-13/业务验收不提升，七天目标ACTIVE继续。

## 2026-10-23：W63配置内容规范化已采用

CRM PR108/source `6c3feacec7a69243a25f6f5a78039922157990ad`已采用远端main `c7662eb196e72ddc8cab6d6c05490b6a310ca172`；12项本地相关用例通过，无跳过；相关API/Worker构建通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/c7662eb196e72ddc8cab6d6c05490b6a310ca172/docs/development/evidence/W63-01/DELIVERY.md)。原规范内容hash复现、精确数字/Unicode和原业务值分别保留；结果不授权/不激活，真实Owner与Bundle保存/公开表单待采用。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W62实际来源跨仓发布集成已采用

CRM PR107/source `54a7886b68ef025f5058861a62abadb0f306247b`已采用远端main `6bb3b43e6d674148af470b4b44aa0b46917f1f47`；1项实际Main/CRM跨进程场景通过，父子不重复计数、无跳过；两仓相关构建通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/6bb3b43e6d674148af470b4b44aa0b46917f1f47/docs/development/evidence/W62-01/DELIVERY.md)。原来源到恢复/发布、实际回滚/提交和租户指针失效已验证；结果仍不授权，真实Owner共同门及公开表单待采用。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W61配置草稿与PG日历绑定已采用

CRM PR106/source `7f7b168864b6b07bb5181e0a0987f39cebb7053a`已采用远端main `42a40ed606c5f457b89fed5feec01274b9799158`；19项去重相关行为及必要编译通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/42a40ed606c5f457b89fed5feec01274b9799158/docs/development/evidence/W61-01/DELIVERY.md)。原配置完整冻结、告知摘要和实际PG日历内容匹配采用；结果不授权/不激活，真实Owner/Bundle保存/公开表单仍待采用。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main当前协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W60不可变日历PG快照已采用

CRM PR105/source `2df15fbb46290b1f5f06c80e52251aca6eee746b`已采用远端main `8be1a856b50aaa68a733b8ca47df1ecd25fbc8a9`；92项相关本地用例及必要编译通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/8be1a856b50aaa68a733b8ca47df1ecd25fbc8a9/docs/development/evidence/W60-01/DELIVERY.md)。原日历身份约束、100ns无损存储、原生不可变守卫和事务采用；真实Owner/激活/公开表单仍待采用。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main当前协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W59询盘投影真实验签已采用

CRM PR104/source `73163779f0a5d4d213cd500023f6629baa849c63`已采用远端main `611e95cfc83928c192070c5b101f2c6b9f202183`；204项相关本地用例及API/Worker构建通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/611e95cfc83928c192070c5b101f2c6b9f202183/docs/development/evidence/W59-01/DELIVERY.md)。可信目录、真实Ed25519与轮换/撤销核查采用，结果CanAuthorize=false；生产信任、字段映射和真实Owner最终门仍待采用。CRM/Handoff准确分支删除、固定树干净detached、finish释放；Main当前协调PR/finish待收尾。七天目标ACTIVE，完整业务验收不提升。

## 2026-10-23：W58真实在线身份与PG当前事实核查已采用

CRM PR103/source `80af97ec22060c1dd32586db4ebb045e71762611`已采用远端main `aaed1098b35cf74fd286099f2bc31b1b9b071f80`；130项相关本地验证通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/aaed1098b35cf74fd286099f2bc31b1b9b071f80/docs/development/evidence/W58-01/DELIVERY.md)。具体OIDC与PG只读当前身份核查、原字段来源已采用；完整字段政策/映射与真实Owner最终门继续D5，CanAuthorize=false。CRM/Handoff准确分支删除、固定树干净detached、finish释放，Main当前协调分支待最终PR/finish。整卡/完整目标/真实验收不提升，七天目标ACTIVE。

## 2026-10-23：W57本次采集凭据与可失效发布读模型已采用

CRM PR102/source `b7e55240f583d9db287f7371a387485ed81b8aee`已采用远端main `6ff205740ac5290154588acd7840ad9f70cb7c96`；77项相关本地验证通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/6ff205740ac5290154588acd7840ad9f70cb7c96/docs/development/evidence/W57-01/DELIVERY.md)。本次完整采集凭据、原生完整发布/CAS及自动失效已采用；当前身份/字段授权与真实Owner最终门继续D5，CanAuthorize=false。CRM/Handoff准确分支删除、固定树干净detached、finish释放，Main当前协调分支待最终PR/finish。整卡/完整目标/真实验收不提升，七天目标ACTIVE。

## 2026-10-23：W56可信完整基线恢复与真实来源依赖图已采用

CRM PR101/source `b220c9298e3ec2f1ad477619c0ae001289a6cd64`已采用远端main `6bd4dc30c962c86fe6ba3661fc52e8bbbfc4b8b0`；64项相关本地验证通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/6bd4dc30c962c86fe6ba3661fc52e8bbbfc4b8b0/docs/development/evidence/W56-01/DELIVERY.md)。可信完整基线AuthoritySelection/resolvedRefs、原生完整恢复约束和真实来源依赖图已采用；Published/真实Owner最终门继续D5，CanAuthorize=false。CRM/Handoff准确分支删除、固定树干净detached、finish释放，Main当前协调分支待最终PR/finish。整卡/完整目标/真实验收不提升，七天目标ACTIVE。

## 2026-10-23：W55原始身份来源与正式内容门已采用

CRM PR100/source `6c8759caf2102849e0fc575c2f1b7ad41a5bd3f9`已采用远端main `8d8dd082232c6f74204c3e30be71c9922b8725ee`；102项相关本地验证通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/8d8dd082232c6f74204c3e30be71c9922b8725ee/docs/development/evidence/W55-01/DELIVERY.md)。显式PG v2来源/Current、正式内容PreCheckpoint/原journal、历史冲突与撤权/会话限制同事务；AuthoritySelection/Published/真实Owner最终门继续D5，CanAuthorize=false。CRM/Handoff准确分支删除、固定树干净detached、finish释放，Main当前协调分支待最终PR/finish。整卡/完整目标/真实验收不提升，七天目标ACTIVE。

## 2026-10-23：W54正式Platform与PG共同锁已采用

CRM PR99/source `9ad4d54a9b18c87c105c887355074cfb02c741e1`已采用远端main `46dfd16e0052eab44c1020970767f4f47fc5cf0d`；101项相关本地验证通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/46dfd16e0052eab44c1020970767f4f47fc5cf0d/docs/development/evidence/W54-01/DELIVERY.md)。当前Platform0.10.3引用、显式PG七锁profile及正式Inbox与实际写者锁互通已落实；完整内容PreCheckpoint/Published/最终Owner门继续D5，不形成Allow。CRM/Handoff准确分支删除、固定树干净detached、finish释放，Main当前协调分支待最终PR/finish。整卡/完整目标/真实验收不提升，七天目标ACTIVE。

## 2026-10-23：W53当前身份投影已采用

CRM PR98/source `796ef79442eb718ad311256b8f0e2598f2c3e104`已采用远端main `7c74f9c9471415df4f4f6429671b8665fafbb4cb`；22项相关本地验证通过。[单份交付](https://github.com/GTX537/CP6.CRM/blob/7c74f9c9471415df4f4f6429671b8665fafbb4cb/docs/development/evidence/W53-01/DELIVERY.md)。PG当前引用防回退、冲突关闭、原生租约/CAS和只读依赖图已落实；PendingInspection不形成Allow。CRM/Handoff准确分支删除、固定树干净detached、finish释放，Main当前协调分支待最终PR/finish。正式PreCheckpoint/完整Published/所有writer最终门及Owner共同事务继续D5，整卡/完整目标/真实验收不提升；七天目标ACTIVE。

## 2026-10-23：BUG419身份目录当前迁移兼容

[Issue419](https://github.com/GTX537/CP6/issues/419)修复已完成6项相关本地PG验证与集中作者审查。身份目录精确准入现行迁移14个业务函数，保留原ACL/拒绝未知与危险定义；[交付报告](../development/evidence/BUG-419/DELIVERY.md)。PR420已核对远端采用，BUG分支删除/DEV-1释放并resume原W52分支；Issue已在实际跨仓联通复测后关闭。继续W52 CRM完整原始基线。七天目标ACTIVE，不提升完整125目标/247运行验收。

## 2026-10-23：W52真实Main基线与PG原证据已合并

CRM PR97/source `4a26c9d32d52c5cbcf8abdc717de32636847ef2d`已采用远端main `248e179cc1b1e9bc31a0f3c09e36f7004a270f6d`；12项CRM及1项Main驱动通过（同一跨仓场景不重复计算），复用Issue419另6项Main验证。[单份交付](https://github.com/GTX537/CP6.CRM/blob/248e179cc1b1e9bc31a0f3c09e36f7004a270f6d/docs/development/evidence/W52-01/DELIVERY.md)。Issue419 PR420已采用并在实际联通后关闭；其分支/席位已收尾。CRM分支删除、工作区干净detached、finish释放；Handoff PR28也已核对采用并完成准确分支删除/finish释放；Main本协调分支最后合并/finish。完整Published/PreCheckpoint/Owner门待后续，Program未启新组件，完整目标/运行验收不提升，七天目标ACTIVE。

## 2026-10-23：W51 CRM PostgreSQL会话基础已合并

CRM PR96/source `83017508cb2c4a702ab8f5fb3bb62d73d12c96af`已采用远端main `48be253dfe3c4b451a9be7eaf9b7ef5f1199086a`；14项相关PG/.NET通过，一次作者审查完成。[单份交付](https://github.com/GTX537/CP6.CRM/blob/48be253dfe3c4b451a9be7eaf9b7ef5f1199086a/docs/development/evidence/W51-01/DELIVERY.md)详列原生CAS/撤权共同事务和剩余身份门。现有Program尚未切PG；完整卡/125目标/247运行不提升。自有PG已停止，测试data 76,150,437字节因自动审批拒绝准确删除而保留，无绕过。CRM PR96与Handoff PR27均已完成准确分支删除、工作区干净detached和finish释放；Main本协调分支最后合并/finish后继续D5，七天目标ACTIVE。

## 2026-10-23：W50库存Outbox与可信刷新投递已采用

产品[PR](https://github.com/GTX537/CP6/pull/416)及[Handoff](https://github.com/GTX537/CP6.AI-Handoff/pull/26)采用，source`68a0ecaad7c900ddb1beea591738b6a14457c9be`、远端main`e084e873c7a80dd9023d2f3bce137ecd2554b799`，24任务文件blob一致；Handoff source`bab3da6d4cb12b1f717781ea6200301058f7640e`、目标`9a1c8a0d9a101db87baece381e25e5ef996acb93`，准确分支删除/DEV-2释放。

[DELIVERY](../development/evidence/WP02-10/stock-outbox/DELIVERY.md)。七天目标ACTIVE；完整业务验收/整卡/125目标/247运行不提升。

## 2026-10-22：W49计划/达成率Owner已采用

产品[PR](https://github.com/GTX537/CP6/pull/414)及[Handoff](https://github.com/GTX537/CP6.AI-Handoff/pull/25)采用，source`d5e8fb07231cdbd5016cb028dd9ea6bab34fec51`、远端main`6cc3c5e3d205b43eded0e398ef673a3a29f5b5b6`，32任务文件blob一致；Handoff source`2ec59c822c266115bc26a50d0463374170133fc4`、目标`942eac113b9c456e37c0fea7ca4137b07eb8f80d`，准确分支删除/DEV-2释放。

[DELIVERY](../development/evidence/WP02-03/planning-owner/DELIVERY.md)。七天目标ACTIVE；正式RES/PERF/SCHED及整卡/125目标/247运行不提升。

## 2026-10-22：W48 OEE Owner已采用

产品[PR](https://github.com/GTX537/CP6/pull/411)和[Handoff](https://github.com/GTX537/CP6.AI-Handoff/pull/24)已采用，source`f4958debb891cbed296ed81df9afa3bf010c8edc`、远端main`bee338101c5bb99161c91fba8fb55f069a2b0435`，29任务文件blob一致；Handoff source`01959b4c349698ddd609e81c72dc2740a5673de8`、目标`cb2769fb5f816b7c1e37d0e760e1028aa67b0aa9`，DEV-2准确分支删除/席位释放。

[DELIVERY](../development/evidence/WP02-03/oee-owner/DELIVERY.md)。七天目标ACTIVE；下一计划Owner，正式RES/PERF仍未采用；整卡/125目标/247运行不提升。

## 2026-10-22：W47设备/停机Owner已采用

产品[PR](https://github.com/GTX537/CP6/pull/408)和[Handoff](https://github.com/GTX537/CP6.AI-Handoff/pull/23)已采用，source`9295ebac050bb43c3330638a59e27e71dc2ef8b2`、远端main`276e05aeeed41c560777e1d73ced49d0108387b1`，26任务文件blob一致；Handoff source`0536f372b2507fe7dceb85aa6a12e98fe881f8a5`、目标`722cc9d6a9034d181cd26d0ec07e186d24327b6c`，DEV-2准确分支删除/席位释放。

[DELIVERY](../development/evidence/WP02-03/machine-owner/DELIVERY.md)。七天目标ACTIVE；OEE/计划Owner下一窗口，正式RES/PERF仍未采用；整卡/125目标/247运行不提升。

## 2026-10-22：W46 WMS实时作用域已采用

产品[PR405](https://github.com/GTX537/CP6/pull/405)和[Handoff22](https://github.com/GTX537/CP6.AI-Handoff/pull/22)采用，source`c09ee7f4bf32b735f98b45e8164d7a1fd84d45f9`、远端main`883709c885d6b13ca272c211f8dde06a7cbd939f`；13任务文件一致。Handoff source`d5ced62b53dc502690df1ce75dade0e0f8ef2b3b`、目标`ad787e92495ad409e96358190bf9939753fb1ddb`，3项资料一致，DEV-2准确分支删除/席位释放。

[DELIVERY](../development/evidence/WP02-03/wms-hub-scope/DELIVERY.md)。七天目标ACTIVE；设备/OEE/计划完整Owner仍未完成，整卡/125目标/247运行不提升。

## 2026-10-22：W45 MES实时通道修复已集成

[产品PR402](https://github.com/GTX537/CP6/pull/402)和[Handoff PR21](https://github.com/GTX537/CP6.AI-Handoff/pull/21)采用；匿名协商、无租户业务广播和页面旧身份残留已复现并修复。4项新增PG/实际HTTP/WebSocket加32项相关回归执行（含1项新测试重跑）均通过；按显示名称去重31项，8项Vitest与完整vue-tsc -b通过；一次作者集中审查，[DELIVERY](../development/evidence/WP02-03/mes-realtime/DELIVERY.md)提供原始范围与失败来源。

DEV-2准确分支清理/释放，自有PG/API停止，生成data回收约655MiB。原DEV-1最后文档PR后finish并关闭Issue401，实际耗时和最终生命周期见管理记录/最终PR。七天目标ACTIVE，D3设备/OEE/计划仍待独立Owner，WmsHub作用域待实测；整卡/125目标/247运行不提升，无Actions/既有迁移/生产。

## 2026-10-22：W44库存原操作恢复已集成

[产品PR #399](https://github.com/GTX537/CP6/pull/399)与[Handoff PR #20](https://github.com/GTX537/CP6.AI-Handoff/pull/20)远端采用；实际库存Owner同事务原结果、精确对象当前读取及close完成。38项去重相关.NET（10新增PG/实际HTTP、28既有库存/Owner边界）、3项客户端Vitest及完整类型检查通过；一次作者审查完成，[DELIVERY](../development/evidence/WP02-10/stock-original-operation/DELIVERY.md)提供范围与来源。

DEV-2分支准确清理/释放，自有PG/API停止，生成data回收约330MiB。原W44-01最后文档PR后finish，准确生命周期/耗时见管理记录和最终PR。七天目标ACTIVE，D4仍局部采用；正式Transfer/REP/RMA/SCHED、可靠外部投递和库存页面恢复仍未完成。125/247及整卡不提升，无新树/代理/Actions/既有迁移/生产。W37–W43已实际收尾。

## 2026-10-22：W43 Main原操作恢复已集成

[产品PR #397](https://github.com/GTX537/CP6/pull/397)与[Handoff PR #19](https://github.com/GTX537/CP6.AI-Handoff/pull/19)远端采用；Main journal与PA090实际命令/页面的同事务原结果、当前授权恢复和close已交付。43项去重相关.NET（11新增PG/实际HTTP、32既有PA090）、16项Vitest及完整类型检查通过；原失败保留，所需范围通过；一次作者审查完成，[DELIVERY](../development/evidence/WP02-10/main-original-operation/DELIVERY.md)保留原失败。

DEV-2分支准确清理/释放，PG/API停止，生成data回收约545MiB。原W43-01最后文档PR后finish，准确生命周期/耗时见管理记录和最终PR。七天目标ACTIVE，D4首批已采用，继续可靠投递/实际Owner前置；旧batch与workflow AwaitingAdapter、Platform/其他业务未采用边界保留。125/247及整卡不提升，无新树/代理/Actions/既有迁移/生产。W37–W42已实际收尾。

## 2026-10-22：W42可信部门绑定已集成

[产品PR #395](https://github.com/GTX537/CP6/pull/395)与[Handoff PR #18](https://github.com/GTX537/CP6.AI-Handoff/pull/18)远端采用：四类显式部门绑定、IAM维护与现有Owner原生范围已交付。100项去重相关.NET（12新增PG/实际HTTP、88既有Owner/订单绑定）、25项Vitest与完整类型检查通过；原失败保留，所需范围0失败/跳过；一次作者审查完成，[DELIVERY](../development/evidence/WP02-03/standalone-department/DELIVERY.md)保留原失败。

DEV-2分支准确清理/释放，PG/API停止、生成data回收约442MiB。原W42-01最后文档PR后finish，准确生命周期/耗时见管理记录和最终PR。七天目标ACTIVE；D2首批已采用，继续补充或D3正式前置核对；RMA适配/MainUow/效果Owner及新对象创建归属保留。125/247及整卡不提升，无新树/代理/Actions/既有迁移/生产。W37–W41已实际收尾，不重复。

## 2026-10-22：七天一期D1质量Owner已集成

[产品PR #393](https://github.com/GTX537/CP6/pull/393)与[Handoff PR #17](https://github.com/GTX537/CP6.AI-Handoff/pull/17)核对远端采用，#392关闭。106项去重相关.NET（16本轮PG/实际HTTP、67既有Owner、23兼容/权限）、20项Vitest与完整类型检查通过；原失败与误选无关测试保留，所需范围0失败/跳过；一次作者审查及定向复查完成。[DELIVERY](../development/evidence/WP02-03/quality-owners/DELIVERY.md)。

DEV-2分支准确清理并释放；自有PG/API停止、生成data回收约552MiB，原失败保留。原W41-01最后文档PR后finish，实际分支/席位/耗时见实时登记与最终PR。[七天持续目标](../development/planning/20261022-seven-day-delivery.md)保持ACTIVE，直接继续D2补充/退货和可信范围，其后按计划推进Main/CRM/询盘与集成；125/247与完整卡不提升。无新树/代理/Actions/既有迁移/生产，W37–W40不重复。

## 2026-10-22：W40 MES不良记录Owner已集成

[产品PR #390](https://github.com/GTX537/CP6/pull/390)合入远端main`e3d3087f760fe59400f1371f8da360d5179e16e6`，[Handoff PR #16](https://github.com/GTX537/CP6.AI-Handoff/pull/16)合入默认资料目标，#389原步骤复测与采用后关闭。83项去重相关.NET成功（12本轮原生PG/实际HTTP、55既有Owner、16兼容/权限/审计），7项Vitest及完整类型检查通过；真实来源和复用范围见单份交付，最终适用集合0失败/跳过；作者审查及定向复查完成。[DELIVERY](../development/evidence/WP02-03/defect-owner/DELIVERY.md)。

DEV-2分支删除、席位释放；自有PG/API停止，5处生成data回收约0.538GiB。原W40-01最后文档PR后finish，真实分支/席位和实际耗时见管理登记/最终PR，不提前宣称未来释放。WMS库存QC/入库检验、MES品质检验自身Owner及补充/退货/设备/OEE/计划等独立入口、无订单部门绑定、采购/财务自身Owner、持久operation/outbox和可靠投递、workflow/CRM及真实DEV/UAT仍独立，125/247不提升。零新树/副本/代理；无Actions/既有环境迁移/真实授权/生产，W39不重做。

## 2026-10-21：W39 盘点、独立库存与看板Owner已集成

[产品PR #387](https://github.com/GTX537/CP6/pull/387)合入远端main`d4a26c8c9401385dad354dde986264e192a97fbb`，[Handoff PR #15](https://github.com/GTX537/CP6.AI-Handoff/pull/15)合入默认资料目标，#386原步骤复测及远端核对后关闭。123项去重相关.NET成功（14本轮原生PG/实际HTTP、38既有Owner、71兼容/权限），68项Vitest及完整类型检查通过；真实来源与复用范围见单份交付，最终适用集合0失败/跳过；作者审查及局部复查完成。[DELIVERY](../development/evidence/WP02-03/stocktake-dashboard-owner/DELIVERY.md)。

DEV-2分支删除、席位释放；自有PG/API停止，5处生成data回收约0.536GiB。原W39-01最后文档PR后finish，真实分支/席位和实际耗时见管理登记/最终PR，不提前宣称未来释放。其他QC/补充/退货/设备/OEE/计划等WMS/MES独立入口、无订单对象部门绑定、采购/财务自身Owner、持久operation/outbox和可靠投递、正式workflow/CRM及真实DEV/UAT仍独立，125/247不提升。零新树/副本/代理；无Actions、既有环境迁移、真实授权或生产；W38不重做。

## 2026-10-21：W38 报工／入库Owner已集成

[产品PR #384](https://github.com/GTX537/CP6/pull/384)合入远端main`8f15c5d5a333b54b99fe0fc7d5ea50fbbec2f176`，[Handoff PR #14](https://github.com/GTX537/CP6.AI-Handoff/pull/14)合入默认资料目标，#383回归及远端采用后关闭。90项去重相关.NET成功（20本轮原生PG/实际HTTP、18既有Owner PG/HTTP、52兼容/权限），17项Vitest（8新+9既有）及完整类型检查通过；三记录复用83项有效成功、最后7项局部复测，最终适用集合0失败/跳过；作者审查及局部复查无本窗剩余阻断。[DELIVERY](../development/evidence/WP02-03/production-inbound-owner/DELIVERY.md)。

DEV-2分支删除、席位释放；自有PG/API停止，7处生成data回收约0.753GiB。原W38-01最后文档PR后finish，实际结果见登记和最终PR说明，不提前宣称自身未来释放。其他WMS/MES入口（盘点/看板等）、无订单对象正式部门绑定、采购/财务自身Owner、持久operation/outbox与可靠投递、正式workflow/CRM及真实DEV/UAT仍独立，125/247不提升。零新树/副本/代理，无Actions/既有环境迁移/真实授权/生产；W37不重复。

## 2026-10-21：W37 独立制造／出库Owner已集成

[产品PR #381](https://github.com/GTX537/CP6/pull/381)已合入远端main`bef201e6ed821a79b5b9a63f8581c61630e0aa7a`，[Handoff PR #13](https://github.com/GTX537/CP6.AI-Handoff/pull/13)已合入默认目标，#380回归及远端采用后关闭。164项去重相关.NET成功（143项未变证据复用+21项当前PG/实际HTTP）、9项Vitest4.1.11页面及完整vue-tsc --build通过，最终适用集合0失败/跳过；一次作者审查与局部复查无剩余本窗阻断。

Handoff分支删除、DEV-2释放；自有进程停止，10处生成data逻辑回收1.073GiB。原W37-01完成最后文档PR后finish，不能预先声称自身未来释放。WorkOrder/Outbound主服务及控制器Owner窗口已完成；其他WMS/MES入口（报工/入库/盘点/看板）、无订单关联对象的正式部门绑定、持久operation/outbox及可靠财务投递、正式workflow/CRM和真实DEV/UAT仍独立，125目标/247运行不提升。表锁成本与best-effort边界明确。零新树/副本/代理，无Actions/既有环境迁移/真实tenant授权/生产。[DELIVERY](../development/evidence/WP02-03/operations-owner/DELIVERY.md)。

## 2026-10-21：W36 订单取消级联Owner已集成

[产品PR #378](https://github.com/GTX537/CP6/pull/378)已合入远端main`d8c9f549e9733ee6c76b68f5120064826e7c8bfd`，[Handoff PR #12](https://github.com/GTX537/CP6.AI-Handoff/pull/12)已合入默认目标，#377回归及远端采用后关闭。119相关.NET（含原生PG/HTTP）、18前端及类型检查通过，最终0失败/跳过；一次作者审查发现的状态/数量矛盾已修正验证。

Handoff分支删除、DEV-2释放；自有进程停止，6处生成data逻辑回收0.642GiB。原W36-01完成本次最后文档PR后finish，不能预先声称自身未来释放。取消Owner及直接依赖已完成，独立MES/WMS入口、其他读取/计算/追踪、持久operation/通知、外部workflow/CRM和真实验收仍独立；125目标/247运行不提升。表锁成本与best-effort边界明确。零新树/副本/代理，无Actions/既有环境迁移/真实tenant审批/生产。[DELIVERY](../development/evidence/WP02-03/order-cancel-owner/DELIVERY.md)。

## 2026-10-21：W36 订单取消级联Owner候选

Order.cancel已接当前原会话、独立动作与完整对象/可信部门；库存预留释放/流水/明细/制造及订单状态处于受控PG事务，子项保存点防半笔释放，最终撤权整笔拒绝，UNKNOWN不重放。保留部分结果与原精度，财务/通知在确认提交后best-effort。前端冻结意图、拒绝重复及过时响应。119相关.NET（含原生PG/HTTP）、18前端及类型检查通过；一次作者审查发现的状态/数量矛盾已修复。[DELIVERY](../development/evidence/WP02-03/order-cancel-owner/DELIVERY.md)。

表锁串行化关联写是明确成本；独立WMS/MES入口、其他读/计算/追踪、持久operation、外部workflow/CRM和真实验收仍独立。125目标/247运行不提升。原任务分支正常PR/Handoff及finish待完成；6处自有PG生成data已回收，零新树/代理，无Actions或生产部署。

## 2026-10-21：W35 普通订单写入Owner已集成

[产品PR #375](https://github.com/GTX537/CP6/pull/375)已合入远端main`dc156717d4d197fbaadd7e1a7f45678b58d013ec`，[Handoff PR #11](https://github.com/GTX537/CP6.AI-Handoff/pull/11)已合入默认目标，#374回归及远端采用后关闭。69相关.NET（含PG/HTTP）、14前端和类型检查通过，0最终未解决失败/跳过；一次作者审查无本窗阻断。

Handoff分支删除、DEV-2释放；自有进程停止，4处生成data逻辑回收0.427GiB。原W35-01完成本次最后文档PR后finish，不能预先声称自身未来释放。订单取消级联Owner、其他读取/计算/追踪入口、业务operation、外部workflow/CRM与真实验收仍未完成；125目标/247运行不提升。零新树/副本/代理，无Actions/既有环境迁移/真实tenant审批/生产。[DELIVERY](../development/evidence/WP02-03/order-write-owner/DELIVERY.md)。

## 2026-10-21：W35 普通订单写入Owner候选

Create/Update/Delete已接当前动作/字段Write/可信部门与对象/最终撤权及原子事务，保留原业务行Owner和精度，创建桥接仅在已确认提交后执行。#374原失败回归，69相关.NET（含PG/HTTP）、14前端及类型检查通过；一次作者审查无本窗阻断。[DELIVERY](../development/evidence/WP02-03/order-write-owner/DELIVERY.md)。

取消级联、其他读取/计算/追踪、持久operation、外部workflow/CRM和真实验收仍未完成；125目标/247运行不提升。原任务分支正常PR/Handoff和最终席位收尾待执行，无Actions/新树/生产部署。

## 2026-10-21：W34 普通订单读取Owner已集成

[产品PR #372](https://github.com/GTX537/CP6/pull/372)已合入远端main`a0dfe9df034f1d99b6fb60583959c678279ff0b1`，[Handoff PR #10](https://github.com/GTX537/CP6.AI-Handoff/pull/10)已合入默认目标，#371回归及远端采用后关闭。70相关.NET（含PG/HTTP）、49前端和类型检查通过，0最终未解决失败/跳过；一次作者审查无本窗阻断。

Handoff分支删除、DEV-2释放；自有进程停止，4处生成data逻辑回收0.427GiB。原W34-01完成本次最后文档PR后finish，不能预先声称自身未来释放。普通写Owner、其他读取/计算/追踪入口、业务operation、外部workflow/CRM与真实验收仍未完成；125目标/247运行不提升。零新树/副本/代理，无Actions/既有环境迁移/真实tenant审批/生产。[DELIVERY](../development/evidence/WP02-03/order-read-owner/DELIVERY.md)。

## 2026-10-21：W34 普通订单读取Owner候选

复用W33可信部门，列表／整单详情／手配号查询、CSV／报表已接独立当前动作、对象与字段用途、最终撤权；新增权限目录不自动授予角色。原生精度／子图、前端失权与同身份草稿保护保持。#371原步骤回归；70相关.NET（含PG/HTTP）、49前端和类型检查通过，一次作者审查无本窗阻断。

原DEV-1／DEV-2正常PR及同分支最后文档后finish，零新树／副本／代理；4处自有生成data回收，证据保留。普通写Owner、其他读取入口、业务operation、外部workflow／CRM及真实验收仍未完成；125目标／247运行不提升，无Actions或部署。[DELIVERY](../development/evidence/WP02-03/order-read-owner/DELIVERY.md)。

## 2026-10-20：W33 部门可信映射与PA090 Owner已集成

[产品PR #369](https://github.com/GTX537/CP6/pull/369)已合入远端main`d58b65cb16608c310ec3fb64a3185d13680760a5`，[Handoff PR #9](https://github.com/GTX537/CP6.AI-Handoff/pull/9)已合入默认目标，P0 #368回归及远端采用后关闭。101不同相关PG/HTTP/IAM、27Vitest与类型检查通过，0最终失败/跳过；一次作者审查无本窗阻断。

Handoff任务分支删除、DEV-2释放；自有服务停止，6处生成data回收0.744GiB。原W33-01分支继续本次最后文档PR后finish释放DEV-1，提交本身不预先证明未来收尾。零新树/额外收尾分支/代理。剩余其他Order CRUD/导出Owner、业务operation恢复、正式工作流/CRM及真实验收保持；125目标/247运行不提升，无Actions/既有环境迁移/真实tenant审批/生产。[报告](../development/evidence/WP02-03/order-department-owner/DELIVERY.md)。

## 2026-10-20：W33 部门可信映射与PA090 Owner候选

显式租户订单部门绑定、管理页/CAS/审计/跨Tab恢复，以及本部门/下级/custom Owner范围已接通；列表和金额计算补齐query/字段Read、防筛选泄漏与失权页面清空，#368原步骤已回归。101相关PG/HTTP/IAM、27Vitest和类型检查通过，0最终失败/跳过；一次作者审查无本窗阻断。

自有服务停止、6处data回收；原DEV-1/DEV-2待正常PR及同分支最后文档后finish。其余Order CRUD/导出、业务operation恢复、外部工作流/CRM和真实验收仍未完成；125目标/247运行不提升。零新树/代理/副本，无Actions/既有环境迁移/真实tenant审批/生产。[DELIVERY](../development/evidence/WP02-03/order-department-owner/DELIVERY.md)。

## 2026-10-20：W32 Owner单价订正与最终撤权门已集成

[产品PR #366](https://github.com/GTX537/CP6/pull/366)已合入远端main`f39351c22ac62def3a6502f43529141b4b0fc10d`，[Handoff PR #8](https://github.com/GTX537/CP6.AI-Handoff/pull/8)已合入默认分支；P0 #365经真实PG原步骤回归、实际HTTP及远端采用核对后关闭。49项后端/原生PG/实际宿主、9项前端及类型检查通过，0最终失败/跳过；一次作者审查无本窗阻断。

Handoff分支已删除、DEV-2已释放，自有PG/API停止并回收0.532GiB生成data。Main继续原W32-01分支完成本次最后文档PR，再finish释放DEV-1；该提交不预先证明自身未来收尾，实际登记/最终PR评论为生命周期回执。无额外收尾任务分支、工作树或代理。

剩余部门可信映射、其余Order操作、operation恢复、正式外部工作流、CRM Owner和真实业务验收不提升。125目标/247运行保持；无Actions、既有环境迁移或生产。[范围、证据与收尾](../development/dispatch/20261020-wave32/README.md)。

## 2026-10-20：W32 单价订正 Owner 与最终撤权门候选

实际PG写入口接入版本化Owner字段/对象/动作映射、原会话重查、持锁到提交及业务/审批意图原子保存；#365原步骤已回归。49项后端/原生PG/实际宿主、9项前端与类型检查通过，0最终失败/跳过；一次作者审查无本窗阻断。

原DEV-1/DEV-2复用、自有服务停止、5处生成data回收。正常PR/Handoff和原任务分支收尾后finish，不另开进度分支。部门映射、其他Order用途/业务恢复、外部工作流和CRM/真实业务仍未完成；125目标/247运行不提升。无Actions/既有环境迁移/生产。[DELIVERY](../development/evidence/WP02-03/order-price-correction-owner/DELIVERY.md)。

## 2026-10-20：W31 已集成并完成产品/Handoff席位收尾

字段权限可信PG来源通过[产品PR](https://github.com/GTX537/CP6/pull/363)合入，main`fc49e6d4fb0e75f00ed51913f1e998aa4b793d54`；[Handoff PR](https://github.com/GTX537/CP6.AI-Handoff/pull/7)已合入默认分支。50不同相关.NET/原生PG通过，0最终失败/跳过；一次作者审查无阻断。产品/Handoff分支已定向删除、固定席位释放，自有PG停止、生成data回收，证据保留。

最终文档W31-CLOSE复用DEV-1，其自身PR/finish以最终登记为准。Owner映射签认/实际业务接线和最终撤权门、CRM持久采用仍未完成，125目标/247运行保持。无新树/代理/副本、Actions、既有环境迁移或生产。[收尾和边界](../development/dispatch/20261020-wave31/README.md)。

## 2026-10-20：W31 字段权限可信PG来源候选

当前主体绑定、同SQL有效角色/原始字段行/持久revision、封存域和原计算器接线已实现；50不同相关.NET/原生PG测试通过，0最终失败/跳过。一次作者集中审查无本窗阻断；自有实例停止，生成data回收且失败证据保留。待正常PR/远端采用与manage finish，固定席位复用、零新树/代理/副本。具体Owner映射签认/业务接线与最终撤权门、CRM持久采用仍未完成；完整IAM/125目标/247运行不提升，无Actions/既有环境迁移/生产。[DELIVERY](../development/evidence/WP02-03/trusted-field-source/DELIVERY.md)。

## 2026-10-20：W30 已正常集成并释放产品/Handoff席位

菜单动作实际维护与目录不可变快照/启动封存已通过[PR361](https://github.com/GTX537/CP6/pull/361)合入，main`8978ea1a95694f0bcdef9b5fb8193368d3aae39d`，#360 Closed；56不同.NET/PG/HTTP、20Vitest、3Chrome和typecheck通过。[Handoff PR6](https://github.com/GTX537/CP6.AI-Handoff/pull/6)已合入默认分支。产品/Handoff任务分支均本地/远端删除，固定席位释放；自有服务停止、3处生成测试data回收且日志保留，零新树/副本/代理。

最终纯文档W30-CLOSE复用原DEV-1，其PR/finish以最终管理登记为准。完整字段真实来源/映射与Owner最终门、CRM持久采用及业务验收仍未完成；不提升125目标/247运行。无Actions、真实tenant审批、既有迁移或生产部署。[收尾及后续边界](../development/dispatch/20261020-wave30/README.md)。

## 2026-10-20：W30 菜单动作维护与授权目录候选

已接通既有功能权限页的菜单动作定义维护、版本/引用冲突与原操作跨Tab恢复；字段/范围目录改为隔离快照并由实际宿主启动封存，修复#360。56不同.NET/PG/HTTP、20Vitest、3实际Chrome夹具和typecheck通过，一次作者SPEC/QUALITY无本窗阻断。自有服务停止，三处生成测试data回收且原失败保留。待正常PR/远端采用及manage finish；零新树/代理/副本，无Actions或部署，完整IAM/Owner/真实业务不提升。[DELIVERY](../development/evidence/WP02-03/menu-action-administration/DELIVERY.md)。

## 2026-10-20：W29 完整整数身份补口已集成收尾

原角色/授权PR354/356基础上，CRM [PR95](https://github.com/GTX537/CP6.CRM/pull/95)先采用兼容消费者，Main [PR358](https://github.com/GTX537/CP6/pull/358)随后合入完整Int32角色身份v2；旧v1合同字节保留，#353 Closed。174不同Main .NET/PG、61 CRM消费/合同、14Vitest与typecheck通过，原W27跨Tab/SSO、W29页面证据按范围复用，未冒称重跑。Handoff [PR5](https://github.com/GTX537/CP6.AI-Handoff/pull/5)已合入；本地/远端三任务分支清理、固定席位释放。15个本轮隔离测试库生成data已回收、日志保留，零额外目录/clone/代理。

最终协调W29-353-CLOSE在原DEV-1仅同步此记录，正常PR及finish结果以管理登记/最终交付为准。完整IAM剩余权威范围、CRM数据库/Owner及125目标/247真实运行不提升；无Actions、真实租户审批、既有环境迁移或生产部署。[实际处置与剩余边界](../development/dispatch/20261020-wave29/README.md)。

## 2026-10-20：W29-353 完整整数身份兼容候选

固定v1身份域缺口已用独立v2合同补齐，旧v1字节与正整数身份保持。Main创建/授权/分配、SSO首次身份、版本粘性/撤销与baseline共174项不同.NET/PG通过；CRM61项实际消费/合同、14Vitest和类型检查通过。CRM PR95已先合入并清理分支、释放DEV-2；Main待正常PR/远端采用及finish。#353待集成关闭，完整IAM/CRM/Owner/真实业务状态不提升；无Actions、真实租户审批或环境部署。[DELIVERY](../development/evidence/WP02-03/integer-role-identity/DELIVERY.md)。

## 2026-10-20：第二十九轮限定公共基础已集成并收尾

角色安全生命周期与页面恢复[PR354](https://github.com/GTX537/CP6/pull/354)、封闭授权编辑/目录版本/完整资源矩阵和页面[PR356](https://github.com/GTX537/CP6/pull/356)正常合入，产品main`c762505cc8c4047bb1faf2166364bb22a2c344b8`。#351/#352/#355 Closed。角色窗61相关.NET、12Vitest、6Chrome；授权窗78不同.NET/PG/HTTP、19Vitest、4Chrome及类型检查通过，跨窗不相加。每任务一次作者SPEC/QUALITY，原失败保留；W27跨Tab认证/SSO首次批准与映射复用。

产品两分支本地/远端已清理、DEV-1释放；Handoff PR4默认目标`d3c58b92ee02356143951994222e7ccc4131d653`已回读、分支清理、DEV-2释放。零新树/代理/完整副本，自有PG/HTTP/Vite停止。最终协调W29-CLOSE在同一DEV-1正常PR/finish后记录自身释放。#353完整int角色身份跨仓版本兼容仍Open，0/负数不是成功创建；完整IAM/CRM/Owner/真实业务不提升。无Actions/真实租户审批/既有库迁移/生产。[本轮处置](../development/dispatch/20261020-wave29/README.md)。

## 2026-10-20：W29-02 封闭授权编辑候选

角色菜单/动作、数据范围、字段配置采用共同门内当前授权、角色/引用/注册域CAS和原子操作回执；实际目录writer前向迁移与普通PG基线验证通过。三个授权页及旧菜单页接入同一安全恢复入口。78不同.NET/PG/HTTP、19Vitest、4实际Chrome夹具及typecheck通过；集中作者审查无本窗阻断。#355待正常PR集成；#353固定跨仓角色身份域差异仍Open。自有进程停止，原DEV-1/DEV-2待正常finish释放；无新树/Actions/部署。完整IAM/CRM/Owner/业务未提升。[DELIVERY](../development/evidence/WP02-03/role-policy-administration/DELIVERY.md)。

## 2026-10-20：W29-01 角色生命周期与页面恢复候选

版本化角色白名单/启停、当前权限与角色/引用CAS、SSO ABA、原子回执和角色页面恢复就绪。实际用户弹窗恢复缺口同窗修复。61相关.NET/PG/HTTP、12Vitest、6实际Chrome夹具、typecheck通过；作者集中审查在本窗范围无阻断。#351/#352待正常集成；固定身份v1的完整整数域差异#353仍Open，不能宣称全域支持。自有PG/HTTP/Vite停止，DEV-1候选与DEV-2 Handoff待正常PR/finish，随后复用原DEV-1开展W29-02授权编辑。无新树/Actions/部署，完整IAM/CRM/Owner/业务状态不提升。[DELIVERY](../development/evidence/WP02-03/role-administration/DELIVERY.md)。

## 2026-10-20：第二十八轮限定公共基础已集成并收尾

用户角色/部门/经理合法引用与原子CAS（PR347）、组织版本/预览/移动/当前范围与用户组织页面恢复（PR349）已正常合入，产品main `f9dd8a444fc385ef6295c088cde59f27ffbb330b`。#346/#348 Closed。各窗77/50有效.NET、7/14Vitest、2/5真实Chrome夹具与类型检查通过/复用，跨窗重叠不相加；一次作者集中审查，原失败保留。W27跨Tab/SSO首次身份批准复用，不重复实施。

两产品分支本地/远端准确清理，DEV-1各窗已释放；Handoff PR3默认分支`0cb04802f7602d61b9baa9c8cfd8f5a25b8fabc7`已回读、分支清理、DEV-2释放。零新工作树/代理/完整副本；自有PG/HTTP/Vite停止，Actions/真实租户审批/既有库迁移/生产均未运行。最终协调文档在同一DEV-1执行W28-CLOSEOUT并由统一finish记录自身PR/释放。完整IAM角色生命周期/保留权威、CRM/Owner及真实业务不提升。[本轮交付与处置](../development/dispatch/20261020-wave28/README.md)。

## 2026-10-20：W28-02+03 组织与页面恢复候选

W28-01已通过PR347合入1593d573，#346关闭、任务分支已清理且原席位释放。组织CAS/预览/原子移动与原操作恢复、健康祖先权限/当前CRM投影/审批、组织页面与用户失权清理现合并为W28-02原分支交付；#348待正常集成。50有效.NET/PG/HTTP、14前端、5真实Chrome夹具及typecheck通过/复用，作者集中审查无阻断；自有PG/HTTP/Vite停止。DEV-1候选与DEV-2 Handoff待正常PR/finish；零新树/Actions/真实环境操作。完整IAM/CRM/业务状态不提升。[DELIVERY](../development/evidence/WP02-03/organization-administration/DELIVERY.md)。

## 2026-10-20：W28-01 用户角色与组织引用候选

BUG346：主附角色/同tenant部门经理引用、实际writer CAS、原子身份Outbox与准确操作回执、用户页面跨Tab/冲突恢复已实现。77项有效.NET、7前端、2实际Chrome夹具通过/复用，旧填充PG迁移及一次作者SPEC/QUALITY通过，原失败保留。候选等待正常PR与finish；W28-02组织树、W28-03页面/总收尾持续执行。固定DEV-1/2复用，无新树/代理/Actions或真实环境操作，完整IAM/CRM/业务未提升。[DELIVERY](../development/evidence/WP02-03/iam-user-assignments/DELIVERY.md)。

> 2026-10-20 WGOV-20261020：四仓合计额度改为2开发+1集成；统一start/status/finish、原子占用、准确PR采用证明和定向任务分支清理已实现，24项相关验证通过，另三仓规则PR已正常合并。本仓通过同一管理PR完成发布和席位释放，最终结果以[单份交付](../development/worktrees/WORKFLOW-DELIVERY.md)及实时登记为准。客户端仍需关闭新聊天Worktree开关并手动配置自动清理；原生Git/文件复制不受脚本拦截。未重开历史清理、改业务功能、操作数据库或触发Actions，业务验收状态不提升。

> 2026-10-20 历史成果实际收尾：CRM L01通过PR93集成，7模型/30浏览器及两项类型检查通过；旧PR5/6按已采用关闭。原登记198→7，另回收34个无Git源码副本及11个证据目录，必要源码/证据保留Git与单文件恢复映射。扩展盘点口径逻辑187.873→48.826GiB，D卷实测净增140.683GiB；5项盘点安全检查通过，4个微小残留因自动审批拒绝删除而保留，共2589字节。无待集成历史业务阻塞；零新树，未操作原始数据库/备份、Docker、生产或Actions，完整业务状态不提升。[收尾报告](../development/worktrees/BACKLOG-CLOSEOUT-20261020.md)。

> 2026-10-20 工作区治理：实际回收59个已保存且无本地残留的工作树，逻辑占用186.93→158.49 GiB，D卷空闲净增29.47 GiB。复用原目录，四仓合计3开发+1集成规则与登记落地。剩余保护存量逐项留因；无业务代码/验收状态/数据库/Actions变更。详见 [工作区登记](../development/worktrees/REGISTRY.md)。

## 2026-10-19：第二十七轮公共认证基础三个限定交付已集成

跨标签页认证协调与安全恢复、明确SSO首次身份批准/配置CAS、精确身份初绑/JIT与Main原子映射恢复，通过PR336/338/340正常合入；#335/#337/#339均Closed，产品main `c86ee22441b0531fad62007e4ee18d3f586910b0`。相关PG/真实HTTP、旧填充升级、故障/并发、实际Chrome页面和Native服务验证通过或复用，单份交付/任务、一次作者集中审查，原失败保留，计数不跨轮相加。

23个本轮隔离PG根停止，资源释放；唯一原工作目录/构建/NuGet/零复制前端依赖复用。无新树/代理/Actions、真实租户审批、既有数据库迁移或生产部署。剩余IAM组织/角色/引用CAS、CRM投影与Owner业务采用仍需后续窗口，完整业务与247运行NOT_RUN不提升。最终文档/Handoff/检查点核对后完成本轮goal。[完整记录](../development/dispatch/20261019-wave27/README.md)。

## 2026-10-19：W27-03 SSO首次身份映射候选

P0 #339已复现并修正：PG按原始字节精确匹配联邦身份。明确批准的初绑/JIT、当前角色/租户/邮箱、Main IdentityChange版本与Outbox/强审计/映射回执同事务；实际SQL与并发SSO不重复抢占，UNKNOWN沿原操作查询或封闭。相关PG/实际HTTP、旧填充迁移、26前端/1Chrome场景/9Native服务验证通过或复用，作者集中审查无阻断。仅隔离资源，全部已停止；无Actions、真实租户批准、既有数据库变更或生产部署。完整IAM/CRM/业务验收不提升。待正常集成及本轮总收尾。[DELIVERY](../development/evidence/WP02-03/sso-identity-admission/DELIVERY.md)。

## 2026-10-19：W27-02 SSO首次身份准入政策候选

#337修正：原配置加入当前原身份/权限、版本CAS、明确邮箱权威批准及角色范围；强审计和不可变原操作回执同PG事务，未知结果仅原操作查询/关闭，密钥不进操作日志或本地存储。9前端/2真实Chrome夹具场景、41项.NET主验证及15/4项局部复查通过/复用，作者集中审查无阻断。原目录/缓存复用、自有进程已停止，无Actions或真实租户批准；W27-03初绑/JIT及总收尾继续，完整IAM/CRM/真实业务未提升。[DELIVERY](../development/evidence/WP02-03/sso-admission-policy/DELIVERY.md)。

## 2026-10-19：W27-01浏览器跨标签页认证恢复候选

#335修正：原能力先存IndexedDB、Web Locks/共享epoch、准确profile/status/close、退出及身份切换清旧数据，业务写不重放，2FA刷新不重建secret。必要PG屏障/准确轮转映射及关闭审计同事务；55前端/6真实Chrome夹具场景通过，68项.NET主验证及7项最终边界通过/复用，作者集中审查无剩余阻断。W27-02/03及收尾仍待执行，完整IAM/CRM/真实业务未提升。[DELIVERY](../development/evidence/WP02-03/authentication-browser-recovery/DELIVERY.md)。

## 2026-10-19：第二十七轮浏览器恢复与SSO准入持续目标

单COORD依次执行前端跨Tab安全恢复、SSO明确准入批准/配置CAS、初绑/JIT五分支与同PG映射回执/身份变化/强审计。预计代理实际4–7小时、故障余量10小时、24小时检查窗口；三窗及收尾全部完成再结束goal。原目录/缓存复用，无新代理/工作树/Actions；不自动批准真实tenant，不提升完整IAM/CRM/Owner/业务。[计划](../development/dispatch/20261019-wave27/README.md)。

## 2026-10-19：第二十六轮认证公共基础三个限定交付已集成

政策/原proof与PG共同门、Web/Native密码/SSO/2FA交互完成、原会话/当前政策/共享设备及精确失败恢复通过PR328/330/332正常集成；#327/#329/#331均Closed。产品main `0b65bb396c1704c41a5d2ddc8740aaff06edaab2`。各窗口187/101/153有效通过（重叠不相加），最新153含106原生PG；一次作者SPEC/QUALITY/任务，旧失败保留。

22次自有PG停止，资源释放，原目录/构建/NuGet/零复制前端依赖复用，零新工作树/代理/Actions或既有库/部署操作。前向迁移与重新登录尚未应用既有环境。完整FE跨Tab恢复/SSO新身份批准/剩余IAM引用CAS/CRM与Owner/真实业务仍未完成，247运行NOT_RUN不提升。最终文档/Handoff/检查点核对后关闭本轮goal。[本轮完整记录](../development/dispatch/20261019-wave26/README.md)。

## 2026-10-19：W26-03原会话、共享设备及准确完成恢复候选

#331修正；原认证/政策/SSO/因子证据贯穿refresh/access与合法模拟绑定，实际设备writer版本/原12小时窗口，精确generation关闭、持久pending和实际formatter缓冲输出，原交互秘密绑定的安全查询/关闭。153有效通过（106PG），一次作者审查及局部复查通过。完整FE跨Tab/未批准SSO新身份/CRM/Owner/真实业务未提升；正常合并后W26总收尾，goal仍active。[DELIVERY](../development/evidence/WP02-03/authentication-session-continuity/DELIVERY.md)。

## 2026-10-19：W26-02交互认证完成候选

#329旧pending/跨设备问题修正；Web/native密码、SSO、2FA交互共同PG门，原proof/浏览器设备绑定、单次TOTP/lease/准备revision及专属enroll转换，session注册/必要审计同提交。101有效相关通过（63PG）、type-check通过，一次作者审查通过。SSO缺明确新身份准入策略只拒新绑定/JIT，不虚构批准。W26-03 refresh/access当前政策、设备FullAuth及精确失败恢复继续；完整IAM/CRM/业务未提升，goal active。[DELIVERY](../development/evidence/WP02-03/authentication-entry-completion/DELIVERY.md)。

## 2026-10-19：W26-01认证政策与PG完成门候选

#327缺失/非法政策已修正；版本化原proof/当前政策与PG共同根门，实际user/tenant/SSO DML修订覆盖，v3含数据库凭据修订防改回旧值。187相关通过（99原生PG/HTTP），一次作者审查通过。前向迁移仅自有旧数据隔离库；采用需迁移并重新登录。全入口采用/refresh/FullAuth为接续W26-02/03，本goal未完成，完整IAM/CRM/业务未提升。[DELIVERY](../development/evidence/WP02-03/authentication-policy-gate/DELIVERY.md)。

## 2026-10-19：第二十六轮认证公共基础持续目标

COORD本聊天独立连续推进政策/原proof/PG共同门、Web/Native全部相关认证入口、refresh与共享设备FullAuth；三项和收尾均完成才结束goal。预计代理实际5–8小时，修正余量约10小时，24小时检查点；复用原目录/PG/构建缓存。完整IAM/CRM/Owner/真实业务未提升。[计划](../development/dispatch/20261019-wave26/README.md)。

## 2026-10-19：第二十五轮三个限定交付全部集成

租户管理当前actor最终门、模拟当前真身/目标与审计Cookie边界、access/refresh原认证状态与精确完成隔离已通过PR320/322/324正常集成；#319/#321/#323均Closed。产品main `f978d8de9ae90a17bd5655e80662afe0b7aca0bd`，280项去重有效相关通过（113原生PG/HTTP）。逐任务一次作者SPEC/QUALITY，无独立代理；旧失败保留，组合采用实际Program检查与未变证据复用。

16次自有PG停止，原目录/缓存复用，无新树/代理/Actions或既有库/部署操作。新PG审计/认证迁移采用前需安装，认证v2需重新登录；现有环境未变。完整认证政策/全writer门、统一会话、CRM/Owner与真实业务仍未完成，247运行NOT_RUN不提升。最终文档/Handoff/检查点核对后关闭外部goal。[本轮记录](../development/dispatch/20261019-wave25/README.md)。

## 2026-10-19：W25-03原认证状态候选完成

W25-01/02已正常集成，#319/#321关闭。W25-03 P0 #323补PG原认证状态持久化、访问消费及refresh/完成阶段当前校验，普通失败精确隔离replacement，已验证完整Program Web/native/2FA/restricted/模拟合法路径。167有效相关通过（53原生）；一次作者SPEC/QUALITY无阻断。新前向迁移只安装自有隔离库，采用时须迁移并重新登录；完整认证政策/Owner/真实业务仍未完成。正常合并及最终收尾前长目标持续。[DELIVERY](../development/evidence/WP02-03/session-authentication-current-state/DELIVERY.md)。

## 2026-10-19：W25-01集成，W25-02模拟最终门候选

W25-01经PR320合入远端main1974bcb4，#319关闭。W25-02 P0 #321修正当前真身/目标与原认证状态、必要审计/提交后Cookie及UNKNOWN关闭；新增可空PG jsonb审计上下文，仅在自有隔离已填库升级验证。83有效相关通过（49原生），作者集中审查无阻断。待正常PR/远端核对，继续W25-03及总收尾；完整IAM/模拟FE/业务不提升。[DELIVERY](../development/evidence/WP02-03/impersonation-final-gate/DELIVERY.md)。

## 2026-10-19：W25-01租户管理最终门候选

P0 #319已在既有PG管理员根内重读并锁定当前actor，四种租户写入及必要审计共同提交。79相关通过（60原生PG/HTTP），作者集中审查无阻断；待正常PR/远端核对。W25-02/03仍待执行，长目标active，完整业务不提升。[DELIVERY](../development/evidence/WP02-04/current-actor-final-gate/DELIVERY.md)。

## 2026-10-19：第二十五轮长持续目标登记

单总控依次完成租户管理当前actor事务、模拟登录进入退出边界、访问/refresh原认证状态绑定；全部三项及收尾完成才关闭goal。预计代理5–9小时、24小时检查点2026-10-20T08:54:40Z。复用原目录与PG/构建缓存，无新代理/Actions/既有库操作。完整IAM/CRM/Owner/业务不提升。[计划](../development/dispatch/20261019-wave25/README.md)。

## 2026-10-19：第二十四轮限定目标已集成

P0 #315平台授撤当前actor最终权限门和主体/tenant绑定已通过PR316合入远端main `5a44d92eeffad18ee209a8c6a28c5707295ce6ac`，原步骤与远端核对完成、IssueClosed。66项有效相关验证（38原生PG/HTTP），一次作者SPEC/QUALITY无阻断。四个自有PG停止，复用原目录，零新树/代理/Actions，D余约44.01GiB。

完整认证版本/会话/政策撤权、其他平台写入口最终门、CRM/Owner与业务仍未完成；247运行NOT_RUN不提升。最终文档远端/Handoff/检查点后关闭外部goal。[本轮记录](../development/dispatch/20261019-wave24/README.md)。

## 2026-10-19：W24-01候选可合并

P0 #315平台授撤最终actor资格检查及主体/tenant绑定已实现，66项有效通过（38原生PG/HTTP），一次作者SPEC/QUALITY无阻断。待正常PR/远端核对，持续目标active；完整认证策略、CRM/Owner和业务不提升。[DELIVERY](../development/evidence/WP02-03/platform-admin-current-actor/DELIVERY.md)。

## 2026-10-19：第二十四轮持续目标登记

当前COORD单独补平台授撤当前调用人最终权限门；预计代理实际执行1.5–3小时，24小时检查点2026-10-20T08:17:12Z。复用目录/PG/缓存，相关验证加一次作者审查。未新增代理/Actions/既有库操作；完整IAM/CRM/业务不提升。见[计划](../development/dispatch/20261019-wave24/README.md)。

## 2026-10-19：第二十三轮有限目标完成

两项P0已正常合并/远端核对/关闭：#308管理员末名与授撤审计共同事务（PR309），#311普通创建专用白名单及服务端身份（PR312）。77项去重有效通过，含63原生PG/HTTP；两项各一次作者SPEC/QUALITY，失败来源保留。两个实施窗口关闭，11自有PG停止，零新worktree/代理/Actions，D余约44.6GiB，原目录/缓存复用。最终文档远端核对后关闭外部goal。

完整公共基础/全IAM/CRM/Owner最终门、既有环境新迁移安装、真实DEV/业务/生产仍未完成。下一轮优先实际当前授权/撤权与完整写者采用，需明确限定窗口，不自动提升总任务。[本轮记录](../development/dispatch/20261019-wave23/README.md)。

## 2026-10-19：W23-02候选通过，待集成

P0 #311用户创建实体绑定已改专用白名单，服务端确定身份/租户与安全初态；17项相关验证通过（11原生PG/HTTP、5密码单测、1生成schema），作者集中审查无阻断。候选待正常合并/远端核对；全IAM/业务不提升。无新迁移、Actions或既有库操作。[DELIVERY](../development/evidence/WP02-03/user-create-whitelist/DELIVERY.md)。

## 2026-10-19：W23-01集成，继续创建白名单

BUG308经PR309合入远端main并关闭，65项有效验证、Handoff已核对。当前单总控在同一24小时目标中登记W23-02，按已接受S5补普通用户创建白名单及服务端身份字段；尚未运行该窗口验证。继续复用目录，完整IAM/业务与既有环境安装未提升。[计划](../development/dispatch/20261019-wave23/README.md)。

## 2026-10-19：W23-01候选通过，待集成

平台管理员末名保护与授撤审计原子保存已实现，65项去重有效通过（52原生PG/HTTP、13既有单测）。新前向迁移仅自有隔离库执行；作者一次SPEC/QUALITY无阻断。#308待正常合并及远端核对，持续目标active；全IAM/业务及既有环境安装未提升。[DELIVERY](../development/evidence/WP02-03/platform-admin-transaction/DELIVERY.md)。

## 2026-10-19：第二十三轮持续目标登记

当前COORD单独推进平台管理员并发/末名保护、PG事务门与必要审计、定向验证及正常集成收尾。24小时检查点2026-10-20T07:24:41Z。W22不重做，复用原目录，无新树/代理/Actions/既有库操作。完整WP02/业务不提升。见[计划](../development/dispatch/20261019-wave23/README.md)。

## 2026-10-19：第二十二轮限定范围收尾

Main身份写者矩阵及BUG304修正已通过[正常PR](https://github.com/GTX537/CP6/pull/305)集成，远端main `0b6da3853f3b09a4397ca9f2c6da460b701ba2fb`核对，Issue304关闭。21项有效原生PG证据（新写者10/既有一致性9/真实服务至普通PG HTTP采集2），一次作者SPEC/QUALITY；失败/跳过原记录保留。Handoff `33d439b768e3af091a2a24a7094e41e30b8cd3a7`已回读。

W22窗口CLOSED，无活动资源；6自有PG和HTTP停止，新增树/代理/Actions0，根7项资料保留，D盘余45.75GiB。仅本次文档收尾远端核对后完成持续目标。完整IAM/CRM授权发布/Owner最终写门、普通写账号与既有升级、真实业务保持未完成。[本轮汇总](../development/dispatch/20261019-wave22/README.md)。

## 2026-10-19：W22-01候选通过，待集成

实际写者盘点与P0 #304菜单并发fanout修正完成，21项去重原生PG验证有效通过（本轮写者10、受影响既有9、实际服务/普通PG HTTP采集2）。原失败/跳过保留且不算通过；作者集中SPEC/QUALITY无阻断。Issue待正常合并及远端核对后关闭，持续目标active。5自有PG停止，无新树/代理/Actions/既有库操作。完整WP02/全IAM/CRM发布/Owner最终门及真实业务不提升。[单份DELIVERY](../development/evidence/WP02-05/identity-writer-coverage/DELIVERY.md)。

## 2026-10-19：第二十二轮持续目标登记

当前COORD单独执行Main IAM写者盘点、捕获缺口修正、实际服务PG/目录实证和正常集成收尾；预计13～20小时，24小时检查点2026-10-20T05:41:46Z。复用原目录与缓存，无新树/代理/Actions。W21已完成不重复；全WP02/CRM发布/Owner最终写门及真实业务验收不提升。详见[本轮计划](../development/dispatch/20261019-wave22/README.md)。

## 2026-10-19：第二十一轮四个有限窗口全部合入并关闭

完整基线核验、版本化普通PG原字节读取、有界HTTP采集、真实PG串联及BUG300修正分别经PR297/298/299/301合入。最终实施main `51d6d771b753787daf74b243cc53c183987c90a4`及产品/测试blob核对，Handoff `3b4e57223fe8809ed8efcffb48c05c222883b2de`回读；Issue300修正后关闭，当前活动Issue 0项。四窗口CLOSED、windows_closed=true、无资源占用；[本轮汇总](../development/dispatch/20261019-wave21/README.md)列明证据及范围。

8个自有PG/HTTP停止，新增树/代理/Actions0，根7项资料保留。本次仅文档/台账收尾，远端核对后完成有限持续目标；完整公共基础/全IAM/CRM采用/Owner最终写门、既有环境切换和真实DEV/业务/生产未提升。

## 2026-10-19：W21-04真实矩阵与BUG-300候选通过

实际产品collector/JWT/普通PG矩阵发现[P0 #300](https://github.com/GTX537/CP6/issues/300)：EF提前同步COUNT使取消与同一SQL观察失效。已改为关联服务器子查询，4项专项复查通过，5项未变成功复用，最终9项有效通过；一次作者SPEC/QUALITY无阻断。[单份DELIVERY](../development/evidence/WP02-05/baseline-integration/DELIVERY.md)保留三次失败及早期报告勘误。

候选待正常PR和远端核对后关闭Issue；随后全轮共享状态/Handoff收尾。4个本窗口自有PG及HTTP已停止，无新树/代理/Actions/既有库操作或部署；完整公共基础/CRM/Owner最终门及真实业务未提升，持续目标active。

## 2026-10-19：W21-03已合入，W21-04开工

候选 `e81b04ade25f410bbd7346379fae37d6a1d1ce99` 经[正常PR](https://github.com/GTX537/CP6/pull/299)合入远端main `406b9cfbf16dcc82252d5d3505e7a18a47900769`，范围文件/祖先关系核对，Handoff `43cffcd3dbbe70c7b164c8c47cad4520d1da7188`回读。[单份DELIVERY](../development/evidence/WP02-05/baseline-collector/DELIVERY.md)。有界实际HTTP采集组件和相关验证已交付，准确结果见唯一DELIVERY；作者SPEC/QUALITY无阻断。

同目录从最新main开始W21-04 真实PG/HTTP完整基线验证集成；当前新行为NOT_RUN。无新树/代理/Actions/既有库操作或部署，持续目标active；完整公共基础和真实业务未提升。

## 2026-10-19：W21-03完整HTTP采集候选通过

产品采集器固定可信来源、禁止重定向、有界读取完整首尾目录及准确原快照，全部一致后才返回不可变基线。35项去重协议/HTTP/取消测试通过，含公开产品构造器真实302拒绝；准确请求匹配及token等待取消补齐后局部6项复查，一次作者SPEC/QUALITY无阻断。[单份DELIVERY](../development/evidence/WP02-05/baseline-collector/DELIVERY.md)。

未启动PG，测试HTTP已停止；待正常集成后进入真实PG宿主采集矩阵。无新树/代理/Actions/既有库操作或部署，持续目标active；完整公共基础/CRM授权采用/Owner最终门和真实业务未提升。

## 2026-10-19：W21-02已合入，W21-03开工

候选 `845d574ca54a0a8c062d168217b37f9976907adc` 经[正常PR](https://github.com/GTX537/CP6/pull/298)合入远端main `18d36ced499f4846ea6e50bda5e200182b5cbb06`，范围文件/祖先关系核对，Handoff `95516e0df1602040292ed94f75483ed63cf359ce`回读。[单份DELIVERY](../development/evidence/WP02-05/baseline-raw-http/DELIVERY.md)。3项原生PG/HTTP及1项无宿主检查通过，4个自有PG停止，作者SPEC/QUALITY无阻断。

同目录从最新main开始W21-03 有界实际HTTP完整基线采集；当前新行为NOT_RUN。无新树/代理/Actions/既有库操作或部署，持续目标active；完整公共基础和真实业务未提升。

## 2026-10-19：W21-02版本化原字节读取候选通过

实际普通PG宿主新增目录总数/来源/边界页及原字节快照，保持旧接口；3项原生HTTP和1项无宿主检查通过，游标绑定来源摘要后局部2项复查通过。原字节空白/转义、hash故障、A/B隔离与no-store覆盖，Owner请求查询0，一次作者SPEC/QUALITY无阻断。[单份DELIVERY](../development/evidence/WP02-05/baseline-raw-http/DELIVERY.md)。

四个自有PG及HTTP已停止，待正常PR/远端核对后推进实际HTTP采集。无新树/代理/Actions/既有库操作或部署，持续目标active；完整公共基础/CRM授权采用/Owner最终门及真实业务未提升。

## 2026-10-19：W21-01已合入，W21-02真实读取开工

候选 `5dec6d11031ffceeed01506211721760f5c6dc0e` 经[PR297](https://github.com/GTX537/CP6/pull/297)合入远端main `3209824ef71dc56bc251d92b75ab40463833c970`，范围文件/祖先关系核对，Handoff `692c7c8b95832074456addd66eb902b936299b95`回读。[单份DELIVERY](../development/evidence/WP02-05/baseline-contract/DELIVERY.md)记录25项基线及38项现有合同通过。

同目录从最新main开始W21-02版本化普通PG/HTTP目录及原字节快照；新行为NOT_RUN。无新树/代理/Actions/既有库操作或部署，持续目标active；完整公共基础和真实业务状态未提升。

## 2026-10-19：W21-01完整基线核验候选通过

已实现来源/原字节/版本/摘要/首尾全集核验和不可变SOURCE_SNAPSHOT结果，失败或取消无部分发布。25项基线行为及38项现有事件合同通过；空键问题局部修正并复查，一次作者SPEC/QUALITY无阻断。[单份DELIVERY](../development/evidence/WP02-05/baseline-contract/DELIVERY.md)保留真实失败与证据复用。

待正常PR/远端核对后推进版本化普通PG/HTTP读取。未启动PG/HTTP，无新树/代理/Actions/既有库操作或部署；完整公共基础、CRM授权采用、Owner最终门及真实业务未提升，持续目标active。

## 2026-10-19：第二十一轮持续目标与四窗口登记

已核对W20最终main `f552995d`、Handoff `30439c2`及四窗口关闭。本轮[24小时计划](../development/dispatch/20261019-wave21/README.md)为完整基线合同、原字节普通PG/HTTP读取、有界首尾完整目录采集、验证集成收尾。持续目标active，检查点UTC2026-10-20 04:15:29（纽约00:15:29）。

W21-01有限窗口已登记，规划合入后真实START；当前无本轮产品通过结果。单对话、PG唯一、同目录和证据复用，精简验证/作者集中审查；无新代理/树/Actions/既有库操作或部署。完整CRM授权采用、Owner最终写门及真实业务状态未提升。

## 2026-10-19：第二十轮四个有限窗口全部合入并关闭

普通PG当前授权v2、显式安装/inspect与漂移拒绝、实际配置/JWT可信目录宿主、受影响HTTP/PG矩阵分别经PR291/292/293/294合入；最新实施main `fc8273dc89e7e8f691bfbded8a6113123c2d76fa`，Handoff `cd419653ae95166ad78368f5ca7354a6af0d6bbd` 回读。四窗口CLOSED且无资源占用，windows_closed汇总同步；[本轮汇总](../development/dispatch/20261018-wave20/README.md)列出各项证据和剩余范围。

12个自有PG和HTTP停止，新增worktree/代理/Actions为0，根7项资料保留。此文档收尾只校验台账/diff/链接，远端核对后完成本轮有限持续目标；完整WP02/公共基础、冻结目录/CRM、Owner最终写授权门、既有库切换及真实DEV/业务/生产未提升。

## 2026-10-19：W20-04实际目录矩阵候选通过

实际配置/JWT/source/authority/普通PG授权和目录HTTP完成7项定向矩阵；含撤销/停用、故障漂移、游标generation、冻结配置、等待时过期及HTTP取消/lease释放。4项有效成功复用，测试ACL恢复顺序修正后3项局部复查通过，产品实现不变；原跳过/失败保留。[单份DELIVERY](../development/evidence/WP02-05/directory-configured-matrix/DELIVERY.md)。

两个自有PG停止，一次作者集中审查无阻断；待正常集成、远端核对及全轮状态收尾，持续目标active。无新树/代理/Actions/既有库操作或部署，完整WP02/公共基础和真实业务未提升。

## 2026-10-18：W20-03已合入，W20-04矩阵开工

候选 `68785b04fec69a476fe8ddd838835b10e7d38f3e` 经[PR293](https://github.com/GTX537/CP6/pull/293)合入远端main `08820bd53067cac140adc755a2c3373ee9e769ab`，范围文件/祖先关系已核对；18配置及1条原生PG/HTTP产品宿主链路通过，Owner请求查询0，自有实例停止。[单份DELIVERY](../development/evidence/WP02-05/directory-host/DELIVERY.md)，Handoff `64f193a3961d281b0da056b5f4d8f36a89a628cc` 回读。

W20-04复用原目录，验证当前授权/故障/合同/冻结配置/时效取消并收尾；当前矩阵NOT_RUN。持续目标active，完整WP02/目录CRM/Owner最终门与真实业务生产不提升。无新树/代理/Actions/既有库操作或部署。

## 2026-10-18：W20-03实际目录HTTP宿主候选通过

严格opt-in配置冻结安装身份、普通LOGIN及reader注册；真实JWT经产品verifier/authority绑定可信scope，当前授权及目录读取均走普通PG连接，失败无Owner回退。18配置检查与1条真实PG/HTTP A→B→A链路通过，Owner请求查询0，实例已停止。[单份DELIVERY](../development/evidence/WP02-05/directory-host/DELIVERY.md)保留RED及范围。

一次作者SPEC/QUALITY无阻断，候选待正常PR/远端核对，W20-04补矩阵及收尾。持续目标active；完整任务/真实业务未提升，无新树/代理/Actions/既有库操作或部署。

## 2026-10-18：W20-02已合入，W20-03实际HTTP宿主开工

候选 `8a39779211b5347cd83669e09266a289832bd476` 经[PR292](https://github.com/GTX537/CP6/pull/292)合入远端main `73ddf0b9374e5f09140be4280895d150749f87c5`，范围文件/祖先关系已核对；两条PG/CLI生命周期与四个旧CLI入口smoke通过，五个自有实例停止。[单份DELIVERY](../development/evidence/WP02-02/directory-installation/DELIVERY.md)保留证据，Handoff `c155fa0a87ff9e139ebacf8504b33e4c5f427f20` 发布并回读。

W20-03复用原目录，实施严格opt-in配置、实际JWT之后可信scope/authority、普通身份当前授权与目录读取。当前验证NOT_RUN；W20-04补矩阵收尾。完整WP02/CRM/Owner最终门与真实业务生产不提升；无新工作树/代理/Actions/既有库操作，持续目标active。

## 2026-10-18：W20-02显式安装/检查候选通过

目录v2具备显式fresh安装、只读inspect、实际database/OID/cluster身份和结构/权限/映射漂移检查；普通reader可绑定冻结回执，失败无Owner回退。真实CLI与原生PG两条完整生命周期通过（含取消rollback、漂移拒绝、提交回执丢失UNKNOWN后inspect），4个旧CLI入口仅做无数据库输入冒烟。[单份DELIVERY](../development/evidence/WP02-02/directory-installation/DELIVERY.md)保留原失败及范围。

一次作者SPEC/QUALITY无剩余阻断，5个自有PG停止；候选待正常PR/远端核对，随后W20-03实际HTTP配置。无新工作树/代理/Actions/既有库操作或部署，完整任务与真实业务不提升，持续目标active。

## 2026-10-18：W20-01已合入，W20-02安装检查开工

候选 `5541e086d52dabfcffced1835729c6811e8aa81b` 经[PR291](https://github.com/GTX537/CP6/pull/291)合入远端main `1b03afcb8ba9b86ea6a9ccc8d45ad5392ba71364`，范围文件/祖先关系核对完成；7项PG去重通过、4个自有实例停止，[单份DELIVERY](../development/evidence/WP02-02/directory-authorization/DELIVERY.md)保留证据。Handoff `65961b0f8bbf1685fbac07fc93817863d240ad44` 已发布并回读。

W20-02复用原目录实现显式安装、只读inspect、目标身份/结构漂移及UNKNOWN恢复边界，当前验证NOT_RUN。实际HTTP采用仍待后续。无新工作树/代理/Actions/既有库操作或部署，持续目标active；完整任务与业务状态不提升。

## 2026-10-18：W20-01目录当前授权最小身份候选通过

独立v2 profile为Sys_Tenants/ServiceToken开放必要列并应用可信LOGIN映射RLS；普通身份当前资格/令牌检查和原目录读取均有原生PG证据。7项去重通过、0剩余失败/跳过；普通control写入、触发器、回滚和v1兼容通过，一次作者SPEC/QUALITY无阻断。[单份DELIVERY](../development/evidence/WP02-02/directory-authorization/DELIVERY.md)保留原RED与复用来源。

候选待正常PR和远端核对，随后进入安装/inspect窗口；当前HTTP宿主尚未切换。四个自有PG停止，无新工作树/代理/Actions/既有库或部署。完整公共基础/CRM/Owner最终门/真实业务不提升，持续目标active。

## 2026-10-18：第二十轮持续目标与四窗口登记

已核对第十九轮最终main `d70d40ef` / Handoff `dca164c`，旧四窗口Closed。本轮单总控继续[24小时计划](../development/dispatch/20261018-wave20/README.md)：普通PostgreSQL身份当前授权、显式fresh安装/inspect/漂移、实际HTTP宿主、受影响串联与集成收尾。持续目标active，检查点America/New_York 2026-10-19 22:30:42。

W20-01有限窗口已登记，规划合入后真实START；当前没有本轮产品通过结果。只复用原目录/缓存，PG唯一、精简验证与一次作者集中审查；无新代理/工作树、Actions、既有库修改或部署。完整公共基础/CRM/Owner最终门/真实业务仍未完成，下方旧时点记录保留。

## 2026-10-18：第十九轮四个有限窗口全部合入并关闭

目录一致性(#284 Closed)、普通账号三表隔离、可信DTO读取、真实HTTP/PG串联分别经PR285/286/287/288合入，最新实施main `279248a25ab757a134968dea872e373820b16196`；Handoff `f5f23c3c2af2f4fef336c1c3cec67a33e23c885b` 回读。每项相关验证及一次作者集中审查闭环，未重复未变成功范围。[本轮汇总](../development/dispatch/20261018-wave19/README.md)列出独立证据和下一候选。

17个自有PG及HTTP停止；新增worktree/代理/Actions为0，根7项用户资料保留。此文档收尾不启动产品测试，远端核对后完成外部持续目标；完整WP02/公共基础、生产宿主/环境切换、CRM/冻结目录/Owner最终门、真实DEV及业务生产未提升。

## 2026-10-18：W19-04 HTTP/PG串联候选已验证

真实JwtBearer/原控制器与当前授权SQL/fixture可信上下文/普通账号目录链路4项场景去重通过，0剩余失败或跳过。覆盖A/B、当前撤销/租户停用、游标409、输入400、读权限撤销及not-ready503；HTTP no-store和错误体有效。原测试预期错误按锁定SDK源码修正，无产品改动。[单份DELIVERY](../development/evidence/WP02-05/directory-http-integration/DELIVERY.md)明确授权查询fixture_owner、目录directory_a/b；一次集中作者审查无阻断。

当前待正常PR/远端main核对及本轮四窗口收尾。三个自有PG及HTTP宿主停止，继续复用原工作目录/输出，无Actions/既有库/部署/代理。完整公共基础、生产host配置与全Main切换、CRM/冻结目录/Owner最终门、真实DEV和业务生产均未提升；持续目标active。

## 2026-10-18：W19-03已合入，W19-04 HTTP串联开工

候选 `90ea548999c0b82f12a00b94a2e0df3941cd6bcf` 经[PR287](https://github.com/GTX537/CP6/pull/287)合入远端main `525b16052f1c8edbe39d0e53eea69720d0de54a8`，16份范围文件及祖先关系已核对。31项去重验证与作者集中审查见[单份DELIVERY](../development/evidence/WP02-05/trusted-directory-reader/DELIVERY.md)，四个PG已停止。Handoff `f72b56518405ea9b21dc0445e507d2b387879fdd` 已发布并远端回读。

W19-04复用原目录，在隔离宿主串联真实JWT/数据库授权/可信context/普通账号目录，分别记录控制授权查询与普通目录身份；当前验证NOT_RUN。默认Main不切换，无新工作树/代理/Actions/既有库/部署；完整WP02和业务生产不提升，持续目标active。

## 2026-10-18：W19-03可信普通账号目录reader候选已验证

可信连接适配器/DTO接口与单语句PG分页已完成；实际普通A/B连接复用、映射撤销、取消释放、catalog变更丢弃旧结果、无Owner回退及调用者事务均有实证。最终31项去重通过（14原生PG、17配置），无剩余失败/跳过；原实现/编排失败与跳过尝试保留于[单份DELIVERY](../development/evidence/WP02-05/trusted-directory-reader/DELIVERY.md)。一次集中作者SPEC/QUALITY审查无阻断。

当前待正常PR与远端main核对，下一项W19-04实际HTTP串联及本轮收尾。四个自有PG停止，无新增工作树/代理/Actions/既有库/部署。完整WP02、全Main切换、CRM/冻结目录/Owner最终门及业务生产不提升，持续目标active。

## 2026-10-18：W19-02已合入，W19-03可信reader开工

候选 `5f62016a855df254641242117e6dbe0ec7f5ba77` 经[PR286](https://github.com/GTX537/CP6/pull/286)合入远端main `c2b808a1e081b20d537a665cef387b90c6b4e164`，范围文件及祖先关系已核对；五项去重PG通过，六个自有PG停止，[单份DELIVERY](../development/evidence/WP02-02/identity-directory-isolation/DELIVERY.md)保留原始证据。Handoff `f201d63ac3f4423c91be83a9b702a255ce9503fa` 已发布并回读。

W19-03复用原目录，接入可信连接管理器、普通账号与DTO-only目录读取，PostgreSQL分页在单语句内保持一致性。当前验证NOT_RUN；默认Main环境不自动切换，完整WP02/CRM/冻结目录/Owner最终门及业务生产不提升。无新工作树/代理/Actions，持续目标active。

## 2026-10-18：W19-02普通账号目录只读隔离候选已验证

实际Bootstrap/Snapshot/Generation三表的RLS及最小权限组件已实现。普通A/B账号仅可读本租户，写入/旁路表/升级/伪造上下文均拒绝；普通control继承身份在FORCE RLS下仍能写入并保留版本/代际触发器。原生PG有效5项去重通过，异常前置/安装回滚有实证；一次集中作者SPEC/QUALITY无剩余阻断，原失败全部保留于[单份DELIVERY](../development/evidence/WP02-02/identity-directory-isolation/DELIVERY.md)。

当前待正常PR/远端main核对，随后W19-03接可信连接实际读取。六个自有PG停止，无新worktree/代理/Actions/既有库或部署。完整WP02、全Main切换/冻结目录/CRM/Owner最终门和真实业务生产未提升；持续目标active。

## 2026-10-18：W19-01已合入，W19-02普通角色隔离开工

候选 `69fc23c0c4c81411058962861a7376bc775f06e2` 经[PR285](https://github.com/GTX537/CP6/pull/285)正常合入远端main `85f4776e36f19db16f5c0077f72906d8d3d31441`，10份范围文件/祖先关系已核对；#284关闭。11项PG与集中作者审查按[单份DELIVERY](../development/evidence/WP02-05/directory-consistency/DELIVERY.md)保留。Handoff `a2a66b333bf48a9b519bad03461a9a9fd6cc0036` 已发布并回读，四个自有PG停止。

W19-02沿原目录从最新main真实START，仅三张身份目录表的普通账号只读RLS/有效ACL、可信login映射及对应PG验证。当前尚无本窗口通过声明；主线/现有数据库不自动切换，完整WP02/冻结目录/CRM/Owner最终门和真实业务生产不提升。无新worktree/代理/Actions，持续目标active。

## 2026-10-18：W19-01目录就绪一致性候选已验证

P0[#284](https://github.com/GTX537/CP6/issues/284)两个实际并发失败已修正：PG单条查询同时读取就绪与内容；bootstrap锁后刷新真实marker及RowVersion。原生PG相关11项去重通过、0失败/跳过，含分页代际切换、取消/事务、Outbox回滚和双等待者重建。单份[DELIVERY](../development/evidence/WP02-05/directory-consistency/DELIVERY.md)含红绿来源与一次集中SPEC/QUALITY作者自审，无剩余阻断。

正常PR/远端main核对尚待执行，#284保持Open。四个自有PG已停止，原目录复用，无新worktree/子代理/Actions或既有DB操作。后续W19-02普通角色只读RLS继续串行，持续目标active；完整WP02、冻结目录协议/CRM采用/Owner最终门及真实业务/生产不提升。

## 2026-10-18：第十九轮持续目标已建立，单总控串行

已核对第十八轮最终main `4233a204002a57da5fb9355a47c10a4f8d45bbd7` 与Handoff `decade3db5ed8db54f41d90b216f438e6012cae0`。用户明确要求下一24小时仍由本对话独自推进，已建立持续目标和[四窗口计划](../development/dispatch/20261018-wave19/README.md)：目录/就绪一致性、普通数据库账号只读隔离、可信连接读取适配、受影响PG/HTTP串联与逐项集成。检查点为America/New_York 2026-10-19 19:49:21；预计18～24小时含修正，未完成不能因到时提升状态。

W19-01有限窗口已登记，规划合入后从最新main真实START；其余按前置串行接续。当前本轮尚无产品通过结果。复用原COORD目录，每任务独立分支，新增/删除worktree及子代理均0。沿PostgreSQL、精简本地验证和一次集中作者自审，无Actions/既有库操作/生产部署。

完整WP02、冻结完整目录协议、全Main普通账号切换、CRM消费与Owner提交前撤权门、真实DEV/业务/生产仍未完成；任务/目标/运行验收状态未提升。下方旧“下一轮”及进行中内容保留历史时点。

## 2026-10-18：第十八轮四项公共基础有限交付完成

当前产品/验收远端main `f9a1496ce54c46cbbf6197cf8afd9675ae0cba63` 包含本轮PR276/278/280/281。当前授权一致读、租户管理强审计、身份快照/版本/Outbox及PG/HTTP串联四窗口均已合入并核对；#275/#277/#279已关闭。各窗口57/39/63/3项成功证据分别保留（有交叠，不累加），单份汇总入口为[W18-04 DELIVERY](../development/evidence/WAVE18/foundation-integration/DELIVERY.md)，含三个前置报告链接和实际边界。任务级SPEC/QUALITY均由唯一总控集中作者自审，不称独立审查。

全轮21个自有PG及临时HTTP已停止、端口释放；未动根7项用户资料。工作树新增/删除0，复用同一目录和构建缓存；未触发Actions、修改既有库或部署。Handoff交付 `2e61d1de816e9d333f5f2d5feddb66419300d3a3` 已回读；最后文档收尾正常合入/远端核对和最终Handoff后才结束持续目标。下方进行中状态仅为历史时点。

完整WP02/F1/F2、普通身份RLS、完整目录/CRM采用、Owner提交前撤权fence及真实DEV/业务/生产保持未完成。下一轮先限定其中一个公共基础窗口再登记，不自动扩成全库schema改造或给A/B/C/D派工。

## 2026-10-18：W18-04串联验收已通过，待正常集成与最终收尾

真实Role/Tenant控制器、当前权限、管理审计和身份快照/两类Outbox的PG/HTTP串联最终3项通过，包含旧token/缓存撤权、跨租户隔离和数据库拒绝后的整体回滚。单份[DELIVERY](../development/evidence/WAVE18/foundation-integration/DELIVERY.md)含实际夹具失败/局部复验与一次集中SPEC/QUALITY作者自审，无剩余阻断。仅测试配置/覆盖有变，无新产品改动；三项前置代码已远端合入，#275/#277/#279均Closed。

两轮自有PG和HTTP均停止。正常PR、远端核对及全轮台账/Handoff最终收尾尚待执行，目标仍active；完整WP02/F1/F2、普通RLS、CRM消费/目录与Owner最终门、真实DEV/业务/生产不提升。新增工作树/子代理/Actions/既有DB操作均0。

## 2026-10-18：W18-03已交付，W18-04串联验收开工

W18-03候选 `865a5b7b2f28c881efb8b377f1825acc3c3ed248` 经[正常PR280](https://github.com/GTX537/CP6/pull/280)合入远端main `58bdd59de191d3dd576c1fb5546a30a123abe74a`，10个范围文件一致；[#279关闭](https://github.com/GTX537/CP6/issues/279#issuecomment-6252281925)。63项去重通过及集中作者自审按[单份DELIVERY](../development/evidence/WP02-05/identity-consistency/DELIVERY.md)保留真实范围，6个自有PG停止。下方“待合并/Open”为历史时点。

W18-04从最新main在原目录串行START，Handoff `b9292f5d9d4a38eb3bc51d41d8393d057591a835` 已发布回读；只补真实Tenant/Role控制器、权限读取、管理审计和身份消息跨切片PG/HTTP。当前尚无W18-04通过声明。无新工作树、子代理、Actions或既有DB操作；完整WP02、RLS、CRM消费、Owner最终门及业务/生产未提升，持续目标active。

## 2026-10-18：W18-03 / BUG279 候选已验证，待正常集成

修复锁后复用旧跟踪快照导致撤权遗漏版本/优先消息的P0；原生PG原步骤红绿，真实捕获/并发/隔离/回滚13项与相关捕获/合同50项去重通过。单份[DELIVERY](../development/evidence/WP02-05/identity-consistency/DELIVERY.md)包含全部失败与复用来源及一次集中SPEC/QUALITY作者自审，无剩余阻断。当前#279保持Open，正常PR和远端核对待执行；W18-04随后串联HTTP。

完整WP02-05、CRM消费/目录、所有writer提交前撤权fence、普通身份RLS及业务/生产验收未提升。6个自有PG停止，既有库未动，无Actions、新工作树或子代理；持续目标active。下方旧进展保留历史时点。

## 2026-10-18：W18-02 已交付，W18-03 身份一致性开工

W18-02候选 `cfa30476fa10445ea70c5995e9ce5fe4d527f708` 已经[正常PR278](https://github.com/GTX537/CP6/pull/278)合入远端main `d77d8c030ee2fd17b8e2f6c8e24d476112d5f0d9`，15个范围文件一致；[#277已关闭](https://github.com/GTX537/CP6/issues/277#issuecomment-6252111391)。39项去重成功与集中作者自审按[单份DELIVERY](../development/evidence/WP02-04/tenant-management-audit/DELIVERY.md)复用，不重跑未变产品。8个自有PG及HTTP已结束，无Actions、既有DB或部署操作。下方待合并/Issue Open为历史时点。

同一总控从最新main在原目录开启W18-03，START经Handoff `a6cc53751a6062fe93cf1582a9c8e9d9e2f2235b` 发布回读，登记身份捕获/快照/公共SaveChanges串行写集及PG55435。首先复现锁后旧跟踪快照风险，尚无新BUG结论或产品通过；复用已合入PG夹具，新增/删除worktree均0。完整WP02-05目录协议、CRM投影、最终撤权fence及业务/生产未提升；持续目标active，W18-04仍待接续。

## 2026-10-18：W18-02 / BUG277 候选已验证，待正常集成

租户create/update/suspend/reactivate与必需管理审计已在同一保存/事务内；失败整体回滚，caller先前工作保留，取消能打断真实锁等待。原生PG/HTTP12项及相关27项去重通过，失败原件保留；真实HTTP平台三道闸与审计失败500/无业务残留已验证。[单份DELIVERY](../development/evidence/WP02-04/tenant-management-audit/DELIVERY.md)包含一次集中SPEC/QUALITY作者自审及默认菜单局部补验，无剩余阻断。

当前#277仍Open，候选尚未正常合并；下一步远端核对后关闭并接续W18-03。普通登录审计不变，未新增幂等恢复；失败DbContext不可直接重试。完整WP02-04/10、普通数据库身份RLS、真实业务/DEV/生产状态未提升。8个自有PG实例与临时HTTP已结束；无Actions、既有DB、部署或新增/删除worktree。持续目标active，W18-03/04仍待串行实施；以下“取消/HTTP待执行”为历史时点。

## 2026-10-18：W18-01 已交付，W18-02 强审计开工

W18-01候选 `d0805b6d28a65d199886b0f2d8c370d3588aa092` 已经[正常PR276](https://github.com/GTX537/CP6/pull/276)合入远端main `f58080a06df806b00e4f7da0ba2a64404253ee6b`，12个范围文件完全一致；[#275已关闭](https://github.com/GTX537/CP6/issues/275#issuecomment-6251826942)。真实PG9项、权限46项、CRM投影2项去重通过，最后测试注解修正仅局部验证1项；源日志和5个已停止实例保留。作者集中自审及[单份交付](../development/evidence/WP02-03/consistent-permission-read/DELIVERY.md)范围不变，未重复原成功验证。以下待合并/Issue Open为历史时点。

同一总控从最新main在原目录切 `codex/wave18-tenant-management-audit-20261018`，W18-02真实START经Handoff `3fb4e89a2ec059e4562ea231d6d4e81a7e89fc75`发布回读；源码已确认管理业务先保存、后best-effort审计缺口，下一步原生PG失败复现。写集/PG55435已登记，公共CP6Context事务段保留给W18-03；普通登录审计合同保持。无新增/删除worktree、Actions、既有DB或部署操作。完整任务/业务验收未提升，持续目标active，W18-03/04仍待串行实施。

## 2026-10-18：W18-01 / P0 BUG275 修复验证通过，待集成

第十八轮首项真实PG并发复现确认[#275](https://github.com/GTX537/CP6/issues/275)：旧读取可组合出从未有效授予的动作。已改为单语句一致快照并传播请求取消，保留主体/tenant、MIN/MAX、合法尾空格及调用者事务所有权。9项原生PG、46项权限、2项CRM投影通过，无失败/跳过；原RED、文本和取消失败均保留。[单份DELIVERY](../development/evidence/WP02-03/consistent-permission-read/DELIVERY.md)包含一次集中SPEC/QUALITY作者自审，无剩余阻断；不称独立审查。

当前候选尚未正常合并，Issue仍Open，下一步远端核对后关闭并接续W18-02租户管理强审计。完整WP02-03及375任务/125业务状态未提升；已有RR事务读取的是调用者旧快照，不是Owner提交前撤权fence。普通身份完整RLS、真实DEV/业务/生产与容量未验收。5个自有PG实例均已停止并保留证据；没有既有DB操作、Actions或部署，新增/删除worktree均0。持续目标保持active，W18-02/03/04未完成。以下“仅规划/尚无产品结果”为历史时点。

## 2026-10-18：第十八轮公共基础持续目标，单总控串行

用户要求未来24小时由当前总控对话独自推进，已建立持续目标及[第十八轮执行计划](../development/dispatch/20261018-wave18/README.md)。从已确认main `b4516ce3b2e4098347ee9a0d89322fcd561a5da9` 接续，依次为当前授权读取一致性、租户管理与必需审计同事务、身份权威快照/版本/Outbox一致性、受影响PostgreSQL/HTTP串联及逐项集成。预计16～24小时，包含修正与合并；时间到不等于目标完成。

本登记仅W18-01开放实施前置，正常规划合并后从最新main开独立分支。其余窗口由同一总控核对实际前置后串行接续，无需用户重复批准；不派新对话/子代理。复用已释放COORD目录，新增/删除worktree均0；用户原件、历史证据及数据库保留。当前没有本轮产品通过声明。

PostgreSQL为唯一新增目标，精简本地验证、一次任务级集中作者自审，明确不冒充独立审查。不触发Actions、不改既有数据库或环境、不部署生产。完整WP02、普通身份全库RLS采用、跨仓Owner撤权fence及业务验收未完成；本轮有限成果不自动提升375任务/125目标状态。

## 2026-10-18：第十七轮收尾，四项Issue均已关闭

原A最终审查134c283/be1e4dc批准菜单候选3bc3d3c8；原B局部复查a7d24cd8/82df565批准发布工具候选aec0e965、解除#251/#252。C经[正常代码PR](https://github.com/GTX537/CP6/pull/271)、D经[正常代码PR](https://github.com/GTX537/CP6/pull/272)逐项合入，远端产品main `8146d5e4063a1ae702e7639dc00f2df0ba29a37d` 包含两候选且范围文件一致。#251/#252/#243原完成条件已核对并Closed；此前#253经PR269关闭。本轮所有实现/审查窗口与资源预约关闭，原候选/证据/目录保留。

复用原11项工具回归和一次实际publish、锁定Vitest4.1.11的23项组件测试及原页面/type-check；失败记录保持。新publish source仍68990978，原宿主/HTTP仍4704592f；菜单仍本地API夹具，未改称最新main宿主、整仓type-check或真实业务通过。总控只做集成一致性及文档/台账检查，无重复产品运行、Actions或数据库/部署操作。

完整WP02-02/18/28、375任务/125业务状态不提升；F1身份/租户生命周期及F2真实Owner事务仍按后续有限窗口推进。总控继续复用既有目录切独立分支，本次新增/删除worktree均0；旧目录回收计划未执行。以下Open/待审查/待合并是历史时点。

## 2026-10-18：BUG253已集成并关闭，C/D继续审查收口

[#253已Closed](https://github.com/GTX537/CP6/issues/253#issuecomment-6250839015)。[原C定向复查](https://github.com/GTX537/CP6.AI-Handoff/blob/46c8ee9de5b1d85f0dd316fa389f492301ef5e52/handoffs/CP6-DEV-20261012/tasks/REVIEW-W13-B/REVIEW.md)解除R1，固定候选836de524通过[正常PR269](https://github.com/GTX537/CP6/pull/269)合入远端main `6be28cb56ffa5cca4d29cfc26d935d769daf3535`；五个范围文件与已验证候选一致。原合法实例/身份的真实stdin/PG回归5通过、0失败/跳过，RED3与原7项未变证据保留真实范围，不因合并SHA变化重复运行。B产品窗口与PG55436预约释放；原始证据、候选和目录暂保留，B继续原REVIEW-W13-C局部复查。

C固定候选aec0e965已通过Handoff5f9d5d12交B复查#251/#252；其报告记录11项工具回归和一次真实publish，尚待原B结论，不预先关闭Issue。D固定候选3bc3d3c8及锁定23项补证已交A，原集中审查最终结论待回。A/B/C/D不重复START/ACK。完整WP02-02/18/28及375任务/125业务状态不提升，当前待审查不转为业务通过。

沿[存储计划](../development/planning/20261018-workspace-storage.md)，本收尾继续复用原COORD目录创建独立分支，新增/删除工作树均0。文档/台账检查通过；无产品重跑、Actions、既有数据库/服务操作或部署。以下原候选Open/不可合并/待复查记录为历史时点。

## 2026-10-18：四对话已开工，协调目录改为复用

第十七轮A真实START及B/C/D实际PROGRESS已回读：A准备菜单集中审查、B修#253、C修#251/#252、D补#243锁定依赖证据。台账已登记实际开始。随后收到D固定候选3bc3d3c8（Handoff2af18458），锁定Vitest4.1.11相关23项通过，仅原DELIVERY改变；A准备报告3e0b584d已到，接续同次集中审查，最终可合并结论待回。B随后交付#253修正836de524（Handoffff88448c），真实stdin/PG定向5通过、保留原3项RED失败，待原C局部复查；C自身#251/#252修正仍在进行。写集/端口/Owner不变。

按用户节省空间要求，完成107个CP6和46个Handoff工作树的只读盘点，已统计约167 GiB逻辑文件，docs约86 GiB；不能当作保证可回收容量。总控复用已合并的wave17-dispatch目录切新分支，没有新增工作树。详见[存储计划](../development/planning/20261018-workspace-storage.md)：固定目录席位、任务独立分支、只读审查免完整检出、保留原证据；首批5个干净已合并协调目录约5.2 GiB仅列候选，尚未删除。当前四人工作目录保持；无Actions、产品测试或数据库操作，完整任务/业务验收未提升。

## 2026-10-18：第十七轮恢复公共基础候选收口

P0/P1队列已闭环，当前四个Open均P2。本轮B修正#253原始期限精度（自有PG55436/真实CLI），C同窗口修正#251/#252源码绑定与自有进程清理；沿原B/C交叉报告只复查变化。D补菜单候选锁定Vitest4.1.11证据，A准备并在固定交付后完成一次集中审查。详见[本轮执行包](../development/dispatch/20261018-wave17/README.md)。

准入不等于开工：尚无本轮实际PROGRESS/START或新候选。新产品分支由总控在准入后从最新main承接原提交，旧候选保留。只跑具体受影响验证；C默认无PG/HTTP，D默认仅相关组件测试，原有效证据复用。375任务状态与125业务验收未提升；共享状态/串行合并由COORD独占，无Actions、现有数据库操作或部署。

## 2026-10-18：BUG132已集成并关闭，第十六轮收尾

[#132](https://github.com/GTX537/CP6/issues/132#issuecomment-6250315486)已Closed。[D原报告局部复查](https://github.com/GTX537/CP6.AI-Handoff/blob/c84c6da6e2bf1ee570513dacf3e03248a8910f00/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-132/REVIEW.md)确认e83593a3可合并、R132-01解除；[正常PR265](https://github.com/GTX537/CP6/pull/265)已合入，远端main `5a29b6b106aa3b4bf267811371771d9209bb80fd` 包含候选且17个范围文件一致。[单份DELIVERY](https://github.com/GTX537/CP6/blob/e83593a3ddd5ffd1ac0614b943eaf3358483989d/docs/quality/evidence/issue-132/DELIVERY.md)保留完整精确集合、有界POST、旧GET兼容及实际错误层413修正证据。

复用前端35、修正HTTP/OpenAPI65和InMemory9通过结果，各0失败/跳过；修前失败和原61项的宿主限制保留，不重复产品测试。Issue原条件满足，无新增数据库/真实DEV门禁，也不宣称该类验收已通过。台账本轮实现/审查窗口关闭、资源预约释放；原A候选保留，B/C旧阻断修正仍需独立准入，375任务/125业务状态不提升。Actions启动/取消/触发变更均0，无数据库操作、现有服务替换或部署。以下Open/待审查内容是历史。

## 2026-10-18：总控已交付R132-01修正，待原D局部复查

修正代码 `40a2d2b292df001cea4c338600ffb958e4381ae7` 已完成本地定向验证，见[原单份DELIVERY](https://github.com/GTX537/CP6/blob/40a2d2b292df001cea4c338600ffb958e4381ae7/docs/quality/evidence/issue-132/DELIVERY.md)。仅库存POST局部处理绑定阶段的413异常，公共中间件/Program/请求上限未改。HTTP夹具加入实际Space错误层；修前2项预期413/实际500的RED保留，修后HTTP/OpenAPI65通过、0失败/跳过（7行为+58合同）。非目标异常保留原500映射，合法边界200、超限413/ProblemDetails及无runtime调用均通过。

Kestrel chunked计数包含格式字节；本夹具单chunk在边界处含14字节格式，报告已明确载荷/传输字节区别，保留最初错误正向预期的失败记录。原前端35/InMemory9及D未变范围结论复用，无重复全仓或数据库/真实DEV验证。

原A候选325dc769和干净工作树保留，COORD仍是修正唯一Owner；原D对固定修正只复查R132-01及影响，沿用原REVIEW，无第二START/ACK。#132及阻断保持Open，产品尚未合并；本条随候选交付，不能当作主线集成或D通过。18232/15232无监听，无Actions、数据库操作或部署，375任务/125业务不提升。

## 2026-10-18：总控独占接手R132-01，原D继续局部复查

用户要求继续推进。远端A仍为325dc769/Handoff8ff05c67，原A工作树干净，未观察到修正开工或交付；原D仍为9ff8c9f的No结论。当前工具无法唤醒原用户对话，COORD按总控权限接手本项定向修正，作为唯一产品写入者；原A保留原候选和工作树，不再并写R132-01。此调整不是原A已接受消息或已开始修正的事实。

本登记正常合并后，从最新确认main创建 `codex/bug-132-http413-correction-20261018` / `D:/CP6-worktrees/bug-132-http413-correction-20261018`，以可审计cherry-pick承接固定325dc769，再修正端点超限错误映射。原13项写集和18232/15232资源转由COORD独占；Program、公共中间件、依赖、数据库仍只读。实现前再次核对原A无变化及端口空闲，如有并行冲突先停下协调。

先在HTTP夹具加入实际Space错误层，复现超限返回500，再做端点局部修复；覆盖exact1MiB200、声明长度/chunked超1字节413、ProblemDetails body/content type、超限无runtime调用及非目标异常不被吞。复用未变前端35/InMemory9等有效证据，只补受影响HTTP/合同。原D在固定修正交付后只复查R132-01及影响，原REVIEW接续，无第二START/ACK或完整审查。

本次仅所有权登记，不宣称修正已完成；#132和R132-01保持Open，产品未合并。无数据库/真实DEV新增门禁，无Actions、数据库操作或部署。完整任务24/375 DONE、业务0/125不提升。

## 2026-10-18：BUG132集中审查暂不可合并，原A接续定向修正

D已对固定候选 `325dc7693edf09ec0c9175c68554e4e3c72b2af0` 完成一次集中SPEC/QUALITY；[原REVIEW更新](https://github.com/GTX537/CP6.AI-Handoff/blob/88c89e639cfa1b1da6d98ed84d96f172cf6022f3/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-132/REVIEW.md)和Handoff `9ff8c9f243b6446272e5b15e5c7901719126b349` 已远端回读。**Ready to merge：No；P2阻断R132-01。** 源码显示实际SpaceDesignProblemDetailsMiddleware把超限绑定异常改为500/SPACE_INTERNAL_ERROR，而原HTTP fixture省略了该层，未证明声明的413/ProblemDetails合同。此为源码审查结论，D和COORD未新执行实际应用探针；不代表大小限制被绕过。

原A在同一BUG132分支及既有写集接续修正，无第二START/ACK。优先端点局部处理绑定阶段异常；Program和公共中间件保持只读。定向HTTP fixture纳入实际Space错误层及相对顺序，保留RED→GREEN：恰好1MiB为200，Content-Length/chunked超1字节均413、application/problem+json及body状态符合合同，超限不调用runtime，非目标异常行为不变。仍只更新原DELIVERY，固定交付后D在原REVIEW仅复查该修正及影响。

原前端35、HTTP/OpenAPI61、InMemory9通过记录保留；其中413结果限简化宿主，不能算实际管线通过。未变前端/内存与其余有效证据复用，只补受影响HTTP/合同及必要编译。本轮不新增数据库或真实DEV门禁，不重做全仓测试或完整审查。

[#132](https://github.com/GTX537/CP6/issues/132)保持Open，产品未集成；R132-01作为本Issue候选阻断记录，不另建重复Issue。#38/#257保持Closed，B/C修正仍待各自窗口；完整任务24/375 DONE、业务0/125不提升。无Actions运行/取消、工作流变更、数据库操作或部署。

## 2026-10-18：BUG132固定交付已收，等待D集中审查

A候选 `325dc7693edf09ec0c9175c68554e4e3c72b2af0`、Handoff `8ff05c67e42e6b78335afc07cf785e561b54a8d0` 已远端回读；[单份DELIVERY](https://github.com/GTX537/CP6/blob/325dc7693edf09ec0c9175c68554e4e3c72b2af0/docs/quality/evidence/issue-132/DELIVERY.md)限登记的10个路径，基线为main `1e29931374626a492ca92e848b8d8fcaf871eddf`，工作树干净。库存查询改为完整精确集合的有界POST，保留旧GET；产品代码尚未集成。

COORD读取原始日志/TRX，核对前端 **35**、HTTP/OpenAPI **61**、InMemory服务 **9** 项通过，各0失败、0跳过，未重跑。前端最终来源为锁定依赖Vitest4.1.11，早先4.1.9结果不计作最终证据；HTTP使用真实Kestrel/Cookie/CSRF及受控权限与runtime数据，服务测试使用EF InMemory/记录源，不冒充PostgreSQL或真实WMS验证。A自有18232/15232均无监听。

A真实START `b7b618df`、D真实START `2b45642d` 已登记。[D原REVIEW](https://github.com/GTX537/CP6.AI-Handoff/blob/843dcc019ea311bc18ac186f5ad397c865f9ff2e/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-132/REVIEW.md)及准备消息 `603df33948af18de597f2e733374b6d21db333fe` 仅为只读准备，尚无Ready to merge结论；“尚无A固定交付”的前置现已满足。原D直接对固定候选在同一REVIEW接续一次集中SPEC/QUALITY，不重复START/ACK或新增报告；总控收件不代替正式审查。

[#132](https://github.com/GTX537/CP6/issues/132)保持Open，待D结论后正常集成和原Issue验收核对。本轮不新增数据库或真实DEV门禁。#38/#257保持Closed，P0暂停已解除；B/C原P2修正仍待各自窗口，旧菜单候选未变。完整任务24/375 DONE、业务0/125不提升。无Actions运行/取消、工作流变更、数据库操作或部署。

## 2026-10-17：仪表盘租户P0已完成真实DEV并关闭，转入P1库存传输

[#257](https://github.com/GTX537/CP6/issues/257)已Closed：[PR260](https://github.com/GTX537/CP6/pull/260)合入D审查通过的22928c9a，远端main47b69063核对候选文件一致；[单份DELIVERY](../quality/evidence/issue-257/DELIVERY.md)记录真实PG DEV验收。实际发布API/Web身份和文件哈希匹配，3个页面登录账号的空租户、精确订单及交替暖缓存通过；停用主体403、匿名401。原6项PG/HTTP结果复用，未重复测试。

4条自有测试订单移除、3账号/3租户停用及refresh吊销，API/Web停止，原PG服务保留。既有认证五表/业务六表指纹、688表和四历史不变。无Actions、工作流修改、迁移或生产部署；不提升其他WMS块、全业务/生产验收。

当前无开放P0，P0暂停解除；依BUG管理P1下一项规则，[第十六轮](../development/dispatch/20261017-wave16/README.md)登记原A修复#132精确库位范围有界传输、原D准备/集中审查，尚无真实START。旧整站fallback补丁已撤回不得照搬。B/C候选和三个P2保留，尚无新的产品修正窗口；#243菜单候选不变。完整任务24/375 DONE、业务0/125不提升。

## 2026-10-17：BUG257固定候选已收，D集中审查待接续

A候选 `22928c9ae9ad93172c61e3df5e0353cdaace4af6` 已推送，Handoff `9de55c7441830061fc73541c20c562575f4086a5` 已远端回读；[单份DELIVERY](https://github.com/GTX537/CP6/blob/22928c9ae9ad93172c61e3df5e0353cdaace4af6/docs/quality/evidence/issue-257/DELIVERY.md)范围严格为登记六文件。COORD读取原始green-subject.trx核对真实PG/HTTP **6通过、0失败、0跳过**，包括四条HTTP隔离/缓存边界与两个原生关系测试；只复用原结果，未重跑产品测试。原失败及夹具修正保留，不将RED中空用户GUID冒充合法主体正向证据。A独立PG/API/Web端口已释放，既有PG服务运行，未操作DEV数据。

A真实START6cdbf743、D真实STARTac370a04均登记。[D单份REVIEW](https://github.com/GTX537/CP6.AI-Handoff/blob/fd1ee29f2f584154f7013ee15adebc71e1744f6f/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-257/REVIEW.md)及Handoff `88bf3beb6a175a83a79d80ea0ff59f2f43a79a8a` 当前仅只读准备，尚未给候选Ready to merge。A固定交付已满足原审查前置，D直接在原REVIEW接续一次集中SPEC/QUALITY，不需第二START/ACK。总控收件核对不代替该正式审查。

[#257](https://github.com/GTX537/CP6/issues/257)保持Open，修复代码尚未合并，真实DEV为NOT_RUN；D通过后由COORD正常集成并在已确认PG副本验证空租户/交替缓存。#38保持Closed。B/C三个P2与旧候选普通实施/集成继续暂停；完整任务24/375 DONE、业务0/125不提升。无Actions启动/取消、工作流修改、数据库迁移或生产部署。

## 2026-10-17：登录P0真实DEV通过并关闭，转入仪表盘隔离P0

[#38](https://github.com/GTX537/CP6/issues/38)已关闭：[单份DELIVERY](../quality/evidence/issue-38/DELIVERY.md)记录用户确认的本机PostgreSQL副本DEV、main84896578实际发布身份和浏览器原步骤通过。首轮专用角色缺user:query/add被拒，修正仅本次角色的两项权限后重跑通过；产品未变，原失败保留。原10用户、4租户及角色/菜单关联指纹、688表和四历史不变；新增测试账号/租户停用、refresh吊销、API/Web停止，既有PG服务保留。认证50项等原证据复用，无全仓重复测试。

真实页面另显示新空租户读取其他租户订单，源码/数据库核对全局查询和缓存，独立登记[P0 #257](https://github.com/GTX537/CP6/issues/257)。[第十五轮](../development/dispatch/20261017-wave15/README.md)登记原A唯一修复、原D准备与一次集中审查；尚无实际START。B/C三个P2修正和原候选普通集成继续暂停，当前原因是#257。375完整任务/125业务目标不提升，仍24/375 DONE、0/125业务完成。

无Actions启动/取消、工作流变更、原库迁移/覆盖、Tunnel或生产部署。测试账号写入仅发生在用户选定PG副本中的本次专用租户；不宣称业务隔离、HTTPS/OIDC enabled或生产验收通过。

## 2026-10-17：登录P0本地修复已集成，真实DEV待确认

[BUG38代码PR](https://github.com/GTX537/CP6/pull/255)正常合并；远端main `17ce6f742bf9aa885f7a293e13c30fcba124ca59` 已核对包含A固定候选 `09017d7ad14c7647601edac258422365ff81daf9`，范围文件与已验候选一致。[D最终集中审查](https://github.com/GTX537/CP6.AI-Handoff/blob/84b6d2de0f7b30d493cf068db94fafc20fb7594b/handoffs/CP6-DEV-20261012/tasks/REVIEW-BUG-38/REVIEW.md)本地Ready to merge = Yes、无阻断，交付ddc65ece已收；本任务单份[DELIVERY](../quality/evidence/issue-38/DELIVERY.md)追加集成记录。

复用已审查的相关认证50项（含HTTP13）、补断言后受影响HTTP13及完整应用PostgreSQL浏览器证据；主线只有协调文档漂移，未变代码/依赖不重复验证。COORD仅执行集成包含性/范围一致性及文档台账检查，不将复用称为新运行。原失败和HTTP/浏览器/PG证据边界保留。

下一步为[#38](https://github.com/GTX537/CP6/issues/38)真实DEV验收：确认目标/入口、包含修复的API/Web身份和测试账号/租户及创建改密范围。DEV NOT_RUN，Issue保持Open；B/C的3项P2修正及原候选普通实施/集成继续P0暂停。完整任务24/375、业务0/125不变，无Actions、工作流变更、部署、Tunnel或既有数据库操作。

## 2026-10-17：A登录候选已交付，交叉审查阻断已登记

[第十四轮最新收件](../development/dispatch/20261017-wave14/README.md)已同步四路真实START与固定消息。A候选09017d7a待D一次集中审查；D的“仍无DELIVERY”是早期快照，现可按原窗口接续，无第二START/ACK。A报告50项相关认证成功（含HTTP13），后续仅受影响13项复跑；本地PostgreSQL完整浏览器成功、资源已停止。COORD没有重跑这些结果，也没有合并A或关闭#38。

B对C审查为No（2项P2），C对B为No（1项P2）；对应GitHub Issues #251/#252/#253。原实施者B/C仅准备窄修正，产品修正未准入，原候选普通实施和集成继续P0暂停。D旧菜单/#243不变；完整任务24/375、业务0/125与原NOT_RUN保持。

文档所列本地DEV身份端点只读请求未成功，实际运行身份仍未确认；无登录/改密/部署/重启/数据库动作。真实DEV NOT_RUN，不以本地PG代替Issue关闭条件。此次验证为收件/源码定向核对、文档diff与台账一致性，没有新增产品测试、Actions或工作流改动。

## 2026-10-17：第十四轮已分配给原四对话，尚无真实 START

[第十四轮执行包](../development/dispatch/20261017-wave14/README.md)登记：A唯一实施登录P0 #38；B只读审查C的b9f3f30b实际publish候选；C只读审查B的e2bfcc0b租户资格候选；D先准备#38浏览器/DEV验收，在A固定候选后完成一次集中SPEC/QUALITY审查。交叉审查不改变原候选Owner、不授予产品写权限；只有A拥有新的认证代码/测试窗口。

本轮基于远端main4d5c115f，A实际从准入合并后的最新已确认main开始。#245已关闭，原A窗口不重开；B/C/D原候选与工作树保留，普通集成仍因#38 P0暂停。没有收到执行者真实START或新的交付，不把发布派工当作开工。完整任务24/375、业务0/125及原NOT_RUN不变。

#38本地代码交付和真实DEV验收分别记录，没有授权部署/替换环境、既有数据库操作、Actions或正式发包。台账、生成页和四状态由COORD统一同步；本次仅做文档/资源/链接及台账一致性检查，不重复产品验证。

## 2026-10-17：权限 P0 修复已集成，转入登录 P0 准备

[权限修复 PR](https://github.com/GTX537/CP6/pull/248)正常合并，远端 main `be66f70748b1d3185d95629b3a9a7e0b139eeea0` 已核对包含集成提交 `33d7c666284891cc91b6b1cb02895f1a61bc64b9`，范围文件与已验候选一致。#245 原步骤由最终相关回归及有效原生 PostgreSQL 证据覆盖，Issue已关闭；A限定窗口CLOSED。完整WP02-03仍PLANNED/NOT_CHECKED，Owner最终撤权fence、权威版本/字段V1来源、全Main与业务验收仍未完成。

[单份交付报告](../development/evidence/WP02-03/current-permission-consumption/DELIVERY.md)保留原15红测、原PG夹具失败、审查发现的2FA用途绕过及修正6失败/21通过记录；最终37项相关测试通过，原审查者局部复查通过，未受影响PG3及其他结果按来源复用。未因提交/合并重复产品验证。

[公共核心基础优先](../development/planning/20261017-foundation-priority.md)已获批准并执行首项P0交付。下一个是#38登录旧Cookie修复，独立范围已准备但尚未开工；真实DEV验收仍须按Issue条件留证。B/C/D候选保留、普通集成暂停，未收到暂停ACK不冒称已收到。24/375完整任务、0/125业务目标不变。无Actions触发/取消、工作流变更、发包、既有数据库操作或部署。

## 2026-10-17：权限 P0 候选修正已通过集中审查与局部复查

[单份交付报告](../development/evidence/WP02-03/current-permission-consumption/DELIVERY.md)记录当前主体/tenant绑定、启用角色和实时权限读取。集中审查在原候选发现真实2FA pending可通过的P0，修正提交 `add3373edd1c8d751a9ff2cd32079b4a96b274c1` 已在当前权限边界拒绝用途标记并保留合法正常/平台/impersonation/OIDC访问；原审查者仅复查修正影响，最终Ready to merge: Yes。

修正前新增回归6失败/21通过；修正后当前权限、真实过滤器及权限服务相关37项通过，0失败/跳过。原候选未受影响的原生PostgreSQL及相关行为按原报告复用，失败全部保留；没有将旧结果称为重新执行。共享状态已准备正常串行合并，#245仍Open直到远端包含性和原步骤证据核对；完整WP02-03仍PLANNED/NOT_CHECKED、业务NOT_RUN，24/375完整任务与0/125业务目标不变。

公共核心基础优先顺序已批准；#38独立窗口仍处于准备，B/C/D普通集成暂停。没有Actions、工作流变化、发包、既有数据库操作或部署。

## 2026-10-17：权限候选审查阻断与限定修正

[已批准基础计划](../development/planning/20261017-foundation-priority.md)登记 #245 候选的唯一 P0 审查问题：两种真实 2FA pending 令牌能通过当前权限读取。COORD 内唯一代理在独立分支/worktree接管三文件修正，原 A 候选与证据保留、不得并行追加；实施在登记发布后开始。只补用途拒绝与相关真实令牌回归，原审查者局部复查，完整任务/业务验收及普通B/C/D暂停状态不变。

## 2026-10-17：公共核心基础优先已获批准

用户已批准[公共核心基础优先](../development/planning/20261017-foundation-priority.md)：P0 闭环→租户/身份/授权→真实共同事务及跨仓采用→询盘完整验收→业务扩展。保留375任务和原完成门，不等待整个WP02清零；四份候选尚待分别审查集成，本轮不新增领域级实施准入。

## 2026-10-17：全项目盘点与 P0 优先安排

[全项目盘点](../development/planning/20261017-project-inventory.md)核对 14 工作包、125 目标、375 任务；用户选择先打通一条可验收业务主线。24 DONE、2 IN_PROGRESS、349 PLANNED 不变，局部交付与完整业务验收分别记录。已读取四个 wave13 新对话的真实 START，随后收到四份固定候选 DELIVERY，均尚未总控审查或集成。

A 在原授权范围复现 P0 #245 并报送修复候选，优先审查集成；另有登录 P0 #38 仍 OPEN，源码检查未见所述路径闭环，本次未运行复现。按 BUG 规则暂停 B/C/D 普通集成及新增实施并经 Handoff 通知，保留工作/资源，可只读准备；尚无实际暂停 ACK，不宣称聊天已停止。恢复须协调窗口明确登记，完整任务与目标状态不改变。首条候选主线为公开询盘→受理/审核→线索，完整目标验收及隐私合同保留，日历日期待剩余范围估算。

本次为文档、台账与沟通同步，检查差异、链接和台账一致性；不跑产品测试，不触发/取消 Actions，不改工作流、不发包、不操作既有数据库或部署。

## 2026-10-17：第十三轮四个新对话窗口已登记

[四任务执行入口](../development/dispatch/20261017-wave13/README.md)：A当前授权读取、B服务租户资格control CLI、C真实publish宿主身份验证、D菜单刷新恢复（关联Issue #243）。用户手动粘贴至四个新对话；限定窗口CONFIRMED，尚未收到实际START，未执行新产品实现。COORD独占共享状态与串行合并，各任务不依赖本轮他人的候选。

第十二轮已交付工作不重复。完整任务24/375 DONE、业务0/125、247 NOT_RUN与41历史引用不变；A/D完整任务PLANNED/NOT_CHECKED，B/C IN_PROGRESS/CONFIRMED，各自完整前置及未验收范围保留。PostgreSQL、相关本地验证及一次任务级集中审查继续生效。无Actions、正式发包、既有库迁移或部署。

## 2026-10-16：第十二轮真实服务签发局部交付已集成

[241号PR](https://github.com/GTX537/CP6/pull/241)正常合并，远端main `1f52f3bf27cc13a4ad51df3d2b6bcfd0f6810fe1`已核对任务祖先关系及全部范围文件。实际 client_credentials HTTP 签发在普通租户 PostgreSQL 登录的同一物理事务中读取真实 Sys_Tenants 资格、写入 ServiceToken 并完成提交前核验；FOR SHARE 将停用更新与签发序列化。新鲜隔离库显式选择 service-token-v1，最小权限 helper 仅返回自身资格，签发令牌可在同库真实自撤销；注册时签名快照覆盖首次 DI 解析，旧配置与旧 profile 保持原支持范围。

实现、一次集中审查和原始验证见[单份报告](../development/evidence/WP02-02/service-issuance/DELIVERY.md)，固定任务Handoff已发布。限定窗口CLOSED，完整WP02-02仍IN_PROGRESS/CONFIRMED。完整 WP02-02 仍 IN_PROGRESS/CONFIRMED；Full17PGT、全 Main、普通用户/部门登录与 snapshot、真实租户创建/维护、bootstrap/dispatcher、ERP/ordinary outbox、其他Owner/上下文及跨仓宿主、在线授权/配置/密钥/凭据更新、真实业务验收、既有库迁移、环境切换及 DEV/UAT/PROD 均 NOT_RUN。本轮仅完成 fresh-only 五表服务端点采用，不构成全 Main 启用或生产发布。

复用候选有效验证，不因合并重跑产品测试。24/375 DONE、业务0/125、247 NOT_RUN与41历史引用不变，A/C/D无新窗口；无Actions触发/取消、工作流变化、发包、既有库迁移或部署。

## 2026-10-16：第十二轮服务签发候选已核验

实际 client_credentials HTTP 签发在普通租户 PostgreSQL 登录的同一物理事务中读取真实 Sys_Tenants 资格、写入 ServiceToken 并完成提交前核验；FOR SHARE 将停用更新与签发序列化。新鲜隔离库显式选择 service-token-v1，最小权限 helper 仅返回自身资格，签发令牌可在同库真实自撤销；注册时签名快照覆盖首次 DI 解析，旧配置与旧 profile 保持原支持范围。

本地 .NET8 / PostgreSQL18.6 相关构建通过。原始第1轮未受影响的94项旧行为、后续目录/资格边界与双向并发锁场景按报告范围复用；第6轮首次解析签名快照及旧Crypto定向14/14通过。审查修正后PUBLIC权限、合法数据库/login、真实HTTPS签发→记录→自撤销链三项定向回归各1/1通过、零跳过；最终新profile与原旧profile的CLI同请求恢复成功，旧baseline digest保持。原始失败、两项红测和information_schema主动扩范围导致的首次修正失败全部保留；最终权限补充范围收敛为pg_catalog / pg_*。COORD核读原始TRX/日志并确认自有55436已停止；未因提交或合并重跑未受影响测试。

完成一次完整SPEC/QUALITY集中审查，发现PUBLIC系统目录有效权限漏检及全串schema替换误改合法数据库/login两项P2。均已修复：新profile按PG原生初始权限拒绝额外PUBLIC授权，规范化seal支持权限还原后同请求恢复；固定schema插值保留合法外部标识符。原审查者仅复查127abecb..1722b061四文件修正及相关原始证据，最终Ready to merge: Yes，无剩余阻断项。COORD核对允许路径、完整diff检查、失败/成功证据及旧profile兼容；旧seal、实体/EF模型/历史迁移和依赖配置未变。

详见[单份报告](../development/evidence/WP02-02/service-issuance/DELIVERY.md)。当前等待正常PR合并及远端核对，尚未记作远端完成。完整 WP02-02 仍 IN_PROGRESS/CONFIRMED；Full17PGT、全 Main、普通用户/部门登录与 snapshot、真实租户创建/维护、bootstrap/dispatcher、ERP/ordinary outbox、其他Owner/上下文及跨仓宿主、在线授权/配置/密钥/凭据更新、真实业务验收、既有库迁移、环境切换及 DEV/UAT/PROD 均 NOT_RUN。本轮仅完成 fresh-only 五表服务端点采用，不构成全 Main 启用或生产发布。 完整任务24/375 DONE、业务0/125、247 NOT_RUN与41历史引用不变。未触发/取消Actions、未改工作流、未发包、迁移既有库或部署。

## 第十二轮签名器必要范围补充

已登记[同一实现窗口的单文件扩展](../development/dispatch/20261016-wave12/WP02-02.md)：修复首次DI解析仍依赖可变legacy签名配置的问题。唯一执行者不变，原成功结果按输入范围复用，最后仍为一次集中审查；完整任务及业务状态不变，无Actions或部署。

## 2026-10-16：第十二轮服务签发窗口已登记

[执行计划](../development/dispatch/20261016-wave12/README.md)：已核对最新Main和Handoff无新交付，选择WP02-02真实服务签发/租户有效性/同事务记录为下一轮；唯一COORD实现代理已开始只读准备，准入发布后实施。第十一轮不重做，A/C/D无新窗口。

完整任务24/375 DONE、业务0/125、247 NOT_RUN与41历史引用不变；WP02-02仍IN_PROGRESS/CONFIRMED。本条完成范围是派工/资源登记，功能尚未交付。沿用PostgreSQL与精简验证，无Actions、正式发包、既有库迁移或部署。

## 2026-10-16：第十一轮服务令牌自撤销局部交付已集成

[237号PR](https://github.com/GTX537/CP6/pull/237)已正常合并，远端main `222f948dbf8003d99f5b8bd51bde97236e62de2e`已核对任务提交祖先关系及全部范围内文件。实现、一次集中审查及三项安装问题的定向修复证据见[单份报告](../development/evidence/WP02-02/service-revocation/DELIVERY.md)。固定Handoff已发布；本局部窗口CLOSED，完整WP02-02仍IN_PROGRESS/CONFIRMED。

本次沿用候选有效验证，不为合并重复构建或测试。新profile仅覆盖fresh-only有限入口，真实租户活跃查询、其他Owner/contexts、跨仓消费者、在线授权更新、既有库迁移及原17PGT/业务验收仍未完成。24/375完整任务DONE、0/125业务、247 NOT_RUN、41历史引用不变。A/C/D无新窗口；未触发/取消Actions、未改工作流、未发包或部署。

## 2026-10-16：第十一轮服务令牌自撤销候选已核验

真实Basic来源、可信租户普通登录及ServiceToken/Snapshot/generation/优先Outbox同事务接线已完成候选核对，详见[单份交付报告](../development/evidence/WP02-02/service-revocation/DELIVERY.md)。复用原120项相关回归、安装收紧后的8项PG/HTTPS及2项共享guard定向结果，原始TRX均无失败或跳过；审查修复另验证未知布局拒绝后正常安装/恢复、发行身份漂移拒绝与真实HTTPS签发撤销、目录及安装状态漂移拒绝，并通过修复后CLI构建和私有stdin恢复/拒绝/同结果检查。原始开发失败、审查红测和夹具/断言纠正记录均保留，未重跑整套或把复用改称新执行。

一次集中SPEC/QUALITY审查发现3项P2：恢复缺目录行核验、发行角色未完整纳入身份校验、fresh安装缺系统schema未知对象拒绝。修复已在f315425b完成；同一审查者wave11_review仅复查8文件修改及其影响，核读定向红/绿TRX与CLI原日志，确认3项均关闭、旧空布局SQL原样提取且旧seal SQL未变；最终Ready to merge: yes，无剩余阻断项，仅适用于本轮有界交付。当前同步共享状态，随后正常PR合并并核对远端；尚未把候选记为远端完成。本次只采用服务令牌自撤销的可信来源、有限PostgreSQL写集及同事务链路；fresh-only安装不是完整Main数据库。真实租户活跃查询、其他Owner/contexts、普通快照与Outbox、ERP/dispatcher、跨仓消费者、在线授权更新、既有库迁移及原17PGT/业务验收仍未完成。 完整任务仍24/375 DONE、业务0/125、247 NOT_RUN及41历史引用。未触发/取消Actions、未改工作流、未发包或部署。

## 2026-10-16：第十一轮由协调内单一执行代理接续

用户要求把继续推进设为目标并允许范围内自主判断。COORD再次核对Main/Handoff，原B尚无START、分配工作树干净，现由当前协调对话内的唯一实现代理接续B / WP02-02窗口；实际资料读取已开始，代码实施在本次执行身份登记发布后开始。旧B对话恢复时应读取最新台账，不重复领取或并写该工作树。

沿用[第十一轮执行包](../development/dispatch/20261016-wave11/README.md)的全部合同、写入/资源边界、PostgreSQL及精简验证要求。24/375完整任务DONE、业务0/125、247 NOT_RUN保持；此登记未交付功能，不新增Actions、发包、既有迁库或部署权限。

## 2026-10-16：第十一轮自撤销接线窗口已分配

[第十一轮执行包](../development/dispatch/20261016-wave11/README.md)：已核对远端 Main、Platform 与 Handoff，第十轮之后无新交付。B / WP02-02 独占服务令牌自撤销的真实认证来源、有限 Core 写集隔离与同事务接线；限定窗口 CONFIRMED，尚未收到实际 START。A/C/D 本轮无新窗口，原有交付保留。

完整任务仍24/375 DONE、业务0/125、247 NOT_RUN、41历史引用。WP02-02仍IN_PROGRESS/CONFIRMED，其他完整状态/依赖不变；basis v1.2无需重复采纳，独立 verifier 未配置仍如实保留。仅派工文档、台账与资源检查；未运行产品测试、Actions或数据库，未发包、迁库或部署。

## 2026-10-16：第十轮三份局部实现已集成

[集成交付](../development/evidence/wave10-integration-20261016/README.md)：PR231/233/232按A→B→D正常合并并核对远端，三局部窗口关闭。字段权限确定性计算、PostgreSQL组合消息存储初始化/恢复、工作台数据状态已入主线。复用各固定候选相关验证与一次集中审查；仅补台账/链接/差异及远端核对，没有重复产品测试或构建。

WP02-03/WP02-18完整任务仍PLANNED/NOT_CHECKED，WP02-02仍IN_PROGRESS/CONFIRMED；真实授权与Owner写集、后端租户/撤权、原17PGT和业务验收未提升。24/375 DONE、0/125业务、247 NOT_RUN及41历史引用保持。未触发/取消Actions、未改工作流，未发包、迁库或部署；没有自动分配下一轮。

## 2026-10-16：第十轮 WP02-18 局部实现接收

Dashboard真实零、无权、失败与旧响应隔离已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP02-18/dashboard-states/DELIVERY.md)。复用固定候选16项定向前端测试、vue-tsc及Chrome桌面/390px手机API夹具检查；已读取最终日志，集中SPEC/QUALITY无阻断项。夹具早期失败保留，未称真实业务E2E。

当前同步状态并按 PR #232 串行合并，远端包含性由 COORD 合并后登记。只交付现有客户端状态与响应竞态；legacy后端KPI/全局缓存、scoped查询、租户/撤权、菜单CAS及20条原业务AC未验收。完整WP02-18保持PLANNED/NOT_CHECKED，WP02-03前置保留。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-16：第十轮 WP02-02 局部实现接收

PostgreSQL消息存储初始化、追加与原结果恢复已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP02-02/provisioning/DELIVERY.md)。复用固定候选CLI Release build7及真实PG18.6 attempt7（单个组合场景、零失败/跳过），已读取原始构建/测试日志与TRX；一次集中审查及修改部分复查PASS。原失败保留，旧RLS/池/RowVersion证据按原范围复用。

当前同步状态并按 PR #233 串行合并，远端包含性由 COORD 合并后登记。工具只支持明确选择的空隔离库与erp-identity-messaging-v1，使用静默初始化窗口；真实可信上下文、业务写集、Owner/WebApi接线及原17PGT仍未验收。完整WP02-02保持IN_PROGRESS/CONFIRMED。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-16：第十轮 WP02-03 局部实现接收

字段权限真实存行MIN与逐用途决策已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP02-03/field-decisions/DELIVERY.md)。复用固定候选报告记录的14项定向测试、必要编译及一次集中SPEC/QUALITY审查；组件没有接入真实授权链，未称本次重跑。

当前同步状态并按 PR #231 串行合并，远端包含性由 COORD 合并后登记。真实授权来源、registry/映射签认、当前版本、撤权fence、缓存与Owner接入未验收；完整WP02-03保持PLANNED/NOT_CHECKED，WP02-02前置保留。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-16：第十轮限定实现已分配

[执行包](../development/dispatch/20261016-wave10/README.md)：A 接 WP02-03 字段权限决策；B 继续 WP02-02 PostgreSQL 初始化/恢复；D 接 WP02-18 工作台数据状态。三个独立分支复用已合并且干净的工作树，写入范围已分开；尚未收到实际 START。C 等真实消费制品与数据库边界稳定，不重复做身份检查或新增信任门。

完整 WP02-02 保持 IN_PROGRESS/CONFIRMED；WP02-03、WP02-18 保持 PLANNED/NOT_CHECKED，仅本轮限定窗口 CONFIRMED。WP01-01、WP02-16 旧窗口 CLOSED；WP02-28 本轮无新窗口。原依赖及历史结果保留，24/375 DONE、0/125 业务、247 NOT_RUN 与41历史引用不变。派工只查差异、链接和台账，不运行产品测试、Actions、数据库或部署。

## 2026-10-16：第九轮四份局部实现已集成

[集成交付](../development/evidence/wave9-integration-20261016/README.md)：PR225/227/226/228 均正常合并并核对远端，四个局部窗口关闭。现有能力包括分阶段信任、Identity/ERP 共用租户事务、安全 CodeGen 预览和实际宿主身份诊断。原定向测试/集中审查按准确范围复用，最终组合补一次 Release 编译通过；未运行新的数据库/业务验收。

A/D 完整任务保持 PLANNED/NOT_CHECKED，B/C IN_PROGRESS/CONFIRMED。独立信任、真实 Owner/宿主业务接线与正向回退兼容仍待完成；24/375 DONE、0/125 业务、247 NOT_RUN 与41历史引用不变。源码、台账及四状态已同步；未触发/取消 Actions、未改工作流，未发包、迁库或部署。

## 2026-10-16：第九轮 WP02-28 局部实现接收

Main 实际运行身份观测与缺证回退拒绝已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP02-28/DELIVERY.md)。原相关单测36/36通过，尾斜杠修正后的真实 Main/授权撤销/固定503/旧health合同验证通过；未改单测结果复用。三份程序集匹配、两份未加载，整体 Unknown。

当前同步状态并按 PR #228 串行合并，远端包含性由 COORD 合并后登记。无两实际制品与准确 PostgreSQL schema/driver 的正向回退证明；正向兼容 NOT_RUN，正式OBS资格/Current trust/业务Owner未验收。完整 WP02-28 IN_PROGRESS/CONFIRMED，不登记 DONE。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-16：第九轮 WP02-16 局部实现接收

CodeGen 安全即时预览接口与页面已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP02-16/codegen-preview/DELIVERY.md)。原 CodeGen 25 项、合法名称修正后定向 20 项通过；未改 HTTP/旧接口证据复用。前端类型和实际 Vue/API 夹具页面检查通过，集中审查问题已关闭。

当前同步状态并按 PR #226 串行合并，远端包含性由 COORD 合并后登记。仅 NU04 局部预览；真实租户/Owner、保存 CAS/unknown 恢复、采番事务与文件应用未验收。完整 WP02-16 保持 PLANNED/NOT_CHECKED，WP02-10 前置不变。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-16：第九轮 WP02-02 局部实现接收

Identity 四表租户隔离与 ERP 同事务适配已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP02-02/identity-priority-tenant-isolation/DELIVERY.md)。原 PostgreSQL18.6 Identity 5/5、受影响 ERP 回归 4/4 通过，真实模型与合成 A/B 数据；其余池/ERP 证据按报告复用。集中审查 PASS，失败日志保留。

当前同步状态并按 PR #227 串行合并，远端包含性由 COORD 合并后登记。真实 IdentitySnapshotWriter/dispatcher/宿主当前授权/Owner、其他上下文与完整生命周期及原17PGT未验收；完整 WP02-02 保持 IN_PROGRESS/CONFIRMED。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-16：第九轮 WP01-01 局部实现接收

采纳与运行信任配置分离已完成候选核对，原验证与集中审查见[单份交付报告](../development/evidence/WP01-01/trust-stages/DELIVERY.md)。原 adapter 27 项、manager 8 项通过，旧夹具修正后单项通过；真实只读 manager 结构检查 PASS。复用原有效结果与集中审查，不称此次重跑。

当前同步状态并按 PR #225 串行合并，远端包含性由 COORD 合并后登记。独立维护者的实际 authority registry/公钥/有效签名尚未配置；真实业务运行未验收。用户采纳已决定，不再重问；完整 WP01-01 保持 PLANNED/NOT_CHECKED。 24/375 DONE、0/125业务完成、247 NOT_RUN、41历史引用不变。此次未重跑产品测试、未触发/取消 Actions、未改工作流或执行迁库/部署。

## 2026-10-15：第九轮四对话派工

[执行包](../development/dispatch/20261015-wave9/README.md)已登记：A 分阶段采纳信任、B Identity/ERP 同事务租户隔离、C Main 实际运行身份、D CodeGen 安全预览。各自独立分支/工作树，源码及 PG 55436/55438 范围分离；本轮 START 均未收到。A/D 完整任务保持 PLANNED/NOT_CHECKED，B IN_PROGRESS/CONFIRMED，C READY/CONFIRMED。ERP PR223 已核对远端并记录 MERGED。24/375 DONE、0/125 业务完成、247 NOT_RUN 与 41 历史引用不变。本次只检查派工资料和台账，不运行产品测试、Actions、数据库或部署。

## 2026-10-15：ERP 共享存储隔离与 EF 事务接入

已实现 ERP 8 张共享表的受保护登录→租户目录、最小权限 helper 与数据库行隔离，
并让内部 EF 接入器使用租约中的同一连接/事务；现有全局唯一键与只追加审计保持。
10 项相关本地验证通过，零失败、零跳过，使用真实 ErpIntegrationContext 模型和自有 PG18 中的合成 A/B 数据。
详见[本步骤交付](../development/evidence/WP02-02/erp-tenant-isolation/DELIVERY.md)及[8 表写入清单](../development/evidence/WP02-02/erp-tenant-isolation/OBJECTS.md)。
安装限显式调用的新布局；已有对象拒绝接管，重复初始化/升级/恢复仍待完整生命周期任务。
现有 handler/worker/业务路由未切换；真实宿主授权、Owner 最终门、其他上下文映射与原 17 PGT 仍待完成。
WP02-02 保持 IN_PROGRESS / CONFIRMED；24/375 DONE、0/125 业务完成及 247 NOT_RUN 不变。
自有 PostgreSQL 已停止，数据与失败记录保留。本步骤没有 Actions 触发/取消/配置变更、发包、既有业务迁库或部署。
下文保留历史时点；原“没有共享 RLS/helper”不代表本轮 ERP 局部组件完成后的状态。

## 2026-10-15：PostgreSQL 租户连接组件

已实现宿主内部的不可变租户绑定、受控目录、专用连接池和显式事务租约；
当前可信 scope、实际数据库/LOGIN、池容量及轮换排空在组件入口检查。
28 项相关本地单元和自有 PostgreSQL 18 验证通过，零失败、零跳过；
集中审查后修正取消异常脱敏，对应 1 项回归通过；原 28 项结果只复用未受影响范围。
数据库只使用本任务合成 A/B 表，自有实例已停止，数据及失败记录保留。
详见[本步骤交付](../development/evidence/WP02-02/tenant-connections/DELIVERY.md)。
四个实际 EF 模型的 328 张表已形成可携带清单，供后续映射使用；其中 317 张有直接租户列的表中，
307 张至少有一个未含租户列的唯一键，不能直接复制到各租户 schema 而削弱原唯一性。
完整 raw SQL/writer/Owner 清单、真实宿主授权、Owner 最终门、共享 RLS/helper、EF 缓存与映射及迁移恢复仍待完成。
组件尚未接入业务路由；WP02-02 保持 IN_PROGRESS / CONFIRMED，原 17 PGT 和业务验收保持 NOT_RUN。
24/375 DONE、0/125 业务完成及 247 NOT_RUN 不变。本步骤无 Actions 触发/取消/配置变更、发包、业务迁库或部署。
下文保留历史时点；旧“尚未实现租户连接”不代表本轮之后的组件状态。

## 2026-10-15：Main 正式采用 Platform 0.10.3 依赖

Main 的四处直接引用和 16 份真实恢复锁已更新，WebApi / 数据库探针构建通过，
CRM、ERP 合同与 PostgreSQL 模型的 151 项相关测试通过、零失败、零跳过。
正式包身份已核对并形成空能力/兼容性矩阵与 Unknown consumer lock；没有虚构运行采用。
详见[本步骤交付](../development/evidence/WP02-02/main-package-adoption/DELIVERY.md)。
WP02-02 从 BLOCKED 转为 IN_PROGRESS / CONFIRMED；本步骤后继续可信租户绑定和 PostgreSQL 存储，
真实 Owner、数据库事务/隔离与原 17 PGT 仍 NOT_RUN，完整任务未记 DONE。
仍为 24/375 DONE、0/125 业务完成；本步骤无 Actions、发包、数据库启动、迁移或部署。
下文保留历史时点；旧“Main 仍为 0.10.2”不代表本次采用后的当前源码。

## 2026-10-15：用户采纳新验收依据并批准一次正式发布

用户明确回复“同意两项”：采纳WP01-01-ACCEPTANCE-BASIS-1.2.0的579条前瞻义务，
并批准准确Platform源码1277da5e0a20c6d96f0a95d0e9b4f20e0008332a的一次0.10.3七包正式发布。
[授权与执行报告](../development/evidence/authorized-adoption-release-20261015/README.md)记录准确范围。
台账已选择原manifest；用户采纳决定已生效，独立信任配置尚未安装，业务仍NOT_RUN。
正式运行38709310767/attempt1成功；七包原字节、Windows/Linux Current结果和归档摘要已核对。
正式包缺失阻塞已解除；Main仍为0.10.2，consumer matrix/lock及真实PG采用留待下一窗口。
24/375 DONE及0/125业务状态保持；没有重跑、触发器变更、Main接线、迁库或部署。
下文“尚未授权”等陈述保留为相应历史时点，不代表本次授权后的现状。

## 2026-10-15：验收配置异常诊断修复（Issue #216）

验收工具先检查配置为JSON对象；数组和标量现在返回结构化拒绝，不再抛出AttributeError。
相关23项算法测试及原步骤复测通过，正常配置与拒绝边界保留；正式采纳和业务状态不变。
见[修复证据](../development/evidence/bug-216-configuration/README.md)；Issue在远端main核对后关闭。
未运行Actions、签名发包、数据库或部署。

## 2026-10-15：第八轮限定实现已合并

WP01-01正式台账adapter经PR217交付；WP02-02的Platform0.10.3登记经[PR](https://github.com/GTX537/CP6.Platform/pull/77)交付。
协调以独立PR76修复Pkcs审计阻塞，并修正两条过时断言；标准定向177项通过，其他有效结果按原范围复用。
两个局部窗口已关闭，完整任务仍PLANNED/BLOCKED与NOT_CHECKED；正式采纳、签名/发包、真实Main/PG及17PGT尚未完成。
24/375 DONE、业务0/125、247 NOT_RUN、41 REFERENCE_ONLY及历史缺件保持。
最终producer源码为`1277da5e0a20c6d96f0a95d0e9b4f20e0008332a`，不是已发布制品。详见[本轮协调报告](../development/evidence/wave8-integration-20261015/README.md)。
未触发/取消Actions、改变触发器、迁库、部署或新增派工。下文保留历史时点。

## 2026-10-15：全项目精简开发验证

用户要求当前及后续任务减少门禁和测试。Main、Platform、CRM统一采用相关验证、一次集中审查、一份报告及有效结果复用；旧贡献指南的默认全套检查/等待远程CI要求被当前规则取代。第八轮A/B验证同步精简，任务完成与业务验收状态不变。见[项目规则](../../AGENTS.md)和[并行约定](../development/09-parallel-development.md)。

## 2026-10-15：第八轮正式门与版本登记窗口

第八轮只开放A/WP01-01正式台账adapter/manager及完整采纳记录候选、B/WP02-02在Platform的0.10.3正式登记代码窗口。两个局部窗口CONFIRMED；完整任务仍PLANNED/BLOCKED与NOT_CHECKED，未收到START，不代表实际采纳或业务开工。C/D旧窗口关闭且不新增派工，WP02-28等待可信producer。24/375 DONE、S01 12/12、业务0/125、247 NOT_RUN、41REFERENCE_ONLY及历史缺件保持。无Actions、正式签名/发包、数据库/业务迁库或部署。 [执行包](../development/dispatch/20261015-wave8/README.md)；[准入证据](../development/evidence/wave8-dispatch-20261015/README.md)。下文保留历史时点。

## 2026-10-14：第七轮两项库实现完成、两项准备归档

WP02-12消息传输与WP02-26 SLO/精确断言/故障工具经集中审查、正常合并和远端核对登记DONE。累计24/375任务，S01为12/12；业务目标0/125、247运行NOT_RUN和41历史REFERENCE_ONLY保持。原始本地证据按准确输入复用，合流只做必要架构冒烟，详见[第七轮协调证据](../development/evidence/wave7-integration-20261014/README.md)。

WP01-01技术候选修复Issue211的六实体原始Id约束缺口后归档，主任务仍PLANNED/NOT_CHECKED，生产门与正式采纳关闭。WP02-02三入口完整依赖构建及私有宿主诊断通过，源码采用候选可审查；正式信任/生产采用尚未批准，主任务仍BLOCKED/NOT_CHECKED，17PGT仍NOT_RUN。前轮准备记录和无法恢复的原件缺口保留。

自有容器/PG已停止并保留数据/失败；协调未重启。无Actions运行/取消/触发变更、正式发包、业务迁库、部署或下一轮派工。下文保留历史时点。

## 2026-10-14：第七轮传输、验收工具与准入候选

第七轮沿用四对话：A/WP01-01完成DP决定候选与禁用完成门验证，B/WP02-02完整依赖构建/运行诊断；两主任务仍PLANNED/BLOCKED及NOT_CHECKED。C/WP02-12消息传输与D/WP02-26 SLO/故障注入READY/CONFIRMED。四独立窗口、旧准备交付和领取历史保留，未收到实际START。22/375 DONE、S01 12/12、业务0/125及247 NOT_RUN不变。只预约自有本地资源，本次未启动服务、数据库、Actions或正式发布/迁库/部署。 [执行包](../development/dispatch/20261014-wave7/README.md)及[准入证据](../development/evidence/wave7-dispatch-20261014/README.md)。下文保留历史时点。

## 2026-10-14：第六轮两项实现交付、两项准备归档

WP02-08 Outbox V2、WP02-25运行身份/健康/遥测经集中审查、四项P2修复和定向回归后正常合并并核对远端main，登记DONE。累计22/375，S01为12/12；业务目标仍0/125。初审缺陷、独立Issue分支、真实RED/GREEN、旧证据复用及共同主线架构冒烟详见[第六轮协调证据](../development/evidence/wave6-integration-20261014/README.md)。

WP01-01准备包与WP02-02消费准备包已合并：前者仍PLANNED/NOT_CHECKED，正式替代依据采纳及DP01–08待决；后者仍BLOCKED/NOT_CHECKED，旧正式包缺新能力、本地包未正式信任，17 PGT仍NOT_RUN。准备归档不计主任务DONE，不改变完成门、产品依赖或可信版本。247运行NOT_RUN、历史原件缺失及WP05 PROPOSED保持。

Outbox仅重启核实身份的专属PG55428用于缺陷回归，收尾停止并保留证据；无业务库操作。无Actions触发/取消/配置更改、正式发包、迁库、部署或新一轮派工。下文保留历史时点。

## 2026-10-13：第六轮两项实现与两项准入准备

第六轮沿用四对话：C/WP02-08 Outbox与D/WP02-25观测READY/CONFIRMED；A/WP01-01替代验收采纳准备仍PLANNED、B/WP02-02消费身份准备仍BLOCKED，两主任务NOT_CHECKED，只授予私有准备目录。未收到START，不把assignment当开工。20/375 DONE、S01 12/12、业务0/125及247 NOT_RUN不变。C仅预约新PG55428，本次未建库、触发/取消Actions、改触发器、正式发包、迁库或部署。 [执行包](../development/dispatch/20261013-wave6/README.md)及[准入证据](../development/evidence/wave6-dispatch-20261013/README.md)。下文保留历史时点。

## 2026-10-13：第五轮四项任务已交付，S01 基础合同收口

S01-12、WP02-27、WP02-07、WP02-11 已经集中审查、必要修正、正常 PR 合并及远端 main 核对，登记 DONE。累计20/375项，S01为12/12；真实业务目标仍0/125。

S01-12整理122向量、19修订和六组消费者责任，完整业务仍NOT_RUN。Inbox复用136个独立本地/隔离PG测试及4+2定向重复证据。WP02-27补齐两项精确资产断言，并修复Schema路径边界Issue60；HTTP修复拒绝响应正文故障与权限回调异常Issue62/63，保留真实失败和新回归。新运行、旧证据复用和共同主线架构冒烟详见[本轮协调证据](../development/evidence/wave5-integration-20261013/README.md)。

WP02-02仍被可消费的受信任包/源码身份阻塞；当前Main固定[0.10.2]不含新能力，本地测试包不解除门禁。下一步先明确采用身份及17项PG验收准入，再分配后续任务；本次未派新任务。247运行NOT_RUN、历史缺口和WP05 PROPOSED不变。无Actions运行/取消/触发变更、正式发包、迁库或部署。下文保留历史时点。

## 2026-10-13：第五轮四任务准入与租户采用制品门

第五轮沿用四对话分配S01-12采用证据、WP02-27包能力与信任、WP02-07同事务Inbox、WP02-11 HTTP安全重试，均READY/CONFIRMED，未收到START。WP02-02因Main可消费的可信ctx-binding制品缺口登记BLOCKED；WP02-27本地验证不自动解除发布/采用门。16/375 DONE、S01 11/12、业务0/125及247 NOT_RUN保持。C仅预约隔离PG测试资源，本次未建库、触发/取消Actions、更改触发器、迁库或部署。 [执行包](../development/dispatch/20261013-wave5/README.md)及[准入证据](../development/evidence/wave5-dispatch-20261013/README.md)。下文保留历史时点。

## 2026-10-13：第四轮四项基础任务已交付

S01-11 ERP授信合同、S01-10 PostgreSQL精确存储、WP02-01可信上下文、S01-07质量合同均经正常PR合并与远端main核对，登记DONE。累计16/375，S01为11/12；业务目标仍0/125。

S01-11在准入审查中修复Buffer字节别名Issue196，新TS428/428、类型检查和原步骤复测通过，原C#417/设计5复用。S01-10正式采用PG映射1.0.0及20字段无损数值尺度补充，政策尺度保持；原PG210+重叠57和生命周期1准确复用。WP02原502复用、新架构98通过；S01-07原657/130与类型检查复用。未执行整个合并组合或真实Owner/宿主/PG业务验收。

[完整协调证据](../development/evidence/wave4-integration-20261013/README.md)保留原候选、失败和历史PROPOSED身份；247运行NOT_RUN、ITER/REL缺口、WP05 PROPOSED不改。下一步可核S01-12及WP02后续任务准入，本次未派新任务。无Actions运行/取消/触发变更、迁库或部署。下文保留历史时点。

## 2026-10-13：第四轮四项任务已分配

沿用A/B/C/D分配S01-11 ERP-CREDIT、S01-10 PostgreSQL精确存储、WP02-01可信上下文、S01-07质量合同；均READY/CONFIRMED，独立工作树和写入窗口已核，尚无实际START。12/375 DONE、S01 8/12和业务0/125不变，247运行NOT_RUN。S01-10只预约隔离本地测试资源；本次未操作数据库、触发/取消Actions或部署。[执行包](../development/dispatch/20261013-wave4/README.md)与[准入证据](../development/evidence/wave4-dispatch-20261013/README.md)。

## 2026-10-12：第三轮四项合同任务已交付

S01-03授信金额、S01-04库存值/比例、S01-06 ATP标识及S01-08 CRM合同，均经审查、所属仓库正常合并及远端main核对，登记DONE并关闭本轮窗口。任务累计12/375（S01为8/12），不是工时或业务百分比。Main复用准确输入对应205/132/60+81及类型检查；CRM发现终态合同/Node环境阻塞后，以有版本的来源补充修正，实际C#186、Node22 Web142、importer6与类型检查通过，49原UI接受并往返。两仓回执与回归支持Issue189关闭，原候选/失败记录保留。

[完整协调证据](../development/evidence/wave3-integration-20261012/README.md)明确原运行复用与本轮新验证的范围。125业务目标仍0、247运行NOT_RUN及历史ITER/REL缺口保持；HTTP/Owner/PG采用另行推进。未自动分配下一轮，无Actions运行/取消/触发变更、迁库或部署。下文保留历史时点。

## 2026-10-12：CRM 终态合同漏同步修补（Issue #189）

第三轮接收发现 CRM 候选按旧 Schema 要求 99 终态公开到期时间非空，与已接受 Wire 修订冲突。新增明确版本 [CRM-INQ-TERMINAL-PROJECTION/20261013-R1](../design-handbook/remediation/addenda/CRM-INQ-TERMINAL-20261013-R1/README.md)，仅补齐两个遗漏节点；原归档、组合资产、输入包和固定向量均保留。静态检查及定向审查通过，真实消费者采用尚待完成。

[Issue #189](https://github.com/GTX537/CP6/issues/189) 保持 Open，S01-08 暂不登记 DONE；需要显式版本采用、根与页面嵌套 99 时序回归及受支持 Node 22 环境验证。来源修补不代表 HTTP、授权、数据库或业务验收完成。共享台账将在本轮协调交付统一同步；无 Actions 运行、取消、触发变更、迁库或部署。下文保留历史时点。

## 2026-10-12：第三轮四项合同任务已分配

沿用A/B/C/D分别分配S01-03授信金额、S01-04库存值与比例、S01-06 ATP稳定标识、S01-08 CRM原生合同。均READY/CONFIRMED，真实独立工作树已建，等待实际START；8/375 DONE与0/125业务验收不变。CRM发布前置独立完成普通工作流仅manual及严格既有批准附注兼容。未运行Actions、数据库迁移或部署。[任务包](../development/dispatch/20261012-wave3/README.md)及[证据](../development/evidence/wave3-dispatch-20261012/README.md)。

## 2026-10-12：第二轮三项合同代码已交付

S01-02严格JSON/NullOnly/结构相等、S01-05前端精确值、S01-09平台准确路由/原字节均经独立集中审查及各自远端main包含性核对，登记DONE并关闭本轮写入窗口。复用准确源码对应的77/202/110项成功结果及前端类型检查，没有把证据复核改称新测试运行。任务总计8/375（计数，不是工时或业务完成率），业务目标仍0/125，247运行NOT_RUN及历史ITER/REL缺口保持。

Main PR184/185、Platform PR58分别交付；平台包尚未发布，真实Owner/数据库/页面采用与生产验收仍独立进行。下一轮可核对S01-03/04/06/07/08/11，未自动领取。无Actions触发/取消/配置变更、迁库或部署。 [完整协调回执](../development/evidence/wave2-integration-20261012/README.md)。

## 2026-10-12：第二轮代码候选接收与协调审查

COORD已实际读取S01-02、S01-05、S01-09的START和DELIVERY，三项登记VERIFYING；不回填此前协调ACK。候选分别报告77、202、110项本地检查，本轮独立核对实际源码、固定向量及测试证据后再正常保护合并。共享状态串行维护，远端main包含前不登记DONE。业务目标0/125及247运行NOT_RUN保持。 [固定接收记录](../development/evidence/wave2-integration-20261012/intake.json)。

## 2026-10-12：WP01-02派工设计交付确认

WP01-02经PR182及真实远端回执登记DONE；A/B/C的S01-02、S01-05、S01-09保持READY并等待实际START，D本轮未分配。共享台账继续由COORD串行处理；业务目标/运行状态及WP05采纳未提升。 [交付与复用](../development/evidence/WP01-02/README.md)。

## 2026-10-12：第二轮三个代码任务准入

COORD接手WP01-02汇总/派工；沿用A/B/C分别分配S01-02、S01-05、S01-09，真实独立工作树已创建，当前READY并等待START，不宣称已经开工。D保留原验收准备。本轮未写业务实现，未运行Actions/迁库/部署；业务目标和运行场景状态不变。 [任务包与写入窗口](../development/dispatch/20261012-wave2/README.md)。

## 2026-10-12：首轮四任务远端交付确认

首轮四项设计/准备均有真实远端交付：S01-01保持DONE，S02-00、WP05-01、WP14-01依据独立审查、修正及PR178/179/180回执登记DONE，资源窗口关闭。S02补齐UPDATE双约束；WP05补43项来源映射仍PROPOSED。业务目标0/125、247运行NOT_RUN及两历史缺口保持；未运行Actions、迁移或部署。后续候选需另行核对与领取。 [完整协调回执](../development/evidence/first-wave-20261012/README.md)。

## 2026-10-12：WP05-01 设计准备接收与集中审查

COORD实际读取Handoff固定消息后在当前时点确认领取/交付窗口并登记VERIFYING，不回填虚构早期准入。ITER/REL版本化替代依据与覆盖矩阵；不可恢复旧30/48仍保留缺口，新依据的正式采纳和WP01-01完成门独立处理，运行NOT_RUN。 [接收依据](../development/task-deliveries/WP05-01/evidence/coordination/intake.json)。本轮不改变125目标交付状态、原清单缺口或247运行场景；无Actions、数据库迁移或部署。

## 2026-10-12：WP14-01 设计准备接收与集中审查

COORD实际读取Handoff固定消息后在当前时点确认领取/交付窗口并登记VERIFYING，不回填虚构早期准入。125目标、13404原验收关联、247修订场景及599消费者的验收准备；当前源子集未变，保留固定快照，所有运行NOT_RUN。 [接收依据](../development/deliveries/WP14-01/evidence/coordination/intake.json)。本轮不改变125目标交付状态、原清单缺口或247运行场景；无Actions、数据库迁移或部署。

## 2026-10-12：S02-00 设计准备接收与集中审查

COORD实际读取Handoff固定消息后在当前时点确认领取/交付窗口并登记VERIFYING，不回填虚构早期准入。共享 schema + 租户业务 schema、专用角色/连接池与事务恢复设计；27个运行场景保持NOT_RUN。协调审查补齐UPDATE旧行USING及PGT-008反例。 [接收依据](../development/evidence/S02-00/coordination/intake.json)。本轮不改变125目标交付状态、原清单缺口或247运行场景；无Actions、数据库迁移或部署。

## 2026-10-12：S01-01 精确合同输入包已交付

用户确认对话A兼任本任务协调，已登记领取及准入。[输入包](../../eng/contracts/s01/README.md)固定33份来源、16个profile和122条共用向量，原23条数值/单位预期完整保留；61次原件/派生哈希检查通过。S01-01经集中审查、PR #176正常合并和远端main包含性核对后登记DONE，详见[交付记录](../development/evidence/S01-01/delivery.md)。

本次仅合同资料与静态检查，业务交付仍0/125、247运行NOT_RUN；未编译业务、运行数据库、触发Actions或部署。S01-02/05/09可基于已交付版本分别核对准入并领取；其他对话与任务没有自动启动。下文保留历史时点。

## 2026-10-12：采用 Handoff 作为开发对话通信通道

用户指定 CP6.AI-Handoff 供各对话直接沟通；[协作入口](../development/09-parallel-development.md)已接入本轮独立资料分支，启动、依赖、进度与交付通过固定提交回传，协调对话读取答复，用户不必逐条转述。唯一开发状态仍为 roadmap.json；Handoff ACK 不替代领取准入、业务验收或远端主线交付。

本轮仅建立通信约定，不代四对话领取任务或声明已送达/开工；发布文件不自动唤醒休眠对话。保留历史设计和接受记录、PostgreSQL 方向及全部 NOT_RUN 范围。验证范围为文档差异、引用和通信入口远端回读；无业务构建、Actions、数据库或部署变更。下方为先前时点快照。

## 2026-10-12：全量任务拆分与多对话分配

已把14个工作包展开为375项任务（356项实现、10项设计/协调、9项完整验证），125目标都有所属工作包的主体实现覆盖。[工作包任务索引](../development/10-work-package-tasks.md)进入全部卡片，[任务进度](../development/07-detailed-tasks.md)读取唯一台账；[多对话协作](../development/09-parallel-development.md)提供领取、共享文件窗口、跨仓交付和复制交接模板。

首轮候选为S01-01精确合同、S02-00 PostgreSQL多租户设计、WP05-01工程历史验收替代设计、WP14-01全链验收准备。当前全部PLANNED、未领取、准入NOT_CHECKED，未创建用户对话或启动业务实现。任务数量不是工时百分比；首轮之后按已交付能力及资源放行，不能让每个对话直接独立改同一模型/迁移/公共合同。

本轮补齐提供者与实际消费者分工，核对依赖无环、125目标覆盖和来源；台账工具增加定义一致性、领取/资源冲突及关闭门，验证和审查见[本地证据](../development/evidence/full-breakdown/README.md)。原125目标/13,404验收关联保持；完整原验收仍123/125，ITER/REL不可找回记录保留；业务交付0/125、247运行NOT_RUN、41静态REFERENCE_ONLY。唯一后续数据库PostgreSQL，无业务编译、数据库/数据迁移、Actions或部署。下方为先前时点记录。

## 2026-10-11：S01细拆与PostgreSQL方向

已形成[12项S01任务及S02-00多租户设计前置](../development/07-detailed-tasks.md)，每项绑定仓库、依赖、文件、正反验收和交付证据。当前全部PLANNED，业务交付仍0/125、247运行场景NOT_RUN；WP01原验收历史缺口状态不变。下一项为S01-01冻结采用输入，随后严格JSON和CRM合同子集。

用户明确后续开发只使用PostgreSQL，选择原因包括schema多租户；[数据库决定](../development/08-postgresql-baseline.md)已写入AGENTS。优先评估schema布局，但未批准全库切换；保留TenantId/授权，S02-00先明确共享表、可信schema路由、权限、连接池、逐租户迁移和恢复。旧双库事实保留历史范围，新增任务不再要求SQL Server。此次只有计划/台账工具及相关本地检查，无业务编码、迁移、Actions或部署。

## 2026-10-11：WP01源码差异与原验收盘点

125/125目标已有固定源码差异判断：105项需改造、20项需新增核心用例。原验收清单完整123/125；PLM-ITER与PLM-REL保留不可恢复的历史原表缺口，WP01整体仍为ASSESSING。

登记13,404条目标—来源验收关联，其中原始AC/TC 8,456条、必需补充校验/连续场景 4,948条。共享条目挂到每个负责目标，不能把这个合计当成全域唯一业务测试数。全部NOT_RUN；247条修订运行场景也保持NOT_RUN，41条历史静态检查仍仅REFERENCE_ONLY。

已形成[逐目标评估与安排](../development/05-implementation-assessment.md)和[首批切片](../development/06-first-slices.md)。下一步从S01精确值/身份采用开始，随后真实事务/授权门、CRM入站；ITER/REL另做有版本的替代验收设计，不阻塞无关切片。本轮业务交付仍0/125，无业务运行、数据库迁移、Actions或部署。下方为前一时点快照。

## 2026-10-11：全量开发大任务与进度台账

用户确认先评估全部开发范围、拆大任务、按实际情况安排批次并持续追踪。基于主线`90ea752e9029d171e6a0307fcda06c66c3e04a69`建立[开发总台账](../development/README.md)：14个大任务、125目标唯一归属，247条修订运行场景按唯一ID登记，41条历史静态检查只作引用。入口盘点固定Main、CRM、Platform准确源码；有代码入口不等于新设计符合性已证。

当前WP01评估中；125目标逐项实现差异与原验收清单仍待确认，247条修订运行验收NOT_RUN。0项本轮已确认交付不表示既有代码为零，也不据目标数量编造工时百分比。后续每次开工、阻塞、验收和交付同步JSON及生成视图；活动BUG继续以GitHub Issues为准。没有业务编码、数据库迁移、Actions或部署；本轮仅文档和本地台账工具，验证范围见[结构检查](../development/evidence/ledger-validation.json)。

## 2026-10-11：编码前设计修订 R1

按用户批准的全面补全计划，在 `codex/design-completion-20261011`、已确认主线基线 `0ed4e0e1f766273f4203ef98687f6a05632225d7` 形成[设计修订R1](../design-handbook/remediation/README.md)。115个原登记编号去重为114个问题族，125目标全部有覆盖；分类为10设计缺口、45资产矛盾、32已有设计待实际采用、13有意差异、11准确后继和3陈旧入口。每项有原证据、明确决定、消费者、修订输出和验收；288条验收记录中247条为待执行的业务/采用场景，41条是本次静态资产检查记录，不能互换。

新增ACC/ORD/DEM物理合同、组合事务/恢复、MATCH/AP纠正close、MAT/MRP双模式数量映射、ITER/OA与SPACE/S5及ENG多输出消费合同；同步无损金额/单位/null类型及复杂连续样例的完整摘要链。125模块当前入口和导航生成依据同步，原六台账/原归档/历史接受保持。R01071无法找回、补找行动关闭，历史证据缺口仍在。

检查覆盖文档链接、原SHA/有界补丁/派生摘要、Schema及局部类型编译，保留初次失败与准确复用范围；详见[本地设计验证](../design-handbook/evidence/design-completion-validation.json)。实际Owner/全写者采用仍UNPROVEN，业务/数据库/端到端验收NOT_RUN，未编码业务、迁移或部署。后续按[编码交接](../design-handbook/remediation/09-implementation-handoff.md)推进；本轮按普通受保护PR/main流程交付，远端包含性和本地快照以实际交付回执为准，不用本地文件数量冒充远端完成。

## 2026-10-10：CP6 归档设计整理，本地资料交付

在独立分支 `codex/design-docs-consolidation-20261010`（基线 `90c871fe571fd6b390f53e8678376d7ce60bcb60`）整理本地归档14,540文件，形成[开发设计手册](../design-handbook/README.md)及125模块正文、跨域合同、附册、准确来源/阅读/缺件账。124目标必要材料已整理；WMS-DEL现有必要内容已整理，R01071指定独审原件于2026-10-11由用户确认无法找回，关闭补找行动并保留证据缺口，未冒充齐备。设计采用、实现、运行、历史未读及缺件分别列示。

任务级集中内容审查及定向修正完成；文档静态检查包括125模块、105 JSON、4 CSV、19,774本地链接/行号，零错误/警告（计数为该次真实执行；后续纯交付证据另行解析）。原档案最终路径/大小/mtime与初始全SHA清单相同，交付副本逐文件核SHA。未运行业务/数据库/编译/Actions，未部署。

用户本地成果位于 `D:/CP6/docs/CP6_开发设计文档_20261010`，展开原件位于 `D:/CP6-archives/consolidation-20261010`。远端仓库仍PUBLIC；用户已在获知这一点后明确授权上传整理成果，按正常PR/main交付，以实际远端记录核对完成，不把本地提交冒充已合并。原归档与展开对象不纳入本次上传，根工作区既有文件保留。详见[整理交付报告](../design-handbook/整理交付报告.md)。

## 2026-10-04：CP6 中文交互项目地图

新增 [Archify 项目地图](../architecture/2026-10-04-cp6-map/README.md)：六张中文交互图分别解释整体运行、业务职责、Space、双库请求链、打包运行与发布推广；固定依据主线 ebcbc4998faa7fee8e38db861753c6e90bdc100a，附解决方案21项目和全部48个已跟踪C#项目清单。六图的showcase结构/制品/Chrome浏览器检查全部通过，入口页桌面与移动宽度、12个既有导航目标通过；总览及数据库图另做截图抽查。准确验证范围及制品哈希保存在同目录verification.json。

图说明源码结构与配置条件；没有更改业务代码、数据库、现有测试前后端、工作流触发或生产环境。API Dockerfile的PostgreSQL restore输入清单保留为静态待专项验证项；本次图检查不替代业务、容器或现场验收。

## 2026-10-04：CP6DB 真实数据的 PostgreSQL 本机测试副本

接续已完成的双库兼容改造，用户单独授权了真实数据本机迁移。原 SQL CP6DB 331 表、67,774 行，COPY_ONLY/CHECKSUM/VERIFYONLY 后独立 SQL 副本前向初始化为354表、67,810行；原库仍保留旧129/47迁移历史，schema/业务数据和既有连接均未修改。PostgreSQL新库330张应用表、67,620行，354张完整源归档表；684项映射逐行摘要多重集与行数全部匹配，181个外键及6个identity序列已校验，0禁用触发器/未验证约束。

运行配置、原始备份、失败日志和业务字段只在本机忽略目录；复用原e940825 API的261文件，2,010相关Gitblob未变，另已保留独立本地发布物。PostgreSQL目标长期保留，API只使用独立端口并禁用hosted services/外联。[本地运行手册](../devops/CP6DB-POSTGRESQL-LOCAL-COPY.md)说明入口、归档和范围。两条历史事件的原时区尚待用户确认，当前副本UTC派生字段仅按UTC暂定解读，原CreateDate完整保留。未执行远程Actions、修改生产门禁或生产部署。

## 2026-10-04：双库兼容版已交付并通过合并后冒烟

[PR165](https://github.com/GTX537/CP6/pull/165)已正常保护合并到远端main 8b64bc8a510e9ad4a82c21c8c6ac13c138e332c3；候选4243c67d7ac2e3cfafb809d2a4efba54f4ec927a是其ancestor，完整树68f7d4accfc58897a6bb430f5f90dded50c4ba94相同，2,389个PR文件均完成分页与Git blob核对。原洁净实现e940825fc7511b41a1a80b4b73897dcc69e51bbb的双库105个适用入口、当前配置/接线139项、56项HTTP与真实原生独立恢复均通过，所有31个正式库已清理。2,091份原始证据与Git index字节一致，失败及复用来源保持。

合并后使用同一原261文件发布物，SQL/PG分别在新的独占库实际初始化、核对history、启动所属API及三个health端点、登录与12项权限/改密/401/403检查；HTTP新建用户均从确切owned库实际查得。两个新库普通DROP并核对不存在；这是新冒烟，不冒称重建、重跑完整矩阵或生产验收。初次PG冒烟脚本属性读取丢失SSL覆盖的失败保留，纯内存复现后改用正式runner的set/get方法；原Planned目标实际不存在、receipt与Failed summary保持，不改变CP6项目代码或TLS生产规则。155份交付/冒烟原件已逐份核对归档。

六工作包的功能、适用验收和代码交付已闭环，结案证据与运行入口见[最终验收及交付](../audits/database-compatibility/WP6-ACCEPTANCE.md)；父任务活动状态以[Issue134](https://github.com/GTX537/CP6/issues/134)为准。父任务只在本次结案文档正常合入并核对远端完整包含性后关闭，下方历史“未完成/guard保留”等记录不代表当前运行能力。普通PG API/worker现可使用所选Provider；没有现有业务数据搬迁或生产部署。

推送/PR/合并前分别核对17个工作流/依赖输入，队列均0，无远程Actions运行/取消、触发/保护变更或管理员绕过。根工作区用户既有改动保留；正式R2/GHCR候选、签名/扫描、审批与SQL生产基线不变。

## 2026-10-04：WP6 最新本地验收全部通过，远端交付待完成

洁净源码 e940825fc7511b41a1a80b4b73897dcc69e51bbb 的最新 wms-local-transaction-fix 四次实际分项全部Passed：PG Matrix34/34、SQL Matrix37/37、两库Application各17/17，共105个适用入口、零失败/跳过；不是单次Full。当前程序集补充配置/四Context接线/生产校验139/139，零跳过。两库使用同一份当前实际发布API（261文件逐字节一致），真实独立恢复专项核对56项HTTP、8次API启动、所有表/行/序列/history/持久key与恢复后消息一次投递/幂等重放通过；本轮31个owned临时库全部普通DROP且AbsentVerified。

八项完成条件的本地范围已逐项绑定实际原件及断言源位置，旧失败/成功及原审查时点保留。一次集中审查与定向修复/清理/WMS整合复核无剩余实质阻断。WP6公共证据归档/index核对、正常PR/main交付、合并后冒烟与远端包含性仍待完成，父Issue134继续Open；不能因本地全部通过就宣布任务关闭。实际版本、源码/产物哈希、复用来源及范围见[最终本地验收](../audits/database-compatibility/WP6-ACCEPTANCE.md)，下方旧状态均为历史时点。

普通PG API/worker临时guard已移除，仅在同一部署使用所选Provider及连接；旧记录中“guard保留”是当时状态。R2/GHCR正式门禁和SQL生产基线不变，未启动/取消远程Actions、修改触发/保护、切换既有环境或部署生产。已有SQL数据搬迁、同实例混库及外部CRM全面改造不在此目标内。

## 2026-10-04：BUG164 已交付，WP6 接入当前版本验收

[BUG164](https://github.com/GTX537/CP6/issues/164) 已由 [PR164](https://github.com/GTX537/CP6/pull/164) 正常合入远端 main `6322ac152cab7d462719169fb0301f49e9ee39ba` 并关闭，WP6 已接入。WMS 原八项＋新增十七项两库各25/25、零失败/跳过；合并后两库各3/3。两个修复测试库及原 WP6 SQL 失败库均已普通 DROP 并确认不存在；SQL 实际持续会话超时拒绝、短暂会话自行结束后清理通过，原 Failed summary 与 core/WMS TRX 字节保持。[专项记录](../audits/2026-10-03-bug-164-wms-local-transactions/README.md)保留111份原件及36份交付、冒烟、清理原件。

当前必需清单只扩展 WMS 为25项，其他61入口不变。最终入口仍为 PG Matrix34、SQL Matrix37、两库 Application各17，共105个适用入口；使用新的 `wms-local-transaction-fix` 运行目录。编译输入已改变，需当前代码七项构建及新的完整双库验收；后续 SQL 可在源码与完整运行文件逐字节一致后复用 PG 的真实构建，两库应用使用同一新发布产物。此前 PG Matrix34/34、旧发布物应用17/17保留历史来源，不代称当前最终验收。WP6 与父任务#134仍未完成；最终恢复审计、集中审查、归档与正常远端交付待完成。没有启动远程 Actions、改变生产门禁或部署既有环境。下方记录保留历史时点。

## 2026-10-03 历史快照（BUG164 合并前）：WMS 事务修复本地通过，交付待完成

最新进展（2026-10-03，BUG164 Open）：WMS LPN 四处和 Label 两处自建 ambient 事务已修复为独立调用时的 DbContext 本地 ReadCommitted 事务，保留调用方已有本地事务所有权及预打开连接的 ambient Required/abort 语义。有效种子 RED SQL17=3P/14F、PG17=10P/7F；修复后两库原8＋新17各25/25，零失败/跳过，2492源码输入与同一构建运行文件核对，一次事务专项审查无P0/P1/P2阻断；111份公共原件及原始/中间失败保留，见[BUG164本地证据](../audits/2026-10-03-bug-164-wms-local-transactions/README.md)。正常远端交付、合并后冒烟及三份自有库（修复两库与原WP6 SQL一库）清理尚待完成；父Issue134/WP6仍Open，最终双库Matrix/Application与恢复验收需要适用修复后的源码。未启动远程Actions，未变更生产门禁或部署环境。

## 2026-10-03：BUG161 已交付，WP6 继续最终双库验收

WP1–WP5 已交付；BUG153/155/157/159/161 分别由 PR154/156/158/160/162 正常交付并关闭。最新主线 `ce49012d37da4b1b01f074d5b5b7fd227780e656` 已接入 WP6。BUG161 修复 CAD Provider 配置替换的完整事务恢复：最多三次回滚/释放/重读，保留版本冲突、幂等重放和证据不可变，拒绝借用调用方事务或保存其待保存更改。修复前协调 PG 0P/2F，均实际40001；修复后两库原15＋新十项各25/25，补充 InMemory17/17；0 warning/error、一次专项审查无阻断。合并后两库各3/3，PG协调场景再次实际40001后恢复，两个自有库已普通DROP并核对不存在，Issue161已Closed，见[专项记录](../audits/2026-10-03-bug-161-cad-provider-recovery/README.md)。

原 WP6 claim-fix Matrix 在28个入口通过后 CAD/资产/协作14P/1F保持失败；该次七个PG临时库已清理，原summary哈希不变。当前接入修复，将新增十项纳入CAD组25，在新cad-provider-fix目录及当前发布物完成最终双库Matrix/Application、恢复、归档及正常交付。此前两库各17/17应用是旧发布物历史结果。父任务#134仍Open，WP6未完成。

所有执行仅本地，Actions启动/取消均0，无触发或保护变更、强推或生产部署。根工作区原改动保持，下方记录保留历史时点。

## 2026-10-03 历史快照（BUG161 合并前）：BUG161 CAD 配置替换本地回归通过，交付待完成

[Issue #161](https://github.com/GTX537/CP6/issues/161) 由 DB-COMPAT-01 WP6 最终 PostgreSQL 矩阵发现：28 个入口通过后，CAD/资产/协作原 15 项为 14P/1F，实际 40001。独立分支从远端 main 4a654320 建立，补充完整事务的最多三次恢复、回滚/释放/重读及调用方事务/未保存更改保护。修复前协调回归 0P/2F（均实际 40001）；修复后两库原 15＋新十项各 25/25、零 skip，补充 InMemory 路由 17/17；0 warning/error，SQL 复用同一已绑定产物。一次专项源码审查无 P0/P1/P2 阻断，71 份公共原件按字节归档；正常远端交付、合并后冒烟、两库清理和 Issue 关闭仍待完成，详见 [BUG161 证据](../audits/2026-10-03-bug-161-cad-provider-recovery/README.md)。WP1–WP5 已交付；WP6 与父 Issue134 保持未完成。原失败及所有历史交付记录保留。没有远程 Actions 或生产部署。

## 2026-10-03：BUG159 已交付，WP6 继续最终双库验收

WP1–WP5 已交付；BUG153/155/157/159 分别由 PR154/156/158/160 正常交付并关闭。最新主线 `4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0` 已接入 WP6。BUG159 精准恢复任务领取的 attempt-number 唯一冲突，保持五次上限、未知异常传播及取消；两库原17＋新6各23/23，零skip，构建/一次集中审查通过。合并后两库原并发和协调场景各2/2，PG实际23505后恢复，两个自有库已普通DROP/不存在核对，见[专项记录](../audits/2026-10-03-bug-159-space-job-claim-recovery/README.md)。

原WP6 Matrix在26个入口通过后出现的任务领取16P/1F保持失败；该次七个PG临时库已清理，原summary哈希不变。此前两库各17/17应用验收是旧发布物的历史结果。WP6接入修复，将新六项纳入任务组23，并在新的claim-fix目录/当前产物完成最终双库Matrix与Application、证据归档及正常交付。父任务#134仍Open，WP6未完成。

所有执行仅在本地，Actions启动/取消均0，无触发/保护变更、强推或生产部署。根工作区原改动保持；下方文字保留各历史时点。

## 2026-10-03 历史快照（BUG159 合并前）：Space worker 领取兼容修复双库本地通过，待交付

WP1–WP5 已交付；BUG157 已由 PR158 正常合入远端 main `5587a2a67ae73715596ca1a135b5863005abac8d` 并关闭，合并后两库冒烟与自有库清理完成。WP6 新 PG Matrix 已通过 26 个入口，随后任务领取并发出现原生 23505，原失败保留。

本次独立修复精准恢复 attempt-number 唯一冲突，并沿用五次完整事务重试、未知错误传播与取消。SQL Server / PostgreSQL 原任务/处理/生成/保留 17 项加新六项各 23/23、零 skip，同一源码/产物，locked build 零 warning/error。实际证据和限制见 [专项记录](../audits/2026-10-03-bug-159-space-job-claim-recovery/README.md)。一次集中审查无实质阻断，62份公共原件按字节归档；正常远端交付、合并后冒烟及两库清理仍待完成；整体 WP6 最终双库矩阵、新应用恢复验收和交付仍待完成，父任务 #134 保持 Open。

没有启动/取消 Actions、修改工作流/保护或部署既有环境；下方旧状态保留各历史时点。

## 2026-10-03：BUG157 已交付，WP6 接入最新主线继续验收

WP1–WP5 已交付，BUG153/155/157 已分别由 PR154/156/158 正常交付并关闭。最新确认主线为 `5587a2a67ae73715596ca1a135b5863005abac8d`，WP6 已接入。BUG157 的楼层完整事务恢复，两库精确克隆各 23/23、设计/发布各 41/41、构建和一次集中源码审查通过；合并后原并发及两个协调场景各 3/3，PG 实际40001后恢复，两个自有库普通DROP且确认不存在，见[专项记录](../audits/2026-10-03-bug-157-floor-initialization-retry/README.md)。

此前两库各17/17应用分项是历史发布物的适用结果；最终PG矩阵先后出现BUG155设计/发布34/35、BUG157克隆15/16失败，原件保持。生产依赖改变，WP6将新增七项纳入克隆清单，并以新执行目录/产物完成最终双库Matrix与Application、证据归档、所有自有库清理及正常远端交付。父任务#134保持Open，WP6未完成。

全部本地执行，Actions启动/取消均0，无触发/保护变更或生产部署。根工作区原文件保持；下方合并前文字保留历史时点，以本节与GitHub当前状态为准。

## 2026-10-03 历史快照（BUG157 合并前）：BUG155 已交付，楼层并发修复双库本地通过

WP1–WP5 已交付；[BUG155](https://github.com/GTX537/CP6/issues/155) 已由 [PR156](https://github.com/GTX537/CP6/pull/156) 正常合入远端 main `2e1f90c629340944d95fadd7aee428f639304ed6` 并关闭，两库原并发/协调冒烟及两个自有库清理完成。WP6 接入后的新 PG Matrix 前 24 个入口通过，楼层并发入口原 16 项中 15P/1F，实际 native 40001，已另登记 [BUG157](https://github.com/GTX537/CP6/issues/157)。

BUG157 在该主线的独立分支补全创建楼层的三次有界完整事务恢复，保留不同 key 的版本冲突和同 key 的合法回放，并拒绝保存/清除调用方状态。新增两项真实 PG 40001 协调回归及五项调用方/恢复控制；两库精确克隆各 23/23、设计/发布各 41/41，零 skip，本地构建与一次集中源码审查通过。当前为本地验证完成、远端待交付，见[专项记录](../audits/2026-10-03-bug-157-floor-initialization-retry/README.md)。原 WP6 失败和首次测试编译/数量门禁失败均保留。

BUG157 正常交付、合并后原步骤复测和两库清理，以及 WP6 最终双库 Matrix、新发布物 Application、归档和远端交付仍待完成；父任务 #134 保持 Open。没有启动/取消 Actions、修改触发/保护或部署既有环境；下方旧状态保留各历史时点。

## 2026-10-03：WP1–WP5/BUG155 已交付，WP6 接入最终验收

WP1–WP5 已交付；BUG153 经 PR154、[BUG155](https://github.com/GTX537/CP6/issues/155) 经 [PR156](https://github.com/GTX537/CP6/pull/156) 正常合入远端 main，最新确认主线为 `2e1f90c629340944d95fadd7aee428f639304ed6`。BUG155 的 Space 校验完整事务恢复，两库各 9/9 校验、各 41/41 完整设计/发布通过，集中审查无实质阻断；合并后原并发/协调并发各 2/2 和两个自有库普通清理完成，Issue 已关闭，见[专项记录](../audits/2026-10-03-bug-155-space-validation-retry/README.md)。

WP6 已保存自有草稿并接入上述主线。修复前正式 Application 两库各 17/17、真实 SignalR 和原生恢复通过，原最终 PG Matrix 在 25 个入口后遇到 BUG155 的 34/35 失败；这些历史结果保持。生产程序集改变，新增六项将纳入必需清单，并以新构建/目录完成最终两库 Matrix 和 Application、证据归档、自有库清理及远端交付。父任务 #134 保持 Open，WP6 未完成。

全部本地执行；Actions 启动/取消均 0，无工作流/保护变更或生产部署，根工作区原改动保持。下方记录保留历史时点，以本节与 GitHub Issue 当前状态为准。

## 2026-10-03：WP5 本地验证与集中审查完成，待远端交付

[PR149](https://github.com/GTX537/CP6/pull/149)已正常合入远端main `c6c662f5b744b44a51427e26fb2faff96472fcc2`，包含候选`cfe479124fb04d770462b874bc2a70508a8bf79e`且完整树相同。合并后原业务快照保存SQL/PG各2/2、零skip（含owner/迁移setup）；六个WP4自有临时库已普通DROP并确认不存在，receipt和PG角色保留。[交付/清理原件](../audits/database-compatibility/wp4-native/post-merge/manifest.json)新增17份，[父任务更新记录](../audits/database-compatibility/wp4-native/post-merge/parent-progress/manifest.json)另三份；原211与七份manifest及各次失败/复用范围保持。

WP5当前为`LocalVerifiedRemotePending`：独立`codex/db-compat-wp5-20261003`分支已完成Space及报表/GDPR相关本地验证、一次任务级集中审查与发现问题的定向修复。克隆SQL14/14及PG分批限定通过、报表4/GDPR1/Core Space5、容量/文件13、带数据报表4和清理故障回滚1均保留原双库执行来源；AI应用原13两库各13通过，设计/发布35及任务/基础约束分批通过。CAD Retry与克隆同key并发定向两库各5/5、SQL独立及历史迁移6项、PG两种安装顺序/脚本重复/Space-only各1/1，详见[WP5实施记录](../audits/database-compatibility/WP5-IMPLEMENTATION.md)与[首轮原件](../audits/database-compatibility/wp5-native/initial/manifest.json)。

集中审查两项P2均已修复并定向复核：PG AI原13＋caller-pending回归1＋CAD1实际15/15，legacy恢复选择器2/2，均零skip；前置SQL生成断言修正后的同8项8/8另按原来源保留，不与首次执行相加。[BUG150](https://github.com/GTX537/CP6/issues/150)已由[PR151](https://github.com/GTX537/CP6/pull/151)交付远端main `522433a370c0c247a98b300c69e88e403217d53a`并关闭，合并后原并发两库各2/2及自有库清理完成，见[BUG150交付原件](../audits/2026-10-03-bug-150-mapping-idempotency/native/post-merge/manifest.json)。WP5在`97e4a9f2e0881c4f423111848632bbc821cde11d`纳入该依赖后，实际整合构建0warning/0error，映射SQL/PG各4/4通过；[审查修复与整合原件](../audits/database-compatibility/wp5-native/review-followup/manifest.json)保留真实RED、异常包装断言失败及后续成功，不改写历史结果或累加重复测试。

WP5正常提交/PR合并、必要合并后冒烟及远端main包含性核对仍待完成；[父Issue134](https://github.com/GTX537/CP6/issues/134)保持Open，WP5/6远端阶段未勾选。WP6完整应用启动、初始化及原生备份恢复验收尚未执行，普通PG API/worker guard保留；本地WP5通过不代替WP6或整体兼容交付。

Actions启动/取消均0，无工作流/保护变更或生产部署。根工作区原改动保持，早期Pending/Open文字保留各历史时点，当前以本节和真实Issue读回为准。

## 2026-10-03 历史快照：映射保存并发修复本地通过，待远端交付

[BUG150](https://github.com/GTX537/CP6/issues/150)在WP5对照测试中确认原SQL Server同键并发死锁，PG对应路径唯一约束冲突未回放。独立修复只调整CAD/Excel映射保存的自有事务恢复，保留调用方待提交状态和事务。真实SQL/PG各4/4、原映射相关12/12、零skip，最终构建零warning/error，一次集中代码审查无实质阻断；[实际证据与边界](../audits/2026-10-03-bug-150-mapping-idempotency/README.md)保留原RED。

本段为PR151合入前的历史LocalVerified/RemotePending记录：当时Issue仍Open，正常合并、远端包含性和原步骤冒烟待完成；现BUG150已交付并Closed，实际主线、双库冒烟及清理见上方和[交付原件](../audits/2026-10-03-bug-150-mapping-idempotency/native/post-merge/manifest.json)。WP4已由PR149交付，WP5草稿保留在另一独立工作树，父任务134与WP5/6继续。无Actions启动/取消、工作流/保护变更或生产部署；根工作区原改动保持。下方历史状态保留原时点。

## 2026-10-03 历史快照：WP4 本地双库验收与集中审查通过，待远端交付

[BUG147](https://github.com/GTX537/CP6/issues/147) 已经 [PR148](https://github.com/GTX537/CP6/pull/148) 正常合入远端 main `974e57c0650279565330a67c844355ba3e1b563d`，候选 `8f42a76b` 的包含性与完整树一致已核对；2026-10-03 10:48:57 UTC Closed。原118项本地结果按相同源码/程序集复用，合并后原业务保存另执行1/1、零跳过，独占库正常清理且只读核对零残留。[14份交付原件](../audits/2026-10-03-bug-147-snapshot-batch/native/post-merge/manifest.json)保留发布检查、首次HTTP408及正常重试、合并后冒烟与关闭记录，不改写原24份证据。

WP4已在独立分支完成本地实现及一次集中代码审查，无实质阻断。两库各通过 OIDC/Refresh 27、原ERP 95、WMS 8；身份原共同21与七个新增专项（每项2，含setup）按各自执行来源保留。财务/权限/采购/WF/通知及事件retry共同25项分批完成；PG采购锁的初次21/22和修复后采购5/5不改写为单次25/25。PG另两项实际40001证明整事务释放后重建并成功幂等；相关unit/SQLite另198/198。必需通过批次均零跳过，WMS未选择专项的预期8skip单列，不计原生验收。全部原失败、复用与外部传输/完整应用运行边界见[实施记录](../audits/database-compatibility/WP4-IMPLEMENTATION.md)。211份公共原件已按字节归档；当前待提交、正常PR合并及远端包含性核对；WP4尚未交付，WP5/6与父任务134继续Open。

根工作区原改动保持；全部验证本地执行，未触发/取消Actions、改工作流/保护或部署生产。下方Open/Pending均保留各历史时点，当前以本节和GitHub Issue为准。

## 2026-10-03：WP3 已交付，WP4 的 SQL 批量快照阻断修复本地通过

[PR146](https://github.com/GTX537/CP6/pull/146) 已合并，远端 main `0b0ab74a04c4d840a2c0bb05e36395aff8b89d32` 包含 WP3。WP4 双库业务回归发现 [BUG147](https://github.com/GTX537/CP6/issues/147)：SQL Server 身份快照批量保存时，EF 表变量与 BIN2 键的排序规则冲突。独立修复只调整该表的 SQL 生成，不改模型、迁移或数据库排序规则。

本地构建零 warning/error；七项真实 SQL 回归与 111 项相关配置/模型检查共 **118/118、零 skip**，包括原五快照/五事件业务保存、两类事务回滚和版本号回填；独占测试库已正常清理并核对零残留。一次任务级专项审查无实质阻断，证据与限制见[专项记录](../audits/2026-10-03-bug-147-snapshot-batch/README.md)。BUG147 仍 Open，待正常 PR/远端包含性核对；WP4 PostgreSQL 草稿留在原独立工作树，尚未验收或合并。父任务134与WP4–WP6继续进行。

未触发或取消 Actions，工作流及保护未改，无生产部署。根工作区原未提交锁文件保持不变，下方待交付文字保留各自历史时点。

## 2026-10-03：WP3 已交付，WP4 身份与核心业务适配开始

[PR #146](https://github.com/GTX537/CP6/pull/146)正常合入远端main `0b0ab74a04c4d840a2c0bb05e36395aff8b89d32`，包含候选`4ddc6c9d8d75f47711fce1c5e56fbb0ac8733e21`且完整树相同。合并后同一API实际再次拒绝普通PG运行、非零退出、无HTTP；[七份交付/冒烟原件](../audits/database-compatibility/wp3-native/post-merge/manifest.json)随本阶段状态保留。WP3原151来源/147原件及各次真实双库结果按原范围复用，两库临时库已清理。父Issue134已勾选WP3，仍Open。

WP4在最新远端main的独立`codex/db-compat-wp4-20261003`工作树开始，首段为OIDC、真实refresh与身份撤销/priority同事务链。已建立新的两库owner-marked临时库，正在准备真实PG RED入口，尚未声称WP4业务通过。其余身份、ERP/WMS、采购/OA/WF和财务按已接受阶段计划继续；[WP4实施记录](../audits/database-compatibility/WP4-IMPLEMENTATION.md)说明范围与未验收项。

根main因原有未提交锁文件被Git拒绝fast-forward，文件及其他未跟踪内容原样保留；没有为同步而stash/暂存/覆盖。WP4基线使用已确认最新origin/main。普通PG API/worker限制保持，WP4–WP6未完成；没有Actions运行/取消、工作流/保护变更或生产部署。下文为此前历史状态。

## 2026-10-03：WP3 本地验收与集中审查完成，远端交付待核对

WP1/2已分别由PR136/145交付；本阶段基线为main `605246ca83cbb8e1a89d3ff7209ba9df65176e98`。WP3已实现两库资源锁、数据库UTC、错误分类、ORD原子分配、共享Context事务、generation/v2游标与工作槽领取，并登记全部指定Provider分支及后续业务责任。

实际两库ORD各7/7及补充各5/5、锁/时钟各5/5、共享事务各3/3、约束与整事务重试各4/4、工作槽各10/10通过；游标SQL3/3、PG4/4含真实进程重启与PG同页并发快照。通用单测75/75和共享18/18保持各自来源；最终调用方相关Core54/54、Space22/22零Skip，包含14个异常注入回归。原启动30P/2Skip后两项真实SQL用例独立2P/0Skip保留原件。API双库首次/重复DatabaseInit四次exit0/noHTTP，未开放普通PG API/worker。

一次任务级审查的P2测试工具超时清理已定向解决；最终ORD与共享工具分别双库复测通过，强制超时只静态复核。原始失败全部保留，151个来源/147份唯一原件逐一hash一致，[manifest](../audits/database-compatibility/wp3-native/manifest.json)与[实施记录](../audits/database-compatibility/WP3-IMPLEMENTATION.md)说明复用及未验证范围。两个WP3独占数据库已普通DROP并确认不存在，原receipt及PG测试role保留。根工作区其他改动未动。

当前LocalVerified/RemotePending：待正常提交、PR、main包含性与必要集成核对；随后执行WP4/5/6。父[Issue134](https://github.com/GTX537/CP6/issues/134)继续Open。没有Actions运行/取消、工作流触发变更、保护变更或生产部署。下文保留此前历史时点。
## 2026-10-03：WP2 本地迁移验收与清理完成，远端交付待完

[PR #144](https://github.com/GTX537/CP6/pull/144) 于07:15:02 UTC正常合入，main `94c0f8c9cac63a72008c4e6b242c94358f25b305` 确认包含功能 `2b6548b1` 与证据 `5348a2a5`；WP2保留任务改动并fast-forward。BUG143原17/17、升级/重复15/15、四案例及恢复、fixture清理29/29和报价SQL/PG各2/2保持原范围；合并后同API原步骤exit0/noHTTP及native完整比较15/15已完成，[Issue143](https://github.com/GTX537/CP6/issues/143) 于07:23:27 UTC Closed，closure原件由WP2交付记录引用。

2026-10-03 WP2最终SQL136→140四指定前向升级实际2/2、完整目录/定向写入/负对照35/35、未知history2/2通过。新API首次/重复DatabaseInit均exit0、无HTTP，seed capture及两次verify各2/2，全部352表计数/history、自定义global/tenant翻译、管理员昵称和密码摘要保持。实际probe BFB8…314C0、Core F84…0DE1、API7097…740A，源码为main94加未提交WP2输入；probe源码/build观察在首次native期间完成，API观察在首次initializer之后，原pre-native误词与root-only SqlClient hash准备比较失败均保留分类，不称native失败或重跑。2950源码hash仅为输入观察，不扩大测试/review范围。

PG完整限定36/36、historical fresh33/33、unknown-history2/2、实际APP首次/重复和334表保持、151/151及独立25/25按原输入复用；另一次PG报价2/2保持独立来源。SQL oracle仍d607，原失败、30/45集中review、两P2及后加四文件/reader两行增量review的来源见[实施记录](../audits/database-compatibility/WP2-IMPLEMENTATION.md)。当前LocalVerified；8个owned测试库已全部清理并确认不存在，原所有权记录字节保持，PG测试role保留（[实际清理原件](../audits/database-compatibility/wp2-native/wp2-cleanup-final-owned-eight-verified-env.json)）；首两次清理工具准备失败均0drop并保留，非迁移失败。原始140份清单加最终输入证明/脚本2份，共142份已归档且全数hash/bytes匹配（[manifest](../audits/database-compatibility/wp2-native/manifest.json)、[源码字节证明](../audits/database-compatibility/wp2-native/wp2-final-review-source-applicability.json)）；75个记录输入一致不称新75测试或review。WP2提交/PR/远端main核对Pending，WP3–WP6及[父任务134](https://github.com/GTX537/CP6/issues/134)继续。普通PG API/worker guard保留，没有Actions、保护变更或部署。下方Current/Open/Pending保留历史时点，以本节与后续实际核对为准。

## 2026-10-03 历史快照：BUG #143 六列容量修复真实本地通过，远端交付待验

[Issue #143](https://github.com/GTX537/CP6/issues/143) 功能源 `2b6548b1` 已通过最终17/17、零skip/warning/error；同实际API/Core首次与重复DatabaseInit均exit0、无HTTP。已有generation的隔离SQL139→140升级/重复各15/15，352表数据/身份/已存token保留，6832列仅六个max→200变化、history仅指定一行，1378索引/202FK及其他目录保持；三报价表modify_date随ALTER前进。四个实际命令案例4/4、各完整恢复15/15，最后列拒绝前五列已实际收窄；三行报价fixture清理后的只读完整比较29/29。实际报价SQL修复前容量RED、修复后SQL/PG各2/2及双库超长最后空格差异见[证据](../audits/2026-10-02-bug-143-quotation-audit-capacity/README.md)。原102/207、准备失败与清理比较28P1F均保留分类，不冒充产品验收。

一次完整30文件审查按原范围复用，P2的两文件修正已定向复核Resolved、无新阻塞；缺失旧时间/完整manifestSHA保持null。当前LocalVerified/RemotePending，Issue143仍Open，待正常PR、远端main包含性和必要整合核对；WP2最终136→140及其余阶段仍待验。未触发Actions或部署。下方2026-10-02 Pending仅为历史时点，不改旧结果来源。

## 2026-10-02：BUG #143 六个报价审计列容量修复本地 GREEN，真实 SQL/交付待验

[Issue #143](https://github.com/GTX537/CP6/issues/143) 从已确认远端main `4d4e2608` 独立分支新增 SQL-only前向 `20261002193500_RestoreQuotationAuditColumnCapacity`，恢复三报价表 Creator/Modifier 的 nullable nvarchar(100)，保留原列collation。原生metadata只接受max或100；max列锁住检查DATALENGTH>200报51043，含尾空格/UTF16，拒绝而不删改或截断数据/依赖，六独立SQLops默认同EF事务，Down拒绝。模型/snapshot/旧136迁移/PG不改。

有效RED **6失败/7通过**，最终目标 **17/17、零跳过、零warning/error**，[原件与范围](../audits/2026-10-02-bug-143-quotation-audit-capacity/README.md)保留首轮snapshot反射夹具错误及后续测试regex修正。actualAPI/Core由真实dotnet test transitive build生成，未单独APIbuild；旧FK12/索引25证据按原范围复用。本BUG本地实现/自动化通过；原生安装/重复/边界/依赖/全数据与晚失败回滚、独立完整审查、正常PR/远端main核对仍Pending，Issue143 Open，不声明WP2/全业务或PG API完成。BUG141已经PR142合入本基线并于19:34:16UTC Closed；下方原Pending是历史时点。未运行Actions或部署。

初版17静态GREEN之后实际SQL102（COLLATE方括号）/缺列207失败已保留；两次全数据/目录恢复15/15不是容量迁移通过。已最小修native collation校验后safe literal拼接和metadata之后动态DATALENGTH bitOUT，新target17/17；APIhash不变但Corehash变更须重新绑定。修正版native与完整审查/远端交付仍Pending，不预写GREEN。

## 2026-10-02 历史快照：BUG #141 已交付，WP2 PG 门禁通过，SQL 四项前向升级待验

[BUG #141](https://github.com/GTX537/CP6/issues/141) 已经 [PR #142](https://github.com/GTX537/CP6/pull/142) 正常合入，远端 main 已核对为 `4d4e260819f0a1b59810403d016c69a2fd7319ae`，Issue Closed。WP2 分支保留本任务改动并 fast-forward 到该主线；SQL 原合同 oracle 仍冻结 `d6074aaa`。该 BUG 的真实 Core138→139、首次/重复初始化、四 FK/级联和 rollback 证据保持原范围，详见[原始审计](../audits/2026-10-02-bug-141-order-foreign-keys/README.md)。

WP2 最新实际 PostgreSQL 完整限定门禁 **36/36**，包括四 Context、65 个 raw 默认、四个全局 NULLS NOT DISTINCT 唯一索引、令牌/generation、时间与代表性关系/金融写入。执行原件 `wp2-migration-pg-final-exact-history-full-write-gates.json` 的源码基线为 `2d974736`，保留执行输入和二进制哈希；文档及主线 SHA 更新不改称新执行。输入 sidecar 补正实际 SDK `10.0.302` 与 1439 个源码哈希，原缺失 SDK 文件的无效 sidecar 保留，未因此重跑 build 或 native gate。实际 PG DatabaseInit 首次/重复退出 0、无 HTTP；其 **334 张表**行数/历史与全局/租户自定义翻译、管理员昵称和密码哈希保留。相关测试 **151/151** 与另一次索引回归 **25/25** 均零跳过，各自保留真实来源；双库金额/FK 和 Space 租户/Unicode 存储门禁各 **3/3**。这些证据证明限定初始化、结构和存储合同，全业务验收尚未完成，普通 PG API/worker 仍受运行门禁限制。

实际 PG 迁移历史负对照 **2/2**：未知 ID 在 `Migrate` 前被拒绝，外部事务回滚后原历史完整恢复；缺失 `--seed-mode` 值或 seed state 路径均实际 exit 1、`DatabaseVersion=null`，在数据库访问前拒绝。这些预期失败分别保留原件，不计为业务成功。WP2 一次集中审查已按分工完成初始化/Provider/工具 **30 文件**及映射/帮助器/迁移/模型测试 **45 文件**两个范围；前者两个 P2 已修复并仅定向复查 seed 参数前置校验与迁移历史精确合同，未因文档更新重启完整审查。执行来源、复用边界及历史失败见[实施记录](../audits/database-compatibility/WP2-IMPLEMENTATION.md)与[模型测试记录](../audits/database-compatibility/WP2-MODEL-TESTS.md)。

[BUG #143](https://github.com/GTX537/CP6/issues/143) 仍 Open，正从最新已确认 main 的独立分支恢复六个报价 Creator/Modifier 历史容量。WP2 最终 SQL **Core136→140** 的 generation、21 个索引、四 FK、六列容量 **四项前向升级尚未执行**；旧 generation-only 136→137 及 BUG #141 的 138→139 分项成功不扩大为最终升级通过。WP2 冻结、最终 SQL 验收、远端交付及 owner 库清理仍待完成，WP3–WP6 与 Issue #134 未完成。未触发/取消 Actions、改变触发或保护、迁移既有业务环境或部署。下方保留各历史快照，其中 Open/Pending 仅指相应记录时点，当前状态以本节为准。

## 2026-10-02 历史快照：WP2 PG 初始化与定向门禁通过，SQL 最终升级待验

本段保留 BUG #141 交付前的 WP2 执行快照；33/33、三项 136→139 计划和当时 Open/Pending 不改称最新执行或当前状态，最新进度见顶部。

[索引修复 PR #140](https://github.com/GTX537/CP6/pull/140)已正常合入并核对远端 main `2d9747360a38dfc519ddebea7aced06a9466b879`，包含功能 `083e9c4d` 与原始证据 `94124c62`；合并后原步骤目录冒烟通过，[Issue #139](https://github.com/GTX537/CP6/issues/139)已关闭。WP2 worktree 保留本任务改动并 fast-forward 到该主线；SQL 原合同 oracle 继续冻结 d607。

WP2 新空 PostgreSQL 结构/代表性写入门禁 **33/33**（65 默认和四个全局唯一索引已安装）；双库金融金额/外键及 Space 租户复合 FK/Unicode 存储边界各 **3/3**。相关 Provider、接线、模型及初始化测试 **151/151**，另一次索引集成回归 **25/25**，两次均零跳过，保留各自真实来源。实际 compiled PG DatabaseInit 首次与重复均退出 0、不启动 HTTP；全部 **334 张表**行数/迁移历史保持，全局及租户翻译、管理员昵称和密码保留。PG 实际 Order graph/四 FK/删除级联专项 **2/2**，旧 SQL136 同专项 **1 通过 / 1 失败**是有效缺 FK RED。成功结果及历史失败见[实施记录](../audits/database-compatibility/WP2-IMPLEMENTATION.md)，这些限定门禁不替代全业务验收。

SQL 完整目录发现四条 Order 外键缺失，由独立 [BUG #141](https://github.com/GTX537/CP6/issues/141) 修复；该源码相关 **37/37 GREEN**，真实 SQL 验收及远端交付仍 Pending，Issue Open。两个 alternate key 已确认由明确的全局 unique index 等价实现，目录继续验证定义。最终 SQL136→139 的 generation/索引/FK 三项正常前向升级尚未执行，旧升级证据不扩大为本轮通过。模式冲突已在访问数据库前拒绝；普通 PG API/worker guard 实际拒绝仍保留。WP2 收尾、集中审查、远端交付和 owner 库清理待完成；后续按授权继续 WP3，WP3–WP6/Issue #134 未完成。未运行/取消 Actions，未改触发、保护、现有数据库或部署。下方记录保留对应历史时点。

## 2026-10-02 历史快照：BUG #141 四条订单外键通过本地及真实 SQL，待正常交付

本段保留 PR #142 合入前的 BUG #141 本地验收快照；其中 Issue Open、远端 Pending 为当时状态，当前已 Closed，交付事实见顶部。

[订单外键遗漏](https://github.com/GTX537/CP6/issues/141) 从已确认 `main` 的 `2d974736` 独立分支新增 SQL-only 前向迁移，恢复四个既有全局业务键关系及 CASCADE/NO ACTION。WITH CHECK 验证原行，同名对象必须完整匹配 enabled/trusted/NFR 等合同，否则拒绝；既有孤儿由 SQL547阻止，不删改数据或 NOCHECK。模型、snapshot、旧迁移及候选唯一键不改。旧链 RED **4 失败 / 5 通过**，FK12+索引25相关 **37/37、零跳过、最终零 warning/error**，[原件和范围](../audits/2026-10-02-bug-141-order-foreign-keys/README.md)保留。

一次独立完整任务审查无 P1/P2/阻塞；源码 `73946e9` 与 API F40298E0… 未变，真实隔离 SQL 首次/重复初始化 exit 0、无 HTTP，Core138→139、Space47，升级/重复比较各 **9/9**：352 表原数据摘要/行数、1378 索引和198原FK保留，仅加4FK（总202）。最终 native guard **11/11**，每次恢复 **9/9**；首轮5项复用、6项新执行，原 NFR夹具51003失败保留，修正组合场景不称 isolated NFR证明。独立WP2关系gate实际SQL **2/2**、四孤儿拒绝/四级联，写入后完整状态比较 **9/9**。本 BUG 本地验收通过，正常 PR、远端 main核对仍 Pending，Issue Open。未连接业务库、部署或触发 Actions；六个报价历史容量差异仍独立WP2阻塞，不声明全结构/业务或 PG API完成。下方保留各历史时点。

## 2026-10-02：BUG #139 本地及真实 SQL 验收通过，待正常交付

[订单查询索引遗漏](https://github.com/GTX537/CP6/issues/139) 的独立 SQL-only 前向修复补 21 个非唯一索引；冲突拒绝，两个历史唯一别名不重复创建，模型/snapshot/旧迁移未改。旧链 RED **21 失败 / 1 通过**，单元 **25/25、零跳过**。同一 compiled API 真实首次与重复初始化均退出 0、无 HTTP；升级与重复比较各 **25/25**，352 表行数/摘要状态保留，原 1,357 个索引捕获 metadata 原样，只新增 21 项。实际 guard **8/8**，每项回滚恢复比较 **25/25**，含晚冲突撤销先前 20 项创建。[原始证据与失败边界](../audits/2026-10-02-bug-139-order-indexes/README.md)保留。

功能源码 `083e9c4d` 的一次独立完整任务审查无 P1/P2/阻塞，后续仅归档/状态变更复用原结果。真实库已有 WP2 generation，因此 Core 137→138，Space 47 保持；不改旧 history。当前本地验收通过，正常 PR、远端 main 核对及关闭尚待交付，Issue Open。未运行 Actions、连接业务库、迁移现有环境或部署；DB-COMPAT-01 其他结构缺口和阶段仍独立待验，不声明 WP2 全目录完成或 PG API 开启。下方保留历史时点。

## 2026-10-02：DB-COMPAT-01 WP1 远端交付，开始 WP2

[PR #136](https://github.com/GTX537/CP6/pull/136) 已正常合入远端 main `d6074aaad3098b61adaeda902b92bf24b3eb0c04`，包含 `d30da70e` / `7bbe2eca`；本地/远端 main 一致，WP1 交付闭环。复用相同代码/依赖的真实 133 配置测试、SQL28 / PG31 限量试验，不重新执行或称为全量业务验收。未触发/取消 Actions，未改保护或环境。

从该基线创建独立 `codex/db-compat-wp2-20261002` worktree，进入四 Context 完整映射、PG 迁移与初始化，见 [WP2 实施记录](../audits/database-compatibility/WP2-IMPLEMENTATION.md)。WP2 尚未验收/交付，WP3–WP6 未完成，整体目标及 Issue #134 保持进行中；PG API/worker runtime guard 保留。下方 WP1“待交付”条目保留提交时的历史时点。

## 2026-10-02：BUG #137 已正常 PR 合并，远端核对后关闭

[采购对账菜单首次资源键修复](https://github.com/GTX537/CP6/issues/137)已在独立分支提交。旧逻辑 RED **2 失败 / 1 通过**；修复后相关测试 **20/20 通过、零跳过**。真实隔离 SQL 空库首次初始化即有菜单资源键；同一 compiled API 再次初始化后，**全部 351 张表行数和迁移历史不变**，包括字段审计。全局/租户翻译、管理员昵称与密码哈希原样保留，两次进程退出 0、不启动 HTTP。详见[执行证据与适用边界](../audits/2026-10-02-bug-137-menu-seed/README.md)。

功能执行源 `bab643750111beef610eba911bb530b422f9d99f`；后续文档及证据提交复用相同功能源码的测试。[PR #138](https://github.com/GTX537/CP6/pull/138)已合入远端 main `d507f389fdcadd8e2fc5dcb9e40f752cde613703`，祖先关系核对包含功能提交，Issue #137 已关闭。不执行 Actions、现有数据库迁移或部署；触发配置未改、取消项 0。

## 2026-10-02：DB-COMPAT-01 WP1 本地门禁完成，待正常 PR 交付

用户要求在本对话持续执行 [Issue #134](https://github.com/GTX537/CP6/issues/134) 的六阶段兼容任务。WP1 从远端确认的 `685a5238` 建立独立 worktree / 分支，已完成 Provider、连接与四 Context 的注册/设计时迁移选择。配置接线本地测试 **133 通过 / 0 失败 / 0 跳过**，包含此前 38 项 Provider 基础测试，不重复相加。固定实际 Platform **0.10.2** 的源码、包身份与运行证据；详见 [WP1 决策](../audits/database-compatibility/WP1-DECISIONS.md)和[探针证据](../audits/database-compatibility/WP1-PROBE.md)。

SQL Server 收尾限量真库探针 **28 Passed / 1 NotApplicable / 0 Failed / 0 Blocked**，PostgreSQL **18.6** 探针 **31 Passed / 1 Rejected / 0 Failed / 0 Blocked**；Rejected 是真实写路径负对照排除 SaveChanges-only 应用 token 候选，不是跳过必需场景。两库使用相同源码与实际运行二进制输入，fingerprint 为 `0F0B443961674D40B8164768225AE12D1A55CD18F28FCE0D5F16BB0CB91C6BB3`。已选定 SQL Server 原生 rowversion、PG 数据库生成的 **8 字节 bytea token**，并独立采用同事务持久化 tenant generation / v2 cursor；真实 SQLSTATE `40001` 与严格 UTC 拒绝及显式归一化规则已试验。原 `28P01`、负对照和修复前失败记录保留。

任务负责人已在核对准确库名、owner 标记、**0 对象 / 0 连接**后清理两库，没有 FORCE 或会话终止；专用 PG 角色保留给后续阶段。WP1 本地门禁已完成，准备正常 PR 交付，当前尚未合并至远端 main；核对远端交付后再从新分支执行 WP2。WP2–WP6 及整体双库兼容未完成，PostgreSQL 应用 runtime guard 保留，实际启动拒绝退出 1。父任务保持 Open；没有新增 Actions、现有环境切换、数据搬迁或生产部署。下方“功能实施未开始”保留前次建档时点。

## 2026-10-02：双数据库兼容任务已登记，功能实施未开始

用户接受“同一套代码、每次部署选择 PostgreSQL 或 SQL Server”，以及共享业务模型、独立 Provider 迁移链的方向。主任务为 [DB-COMPAT-01 / Issue #134](https://github.com/GTX537/CP6/issues/134)，保持 Open；[源码盘点](../audits/2026-10-02-database-compatibility.md)、[设计](../superpowers/specs/2026-10-02-database-compatibility-design.md)与[六阶段计划](../superpowers/plans/2026-10-02-database-compatibility.md)已建档。基线 `157630594e3371fe181955d2f6227ff3b6962c84`：四个 Context、187 个迁移主文件、208 个 rowversion 快照映射；本机 PostgreSQL 18 服务运行只证明环境存在。

下一步 WP1 核验 Provider/Context 工厂、真实 Platform 包、并发 token、身份快照边界及同连接事务。现有 SQL Server 数据搬迁、独立 CRM 全面改造和生产上线另立任务。本次仅文档与任务登记，未修改运行代码、连接业务数据库、执行迁移/业务测试或触发 Actions；文档合并不等于兼容功能完成。

## 2026-09-22：cp6.uk 主站 demo 更新

按用户授权，将 main `ac624299` 的 ERP/WMS/3D Space 发布到独立 `cp6-demo`，使用 `CP6_DEMO_20260922` 副本和独立缓存/消息服务；退休根 API 保持 stopped/no restart，旧 Tunnel 停止。公网登录、健康、双端源码身份、ERP/WMS 列表、Space Published 两层模型已验证；10 项自动化 smoke 通过，错误 SHA 被拒绝。3D 实时库存叠加仍受 [Issue #132](https://github.com/GTX537/CP6/issues/132) 影响。详见[部署证据](../devops/evidence/cp6uk-demo-20260922/README.md)与[操作说明](../../deploy/demo/README.md)。未执行 Actions、生产部署或独立 CRM 接入。

## 2026-09-16：恢复六项 Space 发布修复

从当前主线整合仓库编码解析、发布/回退权限、较大内部发布计划、发布 worker 身份、持久化批次恢复和运行失效行处理；保留当前首页、Viewer 与项目状态。[验证报告](../space/reports/2026-09-16-space-publish-recovery.md)：相关后端 26/26、真实 LocalDB SQL 4/4、最终篡改回归 1/1，均零跳过；合入首页后重新编译并复跑后端 26/26。临时库前后为零，无既有业务库或运行环境变更。

旧库存 URL 候选在超过 100 个 ID 时扩大为整站查询，会触发大站点 10,000 库位限制，已排除。后续须保留精确 ID 范围并验证大站点上的合法小范围请求；本轮没有新增 API/SDK、Actions 或生产部署。

## 2026-09-16：恢复 3D Viewer 自由相机

从已确认的 `origin/main` 基线 `b807d603` 恢复遗漏的 `c703e45d`、`04d927eb`、`d747bd0d`：Viewer 明确支持屏幕空间平移、指针缩放、鼠标/触摸映射，手动开始操作立即取消相机飞行动画；画布增加操作提示、ARIA 说明及右键菜单抑制。现有编辑器视口改动不受影响。恢复的[设计](../superpowers/specs/2026-08-29-space-viewer-free-camera-design.md)和[计划](../superpowers/plans/2026-08-29-space-viewer-free-camera.md)已标明历史语境。

本轮在独立 worktree、Node `22.22.0` 下重新验证：`npm run test:unit -- src/space-viewer/navigate/CameraController.spec.ts src/space-viewer/navigate/Picker.spec.ts src/space-viewer/stacked/StackedViewer.spec.ts src/views/space/viewer/FloorViewer.navigation.spec.ts` **4 文件 / 20 项通过**；`npm run type-check` 和 `npm run build-only` 均退出 0，构建仅提示 chunk 大于 600 kB。回归测试在旧实现下确认 5 项预期失败；组件测试初始化曾因 i18n mock 缺少 `createI18n` 导出失败，已修正测试替身后验证。组件测试覆盖真实模板渲染与 DOM 事件，图形引擎和 API 使用测试替身，不计作真实 WebGL 或生产验收。

本条记录恢复分支的本地验证时点；远端 main 交付由总整合任务核对。未修改现有运行环境、数据库、容器或部署；未触发 Actions，未重复旧计划中的 10,000 库位浏览器验收。

## 2026-09-16：恢复 Space 首页 Design V1 楼层与语言刷新

从已确认主线 `b807d603` 恢复历史遗漏提交 `22b3a9ce` 的五个源代码/测试文件，未覆盖后续主线逻辑。首页在具备 `space:model:read` 时优先汇总活动草稿或已发布版本楼层，Legacy 无模型/无读取权限时保留原路径；已发布楼层的编辑入口返回 Studio。补齐 44 个 Space 五语言词条，启动种子后清理完整包和命名空间缓存；浏览器旧缓存只用于即时显示，初始化仍刷新词条。

本地验证：SpaceHomeView 定向单测 9/9；旧实现对照 5 项预期失败、恢复后 9/9；Vue 类型检查通过；WebAPI Debug 编译 0 警告/0 错误；44 词条唯一性、五语言及占位符一致性检查通过。后续由整合任务合并 main 并核对远端。未运行 Actions、生产部署、真实数据库/Redis 种子或浏览器验收，本结果只证明定向本地恢复。

## 2026-09-16：补齐历史 BUG 管理交付

按用户要求核对本地分支与远端主线，从最新主线整合历史PR #39的[轻量级BUG管理](../quality/BUG-MANAGEMENT.md)、Issue Form和PR模板。保留当前主线全部业务、验收及Actions额度策略；状态冲突以当前记录为基础补充本条，不恢复旧里程碑。该任务只交付治理文档，不表示修复Issue #38。

## 2026-09-16：全项目源码盘点与升级入口

完成业务、架构和界面现状材料整理。公开[Core/Platform分报告](../audits/2026-09-16-full-project/README.md)保留公开源码证据；[含CRM的完整跨仓报告](https://github.com/GTX537/CP6.CRM/blob/main/docs/audits/2026-09-16-full-project/README.md)与升级工作包留在CRM私有仓库。已记录旧Web拣货未提交、外部NoOp与固定规则、消息语义差异、Space/原生客户端及界面统一范围。

本轮只改文档与复现工具，不改业务、数据库、运行宿主或既有验收；未运行业务测试/Actions或生产部署。[验证与交付范围](../audits/2026-09-16-full-project/VALIDATION.md)。

## 2026-09-16：C04B 模拟开发验收通过，交付后 C 系列100%

用户因无真实业务数据明确批准验收v2。C01/C02/C03/C04A既有20%权重及成功证据保持，C04B通过[本轮证据](../evidence/c04b/2026-09-16/simulated-exit/README.md)：20表旧生产模型/状态机退出、静态阻塞8→0、38项定向检查全部通过、20条模拟旧记录保留、独立发布257文件及HTTP三项200、临时库清理为0。C01/C02身份与C03 ERP关联保留，旧实体/状态机字节保留于测试夹具。

**本轮双仓库远端main核验后，C系列开发验收100%（5/5，较80%增加20个百分点）。** 合并前证据明确保留Git待交付状态，最终交付回执独立记录。当前版本详见[进度v2](../project-memory/C_SERIES_PROGRESS.md)；以下历史60%/80%及未关闭条件只表示其记录时点。

本轮开发目标没有剩余业务数据/观察期阻塞。真实用户采用、10工作日/200条Lead、Full Journey、正式演练/发布周期观察及生产上线移至以后独立生产验收，旧表物理DROP另立任务。原Written目标、日历、来源保护及已采用宿主保持；未执行生产部署或Actions，未重跑原五项SQL与六库恢复。


## 2026-09-16 UTC：原运行恢复，C04A 最终验收完成

[运行恢复与 A12 证据](../evidence/c04a/2026-09-16/runtime-recovery/README.md)记录重启后 Docker 修复、原库/容器身份保持、429 原发布文件及请求摘要匹配、原五进程宿主 ready、Kafka/Dapr 恢复、三源 Frozen/60 旧表零行、两目标 Written/原日历版本、两组织身份 Ready/零漂移，以及健康接口和两个公开表单实时成功。仅补中断后的最终验收，旧通过测试及初始化/冻结/启用/发布均未重做。

**整体清单80.0%（较78.3333%增加1.6667个百分点），C04A 12/12＝100%；本任务双仓库远端 main 交付后正式里程碑80%（4/5）。** 原验收回执的 Git 待交付字段保留为历史时点。两个目标首写实际由 `crm_v1.RetentionRun` 在日历发布前触发；当前保持前向修复边界。Docker 以后须使用保留的本机缓存启动脚本；原数据和异常 socket 保留，未重置/恢复数据库。部署 Agent 继续 Stopped/Disabled。

C04B 剩余20%：CRM11 正式演练/切换与采用、至少一个正式发布周期只读观察、旧 EF/运行时依赖退出及前向清理。未生产部署或运行 Actions；本机空来源采用不替代生产验收。下方记录为各历史时点。

## 2026-09-16 UTC：两组织原库日历已发布，A10/A11完成

[实际采用证据](../evidence/c04a/2026-09-16/actual-local-adoption/README.md)记录管理员独占窗口、旧API停止、八入口退休、六份当前备份、一次原库初始化、三源Frozen及经实时证明的整组启用。为两个原管理员补齐缺失的日历权限后，各一次保存/预览/发布成功，表单均200，两目标已Written。**整体清单78.3%（较75.0%增加3.3333个百分点），C04A 11/12＝91.7%，正式里程碑60%。**

中断后Docker及宿主退出；原生持久状态核对保留。第一处Docker残留socket目录已获明确允许并保留修复，第二处engine.sock目录连管理员改名也被Windows拒绝，当前运行未恢复。A12仍缺最终运行验收，暂不计80%；拟议受控Windows重启尚未执行。旧通过SQL/恢复项目沿用；禁止重新初始化、冻结、启用、发布或恢复旧库覆盖Written目标。当前任务仅交付证据/状态文档，未生产部署或运行Actions。下方为历史记录。

## 2026-09-16 UTC：实际本机切换批准已收到，余下 Windows 管理员控制

用户在具体审阅包后回复“没有问题，给你所有开发权限”，现已单独绑定 [批准记录](../evidence/c04a/2026-09-16/approved-adoption-window/approval.json) 与原审阅清单 SHA `531e2810d3ec3a010dd0a646a0cb4cca9a998d33a49cebd53ba4e260feaf93eb`。本机切换、0.2 变化字节、既有操作身份及两组织日历首写已获授权；不再重复索要批准。原审阅包的 false 字段保留为历史状态，未改写原件。

当前 Windows 令牌仍不能停止或禁用准确部署 Agent（错误 5），Agent 仍 Running / Auto，SQL Agent 仍 Stopped / Manual。已准备只控制该 Agent 的 [管理员操作与回执脚本](../evidence/c04a/2026-09-16/approved-adoption-window/README.md)，验证语法及非管理员失败关闭；管理员成功路径尚未执行。实际服务/库未变，旧通过测试未重跑。**整体清单 75.0%、C04A 9/12、正式 60%，本轮增量 0**；A11 尚缺独占控制，A10/A12 继续等待该技术前置。

## 2026-09-16 UTC：实际切换审阅输入就绪，管理员控制仍缺失

[本机审阅包](../evidence/c04a/2026-09-16/final-adoption-review/README.md)固定 429 个原发布文件、六库当前身份、八个未运行旧入口、两组织已有管理员/根部门和初始日历请求。日历正常保存须位于整组启用之后，首次保存即 Written；没有直接写表或把请求文件计作已存储版本。

部署 Agent 当前 Running / Auto，当前 Windows 令牌查询成功，但 Stop/Start/ChangeConfig 权限均返回 Access denied（5）。实际操作仍缺管理员独占窗口及本包最终批准；原 SQL Agent 保持停止，根 API 正在运行并列入排流影响。没有实际服务/库变更。A10/A11/A12 仍未闭环，**整体清单 75.0%、C04A 75.0%（9/12）、正式验收 60%**，本次准备增量为 0。

## 2026-09-16 UTC：A09 原密钥、完整宿主与身份恢复通过

复用真实备份及已初始化副本，原密钥解密 8 条历史会话成功；完整宿主五进程与私有 transport 启动，两组织身份对账 119 投影/105 用户、零漂移，原账号密码/OIDC 登录与 workspace/列表读取通过。公开表单因初始日历未发布返回 409，仍归 A10；两个业务目标始终 Closed / 只读。宿主与自有 transport 已停止，四个只读副本及运行前认证备份保留。见[恢复证据](../evidence/c04a/2026-09-16/full-host-recovery/README.md)。

A09 **100%**；C04A **75.0%（9/12）**，整体清单 **75.0%**（上次 73.3%，本项精确增加 1.6667 个百分点），正式验收仍 **60%（3/5）**。未重跑原五项 SQL、前五库保障或初始化；原输入/发布字节和 51 个既有容器保持。A10 日历、A11 独占控制/最终签收、A12 实际切换继续开放。见[固定清单](C_SERIES_PROGRESS.md)。

## 2026-09-16 UTC：Docker 第六库真实恢复通过，六库保障完成

Docker 旧套接字目录已原样保留；定位跨卷改名问题并恢复引擎后，精确核对原容器/数据库身份，完成第六库不覆盖备份、CHECKSUM/VERIFYONLY、隔离实际恢复、CHECKDB、329 张用户表读取及结构/原库状态保存。备份和只读恢复库保留；51 个现有容器配置、挂载条目、策略与状态保持。见[本轮证据](../evidence/c04a/2026-09-16/sixth-db-restore/README.md)。

六库保障 **6/6＝100%**，合计 950 张用户表已读取；前五库沿用原证据，没有重跑。C04A **66.7%（8/12）**，整体清单 **73.3%**（上一记录 73.1%），正式验收仍 **60%（3/5）**。不是六库共同停写快照或实际切换；完整应用/身份恢复、初始日历存储版本、独占控制/最终签收及实际切换继续待办。原根服务随已授权 Docker 启动按既有策略恢复，C02 transport 仍停止。见[进度清单](C_SERIES_PROGRESS.md)。

## 2026-09-16 UTC：C 系列细分进度与 Docker 第六库阻塞

应用户要求，补充固定 12 项 C04A 清单：整体清单完成度 **73.1%**，C04A **65.3%**，正式里程碑验收仍 **60%（3/5）**。这是既有成果的细分统计，不是本次新增完成 13.1 个百分点。见[详细进度](C_SERIES_PROGRESS.md)。

Docker 启动已获明确授权并实际尝试；保留第一个旧套接字运行目录后，第二个 engine.sock 仍受 Windows 访问错误阻塞。第六库未开始备份，数据库保障保持 **5/6（83.3%）**。未改数据库/卷/ACL，未实际切换，未重跑旧通过项目。原密钥和完整宿主、日历初始存储版本、独占写入/最终签收及实际切换继续待办。见[本次阻塞证据](../evidence/c04a/2026-09-16/docker-recovery/README.md)。

## 2026-09-15：真实恢复副本前向初始化与 Closed 启动通过

从三个实际 C02 备份另建隔离副本，原 0.2 API 仅初始化一次并成功；两组织 Ready/Closed，旧快照保护有效，13 表 492 条既有身份/目录记录保留。独立 loopback API live 200、Closed ready 503，进程已退出，副本只读保留。见[本轮证据](../evidence/c04a/2026-09-15/restored-initialization/README.md)。原发布包/私有输入未变，已通过的五项 SQL 未重跑。

此结果不含完整宿主、身份对账/登录或实际切换；日历实测仍为空，启用前初始存储顺序需解决。Docker 第六库、独占操作与变化字节批准、CRM11/C04B 正式验收及观察仍待办，总目标 **60%（3/5）**。正常 PR 与远端 main 包含性另记交付。

## 2026-09-15：五个实际原生数据库备份与可读恢复副本

五库 COPY_ONLY/CHECKSUM、VERIFYONLY、实际恢复、CHECKDB 及 621 张用户表读取通过；原库/副本结构摘要一致，11 份原私有输入已另存并核对哈希。备份与新只读恢复库保留，实际数据库和发布字节未变。见[恢复证据](../evidence/c04a/2026-09-15/six-db-recovery/README.md)。已通过的五项 SQL 门禁不重跑。

机器重启后旧六进程已退出，Docker 引擎未运行；第六库备份/恢复继续待办，旧 PID 清单失效。下一步补第六库、真实恢复副本上的 0.2/Closed 启动兼容、日历初始存储顺序、独占操作与变化字节签收。尚非六库全套或实际采用完成；CRM11/C04B 仍开放，总目标 **60%（3/5）**。正常 PR 与远端 main 包含性另记交付。

## 2026-09-14：来源证明与整组启用必要 SQL 门禁完成

剩余三项新 SQL 与两项受影响 Enable/Close 已全部通过；最后一项修正了锁分区的测试计数，业务代码、发布包和超时保持。既有通过项不重跑，原失败与准确版本均保留。453 文件边界、定向复查和清理通过，见[最终证据](../evidence/c04a/2026-09-14/verified-target-enable/README.md)；正常合并与远端 main 包含性另记交付。

继续实际本机采用与 Closed 启动、六库备份恢复、独占写入控制、初始日历版本及变化字节签收。CRM11 正式切换与 C04B 采用/观察尚未完成，总体 **60%（3/5）**。

## 2026-09-14：启用草稿整合已交付主线

恢复宿主和停用命令已合入启用草稿，组合工具编译与命令入口通过，453 文件边界通过；原发布包和既有通过结果按原范围保留，未重跑测试或 SQL。详见[组合记录](../evidence/c04a/2026-09-14/verified-target-enable/README.md)。

三个新增 SQL 与两个受影响 Enable/Close 门禁仍开放，Core #114 / CRM #75 继续保持草稿，不能合入主分支。实际执行、变化字节签收、CRM11/C04B 仍待完成，总体 **60%（3/5）**。

## 2026-09-14：独立旧入口停用与本机执行包准备

完整组织集合绑定的 `retire-deployment-local` 已实现，15 项新增检查通过，无 SQL、无跳过，不重跑旧通过项。当前主分支 API 和新操作工具已独立发布，恢复宿主原样复用；18 项新 API 宿主输入与实际八入口已只读核对。详见[本轮证据](../evidence/c04a/2026-09-14/local-execution-package/README.md)。原六进程、五容器和私有配置保持，新增候选运行清单未被选用；447 文件边界通过。正常 PR 与远端 main 包含性另记交付证据。

实际执行包仍为草案：来源/整组启用 SQL 门禁、六库备份/恢复保障、独占写入身份控制、当前日历初始版本、实际 C02 前向采用和读取冒烟、0.2 字节签收仍开放；初始化后禁止简单换回旧 CRM DLL。CRM11 正式切换与 C04B 采用/观察不由本轮替代。总体 **60%（3/5）**。

## 2026-09-14：C04A 独立恢复宿主开发验证完成

独立宿主已移除旧 CRM API、SQL 和验收初始化依赖，17 项新增生命周期/回调检查及 446 文件边界通过，见[本轮证据](../evidence/c04a/2026-09-14/preview-recovery-host/README.md)。只读核对原清单 18 个必要输入，现有 6 个进程和 8 个入口/控制文件保持原样；没有连接 SQL，自有夹具全部清理。正常 PR 与远端 main 包含性另记交付记录。

Core #114 / CRM #75 的 SQL 门禁继续开放，保留已有 62 项通过结果。继续实际生命周期/特权控制、当前 0.2 字节签收及完整执行恢复包；实际采用恢复、CRM11/C04B 正式验收和观察期仍未完成。总体 **60%（3/5）**。

## 2026-09-14：保留来源冻结失败诊断，启用门禁继续开放

独立恢复宿主已由 Core #115 / CRM #76 交付。此启用草稿仅补新增检查的固定错误编号输出，原发布包和 62 项通过结果不变；一次未通过场景仍在来源冻结阶段返回 C04A_SQL_FAILURE，且观察到 SQL 内存缓冲区等待及只读查询超时。详见[诊断续记](../evidence/c04a/2026-09-14/verified-target-enable/README.md)；不据此改写早先未分类证明拒绝，也不继续批量试跑。

四个本次临时数据库已清除，原生/Docker 最终临时库为零，原预览六进程与八个入口摘要不变；私有失败目录保留。三项新 SQL 和两项直接受影响门禁仍待完成，草稿禁止合并；实际采用、变化字节签收和 CRM11/C04B 保持开放。总体 **60%（3/5）**。

## 2026-09-14：C04A 来源冻结与整组启用草稿

来源 Frozen 证明与整组原子启用已在独立分支实现，49 项 Core 与 13 项 CRM 新检查通过；必要 SQL 门禁仍开放，当前为开发草稿。详见[本轮证据](../evidence/c04a/2026-09-14/verified-target-enable/README.md)。444 文件边界通过，SQL 失败原件和准确发布版本保留；不重开无关旧通过项。临时测试库已清理，现有预览保持，两个本轮私有失败夹具目录保留供调查。

实际启用、0.2 变化字节签收、CRM11/C04B 正式验收及观察期均未完成；恢复宿主兼容、特权写入控制和最终执行包继续推进。总体 **60%（3/5）**。

## 2026-09-14：C04A 多组织目标恢复必要开发检查完成

完整目标集合绑定、全部目标持锁恢复和 CRM 集合操作入口通过 60 项新增与 2 项直接受影响检查，见[最终证据](../evidence/c04a/2026-09-14/target-set-recovery/README.md)。最后一项超时检查在只读 SQL 连接恢复后以原样二进制通过；未放宽超时或改动共享环境，旧失败原件保留。原生/Docker 临时测试库及实际来源控制 Schema 最终均为零，一个自有失败临时目录因自动审批拒绝而保留。

已同步最新 main 的启动控制，保留各组件原通过范围。Core 40 文件原样复用，CRM 组合版本重新构建 178 文件并验证命令入口；438 文件边界及合并差异审查通过。此前草稿的 SQL 门禁已关闭，正常 PR 与远端 main 包含性另记交付记录。继续完成恢复宿主依赖兼容、特权写入控制、来源冻结与目标启用联动和最终执行包；实际切换、0.2 字节签收、CRM11/C04B 正式验收及观察期仍开放，总体 **60%（3/5）**。


## 2026-09-14：C04A Node 与未运行启动入口控制开发验证完成

本机排流清单已支持 Node 和未运行的旧启动入口；统一预检、精确活动进程身份、持续退休及旧清单字节兼容通过 8 项新增和 1 项受影响检查。435 文件边界和完整差异审查通过，178 个发布文件固定摘要，见[本轮证据](../evidence/c04a/2026-09-14/local-startup-control/README.md)。没有运行 SQL，原共享预览 6 个进程和 8 个入口/控制文件未变，自有临时进程与目录已清理。正常 PR 与远端 main 包含性另记交付记录。

该能力不替代实际清单完整性或管理员隔离。多目标恢复草稿 Core #112 / CRM #73 仍有一项必要 SQL 检查及最终清理观察待完成；本轮不改其结果。后续保留恢复宿主依赖兼容、特权写入控制、来源冻结证明与目标启用及最终执行包。实际环境未切换，0.2 字节签收、CRM11/C04B 正式验收和观察期仍开放，总体 **60%（3/5）**。

## 2026-09-14：C04A 本机 Docker 来源绑定开发验证完成

已实现本机 Docker 命名管道身份绑定、精确 loopback SQL 入口和实际来源冻结/恢复接线，保留原生请求字节兼容。16 项新增及 2 项直接受影响验证通过，包含发布 Core 工具控制 Docker 来源、原生 CRM 目标证明及恢复后业务写入；435 文件边界通过，一次集中审查没有待修复项，见[本轮证据](../evidence/c04a/2026-09-14/local-container-source/README.md)。新 Core 40 文件固定摘要，CRM 178 文件操作工具原样复用；普通 PR 与远端 main 包含性另记交付记录。

实际 Docker 只读观察确认 20 表空来源；两实例临时测试库和实际控制 Schema 均为 0。六进程共享预览及旧副本/恢复依赖清单已整理，实际数据库和服务未切换。本组件不再列为开发阻塞，不重跑未受影响旧套件。继续三份实际来源的启动路径/特权控制、前向激活和完整批准恢复包；0.2 变化字节、CRM11/C04B 正式验收和观察期仍开放，整体 **60%（3/5）**。

## 2026-09-14：C04A 首写前恢复协调开发完成

已接通 CRM 已关闭且未首写目标的永久终止，以及 Core 在目标锁持有期间恢复旧来源；原目标 Control/Audit 行保留，旧 Enable 拒绝，源精确重放允许业务恢复写入。16 项新增与 1 项直接受影响检查分阶段通过，包含两个真实发布 CLI 场景；一次集中审查的请求解析问题已定向关闭，见[本轮证据](../evidence/c04a/2026-09-14/cutover-recovery/README.md)。本节为分支合并前状态，普通 PR 及远端 main 包含性另以交付记录核对。

本组件不再列为开发阻塞，也不重复已通过且未受影响的旧测试。下一步完成实际原生/Docker 来源的启动路径与特权身份控制、路由/服务停启恢复、前向激活协调，以及绑定 0.2 字节、实际组织/目标/日历的具体执行批准包。实际冻结、终止和切换均未执行；既有预览保持。CRM11/C04B 验收与观察期仍开放，整体 **60%（3/5）**。

## 2026-09-14：C04A 实际源端冻结组件

新增 [实际来源冻结入口](../../tools/CP6.Crm.SourceFence/ACTUAL-FREEZE.md)：同一事务绑定具体数据库身份、锁内检查摘要、审核材料引用和 20 表写入保护。精确重放只返回原回执；状态漂移或缺少绑定拒绝。原 rehearsal 生命周期保持，带实际回执的恢复副本不能通过该入口重新开放。

[本次证据](../evidence/c04a/2026-09-14/actual-source-freeze/README.md)记录新增验证及共享事务抽取的直接代表回归。新增17项与受影响3项已分批通过；中间失败原件、修改与复用适用范围保留。集中审查的回执格式问题已修复并定向通过，独立发布 CLI 已完成最小夹具检查。只读核查另确认原生和 Docker 两份空 CP6DB 共用 broker GUID；Docker API 实际以 sa/sysadmin 连接，必须独立控制生命周期。

这只是受控首次冻结/回执组件交付。实际 reopen/seal 仍拒绝，须继续目标门禁与路由恢复协调、跨源排空和特权启动路径隔离；准备完整恢复包后才请求实际执行批准。0.2实际采用签收、CRM11和后续观察期继续适用。没有实际库冻结、预览替换或生产部署。整体目标 **60%（3/5）**，历史检查点保留如下。

## 2026-09-14：C04A 实际来源检查组件交付

新增独立 [inspect-actual](../../tools/CP6.Crm.SourceFence/ACTUAL-INSPECTION.md)，精确绑定本机实例/库身份，锁内计数 20 表，并采集结构、权限、模块、SQL Agent 和迁移摘要。可拒绝预期 scope 漂移；原实际库写入拒绝边界保留。新增 11 个不同自动化场景分批通过，CLR 方法绑定审查问题已修复，未重跑未受影响的成功项。

[最终独立发布与实际来源证据](../evidence/c04a/2026-09-14/actual-source-inspection/README.md)确认 CP6DB 全部旧表为空、scope 复读一致、实际 freeze 被拒绝、控制 schema 未安装、测试库和临时登录无残留。实例内观察到 5 个启用 sysadmin 主体及 1 个启用 Agent 作业，仍须分类和隔离；原始主体/会话信息保留本机，公共证据仅包含审阅摘要。

整体目标 **60%（3/5）**：C01/C02/C03 已交付；C04A/B 尚未完成。继续真实 writer/作业/权限分类与受控执行接线、目标首写和路由恢复；0.2 实际采用批准、CRM11 和后续观察周期仍适用。本次没有业务/Schema 写入、预览替换或生产操作，scope 摘要不代表批准。下方为历史检查点。

## 2026-09-13：C04A 源端控制组件与真实空来源恢复演练

CRM02 的本地合同确认已由 [CRM PR #68](https://github.com/GTX537/CP6.CRM/pull/68) 关闭，用户兼任三类负责人且暂无替补；后续技术工作不再重复索取这些确认。本次实现独立的[源端保护工具](../../tools/CP6.Crm.SourceFence/README.md)，只允许在本机独立恢复副本执行 20 表原子冻结、围栏前恢复、单调 generation/幂等重放及不可逆封口。

[本地证据](../evidence/c04a/2026-09-13/README.md)记录 30 项真实 SQL 基础回归、审查修复后 10 项定向回归、13 项输入保护，以及实际 COPY_ONLY/CHECKSUM 备份恢复后的 39 项验证。最终 Release DLL 另执行三项真实副本冒烟。未改行为复用原执行来源，不把旧结果标为新代码重跑。源库 20 表仍为空、元数据未变，未安装源端保护；副本保留供检查。

两项 P2 已修复：备份默认目录必须是无重解析点的本机固定磁盘路径；恢复写入后立即重试同一 Reopen 保持幂等。四份状态摘要移除旧 C03 技术测试金额的重复展示，以通过原有公开扫描；原验收报告、摘要与对账结论保留。

**只完成源端组件和空来源恢复演练，C04A 整体仍未关闭。** 下一步继续目标物理 Schema/CRM11 适配，并补齐实际旧写入身份与作业盘点、排空、目标首笔写入接线和路由切换/回退。当前操作身份为 sysadmin，工具明确不宣称完整写入围栏；没有修改旧映射、现有预览、业务启动、生产配置或工作流。下方条目是历史检查点。

## 2026-09-12：C03 开发交付关闭，远端 main 已核验

真实 SQL 联合回归 95/95、Core 单元/HTTP 153/153、CRM ERP 114/114 全部通过且无跳过。集中审查发现的行数/金额边界、报价与商品数量/计价单位、并发 Account 绑定、按租户重试、读取整体超时和权限覆盖均已修复；原始失败与结果哈希保留在[验证索引](../evidence/c03/2026-09-12/local-test-results.json)。

[新一轮真实 Release 消息验收](../evidence/c03/2026-09-12/transport-attempt-4/README.md)七场景通过，新建 ERP SQL 库应用 135 个正式迁移，最终三张订单已按币种完成金额对账，并核验数量与计价单位；具体金额保留在原始技术验收证据中。[本地发布证据](../evidence/c03/2026-09-12/local-release/README.md)绑定 Core `a51938f8`、CRM `b3adb17d`、864 个发布文件哈希和 3 项实际 CRM API HTTP 检查；执行后全部文件摘要一致。

C03 已通过 [Core PR #105](https://github.com/GTX537/CP6/pull/105) 合入 `a9cf3abfa8fee13bda2c8f52c115c5adbf6ed917`，通过 [CRM PR #62](https://github.com/GTX537/CP6.CRM/pull/62) 合入 `362e6b1794c36e6e4abf9cd7eb7708a76e46c467`。[远端交付记录](../evidence/c03/2026-09-12/main-delivery.json)确认任务提交包含关系、完整 Git tree 相同、发布源码未变、37 个契约文件一致及 58 个归档摘要通过。合并 head/main 均无 Actions 运行；其他保护保留，原预览未替换。

C04A 仍等待 CRM02 批准列合同/migration map、DEC-CRM-004 和真实恢复副本/围栏/回退证据；C04B 仍等待 CRM11 生产切换、采用门禁和旧表只读观察期。当前条件未满足，未启动迁移或解除旧映射。CRM06/07 产品状态接线、CRM12 与 WMS/MES 下游另行验收；本地交付不代表生产上线。下方为历史检查点。

## 2026-09-12：C03 ERP 集成进入集中审查与交付验证

真实 ERP BP、报价和订单读取、六类请求/结果契约、同 SQL 事务的命令 Inbox/订单/结果 Outbox、持久重试及管理员审计重放已实现。报价采用显式客户接受和内容绑定；ERP 客户关联、冻结、币种及订单来源有独立约束。[实现与操作说明](../crm/C03-ERP-INTEGRATION.md)。

本地真实 Dapr/Kafka 七个场景全部通过；结果由实际 ERP handler 产生，含丢失确认、旧结果重放、broker 中断和 SQL 故障后的原命令恢复。[原始传输证据](../evidence/c03/2026-09-12/transport-attempt-3/README.md)保留两轮失败及执行时的源码、程序集和数据库身份。后续新增恢复操作另由 47 项真实 SQL 联合验证和 HTTP 权限/CSRF 测试覆盖，不把先前传输结果改称新代码重跑。

当前仍在集中审查、本地发布与正常 PR 交付阶段，尚未关闭远端 main 验证。CRM 消费者提供受控读取与契约适配；验收 probe 的流程状态不等同于 CRM06/07 产品功能。C04A/B、CRM12、WMS/MES 下游和生产迁移/部署继续保留各自前置条件。未触发 Actions，未替换既有预览。下方为历史检查点。

## 2026-09-12：C03 文档发布集合预登记

依据持续的 C 系列实现和文档授权，预登记已审查来源 `6cc6e10fae6b097735a94645d00af68b6cc5f989` 中 C03 工程说明及文档入口的精确摘要。原冻结产品、合同和 C01 文件摘要保留，完整集合匹配、缺件/漂移/混合版本拒绝继续生效；本准备任务不发布新说明或实现代码。

本地集合回归 174 项通过，受信任校验器对后续 C03 文档候选验证通过。回归的旧自动 push 假设已按现行仅手动策略修正，工作流文件未改动。后续实现仍须完成自身本地发布、正常 PR 合并与远端 main 核验；本登记不等于 C03、CRM 产品链或生产验收完成。

## 2026-09-10：开发中局部验证，任务收尾集中审查

用户已确认将精简后的审查节奏写入长期规则：[AGENTS.md 审查与验证节奏](../../AGENTS.md#审查与验证节奏强制)。以完整任务或 PR 为单位集中审查，开发中的相关小步骤连续推进；修复后增量复查和定向回归，只有影响扩大或出现实质风险才扩大检查。低风险文档改动不编译，高风险业务保留定向及专项验证；纯风格和无关重构建议可进入后续事项。

本次仅更新规则与四份项目记忆，沿用现有本地验证、成功结果复用和普通 Actions 仅手动策略；没有修改 CI 触发、分支保护或生产发布门禁。C01/C02 等业务交付状态按下方原记录保留。

## 2026-09-10：C01/C02 按本地验收政策完成开发交付

C02 已通过 [Core PR #101](https://github.com/GTX537/CP6/pull/101) 合入 `fed92b8f9b73638a9f8d7550418ffdd85df76010`，并通过 [CRM PR #60](https://github.com/GTX537/CP6.CRM/pull/60) 合入 `4485261addb2a4d827902a51cbc500b20a78ff08`；远端提交包含关系和完整合并 Git tree 相同均已核对。C01 的 CRM/Core 最终证据已由 PR #59 / #100 交付，Platform 最后记录由 [PR #57](https://github.com/GTX537/CP6.Platform/pull/57) 合入 `30bd23af6808d217a23878bd9437513043c52834`，未重建或覆盖七个正式 `0.10.2` 包。

C01 的 72 项实际验收、C02 的 13 个不同真实传输场景和三类各 100 个拒绝样本均有原始证据。另有三个实际 Release HTTP/消息场景、生产 Vue/Next 的真实浏览器密码/PKCE、工作区及安全 Cookie 验证；1,060 个发布文件摘要已核对。执行 SHA 与合并 SHA 分别记录，未改动的源码/依赖结果复用；报告、失败历史和覆盖索引不回写成新的执行。[交付锚点与复用边界](../devops/evidence/local-release-20260910/main-delivery.json)。

用户因 Actions 额度耗尽明确授权本地发布验收：Core 的五项及 Platform 的五项普通必需检查已移除，其他保护逐字段核对保留。Core 七个普通工作流与 Platform 主验证现在只手动触发；CRM 普通验证保持暂停。相关交付 head/main 均没有新 Actions 运行，不补造远程成功状态，正式发布和生产审批仍适用。

**C01/C02 开发交付关闭；下一阶段为独立的 C03 ERP 设计与实施。** C04A/B 仍须满足数据迁移、切换与回退前置。当前本地预览保留给 owner 检查，独立端口、四个自有数据库及专属消息项目仍在运行；没有替换既有环境或声称完成生产部署。下方待交付条目是此前历史检查点。

## 2026-09-10：C02 本地 Release 发布与实际浏览器检查通过

在用户授权的本地验收政策下，Core/CRM Release API、生产 Next Web 及同源码 Vue 产物已实际运行；三个真实 HTTP/消息场景与 Chromium 密码/PKCE、工作区和安全会话检查通过。此前 13 个真实场景及三类各 100 个拒绝样本按原执行来源复用，不重复编译或改称生产验收。发布文件逐项哈希、来源、原始失败和复现入口见 [发布检查记录](../devops/evidence/local-release-20260910/README.md)。

Core 编译的四次内存失败、Web 页面参数错误及隔离预览代理/检查脚本的失败均保留；对应问题已按范围解决。预览留给 owner 本机查看，使用独立端口、四个独立数据库与专属消息项目；原环境未替换。普通 Actions 未触发，C01 已正常交付。C02 代码和本批证据准备正常 PR 合并及远端 main 核验；完成前不关闭依赖，C03 随后推进，C04A/B 保留迁移与切换前置。

## 2026-09-10：C02 本地真实链路与 300 个拒绝样本通过，按授权继续本地发布

本地真实 SQL、Core/CRM API、密码/PKCE、服务令牌和 Dapr/Kafka 已覆盖 13 个不同场景。第六轮六项定向通过，复用第二至第四轮未受影响的通过项；五轮失败及全部样本原件保留，不改称后来完整重跑。用户禁用、令牌撤销、租户停用各 100/100 在 30 秒内拒绝访问；用户禁用 p99 599.234 ms；令牌撤销 p99 585.965 ms；租户停用 p99 590.952 ms。这些是本地受控负载结果，不代表生产性能。缺口和冲突自动源端核对、真实崩溃/丢失确认重放、普通积压下优先投递及两租户隔离均已有证据。

最新实际运行源为 Core `2210209ee50cf40467b10c9d5d6b912b9e568678`、CRM `7e3f7604af8b36b33d3dd746f023a6bd3ee272cb`，固定 Platform `0.10.2` 和契约 bundle 不变。公开证据及逐项来源在 CRM 的 `docs/delivery/c02/real-transport/2026-09-10/`；各轮资源清理通过。受影响配置/密码更新/CSRF/服务读取缓存/自动核对回归已通过，未变化的组件结果复用。没有新镜像构建、Actions 或生产部署。

用户因 Actions 额度耗尽明确授权以本地发布检查替代必需检查。Core 政策 PR #99 已集成，七个普通工作流仅手动；CRM crm-validation 已暂停，普通 PR/合并不触发 Actions。C01 原始证据与最终状态分别经 CRM PR #59 / Core PR #100 正常合入，远端包含关系及相同 Git tree 已核对。C02 已同步最新 main，继续隔离 Release 发布、实际 HTTP 检查与正常 PR 交付；完成前不关闭 C02。生产发布门禁及 C03/C04 原依赖保留；下方待交付与禁止移除检查的条目为最新授权之前的历史状态。

## 2026-09-10：C02 崩溃恢复通过，服务撤销入口与读取缓存修复

真实传输已覆盖前九项场景，包括发送端提交后崩溃、普通消息积压下优先投递、接收端 Inbox 提交后丢失确认与重复回调。第三轮在服务撤销被通用 CSRF 校验拒绝处失败，原始结果和通过的资源清理记录保留。Core 仅将使用显式客户端凭证的 `/connect/service-revocations` 精确路径纳入既有 CSRF 例外，15 项定向中间件回归通过。CRM 在读取接口返回 401/403 后移除对应旧服务令牌，让下一次有退避的对账尝试重新签发；503/409 保留缓存，4 项定向回归通过。匿名撤销拒绝、真实服务撤销和三类 100 样本拒绝时延仍待定向验收；尚未通过远端 main，也未触发 Actions。

## 2026-09-10：C02 真实业务更新修复

第二轮实际 Core/CRM/SQL/Dapr/Kafka 验收通过前七项场景，随后 PUT 用户更新因实体的必填密码约束返回 400。更新接口现使用独立请求类型，允许省略或留空密码；创建仍要求密码，名称与字段长度约束保留。5 项定向回归证明普通编辑/禁用保留密码哈希且不强制改密，显式重置仍哈希并撤销刷新令牌。实际传输中已通过的七项复用；发送端崩溃、接收端崩溃、服务撤销和三类计时仍待定向运行。失败证据保留，临时资源清理通过；C02 及受保护 main 交付尚未完成。

## 2026-09-10：C02 真实启动暴露租户配置绑定缺陷，定向修复通过

首次 Core/CRM 实际进程验收完成隔离 SQL 初始化后，Core 因读取客户端无法映射租户而退出；该次不计通过，四个临时数据库和专属 Dapr/Kafka 资源已清理。.NET 8 默认绑定器不会填充 Guid 键字典，现对启用的 C02 租户映射显式解析，拒绝无效或空映射，并继续核对每个读取客户端。实际服务注册、两租户映射、配置覆盖、无效输入和默认关闭共 7 项本地回归通过；[原始结果索引](../../eng/crm/identity-events-fixture/evidence/2026-09-10/configuration/summary.json)保留测试来源。跨服务 13 场景与三类实际撤销计时尚待继续；未触发 Actions、推送或变更分支保护。

## 2026-09-10：C02 发布字节修复与真实 Dapr/Kafka 定向验证

- CRM Inbox/投影/对账已保存于本地 `250e674`；43 项定向单元、28 项 SQL/HTTP 场景的最终覆盖、受影响认证兼容、9 项浏览器检查和 25 项来源授权检查已通过，仍非生产或完整传输验收。
- 真实 Dapr 1.18.2/Kafka 4.3.1 发现默认发布会改变 JSON 数据哈希；Core 发布端增加 `rawPayload=true`，接收端保留 `rawPayload=false`。最终三项字节/元数据探测通过，错误实际分区键被拒绝；前两次失败与错误测试预期保留在 `eng/crm/identity-events-fixture/evidence/2026-09-10/transport-byte/`。
- 仅本地编译受影响 fixture/API 并运行隔离传输探测，未触发 Actions、构建镜像或变更运行中的开发栈。此前 Docker 构建只覆盖修复前组件版本；未将其改标为当前镜像结果。
- 下一步仍为实际 Core/CRM SQL、故障恢复、优先队列及三类 30 秒传播采样。正常远端集成继续受必需检查/Actions 额度策略阻塞，完整 C02 未完成。

## 2026-09-10：C02 Core 已完成本地组件验证，CRM 与传输继续实施

- 五类固定 Schema、版本快照、普通/优先 Outbox、业务与直接 SQL 撤销、真实服务令牌记录、初始化及租户绑定读取 API 已实现。复用签名包 `[0.10.2]`，合同索引 SHA-256 为 `24df72e9446723fa4cf3ad12b7e273ff937ef9b0ed154b8c926b1bac6c7eab63`。
- 定向单元测试 123/123，最终真实 SQL/HTTPS 批次 23/23、零失败/跳过。覆盖历史迁移保真、并发版本、三种故障原子回滚、调用方外层事务、实际业务失效入口与服务 API。原始报告和前序失败保存在 [组件证据](../../eng/crm/identity-events-fixture/evidence/2026-09-10/README.md)，工作树执行事实不改标为 main 验收。
- 本地 Linux Docker 构建已成功，签名与 locked restore、发布内容及构建秘密不进入镜像已检查；后续文档修改复用结果，不重建。Actions 的仓库包读取权限尚未验证。功能默认关闭，启用要求 SQL `MultipleActiveResultSets=False` 以支持事务保存点。
- 用户已禁止普通验证触发 GitHub Actions；本地策略分支 `b483e62b` 已将七类普通流程改为手动，但受保护 main 的必需检查阻塞集成。C02 暂存于本地任务分支，不 push/建 PR 间接触发运行，不修改分支保护或伪造状态。CRM Inbox/投影/对账、实际 Dapr/Kafka 故障恢复与三类 30 秒传播样本尚未验收，完整 C02 不计完成。
- C01 真实身份验收已为 72/72；CRM 源码经 PR #58 合入 `c0205517`，合并后六项实质检查成功、汇总作业因额度限制未启动。C01 三仓证据分支保留待正常集成，不重跑已成功的真实验收。

## 2026-09-10：C02 设计已确认，准备实施

- 已根据冻结的事件与投影契约整理 [C02 设计](../superpowers/specs/2026-09-10-c02-identity-events-design.md)：身份变更与 Outbox 同事务、角色完整授权快照、实际 CRM grant 与服务令牌撤销、Inbox 投影、初始化及 15 分钟对账。
- 用户已明确确认按设计继续实施；[实施计划](../superpowers/plans/2026-09-10-c02-identity-events.md) 按 Core 原子事件、CRM 投影/对账和真实传输验收执行。C02 尚未实现或验收，C01 最终真实身份验收仍是实施前置；C03、C04 保留原依赖与迁移条件。本地 Docker/SQL/Kafka 已可用，后续跨仓验证采用隔离实例和真实传输。

## 2026-09-10：C01 跨仓真实验收 72/72 通过

C01 的实际密码/PKCE、服务令牌、固定 Platform/CRM 消费者、严格拒绝、缓存失效/恢复和真实密钥轮换全部通过，**72 通过、0 失败、0 跳过**。Core 实际执行源 `fb55a877de8ee8f9d27fd3bf8e73c824a21549e8`；CRM 实际执行源 `37cf0e58ff146ed58582768cf2c91e3c9fbe81cf` 经 [PR #58](https://github.com/GTX537/CP6.CRM/pull/58) 正常合入 `c02055178d96acf4c15ece8c5ba0f51b01b86f2c`，包含关系及相同代码树已核对。源 PR 七项适用 CI 通过；main 六个实施/适用性作业通过，但 GitHub 计费限制阻止汇总作业启动，整体 CI 失败。用户于 2026-09-10 因 Actions 额度耗尽明确授权本地验证替代必需检查；本批证据按正常 PR 交付，复用已完成的真实验收，不补造远程成功状态。Platform 七个不可变 `0.10.2` 包仍绑定发布源 `fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`，发布原件 main 为 `0944d808057c6cf138867a4d2b4b96247b79e14f`。

公开[原始 summary、JUnit 与五次尝试记录](https://github.com/GTX537/CP6.CRM/tree/main/docs/delivery/c01/real-identity-2026-09-10) 保留实际执行 SHA；成功 summary SHA-256 为 `ae779716b0edbf07b6b37dfea5774ba7d564c06739286f20efa124324d0b5014`，JUnit 为 `3003542d3d98c143df6331da9cbd047877de788bfcfbe5f53acf396ce5932ff1`。真实预发布不少于 60 秒，旧键在最后实际旧令牌到期加默认 60 秒偏差后移除；入口退出 0，自有 SQL 与进程清理完成。四轮失败不改写成成功；相同代码树和输入的实际结果复用。

用户于 2026-09-10 批准 C02 设计，现继续组织/权限/撤销事件的原子发布、CRM 投影与对账，之后推进 C03。C04A/B 保留原前置条件；本次是开发验收，不是生产部署。下方 C01 尚待完成的记录为历史检查点，以本条为当前状态。

## 2026-09-10：用户授权本地验证替代 Actions 必需检查

用户因本月 Actions 额度耗尽，进一步明确授权移除必需检查，并在本地发布检查。已仅移除 Core main 的五项 Actions required status checks，逐字段确认其他分支保护保持不变；正常 PR、会话解决、管理员约束及禁止强推/删除仍生效。七个普通工作流在合并期间暂停，配置经 [PR #99](https://github.com/GTX537/CP6/pull/99) 正常合入 `54096b3fc7f745f0199de9c20a9b7859e569d29e` 后，已恢复为仅手动入口；R2 候选、发布、部署和其他手动工作流状态未变。原始前后设置保存在 `docs/devops/evidence/local-gates-20260910/`。

配置保留普通 GitHub workflow_dispatch，Azure Artifact 桥同步改为手动，防止等待不存在的 GitHub 成功产物。本地发布只作隔离验证，保留源码/发布文件哈希与实际 HTTP 证据，不覆盖既有环境或替代生产发布门禁。远端已核对包含政策提交且 Git tree 相同，七个入口均 active 且仅 workflow_dispatch；该 head 与合并提交均没有 Actions 运行。本地 Release 发布验证继续推进。下方策略尚待集成条目为历史检查点。

## 2026-09-10：普通 CI 改为手动，本地验证优先，集成待完成

- 用户要求停止普通编译/测试的自动 GitHub Actions；7 条工作流在独立任务分支改为 `workflow_dispatch`，规则写入根 `AGENTS.md`。[策略与依赖记录](../devops/LOCAL-VALIDATION-POLICY.md) 列出完整变更，正式发布、部署、分支保护保持原状。
- 当前任务没有排队或执行中的相关运行，实际取消 0 项。旧 main 的 `pull_request_target` 和五个必需检查仍在；未获新授权前不创建会触发运行的 PR，不绕过检查，不宣称 main 已集成。C02 原任务继续本地推进，复用已经成功的相关结果。

## 2026-09-09：C01 服务端已交付，真实消费者验收进行中

- [PR #97](https://github.com/GTX537/CP6/pull/97) 的公开文档集合登记已正常合入 `d7a8b1699cc47ebcf3f8507dd1dc1592756cedfb`，七项 PR 检查及该 main 的五条工作流均成功。下方登记和本地验证条目是历史检查点。
- [PR #96](https://github.com/GTX537/CP6/pull/96) 的最终 head `26be785313b5562a4cd9ca1aeb216c594cc8c70f` 通过七项检查，于 2026-09-09 正常合入远端 `main@22453814f7d465abbf48ff6cb03e794765e490b1`。远端祖先关系与合并前后完整 Git tree 相同已复核；服务客户端、真实单租户签发、JWKS 缓存及密钥轮换的生产者源码交付完成。
- 合并提交的 [client-contract](https://github.com/GTX537/CP6/actions/runs/34395263850)、[SQL integration](https://github.com/GTX537/CP6/actions/runs/34395263357)、[PRD](https://github.com/GTX537/CP6/actions/runs/34395264036)、[public contract](https://github.com/GTX537/CP6/actions/runs/34395264338) 和 [Space integrity](https://github.com/GTX537/CP6/actions/runs/34395263393) 均成功，共五条工作流、六个作业；合并后本地身份回归再次 116/116、零失败/跳过。先前全后端 3059/0/19 和真实 SQL/HTTP 48/48 的边界与记录继续保留。
- 真实 CP6 SQL/密码登录/PKCE、实际 CRM 客户端及固定已发布 Platform `0.8.0-alpha.2` 的跨仓诊断为 7 通过、4 失败、零跳过：ID Token 作 bearer、同一可信 RSA 签名但未知 kid、两种 audience 末尾多 `/` 均错误返回 200。另一个仅观察 CRM 正常缓存的真实元数据诊断为 6 通过、2 失败、零跳过：实际 JWKS 的 60 秒 must-revalidate 及随后的退避请求误用旧缓存；独立、明确标注的宽松缓存头用例在 899 秒接受、900 秒拒绝。两项诊断都属于修复前证据，不是 C01 验收成功。
- C01 继续实施消费端严格令牌/缓存修复，并准备新的不可变 `0.10.2` 正式包及固定真实消费验收；必须取得完整轮换、缓存/刷新/最大年龄及失败关闭的源 SHA/包摘要绑定 summary 和零跳过 JUnit 后才关闭 C01，再推进 C02/C03。C04A/B 保留迁移前置条件，本次不表示生产部署、生产密钥轮换或新包已发布。

## 2026-09-09：C01 生产者本地验证通过，待远端交付

- 按用户确认的 [C01 设计](../superpowers/specs/2026-09-09-c01-identity-design.md) 实现独立服务客户端、真实单租户绑定、CP6.Services 签发、JWKS 60 秒缓存和签名密钥轮换。116 项身份回归与 48 项真实 SQL/HTTP 测试全部通过、零跳过；全后端 3059 通过、0 失败、19 个既有跳过单独记录。生产者契约和独立质量审查通过，正常 PR/main 交付继续推进；[运行手册与验证记录](../crm/C01-SERVICE-IDENTITY.md) 保留准确边界。
- C01 尚未关闭：后续必须使用固定真实 Platform/CRM 消费者取得跨仓缓存、刷新、最大陈旧期与失败关闭验收。再推进 C02/C03；C04A/B 仍受原迁移前置条件约束。下方设计待审阅条目为历史状态。

## 2026-09-09：C 系列启动，C01 设计待审阅

- C 系列已启动，当前为 C01 设计阶段。已核对现有 CP6.Web 身份桥、服务令牌缺口及签名密钥轮换边界；[设计草案](../superpowers/specs/2026-09-09-c01-identity-design.md) 待审阅。先实施生产者服务签发与轮换，再做真实跨仓契约验收，之后推进 C02、C03；C04A/B 保留前置条件。尚未新增代码或取得 C01 完成证据。

## 2026-09-09：C01 公开文档完整集合登记

- C01 生产者 [PR #96](https://github.com/GTX537/CP6/pull/96) 已完成本地实现、自动化验证和独立审查；当前文档门禁要求先在受保护 main 登记完整文档集合，再公开对应内容。本登记固定已审查源 `2005c05fdf764ef21525d6374c1c9bf37f756050` 的 `c01-service-identity-20260909` 集合，仅新增服务身份运行手册摘要并更新首片文档的 C01 导航说明摘要。
- 用户于 2026-09-09 确认 C01 设计并授权继续实施。本次保持两套历史完整集合及历史审批记录，仍拒绝任意文档漂移、缺失、新文件和混合版本；受保护验证器不从候选仓库加载信任。登记分支不修改 `docs/crm` 内容；正常合入并通过 main 检查后，再由 PR #96 发布准确匹配的文档。
- 这里只处理文档登记前置，不表示生产者已经合并、完整 C01 跨仓验收完成或环境已部署。
- 本地验证：138/138 集合与触发器检查、59/59 PRD 负向检查通过；受信任验证器对旧集合和 PR #96 完整集合均通过，历史 GitHub 审批证据有效。独立规格与质量审查通过；登记 PR #97 已通过七项 PR 检查并正常合入远端 main `d7a8b1699cc47ebcf3f8507dd1dc1592756cedfb`，登记提交的 main 检查仍在运行。

## 2026-09-09：CRM 登录页 CSRF 误报修复

- 原因一：匿名 OIDC 登录页的 profile 探测复用业务 HTTP 客户端，401 会触发缺少 CSRF Cookie 的 refresh，产生 403 和“登录已过期”提示。增加仅用于登录页的被动画像读取；失败交还密码登录，不刷新、不改路由、不弹业务错误。
- 原因二：CRM 全局退出后 CP6 的本地登录标记可能仍在，应用启动会提前请求业务权限并触发同一错误。登录页不再预加载工作区权限，普通已登录工作区保持原逻辑。
- 两个问题均先复现并得到失败自动化测试，再修复。16 项定向测试、Vue 类型检查与构建通过；浏览器回归覆盖匿名密码登录、已有会话直接继续、退出后重新登录。默认 DEV 测试账号与业务样例仅用于内部测试；未更改 Cookie/CSRF/OIDC 后端安全策略或生产配置。

## P10 S00–S06 非部署型候选审计闭环（2026-09-09 UTC）

- 当前决定为 **Frozen / Consumable**，候选 `v0.10.1-p10.1`；以本次审计条目和四份台账正常合入远端 main、必需 exact-main 检查成功为生效条件。下方 Candidate / No-Go 与待审批条目保留为历史检查点，不再表示当前候选状态。
- [PR #93](https://github.com/GTX537/CP6/pull/93) 经七项检查合入 `f992adc3295722a0edae151c454665a4be4b689b`，该 main 五条 GitHub workflow / 六作业成功。三条受保护流程同源：validation **34310410625 / 1**、publication **34312917625 / 3**、独立 audit **34317792175 / 1** 均已实际成功；正式全量 1651/1651、零失败/跳过，19 项 gate Success，原生扫描 HIGH/CRITICAL 均为 0。
- 正式七包 `0.10.1` 来自 Platform `3ff27e26962dcfd722887afb80a4306010dd9ee1`；CRM 消费 `a31ca0e323418f7e4108cc6220c0f5fa132e7fc2` 与 PR #47 前向证据均已绑定。候选 SHA-256 `bc3cae8ede16e26a909519ec250fbdf5989b4aa4edfcd6b39397e0958a59ea4f`，OCI digest `sha256:e743e911380a4ed6732685ea33694b6ffb910051c20621388b6903386947296b`。
- [Append-only 审计与原始证据](../devops/p10-audit/README.md) 留存三份精确 Artifact ZIP 和 27 个公开内容文件，逐项 hash/长度已核对；entry `5adde0e29c8cf755d3ad8dfe6e050779d787faeb7040e584428d75dfa0f84558` 固定所有来源、workflow blob、run/attempt/job、trust v1 与对象身份。NuGet 作者仍为 PinnedSelfSigned，`publicCaTrusted=false`、`internallyTrusted=true`；正式 RFC3161 与独立签名没有豁免。
- 只读结论为 `VerifiedNonDeployable`、`candidateAccepted=true`、`deployable=false`。只完成 P10 Platform reference；不授权 WMS/CRM System release、CRM runtime、Portal 身份补造、DEV/UAT/PROD 或迁移。历史失败/孤立对象保留，没有覆盖远端候选或导出私钥。

## P10 已定位 gRPC 高危并获批最小修复，完整本地镜像验证通过（2026-09-09 UTC）

- [PR #92](https://github.com/GTX537/CP6/pull/92) 已正常合入 `2618487466ca529e261b70031d97724dae01ed5e`，七项 PR 检查、main 五条工作流/六个作业和合并后 17 项回归通过；此前“六条 main 工作流”用词在本次更正。
- Owner 批准的 [validation 34303646636](https://github.com/GTX537/CP6/actions/runs/34303646636) 全量 **1650/1650，零失败/跳过**，随后原严重性门禁拒绝原生 SARIF 中唯一 HIGH `CVE-2026-84445`：`opt/cp6/cosign` 的 `google.golang.org/grpc v1.83.1`。诊断 Artifact `10086729181` 已保留；不是普通 WARN，签名/finalize/正式交接未执行。旧 run 未留存报告的根因仍不能回溯推定。
- Owner 随后批准独立 `codex/p10-grpc-security-patch` 分支，仅将 gRPC 升为 `1.83.2`，新衍生工具标识 `3.1.3-cp6.2`；上游源码、Go 1.26.8、其余依赖与信任/扫描阈值不变。真实输出元数据只改变该依赖，旧工具摘要不作为回退接受。
- 新产物 Windows 全量 **1651/1651，零失败/跳过**；实际完整诊断镜像覆盖 9 个系统包、应用/运行时 .NET 依赖及 253 个 Go 包，保留 LOW 7 / MEDIUM 5 / UNKNOWN 3，HIGH/CRITICAL 均为 0。数据库更新时间与失败云端相同；实际 UID 1654、只读、禁网镜像的七项密码学检查通过，两个本地报告身份仍被正确拒绝。
- 两轮独立重建的二进制与元数据逐字节相同；上游 429 项测试/子测试通过，无测试失败/跳过（另四包无测试），十项输入保护、format、四条 workflow actionlint 通过。该补丁的 PR/main 交付及新正式 validation 尚待执行；原始 hash 与边界见[安全构建记录](../superpowers/plans/2026-09-08-p10-cosign-security-build.md)。不把本地修复证据当作候选交付；P10 仍 Candidate / No-Go、`deployable=false`，候选身份仍待 owner 确认。

## P10 前端审计修复已交付，正式扫描失败待原始报告定位（2026-09-09 UTC）

- [PR #91](https://github.com/GTX537/CP6/pull/91) 已正常合入远端 `main@a857d1fdfd81821904d5d5b7e3df5348dd74c530`；七项 PR 检查和五条 exact-main 工作流（六个作业）成功，Web 1102 项测试通过、npm 审计零漏洞。下方 Vitest 尚待验收为历史检查点。
- Owner 批准的 [validation 34298959521](https://github.com/GTX537/CP6/actions/runs/34298959521) attempt 1 在该源码上通过真实七包/CRM 输入、cosign 复现、1648/1648 全量测试（零失败/跳过）及 OCI 构建推送，随后原生扫描步骤退出 1；签名、finalize、正式交接均未执行，未留下 Artifact。
- 已推送但未签名的实际 digest 为 `sha256:f9362a6e10e8ba5934e3aed66c557fe4263fb7df2d6b3f14793670fe30bc00eb`。相同 Trivy 0.74.0 对它的 Windows/原生 Linux 只读复扫均未报告 HIGH/CRITICAL；这不覆盖云端失败，也不足以证明误报或具体根因。原日志缺少失败命令及 SARIF 内容，vendor severity WARN 本身不是已确认原因。
- 独立分支补充 Syft/Trivy/严重性检查阶段、工具/DB 版本、规则级严重性计数及阻塞规则 ID，并仅在 scan 失败时保存两个原生报告，精确 SHA/run/attempt 命名、不可覆盖、七天诊断保留。原 HIGH/CRITICAL 判定、报告字节、签名/交接和审批边界不变。
- 新增两项回归先 RED 后 GREEN，17 项相关测试、format、三条 workflow actionlint 通过；真实 SARIF 的原判定通过，内存高危负例仍退出 1。该补丁的 PR/main 交付和新获批云端验证待完成，不把诊断改善称为根因修复。P10 仍 Candidate / No-Go、`deployable=false`，候选身份仍待 owner 确认。

## P10 预检已通过并合并，前端依赖安全审计阻塞主分支复验（2026-09-09 UTC）

- 独立 Linux [preflight 34254585813](https://github.com/GTX537/CP6/actions/runs/34254585813) 在 owner 批准后完整成功：精确源码 `f4ac824e86e3067a4b82df57a796bdef6f9fe53b`，1648/1648 测试通过、零失败/跳过，真实七包及两平台 cosign 摘要核验通过。诊断 Artifact `10081995323`、归档 SHA-256 `c13253c97ab78e17130453a30556cbddf94003206f9ec3c3dd52e5f0b7fba1bd`；它不是正式候选交接。
- [PR #90](https://github.com/GTX537/CP6/pull/90) 的八项检查成功后正常合入 `main@241d2d3efc3da94bec8830289ad185f1725f1385`；合并树与受检源码一致，合并后 Windows P10 全量 1648/1648、零失败/跳过。下方“预检/PR 尚待执行”为历史检查点。
- 该 main 的 Android、SQL、CRM 和 Space 检查成功；[client-contract 34293825913](https://github.com/GTX537/CP6/actions/runs/34293825913) 在 .NET/Web 测试通过后，因 R2 source gate 的 npm 审计发现 Vitest / `@vitest/mocker` 4.1.9 的两项 moderate 报告而失败。此前同一依赖锁在 PR 中审计为零；本轮没有把 P10 测试通过当作整条 main CI 成功。
- Owner 已批准在独立分支将 Vitest 升至修复版 4.1.11，并允许适度精简门禁。实施范围限测试依赖及其必要锁文件变更；按风险不重复执行未改动的 P10 本地全量，完整 Web 验证由既有 GitHub CI 承担。npm 审计阈值、必需检查、正式托管全量、签名和人工审批均不削弱；本机业务服务与全局 Node/npm 不变。
- 最小补丁已将 Vitest 及七个已锁定的 `@vitest/*` 包从 4.1.9 更新为 4.1.11；逐包核对 npm 官方元数据与 SHA-512，其他 460 个锁记录不变。Node 22.22.0 / npm 10.9.4 的锁文件安装预检成功，原失败命令的独立安全审计回归为零漏洞；这不代替远端实际 `npm ci`、类型检查、单测和构建。
- 依赖修复的 PR/main 验收及新的正式 validation 尚待完成。候选标识仍待 owner 确认；P10 保持 Candidate / No-Go、`deployable=false`，没有候选发布或生产部署。

## P10 获批独立托管预检，分支代码与读取边界已就绪（2026-09-08 UTC）

- Owner 同意独立 Linux 预检并要求尽快完成 P10。新增仅当前修复分支的预检 workflow、小型正式包输入工具和自动化回归；不修改既有时间/签名/漏洞门禁。云端成功仍待实际运行，不能把本机 WSL 失败改记为通过。
- 四项新增回归先 RED 后 GREEN；输入工具七项路径/凭据拒绝检查通过，实际下载的七个正式包逐一匹配既有 SHA-256。用这些真实包重跑 Windows 全量 **1648/1648 通过、0 跳过**；locked restore/build、format 和四条 P10 workflow actionlint 通过。
- 既有独立 `P10_CRM_ACTIONS_READ_TOKEN` Environment 原来仅存同名读取凭据且无运行历史；已新增 owner `GTX537` 审批和唯一 `codex/p10-native-scan-preflight` 分支策略。Secret 元数据时间未变、值未导出；正式候选 Environment 仍为原 main-only/owner 审批。
- 下一步正常推送任务分支、取得实际云端审批及全量成功，再依次 PR 必需检查/合并、exact-main 冒烟和检查、正式 validation、候选发布及只读审计。候选标识仍待 owner 选择，P10 仍 Candidate / No-Go；没有发布签名、Registry/R2 写入或生产部署。[预检执行记录](../superpowers/plans/2026-09-08-p10-hosted-preflight.md)。

## P10 回到发布修复：Docker 已恢复，Linux UTC 倒退仍阻塞全量门禁（2026-09-08 UTC）

- Owner 报告已启动 Docker，并明确目标仍是解决发布报错。实测七个业务容器运行，DB/MQ/Redis/Kafka healthy；Web、API live/ready 均 HTTP 200。此前冷备/启动失败记录保留为历史，不再代表当前业务状态；本轮没有再次重启、清理或改配置。
- 对同一冻结测试程序集和安全构建二进制重新执行：Windows **1644/1644 通过**；Linux **1643 通过 / 1 失败 / 0 跳过**。唯一失败是读取 `crm-pr-linux` 原始归档时结束 UTC 比开始早约 6.341 秒，原有顺序断言正确失败；不是签名或漏洞修复回归。
- Owner 说明本机使用太平洋时间；只读 `Get-TimeZone` 实际返回 `Eastern Standard Time`、当时 UTC−04:00。未改时区。测试比较 UTC；随后单 CPU、断网探针独立记录约 −13.522 秒/+14.149 秒跳变，显示时区差异不能解释该现象。
- Trivy 参数修正和 cosign 安全衍生构建仍在任务分支；format、三条 workflow actionlint、完整分支 diff 检查通过。现有 PR 门禁不执行 P10 全量测试，受保护 P10 Environment 仅允许 main，不能作为分支预检的绕行入口。
- 建议另行批准独立、人工审批、只读的托管 Linux 预检边界，再验证同一完整测试集；尚未新增 workflow/Environment、导出凭据或修改现有保护。按必需门禁失败不得合并，P10 仍 Candidate / No-Go，未 PR/合并/dispatch。报告摘要见[本轮记录](../superpowers/plans/2026-09-08-p10-cosign-security-build.md)。

## P10 Docker 冷备已核验，第二个端点隔离受系统拒绝（2026-09-08 UTC）

- Owner 已批准先冷备再做不清空原盘的恢复。Docker 残留进程按身份核实后退出、两个 WSL 发行版停止；原数据盘以只读共享锁保护复制和完整 SHA-256 核验。109912784896 bytes 原盘/副本摘要一致：`56f2644ad04eca1674a131a88b73d9ce02def44eaf44e54b65a58f63cb2e4427`，原盘修改时间未变，副本设为只读。小型 WSL 系统盘和当前配置也已复制并逐一核验。
- 备份在本机私有目录，ACL 仅当前账户/SYSTEM/Administrators；未上传。它是同盘软件恢复回退副本，不是独立介质灾备，也不证明文件系统/数据库内部完整性。
- 已确认默认 Docker 数据目录的既有目录联接仍指向原数据盘，未迁移或修改数据路径。第一个 `Docker/run` 临时目录已改名留存而非删除；随后启动错误变为 `docker-secrets-engine/engine.sock` 无法访问，尚未启动 Linux engine。
- Docker 再次退出后，第二个仅含该零字节端点的目录被 Windows 拒绝重命名。账户已有完全控制权限、没有 Docker 进程；没有更改 ACL、接管所有权或删除端点。下一步建议在 owner 保存其他工作后尝试完整 Windows 重启，尚未执行，不保证它必然修复。
- 七个业务容器仍未恢复，新的时钟/Linux 全量重验未运行；P10 保持 Candidate / No-Go，未 PR/合并/dispatch。[恢复记录](../superpowers/plans/2026-09-08-p10-cosign-security-build.md)保留完整结果与边界。

## P10 获批 WSL2 重启后 Docker 启动受阻（2026-09-08 UTC）

- Owner 已另行批准完整 WSL2 重启。先正常停止 Docker Desktop，再执行一次 `wsl --shutdown`，确认 Ubuntu 和 docker-desktop 均 Stopped；未修改系统时间、时钟源、WSL 配置或测试门禁。
- Docker 命令行启动超时；随后启动已安装 Desktop 程序，后端日志定位到本地 `dockerInference` 运行时端点无法访问，初始化 Inference manager 失败。当前七个业务容器尚未恢复，本轮时钟/全量测试尚未开始。
- Owner 随后确认点击了错误窗口的 `Reset to factory defaults`；本任务未执行该动作，仍不能据此推断重置完成或数据已丢失。原数据盘文件仍为 109912784896 bytes、修改时间未变且 `vhdxfile` 标识可读，但内容完整性未验证。暂停进一步启动、删除端点或配置变更；先请求约 110 GB 的冷备，再考虑保留原盘的恢复。
- 下方 Docker 已恢复的记录属于前一次 Docker-only 重启，不代表本次状态。P10 仍 Candidate / No-Go，未 PR/合并/dispatch；最新事实见[安全构建记录](../superpowers/plans/2026-09-08-p10-cosign-security-build.md)。

## P10 Docker 重启后的 Linux 重验仍失败（2026-09-08 UTC）

- Owner 另行明确批准 Docker Desktop 重启后，已执行一次重启；七个业务容器恢复运行，DB/MQ/Redis/Kafka 均 healthy，Web 和 API live/ready 三项本机 HTTP 探针均为 200。没有关闭全部 WSL、修改系统时间或时钟源。
- 初次十秒时钟采样没有跳变，但随后同一冻结输入的 Linux full suite 为 **1643 通过 / 1 失败 / 0 跳过**。Messaging 实际包下载测试的开始 UTC 晚于结束 UTC 约 9.195 秒；因此不能视为环境已修复，也不能合并失败门禁。
- 独立单 CPU、禁网 Linux 原生 `date` / `/proc/uptime` 三十秒采样又捕获约 **13.027 秒倒跳 / 14.108 秒前跳**；同期 Windows 三百次采样未发现跳变。问题不只发生在 .NET；当前 WSL2 Linux 时钟源为 `tsc`，尚未证明具体内核缺陷或更改它。
- 当前仍未 PR/合并/再次 dispatch，P10 保持 **Candidate / No-Go**。下方“尚未重启”为重启前历史记录，已由本节取代；原始失败和本次 TRX 摘要均保留在[安全构建记录](../superpowers/plans/2026-09-08-p10-cosign-security-build.md)。完整 WSL2 重置会同时终止当前运行的 Ubuntu 和 Docker，必须另获 owner 授权，不能沿用只批准 Docker 重启的范围。

## P10 cosign 安全构建与本地时钟阻塞（2026-09-08 UTC）

- Owner 已明确批准可复现安全重编译并启动 Docker。固定上游源码、Go 1.26.8 和九项受审依赖更新，得到明确标识的 `3.1.3-cp6.1` 衍生版；不是 Sigstore 官方未修改二进制。三条工作流共用同一隔离构建和输出摘要，原信任、公钥、签名/扫描门禁不变。
- 两个独立容器的 Linux/Windows 二进制及依赖元数据逐字节一致；完整本地运行镜像扫描保留 UNKNOWN 3 / LOW 7 / MEDIUM 5，**HIGH 0 / CRITICAL 0**。实际 UID 1654、只读、禁网镜像中七项密码学检查通过，本地报告仍被正确拒绝为非 GHCR 托管身份。[安全构建记录](../superpowers/plans/2026-09-08-p10-cosign-security-build.md)保留全部摘要、原始失败和重现边界。
- Windows 最终真实输入 suite **1644/1644，零失败/跳过**；上游 **429 个测试/子测试通过，零测试失败/跳过**，另四个包没有测试；10 项真实离线输入保护测试、format 和三条 workflow actionlint 通过。
- Linux 最终 full suite **1589 通过 / 55 失败 / 0 跳过**；55 项均因共享的正式包证据收集在 `s06-packages-time` 时间顺序检查失败。独立只读探针实际捕获 Docker/.NET UTC **倒跳约 16.426 秒，随后前跳约 14.091 秒**；单 CPU、禁网探针又捕获前跳约 16.275 秒。未放宽或绕过时间检查，不能把该 full suite 写成通过。
- 衍生构建保存在隔离任务分支；**尚未 PR/合并，也未再次 dispatch**。需先在稳定时钟环境完成 Linux 重验。Docker Desktop 重启会中断当前七个业务容器，必须取得 owner 的明确同意后才操作；未自行重启、修改系统时间或关闭其他 WSL 会话。
- P10 仍 **Candidate / No-Go**；本地诊断镜像未推送、没有受保护签名/候选发布/生产部署，没有导出 Environment Secret、改变审批或改写旧失败。

## P10 原生扫描参数修复与 cosign 依赖阻塞（2026-09-08 UTC）

- 安全诊断 [PR #89](https://github.com/GTX537/CP6/pull/89) 已正常合入 `main@a41711dd55a093ab0ed127d599e0e7bcf11d548d`，七项 PR 检查、五个 exact-main 工作流及合并后 285 项回归全部通过，取代下方该补丁“待合并”的历史状态。
- [run 34234554610](https://github.com/GTX537/CP6/actions/runs/34234554610) attempt 1 定位到 `crm.pull-request` 403；owner 保存 CRM 专用 token 的 Pull requests Read 后，获批 attempt 2 的真实输入收集、七包/CRM 验证、**1644/1644 零跳过**和 OCI push 成功，随后 Trivy 非法 `--image-src registry` 失败，sign/finalize/artifact 跳过。
- 任务分支已把 Trivy 改为 `remote`，保留 Syft `registry:`；回归先 RED 1 失败/7 通过，再 GREEN 8/8。使用校验摘要后的精确官方工具对已有 GHCR digest 只读扫描，真实 SPDX 被现有解析器接受，真实 SARIF 因 **1 CRITICAL / 14 HIGH** 被原门禁正确拒绝。全部阻塞发现位于镜像内 cosign 3.1.3 的 Go 依赖，不是新的报告格式错误。
- 本地完整真实输入回归 **1645/1645，零失败/零跳过**，format 与三条 P10 workflow 的 actionlint 通过。这证明参数补丁的本地兼容性，不代表含漏洞镜像已被接受。
- 当日官方最新版仍为 3.1.3，尚无更新的官方发布二进制。自建补丁版或更换工具分发来源须 owner 决定；当前参数修复未合入 main，也未再次 dispatch 已知会失败的验证。[预检记录](../superpowers/plans/2026-09-08-p10-native-scan-preflight.md)保留 digest、报告 hash、实际计数及可复现命令。
- P10 仍 **Candidate / No-Go**。没有降低 HIGH/CRITICAL 门禁，没有重新签名/发布候选/审计通过/生产部署，没有导出 Environment 私钥或改写历史失败与正式包。

## P10 GitHub 读取安全诊断补丁（2026-09-08 UTC）

- 重跑时间修复 [PR #88](https://github.com/GTX537/CP6/pull/88) 已正常合入 `main@96c5f71e3c493c61a09d5b667dc32b966508631e`；七项 PR 检查、五个 exact-main 工作流和合并后 197 项回归全部通过。
- 新 [validation run 34229610628](https://github.com/GTX537/CP6/actions/runs/34229610628) attempt 1 获 owner 批准后，在收集输入时以 `github-http-status` 失败。SDK、工具、restore/build 成功；全量测试、OCI 构建/签名与交接上传均跳过。该旧日志没有状态码/目标，不能确认 CRM 权限故障或七包验证成功。
- 经 owner 授权，诊断补丁仅新增固定阶段、固定 GitHub 目标类别和数字状态码 stderr；原错误码、stdout 合同、失败退出及验收策略保留。不输出凭据、路径、URL、响应正文或 headers。相关回归 **175/175**，全量真实输入 **1644/1644，零失败/零跳过**，format 通过。
- 本补丁 PR/exact-main 合并、新源码验证及 owner 审批待继续。P10 仍 **Candidate / No-Go**，未 publication/audit/Frozen；没有生产部署、Secret/权限变更或审批降级。

## P10 S06 真实运行失败与重跑校验修复（2026-09-08 UTC）

- S06 实现 [PR #87](https://github.com/GTX537/CP6/pull/87) 已正常合入 `main@af0154782c8aa73eae47c083a884e2a404ef7b64`，该 main 的五个 GitHub 工作流全部成功。P10 实际验证 [run 34221083970](https://github.com/GTX537/CP6/actions/runs/34221083970) 的两个 attempt 均获 owner 批准，但均失败，未产生验证交接或候选。
- attempt 1 在包读取时报 `feed-http-status`；逐包 API 显示六个 private 包、一个 public Release 包，Abstractions 设置截图确认缺少 CP6 Actions 授权。Owner 已报告六包补齐 CP6 Read，但 attempt 2 在包读取前因 `s06-current-time` 失败，故权限修复尚未通过 hosted 下载验证。
- attempt 2 的创建时间 `12:06:31Z` 晚于执行开始 `12:06:29Z`。本次修复区分重跑记录创建与执行开始，额外读取并绑定同一 run 的 attempt 1，保留全部 selected-attempt/job/source/conclusion/cutoff 校验；不会因原始 attempt 失败而将重跑判失败，也不会把失败运行当成功。固定 CRM PR attempt 1 不变。
- 本地相关回归 **240/240**、完整 verifier suite **1615/1615，零失败/零跳过**，format 通过，含真实失败 run 的只读 API 检查和伪造/时间越界拒绝。修复 PR/exact-main 交付及新源码的真实 validation 待继续；[修复记录](../superpowers/plans/2026-09-08-p10-rerun-chronology.md)保留 RED/GREEN 与边界。
- P10 仍为 **Candidate / No-Go**，未完成 publication/audit/Frozen；所有候选仍 `deployable=false`，没有生产部署、Secret 变更或降低审批。

## P10 S06 实现已通过本地验证，真实候选仍为 Candidate / No-Go（2026-09-08 UTC）

- 正式输入已前向固定为完整七包 `0.10.1`：Platform source `3ff27e26962dcfd722887afb80a4306010dd9ee1`、formal run `34126521193` attempt 1、publication record SHA-256 `8b5fc47fd77902a3433d61b4cf181b67ef61ee994b60ea8286a690ab5084c961`。CRM PR #46 的消费 merge `a31ca0e323418f7e4108cc6220c0f5fa132e7fc2`、main run `34134695003` 和 PR #47 前向留存索引 `1332bf21e4253112d457f86cdc0cba829fc58f20c3914fa738dd992d917e2914` 为 S06 的精确输入。历史 `0.10.0` 及失败记录保留，不能作为当前正式集合替代。
- S06 适配器从正式 Release 包消费合同，已实现真实 NuGet/RFC3161、GitHub 运行/artifact、OCI/SBOM/扫描、用途分离签名、内容寻址 R2 和 Locator 验证。正式作者仍是固定 `PinnedSelfSigned`，`publicCaTrusted=false`、`internallyTrusted=true`；没有扩大 TSA 信任或公有 CA 声明。
- 三条手动受保护 workflow 已实现：validation 一次构建唯一 `ghcr.io/gtx537/cp6-p10-verifier` 并在实际 digest 内完成收集；candidate 使用已完成同源 validation，先独立只读 pre-commit，再条件创建 Locator 和只读 postcheck；audit 在发布完成后运行普通只读验证，不重建镜像或写 R2。审批、两把私钥、bucket-only publisher/consumer 均保持既有边界。
- 合并最新 `origin/main@c93f8608` 并修复审查发现的 Windows-only TSA 路径假设后，本地完整 verifier Release 测试 **1550/1550，0 failed / 0 skipped**；format 与三条 YAML 的 actionlint 通过。两条 S04 已核验系统路径均允许，实际签名、系统信任和在线吊销检查不变；[回归记录](../superpowers/plans/2026-09-08-p10-s06-cross-platform-timestamp.md)保留 RED/GREEN。这些测试不冒充 hosted Linux、GHCR/R2 写入或真实候选验收；受保护实际运行、最终内容寻址审计和远端交付确认仍待完成。
- 操作与边界见 [P10 参考](../devops/P10-PLATFORM-REFERENCE.md)和[运行手册](../devops/HOWTO-P10-PLATFORM-CANDIDATE.md)。本节取代下方 2026-09-03 历史条目中的“下一项双 runner 前置”作为当前接手顺序，但不改写当时的事实。P10 整体尚未 Frozen，所有 Platform 候选保持 `deployable=false`；未执行生产部署或改动现有 WMS R2/Azure 路径。

## CRM 首个切片身份桥：开发交付已验证（2026-09-08 UTC）

- CP6 已交付默认关闭的 RS256 OIDC 授权码/S256、显式真实租户开通映射、实时 CRM 权限/部门/负责人投影及绑定原浏览器 refresh 链的全局退出。精确协议、配置及合并前本地验证快照见 [身份桥记录](../crm/CRM-OIDC-FIRST-SLICE.md)，当前远端交付状态以本节为准。
- [PR #85](https://github.com/GTX537/CP6/pull/85) 的最终 head `af0912160c76085390d4d8863ba07efb5789d9a5` 经全部七项 PR 检查后正常合并为 `main@9453c9b60e268d6b6a1e9f07dfdb71c7b438f73a`。该提交的五个 main 工作流全部成功：client-contract [34192061268](https://github.com/GTX537/CP6/actions/runs/34192061268)、SQL [34192061341](https://github.com/GTX537/CP6/actions/runs/34192061341)、公开 SaaS `34192061306`、PRD `34192061321`、Space 证据完整性 `34192061267`；远端已确认包含受审实现。
- PR/main 的专用身份桥 SQL 均为 22/22，无跳过；artifact 为 `10042303759` / `sha256:d86a3694b5cb91b92a3a6c352dfe072231645063251e54255011591e5840ac55` 与 `10042649603` / `sha256:347b09bcb78591a8bad8e44ff2b7610567ff51181276dbacd70ba0ab486683db`。本地安全复核 97 项相关测试通过；完整 CP6.Tests 为 2981 通过、19 项既有其他模块跳过，未把这些跳过计作本切片 SQL 验收。前端安全返回、type-check、build、两项前向迁移和模型快照一并交付。
- 全新隔离环境的 5/5 真实跨仓浏览器流程通过，覆盖 Vue 密码/OIDC、Next/BFF/SQL 业务、回执归属、范围/PII、全局退出及实际撤权后旧页面清理。配套 [CRM #50](https://github.com/GTX537/CP6.CRM/pull/50) 与合并提交 `3f046e46b725a5703d94ab34f0976eac646fa3d4` 的 PR/main runs `34190712838` / `34191300094` 已通过；其交付记录 [CRM #51](https://github.com/GTX537/CP6.CRM/pull/51) 合并后的 `3ef601cf31e7e3c8a93d9c4e8d945b507da60fe5` / run `34192525333` 也已验证。本地四个服务和六个随机测试库已清理，私有报告保留。
- 上述 SQL 与浏览器使用合成账号和业务数据。当前闭合的是已授权首片的开发交付，不扩展为真实客户开通、C01/C02 全架构、商业 Provisioning、Pilot 或生产验收；没有执行生产部署。

## CRM 首片公开文档预登记与同步已完成（2026-09-08）

- 本次明确授权实施首片并同步 PRD/spec，已审查文档对应源提交 `b92e0a23f55e83ba92b6755b5381ef7d0b1e1e27`。前置 [PR #84](https://github.com/GTX537/CP6/pull/84) 仅登记确定内容，合并为 `main@80cd15deb05922c63fb8849b6142ba7e98331cf3`，其全部 PR/main 检查通过后由 #85 同步受保护基线并提交已登记的完整 13 文件集合。
- 校验器只接受完整历史 12 文件集合，或精确匹配登记摘要的首片 13 文件集合（PRD、Executable Spec、入口三份增补及一份 OIDC 说明）。混合版本、缺失、未知路径和任意内容变更均拒绝；登记规则从受信校验器自身目录加载，不接受待审仓库提供的批准摘要。
- 历史产品 payload、批准记录、Git/GitHub 审批证据及 M0 No-Go 保持原约束；商业敏感信息与秘密扫描继续执行。预登记不等于重新批准历史产品内容、发布新文档或授权生产。
- 准备与实现均按既有 protected-base 流程交付；main push 路径过滤已补入新模块及测试，required context、受保护验证步骤、工作流权限、部署和其他业务保持原样。集合/触发回归 74/74、审批及敏感内容回归 59/59 通过；预登记与实现文档的原始摘要不随本次状态记录改写。

## 文档分类与统一导航（2026-09-07）

- 新增 [文档中心](../README.md) 与 [维护规则](../CONTRIBUTING.md)，按用途连接产品规划、架构、模块规范、操作手册、学习资料、任务记录和历史归档。
- 31 个根目录文件迁入相应分类，原始正文与附件保留，入站链接及相对图片 / 文件链接同步修复；15 份非 Markdown 文件与原始 Git blob 一致。
- 项目记忆增加阅读顺序，旧交接快照、Word 导出、原始资料与正式验收证据的边界已标明。原始资料、CRM 审批、R2 规范、Space 验收、发布流水线及部署行为均未调整。
- 验证范围和可复现命令见 [整理记录](../_inventory/2026-09-07-docs-organization.md)，旧路径可在迁移清单查找。当前业务状态与门禁结论沿用下方既有记录。

## P10 R2/cosign 外部前置切片已完成（2026-09-03 UTC）

- `GTX537/CP6` PR #80（head `17dc0407f58750d729d7207dfa0f59f79182a4c5`）经六项 PR 门禁成功后正常合并为 `main@da54076861b30e710a3eceb9e08023fbc6f9ff87`；exact-main runs 33706881271 / 33706881477 的真实 SQL、Android、Windows 与 Web 作业全部成功。公开 trust blob 与本地审计对象一致。
- 受保护 GitHub Environment `p10-platform-candidate` 只有一条 `main` deployment branch policy，required reviewer 为仓库 owner `GTX537`，`prevent_self_review=false`；八个 Environment secret 名称已确认：`P10_LOCATOR_COSIGN_PRIVATE_KEY`、`P10_LOCATOR_COSIGN_PASSWORD`、`P10_OCI_COSIGN_PRIVATE_KEY`、`P10_OCI_COSIGN_PASSWORD`、`P10_R2_PUBLISH_ACCESS_KEY_ID`、`P10_R2_PUBLISH_SECRET_ACCESS_KEY`、`P10_R2_CONSUMER_ACCESS_KEY_ID`、`P10_R2_CONSUMER_SECRET_ACCESS_KEY`。仓库未记录任何对应私值。
- 权威 trust instance 为 `eng/p10/trust/pinned-trust-store.v1.json`，SHA-256 `0a6e72951c196e612a593cc8831e294bb538c9ba8a79eada4538771a3811d8e9`；`candidate-locator` key ID 为 `sha256:9c0fd05b3159651cc2e9138555f32387988c6961889ee00211139e710f1febaa`，`oci` key ID 为 `sha256:eb623d784fc55294e942fa49062477769a34943d5997fdbdd483ad0fb0103c21`。两把 ECDSA P-256 公钥用途分离，私钥只存在于 Environment secrets。
- R2 authority `cp6-release-r2-v1` 固定到 `cloudflare-r2` / account `30c4a8d1697ffd3de6a1e0a88376607c` / bucket `cp6-release` / jurisdiction `default` / prefixes `candidates/platform/`、`objects/sha256/`。publisher parent token 仅有该 bucket 的 Object Read & Write，consumer token 仅有 Object Read Only；900 秒 actions-only session 已以 `PutObject`、`HeadObject`、`GetObject`、`GetBucketLocation` 和两个前缀生成并通过非写入 `GetBucketLocation`，未上传诊断对象。
- 该切片只关闭 P10 的 R2/cosign 外部输入准备，不等于 P10 S04 或 S06 完成。独立双 runner RFC3161 门禁仍未完成，故 `S04_EXTERNAL_PREREQUISITES_READY` 必须保持 `false`；没有发布 candidate、Locator、NuGet 包、GHCR image，没有公开 R2 bucket、覆盖/删除云对象或执行任何环境/生产部署。

## CRM Platform P09 冻结可消费（S06 完成，2026-09-01 UTC）

- 当前权威阶段为 `S01-S06 complete`，全局状态为 `Frozen / Consumable`。此前公共 PR #77 记录的 `Published / Consumer Candidate` 是不可变历史；该决定只在 Platform 最终审计和 CRM 最终传播各自合并且精确 `main` 门禁全部成功后生效。
- `GTX537/CP6.Platform` PR #29（head `914e70cae4d78114ae181515cfaece11a62a28be`、PR run 33479175077）合并为精确发布源 `main@1c40f21e38929abaaa6006f69ee70d4492890661`，exact-main run 33479705779 五项作业成功；publish run 33480300468 / job 99768201448 发布唯一不可变 `CP6.Platform.Deployment 0.9.0-alpha.1`。publication artifact 9789925866 的 digest 为 `sha256:3daad67d4a15144d5f22b64637f7e9f91bdedc4e95fec4e5e20dd09977d78f27`，普通包 SHA-256 为 `e820d1771ed004b4a7089d008eef3bb2aca4fe35e4912d67057840373c4952cb`，Registry version ID 为 1194316756。
- Platform evidence PR #30（head `eebce761644fe77a4d9fc7b138684db7bf6e7f02`、PR run 33481868328）合并为 `main@5937761d2cb134b963493aca9ebaac792ac033e2`，exact-main run 33482389065 五项作业成功，固定 Profile、Compose、Kubernetes 与 rehearsal evidence SHA-256 以及零残留边界。
- `GTX537/CP6.CRM` 实现 PR #37（head `3b4a291aba02b27bb1d41cd7f2330c11c9fcce62`、PR run 33485657934 attempt 3、SQL artifact 9792143475 / `sha256:5e770a8eb06306830a8ed7d03e612b49a51c4eb21a37f2c79cb628f3622941fd`）合并为 `main@13abc0785d98264436096e330260cd6d8e95687b`；exact-main run 33487546660 与 SQL artifact 9792401387 / `sha256:dac8f064370511d4c36ba71c945c70ae29f7a370e33e9a37b39f332d81053fd7` 成功。CRM 只在测试项目固定 Registry 版本，9/9 P09 黑盒和 72/72 .NET 回归通过，生产项目与 `Program.cs` 未引用或注册 Deployment 包。
- CRM evidence PR #38（head `c283e077fa6716ab4cd22fafb7b8237e72013e88`、PR run 33489443438、SQL artifact 9793158824 / `sha256:bbe3bee7a5c23408e49a947903b1c521726c8846c6175e89986cb02a31b939de`）合并为 `main@85004c838e4179ddd67faca0532cff303e865738`；exact-main run 33490110069 与 SQL artifact 9793416744 / `sha256:d1a7c7f60a43d9dea5239407208686f4dc676700c792cb8eaf386c9e313a414e` 成功。
- CRM evidence-closure PR #39（head `076307b442a9e40372067963c579305d45e729fb`、PR run 33491408393、SQL artifact 9793938423 / `sha256:641355a58f253bec6af58d0b62e76924012c3250ead5435270a1814b859ff78f`）合并为 `main@09d90d24b1a70a24b7dbcdea5c19ab46db378544`；exact-main run 33492405597 的完整 CRM 与真实 SQL 作业成功，SQL artifact 9794324368 / `sha256:25463a33c8e85b35a333ed46422607516b74efcfd58115d511091ea1a38cdc71` 成功绑定 S05 前向证据。
- 公共候选 PR #77（head `8578bc1df9c64b00e0f27ae602d2960a91b8450a`）合并为 `main@ed08018a160d467342ddee823409232e6c412267`；六个 PR 工作流和五个 exact-main 工作流全部成功，只记录 Candidate 阶段而未提前冻结。
- Platform final-audit PR #31（head `bf2e929d1e45bedeb8fcd4c108348a5bb3cc561d`、PR run 33496879686）合并为 `main@8f1c7283946ac98bbda078f40d95972ed01b146d`；exact-main run 33497448441 的 `ubuntu-p09-non-production-runtime` 99822794965、`ubuntu-latest` 99822795157、`ubuntu-dapr-kafka` 99822795183、`ubuntu-sql-server` 99822795286、`windows-latest` 99822795288 均为 success，首次使 producer 达到 `Frozen / Consumable`。
- CRM final-propagation PR #40（head `8643a5c8b0b1ab54e480c1490f2639afe6fbcca4`、PR run 33499604406、SQL artifact 9797173880 / `sha256:1984340af54f2a485fea22b59710645fed9c324ca1fedbad26eddbd6b3102750`）合并为 `main@116018974dc406ab060eec7ccaf34f9303a7e523`；exact-main run 33500324046 的 CRM validation 99831901263 与真实 SQL 99831901244 成功，SQL artifact 9797446436 / `sha256:be1c83b021d9bb874635e22ce57a4d2201d0a4152109c2efb4e508b4ee27fdc6` 完成最终消费者传播。
- 本阶段没有连接或修改真实 Kubernetes、DEV/UAT/PROD、CRM Gateway/Worker、业务 Topic/Subscription、Secret、数据库、迁移、云资源或生产部署，也不授权 P10、C01/C02/CRM03 或 CRM 业务切片。

## Space Editor 自由视口实现完成（2026-08-31）

- 二维空间编辑器已支持指针锚定滚轮缩放、Space + 左键/中键全工具平移，以及拖拽模式空白平移与货架移动分流；工具栏提供缩小、百分比、放大、适配全部和复位视图。
- 采用两阶段 Konva 图层预览与单次提交重绘；初始适配定义为 100%，范围 10%～800%，ResizeObserver 保持世界中心，提交后刷新 Transformer 与命中状态。
- 视口只存在于前端页面生命周期，不进入场景 DTO、命令栈、保存请求或数据库；本任务没有 API、迁移和 CP6DB 数据变更，也没有把开发 Docker 验收描述为生产发布。

## Space Editor 版本化几何兼容修复已完成候选验证（2026-08-30）

- Docker 验收环境的楼层场景接口正常返回约 305 KB 数据，但 `Space_Zone.Polygon`、`Space_Aisle.Polygon` 和 `Centerline` 已是 `{ schemaVersion: 1, points: [...] }`，旧编辑器仍按裸数组遍历，触发 `TypeError: t is not iterable` 并中断画布渲染；该问题与 Design API 站点开关、数据库连接或接口可用性无关。
- 新增统一几何解析器，兼容旧版裸坐标数组和 Schema 1 对象，并让场景渲染、越界碰撞、巷道吸附、模板放置预览及属性面板统计共用同一失败关闭规则；无效、非有限或不支持版本的数据返回空几何，不再拖垮整个编辑器。
- Web 全量 181 个文件 / 956 项测试、严格类型检查和生产构建通过。提交 `fc6b545d` 的隔离 Docker 候选镜像在 `127.0.0.1:18082` 读取真实 CP6DB：场景/模板/权限/连接体请求全部 200，画布、10 条巷道统计和五种工具切换正常，未点击保存或产生写请求，控制台无 Vue/TypeError。
- 既有 Konva 6/7 Layer 性能警告仍存在，但不再造成加载失败；后续如处理必须以独立性能任务和基准证据推进，不能与本次数据合同兼容修复混在一起。

## Space Editor 工具反馈增强已验收（2026-08-30）

- 空间编辑器五种工具现提供持续上下文提示、明确光标和 `aria-pressed` 状态；旋转模式使用高对比度圆形手柄，反向建模在未单选货架时保持可点击并解释前置条件，导出完成后提供页面内成功反馈。
- 场景加载期间选择的工具会在 `InteractionManager` 初始化后重放；组件卸载早于异步加载完成时不再写共享 Store、创建 Stage/InteractionManager 或遗留文档事件。旋转手柄只在恰好单选一个有效货架时出现，多选不会预览或提交部分旋转。
- 新增工具文案使用稳定语义键并提供 `ja`、`zh-CN`、`zh-TW`、`en`、`ko` 本地兜底，后端语言包仍可覆盖。最终 Web 门禁为 179 个文件 / 943 项测试、严格类型检查和生产构建通过；本地 CP6DB 环境的隔离 Chrome 验收确认五工具、撤销/重做、导入取消、导出、反向建模与单选/多选旋转。登录后的编辑器验收动作阶段 `consoleErrors`、`pageErrors`、POST/PUT/PATCH/DELETE 均为 0，且未点击保存。用户已于 2026-08-30 确认验收。
- 本任务 Git diff 只涉及前端、测试及文档，不含 Docker、数据库、API、DTO、迁移或部署文件；执行过程未运行 Docker、数据库管理或迁移命令。反向建模对话框既有的字面量 `{diff}` 未插值问题不属于本次回归，已保留为独立 P1 跟踪项。

## CRM Platform P08 冻结可消费（S06 完成，2026-08-30 UTC）

- 当前权威阶段为 `S00-S06 complete`，全局状态为 `Frozen / Consumable`。该决定只在 Platform S06 PR #26 合并且精确 main 的 Windows/Linux/真实 Dapr-Kafka/真实 SQL 四项门禁全部成功后生效；此前 S05 候选记录继续作为不可变历史，不被改写。
- `GTX537/CP6.Platform` remediation PR #23（head `14b9bc2d41c8446f2c236094b9f2d82f1124d045`、PR run 33320438234）合并为精确发布源 `main@bfb0ebdc2e17f9a580156dbba6c0ce6cf6f3c672`，main run 33320608737 通过；publish run 33320840180 发布五个不可变 `0.8.0-alpha.2` 包，artifact 9734883916、API digest `sha256:db2e44481101dcf450cd1a0d6188572ac8c1529fc148e6ea3d094d8c772a4e61`。逐包 SHA-256：Contracts `b6a0106bdc8c60f0c49ed29263f8e538beb3ac9ad2d181227d40e7e2a8e213df`、Abstractions `842b007d3b8e7c369f7de3e22d03d2eb46746e14ad809ce5b9ce4e01ee1114ca`、AspNetCore `3564b21bd621307e002d1307c8081062e9c3743533fd4f0dac4ef7bed9ada92c`、EntityFramework `d1e7cf733693e13b34a8cb39a64077c498cc4fbb082953a5e7a0b0cb27d1acee`、Messaging `64d5878567213cfde658117e069f3e598ecbd53dbb8e0ef4cfd19ceca314b7a8`。
- Platform PR #24（head `2c1100d57a4ddd55e8bb1b5c673ae631944548d8`、PR run 33321808533）把发布证据合并为 `main@06abda2832902641f7652f1d175d8cf69c1b6b96`，main run 33322000747 通过。不可变 `0.8.0-alpha.1` 仅保留为历史发布证据，因真实下游仍收到 baggage 而被取消消费资格，未覆盖、删除或冒充 alpha.2。
- `GTX537/CP6.CRM` PR #33（head `e6358ad776facc4e13b313b960b650e7b1845d83`、PR run 33329003327、SQL artifact 9737092867 / `sha256:acfa2382c9878f2f4608de14cd09ab6f9620ab4171040a60c920446df835e73b`）固定消费五个 alpha.2 生产包，以 CRM 自有 24/24 P08 黑盒测试和 63/63 .NET 回归闭环；合并 `78577658159d4270c87c0d275259aeee47224711` 后 main run 33329320097 与 SQL artifact 9737180305 / `sha256:91f68ddb0cd67f102ca8c9795631fd3d009e7f85767915aff2a4abc4a51660fd` 通过。
- CRM PR #34（head `f0b22c7415ed51b19375664d7cac83024af92986`、PR/main runs 33330377723/33330705446、merge `45d0418884b3e21e62d87315ea7ef5f595f1819a`）保留 S04 证据；其 SQL artifacts 为 9737472407 / `sha256:62e27a980c8ed2e7f1abab95332e4ca689eedc88c80381a8879eb5bbff199cc8` 和 9737563354 / `sha256:02485cb11308b9d142e73c58de056a6c43c02cc48e0d0a677bbc9fcdb72df340`。PR #35（head `8bd521860396b81d235ae6887b58ebd5718b85ad`、PR/main runs 33332328534/33332741550、merge `bc565fce5bf84904eb1bbe11e7ab13cf6a1e016a`）前向纠正过早冻结；SQL artifacts 为 9738014861 / `sha256:f28d757928959d1c4e4b06f41be773b89b618afcd650cd10cd87dbef7218a4e5` 和 9738124159 / `sha256:9b360a9f2e05b6c376acea5e2308c05f787315c4d059f005cf114c3ccc8b98bc`。
- Platform consumer-evidence PR #25（head `e9657cb55bed6230a7ee0aec33603379c4535775`、PR run 33333573931）经复审无 Critical/Important 后合并为 `main@3bee7abe00e6f79be41bbc6af6dc2290261cb317`；exact-main run 33333782732 的四项矩阵全部成功，完成 producer/consumer/source-of-truth 对账。
- 公共 S05 PR #72（head `7f6b7ace98453c3662113d3eba1be5d324ba863e`）合并为 `main@7ebaa3e573f45cc928994ec9967ff5b5a678819e`；六个 PR 工作流与五个 exact-main 工作流全部成功，只记录候选阶段而未提前冻结。
- Platform final-audit PR #26（head `a19a12672320bab2a84d73b7fc5645f0e90ee904`、PR run 33336245379）合并为 `main@c0dd1f7adfbab398c0231ec90152db781885d52b`；exact-main run 33336448249 的 `ubuntu-latest` 99324082571、`windows-latest` 99324082575、`ubuntu-dapr-kafka` 99324082430、`ubuntu-sql-server` 99324082539 均为 success，首次使 P08 达到 `Frozen / Consumable`。
- CRM final-propagation PR #36（head `bda9efde86e596e9b0b9a65c235b900c5a6900dc`、PR run 33337283054、SQL artifact 9739426862 / `sha256:201c1d3e7e72ad4692cfb2869d02e69bccd55f0522a772f5bc59c286cc73b726`）合并为 `main@8c8fa09d7b1ccf85b74d2cb3bdc4298b7455c6d0`；exact-main run 33337576328 与 SQL artifact 9739516480 / `sha256:ff2234b644cefcd482200d32cc58f5caee21778ef0c52cf3347d64d64e94fc6d` 通过完整 CRM 和真实 SQL 门禁。
- 本阶段只同步 exporter-neutral telemetry/health/resilience/SLO evidence 合同及固定版本消费证据；没有注册真实 exporter、Collector、dashboard、alert、CRM Worker/subscription、生产 SLO、Secret、云资源或部署。

## CRM Platform P07 已冻结可消费（2026-08-29 UTC）

- `GTX537/CP6.Platform` 通过实现 PR #15 与发布 PR #16，在 `main@329bf8ee82091de569cb80f1e83fc5d518f74068` 完成 code-owned YARP allowlist、外部身份头清理、按连接来源分区的固定窗口限流、后端独立认证边界及 loopback E2E；实现 main run 33261055327 attempt 2、发布 main run 33262410890 与 exact-main publish run 33262569274 成功。
- publish run 发布五个不可变 `0.7.0-alpha.1` 包和 artifact 9717721544；artifact SHA-256 为 `bf6487e55d8345b1dfbe39cedc2afcb3e365c9bcdd36bbec17841996cd4e88a0`。逐包 SHA-256：Contracts `609c4f562858be6cfe45fc2d51c9eaeceb50a392ce28135a4a9ea644416054d9`、Abstractions `7149940ddb817145fe615a51c8e517fe1915516bf9188b6e2f084e21d7738479`、AspNetCore `85f0742253ebed8adaad2fcf63f2166545e5c4cf35adf739eb87c1eed63fd010`、EntityFramework `a16de9d73df5dda91c4be9e667c4352fad354c5425fb207ae922148d864b7a0e`、Messaging `b385cebe84ebd6dafac723724b273a8660999042bc95f3e6dd88af433cbbd7fd`。
- `GTX537/CP6.CRM` PR #31 固定消费 `0.7.0-alpha.1`，以 11/11 真实 loopback 测试证明未匹配 route 不触达后端、伪造身份头被清除、429 在第二次后端调用前返回，且直连/代理均不能绕过后端认证；PR run 33264347561 与合并后 `main@02f7078de6a67e7f3fded6df6a84b9f6fb712a84` run 33264676796 通过远端包恢复、真实 SQL 与完整门禁。
- CRM PR #32 将 locator 冻结为 `Frozen / Consumable`；PR run 33265394681 与最终 `main@467d95e46625d4db0bb7aa0932aff5464f64a01b` run 33265702772 的 P01–P07 locator、真实 SQL、40/40 M0、39/39 .NET、39/39 Web、production build 与 3/3 Chromium smoke 均通过。
- 该结论只关闭 P07 通用 Gateway 合同与固定版本消费。C01/C02/CRM03、实际 CRM public route、P09 后端网络隔离、运行时 Gateway/auth 注册、Secret、云资源和部署仍未实现或授权。

## CRM Platform P06 已冻结可消费（2026-08-29 UTC）

- `GTX537/CP6.Platform` 通过实现 PR #12 与发布 PR #13，在 `main@3b1669a05f9b265f9b3fb14ade4d656018cbf6b5` 完成 EF Outbox/Inbox、lease、retention、DLQ/replay、真实 SQL Server 与保留的真实 Dapr/Kafka 回归门禁；实现 main run 33241821365、发布 main run 33242125202 和 exact-main publish run 33242264497 成功。
- publish run 发布五个不可变 `0.6.0-alpha.1` 包和 artifact 9711742920；artifact SHA-256 为 `44431d7f359ea524ba9dc438f6f70d24bf34be69411729a2fe1953e3039b3b86`。逐包 SHA-256：Contracts `acb42d617635ed6ba484edf1281a6c3a049c209d0c861015e5a9e269141722a4`、Abstractions `004ff6d528e7d15a2887df51f42035105804988d49e387d87aea6f0555e4b759`、AspNetCore `1104e5319195a2ff8a59a4cf3893766fa959e8733a10486cf260998c61c020fb`、EntityFramework `63491b51b6c0302b0ec662341764181665952c36788f1ef4dcd1424ef75777e7`、Messaging `5bcbb2bec969ac463876b84c87c36d6a93b316642943ab5f3ded2103d8c6c410`。
- `GTX537/CP6.CRM` PR #29 固定消费 `0.6.0-alpha.1`，复用 P04/P05 失败关闭并在 pinned SQL Server 中证明 invalid、duplicate、payload conflict、out-of-order、failed handler 与 poison DLQ 行为；PR run 33243227124 attempt 3 和合并后 `main@910804f5e7fa02569da958ae325997e10c0ffbc0` run 33244344319 通过远端恢复、真实 SQL 和完整门禁。
- CRM PR #30 将 locator 冻结为 `Frozen / Consumable`；PR run 33244749522 与最终 `main@744ca5d9d06db4470d18a4d8ce3ecfbae42f1d2c` run 33245027773 的真实 SQL、40/40 M0、28/28 .NET、39/39 Web、production build 与 3/3 Chromium smoke 均通过。
- 该结论只关闭 P06 通用事务消息 substrate 与固定版本消费。CRM-F3-CONTRACT/C02 业务事件、`CP6.CRM.Worker`、运行时订阅、Secret、云资源、迁移和部署仍未实现或授权。

## CRM Platform P05 已冻结可消费（2026-08-28 UTC）

- `GTX537/CP6.Platform` 已通过实现 PR #9 与发布 PR #10，在 `main@7acb658e001e2bea4e567feeb4e0f7fb1e47eae6` 完成 Dapr service invocation、structured CloudEvent Pub/Sub、Kafka topic/partition-key 约定及真实 Dapr 1.18.2/Kafka 4.3.1 容器门禁；main run 33192565859 与 exact-main publish run 33192773875 成功。
- publish run 发布四个不可变 `0.5.0-alpha.1` 包和 artifact 9694537167；artifact SHA-256 为 `d2135e57ffcd47695165808c5ce841a6aca6afb8641cb49a499f9b1af23d96db`。逐包 SHA-256：Contracts `b574da91d427a2573c7afd55e686bee590ab401550c057a3436db94c79c81aae`、Abstractions `add6d9e136629791a99e93522557466d4328b4ecdee2ae711f4c57dbf20cf5c0`、AspNetCore `142c7a6eabaa888603471550cc5b74b208c365a631a0cd6f6668cd8e59b6fe6e`、Messaging `3a2e934abeb419861ee8958e52a1c878c9ed988c703839c7225039981ec44fc3`。
- `GTX537/CP6.CRM` PR #27 固定消费 `0.5.0-alpha.1`，组合 P04 envelope/region/legacy 与 P05 topic/key 校验并在副作用前失败关闭；PR run 33194075874 与合并后 `main@76cf6e6eef5dd835e5d2005d9d1e22b69654c759` run 33194583713 通过远端包恢复、28/28 .NET、39/39 Web、production build 与 3/3 Chromium smoke。
- CRM PR #28 将 locator 冻结为 `Frozen / Consumable`；最终 `main@75fa59ffd9e31c9bffb3ec4f8dd27b996cb49c0f` run 33195879078 通过。Platform PR #11 同步三仓闭环证据后，最终 `main@b0a5b472cf9554f01969f93d671658ad1aca8752` run 33196852469 的 Windows、Linux 与真实 Dapr/Kafka 三项门禁通过。
- 该结论只关闭 P05 通用传输合同与固定版本消费。CRM Worker、运行时订阅、P06 Outbox/Inbox、C02/CRM-F3-CONTRACT 业务事件、Secret、云资源和部署仍未实现或授权。

## CRM Platform P04 已冻结可消费（2026-08-28 UTC）

- `GTX537/CP6.Platform` 已通过实现 PR #6 与发布 PR #7 合并 `main@2c4c601228d81b300659b7773748da2e995ce433`；Windows/Linux main run 33167738330 与 exact-main publish run 33167927567 成功，发布四个不可变 `0.4.0-alpha.1` 包和 artifact 9684391334。
- 包 SHA-256 已固定：Contracts `b41b6f65507fc1c1db9db7c6213b793787af17e53c6f3d5c8debac7a7606b278`、Abstractions `d867e6ad43355113ab29a775e0801f11643c0f60ae4c65e58fa41d9646423139`、AspNetCore `8749825d6cfa0d899ef2ab21421818bf1bfdc36e0a31b4607b60de71bda7c5c5`、Messaging `50fd23395f49d14ec22619cdddce8006f2b5ec33c465787496f5c5582a74d762`；发布 artifact SHA-256 为 `07eae751d6288cf8f8d81561ae77ac4ab452d62610966a9c69cad995a05bea3e`。
- `GTX537/CP6.CRM` 已通过 PR #25 固定 Abstractions/AspNetCore/Messaging，复制发布包内 bundle，并在副作用前委托 Platform Schema validator 后拒绝旧别名、未知区域与区域不一致；PR run 33169553326 attempt 2 及合并后 main run 33170491020 通过远端包恢复、40/40 M0、23/23 .NET、39/39 Web 与 3/3 Chromium smoke。
- CRM PR #26 已把机器 locator 冻结为 `Frozen / Consumable`；`main@2a728411c6becd437bb0e1f7f4ead680a0947c52` run 33171913476 再次通过完整门禁。Platform PR #8 同步闭环文档后，`main@313f8090a0a806ab961e3590939b07fab878a77b` Windows/Linux run 33172549942 通过。
- 该结论只关闭通用 P04 CloudEvents 1.0 structured JSON、Draft 2020-12 bundle 与固定版本消费。P05 Dapr/Kafka、P06 Outbox/Inbox、C02/CRM-F3-CONTRACT 业务事件和运行时订阅仍未实现；没有创建 Secret、云资源或部署。

## CRM Platform P03 已冻结可消费（2026-08-28 UTC）

- `GTX537/CP6.Platform` 已通过 PR #4 合并 `main@1a39711423dbae7d869031b5d8570e649ccee8f4`；PR run 33154975361 与双平台 main run 33155123120 成功，publish run 33155315173 从该提交发布三个不可变 `0.3.0-alpha.1` 包。
- 包 SHA-256 已固定：Contracts `4eb84481db0fc6e30104295f485d7f59c2c1fbde1a45b15cb9f84c8340efa8ca`、Abstractions `04d40464f7e9cccaf6288ea97ed715677efa8e76426ad5b5356e60104711ea66`、AspNetCore `91e4e24b011dcd2aff4ba985b58e52ceaa6bb12ad3abc495f6bda43c4c2f4202`。
- `GTX537/CP6.CRM` 已通过 PR #23 合并 `main@16e9955b24a5c8c412839301b12d8a9275250746`，固定 Abstractions/AspNetCore 版本，复用 RS256/JWKS validator 与 RFC 9457 writer，并保持 `CRM_* / crm.error.*` 错误命名。
- CRM consumer PR run 33156706503 与 main run 33157212604 均通过远端私有包恢复、15/15 .NET、39/39 Web 契约和 3/3 浏览器冒烟；P03 状态为 `Frozen / Consumable`。
- 该结论只关闭 P03 合同。C01 issuer/Discovery/JWKS、P07 Gateway、C02、CRM03、真实登录和业务切片仍需各自实现与授权；CRM `Program.cs` 未启用认证，没有创建 Secret、云资源或部署。

## Space Studio V1 Core GA 100% 结案（2026-08-28）

- 受控发布演练绑定应用提交 `21a81767a0155a8cc92325acc3e3cdcc076ee930`、冻结 Source Set/Golden Dataset/Worker Environment 及 10 个实现源 Git Blob；WP2/WP4/WP7/WP5 正式基线均重新校验通过。
- SQL Server Express LocalDB `17.0.4025.3` 上实际执行 Publish/WMS/Published 隔离/恢复 8/8、0 failed、0 skipped；产品 `Cp6SpaceWmsAdapter` 使用 `Real` 数据源分类，自动恢复 5 秒，CHECKSUM 备份恢复 3.6480777 秒，`DBCC CHECKDB`、Published Hash、WMS 写入计数和临时资源清理均通过。
- Kestrel loopback + 验签 JWT 1/1 通过；Customer/Supplier/3PL 控制面全部 403，Published Portal 只读、写入拒绝，内部主体可用。没有把 Mock、未签名角色头或合成数据作为正式安全/生产验收。
- WP6、WP8 已 Complete/Accepted，唯一 DeliveryOwner `BUBAO.GAO` 已 Signed；两类外部输入、WP0～WP8 九个 Gate 和签署均无 Pending，Core GA 派生状态为 `GaReady` / 100%。
- 本次仍是本机受控非生产结案：`productionDataClaimed=false`、`productionWmsClaimed=false`、`productionDeploymentPerformed=false`。生产 Release/CD、生产 WMS 窗口、现场 Pilot 和 Backup Provider 是 GA 后独立事项。

## Space WP5 Viewer 正式接受（2026-08-28）

- 生产 Viewer 的 Current Published Design Revision 边界通过聚焦 3 文件 12/12 和浏览器请求级验证；Draft、错误 Authority、内容漂移、运行态混入和 Legacy/可变 Scene 路径继续失败关闭。
- 正式标准仓为 500 货架/10,000 库位，30/30 冷启动、3,000/3,000 拾取命中、0 失败、0控制台错误；硬件 WebGL2/RTX 3060 全程一致，交互/帧/标签/拾取/着色/Draw Call/标签量全部低于冻结门槛。
- 生产 Canvas、工具栏和楼层列表已补齐键盘与可达语义；1440×900、1280×720、键盘、Chromium Accessibility Tree 和关键控件 4.5:1 对比度 4/4 通过。UI fixture 明确为 Simulated，不计生产数据。
- WP5 已 Complete/Accepted。Core GA 保持 72% / `NoGo`：0 个外部输入、WP6/WP8 共 2 个 Gate 和 1 个最终签署 Pending；下一主线是一次受控 SQL Server/WMS/恢复/安全发布演练，未执行生产部署。

## Space WP2 CAD Start 正式接受（2026-08-28）

- 新增独立受控验收运行器，从仓库外复算授权 `L1-C01.dwg` / `L1-C02.dxf`，验证冻结 `cp6-autocad-worker/1.0.0` Release 和 AutoCAD Core `25.0.58.0.0` 后，在 SQL Server Express LocalDB `17.0.4025.3` 调用产品 Preparation/Parse 服务。
- 两格式均完成 Floor/Unit/Transform/Mapping 显式选择、Preview 零 Draft 写入、sealed Preparation、Parse Start 和幂等重放；篡改请求以 `SPACE_CAD_PREPARATION_INVALID` 拒绝且 Job 保持 2→2，Worker 原始 CAD/Attempt 残留为 0。
- 后端聚焦 21/21、Web Wizard/API 14/14、严格类型检查、正式 Manifest、16 个失败模式和总 GA 52/52 均通过；WP2 已 Complete/Accepted。
- Core GA 固定保持 72% / `NoGo`：0 个外部输入、WP5/WP6/WP8 共 3 个 Gate 和 1 个最终签署 Pending。未执行生产部署、生产 WMS 或远程 Worker 部署；下一主线是 WP5 Viewer 结果接受。

## CRM Platform P02 已冻结可消费（2026-08-28 UTC）

- `GTX537/CP6.Platform` 已通过 PR #3 合并 `main@6004decd2a4e41d9d502738dc5d9063bef9b37b7`；双平台 main run 33144068629 与 publish run 33144178346 成功，发布 `CP6.Platform.Contracts`、`CP6.Platform.Abstractions`、`CP6.Platform.AspNetCore` 三个 `0.2.0-alpha.1` 包并记录逐包 SHA-256。
- `GTX537/CP6.CRM` 已通过 PR #21 合并 `main@72c405b4e6ab0ab708cfa1b579b8821a1402dcfe`，固定消费 `CP6.Platform.Abstractions [0.2.0-alpha.1]`，精确映射 TenantId→OrganizationId 且不允许默认租户。
- CRM consumer PR run 33144894103（attempt 2）与 main run 33146816096 均使用短期、只读 `GITHUB_TOKEN` 完成私有包恢复、构建、12/12 .NET、39/39 Web 契约和 3/3 浏览器冒烟；P02 状态为 `Frozen / Consumable`。
- 该结论只关闭 P02；P03 的后续关闭由上方独立记录追踪。P04 CloudEvents、CRM-F3-CONTRACT、登录和业务切片仍需各自依赖与独立授权；未创建云资源、Secret 或部署。

## Space WP1 手工建模正式接受（2026-08-28）

- 正式 Manifest 绑定已合并的 `main@b0164a15cc7d0ad5716973323fcac27325bcfd5e` 和 8 个测试源 Git Blob/SHA-256；由唯一 DeliveryOwner `BUBAO.GAO` 完成可重复自验收，不设置第二人或独立 QA 门禁。
- SQL Server Express LocalDB `17.0.4025.3` 最终运行 20/20、0 failed、0 skipped；覆盖 Blank/显式 Floor、System/Tenant Template、模板应用、完整编码仓库、Preview 零写入、Apply、Lease/Revision/Idempotency 与失败批次原子性。首次缺少环境变量的 17 skipped 运行已作废且未计证据。
- Web 6 个聚焦文件 25/25；新增 WP1 正式协议、12 个独立失败模式和总 GA 组合验证，`manual-modeling-formal-evidence-v1.0.0.json` 已 Pass/Accepted。
- Core GA 仍为 72% / `NoGo`：0 个外部输入、WP2/WP5/WP6/WP8 共 4 个 Gate 和 1 个最终签署 Pending。下一主线是 WP2 CAD 起始向导结果接受。

## Space WP0 基线与治理正式接受（2026-08-28）

- 唯一 DeliveryOwner `BUBAO.GAO`、Kickoff `2026-08-27`、目标 GA `2026-09-27`、两类外部输入 Owner 与 WP0～WP8 Gate Owner 已固定；单人交付不设第二人、独立复核、角色配额或多人签字门槛。
- 正式 Manifest 绑定 `main@162d110829780e0f1a9c16e4d5b576158e03c849`、两类 Complete 输入及已接受的 WP3/WP4/WP7；PR #59 7/7 必需检查、合并后 WP4 11/11 与总 GA 42/42 均通过，工作区干净且未执行生产部署。
- 新增 WP0 独立失败关闭校验器 9/9，并把模板、未见证清单、缺前置、输入/Owner/日期/Commit 漂移纳入总 GA 47/47 组合门禁；WP0 已 Complete/Accepted。
- Core GA 仍为 72% / `NoGo`：0 个外部输入、WP1/WP2/WP5/WP6/WP8 共 5 个 Gate 和 1 个最终签署 Pending。下一主线是 WP1 手工建模结果接受。

## Space WP4 三路径正式接受（2026-08-28）

- 复用 WP7 冻结 Source Set 与精确 AutoCAD Primary，授权真实 `L1-C01.dwg`、`L1-C02.dxf` 的 CAD IR Package 哈希已绑定；Excel–CAD 使用产品自身生成、Open XML 校验并解析的 12,466-byte 标准 XLSX，手工路径使用受控 PDF、PNG 与空白画布。受控资产均明确 `productionDataClaimed=false`。
- 完整 `CP6.Space.IntegrationTests` 在 SQL Server Express LocalDB `17.0.4025.3` 上执行 `465 passed / 0 failed / 0 skipped`；CAD、Excel–CAD、底图/空白画布均证明 Preview 不改 Draft、显式 Apply、Typed Changeset、Lease、Revision 与 Idempotency 语义。
- 新增正式三路径协议、失败关闭校验器、11 个专项正反向测试和总 GA 组合门禁；`three-path-formal-evidence-v1.0.0.json` 通过并由 `BUBAO.GAO` 接受，WP4 已 Complete/Accepted。
- Core GA 仍为 72% / `NoGo`：两类外部输入均 Complete，剩余 WP0～WP2、WP5、WP6、WP8 共 6 个 Gate 和 1 个 DeliveryOwner 签署 Pending；未执行生产部署、生产 WMS 联调或 WP8 发布演练。下一主线是把可独立复核的 WP0～WP2 逐项接受，再收口 WP5/WP6/WP8。

## Space WP7 正式黄金 CAD 接受（2026-08-28）

- 同一冻结 Source Set 的 20 份 `ApprovedOriginalWork` CAD 已完成正式业务评估：Overall 覆盖率 99.0224%、准确率/高置信精确率 98.7008%、Wilson 下界 98.1717%、人工操作下降 96.9043%；Out-of-sample 五项分别为 99.2352%、98.9828%、98.9828%、98.3541%、97.5781%，Holdout 未报告 Blocking 遗漏为 0。
- 精确 50 MiB 的授权原创派生性能包络执行 1 次预热和 20 次稳定观察；可审查提案 P95 0.038715917 分钟、首次 Ready P95 0.032280823 分钟、0 失败。证据明确不把 DXF `999` 注释填充冒充客户复杂几何，也没有伪造确认、Apply 或 Draft 写入。
- `golden-cad-formal-evidence-v1.0.0.json` 通过失败关闭校验，Primary 资格分 86、`releaseEligible=true`；WP7 已 Complete/Accepted。原始 CAD、完整评估请求、性能 CAD 和 Worker 二进制仍在仓库外。
- Core GA 仍为 72% / `NoGo`：两类外部输入均 Complete，剩余 WP0～WP2、WP4～WP6、WP8 共 7 个 Gate 和 1 个 DeliveryOwner 签署 Pending；未执行生产部署。下一主线转为 WP4 三路径的受控真实端到端接受，并复用 WP7 的同一 Primary/Source Set，不再重复黄金集评估。

## CRM Platform P01/P10 签名里程碑对齐（2026-08-28 UTC）

- CRM V1 公开执行规格已消除 P01/P10 的签名冲突：P01 以空模板编译、可重复 pack、runner contract、无跨仓 ProjectReference 和“不发布空包”作为完成证据；正式 NuGet/镜像签名候选仍属于 P10 发布治理。
- 该修正只对齐已批准的里程碑边界，不新增运行时能力。P01 可保持 `Frozen / Producer Ready`，P02–P10 仍须分别以代码、测试、版本化产物和消费证据关闭。

## Space AutoCAD Primary V1 资格接受与 WP3 结案（2026-08-28）

- `BUBAO.GAO` 已确认 V1 本机受控边界：OS Firewall 出站禁网不再是本地阻断门禁；无业务凭据、无网络监听、临时 CAD 强制删除和可审计报告仍为硬边界。该确认不授权生产、公共 SaaS、远程托管或 Autodesk 软件再分发。
- 正式 `1.0.0` Worker、20 份受控 CAD Release 评测和六维评分已绑定；AutoCAD Primary 得分 86/100，`qualify-providers` 返回 `cadGaReady=true`、唯一 Primary、0 Blocking Code，选择 SHA 为 `d7b9645d915f28e165209b71f69386305711301a6a2fecf7422c15cbcc2a0faa`。
- `PRIMARY_PROVIDER_AND_ISOLATED_WORKER` 外部输入已 Complete，WP3 已 Complete/Accepted；Backup 保持可选且不阻断 V1。评分工具未修改 Site 配置，精确 Provider Version 的实际 Site 写入须继续遵守权限、Revision Fence 和发布演练边界。
- WP7 仍诚实保持 Partial/Pending：转换覆盖、确定性、性能和删除已证明，但业务总体准确率、高置信精确率/Wilson 下界、人工操作减少率及受训用户首次 Ready 时间尚未接受。正式 Core GA 仍为 72% / `NoGo`（0 类输入、8 个 Gate、1 个签署 Pending），没有执行生产部署。

## Space AutoCAD Primary 正式 Release 绑定评测（2026-08-27）

- Release 绑定评测器已由 PR #53 在 7/7 required checks 后合并；从精确 `main@d2d0a0d1b0978a4283bd9387f4120eefe10a135d` 构建并封存正式 `1.0.0`，完整 Worker Release SHA 为 `c794e9c0ebbb2c736866827e07e6682347992dd5a672218efddfe6ff5c0f202e`。
- 正式 20 份受控 CAD 双跑 20/20、确定性 20/20；14,699 总实体中 14,659 支持、支持率 99.727873%，缺失 SourceRef/Blocking/残留均为 0，P95 4.281 秒。版本化报告 SHA 为 `97a9ff7f7cbd60f2c2ea34a5b16e0d645823d94980cd43581dca7129e0373350`。
- 正式报告完成 Release 身份、转换质量、确定性、性能和清理证据，但仍诚实记录 `outboundNetworkPolicy=NotVerifiedAtOsBoundary`；没有生产 mTLS/Firewall、Site 认证或生产部署声明。WP3/WP7 继续等待隔离审批、资格评分和完整业务准确率/操作时长证据。
- 正式 Core GA 仍为 72% / `NoGo`（1 类输入、9 个 Gate、1 个签署 Pending），原始 CAD 与 Worker 二进制保持仓库外。

## Space AutoCAD Primary 批准与单 Provider Ready（2026-08-27）

- `BUBAO.GAO` 已批准当前 AutoCAD 2025 Core Console 作为 V1 唯一 Primary；批准范围限于本机受控 CP6 开发、验证和 Release Rehearsal，不代表 Autodesk 软件再分发、公共 SaaS 托管或生产部署授权。
- 当前 Core Console `25.0.58.0.0`、SHA-256 `d1fd7232893094234f31c65445d0ec9259ffc1df17fb15aad99373e31545cefb`、Autodesk 签名有效，Licensing Service 正在运行；真实安装型合同/Worker 测试 2/2，4,424/4,422 实体，残留 CAD/Attempt 为 0。
- 修正 Lean Schema 3 的运行时漂移：一个满足全部硬门禁、资格分至少 80 且唯一最高分的 Primary 即可通过 `qualify-providers`；机器规则显式升级为 `cad-provider-adr-0001-v2`，Site `CadGaReady` 也只由合格 DWG/DXF Primary 派生。Backup 可选且不阻断 Core GA。
- Primary 选择和受控使用范围已批准，正式 SemVer Worker 与同一 20 份 CAD 的 Release 绑定转换评测也已完成；完整外部输入仍 Pending，下一步是补齐隔离/安全依据并用正式报告生成资格评分。当前仍为 72% / `NoGo`，没有生产部署。

## Space Lean Core GA Schema 3（2026-08-27）

- 首版 Core GA 已移除独立 Backup Provider、Greenfield/Retrofit 双仓、各 14 天 Pilot、客户来源 CAD 和额外人员确认等过度流程门禁；Backup 与现场 Pilot 转为 GA 后韧性/推广增强，不再阻断单人开发结案。
- 硬质量门禁不降：20 份冻结 CAD 与 Holdout、防泄漏、单一 Primary 的真实许可和隔离 Worker、资格分/质量/Wilson/人工操作/性能阈值，以及 SQL Server + CP6 WMS + Published-only Viewer + 恢复 + 安全发布演练全部保留。
- GA 索引升级为 Schema 3：外部输入由三类收敛为 `AUTHORIZED_GOLDEN_CAD_CANDIDATES` 与 `PRIMARY_PROVIDER_AND_ISOLATED_WORKER` 两类，WP3 改为单 Primary，WP8 改为一次内容哈希固定的受控发布演练和单一 DeliveryOwner 签署。
- 当前 CAD 输入已 Complete；剩余 1 类外部输入、9 个正式接受 Gate 和 1 个签署 Pending，派生状态保持 72% / `NoGo`。本次没有伪造 Provider 批准、发布演练或生产部署。依据见 `docs/space/reports/2026-08-27-space-lean-core-ga-reset.md`。

## Space 原创黄金 CAD 候选完成（2026-08-27）

- 单人开发场景已正式采用 `ApprovedOriginalWork`，不再要求不存在的客户来源或第二复核人，也不虚构客户、地址或授权关系；`BUBAO.GAO` 是 20 份原创 CAD 的作者、授权人和实名复核人。
- AutoCAD 2025 原生引擎已在仓库外受控证据区生成并冻结正好 20 份唯一 CAD：10 DWG + 10 DXF，Calibration/Validation/Release Holdout 为 10/5/5，L1～L5 各 4，均为 AC1032；合计 14,659 个 Model Space 图元和 2,455 个带 Handle 的逻辑标准答案元素。
- 每份文件均具有源 SHA-256、原创授权、脱敏证明、单位/坐标系/布局、DWG/DXF 版本、标准答案/预期问题、Mapping Profile/规则版本及实名复核。Source Set SHA 为 `7bc708d5a85b1da2e7f35d43c0e94e38deacda72316d9dbbf09db5e97a742955`，Golden Dataset SHA 为 `2b9438e09e2953b169770d0ee9292d8f9cc9ed697337111bcb61b913484b1f15`。
- 产品 Converter Contract Runner 对 20/20 文件验证通过。仓库只保存脱敏 Manifest、哈希和受控 URN，原始 DWG/DXF 不入 Git；三类外部输入中的 `AUTHORIZED_GOLDEN_CAD_CANDIDATES` 已为 Complete。
- 这不等于 WP7 接受：按后续 Lean Schema 3，单一 Primary 批准/评分、质量与性能、受控发布演练和最终签署仍未完成。正式 Core GA 保持 72% / `NoGo`，剩余 1 类外部输入、9 个 Gate 和 1 个 DeliveryOwner 签署 Pending。依据见 `docs/space/reports/2026-08-27-space-original-work-golden-cad-candidates.md` 与 Lean 重置报告。

## Space Studio Development V1 100%（2026-08-27）

- `CP6_SPACE_STUDIO_DEVELOPMENT_V1` 已在 RepositoryAndDevelopment 范围达到 `DevelopmentComplete` / 100%：统一 Draft/模板、CAD/Excel/手工三路径、编辑器、2D/3D Viewer、开发 CAD Worker，以及发布/WMS/安全/恢复仓库门禁共六项全部通过；唯一 Owner 为 `BUBAO.GAO`，没有多人门禁。
- 20 份合成 DXF 重新生成与完整性审计通过，L1～L5 各 4，覆盖五个冻结 DXF 版本；53,190,207 bytes / 670,000 实体与 79,517,079 bytes / 1,000,000 实体两档压力资产通过。29 个文件的唯一字节差异是 Windows CRLF 检出，换行归一化后完全一致。
- 开发版验收索引与失败关闭校验器强制 `formalGaEligible=false`、`countsTowardProductionGa=false`，并禁止开发数据/报告进入正式 accepted evidence。正式 E02 Ready 负向审计仍因 0 正式黄金、无 10/5/5、无 DWG/版本矩阵以退出码 3 失败。
- 双轨状态固定为：Development V1 = 100% / `DevelopmentComplete`；正式 Core GA = 72% / `NoGo`。后者的 3 类外部输入、9 个接受 Gate 与 1 个签署没有被开发版替代。依据见 `docs/space/reports/2026-08-27-space-development-v1-acceptance.md`。

## Space GA 单人 Owner 与外部输入盘点（2026-08-27）

- `BUBAO.GAO` 已登记为唯一 DeliveryOwner、三类外部输入和 WP0～WP8 的责任人；Kickoff 为 `2026-08-27`，目标 GA 为 `2026-09-27`。WP0 仓库实现改为 Complete，接受与唯一签署仍 Pending，不存在第二人或多人门禁。
- `D:\CP6` 跟踪 CAD 共 28 份，全部为 DXF、DWG 为 0；其中正好 20 份的 `development-v2.0.0` 明确是 1.6～3.9 KiB 合成 DevelopmentSeed，`countsTowardReleaseGate=false`。其余为旧 seed/测试 fixture，均不能计入授权黄金集；授权/脱敏只找到空模板，没有正式编号、逐文件证明和 10/5/5 Manifest。
- `CP6_SPACE_ODA_LICENSE_PATH` 未配置，常见目录没有 ODA Drawings SDK 包；`tmp` 内只有历史 File Converter 27.1 非商业示例边界，不是 Backup SDK/生产授权。Greenfield、Retrofit 和 WMS 窗口也未提供。
- 三类外部输入、WP0～WP8 接受和 DeliveryOwner 签署均保持 Pending；整体继续 72% / `NoGo`。完整依据见 `docs/space/reports/2026-08-27-space-ga-owner-and-cad-intake.md`。

## Space AutoCAD 候选 Worker 不可变 Release 身份（2026-08-27）

- 可运行 Worker Host 不再以 development Provider 启动；发布后的可执行文件生成 Schema 1 Release Manifest，固定全部 Payload 文件、源提交、Runtime、真实 Core Console 完整哈希/文件版本和 DXF Converter 版本，启动时再由外部 SHA-256 锚定并逐项复核。
- 通过后派生 `cp6-autocad-worker/{semver}+worker.{manifest-prefix}.autocad.{core}.dxf.{parser}`；远程协议升为 Schema 2，部署批准 Manifest 的完整 Worker Release SHA 会进入每次 API 请求、在 Worker 落盘前核对并由响应回显验证，12 位版本前缀不能替代完整哈希。`/health/live` 同时返回完整 SHA、源提交和 Runtime；每次 DWG 转换前还会复核 Core Console 完整哈希。
- 真实本机 `win-x64` 发布演练封存 18 个文件，Schema 机器验证通过，Core Console 为 `25.0.58.0.0`；完整 CAD Experiment 含真实安装门禁 57/57、0 skipped，远程协议专项 6/6，`CP6.Tests` 2,939/19/0，整仓 0 warning / 0 error，残留 CAD/非空 Attempt 为 0。
- PR #46 已在 7/7 required checks 后合并为 `main@4375c7c2fc1e297bf3fe845873b1af5af2cb5d66`；合并后用 `0.0.0-rehearsal.postmerge` 从该精确提交重建 18 文件 Payload，Schema 通过，完整 Worker Release SHA 为 `c51c2ce8925f7bf2bf647dd2d958270d7903e6adc212eee37a668bfe9d82dc84`。这只证明主干可重现密封，不是正式 Release/批准证据；仍须用批准 SemVer 重建、取得许可证/Site 批准、部署生产等价隔离环境、接入独立 Backup 并运行授权黄金集。WP3/整体继续 Partial/Pending、72% / `NoGo`。

## Space 托管 DXF Parser 50 MiB 容量（2026-08-27）

- DXF Parser 从 25 MiB 整文件 byte[]/整文本/Split 数组升级为严格 UTF-8 逐行解析，底层流同步执行原始字节计数与 SHA-256；无语义的 999 注释会完整验证但不驻留，当前失败关闭上限为 64 MiB。
- 精确 50 MiB 合法 DXF 合成容量包络通过并生成 1 个实体；64 MiB+1 seekable 输入在解析前拒绝且没有工件。Converter 升为 `cp6-development-dxf/1.1.0`，AutoCAD 组合 Provider Version 自动变为 `{core}+cp6-dxf-1.1.0`，旧认证不能静默复用。
- 完整 CAD Experiment 在真实 Core Console 环境为 47/47、0 skipped；既有 DWG 指标保持 29/19/4,424/4,422，测试根残留 CAD/Attempt 为 0。
- 50 MiB 文件是有效 DXF + 999 注释的合成容量包络，不是正式黄金集性能结果。按 Lean Schema 3，仍需 Primary 的真实 50 MiB/Ready P95、正式 Release、批准和受控发布演练；WP3/整体继续 Partial/Pending、72% / `NoGo`。

## Space AutoCAD 候选 Worker DWG/DXF 双格式（2026-08-27）

- AutoCAD 隔离 Worker 候选现以组合链 Provider 身份同时接受 DWG/DXF：DWG 经精确 Core Console 后进入托管 DXF Parser，原生 DXF 直接进入同一 Parser且不会启动 AutoCAD；外层和内层 Converter 均经统一合同执行器。
- Provider Key 改为 `cp6-autocad-worker-development`；组合 Version 当前已由上方容量任务升为 `{core}+cp6-dxf-1.1.0`（初始双格式里程碑为 `1.0.0`），任一链版本变化都强制重新评分与 Site 认证。
- 候选聚焦 4/4、真实安装环境完整 CAD Experiment 45/45、0 skipped；Core Console 样例仍为 29 图层/19 块/4,424 实体/4,422 支持实体，原生 DXF 断言 0 次 Exporter 调用，测试根残留 DWG/DXF 与 Attempt 均为 0。
- 这只关闭 Primary 候选的 DXF 仓库路径。原生 DXF 仍是 fixture；Parser 容量和不可变非 development Release 机制虽已由上方任务补齐，但正式合并提交 Release、授权 20 份黄金集/真实 50 MiB、独立 Backup、许可证/Site 批准和生产 mTLS 隔离/Failover 仍缺。WP3 保持 Partial/Pending，整体保持 72% / `NoGo`。

## Space Studio WP3 远程隔离 CAD Worker Provider（2026-08-27）

- 已补齐生产侧远程 CAD Provider 接入：Design API 不加载供应商 SDK 或启动 CAD 进程，只通过 mTLS 向隔离 Worker 发送原始 CAD、源 SHA-256、格式和精确 Provider 身份；Tenant/Site/用户/模型/数据库/Mapping/Object Storage 身份均不跨边界。
- 运行注册默认关闭；只有外部哈希固定且未过期的部署批准 Manifest、精确 Provider/版本/格式/部署与数据边界、有效客户端证书、CA/主机名验证和服务端证书 SHA-256 Pin 全部通过时才启动。Mapping Profile 精确版本、完整 Override Replay、语义、诊断和 PreviewSet 仍在 CP6 内生成，Worker 不能选择 Mapping 或写 Draft。
- 新增可运行的 AutoCAD Core Console DWG 候选 Worker；本机真实调用 `accoreconsole.exe 25.0.58.0.0` 处理 Autodesk 样例，得到 29 图层、19 块、4,424 实体/4,422 支持实体，Provider 身份和 Attempt 原始/派生清理合同通过。该样例不是授权黄金 CAD，只计开发候选证据。
- 远程 Provider、路由、候选 Worker、Space Unit/Integration 和完整 solution 门禁已通过；后续任务又补齐 DWG/DXF 双格式、50 MiB Parser 容量和不可变 Release 身份机制。仓库生产接入层与候选 Host 已完成，但许可证/客户/Site 批准、真实 mTLS 无出口部署、从合并提交生成的正式 Release、独立 Backup、20 份黄金集评分和生产等价 Failover 尚缺；WP3 保持 Partial/Pending，整体保持 72% / `NoGo`。

## Space Studio WP1 仓库实现闭环（2026-08-27）

- WP1 Design V1 手工建模的仓库实现已从 Partial 推进为 Complete：同一向导支持 Blank、Published、System Template 和 Tenant Template，模板模式按密封预览幂等初始化全部楼层，版本持久保存来源、模板版本和内容 SHA-256。
- 当前生产 Draft 可先零写入预览，再通过仓库人员表单创建租户私有不可变整仓模板；空白首层显式要求宽度/深度，未完成模板初始化的重试不会覆盖后来修改过的楼层。
- SQL Server LocalDB 纵切覆盖 2 楼层、500 货架、10,000 库位及 Tenant Scope；Version Clone 全类 17/17、OpenAPI 57/57、Web 聚焦 19/19、完整 solution 0 warning / 0 error，EF/SDK/type-check/production build 门禁通过。
- 这只关闭 WP1 的实现缺口，不构成正式接受：`acceptanceStatus`、3 类外部输入、WP0～WP8 和 DeliveryOwner 签署仍 Pending；整体继续为 72% / `NoGo`，下一工程主线是需要真实授权与环境的 WP3 Provider/隔离 Worker。

## Space Studio 单人交付门禁（2026-08-26）

- 本节记录 Schema 2 当时口径；当前阻断项已由本文顶部 Lean Core GA Schema 3 取代。
- 核心 GA 治理已从多人角色门禁改为单一 `DeliveryOwner`：同一实名开发者可兼任产品、开发、QA、UX、架构、安全和 WMS 联调，自验收并签署；不再要求 2 Backend + 2 Frontend3D + 1 QA、五角色实名签字、不同复核人或第二人审批。
- `ga-evidence-index.json` 与开工/黄金 CAD Manifest 升为 Schema 2；外部输入由 5 类减为 3 类（授权 CAD、Provider/隔离 Worker、双仓/WMS 窗口），签字由 5 个角色减为 1 个 DeliveryOwner，黄金 CAD 每份样本由一个实名 `reviewedBy` 复核。
- Schema 2 当时仍要求主备 Provider 与两仓 14 天 Pilot；Schema 3 已把它们改为单 Primary + 一次受控发布演练，同时保留数据、质量、性能、恢复和安全结果门禁。
- 自动化已证明单一 Owner 可以拥有并接受全部外部输入、复核全部黄金样本并签署 GA；模板、fixture、原始客户 CAD、哈希错误、占位身份和未达技术指标仍会被拒绝。本文下方及历史报告中旧的 2+2+1、五方签字、双标注/独立 QA、独立 UX/安全或第二人审批表述只保留作当时审计，均由本节与 Schema 2 合同取代，不再是当前门禁。

## Release/CD 仓库与平台工程结案（2026-08-26）

- PR #32 已把 Azure Release Shadow S0 合入 `main@9009abe687c693fdcbd650261f39b56cf8ccf8fb`；GitHub required checks、额外 Space/CRM 守卫和本地 1 个有效/10 个失败关闭场景全部通过。
- Azure 已创建 `CP6 Release Shadow` Definition #5，只定向授权现有 GitHub endpoint 与 `Default` queue；Run #145 绑定上述完整 SHA，在无 Variable Group、无 Registry/Environment 权限下输出 `Authority=Shadow`、`Deployable=false` 的辅助 Artifact `cp6-release-shadow-s0-145`，结果为 `Succeeded`。
- Phase 1 PR 归属固定为 GitHub，Azure 保持 `pr: none`；OpenAPI/SDK、Android、WMS SQL、候选 E2E/供应链与 Space 独立门禁的责任矩阵，以及 self-hosted Agent 更新/磁盘/离线/单并发/身份隔离规则均已固化。
- Release/CD 的工程建设状态改为 `Complete`；首个生产发行仍为独立的 `No-Go` 执行门禁。实时审计确认 GitHub 无 R2 Release、受保护版本 Tag、R2 workflow Run、Environment 或仓库 Secret，`v1.0.0` 仍是 Draft 且 20 项输入 Pending，Freeze gate 按预期拒绝。
- Phase 3 S1 和 Phase 4～7 只在真实候选、外部环境、批准人或 Pilot 到位后按事件重开单任务卡；不得以 fixture、空 Environment、Azure DEV 成功或工程结案冒充生产上线。完整依据见 `docs/devops/RELEASE-CD-ENGINEERING-CLOSEOUT.md`。

## CRM V1 PRD 完整脱敏产品基线批准（2026-08-26）

- `docs/crm/CRM-V1-PRD.md` v0.2 已迁移到最新 `main` 基线，并移除商业 cohort 数量/地域/名单、精确推广时间表、私有数值商业 KPI、数值 Pilot UAT 和采用门禁；候选期措辞改为不随状态漂移的批准规则，批准的 payload SHA-256 为 `5e646cc8e394c74c35f9716216be1d12fa5f4f7210e42d8d52ab9b86f4528a3a`。
- 合并前全披露面复核进一步清理 `CRM-M0-READINESS.md`、`CRM-PRODUCT-FRAMEWORK.md` 和 `CRM-V1-EXECUTABLE-SPEC.md` 中遗留的 Pilot 样本量、采用窗口和数值 KPI，只保留公开证据类别、技术 SLO 与产品行为；`docs/crm/**` 的全部现有文件由自动发现清单和规范化 SHA-256 失败关闭，新增未登记 CRM 文档会直接阻断。
- `crm-v1-prd` 门禁区分 PR head 诊断与受保护基线验证：当前引导 PR 的 head check 不冒充独立信任边界；工作流进入 `main` 后，required context 只由 `pull_request_target` 中的受保护 base validator 对精确 PR head 数据产生，并以只读权限运行。
- 三次未合并的预审批尝试因脱敏声明不完整而作废；相关证据仅保留在未合并 PR 审计轨迹中，新的干净分支不继承旧敏感提交祖先，也不把旧对象引用写入公开产品基线。
- 当前状态为 `Approved product requirements baseline`；批准绑定候选 commit `00fa3aea66045cb2b949b691824f0fbb830cc739`、PRD blob `b6f8da119bf700340616e8a2d3cc01ceb0dd38d6`、PR #35 ProgramOwner 评论正文摘要 `4092bc5ec3338be408292c5f240579ed036dcd1033858b4d237dc38d39608de1` 和 append-only 历史摘要 `76b3d5d481ad6c128f70abc7ceb770e430907fed97ca8bdd986873dc492720b3`。Public Contract Sync 为 Complete，M0 仍为 No-Go；没有创建业务代码、云资源、Secret、数据库、迁移、候选制品或部署，也不解锁 CRM01。

最后更新：2026-08-27

## Azure Release Shadow S0 仓库合同完成（2026-08-26）

- 新增独立 `azure-pipelines-release-shadow.yml`，固定手动 `trigger: none` / `pr: none`；只运行仓库内固定 `v1.2.3` fixture 合同并发布辅助 `shadow` Artifact，没有 Variable Group、Service Connection、外部下载或自动触发。
- 新增 `Test-Cp6ReleaseShadowCandidate.ps1`，严格验证 Schema 1 candidate result、Schema 2 release manifest、Schema 1 freeze snapshot、version/Tag/完整 Git SHA、manifest/freeze/spec SHA-256、GHCR repository allowlist/完整 digest、三类原生签名制品、供应链证据和 ForwardOnly db-init 元数据。
- Shadow 报告语义在解析器内固定为 `Authority=Shadow`、`Deployable=false`；候选不能通过输入覆盖。合同包含 1 个有效场景与错误来源、版本、SHA、可变 Tag、manifest/spec hash、repository、digest、freeze 绑定和 Deployable 越权共 10 个失败关闭场景。
- `test-r2-source-gate.ps1` 已纳入 S0 PowerShell 解析与行为合同。静态门禁拒绝 Build/Push/Pull/Tag、ACR、外部 fetch、Service Connection、自动 trigger 和部署命令。当前未访问真实 R2/GHCR、未拉取镜像、未创建 Azure Pipeline/连接、未部署；下一单任务卡是 S1 真实候选只读元数据。

## 发布权威与 Registry 决策完成（2026-08-26）

- `ADR-DEVOPS-001` 已固定当前 CP6 唯一候选 Registry 为 GHCR、唯一候选/部署权威为 GitHub R2；API/Web 只由受保护 `vX.Y.Z` Tag 触发的 R2 candidate 一次构建，Schema 2 `release-manifest.json` 与 `candidate-result.json` 构成唯一候选链。
- Azure 下一阶段改为非权威 Release Shadow：只读验证同一 Tag、完整 Git SHA、manifest/freeze/spec/证据哈希和 GHCR digest，输出固定 `Authority=Shadow`、`Deployable=false`；禁止重新 Build、Push、签名、生成第二份清单或部署。
- R2/Azure 等价矩阵、只读 Service Connection 权限、S0/S1/S2 影子期、30 分钟回退和未来 ACR 硬门禁已记录；当前不创建 ACR、不购买资源、不接触生产 Secret/环境。
- Phase 3 S0 仓库合同已完成；下一张单任务卡是 Shadow S1 真实候选只读元数据，需先审批最小只读身份，仍不得 Build/Push/Tag/Deploy。

## DEV 自动发布稳定性闭环完成（2026-08-26）

- #129 以 31 次 1328～1861 MiB 采样证明 2048 MiB + 3 次连续 SQL readiness 会在备份前失败关闭；依据本机约 8 分 40 秒恢复实测，等待窗口保持安全阈值不变并扩为 600 秒。
- #131 attempt 1 的 61 次采样仅 1254～1756 MiB，SQL/备份前安全失败；同 Run 重试 `DeployDev` 后 readiness、CHECKSUM/VERIFYONLY 备份、迁移、健康和 `main@50a1db6d...` 身份均成功。最终失败仅因 attempt 1/2 复用固定 `cp6-dev-evidence` 名称；PR #30 改为 `cp6-dev-evidence-attempt-$(System.StageAttempt)` 并增加合同回归。
- PR #30 合入 `main@08813896d80cc11d7829194e432ae0fbcfa243f6` 后，GitHub client-contract/SQL 通过；Azure 基础 CI #132 以 `individualCI` 成功桥接同 SHA Artifact，并由 `pipelineTriggerType=PipelineCompletion` 自动创建 DEV #133。
- #133 readiness 三次为 2184/2383/2411 MiB 且 SQL=True；第 7 份备份 `CP6_DEV_20260826_042513_866_887d307c_UTC.bak` 为 2,600,960 bytes，SHA-256 `af4f48fd19daeeb2461411a4210a1cb384c649a4fd01322b82b74555d804c9de`，`BACKUP CHECKSUM` 与 `VERIFYONLY` 均通过。
- #133 API/Web 均为 `0.0.0-dev.08813896d80cc11d7829194e432ae0fbcfa243f6` 且 Healthy，image ID 分别为 `sha256:ccfcb019...633f00` / `sha256:16f44b80...c50149`；`cp6-dev-evidence-attempt-1` 发布成功。根 `cp6-api`/`cp6-db` 的 ID、StartedAt、RestartCount `3/2` 未变；自动开关保持 `true`、公网验证保持 `false`，GitHub R2/GHCR 生产权威未变。

## DEV 自动发布开启与首次验收（2026-08-25）

- 手动 DEV 验收 3/3 后，用户明确接受继续完成自动闭环；Azure `CP6 DEV CD` 的 `CP6_DEV_AUTO_DEPLOY_ENABLED=true`，`CP6_DEV_PUBLIC_VERIFICATION_ENABLED=false`，因此自动模式已开启但本次没有切换 `cp6.uk`。
- 基础 CI #124 成功后约 6 秒自动排队 DEV #125 / `dev-20260826.1`。Azure REST 确认 `reason=resourceTrigger`、`pipelineTriggerType=PipelineCompletion`、来源 `GTX537.CP6` #124 / `main@ecbad9e1...`；Classify、587 文件 Artifact 校验、runtime-only Package、锁内 freshness、Backup、Deploy 和证据发布全部 Succeeded。
- #125 生成第 5 份备份 `CP6_DEV_20260826_012133_290_2a3d7daf_UTC.bak`，2,572,288 bytes，SHA-256 `bcd9f2282bd747b61d292852570fdc8df3e7329e012473de6a1ad6171ba3a574`；Pipeline 日志记录 `BACKUP CHECKSUM` / `verifyOnly=passed`，本机重新计算哈希一致，`cp6-dev-evidence` 成功归档 2 个文件。
- 当时 DEV API/Web 均为 `0.0.0-dev.ecbad9e1...` 且 Healthy，运行 image ID 分别为 `sha256:c8d2a559...145d2` / `sha256:d9d03015...24b4c`，最新迁移为 `20260811030108_CrmFoundation`。根 `cp6-api`/`cp6-db` 的容器 ID、StartedAt、RestartCount `3/2` 完全不变，旧 `cp6-cloudflared` 继续运行。任何旧版本手动回退前仍必须先关闭自动。

## GitHub 远程构建与 Azure 轻量 Artifact 桥（2026-08-25）

- 本机完整编译在 #113/#115 仍触发宿主内存或 SQL 门禁；Azure 组织 #110 又无 hosted parallelism，因此基础编译迁至 GitHub `client-contract.yml`。它在 GitHub-hosted Runner 完成 .NET、客户端、OpenAPI、Web、Android 与 R2 source 门禁，生成名称含完整 SHA、内部含逐文件 SHA-256 的 3 天 Runtime Artifact。
- Azure `azure-pipelines.yml` 现只执行合同、使用授权 Checkout 凭证下载、核对工作流来源/事件/结论/完整 SHA/归档 SHA-256、验证 ZIP 路径与内部 manifest，再发布 Azure `cp6-dev-runtime`；不在本机运行 .NET/Node，也不部署。
- GitHub Run 32879704210 在 `f18e4610...` 首次完整成功并生成约 49.8 MiB 产物。Azure #116 因错误查询非仓库专属 extraheader 在下载前失败、Publish skipped；修复为仓库专属键后，GitHub Run 32881647447 在 `489c99be...` 完整成功，Azure #117 随后成功完成下载、双层验证与发布。SQL 与公网七容器 ID/StartedAt/RestartCount 不变。
- PR #24 已合入 `main@a5c6b5fa...`；GitHub client-contract 与 Azure #118 成功产出/桥接同 SHA Runtime Artifact，自动 completion #119 在开关关闭时安全跳过。
- Manual #120/#121 各自完成独立分类、Artifact 验证/封装、CHECKSUM/VERIFYONLY 备份、迁移、健康/身份验证和 `cp6-dev-evidence` 发布；备份由 2→4。#120 备份 SHA-256 `c90a3db2...19a3a`，#121 为 `9fc35ca1...414fb`。最终 API/Web 版本 `0.0.0-dev.a5c6b5fa...59e6`、live/ready Healthy、最新迁移 `20260811030108_CrmFoundation`。
- 手动验收现为 **3/3**。最终 8/8 SQL 查询成功、近 45 分钟无 701/17300，公网七容器 ID/StartedAt/RestartCount 全部不变；只有旧 `cp6-cloudflared` 运行。自动/公网开关继续为 `false`，GitHub R2/GHCR 生产权威未变。

## DEV Manual Run #98/#101/#107 内存失败与 CI Artifact 隔离（2026-08-25）

- Artifact 主线 CI #109/#111 均证明 `Default` Pool 的默认 MSBuild 并行度会在本机 SQL/Docker 共存时形成不可接受的内存竞争：#109 在 API build 工作集约 2.97 GiB、宿主可用约 0.96 GiB时取消；用户释放内存后重跑的 #111 仍达到约 4.63 GiB、可用 1.24 GiB，按门禁取消。两次都没有 Artifact 或 DEV completion/deploy，备份仍为两份，根 API/DB 与旧 DEV API 的 ID、StartedAt、RestartCount 不变，SQL 随取消立即恢复。
- Microsoft-hosted 探测 Run #110 正确选到 `Azure Pipelines` hosted Pool，但组织未获赠或购买 hosted parallelism，Job 在 Checkout 前以 `No hosted parallelism has been purchased or granted` 失败；没有本机负载或环境副作用，也未擅自启用计费。低内存分支 Run #112 随后把 restore 固定为非并行、build/test 固定为单 MSBuild 节点并关闭持久/共享编译服务器、Vue 单测限制两个 worker；完整 CI 于约 9 分钟内成功并发布 `cp6-dev-runtime`，最低观测宿主可用内存约 2.22 GiB，SQL 全程可查询且三项受保护容器重启数不变。分支 Artifact 不可部署，仍须合入后取得成功的 `main` CI Artifact。
- Artifact 方案首次主线 CI #108 在真正 restore/build 前失败：`test-azure-ci-runtime-artifact-contract.ps1` 已打印 passed，但 Azure Windows PowerShell 5.1 继承了进入 Step 前的非零 `$LASTEXITCODE`，随后 YAML 把这一陈旧值误判为脚本失败。根因不是合同断言、YAML 或产物逻辑；Run 无编译、Artifact、completion DEV 或部署副作用，根 API/DB 与旧 DEV API 三项元数据均不变，`CP6_DEV` 仍 ONLINE。修复为 PowerShell 脚本依靠 terminating error/`ErrorActionPreference=Stop`，禁止用继承的外部进程退出码判断 `.ps1` 成败，并对基础 CI/DEV 两个合同 Step 增加静态回归。
- 继续 2/3、3/3 验收前，宿主机仅余 1.09 GiB / 6.9% 内存，预检按门禁拒绝排队；关闭 Chrome 后，12/12 次 `CP6_DEV` 独立 SQL 连接/查询通过，最低可用内存 3.57 GiB / 22.6%，无新增宿主 SQL 701/17300。
- Manual Run #98 正确选择成功的 CI #96 / `main@c9b02c82`，但 API Docker `dotnet publish` 期间 Agent 报宿主内存已使用 96.03%。Run 在 Build 阶段人工取消，Deploy 为 Skipped，没有新备份、迁移或 DEV 候选切换，不计手动验收。
- Docker 事件证明根 `cp6-db` 被 OOM kill 后自动重启一次，`cp6-api` 因数据库恢复累计重启两次；两者与其余根容器当前均恢复 Healthy，但“根环境未受影响”门禁已失败，不能只凭容器 ID 未变化放行。
- 低并发修复合入 `main@76d0832e` 后，自动 CI Run #99 与关闭状态的 completion Run #100 均成功；Manual Run #101 / `dev-20260825.7` 正确选择该提交，但 Docker VM 在串行 publish 时仍达到 95.83%。任务在 Build 阶段安全取消，Deploy Skipped，无新备份、迁移或 DEV 镜像切换；根 `cp6-db` RestartCount 由 1→2、`cp6-api` 由 2→3，因此仍不计验收。当前根基线分别为 StartedAt `15:06:55Z` / `15:07:03Z`。
- DEV 候选构建已改为部署 Agent 在宿主机使用 .NET 8/Node 22 串行生成 API publish/Web dist，再由新增 runtime-only Dockerfile 封装；Web Node 堆限制 768 MiB，两个镜像仍各只构建一次并以 `--iidfile` 固定身份。提交 `72ec0e70` 的完整本机验证生成 API `sha256:bc681051...293d92` 与 Web `sha256:c81ae3ce...77e05`，专属临时目录清零，Docker VM 采样保持约 1.9 GiB 以上可用，根 API/DB 三项元数据未变且宿主 SQL 无新增 701/17300。六组契约、PowerShell 解析、差异与凭据扫描通过；GitHub R2/生产 Dockerfile 未改。自动与公网开关仍为 `false`，手动成功计数保持 1/3。
- 合入后 CI #102、completion #104 和 Readiness #105 成功；错误资源版本的 Manual #106 在 YAML 解析前失败，无 Job/副作用。Manual #107 正确绑定 CI #102，但宿主 `dotnet publish` 工作集约 4.18 GiB、可用内存降至约 0.62 GiB并导致 `CP6_DEV` 新连接超时，按门禁取消；Deploy Skipped、备份仍两份、根 API/DB 基线不变，但旧 DEV API RestartCount 16→17，因此仍不计验收。
- DEV 候选改为下载所选成功 CI 的 `cp6-dev-runtime`：基础 CI 对已通过测试的 API/Web 输出写入版本、完整 SHA、逐文件长度/SHA-256，DEV 重新验证完整集合后只做 runtime-only Docker 封装，不再运行 .NET/Node 编译。真实 145,966,387 bytes API + 7,473,275 bytes Web 本机实测共 587 个哈希文件，约 17 秒生成 API `sha256:10a68d65...9573c6` 与 Web `sha256:23d01b7b...94b9ff`；根 API/DB 与旧 DEV API 三项元数据不变，`CP6_DEV` ONLINE/最新迁移不变。生产 R2/GHCR 未改，自动/公网仍为 `false`，成功计数保持 1/3。

## Azure CI 与首次本机 DEV 发布外部闭环（2026-08-25）

- Azure 基础 CI `GTX537.CP6` Run #92 在 `main@47ca8441898af69d1e66bc1acb6c51129dbe9c18` 完整成功。此前固定失败的 `.NET Restore` 根因是通用 `CP6-Windows` Agent 从 PowerShell 7 启动后继承 `PSModulePath`，使 Windows PowerShell 5.1 重复加载类型数据；清空父进程该变量后同一提交通过。新增 `Start-Cp6CiAgent.ps1` 固化 `CP6-Windows` / `Default` 校验和前台隔离启动，不注册服务、不改电源设置。
- `CP6 DEV CD` Definition ID `4`、`cp6-dev-secrets`、`CP6-Deploy` 与 `cp6-dev` 已按 Pipeline 定向授权；Environment 配置 Exclusive lock，两项非 Secret 开关均为 `false`。Readiness Run #89 由 `cp6_deploy_agent` 服务身份成功验证 Docker、SQL TCP、`sqlcmd` 和备份目录；`cp6_dev_backup` 及锁定 Secret 已完成最小权限验收。
- completion-trigger Run #93 成功完成分类并明确跳过 Build/Deploy，证明自动关闭门有效。首次 Manual Run #94 在生成并 VERIFYONLY 备份后因宿主 `KOUSQLSERVER` 已发生 701/17300 内存耗尽、实例退化而在 db-init 元数据查询超时；API/Web 未启动，失败未冒充成功。管理员重启 SQL 数据引擎并停止当前未使用的 PolyBase/Launchpad 依赖后，`CP6_DEV` ONLINE 且 128 条迁移历史可立即读取。
- Manual Run #95 成功发布 `0.0.0-dev.92`：API/Web 均对应完整 SHA `47ca8441898af69d1e66bc1acb6c51129dbe9c18`，`19991` live/ready 与 `18080` release identity 一致，最新迁移为 `20260811030108_CrmFoundation`。新备份 `CP6_DEV_20260825_123030_332_9b7cd05d_UTC.bak` 为 2,453,504 bytes，SHA-256 `58c6ff73...5079c23`，CHECKSUM/VERIFYONLY 通过；`cp6-dev-evidence` 已发布。
- 根 `cp6` 七个容器 ID、Docker `CP6DB` 与既有 Cloudflare connector 全程未变。当前口径是手动 DEV 验收 **1/3**；自动与公网验证仍关闭，未切换 Tunnel，另两次成功 Run、宿主机内存治理和公网身份验收仍是后续门禁。

## CRM 公开产品对比与 PRD v0.2（2026-08-25）

- `docs/crm/CRM-COMPETITIVE-ANALYSIS.md` 已对比 Salesforce、HubSpot、Dynamics 365、Pipedrive、Zoho、Odoo、SAP Sales Cloud、纷享销客和销售易，按获客、Lead 执行、客户/商机、报价成交、ERP 权威、治理扩展及商业模式建立同口径分析。
- 竞品研究没有扩大 V1：`CRM-COMP-001`～`007` 固定 Lead Pilot 行动优先、稳定对象/状态、来源与 SLA、CP6 ERP/ExternalEvidence 成交权威，并把公海、企微/钉钉、邮件/日历、移动拜访、评分和 AI 保留为后续受控扩展。
- `CRM-V1-PRD.md` 已升为 v0.2，并把竞品结论映射到目标、旅程、前端、后端、升级和验收合同。该 2026-08-25 里程碑当时仍是产品评审草案；当前批准状态与 Public Contract 状态以上方 2026-08-26 记录为准，M0 始终保持 `No-Go`。

## 本机 DEV 外部首次运行就绪审计（2026-08-25）

- 只读审计确认 Docker Desktop 29.3.1、Compose 5.1.1、`MSSQL$KOUSQLSERVER` 和专用 Azure Agent `vstsagent.gaobubao.CP6-Deploy.LAPTOP-3QQ44FJS` 正常运行；`CP6_DEV` 已存在，根 `cp6` 七个容器、`CP6DB` 与 `cp6_cp6-db-data` 保持运行且未被修改。
- `sqlcmd.exe` 已安装于 ODBC 17 标准目录，但该目录不在机器级 `PATH`；交互用户能发现它不代表 `cp6_deploy_agent` 服务身份也能发现。备份脚本和 Readiness YAML 现同时探测 PATH、Go sqlcmd、ODBC 18 与 ODBC 17 标准目录；新增 7 场景行为回归覆盖 PATH、显式路径、全缺失、标准目录回退、Secret 前置门与失败后 `SQLCMDPASSWORD` 恢复，相关数据安全/Readiness/DEV CD 合同测试通过。
- 已创建 `C:\CP6Backups\CP6_DEV` 并关闭继承的宽泛修改权限；SQL Server 服务身份具有 Modify，部署 Agent 只有 Read/Execute，当前管理员维护身份、SYSTEM 和 Administrators 保留 Full Control。独立 SQL 登录 `cp6_dev_backup`、Azure 锁定 Secret 和最小权限已在同日后续外部闭环完成；Run #94/#95 已生成并验证真实备份。
- 微软 Azure CLI 2.89.1 与 Azure DevOps 扩展 1.0.6 已安装并完成设备登录。Pipeline/Variable Group/Environment 定向授权、Exclusive lock 和两项 `false` 开关已完成；手动 Run 当前完成 1/3，Tunnel 切换仍待验收。

## 本机 DEV 双模式发布闭环（2026-08-25）

- `azure-pipelines-dev.yml` 统一支持手动和 completion-trigger 自动模式；`CP6_DEV_AUTO_DEPLOY_ENABLED` 初始为 `false`。两种模式都只接受 Azure 回读为 `completed/succeeded` 的 `GTX537.CP6/main` Run；过期自动 Run 安全跳过，自动开启时禁止选择旧 Run 手动回退。
- 候选使用 `0.0.0-dev.<CI Run ID>` 与完整 SHA 镜像 Tag，从所选提交的隔离 worktree 构建；编排逻辑始终来自当前 `main`。构建同步捕获 Docker 不可变 image ID，部署不依赖可能被并发重写的本机 Tag，并逐容器核对实际运行 `.Image`。deployment job 进入 `cp6-dev` 顺序锁后会二次淘汰过期自动 Run，并受 Windows 全局互斥锁兜底；之后先对 `CP6_DEV` 做 COPY_ONLY/COMPRESSION/CHECKSUM 备份及 RESTORE VERIFYONLY，再停止旧 Web/API、执行一次性前向迁移、依次验证 API 与 Web，并归档触发/备份/镜像/健康证据。
- 根 Compose `cp6`、Docker 数据库 `CP6DB` 与命名卷 `cp6_cp6-db-data` 明确排除在 DEV CD 之外。新增手工导出与旁路导入工具；导入只允许新的 `CP6DEV_IMPORT_yyyyMMdd_HHmmss`，拒绝目标已存在且不含 `WITH REPLACE`，不会自动合并到 `CP6DB`。
- 新增独立 `cp6-public-tunnel` Compose，只加入 `cp6-dev_default`。控制器要求旧 `cp6-cloudflared` 已被用户显式停止，避免同一 Tunnel 双 connector 分流；DEV CD 不执行一次性公网切换。Azure Secret/Exclusive lock 已配置且首次手动 Run 成功；另两次手动 Run、Tunnel 切换和公网身份验收尚未执行。

## 登录体验恢复与可访问性闭环（2026-08-24）

- 登录页已恢复为包装制造运营入口，保留原有账号密码、租户自动识别/显式选择、SSO、2FA 跳转、动态菜单和登录后路由合同，并提供 zh-CN、zh-TW、en、ja、ko 五语言产品说明及桌面/移动响应式布局。
- 浏览器根因复核发现，单靠 CSS 折叠的 Tenant 输入仍会进入键盘 Tab 顺序；折叠区现同时使用 `inert` 与 `aria-hidden`，展开或后端返回 `needTenant` 时会把焦点移到组织输入。语言选择器改为普通按钮组语义，密码登录与 SSO 共享互斥忙碌状态，避免并发发起两条认证流程。
- 顶栏不再在没有健康检查请求时显示“系统服务正常”或使用 `role=status`；改为中性的安全访问入口，不伪造实时运行状态。
- 验证为组件聚焦 10/10、Web 全量 176 文件/902 测试、Vue 类型检查、production build，以及 Chromium 桌面 1440×900/移动 390×844 的无横向溢出、折叠 Tenant Tab 跳过和展开聚焦验收。浏览器使用无后端的本地 Vite 时语言 API 返回 502 属测试环境缺少 API，不是本组件运行异常。

## Kafka 生产者安全退出修复（2026-08-24）

- `KafkaProducerService` 作为 DI Singleton 由 Host 在进程退出时释放。关闭流程现在只执行一次：先最多等待 5 秒刷新在途消息，再无条件尝试释放 producer handle；`Flush` 抛错不再跳过真正的 `Dispose`。
- 刷新异常、释放异常和超时后仍在队列中的消息数都会记录 Warning，但旁路操作日志通道的关闭异常不会反向阻断 WebApi Host 退出。正常 Publish、Kafka 配置和运行期降级语义不变。
- 新增 4 个行为回归，覆盖刷新异常仍释放、剩余消息告警、释放异常不外抛和重复调用幂等；`CP6.Tests` 全量 2,938 passed、19 项既有 SQL 环境门禁 skipped、0 failed。

## 日期时间规范化恢复与 P4/P5 决策（2026-08-24）

- 旧 WIP 的 P4 `env.d.ts` 通配 `declare module '*.vue'` 不恢复：最新 `main` 使用 Vue 3.5、TypeScript 6 和 `vue-tsc` 3.2，在没有该声明时完成干净 `vue-tsc --build`。旧声明中的 `DefineComponent<{}, {}, any>` 会弱化 SFC 类型检查，只保留归档证据，不进入主线。
- 日期时间恢复分支把 OA/PMS/WMS/Space 与通用列表中散落的原始 ISO、字符串截断和浏览器直出统一到共享 `formatDateTime`/`formatDateTimeCell`；`VolTable` 与 `CpListPage` 增加显式 datetime 列合同，高精度 .NET 输入不再直接暴露。
- P5 精度合同固定为普通业务 UI 显示本地日期及“时:分”。没有产品需求时不得在全局 `long` 格式加入秒或小数秒；将来若审计/追踪确需更高精度，必须使用独立命名格式和独立验收，不能扩大所有调用方。
- 回归覆盖 zh-CN、zh-TW、en、ja、ko 五种语言及高精度/空值/非法值；Web 全量 175 文件、892 测试通过，Vue 类型检查和 production build 通过。

## 白天临时家庭测试服务器（2026-08-24）

- 当前阶段选择由本机 Docker Desktop 承载 CP6，Cloudflare Tunnel 仅把容器内的 Web/API 转发到 `cp6.uk` 与 `api.cp6.uk`；它是供同事白天临时测试的开发环境，不是云主机或正式生产部署。
- 新增根目录 `cp6-daytime-server.bat` 和 `scripts/Invoke-Cp6DaytimeServer.ps1`：支持复用现有镜像启动、显式重建后启动、状态检查、仅关闭公网 Tunnel，以及安全停止全部 Compose 服务。全部停止使用 `docker compose stop`，保留容器和 SQL Server/Redis/RabbitMQ/Kafka/i18n 命名卷。
- 流程不会修改 Windows 睡眠、电源计划或计划任务。电脑睡眠、关机、Docker Desktop 停止或网络中断时，`cp6.uk` 暂时不可访问是预期行为；恢复后由用户手动启动/检查。
- 2026-08-24 只读实机验收确认 7 个服务运行，DB/Redis/MQ/Kafka 健康；本机 Web/API 与公网 Web/API 四个地址均为 HTTP 200。为保护正在使用的环境，本任务没有执行重启、重建或停止。
- Cloudflare Workers 中名为 `estimate` 的 Git 集成仍是独立外部清理项，不参与本机 Docker + Tunnel 运行链，也不因本流程交付而视为已修复。

## CRM V1 产品需求草案（2026-08-24）

- 新增 `docs/crm/CRM-V1-PRD.md` v0.1，状态为 `Draft for Product Review`。文档以 2026-08-14 私有 `GTX537/CP6.CRM` Frozen SaaS V1 为长期产品目标，并把已批准的 Lead Pilot C 分栏工作台定义为第一可见交付。
- PRD 已统一 Website/Manual/Import Intake、Lead/Opportunity/PublicSubmission/IntegrationProcess 状态、22 个权限动作、DataScope/PII/Entitlement、幂等/ETag/412、CP6 ERP 与 ExternalEvidence 两种成交权威、Site/CMS、Portal、移动端、KPI、采用门禁和未来升级接口。
- 当前代码事实不变：`main` 只有 20 表 Foundation、状态机、迁移、6 个禁用菜单和 22 个动作；没有 CRM Controller、独立 API、Next.js/React Native 客户端或可用 CRM 页面。私有 CRM 仓当前为 docs-only。
- 本条记录 2026-08-24 的草案事实；Public Contract Sync 后续已由 PR #8 完成。PRD 产品批准仍不把 M0 从 No-Go 改为 Go，只有 M0 Go 后才可按 Lead Pilot 切片拆实施票。

## Space GA 退出码假红修复（2026-08-24）

- Attestation、Pilot、Golden CAD、Kickoff 和 Development Personnel Seed 五个负向测试套件都会在末个预期失败的 validator 子进程后把 `$LASTEXITCODE=1` 留在调用方全局作用域；GitHub Actions 的 `pwsh` 包装器因此把断言全绿的测试误判为失败。
- 五个测试辅助函数现在都只在完成正/负向退出码与错误码断言后清除已消费的子进程状态，并在各自套件汇总前断言全局退出码必须为 `0`。没有放宽任何 GA 证据规则、错误码或 `NoGo` 条件。
- 本地按 Actions 顺序运行全部 Space GA 门禁：当前状态校验通过，Attestation 36/36、Pilot 21/21、Golden CAD 31/31、Kickoff 28/28、Development Personnel Seed 8/8；全部独立 `pwsh` 进程退出码为 `0`。

## CP6 SaaS V1 公开工程契约同步完成（2026-08-26）

- 私有 `GTX537/CP6.CRM` 的 Frozen 产品 merge commit 为 `07a7bb0b50f33b0cb70c18c14f83be77c725626d`，产品摘要为 `e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b`；R00 `CP6-SAAS-R00` 摘要为 `64a53dd895aedc20a51288ad0ffdb69f60ddc7c22012c1df83984efba5adbc03`。
- 公开 `CP6-SAAS-V1-PUBLIC-CONTRACT` 的规范化摘要固定为 `8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9`；ProgramOwner 已在 PR #8 对该精确摘要批准，append-only 记录绑定评论、证据 commit/blob 和私有源摘要，公开合同与 R00 镜像同步为 Complete。
- 公开文件只同步四仓职责、领域/API/事件、安全、发布与 M0 开工规则；价格、支付供应商、商业 cohort、私有个人审批身份和内部风险记录不公开。M0 保持 No-Go；DEC-001、DEC-003 至 DEC-009、专业证据、Critical/High、私有仓库分支保护和必需检查仍不可豁免。

## 仓库分支整顿与当前开发基线（2026-08-24）

- 分支整顿当时的集成基线为 `main@0a14581f87ac1955678bdb664911183fc5a2a2a1`；根工作区已从落后 172 个提交且含 43 个 tracked 修改/4 个 untracked 文件的旧 WIP 分支切回干净主线。整顿前完整引用、脏 worktree patch、原始未跟踪文件与校验清单保存在本机 `D:\CP6-archives\2026-08-24-branch-consolidation`，不得在完成独立备份前删除。
- 远端 72 个旧非 `main` 分支中，61 个已被 `main` 包含、9 个经内容审计后只需归档，均已删除远端引用；陈旧 PR #3 已关闭。两个 CRM 草稿分支已合并当前 `main` 后继续保留为 Draft PR #7/#8，不视为已获产品批准。
- 根目录混合 WIP 当时按职责拆成登录体验、日期时间规范化和 Kafka Dispose 三条续开发分支；三项现均已完成独立评审、自动化和合并流程，旧混合根分支只保留在整顿归档中，不再整体回灌主线。
- 仓库保持 GitHub Public 是本阶段的明确决定；未执行生产部署。`main` 已启用严格分支保护：管理员同样必须经 PR，分支必须基于最新主线并通过 `windows-and-web`、`android`、`sql-integration`，禁止 force-push/删除且要求解决会话。下一治理项是单独修复 Space GA 证据测试脚本“36/36 通过但进程退出 1”的 CI 假红。
- 完整分支处置、恢复入口、验证证据和续开发顺序见 `docs/project-memory/11-Branch-Consolidation-20260824.md`。

## Space Studio v1.3 核心实现（2026-08-12）

- Tenant 私有整仓模板已形成 Design V1 完整纵切：内部编辑者通过带 `Idempotency-Key` 的唯一创建接口提交类型化 Floor/Zone/Aisle/Rack 计划，服务端规范化父链、编码、尺寸、逐层规格、命令数与 100,000 库位上限，计算内容 SHA-256 并保存当前租户不可变 v1；System 模板仍为代码内置只读。`Space_WarehouseTemplate`/`Version` 使用复合租户外键、租户内编码唯一索引、查询过滤和同步/异步不可变护栏；目录、密封 Preview 和既有 Lease/Floor/Content Revision Apply 同时支持 System/Tenant，工作台标识作用域并拒绝把旧模板 Preview 用于新选择。全量 Space Integration 456/456（0 skipped）、Space Unit 549/549、CP6.Tests 2,934 passed / 19 项既有环境门禁 skipped、Web 884/884、Space Studio Playwright 26/26；OpenAPI/权限聚焦 96/96、EF/双 SDK drift、Vue TypeScript、生产构建和完整 solution Release 均通过。该纵切关闭 Tenant 模板持久化、版本不可变、目录、跨租户隔离和按层 Apply；面向仓库人员的模板制作表单、Blank/Published/System/Tenant 四模式统一创建向导及 Template 创建来源持久化仍待后续任务，LM-FR-001/WP1 保持 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 历史 CAD 审核结果目录已接入 Design V1 与 Space Studio：楼层级只读接口只枚举同 Version/Floor 的成功 DWG/DXF Parse Job，并把启动时冻结的 Base Content Revision/Hash 与当前 Draft 比较；只有当前 Revision、来源仍为 PreviewReady 且 PreviewSet Artifact 存在的候选可直接加载。旧 Revision 结果继续可见，但工作台只允许带原 Source 进入 CAD 起始向导重新解析，不能直接 Review/Apply；选择候选会先清理旧 CAD/Excel/Preflight/Match 路由与本地状态。只读用户可以查看目录，编辑用户才能触发重新解析，用户全程无需填写 SourceId/JobId 等内部标识。Space Integration 15/15、OpenAPI/权限 95/95、Web 882/882、Space Studio Playwright 26/26、Vue TypeScript、生产构建与双 SDK drift 通过。该纵切关闭“历史 CAD Source/Parse Job 可发现与显式重新关联”的仓库 UI 边界，不替代真实 Provider/文件/WMS/黄金集/Pilot 接受，WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 当前 CAD + Excel 工作会话已形成统一前端闭环：只有与当前 Floor/Draft Revision 一致的 CAD Review Workspace 才启用入口；用户上传 `.xlsx` 后，工作台等待受控来源扫描、选择服务器 Mapping Profile、自动轮询并展示行/列级预检，Blocking 失败关闭，显式确认后绑定 CAD Source/Parse Job、Floor 和 Content Revision 启动权威匹配，再进入既有 Lease/Revision/Artifact 保护的 Apply 与统一撤销/重做链。Excel Source/Preflight Job 写入 URL 可刷新恢复，匹配 Job 自动轮询；删除依赖来源会清理失效路由链。Web 878/878、Space Studio Playwright 25/25、Vue TypeScript 和生产构建通过。该纵切关闭当前工作会话的统一 Excel 上传 UI；历史 CAD 候选目录已由上一条后续纵切闭环。它们都不替代真实 DWG/DXF+Excel、Provider、WMS、黄金集和 Pilot 接受，WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- LM-FR-019/019A 的仓库主链已完成深审和加固：CAD Job 成功后仍自动从受控 Artifact 加载绑定 Base Content Revision/Hash 的只读 Workspace，前端现在对新增、修改、删除、冲突、低置信度、未识别六类变更显示独立计数和筛选，并拒绝汇总、选择状态或可 Apply 类型不自洽的响应；替换 Workspace 会清除旧选择。静态设计元素使用专用内部 CAD 命令入口，单次可原子确认最多 10,000 项，公开手工命令仍保持 100 项上限；101 项服务纵切证明只推进一次 Floor/Content Revision。Zone/Aisle/Rack 不伪装成通用元素，审核面板会把 `SPACE_CAD_REQUIRES_RULE_ONLY_REVIEW` 显式交接到既有 RuleOnly → Proposal Review → Atomic Apply 主链并预选当前 CAD 来源。Space Integration 15/15、Space Unit 546/546、CP6.Tests 2,933 passed / 19 environment-skipped、Web 873/873、OpenAPI 55/55、Space Studio Playwright 24/24、生产 Web 构建、完整 solution Release 0 warning / 0 error 和 SDK drift 均通过。LM-FR-019/019A 仓库实现闭环，但真实 DWG/DXF 三路径、Provider、黄金集与 Pilot 仍未接受，核心 GA 保持 72% / `NoGo`。
- LM-FR-010～011 的仓库主链已闭环：浏览器与服务端都显式接受 `.dwg`/`.dxf`，服务端以扩展名、声明 MIME 和真实文件签名三重校验后才进入隔离扫描；两种格式继续归一到同一 CAD IR/Preparation/Parse 合同。解析前，工作台现在直接展示自动建议单位、每来源单位对应毫米、原始 X/Y/宽高、自动换算毫米范围、比例/范围是否异常及稳定问题原因，并继续要求用户分别确认单位/坐标转换和映射语义；任一输入变化都会使确认失效。Space Unit 546/546、Web 869/869、Vue TypeScript、生产构建和完整 solution Release 0 warning / 0 error 通过；安装型 AutoCAD 2025 Core Console 真实 DWG 合同用例 1/1 通过，只计开发转换链。生产主备 Provider/Site 审批仍未关闭，核心 GA 保持 72% / `NoGo`。
- LM-FR-014～016 的 CAD 语义与质量诊断已完成仓库复核和补齐：既有解析器覆盖墙、柱、门、月台、区域、巷道和货架，并为每个自动提案保留 SourceRef、命中规则、几何规则、置信度和画布定位；未映射/规则冲突继续由 Mapping 诊断进入问题空间。零尺寸与无法闭合现在使用独立稳定代码，越界除了全图 Blocking 还逐对象保存 SourceRef，并通过 Preparation 合同与 CAD 起始向导直接展示；同目标 Polygon/Circle 的真实面积重叠会为双方生成可定位 Warning，边界接触、不同目标包含关系和无可靠面积的降级块不会误报。Space Unit 544/544、CAD Preparation/Parse/BuildScene/Excel 集成聚焦 37/37、CAD 实验工具常规门禁 39 passed / 1 个安装环境用例 skipped、OpenAPI 55/55、CAD 向导 4/4、CP6.Tests 2,933 和完整 solution Release 0 warning / 0 error 通过；配置安装环境后 AutoCAD Core Console 真实 DWG 用例另行 1/1、0 skipped。LM-FR-014～016 仓库实现闭环，真实黄金 CAD/主备 Provider/Pilot 未因此关闭，WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- LM-FR-013 的 CAD Mapping Profile 已形成完整租户权威：System Profile 继续只读；租户可在 CAD 起始向导中复制系统方案、结构化编辑最多 500 条图层/块规则、启停方案，并以 RowVersion + Idempotency-Key 追加不可变新版本。`Space_LayerMappingProfile`/`Version` 保存规范 JSON、Definition SHA-256、复制来源、创建审计和跨租户外键，版本更新/删除由 `SpaceContext` 同步与异步双路径拒绝；管理 API、Preparation Catalog、OpenAPI/双 SDK 与工作台使用同一版本。真 SQL Space Integration 453/453（0 skipped）、Space Unit 540/540、CP6.Tests 2,933、Web 866、生产构建、SDK/EF 漂移和完整 solution Release 0 warning / 0 error 通过。LM-FR-013 仓库实现闭环；真实 DWG/DXF/Provider/黄金集/Pilot 未因此关闭，WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- LM-FR-012 的 CAD 来源结构审核已接入起始向导：Design V1 Preparation Preview 在不暴露原始 CAD 字节、块引用明细或外部引用路径令牌的前提下返回完整图层/块清单，工作台可搜索并查看图层名称、颜色、线型、可见性、对象/支持/未支持计数，以及块定义、引用和属性引用计数。用户可逐图层选择沿用 Profile、忽略或覆盖目标，并显式调整几何规则和置信度；单位、坐标、Profile 或 Override 变化都会使旧 Preview 失效，必须重新由服务端密封后才能启动 Parse。LM-FR-012 仓库实现闭环；后续租户 Profile 权威见上一项。
- LM-FR-005 来源移除引用预检已交付：Design V1 在提交前分类展示活动阻断与历史保留引用，Apply 绑定双 Revision Fence、幂等键和 Serializable 事务；活动 Job/Generation、底图、当前设计对象或元数据会零写入阻断，终态 Job/工件/问题/标定/导入审计继续保留。确认只软删除来源，物理文件继续走 Retention/Tombstone 权威，工作台不级联删除。全量门禁为 Space Unit 540/540、Space Integration 真 SQL 447/447（0 skipped）、CP6.Tests 2,932、Web 862、OpenAPI/双 SDK/EF/生产构建及完整 solution Release 0 warning / 0 error。WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- Space Studio CAD 与 PDF/图片底图上传已显式消费服务端 `Reused`：只有后端按 SHA-256 确认复用时才提示用户已复用受控文件或当前来源，新文件仍显示普通成功提示；底图复用后继续走同一 Clean/Scanning/Rejected 与挂接链。客户端不计算权威哈希、不绕过隔离扫描。Excel 后端/SDK 的 Reused 合同继续由服务器决定；当时缺失的当前 CAD + Excel 上传向导及历史 CAD 候选目录均已由同日后续纵切补齐。WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- Space Studio 活动 Draft 摘要已补齐来源、创建者、创建/更新时间、状态和 Open Blocking 数量；Design V1 列表与详情按同一合同返回，Blank 与 PublishedVersion 来源由现有版本创建权威确定，创建者缺失时不伪造姓名。真 SQL 验证只统计 Open Blocking，不混入已解决问题或 Warning；OpenAPI、双 SDK 和前端同步。未来 System/Tenant Template 创建来源仍须随四模式向导持久化，创建者显示名解析也未在本卡猜测实现；LM-FR-001/WP1 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- System 整仓模板现在可在 Space Studio 按模板楼层原子写入既有 Draft Floor：服务端只接受内置不可变计划，绑定 Site、模板版本、Proposal Hash、页面租约、Floor/Content Revision 和幂等 CommandBatch，并把 Zone/Aisle/Rack/逐层规格/Location 与目标 Floor 边界放进同一 Serializable 事务。标准 F1 真库一次生成 3 区、10 巷道、250 货架和 5,000 库位；错误租约、过期 Proposal、Revision 冲突与命令失败均零写入，完成态重放不重复。工作台提供密封预览、楼层显式选择、数量确认、窄屏/只读保护和未知网络结果的同批安全重试。Space Unit 537/537、Space Integration 真库 443/443、CP6.Tests 2,925、Web 856/856、OpenAPI/SDK/EF/生产构建门禁通过。Tenant 私有模板与 Blank/Published/System/Tenant 四模式统一向导仍缺，LM-FR-001/WP1 保持 Partial/Pending；核心 GA 仍为 72% / `NoGo`。
- 平台整仓模板目录与实例化预览纵切已交付：Design V1 暴露内部可读的 System/Tenant 过滤合同，首份内置 System 模板固定 2 层、7 区、20 巷道、500 货架和 10,000 库位，并以稳定模板/版本/内容 SHA 和完整父级引用返回机器可读预览。工作台可浏览和预览，且持续标明 `writesDraft=false`；外部主体、旧版本和非法 scope 失败关闭。租户私有模板、Template → Draft Apply 与四模式统一向导仍缺，因此 LM-FR-001/WP1 保持 Partial/Pending，GA 保持 72% / `NoGo`。
- Design V1 Floor shell 与项目入口纵切已交付：Space 首页可按 Site 打开 Space Studio，读取活动 Draft 与设计楼层；没有 Draft 时显式创建 Blank，没有 Floor 时必须填写编码、名称、层级、标高和层高。新 GET/POST Floor 合同使用内部读/编辑权限、Version 级 SQL 锁、Expected Content Revision、Serializable 事务和 Idempotency-Key，创建后以服务端 LogicalId 进入既有租约工作台。真 SQL 聚焦 4/4、Space Unit 534/534、Space Integration 真库全量 441/441、CP6.Tests 2,923 通过、Web 全量 848/848、契约/SDK/EF/GA 证据门禁与生产构建通过；完整 solution Release 0 warning / 0 error。整仓 System/Tenant 模板及四模式统一向导仍缺，因此 LM-FR-001/WP1 保持 Partial/Pending，GA 保持 72% / `NoGo`。
- LM-FR-001 的空白版本初始化纵切已交付：Design V1 `CreateVersion` 现在支持 `Blank` 与既有 `PublishedVersion`；Blank 强制无 `BasedOnVersionId`、不继承线上内容、不移动 Published 指针，并以唯一活动 Draft 槽、初始化 Operation、完成态 Job/Attempt、SQL 事务和 Idempotency-Key 失败关闭。真 SQL 2/2、领域聚焦 7/7 通过。该版本纵切自身不创建 Floor；Floor 初始化/选择现由上一条独立纵切补齐，平台/租户整仓模板仍缺。LM-FR-001 与 WP1 均为 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-025～029 的仓库实现与自动化证据已闭环：保存后的 Design Scene 无需第二次建模即驱动 2D/3D；选择与逐楼层 3D 视角跨模式保留，未保存重画不再因切换 3D 被静默丢弃，3D 中禁止误提交且回到 2D 可继续；首次四步清单默认展开、可折叠重开并具有 44px 热区及非颜色状态；问题可按 Blocking/Warning/Info 筛选并定位选择；低于 1280px 保持只读 3D、版本和问题。WP4 仍为 Partial/Pending，WP5 仍为 Complete/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-021 的仓库实现已闭环：底图标定现在按 P1 原点、P2 比例点、独立验证点 V 的顺序选点，用户输入真实距离、P1 世界原点、旋转和 V 世界坐标，工作台确定性生成服务端实际接收的 P2 世界毫米坐标。预览展示比例、原点、旋转、验证误差及 `max(50mm, 实距×0.2%)` 阈值，误差超限时禁止保存；既有 Lease、Floor/Content Revision、幂等 CommandBatch 和可逆历史权威不变。LM-FR-021 只代表仓库能力完成，WP4 仍为 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-022 的仓库实现已闭环：构件库在既有 Zone/Aisle/Rack/Location Design Layout 权威链之外，补齐墙、柱、门、月台、托盘及输送线、AGV、叉车、工作台、电子秤、充电站六类静态设备。预设固定领域类型、默认尺寸、编码前缀和 `design` 属性，只表达静态几何/编码/自定义属性。创建复用租约、Floor/Content Revision、Content Hash 与幂等 CommandBatch，并进入公共撤销/重做历史；Playwright 验证托盘与六类设备的 2D/3D 同源清单。LM-FR-022 只代表仓库能力完成，WP4 仍为 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-020 的仓库实现已闭环：工作台“图层”模式现提供可访问的底图显示/隐藏、0～100% 透明度和锁定控件，直接驱动真实 Konva 栅格图层；锁定会阻止标定入口，新挂接自动解锁、标定保存后自动锁回。显示偏好按版本/楼层保存在当前浏览器标签页，兼容旧 floor view schema v1，损坏或越界状态失败关闭，不修改 Draft 或制造第二套设计权威。单测、类型检查和 Playwright 已覆盖实际画布变化及重载恢复。LM-FR-020 只代表仓库能力完成，WP4 仍为 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-024 的仓库实现已闭环：PDF/图片底图的挂接、同页替换、标定与显式移除现均受页面租约、Floor/Content Revision、数据库 UTC 和幂等 CommandBatch 保护，并以既有不可变 Command Record 密封前后态；工作台将它们接入与 CAD、Excel–CAD、普通编辑相同的撤销/重做栈。Undo/Redo 只提交原批次、方向和历史 Hash，由服务端复核当前底图/标定指针并生成新的不可变补偿批次。真 SQL 已覆盖挂接、标定、替换、移除、会话隔离、历史篡改和恢复旧标定指针；OpenAPI/双 SDK、Web 与 Playwright 同步。LM-FR-024 只代表仓库能力完成，WP4 仍为 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- Excel–CAD 权威确认现已接入统一撤销/重做：v2 Apply 结果从实际不可变 Command Record 密封历史 Hash/数量，工作台只保存公开历史引用；服务端 Undo/Redo 复核同一页面租约、双 Revision、内容 Hash、原 Apply 工件链、密封历史和当前 Rack/层/库位/绑定/属性/Source 状态，并以新不可变批次原子补偿。介入编辑、历史篡改或旧 v1 结果均失败关闭，OpenAPI/双 SDK、真 SQL和 Playwright 已覆盖。LM-FR-024 现只剩底图挂接/标定可逆合同；WP4 和 72% / `NoGo` 状态不变。
- 详细 Spec LM-FR-024 的 CAD 确认批次已接入统一撤销/重做：CAD Typed Changeset 显式 Apply 后，服务端以实际提交结果密封 Create/Delete/Modify 的补偿命令和修改前完整快照，幂等回放保持同一历史；工作台验证白名单与数量后写入既有历史栈，异常响应保护性切为只读。Excel–CAD 历史已由上一条后续纵切闭环，LM-FR-024 只剩底图挂接/标定可逆合同；WP4 继续 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-018 的人工校正保护已补齐：CAD 来源通用元素可在既有 Design V1 `UpdateProperties` 命令中原子锁定/解除锁定，持久保存单调校正版本、最后操作者和 UTC 时间；锁定后的人工编辑继续递增版本。重新解析命中锁定 SourceRef 时，修改/删除统一变为不可应用的 Blocking Conflict，审核空间可定位并展示版本；服务端 CAD Changeset Apply Fence 再次阻止任何锁定对象写入。迁移、版本克隆、OpenAPI/双 SDK、真 SQL、前端与 Playwright 已覆盖。LM-FR-018 仓库实现闭环；WP4 仍为 Partial/Pending，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-023 的“复制”已补齐：批量检查器可复制 1–100 个 Active 通用元素/货架并允许混合选择，显式确认后把 `CreateElement` 与 `GenerateRackArray` 放入同一 Design V1 原子命令批。通用元素分配新 LogicalId、保留几何/类型/父级/属性并清除唯一业务编码、业务链接和 CAD 来源；货架复制 Active 层与 Generated/Unbound 空编码库位并生成 Zone 内新编码。撤销/重做只 Delete/Restore 已分配身份。真 SQL 混合批、前端单测和 Playwright 已覆盖；LM-FR-023 的对齐、等距、复制、旋转、阵列仓库实现现已闭环。LM-FR-018 已由上条后续纵切关闭；WP4 仍为 Partial/Pending，下一步审计 LM-FR-024 统一撤销/重做及其余三路径细项，核心 GA 保持 72% / `NoGo`。
- 详细 Spec LM-FR-017 的 CAD 异常对象“画布重画”已完成可逆 Design V1 纵切：单个 Active 非资产通用元素可在 2D 画布采集 3–100 个世界毫米顶点，重复、零面积、自交和 Int32 越界在确认前失败关闭；本地绘制期间 Draft 零写入，显式确认后用同一 LogicalId 的 `UpdateProperties` 原子替换为规范多边形，保留类型、业务链接、属性和 CAD 来源。R/Enter/Backspace/Esc、未保存状态、2D/3D 同源、撤销/重做、真 SQL 审计和 Playwright 均已覆盖。LM-FR-017 五项仓库能力现已闭环；WP4 仍为 Partial/Pending，等待其余详细条目审计及真实多路径/Provider/WMS 接受证据，核心 GA 仍为 72% / `NoGo`。
- 详细 Spec LM-FR-017 的 CAD 异常对象“拆分”已完成可逆 Design V1 纵切：单个 Active 非资产组合可拆成 2–100 个独立元素，首部件保留当前 LogicalId，其余部件分配新 LogicalId并继承类型、父级、业务链接、设计属性和 CAD 来源。组合整体移动/旋转会按渲染器同一局部到世界变换展开；保存/撤销/重做分别使用 Update+Create、Update+Delete、Update+Restore，重做不重复创建。2D/3D 等价、真 SQL 原子与零写入、工作台确认和 Playwright 已覆盖；重画已由上条后续纵切关闭，WP4 继续 Partial，核心 GA 仍为 72% / `NoGo`。
- 详细 Spec LM-FR-017 的 CAD 异常对象“合并”已完成一条可逆 Design V1 纵切：2–20 个同类型、同父级、同业务链接和同设计属性的 Active 非资产元素可合并，保留首选 LogicalId，其余元素标记删除；`group` 组合几何逐部件保存来源、位置、旋转、尺寸和原始几何，并以 100 部件/8 层上限失败关闭。2D/3D 同源渲染、聚合选择边界、工作台确认、撤销/重做、真 SQL 原子批和 Playwright 已覆盖；拆分与重画已由后续纵切关闭，WP4 继续 Partial，核心 GA 仍为 72% / `NoGo`。
- 详细 Spec LM-FR-017 的 CAD 异常对象“改类型”已完成一条 Design V1 纵切：通用元素在同一 `UpdateProperties` 命令内改型，保留 LogicalId，并继续受 Lease、Floor/Content Revision、幂等与原子审计保护；资产实例禁止改型。工作台提供受支持类型选择，并以补偿命令支持撤销/重做。真 SQL 1/1、Space Unit 全量 526/526、OpenAPI 44/44、前端全量 780/780、类型检查和 Space Studio Playwright 全量 15/15 通过。该切片复核时把 WP4 从过宽的 Complete 校正为 Partial；合并、拆分与重画已由后续纵切关闭，核心 GA 仍为 72% / `NoGo`。
- 单人开发阶段已新增 `00001`～`00005` 五个 `DevelopmentSeed` 虚拟人员，覆盖本地 Product/Backend/Frontend3D/QA/WMS/Architecture/Security/DevOps 流程视角；专项机器门禁强制一个真实操作者、五个唯一编号、无生产访问且无正式签字资格。总 GA 与开工证据校验同时拒绝纯数字及开发/测试身份进入正式 Owner、接受人和签字人字段。该能力只解除开发测试角色不足，不证明 2+2+1 真实投入，不改变 72% / `NoGo`。
- 本机 AutoCAD 2025 Core Console 开发转换链已接入实验型 `ICadConverter`：原始 DWG 经只读哈希校验后只在 D 盘唯一 `attempts` 目录导出 ASCII DXF，再通过既有确定性 DXF→CAD IR 与共合同执行器；子进程不经 Shell、绑定精确文件版本、超时杀进程树并清理中间源。Activity Insights 长期锁定的 Autodesk 自身运行包与原始数据分离到 D 盘持久缓存，缓存内出现 DWG/DXF 即失败关闭。Autodesk Floor Plan 样例两次得到相同 CAD IR SHA，4,424 个实体中 4,422 个受支持，安装型测试 1/1 通过。该链未注册 Site Provider、未做网络隔离/客户批准/主备评分，样例不计黄金 CAD，WP3/WP7 和 72% / `NoGo` 均不变；GUI `acad.exe` 的本机签名为 `HashMismatch`，正式使用前仍需 Autodesk 修复或批准例外。
- GA 总索引已更新到 2026-08-15，仓库完成度审计登记为 WP0 实现证据，AutoCAD 开发转换报告登记为 WP3 实现证据；两者没有进入 `acceptedEvidence`。证据 JSON 兼容层修复 PowerShell 7.6 自动把 ISO 时间转换为 `DateTime` 后合法 UTC 证明被误拒的问题，PowerShell 5.1/7.6 仍执行同一严格字符串、UTC 和未来时间校验。详细 Spec 复核后 WP0/WP3/WP4 为 Partial，WP7/WP8 为 ExternalExecution，WP1/WP2/WP5/WP6 仅为仓库实现 Complete，所有接受状态继续 Pending。
- WP0 核心 GA 证据索引已建立：`docs/space/acceptance/v1.3-ga/ga-evidence-index.json` 固定 72%→100% 口径、五类外部输入、WP0–WP8 九个不可删除 Blocking Gate 和产品/QA/WMS/架构/安全五个实名签字角色；仓库实现、真实环境接受与正式签字分开记录。`tools/Test-SpaceGaEvidence.ps1` 校验结构、证据路径与派生状态，`-RequireGaReady` 当前按设计以退出码 2 失败。当前仍为 `NoGo`：5 项外部输入、9 个接受门禁和 5 个签字均 Pending，未填写虚构 Owner、Site、Provider 或证据。
- WP0 证据证明链已加固：Signed/Complete/Accepted 统一验证受控 URI、实际本地文件 SHA-256、非占位的实名字段与 ISO-8601 UTC 时间，拒绝越界/不存在/哈希不一致证据、不安全 URI、原始 DWG/DXF 仓库路径和角色/团队/占位人名；GitHub Actions 工作流覆盖 16 个正反向自测。该加固没有填写任何真实证据，不改变 72% / No-Go 状态。
- WP0 五类外部输入现已增加结构化开工 Manifest 与语义门禁：五角色实名/审批权、2 Backend + 2 Frontend3D + 1 QA 及共享角色、20 份授权 CAD 候选、至少两条不同 Provider Key 的 `ICadConverter` 审批链与隔离 Worker、Greenfield/Retrofit 双仓和覆盖完整 Pilot 的 CP6 WMS 窗口都必须在对应分区中成立。每个 Complete 输入还必须证明 Manifest 自身哈希、Owner 与索引一致；签字人登记逐角色回绑总索引。专项 26/26、总 GA 证明链 34/34。此项只关闭证据内容误报，未填写真实外部输入，当前仍为 5 Pending。
- M0 基线 PR #4 已验证并以 merge commit `9c320a74` 合入；WP1 已完成 Design V1 布局创建/修改/删除、工作台接入和批量编码，WP2 CAD 起始向导也已合入远端 `main`。WP3 当前已完成 Site 级 Provider 认证与主备路由的仓库基础，但真实 ODA/APS 适配器、隔离 Worker 注册和客户批准证据尚未完成。
- WP3 新增按 Tenant/Site 版本化、追加式的 CAD Provider 配置：每个配置最多一个 Primary 和一个 Backup，记录部署模式、数据边界、DWG/DXF 范围、有效期、审批证据引用及 Secret 引用；认证明细不可修改，历史配置只允许从 Current 变为 Superseded。独立 `space:model:provider:manage` 权限维护配置，`space:model:read` 只读能力接口不返回 Secret 内容。
- WP3 Provider 资格选择已按 ADR-0001 和冻结执行计划补齐服务端约束：新认证必须绑定四项硬门禁、80–100 总分、评分规则版本、黄金集 SHA、冻结环境 SHA 与资格证据引用；两条链必须共享同一评测基线，Primary 分数必须严格高于 Backup，低于 80、基线混用或并列均零写入。历史认证不会被迁移默认值冒充合格，缺少资格证据时能力接口和执行路由均失败关闭。该项只完成仓库规则，不代表真实 ODA/APS、Site 审批或黄金集评分已经取得。
- WP3 Provider 评分已新增 `CP6.Space.CadExperiment qualify-providers` 机器入口：严格校验六维权重、四项审批证据、Preflight/黄金集/冻结环境哈希、80 分门槛和唯一第一/第二名，只在完整 Pass 时输出受报告 SHA-256 绑定的一主一备认证输入；No-Go 时认证输入为空，且工具从不修改 Site。聚焦测试 34/34 通过；当前尚无真实候选评分和客户接受证据，WP3 与核心 GA 状态不变。
- WP3 Provider 版本身份已从评分证据贯通到执行链：Site 认证、运行时注册、只读能力、Preparation 输出和新 Parse payload v5 都绑定规范 `ProviderVersion`。认证/注册版本不一致时能力与路由失败关闭，Provider 输出版本漂移在 sealed Preparation 前拒绝，v5 Parse 只接受当前仍认证的 Key + Version；历史认证迁移为空版本并失去资格，不猜测回填。评分工具产生的认证输入携带原候选版本，CAD 向导显示主备版本。该项关闭仓库版本漏洞，不代表任何真实 Provider 版本已认证，WP3 仍为 Partial/Pending。
- WP3 `ICadConverter` 共合同执行器已补齐：所有正式调用入口必须经 `SpaceCadConverterContractRunner`，统一保护隔离 Source 只读/不转移所有权、流式 CAD IR 顺序与唯一性、汇总计数、单次完成，以及 Result 与 Sink 实际提交的 Source/Provider Version/Artifact SHA/Summary/Issues 绑定；开发转换入口已接入，适配器吞掉 Source/Sink 异常也会失败关闭。该项只关闭供应商无关执行协议，不代表 ODA、APS 或替代者已实现/认证，WP3 和 72% / No-Go 状态不变。
- WP3 Provider 认证真库门禁已在 SQL Server 17.0.4025.3 LocalDB 运行 3/3、0 skipped：覆盖并发 Replace 只允许一个成功、唯一 Current Revision、历史追加、认证证据不可变、幂等记录、三份迁移脚本重复执行，以及旧认证缺资格或 Provider Version 时能力与路由失败关闭。该项关闭仓库 SQL 自动化 skip，不代表真实 Provider、Site 审批、黄金 CAD 或冻结 Worker 已取得，WP3 仍为 Partial/Pending。
- WP3 Preparation → Parse 的确定性映射输入已补齐：Ready Preparation 保存服务器生成、规范 JSON、SHA-256 密封的 Mapping Replay Snapshot，绑定 Tenant/Source/不可变 Profile、Inventory/Structure/Preview Hash 与完整 Layer Overrides；历史 payload v4 首次携带该快照，当前 v5 在同一快照上再封存 Provider Version，启动服务和 Worker 分别在入队和 Provider 调用前验证。缺失/篡改当前快照或版本零 Job/零 Provider 调用，历史 v2–v4 显式兼容读取。该项只为真实适配器提供重放合同；真实 Provider 仍须加载冻结 Profile、执行覆盖重放和 `ValidateReplay`，因此 WP3 仍为 Partial/Pending。
- CAD Preparation/Parse 现在统一经 `SpaceCadProviderRouter`：只选择当前 Site 已认证、有效、格式和 Provider Version 均匹配且在当前部署注册的 Provider；Primary 仅在可重试资源故障时切到同配置 Backup，不会跨到未认证链，Preparation 已由 Backup 密封时也不会反向切回 Primary。Preparation 保存 Provider Key + Version，Parse payload v5 绑定 Provider Key、Version、Semantic Preview Hash 和 Mapping Replay Snapshot；旧 v2–v4 显式兼容，v1 继续拒绝。
- Space Studio CAD 起始向导先读取 Site CAD 能力，展示配置 Revision、主备链、认证版本和阻断码；没有有效运行链时禁止扫描轮询和 Preview。`CanPrepareCad` 只代表当前至少一条资格完整、已批准、认证/注册版本相同且运行可用的链可用于内部准备，`CadGaReady` 只有主备两条链资格完整、同评测基线、排名有效、认证有效、版本匹配、运行可用且同时覆盖 DWG/DXF 才成立。
- WP1 已形成独立 Design V1 `layout-commands` 原子写链：在不写入通用 `Space_Element` 的前提下，按租约、Floor Revision、Content Revision 和命令幂等 fence 创建、修改、删除 Zone、Aisle、Rack，并由 Rack 逐层规格确定性协调 RackLevel/Location；Space Studio“构件”上下文已接入三类创建表单、画布坐标、逐层规格和库位数预览，右侧属性面板可修改三类布局对象并显式确认级联删除。货架修改保留既有层/库位 LogicalId、编码与绑定，新增库位保持未编码。
- WP1 批量编码任务卡已实现 Design V1 `location-codes:preview` → `location-codes:apply`：服务端按 Zone → Floor → Tenant 默认规则选择，只修改 Active/Unbound/Generated 库位，保护 WMS Bound、Adopted、Imported 和 Manual 编码；Preview 绑定 Floor/Content Revision、规则集和完整 Proposal Hash 且零写入，Apply 再在同一楼层锁与 Serializable 事务内复算，并受租约、双 Revision、Proposal Hash 与命令幂等保护。Space Studio 批量域可选择填空/重建与库区范围，展示修改/保持/保护项，显式勾选后才写当前 Draft；失败包保留原幂等标识，Published/WMS 不被直接修改。
- 低成本 3D 建模 Spec 已在完整保留 v1.2 详细正文的基础上增量修订为 v1.3；RFC-003 明确为“产品决定已冻结、跨职能批准 Pending”，外部 AI 独立 Beta、Viewer 性能门槛收紧、Supplier 不参加现场 UAT，`DesignUnderlayView` 成为单一页面权威。
- Space Studio 已形成冻结四栏壳层，包含 44px 标题栏、60px 命令栏、52+244px 左侧模式/上下文、主 2D/3D 画布、324px 属性/批量/问题检查器和 30px 状态栏；小于 1280px 自动只读。
- WP5 仓库内可达性切片已闭环：检查器采用标准 `tablist/tab/tabpanel` 与方向键、Home/End 漫游焦点；工作台补齐工具状态、快捷键声明、全局清晰焦点环、状态播报和 2D 画布键盘焦点。`G` 可按 Blocking → Warning → Info 循环定位 Open 问题，窄屏定位仍保持只读 3D；右侧 CAD/Excel/属性/WMS 面板统一消费 `--space-studio-*` token，正文/问题说明不低于 16px、元数据 13–14px，主要控件热区不低于 44px。该项是自动化可验证的工作台行为完成，不替代 WCAG 独立审计、真实键盘/辅助技术验收或人工 UX 签字。
- WP5 2D/3D 交互切片已闭环：草稿 3D 通过参数化场景映射做对象拾取，Element/Zone/Aisle/Rack 回到同一 Design LogicalId，RackLevel 归一到所属 Rack；3D 点选与既有 2D/问题选择共用 `selectObjects`，Ctrl/Command 支持切换选择，Orbit 拖动不会误触。2D 平移/缩放、当前投影模式及 3D camera/target 按 Version+Floor 保存在当前标签页，切层或刷新后恢复；损坏、越界或旧 schema 状态会被拒绝，新楼层无保存状态时重新 framing。该项不替代生产 Published-only Viewer 边界核验或独立 UX 验收。
- WP1 2D 画布拖动精调已接入现有 Design V1 命令权威：Rack/Element 按世界毫米直接拖动，已选对象保持多选整体移动，Zone/Aisle 继续走 Layout 属性合同；落点和撤销均通过带 Lease、Floor Revision、Content Revision/Hash 和幂等批次的 `MoveObject` 写入，失败重绘权威场景。前端全量 780/780、Space Studio E2E 14/14、拖动重复 5/5、type-check 和 production build 通过；独立 UX/Pilot 仍为接受门禁。
- WP7 正式黄金 CAD 已建立结构化证据门禁：恰好 20 份唯一真实授权样本、10/5/5、L1～L5≥4、DWG+DXF、双标注/独立 QA 仲裁、冻结版本/Worker/Source Set、Primary/Backup 两份 release-eligible 报告、主 Provider 严格高分、两者使用同一 50 MiB 标准 CAD、Overall/Out-of-sample 五项质量指标、Holdout 零未报告 Blocking 以及 50 MiB/Ready P95 都进入同一 Manifest。WP7 Accepted 前还必须完成授权 CAD、Provider/Worker 两类外部输入和 WP3 验收，并由 Gate 证明 Manifest 自身哈希；模板/fixture/原始 CAD 路径均拒绝。专项 31/31、当前组合证明链 34/34。该项只关闭证据格式和误报，真实 20 CAD 与 Provider 运行仍未发生，WP7 保持 `ExternalExecution/Pending`。
- WP8 双仓 Pilot 已建立结构化证据门禁：最终包必须恰好包含 Greenfield/Retrofit 两个不透明 Site、各至少连续 14 个日历日并逐日列出不可重复/不缺日的记录，零 S1/S2、每个 S3 都有可用绕行并在签字前关闭、2D/3D/对象清单与 WMS 100% 一致、15/240 分钟恢复、旧 Published 持续服务、Published-only Viewer、无长期双写，以及客户仓库代表和实施负责人的独立实名确认。未来日期或 Pilot 结束前预签的证据也会失败。通用 GA 校验器只在五个内部签字人均 Signed 且每份签字证明的接受人与登记姓名一致后允许 WP8 Accepted，并且只接受被自身哈希证明、通过专项校验的真实 Manifest；模板、Manifest 或嵌套证明中的测试 fixture 均拒绝。专项 21/21、组合证明链 23/23。该项只关闭证据格式和误报漏洞，两仓 Pilot 仍未发生，WP8 保持 `ExternalExecution/Pending`。
- WP5 Viewer 当前仓库性能门禁已在代码提交 `bd206ff8` 的干净跟踪工作区正式复验：Windows 11 / Chrome 151 / Intel Iris Xe 31.0.101.4502 / D3D11 / WebGL2 / 1920×1080，确定性 500 货架/10,000 库位场景执行 1 次单独预热和 30 次冷 Context。30/30 运行、3,000/3,000 次实际拾取命中，0 console errors、0 软件渲染；可交互 P95 62.3ms、帧时间 P95 8.2ms、拾取 P95 0.3ms、10,000 库位着色并提交渲染 P95 2.0ms、36 draw calls，全部通过冻结门槛。原始样本、环境、输入文件哈希和截图见 `docs/space/reports/2026-08-14-space-viewer-v13-ga.md`；该证据不替代生产 Published-only Viewer、独立 QA/UX 或 Pilot。
- WP5 生产 Viewer 的仓库权威边界已闭环：新增 Site 级 `published-scene` 只读合同，只从 `CurrentPublishedVersionId` 解析 Production/Published Design Revision，且明确排除 runtime overlay；单层 Viewer、跨层 Viewer、Control Tower 与楼层列表均不再读取可变旧 `/space/floor/*/scene`/floor API。客户端再次绑定 Site/PublishedVersion/状态并按 RackLevel 确定性投影可拾取 Location，Draft 注入、身份漂移、缺失几何或跨层任一楼层失败均整体失败关闭，不留下半仓画面。OpenAPI、双 SDK、权限和结构守卫已同步；Published-only 真库用例已在 SQL Server LocalDB 实际通过，并证明楼层必须在版本发布前封存、发布后不可追加。生产等价部署、真实发布数据和独立 QA 复核仍是门禁。
- WP4 仓库内路径闭环新增底图标定与 Excel–CAD 可发现性：图片上传并挂接后，上下文面板明确提供“标定底图/重新标定底图”入口；带 `matchJobId` 进入工作台会自动切到问题域并加载权威匹配，定位 Rack 后仍须显式确认才 Apply。浏览器自动化分别覆盖图片上传→标定、Excel–CAD 审核→确认，以及 DWG、DXF 两种格式各自经服务器 Preview 和双确认启动解析。上述证据使用受控 API fixture，只证明 UI/合同路径，不替代真实 Provider、真实文件、Excel 数据与 CP6 WMS 环境闭环。
- WP6 发布 Warning 认领已形成独立 fence：Publish Preview 返回与 ValidationRun 和排序后的 Warning Issue 集绑定的 SHA-256；存在 Warning 时，发布页要求独立逐项确认并把哈希带入 Publish Attempt。缺失确认稳定返回 422，集合或 Run 变化返回 409，历史重发遇到 Warning 会停在生成的 Ready 版本等待人工预览确认，不再静默继承通用审批。该项关闭仓库内合同/服务/UI 行为，不替代真实 CP6 WMS 故障恢复、对账、监控告警、运行手册或双仓 Pilot。
- WP6 发布恢复可观测性基础已交付：跨租户聚合器以不可变 Publish Audit 时间为主，按固定 `waiting_retry`、`manual_intervention`、`reconciliation_required` 状态输出活动数量、最老等待时长、SLO 超时数量和目标秒数，不暴露 Tenant/Site/Version/Attempt；Prometheus 规则覆盖 15 分钟自动恢复、4 小时人工恢复/对账及指标缺失，运行手册固定旧 Published 连续服务和正式 Retry/Reconcile 路径。仓库规则和 Mock 自动化不替代真实告警加载/通知或 SQL/WMS 演练。
- WP6 发布恢复指标已在本机真实 SQL Server LocalDB 暴露并修复 EF 翻译缺口：原复合键 GroupJoin 在 WMS 超时进入 `WaitingRetry` 后无法生成 SQL，现改为显式 TenantId + AttemptId + AttemptStatus 相关子查询；恢复指标单测 6/6、发布编排真库 3/3 通过，超时期间旧 Published 保持且重试完成。该证据只证明真实 SQL 引擎查询与编排，不替代生产等价 SQL/WMS、告警通知和恢复时限接受。
- Space Integration 已在 SQL Server 17.0.4025.3 LocalDB 完整运行 426/426、0 failed、0 skipped。首次 424/426 暴露并关闭发布恢复指标 SQL 翻译与 Published Viewer 夹具先发布后写快照两项问题；最终复跑覆盖全部原环境门禁。该结果关闭本机真实 SQL 自动化 skip，不替代生产等价拓扑、真实 CP6 WMS/IdP/告警链、独立验收或 Pilot。
- CP6.Tests 中属于 Space/WMS 的额外 SQL Server 集合已在同一 LocalDB 独立运行 15/15、0 skipped：覆盖 Location 过滤唯一索引、两阶段换码、rowversion、Control Tower SQL 翻译、WMS Move/Replenish/Serial/LPN/Feature Flag 事务，以及 Integration Event UTC 回填的 Session applock。全套 CP6.Tests 启用 SQL 后另有两个 OA/PUR 共享隔离 Stage 门禁按设计拒绝普通临时库；它们不计入 Space/WMS 结果，也未被绕过。
- WP6 外部主体仓库矩阵已闭环：Design V1 授权阶段在权限属性、模型绑定和 Controller 之前拒绝 Customer/Supplier/3PL 访问 Draft、Source、Upload、Lease、Validate、Publish 和 AI；Published-only Portal 是唯一显式例外，反射守卫禁止扩张放行面。该边界不替代真实 IdP HTTP 负向、生产 SQL 跨租户、独立渗透测试或安全签字。
- 新增 Floor 编辑租约：数据库唯一槽、数据库 UTC、90 秒租期、30 秒前端续租、释放、同用户不同浏览器会话隔离、过期重申请、带双权限和原因的强制接管、不可变接管审计。编辑命令请求新增必填 `leaseId`，保存与租约写入共享 Floor applock，Revision/命令/幂等失败关闭。
- CAD Parse 成功后可由 Job/Source 路由自动读取并校验 PreviewSet SHA、Tenant/Source/Job/Floor 与解析启动时 BaseContentRevision；审核空间输出带基线与哈希的 typed 新增/修改/删除/冲突/低置信度/未识别变更集，经用户勾选后通过租约、Revision、ContentRevision 与幂等 fence 原子合入 Draft，stale 或工件链异常均零写入。CAD 起始向导现会轮询并同步安全扫描终态，要求用户显式确认当前楼层、来源单位、原点、旋转和服务器已知 Mapping Profile；服务端在受控 `ISpaceCadPreparationProvider` 边界内检查原始 CAD，生成坐标、库存、映射和语义预览，并保存绑定 Source SHA、Profile/Transform/Preview Hash、BaseContentRevision/Hash 与两小时有效期的 sealed Preparation。唯一解析启动接口新增必填 `preparationId`，伪 Profile、伪 Hash、过期或 Draft 已前进均拒绝且零 Job/Draft 写入。前端只有在两项显式确认后才能启动解析，本地 JSON 仅保留为高级回退。
- 空白画布/底图路径已可直接创建墙、柱、门、月台和静态设备；Zone/Aisle/Rack 可在工作台创建、选择、修改和显式级联删除，并使用同一租约、Revision、幂等和恢复状态。三类布局对象与 Element 一起进入共享参数化渲染计划，2D/3D 机器清单一致；库位批量编码已在同一批量检查器闭环。工作台“运行校验/校验并发布”会携带 Site/Version 进入正式发布控制面并自动发起 Validation，发布本身仍要求 Preview、审批确认和 `space:model:publish`，不会自动执行。
- 当前累计仓库门禁：完整 Release solution 0 warning / 0 error；Space Unit 531/531、CAD Converter/IR 聚焦 23/23、Provider 评分与 CAD Experiment 工具 34/34、Provider SQL 3/3、CP6.Tests 默认门禁 2,916 passed / 19 environment-gated skipped，另有 Space/WMS SQL 聚焦 15/15、Client 71/71、Space Integration 在 SQL Server LocalDB 426/426（0 skipped）、Web 794/794、Space Studio Playwright 17/17、Provider/Preparation/Parse 聚焦 33/33、OpenAPI 聚焦 44/44、Published Viewer 聚焦 12/12、Viewer 性能聚合单测 5/5、Viewer CPU 性能 1/1、Iris Xe 正式冷运行 30/30、发布管理聚焦 5/5、恢复指标/告警合同 6/6、外部主体矩阵 30/30、权限/OpenAPI/主体边界聚焦 112/112、GA 证据自测 36/36、Vue type-check、production build、OpenAPI/C#/TypeScript SDK drift、EF pending-model 和 diff whitespace 通过。LocalDB 真库证据不等于生产 SQL/WMS 接受；合并后仍执行聚焦冒烟。
- 本项完成的是仓库核心实现、自动化、Published-only 代码边界和当前 SHA 的 Viewer 硬件性能门禁，不代表 GA：真实主/备 DWG Provider、20 份授权黄金 CAD、生产等价部署与真实发布数据复核、独立 UX/辅助技术验收、两仓各 14 天 Pilot、WMS 恢复演练和五角色签字仍未完成。
## CRM V1 T1 对抗审阅收口（2026-08-13）

- T1 继续保持纯规范范围；没有创建 `CP6.CRM` 仓库、业务代码、数据库、云资源、迁移或部署。新仓只允许由满足 T1/M0/P01 前置的 `CRM01-S01` 创建。
- PublicSubmission 已形成独立 Intake 闭环：Quarantined 只能竞争性 release/reject/expiry，匿名化是 PII 生命周期；复用既有 22 个权限，并把首次响应 SLA 固定在原 ReceivedAt，Owner 30 分钟从 release 起算。
- 公开写入固定为浏览器到同源 BFF，再以服务 JWT、Dapr mTLS AppId、workload identity/NetworkPolicy 调 CRM；稳定 attempt 绑定 payload/browser，回执 Cookie 按最终编码 3800 bytes 控制。
- 生产连续性固定 Azure SQL Database GP vCore zone-redundant、GZRS/35-day PITR，以及受控 Azure Storage ZRS Emergency Intake；AZ 与逻辑损坏分别以 RPO/RTO 和季度恢复证据验收。
- System Release Manifest 新增 previous digest 与 DB/OpenAPI/Event/Dapr 兼容范围；默认系统整体回退，组件级例外必须有签名证据，Schema/业务数据永不回退。
- 本地工程/设计与合入前 fallback 复核修正了 Dapr 调用图、IntakeDeptId/PII 权限、实际 migration ID 和首次切换回退边界，修正后无剩余 Critical/High；正式 gstack 交互审阅因当前宿主缺少技能强制的 AskUserQuestion 接口未签发，不能冒充技能门禁。CRM Foundation 16/16、Markdown 相对链接和 `git diff --check` 通过。

## OpenAPI 原生客户端漂移门禁修复（2026-08-13）

- `client-contract` 的 OpenAPI 指纹曾同时在 `main@c68d9b53` 和 CRM PR #5 失败；两者均为期望 `D305...`、实际 `774F...`，确认不是 CRM 文档分支引入。
- 根因有两项：PowerShell 5.1/7 的 JSON 序列化仍会产生不同字节；旧门禁还把全部全局 schema 纳入原生客户端指纹，使 CRM、Space、Finance 等无关 API schema 也触发客户端漂移。
- 门禁现由 Node.js 对 JSON 键作稳定排序，只保留原生客户端路径及其递归可达 schema；PowerShell 入口、失败关闭语义和受控 `-Update` 流程保持不变。新增 4 个 Node 单测，Node 20/22 均通过，当前指纹为 `A49DB452941BF554AEAD66E35C41A7013CB280F7B5A918E41195C4C0FF44A637`。
- 本地完整门禁通过：OpenAPI live check、CP6.Tests 2859/2859、Client 71/71、Web 719/719、类型检查、生产构建与 R2 source gate；19 个 SQL/环境门禁保持基线 skip。本任务不改 API、客户端行为、数据库、部署或生产资源。

## CRM V1 规范批准基线（2026-08-12）

- 规范任务从 `main == origin/main == c68d9b53b4cf3adb5925b8258c36969fdebda753` 创建独立 `codex/crm-v1-spec-approval-20260812` 分支；只修订 CRM 规范与项目记忆，不修改旧根工作区、业务代码、仓库、云资源、数据或部署。
- `CRM-PRODUCT-FRAMEWORK.md` 与 `CRM-V1-EXECUTABLE-SPEC.md` 已升级为 Approved implementation-planning baseline。审阅证据为工程/设计计划 `C8574D3BE11C5492C2CFFA8797917FE4898328E16B25A832267714C719701A08`、QA 计划 `1A6995F45DAD2CD4DD511B7D6CF2E5FA760123C63DD597D1E0F5975D91C5F281`、采用/产品设计 `C60FA78E3F876D0682CB39814EEB9383FBAEC17D1E285DC59D2BB9256C322DF7`。
- 本轮冻结 Lead 创建/Assignment/Activity/Merge 的幂等与并发、412 保文/差异/显式重试、4 个租户业务小时 SLA、C 分栏 Pilot、公开站点 IA/视觉/受控 CMS、加密有界回执 Cookie、真实隔离 ERP UAT，以及 Pilot/CRM12 分层性能门禁。
- Observation、Pilot UAT、Lead Adoption 和 Full Journey Adoption 都是不可豁免硬门禁；整改窗口与重新立项/终止条件保留在私有 Adoption Manifest。CRM V1 唯一 Registry/候选权威固定为 GHCR/GitHub R2，Azure 只能做 CI、DEV 学习、影子验证或消费同一 digest；ACR 迁移独立立项。
- 该状态只表示 T1 规范批准，不表示 CRM 端到端实现完成。下一张票据是 M0/R00 DevOps ADR；Sponsor、Product/Sales Operations/Security/ERP/Data/SRE/Release Owner、Pilot cohort 和 Observation 证据缺失时自动 No-Go。
- 最新基线复核确认 20 个 CRM DbSet、无 CRM Controller/前端路由、JWT 仍为 HMAC SHA-256 且旧租户上下文仍回退 A1；`dotnet test CP6.Tests/CP6.Tests.csproj -c Release --filter "FullyQualifiedName~CP6.Tests.Crm" --nologo` 为 16/16 passed、0 failed、0 skipped。Markdown 相对链接和 `git diff --check` 通过。

## CRM 产品框架与三仓可执行 Spec（2026-08-11）

- 已从 `main == origin/main == f149c75e` 的干净独立 worktree 核对 CRM Foundation：聚焦回归 16/16 通过。当前主线实际只有 20 张 CRM/CMS 表、状态机、迁移和菜单权限种子，没有 CRM Controller、应用服务或前端路由；现有 JWT 仍是 HS256、租户上下文仍有默认 A1，均不得复制进新服务。
- 新增 `docs/crm/CRM-PRODUCT-FRAMEWORK.md`，把 CRM 定位为包装/制造企业从获客到 ERP 订单的行业化售前工作台，冻结角色、22 个权限动作、获客渠道、端到端旅程、V1/VNext、信息架构、UX、指标和产品验收场景。
- 新增 `docs/crm/CRM-V1-EXECUTABLE-SPEC.md`，固化 `CP6`、`CP6.Platform`、`CP6.CRM` 三仓边界，以及 Dapr/Kafka/YARP、RS256/OIDC/JWKS、Next.js、独立 CRM 数据库、CloudEvents/JSON Schema、Outbox/Inbox、20 表一次性迁移和 build-once 发布方案。
- Spec 已覆盖领域/状态机、数据/API/事件、权限/PII/租户隔离、ERP 集成、SLO/威胁模型、测试/发布门禁、里程碑/任务依赖和 Definition of Done。该条记录的是 2026-08-11 初稿状态；2026-08-12 已完成上方规划批准，但 named 开工/生产 Owner 审批仍待完成；未创建新仓库、未改业务代码、未迁移数据或部署。
- CRM 实施必须按 Spec 的 Platform P01–P10、CP6 C01–C04、CRM01–CRM12 依赖图拆分小分支。2026-08-12 已固定 CRM V1 使用 GHCR/R2；R00 只负责在 ADR 中记录该权威、候选清单与 Azure 非权威边界，仍是 P09/P10/CRM12 的硬前置。

## Azure DevOps CI/CD 项目记忆（2026-08-11）

- 已交付本机学习环境的 `azure-pipelines-dev.yml`：仅由 `GTX537.CP6` 在 `main` 成功后的 pipeline completion 触发，在 `CP6-Deploy/LAPTOP-3QQ44FJS` 上核对完整 Git SHA，构建一次 commit-addressed API/Web 镜像，再用 `cp6-dev` deployment job 执行 db-init、服务启动、健康/发布身份验证和非敏感证据归档。
- Azure Variable Group `cp6-dev-secrets` 已由 2026-08-11 外部截图确认存在，四项 DEV SQL/RabbitMQ/JWT 变量均为锁定 Secret。部署脚本新增进程环境 Secret 模式，同时保留人工 DPAPI 模式；只有 deployment task 接收 Secret，RabbitMQ 使用独立 `cp6-dev_rabbitmq-data-azure` volume，不覆盖原人工 Lab 数据。
- 上述段落记录仓库配置交付时的边界；同日后续已创建 `CP6 DEV CD`，完成 `CP6-Deploy`、Variable Group、`cp6-dev` 定向授权，并由 Run #95 取得首次成功 Run、Environment deployment history 和 Artifact 证据。该本机镜像缓存方案仍不适用于 UAT/PROD-LAB，也不关闭 Registry/发布权威门禁。
- Azure `CP6-Deploy` 专用 Pool 已建立；Agent `LAPTOP-3QQ44FJS` 以非管理员本机账号 `cp6_deploy_agent` 作为延迟自动启动 Windows 服务运行，Azure 截图显示版本 `5.277.0`、Online/Idle。通用 CI Agent `CP6-Windows` 仍留在 `Default` Pool。
- 手工、无 Secret、无 Checkout 的 `azure-pipelines-deploy-agent-readiness.yml` 已在 Azure Build ID `10`（Run `20260811.1`）成功运行；截图与 Worker 日志确认专用 Job 身份、非管理员边界、Docker Desktop Linux engine、Compose 和 `KOUSQLSERVER` TCP 门禁通过。Pipeline 后续已重命名为 `CP6 Deploy Agent Readiness`，并由 Build ID `89` 补齐 `sqlcmd` 与备份目录服务身份验收。
- 已交付本机 `cp6-dev`、`cp6-uat`、`cp6-prod-lab` 三套隔离 Compose project；每套包含 Redis、RabbitMQ、Kafka、API、Web，分别使用 `CP6_DEV`、`CP6_UAT`、`CP6_PROD_LAB` 和独立 migrator/runtime SQL 登录。三套环境均为 5/5 容器健康，live/ready Healthy，API/Web 版本与 Git SHA 一致。
- 新增 `deploy/lab/` 与 `Invoke-Cp6LabEnvironment.ps1`，从现有 SQL DPAPI note 临时渲染最小权限 Secret；RabbitMQ/JWT Lab 密钥保存在额外 DPAPI vault，明文临时文件在每次命令结束后删除。`KOUSQLSERVER` TCP `50286` 由工具自动发现，不写入 Git。
- 修复 API Dockerfile 未在 restore 层复制 Space 项目文件，以及 Web Docker 构建上下文无法读取仓库级 TypeScript SDK 的缺陷；Lab、根 Compose 与 R2 candidate 现统一使用可复现的构建边界。
- 2026-08-11 用户提供的 Azure DevOps 列表截图已确认 `cp6-dev`、`cp6-uat`、`cp6-prod-lab` 三个逻辑 Environment 存在，且当时均为 `Never deployed`。2026-08-25 已补齐 DEV 的定向 Pipeline permissions、Exclusive lock 与首次 deployment history；UAT/PROD-LAB 仍未部署，详见 `docs/devops/AZURE-ENVIRONMENTS-SETUP.md`。
- 新增 `docs/devops/` 项目级文档入口，整理当前 Azure CI、目标 Release/CD、Build once、环境策略、发布步骤和分阶段路线；`AGENTS.md` 与根 README 已接入，Codex 后续无需从聊天记录猜测上下文。
- 当前仓库事实是：`azure-pipelines.yml` 在 `main` 提交上运行 `Default` self-hosted pool，完成 .NET 8/Node 22 的后端/客户端测试和 Web 类型/单测/构建；`pr: none`。另有 DEV 学习链在部署 Agent 本机 Build/Deploy，但尚无 Azure Registry、不可变候选或生产环境部署。
- 现有 `.github/workflows/r2-*` 已实现更完整的受保护版本、GHCR 镜像、SBOM/漏洞扫描、签名、不可变证据和 digest 部署，因此在 Azure 门禁等价并显式切换前继续作为生产发布权威。
- 聊天规划中的 ACR 被记录为候选目标而非当前事实。当前只新增本机 DEV 学习流水线，没有创建 Registry/云资源或执行生产部署；CRM V1 已在 2026-08-12 固定沿用 GHCR/R2，R00 记录候选清单、Azure 非权威影子期和回退。ACR 或其他产品的长期 Registry 迁移另行决策。

## CRM V1 Foundation（2026-08-10）

- 已确认 CRM V1 产品边界并形成 `docs/crm/CRM-V1-SPEC.md`：官网/人工线索、线索池、负责人和协作人、活动、企业/联系人、商机、报价接受、ERP 订单赢单，以及独立轻量营销官网。
- Foundation 已落地 20 张 CRM/CMS 实体表和 EF 迁移，覆盖租户隔离、租户内唯一约束、公开路由先解析租户、PII 擦除标记、聚合外键与固定状态机。Won 只有在订单已创建时才合法，Accepted 要求已接受报价。
- 已登记 6 个菜单节点和 22 个动作，所有租户管理员获得权限；在 Vue 页面交付前菜单保持禁用，避免 admin 看到不可用入口。本里程碑是基础设施完成，不代表 CRM 端到端功能已经完成。
- 下一阶段为 Intake：人工/公开线索接入、线索池、分配与协作、SLA、重复候选和活动时间线 API；其后依次交付转化/ERP、前端体验、营销官网和运营任务。

## Space 单格货位码 Zone 级 rackSeq（2026-08-10）

- `CodeEngineService.GenSingleAsync` 已从目标 Zone 加载完整货架集合，并与批量 `GenerateAsync` 复用同一套 `(X, Y, Id)` 确定性排序；非首架单格生成不再被错误简化为序号 `1`。
- 相同几何坐标时以货架 `Id` 作稳定兜底。回归用例证明第二货架的单格编码与整层批量重建一致，且不会和首架编码重复。
- 本切片没有改变规则模型、API、数据库、迁移或前端。门禁为 CodeEngine/LocationPublish 聚焦 55/55、CP6.Tests 2843 passed / 19 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error；新增排序路径覆盖率审计 8/8，任务 diff 与新增行格式检查通过。

## FIN BudgetLine 版本级并发控制（2026-08-10）

- `BudgetVersion.RowVersion` 已经是预算行聚合并发边界。新增、编辑、删除和 Excel 确认导入都从客户端接收版本令牌，每次行写入都会推进该令牌；因此两个用户即使修改不同预算桶，旧快照也会统一失败为 `E-A5-CONCURRENCY-001`。
- API 对缺失/非法令牌失败关闭；前端在新增、行内编辑、删除、确认导入后同时刷新版本和行数据。单行的行头/12 期明细与 Excel 整批确认分别在单一事务中持久化。
- 本机 `KOUSQLSERVER` 原生 `rowversion` 证明 1/1，覆盖不同行的第二写者拒绝、刷新重试和陈旧删除回滚。其余门禁为 FIN 303 passed / 1 个既存 SQLite 限制项 skipped、前端令牌合同 3/3、Vue type-check、WebApi Release 0 warning / 0 error。

## PLAN/PUB Attachment 宿主业务权限补强（2026-08-10）

- Attachment 保持横切组件架构，不新增没有页面可锚的 `pub-attachment` 暗菜单；`Attachment:EnforceBizPermission` 缺省改为 true，显式 false 只保留为受控兼容开关。
- list/upload/download/preview/delete/rebind 六个入口统一按请求或持久化 `BizType` 回查宿主菜单。下载/预览在授权后才打开物理流，删除在授权后才调用服务；rebind 还要求当前用户是 draft token 下全部附件的上传人，并拥有全部宿主菜单。
- `PubUpload` 新增必填 `writePermission` 宿主 action key；无写权限时隐藏上传与删除，但保留下载/预览。前端只是 UX，后端宿主菜单回查仍是安全边界。
- 门禁：Attachment/PLAN-PUB 聚焦 21/21、OpenAPI 30/30、CP6.Tests 2841 passed / 18 environment-gated skipped / 0 failed、PubUpload 3/3、前端全量 716/716、Vue type-check 与 production build 通过、WebApi Release 0 warning / 0 error。

## 分支优先规范与本地配置优先级（2026-08-09）

- `e4e33364` 已把仓库级“分支优先、验证后合并”规范合入并推送 `main`；`AGENTS.md` 和 `DEVELOPMENT-GUIDE.md` 约束每个任务使用独立分支/必要时使用 worktree、只暂存任务文件、必需门禁通过后再合并与推送。
- 配置任务提交 `e3bf2420` 把 `appsettings.Local.json` 插入逻辑从 `Program.cs` 提取为可测试组件，并只匹配 `Prefix: null` 的无前缀环境变量源；`DOTNET_`/`ASPNETCORE_` 主机源不会被误认，环境变量与其后的命令行源继续覆盖本地 JSON。
- `.claude/settings.local.json` 已作为机器本地文件忽略；任务没有提交 `localhost\\KOUSQLSERVER` 等机器专属 `launchSettings` 配置。
- 验证：配置聚焦 4/4、OpenAPI 契约 30/30、CP6.Tests 2832 passed / 18 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error、C# whitespace 与 `git diff --check` 通过。

## WF 通知定向推送与遗留广播清理（2026-08-09）

- 运行链路已收敛为事务内 outbox：`PersistentWfNotifier` 只根据用户偏好写通知意图，不再持有数据库、邮件或 SignalR 直发依赖；提交后由 `WfNotificationDispatchWorker` 统一派送。
- 实时通知使用 `Clients.User(row.UserId.ToString())`，目标值与 JWT `ClaimTypes.NameIdentifier` 的用户 GUID 一致；`NotifyHub` 继续要求认证。未注册的 `SignalRWfNotifier` 与四段永远不可达的 `Clients.All` 回退代码已删除，避免旧广播路径被重新启用。
- 项目记忆中“WF 仍广播”的 API TODO、Todo 和 KnownIssue 已同步关闭。门禁为通知聚焦 13/13、CP6.Tests 2832 passed / 18 environment-gated skipped / 0 failed、WebApi Release 0 warning / 0 error。

## E13 无锁 Zone 父关系确定性推导（2026-08-09）

- 功能提交 `d19a5300` 基于 `main@6bbdd760` 交付 `warehouse-rule-only-v2`：Aisle/Rack 没有人工 `relations.zoneSourceKey` 锁时，只在同一权威 CAD Semantic Preview 中恰有一个 Zone Polygon 被证明完整包含其确定性几何时写入父关系；来源为 `DeterministicRule`，证据码为 `RULE:ZONE_GEOMETRY_CONTAINMENT_V1`。
- 零候选与多候选继续以 Blocking `SPACE_RULE_ONLY_PARENT_REQUIRED` 失败关闭，细节分别为 `no-containing-zone` / `ambiguous-containing-zones`；凹多边形路径按完整线段而非端点或 Bounds 判断。人工锁仍优先，冲突 AI Relation 被拒绝并保留融合问题，已解析关系进入环检测。
- `warehouse-rule-only-v1` 的冻结 Run 与恢复链保持旧行为，不被新算法静默改写；不同 SourceHash 的匹配、建议继承和人工确认仍是独立产品卡，不在本切片内。
- 门禁为融合聚焦 16/16、BuildScene 3/3、Space Unit 492/492、默认 Integration 288 passed / 95 SQL 环境门禁 skipped、完整 Release/AOT 0 warning / 0 error。无 Migration、HTTP/OpenAPI/SDK、前端、Provider、Usage、High Accept 或 Draft 写入变化。完整证据见 `docs/space/reports/e13-deterministic-zone-parent-inference.md`。

## `main` 同步、P2.5 受控整合与分支策略（2026-08-09）

- PR #2 已以 `8045d872` 把原 `integration/space-v1-20260730@f8c3bae8` 受保护合入 `main`；5 个预期冲突按权威边界人工解决，Docker 本地 HTTP Cookie 修复以 `0fc6f529` 等价纳入。
- P2.5 Analytics Control Tower 随后以 `030a97b9` 在当前 E10 Runtime/Viewer 真相源上选择性整合；保留独立控制塔、实时脏库位批处理、分析配置、定时 ABC 快照、容量发布和共享 ABC 分类器，不整段摘取历史分支。
- 历史迁移 `20260720035903` 未进入主线；替代迁移 `20260809092206_SpaceAnalyticsControlTowerCurrent` 基于当前 ModelSnapshot，仅新增两张分析表和三个索引。`b2a91680` 已对齐 Space 权限、菜单种子与配置文档。
- 本次文档同步以远端 `main@e4e33364` 为基线；该提交通过 `04eaf42d` 明确分支优先规则。后续开发、修复、重构和文档任务必须从最新 `main` 建独立分支；根工作区有未提交改动时使用独立 worktree。
- 合并前门禁包括 OA 2/2、前端 711、CP6.Tests 2816、Space Unit 487、默认 Integration 288、客户端 71 与心跳重复 50、EF drift、完整 Release/AOT 0 warning / 0 error。14 个 Core + 36 个 Space 幂等迁移已从 main 基线在 LocalDB 双执行，51083/51000/51020 失败关闭通过；正式生产仍需备份恢复副本演练。完整评估见 `docs/space/reports/2026-08-08-main-merge-readiness.md`。
- 主线代码不代表正式 CAD/Provider/S14/S15/S18/S19、R2 标签或生产部署获批；这些门禁继续独立失败关闭。

## E13 RackGenerationProfile 权威版本链（2026-08-08）

- 功能提交 `19d32650` 在基线 `d0d1c713` 上新增独立的 RackGenerationProfile 头/不可变版本存储、System/Tenant 可见性、Tenant-only 幂等创建、列表与精确版本读取；未复用渲染资产冒充生成规格。
- Generation Run 首建会冻结经 Active/Ready 与租户校验的精确版本；RuleOnly Worker 把该版本显式绑定到权威 Preview 的 Rack 项并确定性生成 RackLevel/Location。Web 提供显式可清空选择，不自动推断默认；未选择继续产生 Blocking。
- Migration `20260808164544`、幂等 SQL、三条 API、读写审计、Problem Details、OpenAPI 118 operations、C#/TypeScript SDK 已同步。真实 SQL 迁移/双执行/隔离/约束 1/1，前端聚焦 9/9、全量 711/711，Space Unit 487/487，默认 Integration 288 passed / 95 skipped，CP6.Tests 2816 passed / 17 skipped，完整 Release/AOT 0 warning / 0 error。
- 本切片不启用 Provider、网络、Secret、Usage、High Accept 或 Draft 自动写入。现有方案追加 v2、System 配置入口和完整管理 UI 尚未实现；下一项内部优先级转为无锁父关系确定性推导或不同 SourceHash 人工确认。完整证据见 `docs/space/reports/e13-rack-generation-profile-authority.md`。
- 功能/报告提交 `19d32650` / `6f12a19e` 已通过 no-ff 提交 `70dd670d` 进入远端集成分支；合并态前端 9/9、OpenAPI/权限 63/63、SDK drift 复验通过。祖先链确认后已删除本地/远端临时分支；清理 38 个可再生成目标和 29,418 个文件，释放 1,985,000,330 bytes（约 1.85 GiB），`main` 未修改。

## E13 Generation Run 建模 Web 入口（2026-08-08）

- 功能提交 `52bb3a29` 与验证报告提交 `282d4e54` 已通过 no-ff 提交 `2871df1b` 进入 `integration/space-v1-20260730`；编辑器可从已确认 DWG/DXF Preview 启动 RuleOnly Run，并在同一决策面板显示排队/进度、审核、Apply 和 Failed/Stale replacement 恢复，旧 recovery 合同已移除。
- Run 公开 DTO 补齐冻结 Source/Mapping/Rack Profile 标识，恢复请求重新读取当前 Draft RowVersion/ContentRevision，并发送 `If-Match`、旧 Run RowVersion、BasedOn 血缘和稳定 Idempotency-Key。Queued 等中间态不再提前读取尚未物化的 Review/Proposal。
- 门禁：前端聚焦 3 files / 11 tests、全量 133 files / 710 tests、type-check/production build、OpenAPI/审计 31/31、Space Unit 484/484、默认 Integration 283 passed / 94 skipped、CP6.Tests 2812 passed / 17 skipped、SDK strict/drift 与完整 solution Release/AOT 0 warning / 0 error。无 Migration、Provider、外部网络、Usage 或 Draft 自动写入。
- 下一项内部缺口优先为权威 RackGenerationProfile 版本存储/读取，随后可独立处理无锁父关系推导或不同 SourceHash 人工确认；外部 Provider 与正式 CAD/黄金集证据仍独立存在。完整边界见 `docs/space/reports/e13-generation-run-web-entry.md`。
- 合并态前端聚焦 11/11、OpenAPI 合同 29/29 与 SDK drift 通过；清理 38 个可重建目标和 29,416 个文件，释放 1,982,552,577 bytes（约 1.85 GiB）。远端祖先链确认后已删除本地/远端临时分支，`main` 未修改。

## E13 首次 Generation Run 创建入口（2026-08-08）

- 功能提交 `770bdc96`、验证报告提交 `bbcaf6fe` 已通过 no-ff 提交 `9d0971f4` 进入 `integration/space-v1-20260730`；合并后聚焦 9/9、OpenAPI/审计 31/31 复验通过，`main` 未修改。
- 在集成基线 `54f1cda7` 上以功能提交 `770bdc96` 把 `POST /versions/{versionId}/generation-runs` 收敛为统一 `CreateGenerationRun`：无 BasedOn 首次创建，有 BasedOn 保留 Failed/Stale replacement Run；必须提交 `If-Match`、ContentRevision 与 Idempotency-Key，继续使用 `space:model:generate-ai` 和显式审计。
- 首建只放行 RuleOnly；Tenant Disabled 的 AiAssisted 返回 `SPACE_AI_DISABLED`，已启用但生产 Provider-backed BuildScene 未配置时返回 `SPACE_AI_PROVIDER_UNAVAILABLE`。创建过程零 Provider、零 Usage、零 Draft 写入，不注册 Mock/Local Provider 冒充生产能力。
- Version/Source/Clean file/SourceHash/坐标/Floor/Mapping/PreviewSet 全部重新校验；业务键固定 Preview Artifact ID 与文件 SHA，Worker 和后续 replacement Run 精确消费同一 Preview。未经权威存储验证的 RackGenerationProfile GUID 拒绝进入 Run。
- 公开 create 幂等域覆盖首次与恢复：同键同请求重放、同键不同请求冲突、不同键相同业务输入复用 Current Run。OpenAPI 与 C#/TypeScript SDK 已同步，operation 总数仍为 115。
- 门禁：聚焦 9/9、OpenAPI/审计 31/31、Space Unit 484/484、默认 Integration 283 passed / 94 skipped、CP6.Tests 2812 passed / 17 skipped、C# SDK 与 TypeScript strict、SDK drift、完整 solution Release（含 Desktop/Android AOT）全部通过；最终构建 0 error / 7 条未改动测试文件既有 warning，C# SDK 为 0 warning / 0 error。无 Migration。
- 下一个内部缺口优先补权威 RackGenerationProfile 版本存储/读取，或把本 API 接入建模 Web UI；无锁父关系推导、不同 SourceHash 人工确认、外部 Provider 与正式 CAD/黄金集证据仍独立存在。完整边界见 `docs/space/reports/e13-generation-run-create-production.md`。
- 合并后清理当前隔离工作区 36 个可重建 `bin/obj` 目录、8,622 个文件，共 1,666,117,627 bytes（约 1.55 GiB）；临时功能分支待远端祖先链确认后删除。

## E13 纯规则 BuildScene 生产执行链接线（2026-08-08）

- 功能提交 `36cc0241`、验证报告提交 `89c6fb2a` 已通过 no-ff 提交 `9e7f7e0a` 进入 `integration/space-v1-20260730`；合并后规则/融合 21/21、BuildScene 与默认注册 3/3 复验通过，`main` 未修改。
- 在集成基线 `4d9bc3f6` 上以功能提交 `36cc0241` 实现生产默认 `SpaceBuildSceneJobStepExecutor`：`RuleOnly` recovery 现在能从私有、哈希和血缘校验通过的 CAD PreviewSet 走完 12 步 BuildScene，持久化只读 Proposal/Issue 并把 Run 推进到 AwaitingReview；Provider-backed 模式继续 `SPACE_AI_PROVIDER_UNAVAILABLE` 失败关闭。
- local-only 特征快照使用稳定 `Source SHA + SourceRef` SourceKey、Run 隔离 correlation 和零 Provider 调用；同 SourceHash 的已确认 E13-S10 locked facts 会从旧 Proposal SourceRef 重映射并参与融合，覆盖名称、allowlisted 类型属性和 Zone/Aisle/Wall 父关系。不同 SourceHash 仍不自动继承。
- Proposal/Issue 落库使用 Serializable 和逐字段重放校验；元素尺寸、Aisle/Rack 父关系和 RackProfile 缺失均 Blocking。执行器不写 Draft、不创建 AI Usage、不启用 High Accept；Draft 继续只能经人工 Decision 与原子 Apply 修改。
- 门禁：规则/融合聚焦 21/21、BuildScene 端到端与外部模式关闭 2/2、Space Unit 484/484、默认 Space Integration 277 passed / 94 SQL-gated skipped、CP6.Tests 2811 passed / 17 environment-gated skipped、完整 solution Release（含 Desktop/Android AOT）0 warning / 0 error。
- 本切片关闭“生产 BuildScene 全部占位失败”和“同源 LockedFacts 未接 Worker”的内部缺口，但尚无首次 Generation Run 创建 API；当前入口仍是 Failed/Stale Run 的 RuleOnly recovery。外部 Provider、不同 SourceHash 人工确认继承、权威 RackGenerationProfile、确定性无锁父关系和正式 CAD/黄金集证据仍未完成。完整边界见 `docs/space/reports/e13-rule-only-build-scene-production.md`。
- 合并后清理当前隔离工作区内 36 个可重建 `bin/obj` 目录、6,108 个文件，共 1,209,344,722 bytes（约 1.13 GiB）；临时功能分支待远端集成祖先链确认后删除。

## E13-S14 离线质量评估开发切片（2026-08-08）

- 在集成基线 `6c99b0fe` 上以功能提交 `e69b3bca`、报告提交 `9261d59a` 和 no-ff 提交 `292a26ed` 交付离线质量评估器、Calibration-only 阈值校准、Validation+ReleaseHoldout 样本外验证、95% Wilson 下界和规范报告 SHA-256；最终融合提案按 SampleId+SourceKey 一对一匹配，重复猜测计 False Positive，类型、关键属性和关系必须正确。
- 正式清单硬门禁覆盖 20 资产、L1～L5、10/5/5、唯一 CAD hash、授权/脱敏、应用/Parser/Provider/Model/规则/映射/答案版本、独立标注仲裁、验收日期、不可变和完整性审计。DevelopmentSeed 即使指标全绿也不能成为发布证据。
- `evaluate-ai-offline` 命令支持开发测量与 `--require-release-eligible` 失败关闭；无 Provider 调用、Draft 写入、Migration、HTTP/OpenAPI 或 SDK 变化。门禁为核心 11/11、命令 1/1、CAD 工具 26/26、Space Unit 482/482、默认 Integration 275 passed / 94 skipped、CP6.Tests 2811 passed / 17 skipped、完整 solution Release 0 warning / 0 error。
- 本切片是 E13-S14 工程能力，不是正式 S14 签收；仍需获授权黄金 CAD、独立标注/QA、真实版本冻结、Provider 输出、人工操作实测和签字。完整边界见 `docs/space/reports/e13-s14-offline-evaluation-gate-development.md`。
- 功能和报告已进入并推送远端集成祖先链，临时本地/远端分支已删除；清理 41 个可重建 `bin/obj` 目录、6,982 个文件，释放 1,380,171,591 bytes（约 1.29 GiB）。`main` 未修改。

## E03-S05 Excel 设计元数据权威 Apply 扩展（2026-08-08）

- 在集成基线 `0cff2123` 上以功能提交 `b5aa87b2` 关闭标准 Excel 的最后三个已知失败关闭字段：`Bindings` 成为 ModelVersion 内的设计声明，Site 权威 WarehouseCode 必须逐字符一致，AdapterId 固定自当前运行源；每个有绑定行的 Location 恰有一个 WmsPrimary，并可带 WmsAlias。Excel 声明不冒充 WMS Adoption。
- 新增版本化 `Space_LocationExternalBinding`、`Space_DesignAttribute` 和 `Space_LocationRevision.LocationType`。Attributes 只归属 Rack、`RackCode/LevelNo` 标识的 RackLevel 或 Location，Namespace 限 Owner/Batch/Container/Manufacturing/Custom；LocationType 限 Storage/Staging/Picking/Buffer。权威遗漏软删除并追加确定性命令，Primary/Alias 互换使用事务内两阶段更新。
- 同一 Serializable CommandBatch 现在覆盖 Rack/RackLevel/Location/Binding/Attribute；Apply 处理器与层级计划升级 v2。普通 Draft 和 Planning Scenario 克隆统一 v2，内容哈希/Validation v2、发布预览、Design Scene、Planning Exchange、OpenAPI 与 C#/TypeScript SDK 均包含新增数据。
- EF Migration `20260808131619_SpaceE03S05ExcelDesignMetadata` 与增量 SQL 已验证：脚本在临时 SQL Server 库连续执行两次后为 1 条历史、2 张新表、1 个 LocationType 列；EF model drift clean。
- 门禁：元数据/Validation 15/15、Match/Apply 12/12、Space Unit 471/471、默认 Space Integration 275 passed / 94 SQL-gated skipped、场景与 Clone/Migration 真实 SQL 10/10、CP6.Tests 2811 passed / 17 environment-gated skipped；完整 solution Release 0 error / 7 个未改动测试文件既有 warning，SDK drift、TypeScript strict、C# whitespace 与 diff 检查通过。完整证据见 `docs/space/reports/e03-s05-binding-attribute-location-type-apply.md`。
- 功能提交 `b5aa87b2`、验证报告提交 `713ac99d` 已通过 no-ff 合并提交 `691c2d31` 进入 `integration/space-v1-20260730`；本收尾提交随集成分支推送远端，`main` 未修改。合并后清理 37 个可重建 `bin/obj` 目录、9,093 个生成文件、1,783,694,326 bytes（1,701.06 MiB，约 1.66 GiB）。正式 CAD/Excel 签收仍等待获授权 DWG/DXF Provider、组织黄金集、真实大文件/故障/性能证据，以及生产镜像部署、备份迁移和 WMS 发布/恢复演练。

## E03-S05 Excel 层级与 RackTemplate 权威 Apply 扩展（2026-08-08）

- 在集成基线 `677f8df5` 上以功能提交 `cb802cf6` 扩展 E03-S05：同一 Serializable CommandBatch 现在按 `Racks → RackLevels → Locations` 写入，子对象按稳定业务键更新或按冻结行身份生成确定性 LogicalId/CommandId；整批只提升一次 Floor Revision 与 ContentRevision，重放不重复创建。
- 标准工作簿本身没有 Zone/Aisle 工作表，因此不伪造格式；`Racks.ZoneCode` 唯一解析已有 Zone，Aisle 保持可空。RackLevel 单元尺寸由 Rack 尺寸和 Bin/DepthCount 以整数毫米推导，Location 继承对应层的 CellWidth/ClearHeight/CellDepth/MaxLoad。映射方案声明子表权威时，省略的活动子对象改为 Disabled；WMS 已绑定 Location 不允许被省略删除。
- 非空 `RackTemplateCode` 解析可见活动 Asset：Tenant 同码优先于 System，并固定到最新 Ready 不可变 SpaceAssetVersion。Excel 显式尺寸与层级行仍是导入几何权威，模板只固定版本血缘。
- `Bindings`、`Attributes` 和非空 `LocationType` 继续返回 `SPACE_EXCEL_CAD_APPLY_SCOPE_UNSUPPORTED`，因为现有模型分别缺少仓库到 Site/Adapter 的权威解析、Rack/RackLevel/Location 属性归属和 LocationType 持久字段；不静默丢失数据。无 Migration、HTTP、OpenAPI、SDK 或前端变化，`main` 未修改。
- 门禁：Location 领域 3/3、Match/Apply 9/9、Space Unit 467/467、默认 Space Integration 272 passed / 94 SQL-gated skipped、CP6.Tests 2811 passed / 17 environment-gated skipped；完整 solution Release（含 Desktop/Android AOT）0 warning / 0 error，任务文件格式和 diff 检查通过。完整证据见 `docs/space/reports/e03-s05-hierarchy-template-apply.md`。
- 功能提交 `cb802cf6`、验证记录提交 `35e77a05` 已通过 no-ff 合并提交 `99801da1` 进入并推送至 `integration/space-v1-20260730`；`main` 未修改。合并后清理本隔离工作区 37 个可重建 `bin/obj` 目录、6,649 个生成文件，共释放 1,262.83 MiB（约 1.23 GiB）。

## Space 生产处理 Job Worker 接线（2026-08-08）

- 在集成基线 `17bce8df` 上以功能提交 `d09e44dc`、验证报告提交 `67d8b417` 和 no-ff 提交 `51012a43` 进入 `integration/space-v1-20260730`；`main` 未修改。
- 保留发布专用 Worker 独立处理 HistoricalRepublish/Reconcile/Publish；新增 Processing Worker 按租户消费 ExcelPreview、CadParse、ExcelCadMatch、ExcelCadApply、Import、BuildScene、ApplyGeneration、Validate 和 AiRetentionCleanup。E03/E13 非发布 Job 不再停留在“API 可排队、生产 WebApi Host 不认领”的内部状态。
- Processing Worker 每个租户每轮每类型最多认领一个 Job，复用既有租约、心跳、checkpoint、超时、退避、取消和接管；热队列不能饿死其他类型。稳定非空系统 Actor 同时传播到 Core/Application 上下文，满足 AI Apply 等后台安全门禁。
- 未配置 CAD、Import/BuildScene 或外部 AI Provider 时仍按既有稳定错误失败关闭；本卡不启用外部网络、密钥、URL 或来源不明的转换器。无数据库、Migration、API、OpenAPI、SDK 或前端变化。
- 门禁：Worker 聚焦 3/3、默认处理器注册 1/1、Job Processor 17/17、Space Unit 464/464、默认 Space Integration 270 passed / 94 SQL-gated skipped、CP6.Tests 2811 passed / 17 environment-gated skipped；完整 solution Release（含 Desktop/Android 双架构 AOT）0 warning / 0 error，任务文件格式与 diff 检查通过。完整证据见 `docs/space/reports/space-processing-job-worker-production.md`。
- 合并后清理 36 个可重建目录、6,190 个文件、1,206,049,385 bytes（约 1.123 GiB）。生产部署仍需发布包含本提交的镜像；正式 CAD Provider、授权 DWG/DXF 黄金集和真实性能证据未因此解除。E03-S05 的 RackLevel/Location 与 RackTemplateCode 权威原子写入已由后续扩展完成；标准工作簿没有 Zone/Aisle 表，ZoneCode 只解析已有 Zone。

## E03-S05 Excel 导入确认与幂等写入开发切片（2026-08-08）

- 在集成基线 `cdc629ea` 上以功能提交 `4e92e435`、验证报告提交 `d048e01a` 和 no-ff 提交 `f735747a` 进入 `integration/space-v1-20260730`；`main` 未修改。
- E03-S04 权威匹配面板现在必须由用户显式确认，服务端才创建 `ExcelCadApply` Job。确认绑定 Artifact ID/SHA、ExpectedContentRevision 与幂等键；Worker 重新打开私有 Artifact 和 Excel、重算规范投影并核验 Tenant/Site/Version/映射/Floor/修订，浏览器不能提交匹配结果、目标或写命令。
- Apply 在 Serializable 事务中写入货架、关联 Excel Source、追加确定性 CommandBatch/Command、提升一次 Floor Revision 和一次 ContentRevision，并把来源置为 Imported。重复确认、不同幂等键重确认、Job 重放及“数据库已提交但 checkpoint 未保存”的恢复均复用同一批次；任何权威输入漂移整批失败关闭、零部分写入。无新表或 Migration。
- 本卡正式写入范围仅为已匹配 `Racks` 行；其他目标工作表或非空 `RackTemplateCode` 返回 `SPACE_EXCEL_CAD_APPLY_SCOPE_UNSUPPORTED`，不会静默忽略。OpenAPI 操作数为 115，C#/TypeScript SDK 与前端显式确认状态已同步。
- 门禁：API/权限/OpenAPI 62/62，Apply/原子性/注册 8/8，Match/Artifact/Processor 27/27，Space Unit 464/464，默认 Space Integration 270 passed / 94 SQL-gated skipped，CP6.Tests 2808 passed / 17 environment-gated skipped，前端 132 files / 705 tests；类型、生产构建、SDK drift、格式与 diff 检查通过；完整 solution Release（含 Desktop/Android 双架构 AOT）0 error / 10 条既有 warning。完整证据见 `docs/space/reports/e03-s05-excel-import-confirmation-idempotency.md`。
- E03-S01～S05 的应用内主链已闭环，但生产 WebApi Hosted Worker 尚未认领 `ExcelCadApply`；完整 Excel 层级/模板写入、正式 CAD Provider、组织授权 DWG/DXF 黄金集和真实大文件/故障/性能证据仍未完成，因此不记作生产 CAD/Excel 正式签收。合并后清理 38 个可重建目录、28,788 个文件、1,544,566,969 bytes（约 1.438 GiB）。

## E03-S04 服务端权威 Match Artifact 开发切片（2026-08-08）

- 在集成基线 `27d3989b` 上完成功能提交 `4db2d0d0`、验证报告提交 `93f65a33`，并以 no-ff 提交 `3ee23655` 进入 `integration/space-v1-20260730`；`main` 未修改。
- 新增服务端权威 `ExcelCadMatch` Job 与私有 `ExcelCadMatchPreview` Artifact。HTTP 只冻结 Excel Preflight、CAD Parse/PreviewSet、映射方案和 Draft ContentRevision 后排队；Worker 重新打开私有 Excel、校验 Job/Artifact/Schema/SHA/血缘并直接读取数据库 Floor/Zone/Rack 权威快照，客户端不能提交匹配结果或哈希。
- 新增创建/查询 API、`space:model:edit/read` 权限、写/读审计、外部主体拒绝、受保护游标筛选分页和编辑器权威审阅/画布定位。Draft 修订漂移时保留只读历史证据但关闭 `CanConfirm`；重试只复用唯一且完整校验通过的 Artifact。OpenAPI 操作数为 113，C#/TypeScript SDK 已同步，无 Migration。
- 门禁：Space Unit 464/464；默认 Space Integration 267 passed / 94 SQL-gated skipped；CP6.Tests 2806 passed / 17 environment-gated skipped；前端 132 files / 702 tests；完整 solution Release（含 Desktop/Android AOT）0 error / 10 条既有 warning；类型检查、生产构建、SDK drift、受影响文件格式和 diff 检查通过。合并后清理 39 个可重建目录、32,452 个文件、2,475,932,206 bytes（约 2.306 GiB）。完整证据见 `docs/space/reports/e03-s04-authoritative-match-artifact.md`。
- E03-S05 的人工确认与原子 Draft 写入现已解除应用内前置依赖，但仍必须只消费本卡权威 Artifact，精确校验 ContentRevision 并失败关闭。正式 CAD Provider、组织授权 DWG/DXF 黄金集、生产 Excel/CAD Worker 部署和真实大文件/故障/性能证据仍未完成，因此本卡不是生产 CAD 正式签收。

## E06-S06 版本发布管理 UI 开发切片（2026-08-08）

- 在集成基线 `088648b7e` 上以功能提交 `69a8b77a`、验证报告提交 `3fefe0ef` 和 no-ff 提交 `0d61f3dc` 进入 `integration/space-v1-20260730`；`main` 未修改。`/space/publish` 现提供验证、权威差异/WMS 影响预览、外部审批引用确认、发布进度/失败恢复/人工重试、审计时间线和历史版本重新发布入口；原库位发布工具保留在 `/space/location-publish`。
- 页面严格复用 E06-S01 至 E06-S05 的服务端权威链：按 `space:model:validate/publish/rollback` 分权，保留不确定失败期间的稳定幂等键，409/422 失败关闭，历史回退创建新版本和新尝试而不改写旧证据。桌面 1440 px 与手机 390 px 实际浏览器检查均无横向溢出，四阶段流程完整可见。
- 新增只读 `GET /api/space/design/v1/sites/{siteId}/publish-attempts`，聚合发布尝试、版本、Job、未解决对账问题和 HistoricalRepublish 血缘，带 Tenant/内部主体/Site 范围、受保护游标、标准 Problem Details 和读审计；OpenAPI 操作数为 111，C#/TypeScript SDK 已同步并通过漂移检查，无 Migration。
- 门禁：Space Unit 462/462；CP6.Tests 2804 passed / 17 environment-gated skipped；默认 Space Integration 263 passed / 94 SQL-gated skipped；前端 130 files / 698 tests；类型检查、生产构建、WebApi/C# SDK Release、SDK drift、桌面/手机视觉和 diff 检查通过。合并后清理 26 个可重建目录、24,325 个文件、1,409,759,827 bytes（约 1.313 GiB）。完整证据见 `docs/space/reports/e06-s06-publish-management-ui-development.md`。
- E06 本地开发主链现已有管理入口，但仍不是生产签收：生产等价 WMS 外部写入/恢复/告警/人工对账演练、正式 CAD Provider 与组织授权 DWG/DXF 黄金集、E03-S05 权威 Match Artifact 写入链，以及 Beta/GA 的跨职能、容量、SLO、灾备和安全证据仍未完成。

## E06-S05 历史版本重新发布回退开发切片（2026-08-08）

- 在集成基线 `bf9c07d8` 上以功能提交 `ea16ce67`、no-ff 提交 `85e039f8` 进入 `integration/space-v1-20260730`；`main` 未修改。新增 `space:model:rollback`、创建/查询历史重新发布 API、OpenAPI 及 C#/TypeScript SDK，OpenAPI 操作数为 110。
- 回退不是改写旧版本：系统冻结不可变 HistoricalRepublish 证据，把 `Production + Superseded` 历史快照克隆成新生产候选，保留 LogicalId 并建立 BasedOn/CloneOperation 血缘；再按当前规则与当前 WMS 能力校验，创建新的 PublishPlan、PublishAttempt、Publish Job 和追加式审计链。历史版本、旧计划、旧尝试和旧审计均不修改或删除。
- `HistoricalRepublish` 后台 Job 按克隆、校验、排队三步执行，复用 E06-S04 的租约、超时、退避、恢复和对账。旧生产指针、活动草稿/发布、校验阻断、能力变化和计划漂移都失败关闭；新尝试完成前当前 Published 保持有效。运行态激活再次核验持久化 Attempt/Plan 与原申请人，允许系统 Worker 执行但防止内部身份或计划替换。
- Migration `20260807170204` 使用强 Tenant 复合外键、唯一幂等约束、不可变证据保护和前向修复 Down；幂等 SQL 已生成。门禁：Space Unit 462/462、CP6.Tests 2803 passed / 17 skipped、默认 Space Integration 261 passed / 94 SQL-gated skipped、发布真实 SQL 3/3（其中历史重新发布 1/1）；完整 solution Release（含 Desktop/Android 双架构 AOT）0 warning / 0 error，EF/SDK/TypeScript/Web/diff 门禁通过。证据见 `docs/space/reports/e06-s05-historical-republish-development.md`。
- 本卡没有实现 E06-S06 发布管理 UI。生产等价 WMS 演练、正式 CAD Provider/授权黄金集、E03-S05 权威 Match Artifact 写入链及 Beta/GA 证据仍未完成；下一张为 E06-S06。

## E06-S04 发布队列与恢复开发切片（2026-08-07）

- 在集成基线 `0c416d44` 上以功能提交 `0f1ee6a9`、no-ff 提交 `0c1e75ff` 进入 `integration/space-v1-20260730`；`main` 未修改。E06-S03 请求内 Saga 已改为持久化 Publish/Reconcile 作业，WebApi Hosted Worker 按 Tenant 执行，租约心跳使用独立 SpaceContext。
- PublishAttempt/Batch 冻结 Job、请求、步骤、退避和批次恢复信息。30 分钟步骤超时、Job Ledger 指数退避、显式人工 retry、未解决问题优先 Reconcile，共同支持进程退出、WMS 超时、部分结果和运行态结果不确定后的安全恢复；恢复完成前旧 Published 指针保持不变。
- 新增哈希链式追加审计，区分失败观察、重试调度、人工介入、对账请求/解决和最终完成；审计事件数据库禁止更新/删除。Migration `20260807144532` 对旧活动发布失败关闭，幂等脚本双执行通过。人工 retry API、OpenAPI 和 C#/TypeScript SDK 已同步，OpenAPI path 数为 108。
- 门禁：Space Unit 458/458、API/权限/OpenAPI 聚焦 59/59、CP6.Tests 2799 passed / 17 skipped、默认 Space Integration 259 passed / 93 SQL-gated skipped、E06-S04 真实 SQL 4/4；完整 solution Release（含 Desktop/Android AOT）0 error / 7 条既有 warning，EF/SDK/diff 门禁通过。证据见 `docs/space/reports/e06-s04-publish-recovery-development.md`。
- 本卡没有实现 E06-S05 历史再发布回退或 E06-S06 管理 UI。生产等价 WMS 演练、正式 CAD Provider/授权黄金集、E03-S05 权威 Match Artifact 写入链及 Beta/GA 证据仍未完成；下一张为 E06-S05。

## E06-S03 仓库级发布编排开发切片（2026-08-07）

- 在集成基线 `0bde7bc9` 上以功能提交 `48082680`、no-ff 提交 `5b9b95ab` 进入 `integration/space-v1-20260730`；`main` 未修改。新增创建/读取发布尝试 API、`space:model:publish` 权限、写/读审计、OpenAPI 及 C#/TypeScript SDK。
- 发布入口重新构建并验证 E06-S02 权威计划，绑定 Published 指针、ValidationRun、PlanHash、ContentRevision 和实时 WMS 能力。Serializable 仓库槽位事务持久化不可变 PublishPlan，同租户/Site 只允许一个活动发布；同幂等键同请求稳定重放，同键异请求冲突。
- 请求内 Saga 依次执行 WMS 预检、稳定分批 apply、异常状态查询、回执保存和逐项回读。外部写入可能开始后不随 HTTP 取消丢失证据。只有全部 WMS 回读一致，才在 SpaceContext/CP6Context 同一 SQL 事务中物化运行态、校验投影哈希并原子切换 Published 指针。
- 零影响预检失败退回 Ready 并释放槽位；部分/未知/矛盾回执、回读不一致或运行态失败保留旧 Published，将目标置为 ReconciliationRequired 并保存问题与证据。新增 6 张发布/回执/对账/运行态表，Migration `20260807135544` 的 Down 失败关闭。
- 门禁：领域聚焦 4/4、API/权限/审计/OpenAPI 58/58、真实 SQL 1/1 fact（覆盖成功激活和部分 WMS 对账）、Space Unit 452/452、CP6.Tests 2798 passed / 17 skipped、默认 Space Integration 259 passed / 90 SQL-gated skipped；Infrastructure/WebApi Release 0 warning/0 error，完整 solution 双架构 AOT 0 error / 10 条既有 warning，EF/SDK/幂等 SQL/diff 门禁通过。合并后清理 36 个可重建目录、6,566 个文件、1,625,959,672 bytes（约 1.514 GiB）。证据见 `docs/space/reports/e06-s03-publish-orchestration-development.md`。
- 本卡没有实现 E06-S04 队列/超时/重试/人工对账审计、E06-S05 历史再发布回退或 E06-S06 UI。生产 Hosted Worker、正式 CAD Provider/授权黄金集、E03-S04/S05 权威匹配写入链及 Beta/GA 证据仍未完成；下一张为 E06-S04。

## E06-S02 版本差异与影响预览开发切片（2026-08-07）

- 在集成基线 `05c1df86` 上以功能提交 `a174f7cc`、no-ff 提交 `5bd2c616` 进入 `integration/space-v1-20260730`；`main` 未修改。新增 `GET /api/space/design/v1/versions/{versionId}/publish-preview`、`space:model:read`、读审计、标准 Problem Details、OpenAPI 及 C#/TypeScript SDK。
- 预览只读取服务端权威目标版本、当前 Published 指针、匹配的终态 ValidationRun 和实时 WMS 能力；按 LogicalId 稳定生成 Create/UpdateMaster/UpdateGeometryOnly/Disable/Restore/NoOp、WMS 影响与阻断项。稳定 PlanHash 绑定版本、ContentHash、ValidationRun、规则/适配器/能力和全部有序计划项；Location 改码失败关闭，已采纳库位不重复创建。
- 支持楼层/对象/动作/影响/NoOp 筛选及绑定计划和筛选条件的受保护游标。`Publishable` 只在 Passed、Ready、零校验阻断且零计划阻断时为 true。本卡无 WMS 调用、无发布指针切换、无 PublishPlan 持久化、无 Migration；这些属于 E06-S03 及后续卡。
- E06-S01 来源规则同时前向修正：Editor/Template 内建来源的合法终态 Ready 可参与发布；DWG/DXF 等文件来源继续要求 PreviewReady/Imported。门禁：引擎聚焦 17/17、API/权限/OpenAPI 55/55、真实 SQL 3/3、Space Unit 448/448、CP6.Tests 2794 passed / 17 skipped、默认 Space Integration 259 passed / 89 SQL-gated skipped、完整 solution 双架构 AOT Release 0 warning/0 error，EF/SDK/diff 门禁通过。合并后清理 36 个可重建目录、6,578 个文件、1,621,278,419 bytes（约 1.51 GiB）。证据见 `docs/space/reports/e06-s02-publish-preview-development.md`。
- 这不是完整 E06/Beta/GA 签收。下一张为 E06-S03 仓库级发布编排：必须持久化不可变计划并以可恢复 Saga 执行，只有 WMS 成功并回读验证后才能激活运行态；生产 Hosted Worker、正式 CAD Provider/授权黄金集、E03-S04/S05 权威匹配写入链等缺口仍保留。

## E06-S01 版本权威校验引擎开发切片（2026-08-07）

- 在集成基线 `022cb937` 上以功能提交 `c17242c3`、no-ff 提交 `76c70230` 进入 `integration/space-v1-20260730`；`main` 未修改。校验只读取服务端权威 ModelVersion 快照，冻结 ContentRevision/Hash、规则集、WMS Adapter/CapabilityHash、Job 和 Correlation，不接受客户端自报校验结果。
- 新增 ValidationRun、统一 Issue 血缘和 Validate Job Processor；覆盖编码、几何、层级、绑定、来源、AI provenance、已发布身份冻结及既有问题。无 Blocking 时 Passed/Ready，有 Blocking 时 Blocked/Draft，权威输入或能力漂移时 Failed/Draft。相同输入在 SQL transaction app lock 与唯一索引下只生成一个 Run/Job；发布态在复用前失败关闭。
- 新增 POST 创建/复用校验和 GET 查询接口、`space:model:validate` 权限、写/读审计、OpenAPI 及 C#/TypeScript SDK。Migration `20260807105256` 与幂等 SQL 双执行通过，Down 失败关闭以保留审计证据。
- 门禁：校验引擎 9/9、API/权限/审计/OpenAPI 74/74、Space Unit 440/440、CP6.Tests 2793 passed / 17 skipped、默认 Space Integration 259 passed / 86 SQL-gated skipped、E06-S01 真实 SQL 3/3；完整 solution Release 0 warning/0 error，EF/SDK/幂等 SQL/diff 门禁通过。合并后清理 36 个可重建目录、7,159 个文件、1,974,302,339 bytes（约 1.839 GiB）。证据见 `docs/space/reports/e06-s01-validation-engine-development.md`。
- 这不是完整 E06/Beta/GA 签收。E06-S02～S06、生产 Hosted Worker、E03-S04 权威 Match Artifact/E03-S05 写入链，以及正式 CAD Provider/授权黄金集仍未完成；下一张可独立推进 E06-S02 版本差异与影响预览 API。

## E02-S08 CAD 解析作业安全开发切片（2026-08-06）

- E02-S08 已以功能提交 `20ade7e7`、证据提交 `29667831` 和 no-ff 提交 `feaf29fb` 进入 `integration/space-v1-20260730`；`main` 未修改。新增 CAD Source 上传、CadParse 排队/查询/取消/显式重试、Artifact 持久化和 PreviewReady 收口，全部复用现有 Job Ledger、Attempt、Step、租约与 checkpoint。
- 排队/运行期间 Source 保持 Ready；取消、Provider 失败或进程中断不会留下 Parsing 污染。只有 CadIr、LayerInventory、PreviewSet 三类 Artifact 的引用、大小和 SHA 全部复验通过，最终事务才执行 Ready → Parsing → PreviewReady；全路径不写 Draft。
- 同 Job 重放复用自身 Artifact；显式 Retry 只在输入哈希和 `space-cad-parse-v1` processor version 相同时复用直接父 Job checkpoint。SQL Server 同一 Tenant/Source 以 transaction-owned application lock 串行化启动和重试，并验证双连接同键只产生一个 Retry Job。生产 Provider 默认失败关闭，未配置文件存储时只在实际执行步骤时失败，不阻止 WebApi composition。
- 门禁：Space Unit 431/431；默认 Space Integration 259 passed / 83 SQL-gated skipped；本卡内存 4/4、processor 5/5、controller 3/3、权限 27/27；KOUSQLSERVER 血缘与跨 Retry checkpoint 各 1/1；CP6.Tests 2788 passed / 17 environment-gated skipped；Release build、EF 无漂移通过。完整真实 SQL 全量曾因长时间无输出人工终止，不记为通过。
- 这是开发闭环，不是正式 CAD 签收。仍缺生产原生 DWG/DXF Provider、组织有权使用的黄金集、production Worker host 自动接线和真实大文件/故障/性能证据；合成 DXF 不能替代发布门禁。合并后清理 21 个可重建目录、996 个文件、623,921,427 bytes（约 0.581 GiB）。证据见 `docs/space/reports/e02-s08-cad-parse-job-safety-development.md`。

## Version Clone 必填字段前向修复（2026-08-06）

- 在集成检查点 `90e51a2e` 上以功能提交 `0564afad`、no-ff 提交 `01eba1b7` 修复 Published → Draft Clone；`main` 未修改。
- 根因为手写快照 SQL 漏复制 Zone/Aisle/Rack 非空 `Name`，导致三类父记录插入失败并连锁触发 RackLevel/Location 外键错误；Rack 的可空 `RackType` 也存在静默丢失。该问题已在 `ac9c977c` 独立基线复现，确认不由 E13-S17 retention Migration 引入。
- 修复仅补齐 `Name` 与 `RackType` 列映射，无 Schema/Migration 变化；回归覆盖不同于编码的名称、非空 RackType、RowId 重映射、LogicalId 和层级关系保真。
- 门禁：新增回归修复前 1/1 失败、修复后 1/1 通过；Version Clone 7/7、Space Unit 430/430、Space Integration + KOUSQLSERVER 336/336 且 0 skipped；Unit/Integration Release build 均 0 warning / 0 error，diff check 通过。证据见 `docs/space/reports/version-clone-required-fields-forward-fix.md`。
- 远端集成已推进到 `08e3fe40` 并确认祖先链；临时功能分支已在本地/远端删除。清理 16 个可重建 `bin/obj` 目录，回收 513,840,161 bytes（约 0.479 GiB）。

## E13-S17 迁移、前向修复与保留清理（2026-08-06）

- 在 `ac9c977c` 基线上以功能提交 `12db5531`、no-ff 提交 `e7720df4` 进入 `integration/space-v1-20260730`；`main` 未修改。
- 新增 Tenant 级 `AiRetentionCleanup` Job、严格每日冻结 payload、5 次安全重试、Step checkpoint 与 SQL Server transaction-owned `sp_getapplock`。外部主体和非专用 service principal 在排队前失败关闭，公共 HTTP 未暴露清理入口。
- Draft/Failed/Abandoned 的非 current 终态 Run 默认 90 天后净化大载荷；Usage 至少 365 天后逻辑归档；Staging 清空临时 JSON 后软删除。Published/Superseded/Publishing/Reconciliation、有效保留锁、Decision、Locked Fact、CommandBatch、预算账本和审计历史不清理。
- Migration `20260806160931` 只新增 5 个 nullable 列与 4 个索引；幂等 SQL 连续执行两次通过。`Down` 以 `THROW 51017` 禁止破坏性回滚，失败必须通过更高版本 forward-fix Migration 修复。
- 门禁：E13-S17 unit 6/6、内存/迁移 4/4、KOUSQLSERVER 3/3、Space Unit 430/430、默认 Integration 255 passed / 81 SQL-gated skipped、Release build 0 error、EF 无漂移、diff check 通过。完整证据见 `docs/space/reports/e13-s17-ai-retention-forward-fix.md`。
- 全量真实 SQL 的两个 Version Clone 失败已在起始提交独立复现，属于既有 clone SQL 缺少 Zone/Aisle/Rack `Name` 的基线问题；随后已由 `0564afad` / `01eba1b7` 修复并复验 336/336。E13-S14/S15/S19 仍等待外部证据；E13-S18 依赖 S15，不能提前签收。
- 远端集成已推进到 `1659b333` 并确认包含功能提交；临时功能分支已在本地/远端删除。清理 16 个可重建 `bin/obj` 目录，回收 523,868,809 bytes（约 0.488 GiB）。

## E13-S13 外部用户拒绝与数据外发门禁（2026-08-06）

- 在 E13-S11 后续集成基线 `d2a96be4` 上完成功能提交 `37bf5c37`，并以 no-ff 提交 `e1682efc` 集成到 `integration/space-v1-20260730`；`main` 未修改。
- Provider Gateway 在策略、配额和 Provider 前再次拒绝外部主体并要求有效内部 Tenant/Actor。External Provider 发送前执行冻结 JSON 字段白名单和最小化 HMAC Token 语法门禁；原始名称、任意 Prompt、路径、URL、额外字段或非白名单锁定事实以 `SPACE_AI_OUTBOUND_PAYLOAD_DENIED/403` 失败关闭。
- 4 个 AI 控制器的 16 个端点均有显式审计元数据，7 个 GET 均启用读审计。Customer、Supplier、3PL × 16 操作形成 48 条稳定 `SPACE_EXTERNAL_SUBJECT_DENIED/403` 断言，并证明拒绝发生在数据访问和写入前。
- 验证：Space Unit 424/424，Provider/最小化 34/34，外部主体与 AI 管理 8/8，审计/OpenAPI/权限 87/87，非 SQL 管理/注册/矩阵 10/10，KOUSQLSERVER Apply/恢复/配额/外部矩阵 21/21；Application Debug/Release 0 warning/0 error，WebApi Release 仅有 3 条既存 Core nullable warning。证据见 `docs/space/reports/e13-s13-external-ai-security.md`。
- CSO 范围审计没有确认当前可利用漏洞：生产仍无 Gateway 调用方、External Provider 注册为空且配额失败关闭。本卡是未来接入真实 Provider 前的安全封口，不是网络端到端签收。验证后清理 28 个可重建 `bin/obj` 目录，回收 1,062,306,204 bytes（约 0.989 GiB）；下一张建议卡为不依赖外部 CAD/Provider 的 E13-S17 迁移、前向修复与保留清理。

## E13-S11 Generation Run 恢复产品化开发切片（2026-08-06）

- 在 E13-S10 集成状态基线 `c1efea2b` 上完成功能提交 `dcbbfca8`、证据提交 `c695850f`，并以 no-ff 提交 `d3c2da75` 集成到 `integration/space-v1-20260730`。
- 新增取消、同输入安全重试、废弃、权威 Apply 结果对账、Failed/Stale replacement Run 与 RuleOnly 降级 API；全部 mutation 要求内部主体、租户/Site、精确 rowversion、幂等键和审计。OpenAPI、C#/TypeScript SDK 与前端审核面板已同步。
- 取消在 Worker 安全点确认，不拆分 S10 原子 Apply；同输入重试只允许 Transient/Resource/Bug 且沿用同一 Job/Run/检查点/ApplyPlan。对账只信任匹配当前 revision、RunId 和 ApplyPlanHash 的已提交 CommandBatch，不根据 Job 文本猜测成功。
- Failed/Stale 恢复创建新 Run/BuildScene Job 并保留 basedOnRunId、冻结输入、Decision 与审计；旧 Run 不原地 rebase。Failed current 源先在同一事务中退役再插入 replacement；同键并发锁内优先重放，避免旧 rowversion 误报 409。只有 BuildScene Provider 不可用才提示 RuleOnly。
- 门禁：WebApi build 0 warning/0 error、状态机与分类 42/42、OpenAPI/权限 52/52、AI Apply/Recovery 真实 SQL 14/14、前端聚焦 2 files/6 tests、前端全量 129 files/695 tests、type-check、production build、SDK 生成与 `git diff --check` 全部通过。完整证据见 `docs/space/reports/e13-s11-generation-run-recovery.md`。
- 生产默认 BuildScene executor 仍失败关闭；真实全阶段 `LoadLockedFacts` 自动接线、异源几何建议继承与人工确认、外部 Provider、授权真实 CAD、正式黄金集、性能/试点和发布证据仍未完成。已清理 34 个可重建目录并回收 1,008,090,267 bytes（约 0.939 GiB）；`main` 未修改。下一张建议卡为 E13-S13 外部用户拒绝与数据外发门禁，E13-S17 迁移/保留清理也已独立解除依赖。

## E13-S10 Staging 与原子 AI Apply 开发切片（2026-08-06）

- 在 E13-S09 集成状态基线 `b663c4ae` 上完成功能提交 `43dc5534`、既有审核基线更新纠偏 `fbc59fb3`、证据提交 `5be724cf`，并以 no-ff 提交 `0c587d4c` 集成到 `integration/space-v1-20260730`。
- 新增 `Space_GenerationStagingElement`、冻结 ApplyPlanHash、ApplyGeneration Job/Worker、Run 状态查询和原子 Apply API。POST 同步重验 ContentRevision、Run rowversion、ReviewEtag、权限、租户/Site 与幂等键；Worker 在短 SQL 事务内按 Run→Version→Model 加锁并再次验证 Proposal/Issue/Revision/唯一/引用/边界/碰撞。
- Added 提案创建 Zone/Aisle/Rack/RackLevel/Location/Element；Modified/Unchanged 按同类型、同 Floor LogicalId 原位更新。Rack 派生项按确定性 ID 复用/补齐/停用，WMS 绑定 Location 不可被派生缩减移除；跨类型、跨楼层和资产库 Element 冲突失败关闭。CommandRecord 保存权威 before/after。
- Queue 使用 Serializable 与租户 + Run 的 transaction-scoped `sp_getapplock`；同键双连接只产生一个 Job/幂等记录。成功只增加一个 Floor Revision 与 ContentRevision；故障注入、Stale、409 和校验失败均保持 Draft 零部分写入，Published/WMS/设备状态不变。
- 门禁：E13-S10 真实 SQL 7/7、Space Unit 413/413、默认 Space Integration 248 passed / 71 SQL-gated skipped、CP6.Client 71/71、CP6.Tests 2783 passed / 17 environment-gated skipped、前端 129 files / 694 tests；完整 solution、type-check、production build、OpenAPI/C#/TypeScript SDK drift、EF/Migration/幂等 SQL drift 与 `git diff --check` 通过。完整证据见 `docs/space/reports/e13-s10-atomic-ai-apply.md`。
- 下一张独立卡是 E13-S11 取消、重试、降级和 Stale 恢复产品化。真实 Worker `LoadLockedFacts` 自动接线、异源几何建议继承、外部 Provider、授权真实 CAD、正式黄金集和发布晋级证据仍是独立缺口。功能分支已远端备份，前端依赖清理回收约 0.31 GB；`main` 未修改。

## E13-S09 决策与人工锁定修正开发切片（2026-08-05）

- 在 E13-S08 集成状态基线 `e469c6ca` 上完成功能提交 `c87289f2`、证据提交 `382d5722`，并以 no-ff 提交 `396ee38b` 集成到 `integration/space-v1-20260730`：交付追加式 Accept/Reject/Modify 决策、单条/批量 API、权威审核读取、问题解决血缘、人工锁定事实、Migration、OpenAPI/C#/TypeScript SDK 和实时前端决策面板。
- 单条写入同时复核 Proposal rowversion 与 ReviewEtag；受保护游标绑定租户、Run、审核状态和筛选，Serializable 事务原子提交 Proposal、Decision、Issue、审核完成时间和 24 小时幂等记录。批量上限 1,000，批量 Accept 默认关闭；服务端始终重验资格。
- Modify 仅允许 RFC 6902 `replace` 精确白名单，单次可修改并锁定 1～32 个唯一字段；关系、业务枚举、理由码和评论均失败关闭校验。Decision、锁定事实和审计均追加写；`ReviewCompletedAtUtc` 只由服务端在全部有效提案已决策且无 Open Blocking Issue 时写入。
- 新增不可变 `Space_GenerationLockedFact`：相同 SourceHash 按 `SourceKey + ProposalType + FieldPath` 自动继承人工终值；不同 SourceHash 不猜测、不自动继承。内部审核 API 受 `space:model:review-ai` / `space:model:edit`、租户/Site 和 external-principal 闸保护；本切片不写 Draft、Published、WMS 或设备控制数据。
- 门禁：E13-S09 真实 SQL 3/3、Space Unit 413/413、Space Integration + KOUSQLSERVER 312/312、CP6.Tests 2779 passed / 17 environment-gated skipped、CAD 工具 25/25、前端聚焦 2 files / 7 tests、前端全量 128 files / 692 tests；type-check、production build、OpenAPI/C#/TypeScript SDK drift、EF/Migration/幂等 SQL drift 均通过。完整 solution Release 非增量单线程构建 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变。完整证据见 `docs/space/reports/e13-s09-proposal-decisions-human-locks.md`。
- 下一张独立卡是 E13-S10：Staging + 原子 Apply；任何冲突或校验失败都不得产生部分 Draft。真实 Worker 的 `LoadLockedFacts` 自动接线、异源几何“建议继承 + 人工确认”、外部 Provider、授权真实 CAD 和正式黄金集仍是独立缺口，不因本切片解除。`main` 未由本任务修改。
- 功能 tip `382d5722` 已确认进入远端集成祖先链，本地/远端临时功能分支均已删除；本工作树已清理 `node_modules`、前端 `dist` 及本轮 Release `bin/obj` 共 38 个目录，回收 1,522,981,317 bytes（约 1.42 GiB）。Debug 缓存、源码、锁文件、报告和远端 Git 历史保留。

## E13-S08 AI 提案审核工作台开发切片（2026-08-05）

- 在 E13-S07 集成基线 `d100a956` 上完成功能提交 `b1ab93f6`、证据提交 `5c2e0605`，并以 no-ff 提交 `fbf4596e` 集成到 `integration/space-v1-20260730`：新增只读完整 Draft 基线、审核工作区合同、确定性差异投影、受保护游标分页、批量选择资格预检、开发 CLI 和 Design V1 本地审核面板。
- 工作区绑定 Tenant、ModelVersion、Floor、ProposalSet SHA、Baseline SHA、ContentRevision/Hash、ReviewEtag 和 Workspace SHA；反序列化会深验只读/未写入标记、规范排序、唯一身份、摘要、差异和哈希。默认页长 50、最大 200，工作区最多 100,000 项，批量选择最多 1,000 项；游标同时绑定工作区与筛选，陈旧 ETag、未知/重复 ID、空或超量筛选失败关闭。
- 每项保留字段胜者/evidence、关系、问题、整数毫米几何和画布位置，并给出 Added/Modified/Unchanged、字段 Added/Removed/Changed、RackLevel/Location 容量 before/after。前端支持筛选、分页、详情、选择和画布定位；Model/Floor/Revision/Hash 过期时禁用定位与选择；没有 API mutation、Accept/Reject 写按钮或 Draft 写入口。
- Accept 资格只允许 Ready + High + 提案允许批量接受且无 Blocking；Reject 也只生成资格预检。所有预检固定 `RequiresServerRevalidation=true`、`DecisionWritten=false`、`DraftWritten=false`。开发 CLI 使用 32～128 byte HMAC 游标 key 并清零；生产必须复用既有 Data Protection 租户/主体/授权/时效绑定。
- Sample13 本轮生成 21 项（High 13 / Low 8、Ready 0 / NeedsReview 21 / Blocked 0），空楼层开发基线使 21 项诚实标为 Added；Workspace `2fc473e4...c5530efd`、58,645 bytes，重复生成字节一致。两页各 5 项且无交集；High+Rule+locatable 返回 13；Accept 预检 13 项全部因需要单项审核而不可用，Reject 预检 21/21 可选，两者均没有写入。空基线不冒充真实 Draft 对比，Modified/Unchanged 由单元测试覆盖。
- 门禁：E13-S08 后端 4/4、Space Unit 401/401、CAD 工具 25/25、前端聚焦 4/4、前端全量 127 files / 689 tests、type-check 和 production build 通过；完整 solution Release 非增量单线程、禁用节点复用/共享编译构建 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变；合并态后端 4/4、CAD 25/25、前端 4/4 和 type-check 复验通过。完整证据见 `docs/space/reports/e13-s08-proposal-review-workbench-development.md`。
- 这是开发切片，不是正式生产签收：仍缺 Proposal/Review 持久化、Migration、租户授权/审计、公共 API/OpenAPI/SDK、权威 Draft 基线服务、真实项目性能/差异证据及 Worker/Run/Artifact 接线；E13-S05～S07 正式缺口不解除。下一张独立卡是 E13-S09：追加式单条/批量 Decision、rowversion/ReviewEtag 并发控制、服务端资格复验、补丁白名单和审核完成状态；仍不得提前 Apply，High 自动批量接受继续关闭。`main` 未由本任务修改。
- 功能 tip `5c2e0605` 已确认进入远端集成祖先链，本地/远端临时功能分支均已删除；本工作树的 `node_modules`、前端 `dist`、`tmp/e13-s08`（含开发 HMAC key）及本轮 Release/AOT `bin/obj` 已清理，共回收 1,653,860,578 bytes（约 1.54 GiB）。Debug 缓存、源码、锁文件、Schema、报告和远端 Git 历史保留。

## E13-S07 规则/AI 融合与确定性生成开发切片（2026-08-05）

- 在 E13-S06 集成基线 `44c87a26` 上完成功能提交 `8be8b20f`、证据提交 `d81119f5`，并以 no-ff 提交 `7d5c8aa0` 集成到 `integration/space-v1-20260730`：新增 `IWarehouseDraftSynthesizer`、只读提案合同、确定性合成器和 RFC 4122 UUIDv5 身份生成器。
- 合成器重新核验 Provider Output Canonical SHA，并绑定 ModelVersion/RuleVersion、Tenant/Floor、Source/Transform、Semantic Preview、Provider Input、local Source Map 和 locked-fact 快照；任一身份或快照错配整体失败，不返回部分提案。反序列化提案还会深验规范顺序、证据胜者、关系、LogicalId、货架派生、摘要和自身 SHA。
- 字段固定 `HumanLocked > DeterministicRule > AI > TemplateDefault`：锁定/规则/AI 冲突保留双方证据与稳定问题码；强规则保留 High，软规则冲突降为 Medium/Low。AI 只补 allowlisted 语义属性；无确定性规则几何的 AI 建议、类型不兼容锁定属性、未解析父目标和父关系环均 Blocking，不能创建隐式对象。
- 提案几何只能来自 E02-S06 的整数毫米 `CadIrDeterministicRule`；对象、RackLevel、Location 分别用 ModelVersion+SourceHash+SourceKey、Rack+层号、Rack+层/列/深位生成 UUIDv5。货架方案固定 HumanLocked > ExcelMapping > ExplicitSelected；缺方案报 `SPACE_RACK_PROFILE_REQUIRED`，不使用隐式尺寸。
- 样例 13：22 个特征/21 条 Local 建议生成 21 个唯一只读提案，High 13 / Medium 0 / Low 8；8 Rack 按显式开发方案派生 24 层、192 库位，0 Blocking；ProposalSet `fba6c44c...cf31a288`、40,424 bytes，重复运行字节一致；全部 geometrySource=Rule，external=false、draft=false、readyForApply=false。
- 门禁：E13-S07 聚焦 10/10、Space Unit 397/397、CAD 工具 25/25、默认 DI 1/1；完整 solution Release 非增量单线程、禁用节点复用/共享编译构建 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变；格式与差异检查通过。无数据库、Migration、公开 API、前端、OpenAPI 或 SDK 变化。
- 这是开发切片，不是正式 E13-S07 生产签收：仍缺持久化/授权/版本冻结的 RackGenerationProfile 与 Excel/人工方案选择链、现有编码服务的 Application 层只读纯预检端口、完整 Floor/Draft 边界/碰撞/父归属/现有码冲突证据，以及 Worker/Run Artifact/审计接线。E13-S05/S06 外部 Provider 正式门禁也不因此解除。完整证据见 `docs/space/reports/e13-s07-rule-ai-fusion-development.md`。
- 当前提案合同可作为 E13-S08 分页、差异预览和审查工作台的只读输入；E13-S08 不得写 Draft 或绕过上述正式缺口。`main` 未修改。

## E13-S06 Provider 输出 Schema 与不可信输入校验开发切片（2026-08-05）

- 在 E13-S05 集成基线 `0c59cc34` 上完成功能提交 `7b95c29e`、证据提交 `1b297a91`，并以 no-ff 提交 `551ad8d4` 集成到 `integration/space-v1-20260730`：新增 `IWarehouseGenerationOutputValidator`、确定性验证器和带 Canonical SHA-256 的 `ValidatedSemanticResult`。
- 原始 Canonical JSON 在反序列化前执行 64 MiB/深度/严格 JSON、必填/未知/重复字段、字符串枚举、非负 Usage、0～1 decimal、数组上限和 C0/C1 控制字符门禁；权威输出 Schema 同步收紧安全字符串并表达五类专属属性组合。
- typed 语义门禁拒绝未知/重复 Suggestion SourceKey、自引用/未知/重复/超量关系、非法枚举/范围、空或重复 Evidence、未知 Diagnostic SourceKey 和 Zone/Rack/Door/Dock/StaticEquipment 属性错配。成功输出生成稳定 Canonical SHA；失败统一为非重试 `SPACE_AI_OUTPUT_INVALID`/502，只暴露稳定违规码。
- `SpaceAiGenerationGateway` 在 Provider 返回后、配额租约释放前强制验证，失败不返回部分对象且租约正常释放；默认 DI 注册验证器，但 Provider Registry 继续为空。开发 CLI 可验证原始 Canonical 文件，成功只打印 Schema/模型/计数/SHA，读取缓冲区清零；无网络、持久化或 Draft 写入。
- 样例 13 的 22 个输入特征产生 21 条 Local 建议和 1 条诊断；原始文件 SHA `5e57cce3...6f7e23`，Canonical SHA `913e99b4...84767c`，独立重复运行字节一致。未知建议/关系引用为 0，39 个身份/哈希/SourceRef/属性值敏感候选命中 0，全部命令报告 external=false、draft=false。
- 恶意矩阵覆盖 9 类原始 JSON、16 类 typed 语义以及字节上限、稳定 SHA、Gateway 租约和 CLI 篡改。门禁：Provider/验证器/Gateway 55/55、Space Unit 387/387、CAD 工具 25/25、默认 DI 1/1；完整 solution Release 非增量单线程、禁用节点复用构建 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变；格式、Schema JSON 和差异检查通过。
- 这是开发切片，不是正式 E13-S06 端到端签收：仍依赖 E13-S05 首个外部适配器在传输层限流/限长、映射厂商原生响应并调用同一验证器，还需供应商/模型/区域/SecretReference/租户策略、真实非法响应故障注入和 Run/Artifact 审计。完整证据见 `docs/space/reports/e13-s06-provider-output-validation-development.md`。
- 功能 tip `1b297a91` 已确认进入远端集成祖先链，本地/远端临时功能分支均已删除；可重建的 `tmp/e13-s06` 共 7 个文件、71,099 bytes（包含开发 HMAC key）已删除，源码、Schema、报告和远端 Git 历史保留，`main` 未修改。

## E13-S05 Mock/本地 Provider 与故障降级开发切片（2026-08-05）

- 在 E13-S04 集成基线 `454c521c` 上完成功能提交 `e519942b`、证据提交 `e55b49aa`，并以 no-ff 提交 `6bb43fc5` 集成到 `integration/space-v1-20260730`：新增确定性、无网络的 Mock、本地启发式和可重试故障降级实现，三者统一通过既有 `IWarehouseGenerationProvider` SPI。
- Mock 按稳定 SourceKey 和上限生成固定类型/0.5 置信度建议；Local 只读取 E13-S04 允许的脱敏 Layer/Block 分类令牌，确定性识别 Rack/Aisle/Wall/Column/Door/Dock/StaticEquipment/Zone/Floor，未命中显式诊断，不读取原始 CAD、属性值、SourceRef、路径或租户身份。
- 只有 Unavailable、Timeout、RateLimited 可降级到 Local，并追加稳定 Warning；ContractViolation 和用户取消不降级。异常不携带端点、凭据、响应体或内部异常；建议、关系和诊断继续受合同上限约束。
- 开发 CLI 支持 `mock|local|fallback-local`，在反序列化前拒绝未知 Schema/WarehouseKind，明确拒绝 `external`；生产 Registry 未注册开发 Provider，也没有凭据、网络、Run/Usage 持久化或 Draft 写入。
- 样例 13 的 22 个最小化特征产生 Mock 22 条、Local 21 条以及三类降级各 21 条建议；5 份结果共 106 条建议，未知输入/关系引用与范围违规均为 0，38 个敏感候选命中 0。Mock、Local、Timeout 降级重复运行均字节一致，全部运行报告 external=false、draft=false。
- 门禁：Provider 实现 + SPI 27/27、Space Unit 359/359、CAD 工具 25/25；完整 solution Release 非增量单线程、禁用节点复用构建 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变。首轮恰逢 Visual Studio 更新导致旧 iOS 26.2 SDK 目录缺失；工作负载自动恢复到 iOS 26.5 后同一命令无代码修改通过。格式和差异检查通过，本切片无数据库、Migration、WebApi、前端、OpenAPI 或 SDK 变化。
- 这是开发切片，不是正式 E13-S05 完成：首个外部适配器仍等待供应商/合同、区域与数据驻留、端点别名、SecretReference、租户外发授权、生产输入输出校验和计费证据。下一独立切片可先做 E13-S06 本地不可信输出校验，但不能借此启用外部 Provider 或应用建议。完整证据见 `docs/space/reports/e13-s05-provider-development.md`。
- 功能 tip `e55b49aa` 已确认进入远端集成祖先链，本地/远端临时功能分支均已删除；可重建的 `tmp/e13-s05` 共 13 个文件、125,056 bytes（包含开发 HMAC key）已删除，源码、报告和远端 Git 历史保留，`main` 未修改。

## E13-S04 CAD IR 特征最小化与脱敏开发切片（2026-08-05）

- 在 E04-S05 集成基线 `f5f0c9e8` 上完成功能提交 `8fffdf07`、证据提交 `d635796e`，并以 no-ff 提交 `8bc1114d` 集成到 `integration/space-v1-20260730`：parsing-ready CAD Coordinate Preparation 现在可确定性投影为 `MetadataOnly` 或 `StructuredFeatures` Provider 输入，并生成独立 local-only SourceRef 映射。
- MetadataOnly 只发送 HMAC SourceKey/图层/块/属性/重复/映射提示令牌、实体枚举/计数、10 度角度桶和无量纲长宽比桶；坐标、关系和 object-level locked facts 均禁止。StructuredFeatures 才允许四位小数的 0～1 相对包围盒、有界重复关系和声明枚举锁定事实；绝对坐标、属性值、CAD Text、身份/文件/存储信息始终不发送。
- Run correlation 与全部可识别令牌按 Run、domain 和 32～128 byte HMAC key 隔离；Layer/Block 只暴露仓库领域白名单分类，其余原文只进入 HMAC。Provider 输入不含 Tenant/Site/ModelVersion/Run/File/Source/Floor/SourceRef/源哈希；local map 明确 `isLocalOnly=true` 并以 Provider 文件 SHA 和自身规范 SHA 防篡改。开发 CLI 从二进制 key 文件读取，使用后清零，不调用 Provider、不写 Draft。
- 合成样例 13 的 22 个 SourceRef 在 MetadataOnly 下形成 8 个统计特征，在 StructuredFeatures 下形成 22 个特征、12 个相对包围盒和 14 条关系；38 个非空敏感候选外发命中 0，越界包围盒 0。Provider SHA 分别为 `c5fbdcf2...7d697efa` 与 `164020fa...8bc2b65`，Structured Provider/local map 重复运行均字节一致。
- 门禁：minimizer + Provider SPI 聚焦 27/27，功能树与 no-ff 合并树的 Space Unit 350/350、CAD 工具 24/24；两棵树完整 solution Release 非增量单线程构建最终均为 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变。合并态首轮在第三方 Kotlin 协程程序集的 Android x64 AOT 汇编器处瞬时失败，关闭 build server、禁用节点复用后原强度重跑通过，未修改代码或关闭 AOT；格式、两份 AI Schema JSON、工件哈希/反序列化/应用验证和差异检查通过。本切片无前端、数据库、API 或 SDK 变化。
- 这是开发切片，不是正式 E13-S04 生产验收：仍需生产 E02-S03 CAD Artifact、Tenant/Run/Policy/SecretReference 绑定、Artifact 保留、权限/审计和授权真实 CAD 覆盖。E13-S05 才调用 Provider/实现降级，E13-S06 才校验不可信输出；本切片不应用建议。完整证据见 `docs/space/reports/e13-s04-cad-feature-minimization-development.md`。
- 功能 tip `d635796e` 已确认进入远端集成祖先链，本地/远端临时功能分支均已删除；可重建的 `tmp/e13-s04` 共 9 个文件、68,361 bytes（包含开发 HMAC key）已删除，源码、Schema、报告和远端历史保留，`main` 未修改。

## E04-S05 CAD 问题列表与画布定位开发切片（2026-08-05）

- 在 E03-S04 集成基线 `3300d01b` 上完成功能提交 `2ac9472f`、证据提交 `5114307e`，并以 no-ff 提交 `bd4ab90a` 集成到 `integration/space-v1-20260730`：E02-S07 的 CAD diagnostics、Low/Rejected proposals 和可选 E03-S04 Excel Unmatched/Conflict/Error 行现在形成确定性、只读的 CAD Review Workspace。
- Workspace 绑定 Tenant、ModelVersion、Floor、Diagnostic/可选 Match、编辑器修订/内容/快照、Previous Workspace 与自身 SHA。跨身份、旧修订、同修订不同内容哈希、篡改、重复身份和非法位置失败关闭；消失的稳定 TrackingKey 标为 Resolved，返回时重新 Open，不改写上游事实。
- Design V1 可本地加载开发工件，按状态、严重度、类型、关键字和可定位性筛选；点击后优先按 LogicalId、其次按精确 SourceRef 选中对象，并让底图、设计对象层与问题覆盖层共享 pan/zoom 自动居中。零面积 CAD 实体仍有 18px 可见锚点；过期工件禁用定位。
- 样例 13 产生 29 项 Open：Info 12 / Warning 17 / Blocking 0，25 项可定位、4 项不可定位，Workspace `3a296228...17288eb`，JSON `29ff0014...3f6eeb3`（34,843 bytes），两次运行字节完全一致且无 `null` 字段。Low+locatable 返回 8 项，Open+Warning+locatable 返回 17 项。
- 门禁：E04-S05 应用 5/5、前端聚焦 15/15、Space Unit 341/341、CAD 工具 23/23、前端全量 126 files / 685 tests、类型检查和生产构建通过；功能树与 no-ff 合并树的完整 solution Release 非增量单线程构建均为 0 error / 10 条既有 warning，Desktop/Android AOT 强度不变。i18n 仍为既有 908 项缺失快照 key，本切片净新增 0。
- 这是开发切片，不是正式 E04-S05 验收：本地 JSON 导入不替代生产 CAD Artifact/Issue API、权限/审计、服务端权威签发、授权真实图纸或真实编辑器验收，也不写 Draft。E03-S05 的用户确认与幂等 Draft 写入仍受生产 CAD 链和并发修订门禁约束。证据见 `docs/space/reports/e04-s05-cad-review-workspace-development.md`。
- 功能 tip 已确认进入远端集成祖先链，本地/远端临时功能分支已删除；可重复生成的 `tmp/e04-s05` 与本工作树 `cp6.web/node_modules` 共清理 22,369 个文件、332,663,196 bytes（约 317.3 MiB），锁文件和源码保留，`main` 未修改。

## E03-S04 Excel/CAD 元素匹配开发切片（2026-08-04）

- 在 E02-S07 集成基线 `4d945b5d` 上完成功能提交 `2da39667`、证据提交 `02cdbcff`，并以 no-ff 提交 `b2a2320c` 集成到 `integration/space-v1-20260730`：E03-S03 规范化 Excel 货架行现在可与 CAD 语义提案和只读编辑器快照进行确定性匹配，显式给出 New/Update/Unchanged/Unmatched/Conflict/Error 及独立未匹配查询。
- 匹配只接受 CAD/编辑器 SourceRef 或受控货架码属性；多候选、来源不一致、跨楼层和多 Excel 行争用同一目标均失败为 Conflict。每行保留采用键、候选、差异、错误码、CAD 置信度、画布位置和独立证据 SHA；Tenant/ModelVersion/Floor、Excel 映射与工作簿投影、Semantic、Diagnostic、编辑器修订和顶层预览形成完整哈希链。
- 样例 13 的 10 行结果为 New 8 / Unmatched 1 / Error 1，8 条 New 全部可聚焦；Match Preview `c6ca3640...72a4c107`，JSON 文件 `369372e1...67acd951`（15,732 bytes），两次运行字节完全一致。未匹配、新建可定位和错误查询分别返回 1、8、1 条；因未匹配、错误和 Low CAD 候选，`CanConfirm=false`。
- 门禁：E03-S04 聚焦 8/8、E03-S03 预检回归 6/6、Space Unit 336/336、CAD 工具 23/23；功能树和 no-ff 合并树的完整 solution Release 非增量单线程构建均为 0 error / 10 条既有 warning，格式、Schema、类型验证、空字段省略和差异检查通过。
- 这是只读开发切片，不创建永久 LogicalId，不写 Draft/数据库/编辑器，不替代正式 CAD 适配器、生产 Artifact/持久化、权威编辑器快照服务、API/权限/审计、授权真实图纸或 UI 验收。E03-S05 继续负责用户确认和幂等 Draft 写入；生产链等待期间下一独立开发切片优先 E04-S05 问题/未匹配列表与画布聚焦。证据见 `docs/space/reports/e03-s04-excel-cad-matching-development.md`。

## E02-S07 CAD 语义证据与问题定位开发切片（2026-08-04）

- 在 E02-S06 集成基线 `68d59562` 上完成功能提交 `19b6c443`、证据提交 `2eee2081`，并以 no-ff 提交 `c792ea8c` 集成到 `integration/space-v1-20260730`：每个只读语义提案现在都带 SourceRef、采用规则、置信度分段、独立证据哈希和整数毫米画布位置；Mapping/Semantic 问题形成稳定、可筛选的空间索引。
- 诊断工件绑定 Tenant/Floor 及 Source、Transform、Inventory、Profile、Mapping、Semantic 全链 SHA-256；构建时重算语义链，错配与篡改失败关闭。Document/Layer/Block/Entity 四级定位显式区分可聚焦与不可聚焦，空图层保留 ID 但不伪造范围。
- 样例 13：Diagnostic Index `f0d18f95...17209448b`，JSON 文件 `aa04fc74...70eacdc0c`（46,892 bytes）；21 条提案证据为 High 13 / Review 0 / Low 8 / Rejected 0，21 条问题为 12 Info / 9 Warning / 0 Blocking，其中 17 条可聚焦、4 条真实空图层不可聚焦，重复运行字节完全相同。
- 门禁：E02-S07 聚焦 6/6、Space Unit 328/328、CAD 工具 23/23、合并后完整 solution Release 非增量单线程构建 0 error / 10 条既有 warning，受影响文件格式、Schema JSON 与差异检查通过。并行 Android AOT 曾瞬时崩溃，关闭残留构建节点后在不降低 AOT 强度的条件下通过。
- 这是开发切片而非正式 E02-S07 验收；尚无问题列表 UI/画布点击高亮、人工删除/合并/拆分、字段锁定或修正重放，也未写 Draft/数据库。正式验收仍等待授权原生 CAD 适配器、冻结 Worker、独立真实黄金集、生产持久化/API/权限/审计与精度/覆盖率证据。下一开发主线优先 E03-S04 Excel 行与 CAD/编辑器元素候选匹配，随后 E04-S05 消费本索引实现问题列表和画布定位。证据见 `docs/space/reports/e02-s07-cad-semantic-diagnostics-development.md`。

## E02-S06 CAD 基础语义解析器开发切片（2026-08-03）

- 在 E02-S05 集成基线 `b3c45a8f` 上完成功能提交 `c8e2ae87`、证据提交 `be32c9a7`，并以 no-ff 提交 `fdb210b4` 集成到 `integration/space-v1-20260730`：Prepared IR、Inventory、封存 Profile 与 Mapping Preview 现在形成同租户、全哈希绑定的失败关闭链，输出确定性只读语义提案，不创建永久 LogicalId，不写 Draft。
- 每个对象保留临时 `previewObjectId`、SourceRef/图层/块/属性、目标类型、采用规则、默认高度/厚度、整数毫米规范几何、置信度与选择状态；统一区分 Element/Zone/Aisle/Rack，覆盖 Wall、Column、Door、Dock、Zone、Aisle、Rack。零长度、零面积和不支持图元显式 Rejected，不静默丢弃。
- Block 规则逐引用检查属性，命中时优先于 Layer 且不重复；无真实块轮廓时保留 BlockInstance 仿射变换、置信度封顶 0.69 并告警，不伪造货架尺寸。阈值固定为 `>=0.90` 自动选中、`0.70–0.89` Warning 待确认、`<0.70` 候选展示；必需来源只有拒绝几何时 Blocking。
- 样例 13：Semantic Preview `e398d192...befc866`，JSON 文件 `75845d12...7202ea`；22 源对象中 21 提案、13 AutoAccepted / 8 Candidate / 0 Rejected、13 Confirmable / 13 Selected、8 Info / 8 Warning / 0 Blocking，重复运行字节完全相同。
- 门禁：E02-S06 聚焦 6/6、20/20 合成 CAD 完成语义链、CAD 工具 23/23、Space Unit 322/322、完整 solution Release 非增量构建 0 error / 10 条既有 warning、格式/Schema/差异检查通过。证据见 `docs/space/reports/e02-s06-cad-semantic-development.md`。
- 这是开发切片而非正式 E02-S06 验收；仍等待授权原生适配器、冻结 Worker、正式黄金集、生产 Artifact/持久化、复杂块/曲线证据和受权 Draft Apply。等待期间可继续 E02-S07 开发侧问题定位与锁定修正预览，不得提前声称正式 CAD 验收。

## E02-S05 CAD 图层映射方案开发切片（2026-08-03）

- 在 E02-S04 集成基线 `f4b596f0` 上完成功能提交 `2736427c`、证据提交 `29118c19`，并以 no-ff 提交 `b6d58a1e` 集成到 `integration/space-v1-20260730`：新增 Definition SHA-256 封装的不可变 CAD Mapping Profile；System 方案无租户、租户侧只读，租户复制记录 System 基线，后续修改创建新版本。Tenant Profile 跨租户失败关闭。
- Layer/Block 规则支持 Exact、Glob、受限 NonBacktracking Regex 和 Block 属性条件，冻结目标语义、几何规则、默认高度/厚度、置信度、优先级和必需标记。逐层 Override 优先；同优先级/特异性多命中 Blocking；必需来源缺失或为空 Blocking；空/未知来源仍完整列出。
- Preview 绑定 Tenant、Profile/Version/Definition、Source、Inventory、源结构、Override、Reuse Key 和 Preview SHA-256；复用键排除 Floor/坐标 Transform，因此同一 CAD 换楼层仍复用，但不同租户/方案/来源/覆盖不会串用。新命令 `seal-dev-mapping-profile`、`preview-dev-mapping` 不写 Draft，无 Migration/API/外部 AI。
- 样例 13：Profile `732eef8a...de59d1`，Structure `9636bd72...0911ab`，Reuse `014cdc75...1c879b`，Preview `98a0a315...8009ca`；15 图层中 10 mapped / 5 unmapped，1/1 块 mapped，覆盖 21 个图层对象和 8 个块引用，4 Info / 1 Warning / 0 Blocking，可进入开发侧语义解析。
- 门禁：E02-S05 聚焦 12/12、20/20 合成 CAD 标准方案预览、CAD 工具 23/23、Space Unit 316/316、完整 solution Release 非增量构建 0 error / 10 条既有 warning、JSON/CLI/差异检查通过。证据见 `docs/space/reports/e02-s05-cad-mapping-development.md`。
- 这是开发切片而非正式 E02-S05 验收；仍等待授权原生适配器、正式持久化清单/方案、并发、API/权限/审计/UI 和真实图纸证据。E05-S01 已完成，等待期间可继续 E02-S06 开发侧只读语义提案，不得直接写 Draft。

## E02-S04 CAD 图层与块清单开发切片（2026-08-03）

- 在 E02-S03 集成基线 `01a59696` 上完成功能提交 `b77faf96`、证据提交 `324c8755`，并以 no-ff 提交 `be639d07` 集成到 `integration/space-v1-20260730`：CAD IR v1 向后兼容增加图层颜色、线型和可见性；开发 DXF 转换器保留完整 `TABLES/LAYER` 和空图层，未声明图层显式合成并产生 Warning，不再只列出有对象图层。
- 新增来源/坐标 Transform/Floor/Inventory SHA-256 绑定的确定性清单：图层对象/支持/不支持/类型/块引用/属性数与范围，块定义/XRef/引用/属性摘要，以及每个块引用的稳定 SourceRef、受控属性值和范围。非 Ready、Blocking、来源/楼层/范围或坐标元数据不一致均失败关闭，无 Migration/WebApi/Draft 写入。
- 图层、块和引用支持受限分页查询，覆盖名称/ID、显隐、图元类型、XRef、图层、块名和属性键值；单页最多 200。开发工具新增 `build-dev-inventory` 与 `query-dev-inventory`，合同见 `docs/space/contracts/cad/v1/inventory.schema.json`。
- 样例 13：Source `aa573f04...1fb106`，新 CAD IR `b6aa6501...614310`，Transform `b1223a8f...353cfba`，Inventory `63432958...9697a9`；F01 范围 `(0,-1200)～(36000,24000)` mm，15 图层/7 空层、1 块、8 个带属性块引用、22 supported 对象；`RACK_ID=R-01-01` 精确查询返回 `H:110`。
- 门禁：E02-S04 聚焦 10/10、20/20 合成 DXF 清单链、CAD 工具 22/22、Space Unit 304/304、完整 solution Release 非增量构建 0 error / 10 条既有 warning、JSON/CLI/差异检查通过。证据见 `docs/space/reports/e02-s04-cad-inventory-development.md`。
- 这是开发切片而非正式 E02-S04 验收；仍等待授权原生适配器、冻结 Worker、正式黄金集、生产 streaming/持久化/API/权限/UI 与真实复杂图纸证据。等待期间可继续 E02-S05 开发侧图层映射方案。

## E02-S03 CAD 坐标确认开发切片（2026-08-03）

- 在 E02-S02 开发 CAD IR 集成基线 `97d6871f` 上完成功能提交 `09b26b87`、证据提交 `d78b3b09`，并以 no-ff 提交 `7741da61` 集成到 `integration/space-v1-20260730`：分析阶段分别给出源单位范围和建议毫米范围；已识别单位仍必须明确确认，未知单位不猜测，确认记录绑定来源 SHA-256。
- 确认合同冻结源原点、目标 Floor 原点、逆时针 Z 旋转、Floor LogicalId/Code/Level/Elevation、边界与 `LOCAL_MM_Z_UP`。变换可纠正错误检测比例，点、半径、偏移和边界按 AwayFromZero 量化为整数毫米；普通图元保持 Identity，块引用复合实例矩阵；同一输入产生稳定 Transform SHA-256。
- 默认图纸单边 1 m～5 km；边界缺失、范围异常、未确认单位、错误来源哈希、非法楼层坐标系或超出楼层边界 50 mm 均失败关闭。DWG/DXF `SpaceModelSource` 缺少规范坐标元数据时不能进入 Parsing；既有 Excel/底图/编辑器路径不受影响，无 Migration。
- 20/20 合成 DXF 完成转换、分析、确认和楼层准备。样例 13 归属 F01，22 图元、0 问题，范围 `(0,-1200)～(36000,24000)` mm，Transform SHA-256 为 `b1223a8f...353cfba`。
- 门禁：E02-S03 聚焦 13/13、CAD 工具 20/20、Space Unit 294/294、完整 solution Release 0 error / 10 条既有 warning，最终 SDK 可访问增量构建 0 warning / 0 error，JSON/CLI/差异检查通过。证据见 `docs/space/reports/e02-s03-cad-coordinate-development.md`。
- 这是开发切片而非正式 E02-S03 验收；仍等待授权原生 DWG/DXF 适配器、冻结 Worker、正式黄金集和同租户/同版本持久化服务链。等待期间可继续 E02-S04 开发侧图层/块清单。

## E02-S02 CAD IR 开发契约（2026-08-02）

- 在合成 CAD 图纸集成基线 `08fe896a` 上完成第一段可执行 CAD IR 链路：功能提交 `89759cec`、验证文档 `8f3e9252`、no-ff 受控集成 `9e8cf4af`。Contracts 定义供应商中立的 CAD IR v1，Application 定义 `ICadConverter`、只写 streaming sink 接口和失败关闭契约验证器；WebApi、Draft 仓储和供应商 SDK 类型不跨越该边界。
- 新增开发命令 `convert-dev-ir`，可把 UTF-8/ASCII DXF 转换为确定性 JSON IR；验证精确来源 SHA-256、稳定 sourceRef、图层/块/图元计数、坐标边界和转换器身份，支持毫米/厘米/米/英寸/英尺归一化，未知单位 Blocking，不支持图元显式保留并报问题，XRef 原始路径不出边界。
- 20/20 合成图纸转换通过：130 个图层记录、23 个块、292 个图元，其中 278 个受支持、14 个不支持且全部有显式问题、缺失 sourceRef 为 0。样例 13 产生 8 层、1 块、22 图元，IR SHA-256 为 `f080ac0c...20a9ba`。
- 门禁：CAD 实验工具 19/19、CAD IR 契约聚焦 9/9、Space Unit 281/281、完整 solution Release 0 error / 10 条既有 warning、`git diff --check` 通过。证据见 `docs/space/reports/e02-s02-cad-ir-development-contract.md`。
- 这是 E02-S02 开发切片，不是正式验收：仍等待 E02-S01 的原生 DWG 适配器/商业授权、冻结隔离 Worker、独立正式黄金集和生产规模 streaming/压力证据；完成供应商选择后再接同一契约并进入 E02-S03。

## E02 合成开发 CAD 图纸包（2026-08-02）

- 新增 `docs/space/acceptance/development-v2.0.0`：20 份仓库内可重复生成的合成 DXF，L1～L5 各 4 份，覆盖 AC1009/AC1015/AC1021/AC1027/AC1032 文件头以及规则、多楼层、非正交、综合和噪声场景。
- 新命令 `generate-dev-corpus` 同步生成 SHA-256 清单、场景索引、最小期望答案、期望问题、Provider IR、图层映射和开发使用声明。全部资产不含真实客户、供应商、地址、人员、标题栏或设备序列号数据。
- CAD 实验工具测试 12/12；20/20 文件完整性、哈希、成对 DXF 行、EOF、唯一 Handle、五类布局与 DXF 文件头矩阵通过。
- 该数据包明确为 `DevelopmentSeed` 且 `countsTowardReleaseGate=false`：可推进解析、映射、问题、IR、UI 和回归开发，但不替代原生 DWG、ODA/APS 授权、冻结 Worker 和独立正式黄金集。证据见 `docs/space/reports/e02-synthetic-development-cad-corpus.md`。

## E12-S05 完成状态（2026-08-02）

- E12-S05 已完成实现、全量门禁、远端备份、no-ff 受控集成和临时资源清理：起始基线 `ad77540d`，功能提交 `dd505f6f`，集成提交 `c4b139ab`。内部规划人员可从 Ready/Succeeded/Production Isolated 场景下载 glTF 2.0 单文件 `.glb`。
- 导出在 Serializable 一致性快照中稳定排序楼层、区域、巷道、货架、货架层、库位和通用元素，总数据节点上限 50,000；同一快照字节与 SHA-256 确定。CP6 `LOCAL_MM_Z_UP` 毫米坐标固定转换为 glTF `+Y` 向上、米制坐标。
- 货架、可定位库位和盒体元素提供共享网格、面法线与材质；边界、多边形、中心线、货架层规格和稳定 LogicalId 进入 `extras.cp6`。这是一份低成本可视化交换包，不是 CAD authoring、DWG 回写或生产发布入口。
- 新增 1 个 GET API、1 个只读权限和四个五语页面词条；Design V1 从 83 增至 84 operations，C#/TypeScript SDK 已同步。响应带 no-store、nosniff、ETag、schema 与 SHA-256 头，且不含库存、人员、设备事件或历史任务运行态。
- 全量门禁：Space Unit 272、默认 Space Integration 247 passed / 63 SQL-gated skipped、CP6.Tests 最终复验 2777 passed / 17 environment-gated skipped、前端 123 files / 676 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning；生产构建、双 EF、SDK drift 与 TypeScript strict no-emit 全部通过。证据见 `docs/space/reports/e12-s05-standard-gltf-exchange.md`。
- 合并态 GLB 2/2、权限/API/OpenAPI 65/65、前端同树 9/9、双 EF 与 SDK 门禁通过。功能 tip 已进入远端集成祖先链，远端/本地功能分支和功能工作树已删除，释放 2,869,523,790 字节（约 2.672 GiB）。E12-S06 仍等待正式黄金样本、DWG SDK/供应商授权与可审计试验环境；`main` 未修改。

## E12-S04 完成状态（2026-08-02）

- E12-S04 已完成实现、全量门禁、远端备份和 no-ff 受控集成：起始基线 `6d13d7da`，功能提交 `7b919b4b`，文档 tip `a9298bad`，集成提交 `577168e3`。内部规划人员可固定 2～10 个不同生产隔离场景的同源仿真证据，人工指定基线，并查看距离、拥堵、容量、吞吐和成本的原值、差值及显式阈值风险。
- 比较强制同 Site/Model/基础 Published 版本、来源数据哈希、历史窗口、任务口径、仿真定义、几何、币种、费率和吞吐窗口；容量假设差异显式标记。系统不计算总分、不排名、不推荐，也不预选决策方案。
- 人工决策只允许 Selected/Deferred/RejectedAll 和必填理由；后续记录必须替代唯一当前链头。比较、风险和决策全部追加式/不可变，永不合并、写入或发布到生产。
- 新增 6 个 planning API、4 个权限、四张租户隔离证据表、EF Migration/增量幂等 SQL；Design V1 从 77 增至 83 operations，C#/TypeScript SDK 已同步。规划页新增跨分支比较矩阵、风险与决策历史。
- 47 个比较词条均有五语运行时种子，静态 i18n 欠账仍为既有 908 项，本卡净新增 0。
- 全量门禁：Space Unit 272、默认 Space Integration 245 passed / 63 SQL-gated skipped、CP6.Tests 2775 passed / 17 environment-gated skipped、前端 123 files / 674 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning；生产构建、双 EF、SDK drift 与 TypeScript strict no-emit 全部通过。证据见 `docs/space/reports/e12-s04-scenario-comparison-decision.md`。
- 合并树与功能 tip 一致，合并态引擎 4/4、服务 3/3、权限/合同/OpenAPI/五语 66/66、前端 10/10、类型检查、双 EF、SDK 和 TypeScript 门禁通过。功能 tip 已确认进入远端集成祖先链，功能工作树、临时依赖链接及本地/远端功能分支已删除，释放 2,520,628,564 字节（约 2.348 GiB）。下一张可独立实施 E12-S05“标准交换格式导出”，`main` 未修改。

## E12-S03 完成状态（2026-08-02）

- E12-S03 已完成实现、全量门禁、远端备份和受控集成：起始基线 `1650e8ba`、功能提交 `ab21aed4`、文档 tip `2cd1faed`、no-ff 集成 `f2d68897`。内部规划人员可在生产隔离场景中基于不可变脱敏历史数据集运行确定性规划仿真。
- 距离使用同层货架格口锚点直线距离并显式报告未知覆盖；拥堵按目的位置历史执行区间重叠；容量使用调用方声明任务数量单位；吞吐使用精确历史时长和固定时间桶；人工按 worker token 区间并集；成本只使用显式距离/人工/拥堵单价。
- 新增 3 个 planning API、2 个权限、两张不可变租户隔离证据表、EF Migration/增量幂等 SQL；Design V1 从 74 增至 77 operations，C#/TypeScript SDK 已同步。规划页可配置容量、时间桶、币种和单价，并展示五类 KPI、热点、结果哈希与无生产回写护栏。
- 41 个仿真词条均有五语运行时种子。静态 i18n 欠账仍为既有 908 项，本卡净新增 0。
- 全量门禁：Space Unit 268、默认 Space Integration 242 passed / 63 SQL-gated skipped、CP6.Tests 2771 passed / 17 environment-gated skipped、前端 122 files / 670 tests、完整 solution 非增量 Release 0 error / 3 条既有 warning；生产构建、双 EF、SDK drift 与 TypeScript strict no-emit 全部通过。证据见 `docs/space/reports/e12-s03-planning-simulation.md`。
- 本卡不做巷道路由、实时交通、高精度物理求解、财务实际、方案排名或生产回写。合并态引擎 4/4、服务 3/3、权限/合同/OpenAPI/五语 65/65、前端 7/7 及剩余一致性门禁通过。功能 tip 已确认进入远端集成祖先链，功能工作树及本地/远端临时分支已删除，释放 2,182,809,248 字节（约 2.03 GiB）。下一张可独立实施 E12-S04“多场景比较与决策记录”，`main` 未修改。

## E12-S02 完成状态（2026-08-02）

- E12-S02 已进入远端受控集成基线：数据/时钟/迁移 `4fb6941d`、API/UI/权限/SDK `d89919b8`、no-ff 集成 `c8ccbf56`。内部规划人员可向 E12-S01 克隆成功且生产隔离的场景导入最多 10,000 条不可变历史任务，并以确定性回放时钟映射历史 UTC 时间。
- 合同只接受 64 位 SHA-256 task/worker token 和调用方不可逆脱敏确认，不含订单、人员、物料或 SKU 原始标识字段；所有任务位置必须存在于场景固定快照，数据集与任务不可修改/删除且永不回写生产。
- 新增 3 个 planning API、2 个权限、两张租户隔离证据表、EF Migration/增量幂等 SQL；Design V1 从 71 增至 74 operations，C#/TypeScript SDK 已同步。规划页仅对 Ready/Succeeded/Isolated 场景开放 JSON 导入、列表和回放证据读取。
- 25 个数据集词条及 3 个场景入口词条均有五语运行时种子。静态 i18n 欠账仍为既有 908 项，本卡净新增 0。
- 全量门禁：Space Unit 264、默认 Space Integration 239 passed / 63 SQL-gated skipped、CP6.Tests 2767 passed / 17 environment-gated skipped、前端 121 files / 667 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning、生产构建、双 EF、SDK drift 与 TypeScript strict no-emit 全部通过。合并态领域 3/3、服务 4/4、权限/契约/OpenAPI/种子 64/64、前端 5/5 及剩余门禁通过。证据见 `docs/space/reports/e12-s02-deidentified-history-replay-clock.md`。
- 功能 tip 已先远端备份并确认进入远端集成祖先链；随后删除功能工作树及本地/远端临时分支，释放 2,177,363,070 字节（约 2.03 GiB）。历史由远端受控集成分支完整保留，`main` 未修改。

## E12-S01 完成状态（2026-08-02）

- E12-S01 已进入远端受控集成基线：隔离模型/迁移 `c673b7ec`、功能 `8d75e79e`、no-ff 集成 `0ac603d4`、五语收口 `3d41c8d9`。新增内部生产隔离规划分支，固定当前生产 Published 快照，可并存且不占生产 Draft/Published 指针。
- 场景版本具有独立 `PlanningScenario` purpose，领域与数据库双重拒绝其进入生产发布生命周期；固定基础版本后即使生产版本变为 Superseded，异步 Worker 仍克隆原快照，不自动追随或合并生产变化。
- 新增 PUT/GET/list 场景端点、调用方 UUID + payload hash 幂等、不可变分支证据、租户复合外键、迁移与增量幂等 SQL；Design V1 从 68 增至 71 operations，C#/TypeScript SDK 已同步。
- `/space/planning` 提供站点选择、创建、固定血缘、版本、克隆任务、隔离状态和自动轮询；20 个页面词条已补齐五语运行时种子。i18n 静态欠账仍为既有 908 项，本卡净新增 0。
- 最终门禁：Space Unit 261/261、默认 Space Integration 235 passed / 63 SQL-environment skipped、CP6.Tests 2763 passed / 17 environment-gated skipped、前端 120 files / 664 tests、完整 solution Release 0 error / 10 条既有 warning、生产构建、两个 EF Context、SDK drift 与 TypeScript strict no-emit 全部通过。合并态聚焦复验与五语聚焦测试通过。交付证据见 `docs/space/reports/e12-s01-production-isolated-scenario-branch.md`。
- 功能 tip 先远端备份并确认进入远端集成祖先链，随后已删除功能工作树及本地/远端临时分支，释放 2,877,403,216 字节（约 2.68 GiB）。历史由远端受控集成分支完整保留，`main` 未修改。

## E11-S06 完成状态（2026-08-02）

- E11-S06 已进入本地受控集成基线：合同 `46884878`、功能 `f10b4b54`、文档 `f50ce454`、no-ff 集成 `d49fe1d0`。新增只读批次效果评估，组合 E11-S03～S05 的建议、审批、分派回执、当前执行事实和指定 Published 版本几何，不新增评估表或写操作。
- 看板提供推荐→选择→回执→开始→完成/关注/补偿漏斗、显式比率与带样本数的时长。任务按 `TaskId`、人员按 `SourceId + ExternalId` 稳定排序形成同一获批队列的计划几何反事实；样本不足、锚点不完整、跨层或原始距离约束不满足时整项不可用。
- 实际路线节省、吞吐提升和货币收益因缺少任务轨迹、历史控制与成本归因基线而固定不可用；回退结果如实显示，时间证据不完整或无效时排除样本并明示 limitation。响应不含姓名、邮箱、内部 `UserId`、`AssignedTo` 或逐任务收益明细。
- Viewer 新增效果看板、手动刷新、来源时点、样本量、计划改善/持平/回退和收益边界；新建议、新审批、关闭与卸载会使旧响应失效。28 个五语键使快照从 4,587 增至 4,615；i18n 仍有 908 项既有欠账，本卡净新增缺失为 0。
- 功能分支门禁：Space Unit 258/258、默认 Space Integration 232 passed / 62 SQL-environment skipped、CP6.Tests 2759 passed / 17 environment-gated skipped、前端 118 files / 660 tests、完整 solution Release、生产构建、两个 EF Context、SDK drift、TypeScript SDK strict no-emit、OpenAPI surface 与差异检查通过。合并态复验：引擎 9/9、服务 2/2、权限/合同/种子 7/7、前端 23/23及类型、EF、SDK/OpenAPI 与差异门禁通过。交付证据见 `docs/space/reports/e11-s06-outcome-evaluation.md`。
- 功能 tip `f50ce454` 先完成远端备份，再确认其为本地/远端一致的清理前集成状态 `fc123f5d` 的祖先；随后已删除功能工作树及本地/远端临时分支，共释放 2,707,376,655 字节（约 2.52 GiB），历史由远端受控集成分支完整保留。`main` 未被本轮操作修改。

## E11-S05 完成状态（2026-08-02）

- E11-S05 已进入受控集成基线：合同 `139c76b5`、功能 `e8df8288`、文档 `a0b247ab`、no-ff 集成 `cf35849c`。新增审批批次实时执行状态、持久化动作账本、调用方 UUID + payload hash 幂等、最多 3 次人工重试与整批未开始任务的安全补偿。
- 三层重放保护覆盖 OA 回调、任务适配器回执和执行动作；精确重放返回原结果，部分/冲突回执失败关闭。重试每次重新验证 Published、不可变建议、人员实时性与空闲、内部映射、WMS 范围及任务并发事实。
- 补偿仅在整批任务仍 Pending、仍为原分派人、执行版本未变、从未开始/完成且原始回执完整一致时撤销 `AssignedTo`；不修改执行版本或结果，不认领/启动/完成任务，不修改库存/订单，不发出 WCS/PDA 命令。Migration 为 `20260802192420_SpaceE11S05ExecutionReceiptsCompensation`。
- Viewer 新增执行聚合状态、逐任务事实、动作历史、重试余额、补偿阻断码及显式原因输入，并阻止旧异步响应覆盖。28 行五语种子中 26 个新键使快照从 4,561 增至 4,587；i18n 仍有 908 项既有欠账，本卡净新增缺失为 0。
- 功能分支门禁：Space Unit 249/249、默认 Space Integration 230 passed / 62 SQL-environment skipped、CP6.Tests 2757 passed / 17 environment-gated skipped、前端 118 files / 658 tests、完整 solution Release、生产构建、EF/SDK drift、TypeScript SDK strict no-emit 与差异检查通过。合并态复验：服务/适配器 14/14、权限/合同/种子 35/35、前端 21/21、类型、SDK drift、EF pending model 与差异检查通过。交付证据见 `docs/space/reports/e11-s05-execution-receipts-compensation.md`。
- 功能分支先推送远端备份；确认功能 tip `a0b247ab` 是本地/远端一致的集成状态 tip `17d6a3e0` 的祖先后，已删除功能工作树及本地/远端临时分支。共享依赖目标保留，本轮释放 D 盘 2,170,007,351 字节（约 2.02 GiB），历史由远端受控集成分支完整保存。

## E11-S04 完成状态（2026-08-02）

- E11-S04 已进入受控集成基线：合同 `098fb54b`、功能 `a7298e28`、文档 `a552d05d`、no-ff 集成 `c19231db`。新增内部 PUT/GET/cancel 调度审批资源、调用方 UUID 幂等、OA BizType `SPACE_DISPATCH_ASSIGNMENT`、提交/读取/取消权限与审计；提交人与最终审批人严格分离。
- 审批请求冻结 E11-S03 建议哈希、Published/仓库、选中 rank、任务并发、人员真实身份与双时点以及内部用户映射；对外不返回人员姓名、邮箱或内部人员 `UserId`。最终通过前重新验证全部事实，任一漂移整批进入 `Stale` 或 `FailedNoEffect`，不产生部分写入。
- `cp6-mobile-task-assignment-v1` 只分配现有 Pending 且未分派的真实 `MobileTask`，完整预检、任务分配、事件和回执在同一工作单元中提交；不认领、不启动、不修改库存/订单、不伪造 WCS，也不另建 PDA 事实源。Migration 为 `20260802184419_SpaceE11S04DispatchApproval`。
- Viewer 新增显式选择、理由、提交、刷新、取消、状态与回执，并以请求版本阻止旧响应覆盖。21 行五语种子使唯一键快照从 4,542 增至 4,561；i18n 历史缺失由 909 降至 908，本卡没有新增缺失键。
- 功能分支门禁：Space Unit 249/249、默认 Space Integration 224 passed / 62 SQL-environment skipped、CP6.Tests 2757 passed / 17 environment-gated skipped、前端 118 files / 656 tests、完整 solution Release 与前端生产构建、EF/SDK drift、两个 TypeScript strict no-emit 和差异检查通过。合并态冒烟：审批服务/适配器 8/8、权限/合同/种子/基础设施 44/44、前端 19/19、类型、SDK drift 与 EF pending model 通过。交付证据见 `docs/space/reports/e11-s04-dispatch-approval-adapter.md`。
- 功能分支先推送远端备份；确认功能 tip `a552d05d` 是本地/远端一致的集成状态 tip `b317dfa5` 的祖先后，已删除功能工作树及本地/远端临时分支。共享依赖目标保留，本轮释放 D 盘 2,190,180,352 字节（约 2.04 GiB），历史由远端受控集成分支完整保存。

## E11-S03 完成状态（2026-08-02）

- E11-S03 已进入受控集成基线：合同 `3cf42534`、功能 `419d3f6c`、文档 `eea62de0`、no-ff 集成 `cf7bf778`。新增内部 PUT/GET 人员调度建议资源、调用方 UUID 幂等、不可变推荐证据和 `space-dispatch-v1` 定义；不审批、不分配、不认领、不启动、不修改任务或人员，也不向 WMS/WCS/PDA 写入。
- 任务只来自当前 CP6 `MobileTask` 的 Pending 且未分配事实，首个可行动位置固定优先 From、缺失时 To，并携带 ContractVersion/ExecutionVersion/RowVersion。人员必须同时具备新鲜位置与工作状态、严格 Idle，默认排除 Simulated；所有任务、人员和 Published 身份越界均失败关闭。
- 匹配先用 Hopcroft–Karp 保证最大基数，再做确定性最小成本；配对乘积上限 100,000，返回上限 100。证据分别保存任务/人员/配对首因排除、最多 100 个样例、匹配容量、截断和限制说明；几何距离不冒充通道路线、时间或 SLA。
- Migration `20260802180049_SpaceE11S03DispatchRecommendations` 新增租户隔离的不可变证据表、Published 复合外键、计数/JSON/哈希约束和索引。Viewer 新增手动 `DSP` 面板，与 KPI/DIAG/PUT 互斥，展示来源、任务并发、人员双时点、建议与排除证据，并支持任务首端定位。
- 功能分支门禁：Space Unit 249/249、默认 Space Integration 216 passed / 62 SQL-environment skipped、CP6.Tests 2752 passed / 17 environment-gated skipped、前端 118 files / 653 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning、EF/SDK drift、两套 TypeScript strict no-emit、生产构建和差异检查通过。合并态冒烟：引擎/运行时合同 6/6、服务/适配器 6/6、权限/审计/API/种子 23/23、前端 16/16、类型与 SDK drift 通过。42 个五语键使快照到 4,542；i18n 历史缺失仍为 909，本卡净新增 0。交付证据见 `docs/space/reports/e11-s03-dispatch-recommendations.md`。
- 功能分支先完成远端备份，再确认其 tip `eea62de0` 是本地/远端一致的集成状态 tip `7e627624` 的祖先；随后已删除功能工作树及本地/远端临时分支，共释放 D 盘 2,528,428,032 字节（约 2.35 GiB），共享依赖目录保留，历史由受控集成分支完整保存。

## E11-S02 完成状态（2026-08-02）

- E11-S02 已进入受控集成基线：合同 `3ccd2936`、功能 `644293f1`、文档 `034a1b1b`、no-ff 集成 `a2b47826`。新增内部 PUT/GET 上架推荐资源、调用方 UUID 幂等、不可变推荐证据和 `space-putaway-v1` 定义；不预留、不移动库存、不创建任务，也不向 WMS/WCS/PDA 写入。
- 候选只使用当前 Published/Active 空间模型和一致的当前 WMS 库存/活动任务来源。精确合并要求显式货主与批次及全部正库存逐行完全匹配，否则只推荐空库位；返回稳定 rank、规则命中、九类首因排除计数和最多 100 个样例，几何距离不冒充路线距离，入库数量不冒充容量。
- Migration `20260802172258_SpaceE11S02PutawayRecommendations` 新增租户隔离的不可变证据表、复合 Published 外键、计数/JSON/哈希检查约束和查询索引。Viewer 新增手动 `PUT` 面板，与 KPI/DIAG 互斥，支持当前楼层、候选/排除定位、旧响应失效和失败保留上次成功结果。
- 功能分支门禁：Space Unit 245/245、默认 Space Integration 211 passed / 62 SQL-environment skipped、CP6.Tests 2748 passed / 17 environment-gated skipped、前端 117 files / 648 tests、完整 solution 非增量 Release 0 error / 10 条既有 warning、EF/SDK drift、TypeScript SDK strict no-emit、生产构建和差异检查通过。合并态冒烟：引擎 5/5、服务 6/6、权限/审计/契约/种子 34/34、前端 14/14、类型与 SDK drift 通过。42 个五语键使快照到 4,500，i18n 历史缺失由 911 降至 909，本卡净新增 0。交付证据见 `docs/space/reports/e11-s02-putaway-recommendation-candidates.md`。
- 远端备份、祖先关系与集成本地/远端一致性验证后，已删除 E11-S02 功能工作树及本地/远端临时分支；共享依赖目录保留。本轮释放 D 盘 2,187,710,464 字节（约 2.04 GiB），功能历史由远端受控集成分支完整保留。

## E10-S06 完成状态（2026-08-02）

- E10-S06 已进入受控集成基线：合同 `bffe1877`、实现 `0676ba4a`、文档 `969e7c38`、no-ff 集成 `5f86edcb`。新增只读 `GET /api/space/design/v1/sites/{siteId}/runtime/overview`，只汇总当前 Published/Active 模型，ABC 窗口限定为 1～365 个完整自然日；库存、作业和 ABC 保持独立来源、观察时间和部分可用语义。
- 楼层面积使用毫米边界鞋带公式，缺失任一活动楼层面积时不伪造站点总面积；货架占地率只表达建模足迹。占用率按正库存物理库位计算；因没有容量主数据，容量利用率固定为空并给出 `WMS_LOCATION_CAPACITY_NOT_AVAILABLE`。库存不跨单位合计，活动任务数/Stop 数不冒充吞吐量。
- ABC 只使用正数 OUT 事实，按出库量和物料稳定排序，以排名前累计占比 `<80%`/`<95%` 划分 A/B/C；有当前库存但无正出库事实的 SKU 明确为 Unclassified。Viewer 新增 KPI/异常/逐层总览和固定 ABC 颜色，ABC、库存空间筛选、作业热图三个颜色权威互斥，请求版本阻止旧响应覆盖。
- Design V1 从 67 增至 68 operations，C#/TypeScript SDK 已同步，无数据库 Migration。功能分支全量门禁：Space Unit 236/236、默认 Space Integration 198 passed / 62 SQL-environment skipped、本卡真实 SQL 3/3、CP6.Tests 2739 passed / 17 environment-gated skipped、前端 115 files / 639 tests、完整 solution 0 error / 10 条既有 warning、EF/SDK drift 和生产构建通过。合并态冒烟：合同 23/23、Runtime/适配器 81/81、权限/OpenAPI 46/46、前端 25/25、类型检查和 SDK drift 通过。
- i18n 快照仍未绿色：集成基线已有 881 项，本卡新增 30 项，共 911 项；没有篡改生成快照掩盖技术债。E10 P2 S01～S06 至此均有完成证据。CAD/E06 主链继续等待正式黄金集、授权供应商证据和冻结 Worker；下一张独立实施卡须按依赖重新选择，不能把快照口径直接扩写成趋势、推荐或执行控制。

## E10-S05 完成状态（2026-08-02）

- E10-S05 已进入受控集成基线：实现 `65c59555`、文档 `53bea9b9`、no-ff 集成 `e270c2cc`。复用 `GET /api/space/design/v1/sites/{siteId}/runtime/inventory/locate`，新增可选货主条件；货主、SKU、批次和容器至少一个，多个条件固定精确 AND，货主在服务边界规范为大写。
- WMS 继续是库存业务事实源；服务端重新验证适配器返回的正库存及全部筛选条件，越界结果以 502 失败关闭。CP6 适配器通过仓库、库位、SKU、批次的唯一库存业务键为容器取得货主，不向 Design Revision 复制或在浏览器猜测业务事实。
- 3D Viewer 新增可持续库存空间筛选：命中库位琥珀色、当前层未命中库位压暗，展示本层/全站/分层数量和来源证据；筛选跨库存轮询与楼层切换保持，清除后恢复库存模式，并以请求版本阻止过期响应覆盖。库存筛选与作业热图互斥，原有一次性定位仍保留。
- Design V1 保持 67 operations，C#/TypeScript SDK 已同步，无数据库 Migration。门禁：运行合同 2/2、Runtime/适配器 68/68、权限/OpenAPI 45/45、前端聚焦 22/22、前端全量 114 files / 632 tests、Space Unit 236/236、默认 Space Integration 190 passed / 61 SQL-environment skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning，EF/SDK/TypeScript/差异门禁通过；本卡真实 SQL 1/1。完整真实 SQL 矩阵 250 passed / 1 个已独立复现的 Excel 预检种子循环依赖基线失败。
- E10-S06“仓库 KPI 快照、利用率与 ABC 口径”是下一张具备前置条件的 P2 卡。CAD/E06 主链仍等待正式黄金集、授权供应商证据和冻结 Worker 等外部输入；本卡不改变该优先级或失败关闭边界。

## E10-S04 完成状态（2026-08-02）

- E10-S04 已进入受控集成基线：实现 `9a9802a8`、文档 `f961d7e5`、no-ff 集成 `b4d5b81e`。新增当前设备读取 `GET /api/space/design/v1/sites/{siteId}/devices`，沿用 `space:model:read`，支持来源/设备/状态/楼层/活动告警过滤与受保护游标；外部主体在读库前拒绝。
- `Space_DeviceState` 以独立位置/运行状态游标维护设备当前投影，`Space_DeviceAlarmState` 以设备+外部告警身份维护显式 Raise/Clear 生命周期。迟到事件继续追加台账并返回 `AcceptedStale`、`ProjectionApplied=false`，但不回退投影或重新激活已被较新 Clear 关闭的告警；台账和投影在同一 Serializable 事务中提交。
- 当前读取包含无事件的 Unknown 映射、当前 Published 映射有效性和锚点、来源位置/状态证据、5 分钟独立新鲜度、Real/Simulated 以及活动告警严重度与事件证据。Migration `20260802144027_SpaceE10S04DeviceRuntime` 新增两个投影表、rowversion、租户复合外键、唯一索引、检查约束和身份写保护。
- 3D Viewer 已移除旧设备演示接口调用：只绘制活动楼层，来源 XYZ 优先，缺失时仅回退当前 Published 映射元素锚点；状态色、模拟线框、过期透明度和活动告警环均显式呈现，Three.js userData 保留映射/来源/位置/状态/告警证据，切层、关闭和卸载会释放 GPU 资源。
- Design V1 从 66 增至 67 operations，C#/TypeScript SDK 已同步。门禁：领域 2/2、设备服务 9/9、真实 SQL 本卡 2/2、权限/审计/OpenAPI 70/70、前端聚焦 14/14、前端全量 113 files / 629 tests、Space Unit 236/236、默认 Space Integration 189 passed / 60 SQL-environment skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning，EF/SDK/TypeScript/差异门禁通过。完整真实 SQL 矩阵 248 passed / 1 已独立基线复现的 Excel 预检种子循环依赖失败。
- E10-S05“货主、SKU、批次和容器空间筛选”是下一张具备前置条件的 P2 卡；MQTT/OPC UA/厂商连接器、凭据、告警确认、设备控制、历史轨迹和预测分析仍未实现，也不得混入 S05。CAD/E06 主链继续等待正式黄金集、授权供应商证据和冻结 Worker 等外部输入。

## E10-S03 完成状态（2026-08-02）

- E10-S03 已进入受控集成基线：实现 `10b16c51`、文档 `8ce91d41`、no-ff 集成 `88efd23d`。新增版本化 `space-device-event-v1` 合同、设备主数据映射 GET/POST/PUT 与设备事件写入；读取沿用 `space:model:read`，变更要求 `space:integration:manage` 并使用稳定审计动作。
- 映射以 `TenantId + SiteId + SourceId + DeviceExternalId` 为权威身份，绑定当前 Published/Active 的稳定设备元素 LogicalId；设备类型与 Device/Conveyor/Workstation/Elevator/StaticEquipment 兼容子集失败关闭，同一来源的设备和元素保持一对一，更新使用 rowversion。
- 设备事件支持 PositionObserved、OperatingStateChanged、AlarmRaised、AlarmCleared 四类互斥形状，严格冻结 Real/Simulated、设备/状态/告警枚举、UTC 时间、五分钟未来偏差、非负序列、毫米 XYZ、Published 楼层/库位引用和来源事件幂等；相同载荷安全重放，不同载荷稳定冲突。
- Migration `20260802141148_SpaceE10S03DeviceEvents` 新增 `Space_DeviceMapping` 与追加式 `Space_DeviceEvent`，含复合租户外键、唯一索引、检查约束和事件历史写保护。旧 `WmsDeviceQuery` 仍明确保持 Unavailable 空占位，不冒充真实 WCS/IoT 来源。
- Design V1 从 62 增至 66 operations，C#/TypeScript SDK 已同步。门禁：E10-S03 服务/真实 SQL 7/7、权限/审计/OpenAPI 70/70、Space Unit 234/234、默认 Space Integration 186 passed / 60 SQL-environment skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning，EF/SDK/TypeScript/差异门禁通过。完整真实 SQL 矩阵 245 passed / 1 已独立基线复现的 Excel 预检种子循环依赖失败。
- 该节记录 E10-S03 完成时的后续边界；E10-S04 现已由上方最新状态接续完成。MQTT/OPC UA/厂商连接器、凭据、告警确认或控制写回仍未实现。

## E10-S02 完成状态（2026-08-02）

- E10-S02 已进入受控集成基线：实现 `e70c2715`、文档 `86ad63bb`、no-ff 集成 `29a69a2b`。新增内部当前位置读取与受审计的授权轨迹查询；当前位置沿用 `space:model:read`，轨迹要求 `space-audit:read` 并以 `space.personnel.trajectory.read` 失败关闭审计。
- 查询只返回稳定来源/人员外部 ID、空间运行字段、来源事件和时间证据，不返回姓名、邮箱或内部 `UserId`；外部主体在读库前拒绝，站点访问判断先于存在性查询。位置只来自 E10-S01 `PositionObserved`，不从 WMS、任务或几何推测。
- 当前新鲜度阈值为 5 分钟，过期数据仍返回并显式标记；轨迹单次最长 24 小时、可见查询期 30 天。30 天不是物理清除，追加式事件账本继续保留，物理归档/删除须在后续独立生命周期卡完成。
- 3D Viewer 已加入当前人员和授权轨迹图层，只绘制活动楼层的来源 XYZ，区分过期/模拟/工作状态，缺少 XYZ 时明确显示未定位并不推断；切层、旧请求和卸载均清理图层/GPU 资源。
- Design V1 从 60 增至 62 operations，C#/TypeScript SDK 已同步，无新 Migration。门禁：E10 服务 12/12、权限/审计/OpenAPI 68/68、前端聚焦 8/8、前端全量 113 files / 626 tests、Space Unit 234/234、默认 Space Integration 180 passed / 59 SQL-environment skipped、CP6.Tests 2736 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning，EF/SDK/TypeScript/差异门禁通过。完整真实 SQL 矩阵 238 passed / 1 已独立基线复现的 Excel 预检种子循环依赖失败。
- E10-S03 的设备实时状态、AGV/输送设备和告警尚未实现；E10 仍属 P2，不改变 CAD 阻塞主链的优先级和依赖。

## E10-S01 完成状态（2026-08-02）

- E10-S01 已进入受控集成基线：实现 `1c7aa0e2`、文档 `1da17591`、no-ff 集成 `ec29d41f`。新增版本化 `space-personnel-event-v1` 合同与 `POST /api/space/design/v1/sites/{siteId}/personnel-events`，只允许具有 `space:integration:manage` 的内部集成主体写入。
- 人员事件明确区分 `Real`/`Simulated`，同一站点和来源不能切换类型；来源事件 ID 提供业务幂等，相同载荷安全重放，不同载荷稳定冲突。Space 不猜测位置，不从 WMS/任务/几何推导忙闲状态。
- `Space_PersonnelEvent` 保存追加式事件事实，`Space_PersonnelState` 以独立的位置/工作状态游标维护当前投影；历史乱序事件进入账本但不回退投影，已绑定用户不能重分配。
- Migration `20260802125928_SpaceE10S01PersonnelEvents`、数据库检查/唯一约束、租户过滤、rowversion、OpenAPI 60 operations 及 C#/TypeScript SDK 已闭环。
- 门禁：E10 领域 3/3、服务/EF 7/7、权限/OpenAPI 43/43、Space Unit 234/234、默认 Space Integration 175 passed / 58 SQL-environment skipped、真实 SQL 本卡 2/2、CP6.Tests 2734 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning、EF/SDK/TypeScript/差异门禁通过。完整真实 SQL 矩阵 231 passed / 1 既有 Excel 预检种子循环依赖失败；该失败已在不含本卡的 `e8d4e1c2` 基线独立复现。
- E10-S02 的实时读取、授权轨迹和保留策略尚未实现；E10 仍属 P2，不改变 CAD 阻塞主链的优先级和依赖。

## E03-S01～S03 与 E13-S16 完成状态（2026-08-02）

- E03-S01～S03 已连续进入受控集成基线：标准建模 Excel 模板 `033e8872` / `8521a701`，版本化字段映射 `f1310b40` / `e0cc4964`，Excel 数据预检 `9d0a59e7` / `3571f677`。当前链路已具备标准模板下载、租户私有映射、不可变版本、50 MB 隔离上传、异步预检、结构化问题清单和受保护错误报告。
- E13-S16 已进入受控集成基线：实现 `0549a1f2`、文档 `6ec0c02a`、no-ff 集成 `ad4de0b0`。租户管理员可在 `/space/ai-admin` 管理版本化数据策略、站点、获批 Provider 别名、1～3 并发与日/月预算，并查询实际/估算/未定价用量；合同不接受或回显密钥、URL、Endpoint。
- E13-S16 新增 `Space_AiTenantPolicy` 追加式版本表、Design V1 策略/用量 API、`space-ai-admin:read/manage` 权限、五语 seed、OpenAPI 及 C#/TypeScript SDK。外部主体显式拒绝，无策略时继续 `Disabled` 失败关闭。
- 最新门禁：服务 5/5、权限 18/18、Space Unit 231/231、Space Integration 168 passed / 57 SQL-environment skipped、CP6.Tests 2733 passed / 17 environment-gated skipped、前端 112 files / 622 tests、type-check、production build、完整 solution 0 warning / 0 error、EF/SDK drift 与差异检查全部通过。
- `npm run i18n:check` 保持既有 843 项存量缺口，本卡未增加缺口。E03-S04 及 E04-S05 仍等待 E02-S07/CAD 语义预览链；E13-S04 仍等待 E02-S03，不能提前启用外部 Provider、CAD IR、输出校验或 Apply。

## E04-S06 完成状态（2026-08-02）

- E04-S06 2D/3D 同源预览已完成并进入受控集成基线：功能提交 `20f248bd`，no-ff 集成提交 `2b6ef127`，起始基线 `99e367f1`。
- Design V1 编辑器默认提供 2D+3D 分屏以及纯 2D、纯 3D 切换；两种投影消费同一个 `ISpaceDesignSceneDto`，保存成功后使用服务端回读场景同步重建，不维护第二套模型。
- 3D 只读预览复用参数化 SceneBuilder、InstancedMesh 与资产链，支持俯视/等轴/正视和自动适配；界面明确显示版本状态及“不含生产库存/任务”，不开放 3D 写操作。
- 机器一致性清单分别来自编辑器实际 Konva 投影和实际 Three.js 对象树/实例矩阵/几何，统一比较数量、LogicalId、父级、业务编码、毫米位置/尺寸、旋转、逐层规格与规范 primitive，并用 SHA-256 固化；篡改实际实例矩阵会被自动化识别。
- 生命周期边界保持失败关闭：Removed/Disabled 不渲染、移除货架不泄露遗留 Active 子层、真正孤立层仍报错；2D 路径多线段在套索选择时按 LogicalId 去重。
- 门禁：前端聚焦 4 files / 13 tests、全量 108 files / 612 tests、type-check、production build、Space Unit 231/231、默认 Space Integration 140 passed / 55 SQL-gated skipped、Design Scene 真 SQL 3/3、Space Integration + KOUSQLSERVER 195/195、CP6.Tests 2720 passed / 17 既有环境 Skip、完整 solution 非增量构建 0 error / 10 条既有 warning、SDK drift、TypeScript SDK strict no-emit 与 `git diff --check` 全部通过。
- 本卡无新端点、DTO、数据库模型、Migration、OpenAPI 或 SDK 表面变化。机器证据见 `docs/space/reports/e04-s06-shared-2d-3d-preview.md`。
- 下一张建议独立卡为 E03-S01“标准建模 Excel 模板”，其 E05-S02 依赖已满足；E04-S05、E06 与 E13 后续链路继续等待各自前置条件。

## E09-S05 完成状态（2026-08-02）

- E09-S05 外部访问审计与有效期已完成并进入受控集成基线：功能提交 `83798dcf`，no-ff 集成提交 `c658871c`，起始基线 `a5b53a2b`。
- 登录继续复用统一 `Sys_SecurityLog`；Space 追加写账本新增稳定 Portal 会话入口、组织选择、PublishedScene/Stock/Task 查看事件，记录 Organization Context、Site、结果、条目数、授权版本、Correlation/Trace 与受控客户端元数据，404 范围拒绝记为 `Denied` 且不保存异常明文。
- Organization、Membership、Grant、FieldPolicy 变更端点使用稳定业务动作码和 `space:external:manage` 证据；写入前审计失败关闭，成功写入后最终审计不可用返回 outcome unknown。暂停、撤销和退休通过同一 update 资源链可追踪。
- 外部 `Export` 求值现在强制审计允许/拒绝，并记录组织/成员安全戳、AuthorizationVersion、GrantIds 与 FieldPolicyIds；审计不可用时清空命中授权并返回 `SPACE_AUDIT_UNAVAILABLE`。本卡未新增独立导出端点。
- 同一现有会话已自动化证明 Membership/Grant 到期、暂停、撤销以及 FieldPolicy 退休在下一请求立即失效；Active Policy 版本变化在下一响应产生新 AuthorizationVersion。真 SQL 连续证明成员到期→Grant 到期→续期恢复→Policy 退休的逐请求重验证链。
- 本卡无新端点、DTO、迁移或 SDK 表面变化，OpenAPI 保持 36 paths / 47 operations。门禁：审计聚焦 48/48、Portal/真 SQL 合并态 16/16、Space Unit 231/231、Space Integration + KOUSQLSERVER 195/195、CP6.Tests 2720 passed / 17 既有环境 Skip、非增量 solution 0 error / 10 条既有 warning、EF/SDK drift、TypeScript SDK strict no-emit、前端 type-check/106 files/607 tests/production build 全部通过。
- E09-S01～S05 工程范围完成；产品、QA、WMS 与安全负责人的正式 GA 签字仍是发布治理动作，机器证据见 `docs/space/reports/e09-s05-external-access-audit-validity.md`。
- 下一张建议独立卡为 E04-S06“2D/3D 同源预览”；E04-S03、E05-S03 依赖已满足。E04-S05 继续等待 E02-S07，E10 仍为 P2。

## E09-S04 完成状态（2026-08-02）

- E09-S04 跨租户越权自动化已完成并进入受控集成基线：功能提交 `f045bd6f`，no-ff 集成提交 `c82d4fae`，起始基线 `dfacbb48`。
- 新增发布阻断矩阵，覆盖猜测 Organization/Site/Location ID、同码租户协作图、Published 场景身份、Stock/Task 运行态身份、分页游标、授权版本和字段裁剪；所有越权路径统一失败关闭，不泄露目标存在性。
- 内存与真实 SQL 使用两个租户创建相同 User、Site/Floor/Zone LogicalId、组织码、策略名、Owner 和 Task ID，九类外部协作表的正常查询各只见本租户，审计视图可确认两套数据确实同时存在。
- Portal 场景投影前新增 Schema/Authority/Site/PublishedVersion/Published 状态/Floor-Site 身份校验；Stock/Task 在处理条目前新增 SiteId + PublishedVersionId 校验；同码但非候选 LocationLogicalId 的运行态条目固定 404。
- `AuthorizationVersion` 现在显式绑定 Tenant/User/Organization/Resource，并继续包含组织/成员安全戳、Grant/Policy 版本；Data Protection 游标已自动化验证 Tenant、Actor、Organization、grant version、资源和过滤哈希任一变化均不能复用。
- 本卡无新端点、DTO、迁移或 SDK 表面变化；OpenAPI 保持 36 paths / 47 operations。功能门禁：Space Unit 231/231、Space Integration + KOUSQLSERVER 187/187、CP6.Tests 2713 passed / 17 environment-gated skipped、完整 solution 0 warning / 0 error、EF/SDK drift 与前端 106 files / 607 tests 全部通过。
- 合并态聚焦门禁：隔离矩阵含真实 SQL 16/16、游标/执行上下文中间件 42/42、完整 solution 非增量构建 0 error / 10 条既有 warning、EF/SDK drift、TypeScript SDK strict no-emit、前端 type-check/607 tests/production build 全部通过。交付报告见 `docs/space/reports/e09-s04-cross-tenant-isolation.md`。
- 下一张建议卡为 E09-S05：外部登录/组织选择/查看/导出/授权变化审计，以及 Membership/Grant/Policy 到期、暂停或撤销后现有会话下一次请求立即失效的证据。

## E09-S03 完成状态（2026-08-01）

- E09-S03 外部只读 Portal 与字段策略已完成并进入受控集成基线：功能提交 `88bc42d1`，no-ff 集成提交 `1850b2d8`，起始基线 `13c7b9da`。
- 新增租户权威 `Space_FieldPolicy` 与 `Space_FieldPolicyField`，覆盖 PublishedScene/Stock/Task、显式字段 allowlist、None/Partial/Hash/Redact 掩码、Audience、`CanExport`、版本和终态退休；数据库以复合租户外键、检查约束和过滤唯一索引失败关闭。
- 新增 `/api/space/field-policy` 管理 API，读取/变更沿用 `space:external:read/manage`；Grant 控制行与空间范围，字段策略控制字段、脱敏和导出能力，未知字段默认不可见，导出必须同时得到 Grant 与策略许可。
- 新增 `/api/space/portal/v1` 只读 Organizations/Sites/PublishedScene/Stock/Tasks。外部主体只允许 Portal 的 GET/HEAD；除组织选择外必须提供单一非空 Organization Context，内部主体、未知主体类型、缺失/歧义上下文和外部主体访问其他 Space 路径均拒绝。
- Portal 只读取当前 Published/Active；结构 ID 来自数据库权威候选，业务值字段按策略裁剪。多个合法 Grant 按完整子句 OR，字段采用命中范围内最少限制掩码；其他资源 Grant、运行源身份和 Zone-only 父层字段均不能扩大可见范围。
- OpenAPI 从 29 paths / 38 operations 增至 36 paths / 47 operations，并更新 C#/TypeScript SDK；OpenAPI、C#、TypeScript 工件 SHA-256 分别为 `BCFFEF09...DAF2`、`C7BCC222...6C4B`、`5F5132E7...9ABF`。
- 功能门禁：Space Unit 231/231；Space Integration + KOUSQLSERVER 181/181、0 skipped；CP6.Tests 2711 passed / 17 environment-gated skipped；完整 solution build 0 error；前端 106 files / 607 tests、type-check、production build；EF/SDK drift 与 C#/TypeScript SDK 编译通过。S02→S03 增量 SQL 在临时库连续执行两次通过。
- 合并态聚焦门禁：完整 solution build 0 error、字段策略领域 3/3、Portal/策略/Grant/求值器含真实 SQL 22/22、权限/OpenAPI/中间件/ProblemDetails 84/84、EF/SDK drift、前端 106 files / 607 tests、type-check 和 production build 全部通过。交付报告见 `docs/space/reports/e09-s03-external-portal-field-policy.md`。
- 下一张建议卡为 E09-S04：跨租户越权自动化测试，覆盖猜测 ID、同码组织/仓库/库位、分页/游标、缓存和 Portal DTO；E09-S05 随后补齐外部访问审计与有效期证据。

## E09-S02 完成状态（2026-08-01）

- E09-S02 外部组合授权与访问求值器已完成并进入受控集成基线：功能提交 `cae12c7e`，no-ff 集成提交 `feefa9cd`，起始基线 `8869ac58`。
- 新增 `Space_ExternalGrant` 及 Floor/Zone/Owner/Object 规范化子表；Site 必填，Floor/Zone 固定使用当前 Published Revision 的稳定 LogicalId，数据库以复合租户外键、状态/有效期/版本检查和过滤唯一索引失败关闭。
- 多个 Grant 保持完整子句 OR，单个 Grant 内 Site/Floor/Zone/Owner/BusinessObject 按 AND 匹配；禁止跨 Grant 展平维度造成笛卡尔权限升级。Export 还要求命中子句显式 `CanExport`。
- `ISpaceAccessEvaluator` 验证可信 Tenant/User、单一 Organization Context、Active Organization、有效 Active Membership 与有效 Active Grant；任一缺失、过期、暂停、撤销、歧义或跨组织拼接均拒绝。安全范围携带组织/成员安全戳、GrantVersion 和确定性 AuthorizationVersion。
- 新增 `/api/space/external-organization/{organizationId}/grant` 管理 API，读取/变更沿用 `space:external:read/manage`。`FieldPolicyId` 在 E09-S03 前只作保留字段，非空请求固定 422；外部主体仍被全局拒绝直接进入 `/api/space`，未提前开放 Portal。
- OpenAPI 增加 2 个 route family / 4 个操作并更新 C#/TypeScript SDK；运行时客户端表面哈希为 `FFCE63E749C7653E553A57D32EA85A7FF846F17199AF3436FE787CD26F509259`。
- 功能门禁：Space Unit 228/228；Space Integration + KOUSQLSERVER 169/169、0 skipped；CP6.Tests 2703 passed / 17 environment-gated skipped；完整 solution build 0 error；前端 106 files / 607 tests、type-check、production build；EF/SDK drift 与 C#/TypeScript SDK 编译通过。S01→S02 增量 SQL 在临时库连续执行两次通过。
- 合并态聚焦门禁：完整 solution build 0 error、领域 4/4、访问求值/管理/真实 SQL 10/10、权限/OpenAPI 35/35、EF/SDK drift 通过。交付报告见 `docs/space/reports/e09-s02-external-grant-scope-evaluator.md`。
- 下一张建议卡为 E09-S03：接入 Published-only 外部只读 Portal、资源 DTO allowlist、字段策略/脱敏和导出裁剪；完成前继续保留外部主体的全局拒绝。

## E09-S01 完成状态（2026-08-01）

- E09-S01 外部组织与成员模型已完成并进入受控集成基线：功能提交 `a599cfd7`，no-ff 集成提交 `09538ca3`，起始基线 `0c02fc80`。
- 新增租户权威 `Space_ExternalOrganization` 与 `Space_ExternalMembership`；支持 Customer/Supplier/ThirdPartyLogistics、ERP BusinessPartner 可选关联、用户多组织成员关系、有效期、终态生命周期、SecurityStamp、rowversion 和审计字段。
- 数据库以复合租户外键、过滤唯一索引、枚举/有效期/关联成对检查约束失败关闭；同类型组织编码唯一，客户/供应商/3PL 可以使用相同业务码而保持组织隔离。
- 新增 `/api/space/external-organization` 组织/成员管理 API，读取需要 `space:external:read`，变更需要 `space:external:manage`；跨租户用户、客商与组织引用不泄露存在性。
- OpenAPI、C# 与 TypeScript SDK 已更新；运行时客户端表面哈希为 `6011AA0FC2B4B2A81C5D915B1DEE1D0ADC84BE01BB8D2962A3D087B896E1EF76`。
- 功能门禁：Space Unit 224/224；Space Integration + KOUSQLSERVER 159/159、0 skipped；CP6.Tests 2703 passed / 17 environment-gated skipped；完整 18 项目 solution build 0 error；前端 106 files / 607 tests、type-check、production build；EF/SDK drift、TypeScript SDK 与运行时客户端表面均通过。
- 合并态聚焦门禁：领域 4/4、组织/成员内存与真实 SQL 6/6、权限/种子/ProblemDetails/OpenAPI 49/49、EF/SDK drift 与 TypeScript SDK 编译通过。交付报告见 `docs/space/reports/e09-s01-external-organization-membership.md`。
- 下一张建议卡为 E09-S02：实现 Organization Context、有效 Membership 与 Site/Floor/Zone/Owner/BusinessObject 组合 Grant，并在缺失、歧义或跨组织拼接时失败关闭。

## E08-S05 完成状态（2026-08-01）

- E08-S05 10,000 库位性能基线已完成并进入受控集成基线：功能提交 `cc1d8baf` + `24464fab`，no-ff 集成提交 `7a05c05f` + `675e485c`，起始基线 `5d37865a`。
- 锁定门槛：完整场景 ≤100 draw calls、Medium tier ≥50fps、≤3 秒可交互、标签 P95 ≤16ms/对象池 ≤200、拾取 P95 ≤150ms、10,000 条着色与运行态查询 ≤3 秒。
- 500 个货架框已从逐对象 `LineSegments` 合并为单个 wireframe `InstancedMesh`；完整标准仓 draw calls 从 535 降到 36。库位颜色缓冲在建桶时预分配，库存覆盖层走分桶批量着色并保留旧 ViewerHandle 回退。
- 硬件 WebGL 验收使用 Intel Iris Xe / D3D11：10,000 库位、36 draw calls、275ms 可交互、P95 帧间隔折算 83.3fps、标签 3.5ms、拾取 0.4ms、着色 3.0ms、35 个同屏标签、0 console errors。执行器检测到 SwiftShader 时拒绝形成 GPU PASS。
- 运行态服务现以精确 10,000 个 Published/Active 库位验证库存与任务查询各 20×500 分块且各自 ≤3 秒；既有 10,001 个不同库位在 WMS 调用前 400 拒绝。
- 功能分支门禁：Space Unit 220/220；Space Integration 105 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 106 files / 607 tests、CPU/硬件性能门禁、type-check、production build；WebApi/C#/TypeScript SDK build 与 SDK drift 通过。
- 合并态聚焦门禁：Viewer 19/19、CPU 性能 1/1、运行态 10,000/10,001 边界 2/2、follow-up 2/2、type-check 与 range whitespace 通过。交付报告见 `docs/space/reports/e08-s05-10000-location-performance.md`。
- 下一张建议卡为 E09-S01：外部组织与成员模型，使客户、供应商、3PL 可关联用户和租户。

## E08-S04 完成状态（2026-08-01）

- E08-S04 拣货任务与路径验收已完成并进入受控集成基线：功能提交 `9f7e38f8`，no-ff 集成提交 `994339a6`，起始基线 `944e465f`。
- 新增 `GET /api/space/design/v1/sites/{siteId}/runtime/tasks/path?taskId=...`；任务号必填且在 WMS 边界筛选，继续沿用 Published/Active、采纳身份、500 分块、10,000 上限及来源新鲜度。
- 响应提供 WMS 实际顺序、楼层/库区/坐标、跨层/跨区切换、总量与分区工作量，以及当前 Published 巷道拓扑；可用空结果与 `Unavailable` 严格区分，重复实际序号失败关闭为 502。
- Viewer 同时展示实际顺序、仅演示且不回写 WMS 的优化顺序、跨层/跨区和工作量；实际停靠点支持 Locator 跨层定位，残缺坐标不生成不完整优化路径。
- 当前 Design Revision 没有连接体拓扑，跨层段明确降级为近似直连并提示，不把近似路线伪装为精确结果。
- 功能分支门禁：Space Unit 220/220；Space Integration 105 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 105 files / 603 tests、type-check、production build；WebApi/C#/TypeScript SDK build 与 SDK drift 通过。
- 合并态聚焦门禁：runtime 47/47、OpenAPI/SDK 18/18、前端 9/9、type-check 与 SDK drift 通过。交付报告见 `docs/space/reports/e08-s04-task-path-acceptance.md`。
- 下一张建议卡为 E08-S05：10,000 库位性能基线，锁定场景交互、标签和批量查询门槛。

## E08-S03 完成状态（2026-08-01）

- E08-S03 物料/批次/容器定位验收已完成并进入受控集成基线：功能提交 `8d8f7e01`，no-ff 集成提交 `dfb6e93b`，起始基线 `faeacd4b`。
- 新增统一运行源端点 `GET /api/space/design/v1/sites/{siteId}/runtime/inventory/locate`；物料、批次、容器至少一个，多个条件固定按精确 AND 匹配。
- 查询当前 Published/Active Space 库位并沿用采纳后的 WMS 身份、500 分块和 10,000 上限；响应按 Space 逻辑库位聚合，显式提供双身份/双编码、楼层、数量、匹配事实、命中库位数、楼层数与 E08-S02 来源新鲜度。
- 可用来源的零命中与 `Unavailable` 来源严格区分；不满足条件、非正库存或同一 WMS 身份多编码的适配器响应以 502 合同违例失败关闭。
- Viewer 搜索支持编码、物料、批次、容器；多结果按楼层分组，由用户选择候选后复用现有跨层 Locator，不再擅自跳第一条；旧并发响应不能覆盖新搜索。
- 功能分支门禁：Space Unit 220/220；Space Integration 101 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 103 files / 597 tests、type-check、production build；WebApi/C# SDK 0 warning / 0 error；TypeScript SDK 与 SDK drift 通过。
- 合并态聚焦门禁：runtime 44/44、OpenAPI/SDK 18/18、前端 5/5、type-check 通过。交付报告见 `docs/space/reports/e08-s03-inventory-locate.md`。
- 下一张建议卡为 E08-S04：拣货任务与路径验收，覆盖实际/优化顺序、跨区/跨层和工作量。

## E08-S02 完成状态（2026-08-01）

- E08-S02 库存来源、时间和延迟展示已完成并进入受控集成基线：功能提交 `9a478c7a`，no-ff 集成提交 `d4cd8a82`，起始基线 `bbe77f3e`。
- 3D Viewer 库存覆盖层已从旧楼层库存接口迁移到 E08-S01 统一运行源；按当前楼层 Space 逻辑库位 ID 查询和聚合，不以 WMS 编码漂移替换稳定身份。
- 运行源公开并显示来源类型、来源系统、运行连接、数据观察时间、CP6 接收时间、延迟、时钟超前，以及 Viewer 会话最近成功/最近失败/恢复状态。
- 统一源暂无容量、锁定和拣货流程事实，Viewer 只展示空/有库存，并把利用率明确标为占用估算；不伪造满、锁定或在拣状态。
- 功能分支门禁：Space Unit Release 220/220；Space Integration Release 96 passed + 48 SQL-gated skipped；OpenAPI/SDK 18/18；前端 102 files / 593 tests、type-check、production build；C# SDK 0 warning / 0 error；SDK 无 drift。
- 合并态聚焦门禁：runtime 40/40、OpenAPI/SDK 18/18、前端 20/20、type-check 通过。交付报告见 `docs/space/reports/e08-s02-runtime-freshness.md`。
- 下一张建议卡为 E08-S03：物料/批次/容器定位验收，覆盖多结果、空结果和跨层结果。

## E08-S01 完成状态（2026-07-31）

- E08-S01 统一运行态数据源已完成并进入受控集成基线：最终功能提交 `3df6b1d2`、no-ff 集成提交 `b2bb7a35`、设计提交 `636eb6d5`。
- 功能分支全量验证：Space Unit 220 passed / 0 failed / 0 skipped；默认 Space Integration 94 passed / 0 failed / 48 SQL 环境门禁 skipped；OpenAPI/权限/数据源合同聚焦 45 passed；Release 完整 solution build 0 error / 10 个既有 warning；SDK 无 drift；EF 无待迁移模型变化；feature range `git diff --check` 静默通过。
- 最终复审修复了生成 C#/TypeScript SDK 丢失合法 nullable 响应类型的问题；修复后 OpenAPI/权限 34/34、Client build 0 warning / 0 error、SDK 无 drift，并由原 API 复审者确认关闭。合并态门禁为 runtime/adapter unit 23/23、runtime/adapter/simulator integration 56/56、OpenAPI/权限 34/34、SDK 无 drift。
- 运行权威规则：当前 Published/Active Space 模型是空间与身份权威；生产 `Cp6SpaceWmsAdapter` 是库存/任务运行态权威；模拟器只允许显式选择/测试；Design Revision 不持久化库存、任务等运行事实。
- 已交付 `GET /api/space/design/v1/sites/{siteId}/runtime/inventory` 与 `GET /api/space/design/v1/sites/{siteId}/runtime/tasks`，均要求 `space:model:read`，支持重复 `locationLogicalId` 筛选、Space/WMS 双 LogicalId 与双编码。
- 查询按 500 个位置分块、最多 10,000 个位置；来源/输出合同违例失败关闭为 502，适配器异常为可重试 503；明确 `Unavailable` 返回空 `Items` 并携带 `IsAvailable=false`，不与真实空结果混同。
- 该节记录 E08-S01 完成时的后续建议；E08-S02 现已由上方最新状态接续完成。E08-S01 交付报告见 `docs/space/reports/e08-s01-unified-runtime-source.md`。

## E07-S05 完成状态（2026-07-31）

- E07-S05 存量 WMS 采纳与绑定已完成：独立采纳账本、刷新、分页、单项/批量绑定、空位放置、差异 Issue 同步、rowversion 并发、权限/OpenAPI/SDK 和 Design V1 编辑器侧栏均已闭环。
- 功能提交 `15ccf992`，no-ff 集成提交 `389bf4ec`；交付报告见 `docs/space/reports/e07-s05-wms-adoption.md`。
- 验证：Space Unit 218；默认 Space Integration 56 passed / 48 SQL-gated skipped；WMS 聚焦 11/11，其中 KOUSQLSERVER 3/3；OpenAPI/权限 35/35；前端 98 files / 579 tests；production build、完整 solution build、EF model drift 和 SDK drift 均通过。
- E07-S01 至 E07-S05 已全部进入受控集成基线。该条记录的是 E07-S05 完成时的后续建议；E08-S01 现已由上方最新状态接续完成。

## Git

- 交付分支：`main`
- T6 通过 merge commit `d79a39c` 合入并推送；T7 冒烟修复为 `ffca422`
- Space E04 S06 功能/集成提交：`20f248bd` / `2b6ef127`
- Space E09 S05 功能/集成提交：`83798dcf` / `c658871c`
- Space E07 S05 功能/集成提交：`15ccf992` / `389bf4ec`
- Space E08 S01 功能/集成提交：`3df6b1d2` / `b2bb7a35`
- Space E08 S02 功能/集成提交：`9a478c7a` / `d4cd8a82`
- Space E08 S03 功能/集成提交：`8d8f7e01` / `dfb6e93b`
- Space E08 S04 功能/集成提交：`9f7e38f8` / `994339a6`
- Space E08 S05 功能/集成提交：`cc1d8baf` + `24464fab` / `7a05c05f` + `675e485c`
- Space E09 S01 功能/集成提交：`a599cfd7` / `09538ca3`
- Space E09 S02 功能/集成提交：`cae12c7e` / `feefa9cd`
- Space E09 S03 功能/集成提交：`88bc42d1` / `1850b2d8`
- Space E09 S04 功能/集成提交：`f045bd6f` / `c82d4fae`
- Space E10 S01 功能/文档/集成提交：`1c7aa0e2` / `1da17591` / `ec29d41f`
- Space E10 S02 功能/文档/集成提交：`e70c2715` / `86ad63bb` / `29a69a2b`
- Space E10 S03 功能/文档/集成提交：`10b16c51` / `8ce91d41` / `88efd23d`
- Space E10 S04 功能/文档/集成提交：`9a9802a8` / `f961d7e5` / `b4d5b81e`
- Space E10 S05 功能/文档/集成提交：`65c59555` / `53bea9b9` / `e270c2cc`
- Space E10 S06 功能/文档/集成提交：`0676ba4a` / `969e7c38` / `5f86edcb`
- Space E11 S01 合同/功能/文档/集成提交：`66b6c17f` / `53a07d46` / `a6d7a55c` / `8d4732e2`
- Space E11 S02 合同/功能/文档/集成提交：`3ccd2936` / `644293f1` / `034a1b1b` / `a2b47826`
- Space E11 S03 合同/功能/文档/集成提交：`3cf42534` / `419d3f6c` / `eea62de0` / `cf7bf778`
- Space E11 S04 合同/功能/文档/集成提交：`098fb54b` / `a7298e28` / `a552d05d` / `c19231db`
- Space E11 S05 合同/功能/文档/集成提交：`139c76b5` / `e8df8288` / `a0b247ab` / `cf35849c`

- 交付分支：`main`
- T6 通过 merge commit `d79a39c` 合入并推送；T7 冒烟修复为 `ffca422`
- Space 受控集成分支：`integration/space-v1-20260730`
- Space E00 + E01 S01–S03 集成提交：`539d56de`
- Space E01 S04 功能/集成提交：`bac76444` / `85792161`
- Space E01 S05 功能/集成提交：`3258d47f` / `36f534d9`
- Space E01 S06 功能/集成提交：`6daf1aeb` / `2ccdff7a`
- Space E02 S01 实验门禁功能/集成提交：`fe959066` / `3742fbff`
- Space E04 S01 功能/集成提交：`1d57a3b5` / `e8e84853`
- Space E04 S02 功能/集成提交：`20ee0af0` / `c1043d15`
- Space E04 S03 功能/集成提交：`b322e84a` / `39146c38`
- Space E04 S04 功能/集成提交：`9a87dc30` / `f9c7fd21`
- Space E07 S01–S03 功能/集成提交：`d06a8bd1` / `6e67a9d1`
- Space E07 S04 功能/集成提交：`74577015` / `6d751e0c`
- Space E13 S01 功能/集成提交：`8f7fc25e` / `ea161975`
- Space E13 S02 功能/集成提交：`cff25a25` / `94822669`
- Space E13 S03 功能/集成提交：`cebd401a` / `dca6e19c`
- Space E13 S12 功能/集成提交：`54456946` / `b33929fb`
- Space E13 S16 功能/文档/集成提交：`0549a1f2` / `6ec0c02a` / `ad4de0b0`
- Space E03 S01 功能/集成提交：`033e8872` / `8521a701`
- Space E03 S02 功能/集成提交：`f1310b40` / `e0cc4964`
- Space E03 S03 功能/集成提交：`9d0a59e7` / `3571f677`
- Space E05 S01 功能/集成提交：`5bb0cdfb` / `49dbabe3`
- Space E05 S02 功能/集成提交：`2fc03681` / `3d554852`
- Space E05 S03 功能/集成提交：`00021f0a` / `a1edecef`
- Space E05 S04 功能/集成提交：`85b57960` / `888de795`
- Space E05 S05 功能/集成提交：`856f138c` / `a3864d9c`
- Space 历史基线文档提交：`407dcbea`
- Space 后续候选安全检查点：`checkpoint/space-candidate-20260730`（`0d25da4d`，不得整包合入）
- 远端：`origin`（GitHub 私有仓库）
- 换机标签：`migration-2026-07-18-ready`
- 数据备份：Git LFS 三对象，已推送并校验

## 当前波：Space V1 受控集成

| 范围 | 状态 | 证据 |
|---|---|---|
| E00 S01–S04 | 已进入集成基线 | `539d56de`；事实清单、兼容护栏、数据源契约、审计/可观测性 |
| E01 S01–S06 | 已进入集成基线 | `539d56de` + `85792161` + `36f534d9` + `2ccdff7a`；版本/来源文件/Job Ledger、Published→Draft Clone、Design API v1、生成 SDK、文件安全扫描与保留清理 |
| E02 S01 | 部分进入集成基线，最终签收受阻 | `fe959066` + `3742fbff`；中立审计/压力/运行证据/preflight 已集成；另有 20 份可重复生成的合成开发 DXF（L1～L5 各 4 份，五种 DXF 文件头），但正式 DWG 黄金集、授权、供应商包/凭据和冻结 Worker 尚缺 |
| E03 S01–S03 | 已进入集成基线 | `033e8872` + `8521a701` + `f1310b40` + `e0cc4964` + `9d0a59e7` + `3571f677`；标准 Excel 模板、版本化字段映射、隔离上传、异步预检、结构化问题与受保护错误报告 |
| E02 S02–S08、E03 S04、E04 S05、E13 S04 | 开发切片已进入集成，正式签收受阻 | 合成 DXF 已贯通中立 CAD IR、坐标、Inventory、Mapping、语义、问题定位、Excel/CAD 匹配、Review Workspace、CAD Parse 作业安全和 AI 外发最小化；均不替代授权适配器/生产 Artifact/权限审计/真实黄金集验收。E13-S04 no-ff 为 `8bc1114d` |
| E04 S01–S04、S06 | 已进入集成基线 | `1d57a3b5` + `e8e84853` + `20ee0af0` + `c1043d15` + `b322e84a` + `39146c38` + `9a87dc30` + `f9c7fd21` + `20f248bd` + `2b6ef127`；安全底图、坐标标定、通用元素属性、统一批量编辑与补偿命令，以及同一 Design Scene 的 2D/3D 只读预览和实际渲染结构一致性证明 |
| E07 S01–S05 | 已进入集成基线 | `d06a8bd1` + `6e67a9d1` + `74577015` + `6d751e0c` + `15ccf992` + `389bf4ec`；版本化能力合同、CP6 真实适配器、持久化幂等账本、标准模拟器、确定性标准仓与存量 WMS 采纳/绑定 |
| E08 S01–S05 | 已进入集成基线 | `3df6b1d2` + `b2bb7a35` + `9a478c7a` + `d4cd8a82` + `8d8f7e01` + `dfb6e93b` + `9f7e38f8` + `994339a6` + `cc1d8baf` + `24464fab` + `7a05c05f` + `675e485c`；统一 Published 运行源、双身份、来源新鲜度、库存定位、任务路径与 10,000 库位性能基线 |
| E09 S01–S05 | 已进入集成基线 | `a599cfd7` + `09538ca3` + `cae12c7e` + `feefa9cd` + `88bc42d1` + `1850b2d8` + `f045bd6f` + `c82d4fae` + `83798dcf` + `c658871c`；外部组织/成员、组合 Grant、字段策略/脱敏、Published-only Portal、跨租户阻断矩阵，以及访问审计和授权有效期即时重验证 |
| E10 S01–S06 | 已进入集成基线 | `1c7aa0e2` + `1da17591` + `ec29d41f` + `e70c2715` + `86ad63bb` + `29a69a2b` + `10b16c51` + `8ce91d41` + `88efd23d` + `9a9802a8` + `f961d7e5` + `b4d5b81e` + `65c59555` + `53bea9b9` + `e270c2cc` + `0676ba4a` + `969e7c38` + `5f86edcb`；人员/设备事件与运行投影、3D 叠加、库存空间筛选，以及仓库 KPI、面积/占用、ABC 与异常快照 |
| E11 S01 | 已进入集成基线 | `66b6c17f` + `53a07d46` + `a6d7a55c` + `8d4732e2`；内部只读运营诊断、路径覆盖/折返/停留/观测重叠、诚实库位占用压力、隐私边界、审计权限和 Viewer DIAG 面板 |
| E11 S02 | 已进入集成基线 | `3ccd2936` + `644293f1` + `034a1b1b` + `a2b47826`；内部上架推荐、不可变证据、首因排除解释、精确合并与空库位候选、权限审计和 Viewer PUT 面板 |
| E11 S03 | 已进入集成基线 | `3cf42534` + `419d3f6c` + `eea62de0` + `cf7bf778`；内部人员调度建议、真实待分配任务与人员双时点、确定性最大基数匹配、不可变证据、首因排除和 Viewer DSP 面板 |
| E11 S04 | 已进入集成基线 | `098fb54b` + `a7298e28` + `a552d05d` + `c19231db`；OA 审批、提交/终审人分离、最终事实重验证、真实 `MobileTask` 整批分派、幂等回执与失败关闭 |
| E11 S05 | 已进入集成基线 | `139c76b5` + `e8df8288` + `a0b247ab` + `cf35849c`；实时执行状态、三层幂等回执、受限人工重试、安全整批补偿、权限审计和 Viewer 执行治理 |
| E13 S01–S13、S16 | 已进入集成基线；真实外部 Provider 仍关闭 | Provider/确定性端口、Run/Proposal/Decision/Usage、可恢复 Worker、最小化/本地生成/输出校验/融合/审核/决策/原子 Apply/恢复、同源人工锁接线、RuleOnly PreviewSet→AwaitingReview 生产执行、外部主体与外发门禁，以及数据库配额和策略/用量 UI |
| E05 S01–S05 | 已进入集成基线 | 通用元素、逐层货架、统一场景 DTO、版本化资产库及确定性参数化 3D 渲染 |
| E06 S01–S02 | 已进入集成基线 | `c17242c3` + `76c70230` + `a174f7cc` + `5bd2c616`；权威 ValidationRun 与来源/规则/WMS 能力冻结、确定性版本差异/WMS 影响预览、稳定 PlanHash、权限审计和 SDK；两卡真实 SQL 各 3/3、完整 Release 与漂移门禁通过 |
| E02 S02–S08 正式签收、E03 S05、E06 S03–S06、E13 S05～S11 正式外部链验收、S14～S15/S17～S19 等剩余范围 | 候选证据或尚未实现 | 生产 CAD 链、正式输入、Hosted Worker、权威 Match Artifact、持久化发布编排/重试/回退/UI 和真实外部 Provider 证据仍需逐卡解除；`0d25da4d` 只作提取来源，不得以候选报告或开发切片替代正式集成验收 |

## 上一完成波：GR-VP

| 任务 | 状态 | 证据 |
|---|---|---|
| T1 标准一般用户角色种子 | 完成 | `ddcfa1ac`，7 测试 |
| T2 OA/WF v-permission | 完成 | `15823c38`，40 按钮/17 视图 |
| T3 ERP v-permission | 完成 | `4a48525e`，39 按钮/16 视图 |
| T4 MES v-permission | 完成 | `6e4ade1`，31 指令/12 视图/24 键 |
| T5 FIN v-permission | 完成 | `5732057`，66 指令/16 视图/51 键 |
| T6 PUR/PLAN/PUB v-permission | 完成 | `4bb7512` + `cf20d42`，37 页面级声明/12 视图/33 键；异步加载守权 |
| T7 部署与冒烟 | 完成 | API/Web 双镜像；A1 角色 SQL；`qa_general` 自审批/越权/403 冒烟 |

## 最近验证基线

- E06-S02 已推进至受控集成提交 `5bd2c616`：功能提交 `a174f7cc`；引擎聚焦 17/17、API/权限/OpenAPI 55/55、Space Unit 448/448、CP6.Tests 2794 passed / 17 environment-gated skipped、默认 Space Integration 259 passed / 89 SQL-gated skipped、本卡真实 SQL 3/3、完整 solution 双架构 AOT Release 0 warning / 0 error、EF/SDK drift 和差异检查均通过。
- E06-S01 已推进至受控集成提交 `76c70230`：功能提交 `c17242c3`；Space Unit 440/440、CP6.Tests 2793 passed / 17 environment-gated skipped、默认 Space Integration 259 passed / 86 SQL-gated skipped、本卡真实 SQL 3/3、完整 solution Release 0 warning / 0 error、EF/SDK drift、幂等增量 SQL 双执行和差异检查均通过。
- E11-S05 已推进至受控集成提交 `cf35849c`：合同 `139c76b5`、功能 `e8df8288`、文档 `a0b247ab`。功能分支全量门禁为 Space Unit 249/249、默认 Space Integration 230 passed / 62 SQL-gated skipped、CP6.Tests 2757 passed / 17 environment-gated skipped、前端 118 files / 658 tests、完整 solution Release、生产构建、EF/SDK drift、TypeScript SDK strict no-emit 和差异检查通过；合并态服务/适配器 14/14、权限/合同/种子 35/35、前端 21/21、类型、SDK drift 与 EF pending model 通过。i18n 仍为 908 项既有欠账，本卡净新增 0。
- E11-S04 已推进至受控集成提交 `c19231db`：合同 `098fb54b`、功能 `a7298e28`、文档 `a552d05d`。功能分支全量门禁为 Space Unit 249/249、默认 Space Integration 224 passed / 62 SQL-gated skipped、CP6.Tests 2757 passed / 17 environment-gated skipped、前端 118 files / 656 tests、完整 solution Release、生产构建、EF/SDK drift 与两个 TypeScript strict no-emit 通过；合并态审批服务/适配器 8/8、权限/合同/种子/基础设施 44/44、前端 19/19、类型、SDK drift 与 EF pending model 通过。i18n 历史欠账由 909 降至 908。
- E11-S03 已推进至受控集成提交 `cf7bf778`：合同 `3cf42534`、功能 `419d3f6c`、文档 `eea62de0`。Space Unit 249/249、默认 Space Integration 216 passed / 62 SQL-gated skipped、CP6.Tests 2752 passed / 17 environment-gated skipped、前端 118 files / 653 tests、完整 solution Release、EF/SDK drift 与生产构建通过；合并态引擎/运行时 6/6、服务/适配器 6/6、权限/审计/API/种子 23/23、前端 16/16、类型与 SDK drift 通过。
- E11-S02 已推进至受控集成提交 `a2b47826`：合同 `3ccd2936`、功能 `644293f1`、文档 `034a1b1b`。Space Unit 245/245、默认 Space Integration 211 passed / 62 SQL-gated skipped、CP6.Tests 2748 passed / 17 environment-gated skipped、前端 117 files / 648 tests、完整 solution 非增量构建 0 error / 10 条既有 warning、EF/SDK drift、两个 TypeScript strict no-emit、production build 与差异检查通过；合并态引擎 5/5、服务 6/6、权限/审计/契约/种子 34/34、前端 14/14 和 SDK drift 通过；i18n 欠账由 911 降至 909。
- E11-S01 已推进至受控集成提交 `8d4732e2`：只读运营诊断、Real-only 人员证据、路径/折返/停留/观测重叠、当前库位占用压力与真实容量不可用边界完成；Space Unit 240/240、默认 Space Integration 205 passed / 62 SQL-gated skipped、CP6.Tests 2744 passed / 17 environment-gated skipped、前端 116 files / 643 tests、完整 solution 0 error / 10 条既有 warning、EF/SDK drift 通过。合并态引擎 4/4、服务 7/7、权限/审计/契约/种子 59/59、前端 12/12 和严格类型检查通过。35 个新界面键均有五语种子，i18n 欠账保持基线 911 项。交付证据见 `docs/space/reports/e11-s01-operations-diagnostics.md`。
- E10-S06 已推进至受控集成提交 `5f86edcb`：仓库 KPI、面积/占用口径、独立来源部分快照、ABC 分类和 Viewer 互斥覆盖完成；Space Unit 236/236、默认 Space Integration 198 passed / 62 SQL-gated skipped、本卡真实 SQL 3/3、CP6.Tests 2739 passed / 17 environment-gated skipped、前端 115 files / 639 tests、完整 solution 0 error / 10 条既有 warning、EF/SDK drift 通过。合并态合同 23/23、Runtime/适配器 81/81、权限/OpenAPI 46/46、前端 25/25、类型检查和 SDK drift 通过。i18n 保留 881 项基线债务和本卡新增 30 项。交付证据见 `docs/space/reports/e10-s06-warehouse-overview.md`。
- E10-S05 已推进至受控集成提交 `e270c2cc`：货主、SKU、批次和容器精确 AND 空间筛选完成；运行合同 2/2、Runtime/适配器 68/68、权限/OpenAPI 45/45、前端 114 files / 632 tests、Space Unit 236/236、默认 Space Integration 190 passed / 61 SQL-gated skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning、EF/SDK/TypeScript drift 通过，本卡真实 SQL 1/1。完整真实 SQL 矩阵 250 passed / 1 个已知基线失败。交付证据见 `docs/space/reports/e10-s05-inventory-spatial-filters.md`。
- E10-S04 已推进至受控集成提交 `b4d5b81e`：设备当前/告警投影、读取 API 和 3D 叠加完成；领域 2/2、设备服务 9/9、本卡真实 SQL 2/2、权限/审计/OpenAPI 70/70、前端 113 files / 629 tests、Space Unit 236/236、默认 Space Integration 189 passed / 60 SQL-gated skipped、CP6.Tests 2738 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning、EF/SDK drift 与两个 TypeScript strict no-emit 通过。完整真实 SQL 矩阵 248 passed / 1 已知基线失败；合并态设备 9/9、权限/审计/OpenAPI 70/70、前端聚焦 14/14、EF/SDK drift 通过。交付证据见 `docs/space/reports/e10-s04-device-runtime-overlay.md`。
- E04-S06 已推进至受控集成提交 `2b6ef127`：功能分支与合并态前端全量均为 108 files / 612 tests，聚焦 4 files / 13 tests、type-check 和 production build 通过；Space Unit 231/231、默认 Space Integration 140 passed / 55 SQL-gated skipped、Design Scene 真 SQL 3/3、Space Integration + KOUSQLSERVER 195/195 且 0 skipped、CP6.Tests 2720 passed / 17 environment-gated skipped、完整 solution 非增量构建 0 error / 10 条既有 warning，以及 SDK drift、TypeScript SDK strict no-emit 和差异门禁均通过。
- E09-S05 已推进至受控集成提交 `c658871c`：审计聚焦 48/48、Portal/真 SQL 合并态 16/16、Space Unit 231/231、Space Integration + KOUSQLSERVER 195/195、CP6.Tests 2720 passed / 17 environment-gated skipped、完整 solution 非增量构建 0 error / 10 条既有 warning、前端 106 files / 607 tests、EF/SDK drift 与 TypeScript SDK strict no-emit 均通过。
- E09-S04 已推进至受控集成提交 `c82d4fae`：功能分支门禁为 Space Unit 231/231、Space Integration + KOUSQLSERVER 187/187 且 0 skipped、CP6.Tests 2713 passed / 17 environment-gated skipped、完整 solution 0 warning / 0 error、前端 106 files / 607 tests、EF/SDK drift 与 TypeScript SDK strict no-emit；合并态聚焦门禁为 16/16、42/42、完整 solution 0 error / 10 条既有 warning、前端 607/607 及 EF/SDK drift。
- E09-S03 已推进至受控集成提交 `1850b2d8`：功能分支门禁为 Space Unit 231/231、Space Integration + KOUSQLSERVER 181/181 且 0 skipped、CP6.Tests 2711 passed / 17 environment-gated skipped、完整 solution build 0 error、前端 106 files / 607 tests、EF/SDK drift、C#/TypeScript SDK 编译与 S02→S03 幂等增量 SQL 双执行通过；合并态聚焦门禁为 3/3、22/22、84/84、前端 607/607 及 EF/SDK drift。
- E09-S02 已推进至受控集成提交 `feefa9cd`：功能分支门禁为 Space Unit 228/228、Space Integration + KOUSQLSERVER 169/169 且 0 skipped、CP6.Tests 2703 passed / 17 environment-gated skipped、完整 solution build 0 error、前端 106 files / 607 tests、EF/SDK drift、C#/TypeScript SDK 编译与 S01→S02 幂等增量 SQL 双执行通过；合并态聚焦门禁为 4/4、10/10、35/35 及 EF/SDK drift。
- E09-S01 已推进至受控集成提交 `09538ca3`：功能分支门禁为 Space Unit 224/224、Space Integration + KOUSQLSERVER 159/159 且 0 skipped、CP6.Tests 2703 passed / 17 environment-gated skipped、完整 solution build 0 error、前端 106 files / 607 tests、EF/SDK drift、TypeScript SDK 和运行时客户端表面通过；合并态聚焦门禁为 4/4、6/6、49/49 及 EF/SDK drift。
- E08-S04 已推进至受控集成提交 `994339a6`：功能分支门禁为 Space Unit 220/220、Space Integration 105 passed / 48 SQL-gated skipped、OpenAPI/SDK 18/18、前端 105 files / 603 tests、type-check、production build、WebApi/C#/TypeScript SDK build 与 SDK drift；合并态聚焦门禁为 runtime 47/47、OpenAPI/SDK 18/18、前端 9/9、type-check 和 SDK drift。
- E08-S03 已推进至受控集成提交 `dfb6e93b`：功能分支门禁为 Space Unit 220/220、Space Integration 101 passed / 48 SQL-gated skipped、OpenAPI/SDK 18/18、前端 103 files / 597 tests、type-check、production build、WebApi/C#/TypeScript SDK build 与 SDK drift；合并态聚焦门禁为 runtime 44/44、OpenAPI/SDK 18/18、前端 5/5 和 type-check。
- E08-S02 已推进至受控集成提交 `d4cd8a82`：功能分支门禁为 Space Unit Release 220/220、Space Integration Release 96 passed / 48 SQL-gated skipped、OpenAPI/SDK 18/18、前端 102 files / 593 tests、type-check、production build、C# SDK build 与 SDK drift；合并态聚焦门禁为 runtime 40/40、OpenAPI/SDK 18/18、前端 20/20 和 type-check。
- E08-S01 已推进至受控集成提交 `b2bb7a35`：功能分支全量门禁为 Space Unit 220、默认 Space Integration 94 passed / 48 SQL-gated skipped、OpenAPI/权限/数据源合同 45、完整 solution build 0 error / 10 个既有 warning、EF/SDK drift 均通过；合并态聚焦门禁为 23/23、56/56、34/34 和 SDK 无 drift。
- 历史 E04-S04 验证快照：当时集成代码提交为 `f9c7fd21`。合并态完整 solution 构建 0 error / 10 个既有 warning；Space Unit 213 passed，默认 Space Integration 48 passed / 45 SQL-gated skipped，Design Scene 真实 SQL 3/3 passed；OpenAPI/权限 25/25 passed；前端 96 files / 575 tests、type-check、production build 通过；EF 无待迁移模型变化，SDK drift 通过。
- E02 S01 实验门禁已推进至 `3742fbff`：中立工具 10/10 测试通过，Aspose 隔离实验适配器构建 0 warning / 0 error；5 个冻结 Seed 完整性通过，50MiB 与 100 万实体压力资产生成通过。严格 readiness 按预期退出 `3`，ODA/APS 模板 preflight 按预期退出 `4`，表明外部签收条件仍未满足。
- E02 合成开发语料新增 `development-v2.0.0`：仓库内生成器可重复生成 20 份 DXF，L1～L5 各 4 份，覆盖 AC1009/AC1015/AC1021/AC1027/AC1032 以及块/属性/填充/样条/椭圆/标注/XRef 等开发场景。工具测试 12/12、数据包完整性、哈希、句柄和 DXF 文件头矩阵通过；清单明确 `countsTowardReleaseGate=false`，不替代原生 DWG、供应商授权和正式黄金集。交付证据见 `docs/space/reports/e02-synthetic-development-cad-corpus.md`。
- E07 S01–S03 已推进至 `6e67a9d1`：Release 全解构建 0 error（7 个既有测试 warning），Space Unit 73 passed，Space Integration 35 passed / 30 SQL 环境门禁 skipped，CP6 主测试 2674 passed / 17 environment-gated skipped，Client 71 passed，EF 模型与 Migration 一致；新增代码精确格式门禁通过。
- E07 S04 已推进至 `6d751e0c`：500 货架、10,000 库位、100 SKU、5,000 库存记录、100 拣货任务和 6 个固定故障样本由同一固定种子生成；两次独立生成的 17 个文件差异为 0，干净检出后的 Manifest 16 个受管文件哈希错误为 0。合并态 Release 全解构建 0 error（10 个既有 warning），Space Unit 79 passed，Space Integration 40 passed / 30 SQL 环境门禁 skipped，CP6 主测试 2680 passed / 17 environment-gated skipped，Client 71 passed。
- E13 S01–S03、S12 已完成 Provider 安全端口、运行审计模型、可恢复 Worker 控制面、三并发槽和日/月预算原子账本；外部 Provider、CAD IR、输出校验与 Apply 仍未提前启用。
- E03 S01–S03 与 E13-S16 已推进至集成提交 `3571f677` 和 `ad4de0b0`；最新门禁为 CP6.Tests 2733 passed / 17 environment-gated skipped、前端 112 files / 622 tests、完整 solution 0 warning / 0 error、EF/SDK drift 通过。E13-S16 交付证据见 `docs/space/reports/e13-s16-ai-policy-budget-usage-ui.md`。
- E10-S01 已推进至集成提交 `ec29d41f`：人员事件合同、追加式账本和双时间游标投影完成；Space Unit 234/234、默认 Space Integration 175 passed / 58 SQL-gated skipped、本卡真实 SQL 2/2、CP6.Tests 2734 passed / 17 environment-gated skipped、完整 solution 0 error / 10 条既有 warning、EF/SDK/TypeScript drift 通过。交付证据见 `docs/space/reports/e10-s01-personnel-event-contract.md`。
- E05 S01–S05 已完成通用元素、非均匀逐层货架、Design Revision 权威场景、System/Tenant 版本化资产库和 `space-parametric-v1` 确定性前端渲染链；资产不加载外部 URL 或脚本。
- E04 S01–S04 已完成 PDF/PNG/JPG 底图、安全扫描、挂接、标定、通用元素属性，以及货架/元素共享的框选、对齐、等距、旋转、删除、阵列和补偿式撤销/重做；命令继续保持 Draft/revision 失败关闭、协议幂等、整批原子性和逐命令 before/after 审计。默认扫描器继续失败关闭，多副本生产环境必须配置真实扫描引擎与共享耐久卷。
- Space 集成前端：type-check 通过，96 files / 575 tests passed，production build 通过；仅有既有大 chunk 提示。
- CP6.Tests 全量本轮为 2682 passed / 6 个既有 RFQ 固定日期失败 / 17 environment-gated skipped；同一 RFQ 失败已在 S03 前的 `f8dff096` 基线复现，不是 Space 回归。
- 后续候选检查点 `0d25da4d` 已独立通过更大范围候选回归，但它仍不是实现真相，也不授权整包合并。
- 后端在 GR-VP T1 报告中：2220 passed / 5 skipped。
- 前端在 T7 干净 `main` 重新验证：73 files / 488 tests passed，type-check 0，2649 modules production build 通过；在线 Web 与新 chunk 均为 200。
- T6 后端权限 oracle：11/11 passed。
- T7 真实权限链：4 菜单、8 动作；本人待办 200、他人待办 400、无权端点 403；测试流程数据已清理。
- 这些是最近任务报告基线，不代表生成本知识库时重新运行了全量测试。

## 数据状态

- `CP6DB`、`CP6DB_OA`、`CP6DB_SpaceQA` 已于 2026-07-18 备份。
- 三份均通过 SQL Server `RESTORE VERIFYONLY WITH CHECKSUM`。
- 新机恢复后需重新轮换 Secrets 并做登录、权限、i18n 与关键业务冒烟。
- 当前运行库 `CP6DB` 的租户注册表只有 `DEFAULT/A1`；RoleId=10 为 1 角色/4 菜单/8 动作，admin 为 148 菜单/323 动作。`qa_general` 保留为 A1 常驻测试用户。

## 下一动作

`main` 同步和 P2.5 受控整合已经完成，不再重复处理候选分支。下一张不依赖外部输入的 Space 卡优先实现无人工锁时的确定性父关系推导：只有唯一、可证明的几何包含关系才能生成 Zone/Aisle/Rack 父关系，无匹配或多候选继续 Blocking。不同 SourceHash 的几何建议继承与人工确认另开产品卡。GR-VP T1–T7 已完成，不要重做；其 PMS/Sys 权限 UX、角色显示名和 insert-only 基线语义仍按 `06-Todo.md` 的 P1 独立处理。正式 CAD/黄金集、外部 Provider、S14～S15/S18～S19、E12-S06、生产迁移与 WMS 发布/恢复演练继续作为独立失败关闭门禁；禁止把候选检查点 `0d25da4d` 整包合入。


## 2026-10-25：README 通用制造定位与入口同步（README-20261025）

根 README 已同步为面向通用制造业的企业管理平台，补齐工程/PLM、质量与端到端业务链，区分产品目标、已有实现和实际验收；采用 PostgreSQL 后续开发基线。当前业务任务状态仍由 roadmap.json 管理。 [本任务报告](../development/evidence/README-20261025.md)。
