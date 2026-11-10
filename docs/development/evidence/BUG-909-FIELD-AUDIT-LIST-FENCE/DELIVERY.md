# BUG-909 — 字段审计摘要当前查询

W454-01；Issue [#909](https://github.com/GTX537/CP6/issues/909)；基线 `9bcec3b575c85caa5138f31e72c9d37776a8e5cf`。本报告记录实际本地结果；最终远端采用、分支处置及DEV-2释放以对应PR、远端main和 `manage.ps1 finish` 为准。

## 改动

原摘要列表在缓存动作检查后直接Count/读取。PostgreSQL现在由同Context的 `FieldAuditListOwnerV1` 使用既有 `sys-field-audit:query`、实际签名会话和认证父事务，认证共同根先于实际 `Sys_FieldAuditLogs` SHARE；计数、分页、安全摘要生成及最终会话/取消检查在同一当前边界内。数据库不可用和未知提交明确返回，回滚清理跟踪，不盲目重试。

原entityName/entityKey/userId/from/to筛选、页号至少1/页长1..200、ChangedAt和Id降序、原actor元数据以及 `FieldAuditChangesV1.Summarize` 全部保留。不可读历史仍 `changeCount=null` / UNREADABLE，空数组仍0 / READABLE，不返回完整Changes。实际表没有软删除字段，未加这一过滤或更改历史解释。历史非PostgreSQL完整列表体、原构造使用、record整段方法、原业务Owner/映射/安全投影/游标保持；没有generic history fallback。

新Owner只使用原菜单动作，未改变菜单默认授权、模型、解析器、包或工作流。既有详情仍要求实际业务对象/字段权限，摘要采用不扩大详情实体支持。

## 实际验证

- 同一冻结原生源码在原产品执行5项：3项列表当前授权/顺序问题复现，正常摘要和已有记录安全投影2项通过，零跳过。
- 修复后5项实际Main Kestrel/PostgreSQL18.6全部通过、零跳过。其中4项新列表行为、1项既有实际HTTP记录投影。禁用用户先完成为401，撤销当前query为403；原审计与无关库存保持，恢复身份/动作并重新登录可读。读取先获得认证根则200完成，之后撤权继续，后续读取403。
- 正常实际筛选包括实体、主键、用户和日期右开边界，正常/损坏Changes及其他租户同码记录；响应不含原RAW-AUDIT-VALUE或Changes。不可读为null/UNREADABLE，正常为1/READABLE。额外203条同时间模拟历史验证原200上限、页号0归1、Id降序及尾页3条，原历史/另一租户均保持。
- 既有record实际HTTP场景重新运行：仅当前Machine的Status/ActiveFlg安全投影，原Remarks及actor PII不进入详情，原历史不变；未知实体仍503 DETAIL_UNAVAILABLE。原映射版本、代码和解析器没有修改。

原生控制在已有认证根内用实际SQL修改模拟用户/动作，非生命周期HTTP；实际宿主、签名认证、权限与数据库执行，未触及用户环境审计。模拟历史用于相同摘要合同，不作为来源资格或正式业务决定。受影响Core/WebApi/Tests正常SDK本地构建通过，依赖/未变产品来源核对，Core沿用关闭全量分析器口径，保留原警告。

一项集中SPEC/QUALITY及权限专项审查覆盖完整diff、原列表代码等价和原record路径。私有证据位于 `D:/CP6-local-test/CP6-72H-20261109/W454-01/` 的 red/green build、hosted、实际锁等待/HTTP/审计及库存写集、TRX、源码/SDK产品摘要、`actual.json` 和停止记录；夹具均已停止。

## 采用、工时和余项

这只补WP02-03当前摘要读取，保留 [W64-01](../W64-01/DELIVERY.md) 的原安全历史范围和余项。完整列表资源/PII政策、强制原子审计写入、导出/hold/引用图/保留清理、完整字段V1、PGT-017及全Main/跨仓仍待对应实际采用；WMS-CTRL R3原生审批/Drain/应用协议也未据此完成。原375仍38 DONE/1 IN_PROGRESS/336 PLANNED，正式PG16 PASS/11 NOT_RUN。

W453经 [PR #908](https://github.com/GTX537/CP6/pull/908) 进入本窗基线，WP02-03交付核销，分支准确删除、DEV-2干净释放后复用。W453紧边界原生活跃17.39–17.47分钟，比25–40预测短，同框架非物理Owner改用15–30；本窗单查询预估15–25，收尾按原生采样校准，租期和进程不重复计时。

用户确认48小时预算172800秒；历史36小时及实际40小时144047采样保持，下一原生检查点158400。普通验证仅本地，无Actions调度、发布、部署、网络安全或防火墙操作。
