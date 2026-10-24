# W88-01：VMI当前库存观察与历史估算独立Owner

Issue [481](https://github.com/GTX537/CP6/issues/481)，COORD本聊天单人。Main基线`760fdd91e22d6a81fe66429f3d040f651bd7e71a`；任务`BUG-481-VMI-OWNER`，固定DEV-1，Handoff固定DEV-2。开工2026-10-23T23:48:27Z，预计50–75min＋独立前置3min，实际按finish、不重复计算Handoff。[范围与前置](../../dispatch/20261023-wave88/README.md)。W37–W87不重复。

## 合同与交付

已核当前WMS-VMI-01手册及b6a79341caa5168a__WMS-VMI-01_VMI作业与结算依据_完整详设_R2_CANDIDATE.md全部原件、WP10-39/40与G1–G4。原始历史平均估算、单位混合和Confirmed布尔不符合正式按日计费/Finance接受。本窗完成WP02-03当前独立入口和历史安全读取，未冒称完整VMI作业与计费采用。

`VMI-OWNER/1`三个具名查询连接当前原会话、query动作、可信租户/本人/部门2–4/全租户范围、原生权限及业务表读围栏、最终权限重核。当前库存为VMI范围与Stock Owner实际Stock.Id行交集，加有效仓库/库位与Wms仓区许可；真实stock UUID部门映射复用。历史账单独立使用实际vmi-billing UUID，保Creator本人/部门/租户过滤，不通过当前库存客户集合猜测旧账单权限。所有过滤在计数及分页之前，默认50/最多100，偏移溢出和客户/年月输入校验，客户+单位或稳定Id排序；这是当前观察分页，不宣称跨请求固定历史cut。

库存按原客户/UnitCd分组，未知单位不合计，跨单位无总和。聚合在PostgreSQL numeric中完成并转text后返回，不先压回.NET decimal；实际两条最大numeric(21,8)求和得到19999999999999.99999998。单行数量与旧账单数量/费率/金额全部精确十进制字符串，历史numeric(18,2)最大金额9999999999999999.99不经过JavaScript number。旧AvailableQty仅标旧物化余额，Free=null/NOT_EVALUATED，不等特定用途可用量。估值UNKNOWN/null，不把UnitPrice缺失当0或当正式估值。

账单原Begin/End/Avg/Rate/Amount/Confirmed保留，标POINT_IN_TIME_ESTIMATE，单位/币种/FinanceOutcome均UNKNOWN。金额和费率分别要求当前amount-read/rate-read动作及字段MIN；CustomerName/Remarks各自字段MIN；隐藏字段null且显式不可读。无按隐藏金额排序、筛选或汇总。新增query/amount-read/rate-read只登记菜单目录，不给角色自动授权。原生迁移保全部既有对象种类及不可变身份/租户/部门约束，真实同租户非删除账单UUID校验；IAM实际查找、绑定/禁用与页面选择器同步。

PostgreSQL旧读取409 OWNER_READ_VERSION_REQUIRED。旧calculate/confirm在当前动作和可见实际账单门后503 VMI_OWNER_ADOPTION_UNPROVEN，发生序号/账单/库存/Finance效果前拒绝。未造持久operation、未自动重试。旧非PG原行为及其两项测试保持历史范围，新开发仅PostgreSQL。真实Controller/Program响应no-store，Owner异常不会被旧catch降成400。

页面客户库存、库存明细、历史账单均使用新Owner分页接口，精确字符串原样显示，未知量纲/币种/估值不默认补值；年月输入为YYYYMM，旧确认标记不冒充送付/入账。身份epoch、当前自身/来源/敏感动作、客户/模式/筛选/分页变化与失败清旧值；迟到、乱序、卸载响应不能回填；核版本/来源、数量类型、实际客户与披露标志。无旧计费/确认操作。

## 验证与集中审查

18独立.NET用例（15原生PG、2既有非PG VMI行为、1两租户启动种子）及22Vue用例通过，无跳过；相关构建和vue-tsc通过，最终权限故障定向补验1项并入原15项、不重复计数，去重40项。一次作者完整SPEC/QUALITY覆盖当前会话/实际对象与来源交集、字段与精度、SQL聚合、分页、真实UUID映射及直接IAM消费者、原生并发、旧写零效果和页面乱序；无剩余阻断。最后仅补最终权限读取故障与检查相关变化，没有重复全套审查。

- `owner-red-20261023T235028Z.json`：3真实PG红灯，无Owner仍能旧读、计算、确认；原失败保留。
- `owner-initial-20261023T235545Z.json`：3红灯全部转绿。
- `owner-native-20261023T235659Z.json`：11/15通过，4项在测试准备阶段因重复客户＋年月违反现有唯一约束；修正夹具使用真实独立账单客户，不改产品约束，原失败保留。
- `owner-reviewed-20261023T235838Z.json`：18/18，无跳过；15原生PG覆盖混单位/未知单位、精确聚合/金额、三个接口103条分页、当前源交集/仓区/删除、本人/跨租户、真实部门2–4/IAM启停、跨租户映射23514、迁移重放不授角色、冻结输入/外层事务/预跟踪拒绝、真实Program/login/HTTP/no-store/撤权、旧写零效果。实际双连接pg_stat_activity观察原始账单UPDATE等待Lock，读取保旧值，提交后写入才放行。另2历史非PG行为和1两租户种子。
- `owner-final-fence-20261024T000006Z.json`：仅原边界用例补入最终权限查询不可达注入，1/1通过；整组结果复用，去重仍18项。
- `owner-ui.json`：22/22，9 VMI页面及13 IAM（新增真实账单UUID选择/保存）；`owner-types-result.json`实际exit0。
- 5个自有PG串行55435，ownership/stopped齐、无postmaster.pid，数据/日志/TRX/失败全部保留。无全仓回归、Actions、既有库迁移、镜像或部署；已有构建警告保持。未执行现场浏览器或正式业务验收。

## 采用和剩余

正常Main产品PR→远端精确采用→Handoff DELIVERY/合并/finish→同任务Main最后状态PR/finish。最后证明前不提前声明分支删除或席位释放；实际计时以生命周期为准。

24/375整卡、125目标0完整交付、247正式运行NOT_RUN保持。完整WP10-39/40、16AC、G1–G4按日历史切点/Root/Slice/合同/换算/币种舍入/OA/Finance仍UNPROVEN；CRM真实Site/Form Owner/公开受理等继续对应窗口。七天目标ACTIVE。W51指定停止data删除被自动审批以blocked by policy拒绝，原地保留不重试。
