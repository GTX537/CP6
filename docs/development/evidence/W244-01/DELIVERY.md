# W244-01 统一排程看板交付

W244统一排程看板：明确Site、完整引用筛选、同捕获KPI与稳定分页、未知/未排/历史状态、工厂日周时间轴和准确操作详情。

**生产业务验收采用模拟数据。** 工单初始/工程快照、资源/班组/工序引用、Site/时区、工单状态与交期、执行观察明确模拟；捕获持久化、实际查询/分页/权限、HTTP宿主和Vue组件实际执行。本范围验收通过；实际环境为本地隔离PostgreSQL18、Main API和组件运行器。生产部署：未执行。

## 实际交付

- 工厂授权上下文与本地业务日默认区间；完整引用/日期/状态一次筛选，KPI与条形/分页来自同一不可变capture，30分钟到期，更新来源不混页。
- 当前三Owner对象权限交集；读取原捕获重新核对当前可见范围。部分工厂权限的指标为null/PARTIAL，不返回隐藏总数；冲突隐藏身份只返回占用区间，未知占用明确警示。
- 已采用操作直接引用当前Root、Occurrence/Snapshot/五域、实际观察/数量单位基准和队列头。无计划不补造时间，旧状态6为历史已检验；历史无准确身份/基准保留只读及未知。
- 看板首次选择明确Site、日周时间轴按工厂日历跨DST、计划/实际/交期分层、未排区和准确单操作详情；原系统参考按需加载。旧响应不能覆盖新筛选，身份/授权变化清除私有结果，失败保留STALE/asOf，初次失败未知。
- 新模拟来源入口检查准确原生操作清单及来源版本，持久保存原来源，复用现有内部合同与不可变documents；没有新资格包装或额外角色授予。

[详细合同](../../scheduling-board-v1.md)。正式写入沿用W240–W243，不改变库存、数量或现有写协议。

## 实际验证及审查

9 relevant PostgreSQL18/actual HTTP board scenarios PASS;23 Vue scenarios PASS (8 board,7 time,8 existing planning);app vue-tsc PASS. Simulated production business inputs, actual captures/read transactions/authorization/API. Initial broad Board_ selector included15 unrelated successful tests and2 out-of-scope skips; exact selector8/8 plus affected delay regression2/2, skipped0; incidental results not counted.

九项场景：同捕获KPI/分页面对来源更新保持一致；部分权限及隐藏冲突隐私、撤权原捕获拒绝；未排/实际未知/完整空结果区分；历史已检验和DST歧义保留；过期、来源版本/清单和非法查询边界；准确Occurrence/根上下文和工厂默认日期；日期/状态/完整引用筛选、延迟小数与未知资源占用；取消操作不抬高工单延迟和完成分母；实际HTTP分页、偏移拒绝和详情。

One author task SPEC/QUALITY review: full query and capture identity, consistent projection/KPI/page cut, current visibility and hidden conflict privacy, simulation source version/manifest boundary, exact occurrence/snapshot/five-domain reuse, legacy status6/DST ambiguity, nullable metrics/actuals, date/state/ref filters, actor invalidation and out-of-order UI responses. Corrected detail-403 invalidation of an in-flight board, unknown occupancy disclosure, exact decimal delay arithmetic, cancelled operations excluded from delay maximum, and factory calendar-axis DST; affected tests passed. No extra qualification layers.

后端来源：`D:/CP6-local-test/wave244/foundation/pg-reviewed-20261101T180229Z.json`与`pg-delay-20261101T180704Z.json`及TRX。前端：`D:/CP6-local-test/wave244-web-final.log`23项与`wave244-web-final-types.json`exit0。首轮宽匹配额外运行的无关成功/跳过均不计验收，日志保留；精确筛选8通过、后续延迟定向2通过，合计9个不同场景、0跳过；7个未受局部修正影响的成功结果直接复用。本范围无失败场景。既有不相关编译警告保留。未执行全仓回归、浏览器布局或正式生产候选，不声称通过。未触发Actions；工作流和依赖无变化，推送前复用同内容事件审查并核对当前引用。

## 剩余和生命周期

本轮不关闭完整D3/WP08-10/WP08-11或原七天目标。原D3全部AC01–36、拖拽草稿/独立详情路由和跨阶段剩余仍待核对。模拟业务来源，实际本地隔离PostgreSQL18和HTTP；未生产部署、未既有环境迁移。

D1/D4闭环，D2/D3/D5/D6/D7仍部分完成；2/7不是工程比例，375正式任务/125目标/247验收未由本窗口改变。下一步：按原MES-SCHED-01 AC01–36核对D3综合验收，补齐拖拽草稿/独立详情路由等真实差距；继续原D2/D5/D6/D7直接剩余范围。生产业务验收缺失输入直接模拟，不等待企业数据。

COORD本聊天单人，复用DEV-1/DEV-2，基线`31b7f02bfd510da113c6889532ed2a11ab15be0a`，预计实际45–80分钟。W243按最终proof仅归档一次。此为合并前快照；准确PR、分支清理、工作区状态、官方finish/席位释放和实际时长以本轮最终proof核销，固定路径/缓存保留。

Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/220)已完整采用并finish；Main待最终核对。本快照距开工22.49分钟。
