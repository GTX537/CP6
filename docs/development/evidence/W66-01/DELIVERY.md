# W66-01 / Issue439 附件引用与共享内容保留

COORD本聊天单人，2026-10-23T13:16:42.099771+00:00。基线Main`b6afa549972de3ab10b8cb20d93aba27842e40d1`；固定DEV-1、任务`BUG-439-ATTACHMENT-RETAIN`、分支`codex/bug-439-attachment-retain`。[Issue439](https://github.com/GTX537/CP6/issues/439)。本报告是唯一产品交付报告，采用和最终生命周期追加于本报告/PR。

## 原缺陷与修复

原路径按MD5/扩展名共享物理内容，却只在当前tenant计算引用；A删除自己最后一条记录后擦除B仍使用的文件。旧路径还在调用方事务提交前删除文件，rollback恢复数据库行却无法恢复bytes。真实新隔离PostgreSQL＋LocalFileStore红灯确认：B元数据仍存在但文件已丢失。

普通AttachmentService.DeleteAsync不再调用IFileStore.DeleteAsync；删除最后可见引用也不授权擦除共享内容。这个防丢失变更是共享服务逻辑，历史非PG路径也停止物理删除；没有新增SQL Server实现或双库验收。全域物理处置必须等独立存储Owner完整引用/hold/UNKNOWN/保留决定，本窗不新增清理任务、期限或自动垃圾回收。

PG解绑用当前TenantId及准确Id的单条原生DELETE，不用Find的外租户跟踪对象，不SaveChanges无关待写。已有事务保留调用方commit，原生数据库失败回滚本次保存点，先前工作可继续；已删除的本地目标跟踪被移除，caller rollback可重新读取原记录和仍保留的内容。无外层事务时单条DELETE自行原子提交；没有假造跨文件系统原子删除。

API成功确认仅UNLINKED、physicalDeletion=NOT_REQUESTED，不表示已完成永久删除、已安排清理或证明原文件当前可用。PubUpload按钮/确认/成功明确移除引用，新页面遇旧服务器缺此确认保留当前显示并提示刷新核对，不报成功。四条五语文案通过原seed加入，未迁移现有环境。

## 验证与审查

- `D:\CP6-local-test\wave66\foundation/attachment-red-20261023T125736Z.json`：实际PG＋磁盘原问题1失败，A删除后B文件丢失；失败保留。
- `D:\CP6-local-test\wave66\foundation/attachment-first-20261023T130927Z.json`：19项18通过1失败。失败发生在夹具建表阶段：PG 42P16不允许临时表外键引用永久表；没有进入待验证的失败恢复路径。
- 仅将该夹具改为本次新隔离库内唯一命名的引用表、finally准确DROP；产品代码未变。`D:\CP6-local-test\wave66\foundation/attachment-fixture-fix-20261023T131232Z.json`该1项通过，真实FK 23503失败后保存点恢复/先前工作commit通过。其他18成功结果按原来源复用，不冒充再次执行。合计19项.NET独立用例（4原生PG、5原服务、10控制器）全部成功。
- `D:\CP6-local-test\wave66\foundation/attachment-ui.json`：5项Vue行为通过，含既有3项宿主按钮权限，以及明确解绑确认后成功、旧响应缺确认不报成功。真实SFC/ElementPlus组件和API替身，不称生产端到端。
- 原生PG＋实际文件覆盖跨tenant共享bytes、最后引用/重复解绑仍保留内容、caller rollback恢复可读记录/commit只移除引用、外tenant脏跟踪不扩大删除、不提交无关待写，以及上述真实FK失败恢复。现有菜单授权原单测继续通过，但不是完整业务Owner/当前撤权证明。
- 相关Core/Entity/API/Tests构建通过，已有依赖/nullable/EF警告保留。一次作者完整SPEC/QUALITY核对租户谓词、原生DML和保存点、无物理调用、跟踪目标、接口声明与页面不确定结果、配置/依赖和全任务diff；测试夹具问题定向修正，无剩余阻断项。

三个本轮PG簇均停止、无PID；PGdata/日志和剩余本轮测试文件保留。未接触现有用户/生产文件。没有Actions、部署、现有库迁移、真实云存储删除或生产验收；没有全仓/全业务Owner回归。

## 范围与收尾

旧菜单级上传/查询/下载/绑定授权仍是legacy范围；新业务对象/字段/用途/冻结共同门、扫描、不可变安全ContentRef、tombstone、处置operation回执和完整引用/保留图未实现。本窗不把停止擦除当成完整文件Owner；原上传MD5/路径/下载/重绑保持历史含义。完整WP02-14 PLANNED/NOT_CHECKED、24/375整卡、125目标0交付及247 NOT_RUN不提升。

Handoff首次start在只读git ls-remote --heads挂起。核实本次孤立Git子树21368/26380/35080后停止，管理脚本正常返回原180秒超时并释放自身锁；未手改登记或抢占席位。重试只增加当次进程低速超时配置、未改全局Git/代理；W66-HANDOFF正常start成功，START仅提交一次。外部协调helper首次分支名断言失败在任何写入前发生，修正为实际w66分支后正常登记。

原估60–90min，实际以Main start到最终finish计算，子任务不重复累计。候选通过，待Main/Handoff正常采用、最后台账与准确分支/席位收尾。七天目标ACTIVE继续。W51准确data删除曾被自动审批拒绝，原因blocked by policy；停止保留且不重试。

## 实际采用与最后收尾

Main [PR440](https://github.com/GTX537/CP6/pull/440) source `6aa764609839a2be32fa91d90d1668c3fbb59b7b`采用远端main `f5bbbb08312170f6eda34f9d8d21582bb515a7bd`，全部20路径exact核对通过。Handoff [PR42](https://github.com/GTX537/CP6.AI-Handoff/pull/42)采用 `11fd7e78c55114c51f5f7ad4c7d10f6681917956`，准确任务分支删除、固定树干净detached、finish释放。十个产品路径与已测提交逐blob相同，未重复成功验证；最后文档diff和台账生成单独检查。

Main同一任务分支最后状态PR及finish待完成；Issue439在最终远端和生命周期核对后关闭。当前约29.88min，原估60–90min，最终以finish为准；前置只读核对约4min，子任务不重复累计。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave66-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续真实Owner前置和D6公共支撑。
