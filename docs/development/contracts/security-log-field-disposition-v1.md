# W321：准确安全事件的已分类字段处置 v1

原WP02-15逐Owner推进，沿原24h目标，不新开周期。本窗Main.SecurityLog.Fields.v1仅处理Sys_SecurityLogs准确Id中已有W311主体字段分类的UserName/ClientIp；原TenantId/UserId、事件类型及原时间保留。UserAgent、Reason、RequestTenantCode、ContextJson、Creator/Modifier等未核内容明确未覆盖；UserId为空不按同名猜归属。来源为当前SubjectExportCatalogV1/Reader、Sys_SecurityLog、实际认证/IAM审计写者与SecurityLog只读查询入口。暂无原生安全日志UPDATE/DELETE调用；OperLogCleanup只处理不同的Sys_OperLog，保持其真实范围，旧直接擦除仍410。

## 设计与授权

用户已批准24h连续单人执行及逐Owner处置/恢复；本窗COORD按现源码确认并登记共享窗口。brainstorming在此完成取舍：仅隐藏页面不能清实际字段；整行/自由文本扫描会扩大未知归属与审计损失；选择已分类准确字段的实际清理，保留最小事件身份及其余明确余项。通用重复确认不覆盖项目连续窗口授权；当前同Root/权限/事务/hold/原命令/恢复合同直接采用，不新造资格层。

原CreatedAt/CreateDate为timestamp without time zone，不能当UTC或法律锚点。新Owner政策由独立当前能力管理员设置target tenant、BasisRef、enabled和明确NotBeforeUtc；输入按UTC及PG微秒精度规范化，最终决定点用数据库Clock。政策不从旧时间猜期限，也不因“日志”一名套用7/30/90天。缺合法依据可用明确模拟政策验收，报告不冒真实法定结论。

## 准确范围与效果

每计划一个准确LogId；从实际行派生target tenant/stable UserId，不接客户端Actor/Subject/字段任意列表。按operator tenant所属政策管理，日志目标可为显式其他租户；独立security-read只授安全元数据/版本，不授原用户名/IP/自由内容。security-policy/prepare/approve/revoke/execute分别授权，不自动授角色。由当前UTC政策、准确行版本/分类字段摘要和全主体hold冻结scope digest，申请人不能自批。

所有原生日志写者加入已有IAM共同根，私有行修订按实际INSERT/UPDATE推进，ABA失效；不另改EF模型/旧SQL Server实现。新行为仅将UserName/ClientIp真正置NULL，保留Id/TenantId/UserId/EventType/CreatedAt以及其余字段。最小执行/原命令/不可变墓碑同commit，源值只在已有边界投影/摘要计算，不复制旧PII到处置历史。没有字段值时不能冒新清理成功。

原生无所属来源的字段清理被拒绝；已处置准确事件不能通过UPDATE/INSERT改身份或恢复所清字段。原生DELETE/TRUNCATE仍受共同根及现全主体hold；没有声称新增完整审计保留/整行purge Owner或改变旧未处理字段合同。原生日志创建允许匿名事件/可空字段，正常认证/IAM审计仍能真实持久化。只有相关源写入被修改，才做相应定向认证/审计回归，不全仓回归。

复用PrivacyDispositionOwnerV1可信独立事务/当前实际人/最终会话、Can、Clock、Hold/AppendHold，独立Owner原命令命名空间。已知原结果在执行撤权后仍可读；scope/当前批准/政策/hold/版本在最终根内核对。真正COMMIT传输未知保原UUID，NOT_FOUND不等无效果，关闭阻止迟到；明确Owner23514拒绝证明回滚，按具体错误返回。当前原用户行不存在也能由准确日志管理同一主体hold，旧身份入口不改变资格。

## 恢复与页面

新政策/墓碑参加真实restore_epoch。原SourceRoot→CandidateRoot和两次提交责任直接采用；当前源政策NotBefore/hold及准确新计数进入预览/独立审批。候选外部边界一次核对实际事件身份、版本及已分类字段摘要；存在才实际清字段，已为空或整行缺失分别如实记录并安装相同来源屏障，不删除未知内容或伪造候选本地审批。候选父回执/效果/开放标记同事务，延迟约束拒绝静默跳过的字段UPDATE，源结果未落定继续隔离。

RestoreScope新增security字段墓碑计数为0时省略，Unit新增LogId为空时省略，保持旧身份及原生载荷0新Owner时wire/摘要。明确显示日志字段的实际结果，不能把它当载荷删除字节或完整安全日志匿名化。页面沿现GDPR分面、独立两人、执行确认、身份分区UUID/navigator.locks、取消零写和迟到身份清理；原回执独立显示，计划刷新失败不露旧执行按钮。

## 实施顺序与验证

1. 实际行为红测：旧安全日志字段可被无依据清理/复活；独立新读权；再实现前向DDL、准确Owner政策/预览/审批/字段效果/原结果。
2. 同Root的原生修订/字段边界、最小回执/墓碑/原命令同事务；当前hold/撤权/ABA、未知和关闭、故障原子回滚。
3. 已接恢复候选真实pg_dump/pg_restore、旧/空/缺失/冲突记录、当前源版本/政策/hold、候选与源结果未知及实际字段效果。
4. 严格新API/GDPR页面，真实认证HTTP/两人Chrome和相关Vue/类型；真实认证/审计、既有身份/载荷恢复必要回归；一次COORD任务级SPEC/QUALITY审查。
5. 单报告/roadmap/生成页/四状态同任务同步，正常Main/Handoff采用、准确分支清理与官方finish释放。仅新隔离PG18及模拟业务/政策，未授权生产部署或既有数据库迁移/处置/恢复。

原37536DONE/2IN_PROGRESS/337PLANNED，原59余47，125/247不改，WP02-15仍PARTIAL。W320实测6204秒/约1.72h，对原2–3.75h偏快；本窗新原生认证审计写者与旧时间边界增加风险，初估2.5–4h低置信实际执行，按实际推进，不空等凑时；下一72000秒校准。若较快结束，继续原范围相邻真实未完任务，W310预算末尾。
