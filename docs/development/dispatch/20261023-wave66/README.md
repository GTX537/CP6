# W66 / 七天一期文件公共基础：附件解绑与物理处置分离及PostgreSQL租户事务保护

本聊天COORD单人，真实开工 2026-10-23T12:55:21.264813+00:00。Main `b6afa549972de3ab10b8cb20d93aba27842e40d1`；Handoff `8e4e41a9b84701059644463e0ec6fe28a6c8d743`。W65 Main437/438、Handoff41已采用，Issue436关闭；准确分支删除、固定树干净detached、全部席位释放，实际52.58min、前置约4min。七天目标ACTIVE，W37–W65不重复。

## 核对与准入

AGENTS/POLICY哈希未变，四仓固定席位容量无告警。Main/Handoff当前远端采用及原文件blob已核对；无重复开放任务/PR，WP02-14未领取。已完整读取SYS-FILE-01归并及15d5aa240b03f6ab原122行；核对WP02-14任务卡、ACT仅LOCAL_AUTHORITATIVE_GUARD的已登记修订、当前AttachmentService/Controller/接口/实体、Local/S3存储、PubUpload/API与原测试。这里不建立ACT或技术内容新提交门，不把WP02-10局部采用提升为完整前置。

[Issue439](https://github.com/GTX537/CP6/issues/439) P0：不同tenant相同文件共享不含tenant的MD5路径，但旧删除按本tenant引用数擦除全域blob；外层事务回滚也无法恢复已删物理文件。先以新隔离PG和实际LocalFileStore复现，不读取或删除现有用户文件。

## 实施边界

- 普通附件删除只移除本tenant引用；取消该路径对IFileStore.DeleteAsync的调用。即使本tenant或全表当前count为0，也不据此构造完整引用/hold/UNKNOWN/保留门。不新增自动清理器、期限或后台物理处置。
- PG元数据删除限定当前TenantId和准确Id，拒绝借外租户跟踪行扩大效果；只执行本次删除，不替调用方SaveChanges其他待写。已有调用方事务由调用方commit，数据库失败恢复本次保存点并保留先前工作。
- 接口返回UNLINKED、physicalDeletion=NOT_REQUESTED；页面确认及成功文案明确只移除引用，不冒永久删除、已排队清理或文件本身可用性。旧菜单校验保留真实兼容范围，不称已具备当前业务Owner/字段/用途/冻结门。
- 原上传、MD5历史提示/路径、下载、重绑、存储接口和旧证据保留；新修复不把MD5当安全ContentRef。无schema/迁移/依赖、无SQL Server新增实现或双库验收、无生产文件操作。
- 全域引用图/保留决定、tombstone和原operation处置receipt、扫描及不可变安全版本、实际业务Owner绑定/最终门仍在完整WP02-14后续范围；不得用停止物理删除冒称这些能力已验收。

## 时间、验证与收尾

预计实际60–90min：真实PG/文件红灯和实现20–30，事务/租户/文件边界与相关原控制器/页面验证15–25，集中SPEC/QUALITY、合并和网络收尾25–35。实际按同任务start到finish记录，子任务不重复累计。

固定DEV-1 `codex/bug-439-attachment-retain`、DEV-2 `codex/w66-handoff`，不新增树。使用现有新隔离PG夹具55435；文件根仅在该次拥有的测试根下创建随机新目录，证据/数据结束停止保留，不清理W51被拒data。验证跨tenant共享bytes、最后引用、caller rollback/commit、外tenant与待写跟踪、原生数据库失败后的先前工作，相关控制器及PubUpload行为；一次作者完整diff SPEC/QUALITY，不全仓回归。

一份报告，总台账/四状态随Main同任务提交；Handoff固定START/DELIVERY，正常合并核远端采用后准确分支删除、finish释放。完整24/375整卡、125目标0交付、247运行NOT_RUN和WP02-14 PLANNED/NOT_CHECKED不提升。无Actions、既有库迁移或部署。W51准确data删除自动审批拒绝原因blocked by policy；停止保留且不重试。

## W66验证完成

19项.NET独立用例通过（18项首轮成功复用、1项仅夹具修正后复验），5项页面行为通过，无跳过；相关Main构建通过。三个PG停止保留；原跨tenant文件丢失红灯与夹具42P16失败保留。进入两仓采用及同任务最后收尾；完整文件Owner不提升，七天目标ACTIVE。

## 实际采用与最后收尾

Main [PR440](https://github.com/GTX537/CP6/pull/440) source `6aa764609839a2be32fa91d90d1668c3fbb59b7b`采用远端main `f5bbbb08312170f6eda34f9d8d21582bb515a7bd`，全部20路径exact核对通过。Handoff [PR42](https://github.com/GTX537/CP6.AI-Handoff/pull/42)采用 `11fd7e78c55114c51f5f7ad4c7d10f6681917956`，准确任务分支删除、固定树干净detached、finish释放。十个产品路径与已测提交逐blob相同，未重复成功验证；最后文档diff和台账生成单独检查。

Main同一任务分支最后状态PR及finish待完成；Issue439在最终远端和生命周期核对后关闭。当前约29.88min，原估60–90min，最终以finish为准；前置只读核对约4min，子任务不重复累计。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave66-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续真实Owner前置和D6公共支撑。
