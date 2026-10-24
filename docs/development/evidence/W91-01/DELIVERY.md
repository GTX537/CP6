# W91-01：报表中心独立当前Owner

Issue[488](https://github.com/GTX537/CP6/issues/488)，COORD本聊天单人。Main基线`9991c1734520bf7d9b421ad865485b27cc97d84d`，任务`BUG-488-REPORT-OWNER`，DEV-1 Main和DEV-2 Handoff原仓固定复用；无bind或新目录。Main start2026-10-24T01:06:50Z，原估45–70min＋独立前置约4min，最终按finish计且不重复计Handoff。[范围和前置](../../dispatch/20261024-wave91/README.md)。W90全部收尾证据已归入台账，W89保留确认边界。

## 实际行为与范围

全文读取当前ReportCenter服务/接口/Controller、实体、相关历史测试、页面，以及WMS-ANALYTICS-01手册、WP10-44/45任务跨度；不声称新全文读取17份原始附件。三个真实PG红灯证实无Owner仍返回空报告、原流水和直接CSV。当前限定修复属于WP02-03入口权限，不把旧聚合升级成完整Analytics。

`REPORT-CENTER-OWNER/1`两个新GET分别读取当前原库存与原流水。可信原会话、独立`wms-report-center:query`与`wms-stock:query`，各自租户/本人/部门2–4/全部范围及仓区原UUID交集在真实计数/分页前生效；当前有效仓库、原库位及软删除同步过滤。库存和流水各自使用既有可信`stock`/`stock-transaction`绑定，不从关联业务号或当前库存推导历史流水归属。原生固定顺序身份/业务表读锁、参数冻结和最终两种权限重核覆盖整个读取；原始SQL写者同样被读锁阻挡，不开放外层事务、预跟踪业务实体或异contextOwner。

默认50/最大200，库存按仓/库位/产品/批次/UUID，流水按原时间降序/UUID稳定排序；5001实际流水验证旧5000上限之后仍可分页且总数准确。每次请求是当前观察，跨页不承诺冻结集合。过滤只允许原业务码，不提供隐藏操作者/备注/货主值查询；显式空批次在服务端可独立过滤。原数量numeric(21,8)转精确字符串，最大正值和负调整值均原样，不按TxnType改符号，不跨单位/货主合计。未知单位和原货主类型显式保留；旧Allocated/Available只标原记录值，Free未按RequestedUse评估，金额/估值未知。

日期过滤是原TxnDateTime无Site时区的记录轴，成对ISO日期，合法年份与最后日亚秒不丢，9999-12-31不AddDays溢出；不混称历史业务cut或knowledgeCut。原流水不是完整发生身份，不能据此计算正式频次、ABC或历史净腿。OperatorCd、OwnerCd、Remark按当前字段MIN；跨业务RelatedNo/RelatedType暂不披露，原数据库值不改、不伪造来源Owner已采用。冻结报告、完整纠错/lineage、Root/Claim/Restriction、正式Free/估值及导出仍留原完整合同。

五种旧PG报告经当前query门后409 OWNER_READ_VERSION_REQUIRED。五条旧HTTP CSV路由先经独立export当前动作门，随后503 REPORT_SNAPSHOT_ADOPTION_UNPROVEN；在旧数据查询和制品生成前拒绝。通用同步ExportCsv的游离IEnumerable没有冻结快照/当前行字段证明，PG在枚举前拒绝REPORT_EXPORT_CONTEXT_REQUIRED，不能从服务直调逃逸；非PG原实现私有保留，7项历史行为通过。所有真实HTTP端点no-store，Owner异常不降为400。菜单481锚定现有派生键；只登记query/export目录，数据库迁移及启动重放不自动给角色授权，无EF模型改变。

页面改为当前库存/原流水两种原记录视图，精确字符串直接展示、原时间不改时区、未知不补默认，正式CSV/报告不提供成功入口。权限/身份epoch、模式/条件/分页改变立即清旧；迟返/旧模式失败/卸载不回填。验证返回版本、语义状态、来源模式、页码/数量类型/字段披露及重复对象，错误则清空。当前页面只证明相关Vue行为，未声称真实现场浏览器或正式业务验收。

## 验证与集中审查

24独立.NET（16原生PG、7既有非PG报告行为、1两租户目录种子）与9Vue通过，无跳过；相关构建及vue-tsc通过，定向补验去重合计33项。一次作者完整SPEC/QUALITY审查覆盖全部分支产品差异、新文件、源Owner交集、目录不授角色、原始值精度/未知、日期/分页、十旧HTTP路径及通用导出门、页面竞争。审查补验最终来源权限失败，未改产品算法；其余有效证据按原来源复用，没有重复全仓检查，无剩余阻断。

- `owner-red-20261024T010752Z.json`：3实际PG失败，均为缺Owner未拒绝；原失败/log/TRX保留。
- `owner-native-20261024T011058Z.json`：24/24。16原生PG覆盖原步骤转绿、5001真实分页、跨租户/独立双方scope/部门2–4绑定与禁用、仓区/库位/删除、字段/来源、最大精度与未知、最后日微秒与全合法日期范围、原查询冻结、Owner最终权限故障、外层事务/预跟踪拒绝、查询/导出互不授予、目录重放不授角色、实际Program/login/HTTP/no-store/撤权及pg_stat_activity观测原生写锁等待；7旧非PG报告与1两租户种子。
- `owner-review-supplement-20261024T011352Z.json`：1/1，仅同一已计用例追加Stock来源最终权限读取失败也拒绝；不重复计入33。接口补充历史范围注释不改变行为。
- `owner-ui.json`9/9：精度/未知及真实分页、两个当前动作、身份撤换、模式和筛选/日期/分页变化、旧成功/失败/卸载迟返、异常契约/隐藏字段/源引用拒绝；`owner-types-result.json`实际exit0。
- 3个自有PG串行55435，ownership/stopped齐、无postmaster.pid，原数据/日志保留。无Actions、全仓回归、镜像、既有库迁移或部署；既有构建警告未改。

## 采用与剩余

待Main产品PR精确采用→Handoff DELIVERY/合并/准确分支删除/finish→同Main任务状态收尾PR/准确分支删除/finish→Issue488关闭。完成前不提前声称席位已释放；固定目录留复用，最后证据记录准确来源和实际耗时。

24/375整卡、125目标0完整交付、247正式运行NOT_RUN保持。完整WP10-44/45、N-G1–5、18AC以及固定原生cuts/双时间/正式算法/冻结发布与导出保持UNPROVEN/NOT_RUN；本窗不代替完整已接受Analytics。七天目标ACTIVE，继续D2–D7必要工作。

W89组合跨仓切换自动审批返回`blocked by policy`，CRM未开工。Main484仅准备文档采用，两个原W89分支保存并deferred释放，明确确认仍待答复；本窗不重试或绕过bind。历史W51指定停止data删除同样保留不重试。
