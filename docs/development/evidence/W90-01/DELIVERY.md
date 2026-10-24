# W90-01：套件主数据与历史订单独立Owner

Issue [485](https://github.com/GTX537/CP6/issues/485)，COORD本聊天单人。Main基线`d65abaf82b6b0ac339d0fc3b6ce945dfc82ddf4f`；任务`BUG-485-KITTING-OWNER`，DEV-1 Main与DEV-2 Handoff保持原仓复用，无跨仓bind。Main开工2026-10-24T00:43:24Z，预计45–65min＋独立前置粗估4min，实际按finish、不重复计算Handoff或与W89保存重叠的只读准备。[范围与前置](../../dispatch/20261024-wave90/README.md)。

## 实际改动

本轮全文核对WMS-KIT-01设计手册、WP10-26/27任务、当前服务/接口/实体和页面。复用原接受边界，不声称重新全文读取所有原始工程材料。旧查询直接返回实体；旧主数据/订单写入口没有当前独立Owner。真实PG红灯复现缺Owner仍读取、创建主数据和取消订单。本窗修补WP02-03现存入口安全边界，不把旧逐腿ApplyAsync改称正式完整Composition。

`KITTING-OWNER/1`四个GET提供当前主数据列表/组件详情及历史订单列表/详情。当前可信原会话、wms-kitting:query、租户/本人/部门2–4/全部范围在数据库计数及分页前生效；kit-master和kit-order分别以真实原UUID绑定，当前配方不能授予历史订单权限。两个真实IAM查找/绑定入口与原生租户/对象不变约束同步，迁移只登记query目录，不自动给角色授权。默认50/最大200，稳定SKU/Id或CreateDate/Id排序；只查询码/单号，不借隐藏名称筛选泄露内容。跨页是当前观察，未宣称固定历史cut。

订单要求有效原仓库/库位与当前仓区许可。主数据只有默认仓库、没有源区身份，因此部分区许可不能当整仓许可；没有默认仓库的合法原记录仅在明确全仓权限下可读，空仓库授权不等于全仓。主数据及组件保现存数据语义，未伪造不可变工程版本、Basis或已批准配方。组件RequiredQty和订单Qty按原numeric(21,8)转精确十进制字符串，未经JavaScript number；组件各自单位保留，不跨单位求和。历史订单未存单位，UnitCd=null/NOT_RECORDED，不按当前组件或当前配方补造。旧Executed/Cancelled只作为历史标记，CompositionState仍UNPROVEN。

KitName、ComponentName、OperatorCd、Remarks、ExecutedTxnNos均使用当前字段MIN。流水原字符串仅当每一个非重复引用都能匹配同租户真实StockTransaction、原RelatedNo/RelatedType，且Stock Owner当前对象范围与所有原仓位均可见时原样披露；原料腿不因订单目标仓位可见而自动获准。缺/错/重复/畸形/无权来源一律null并标不可披露，原值仍在数据库中；原记录没写流水也不证明没有发生效果。真实来源当前权限参与最终重核，引用集合齐全不等于完整组成或成功证明。

Owner复用原生身份围栏、固定排序业务表读锁与最终当前权限重核，新增KitMaster/KitMasterComponent/KitOrder纳入图；原始SQL写者同样被读锁阻挡。不接受外层事务、预跟踪业务实体或跨context Owner。PostgreSQL全部旧GET经Owner后409 OWNER_READ_VERSION_REQUIRED；六种旧写（主数据建/改/删、订单建/执行/取消）在当前动作及适用现有对象可见性之后503 KIT_OWNER_ADOPTION_UNPROVEN，任何实体、序号、库存/消息效果之前拒绝。没有启用新正式业务写、自动重试或虚构operation。非PG私有旧实现及7项历史用例保留，新开发仅PostgreSQL；接口原先无依据的全组原子注释已纠正。真实Controller/Program均no-store，Owner异常不被旧catch降成400。

页面分当前主数据与历史订单，保留原数量和未知含义，不提供旧写按钮。身份epoch、当前动作/Stock来源动作、模式/筛选/分页变化立即清空；迟返、乱序、失败和卸载响应不回填；响应版本、精确字符串、披露标志、实际选中UUID及业务码都需对应。false启用筛选和0草稿标记原样保留，清空选择器统一正规化。

## 验证与集中审查

24独立.NET用例（16原生PG、7既有非PG套件行为、1两租户种子）及25Vue用例通过，无跳过；相关构建和vue-tsc通过，定向补验与原组去重合计49项。一次作者完整SPEC/QUALITY审查覆盖分支相对基线全部产品差异及新增文件、权限/原始会话/真实来源/字段与精度、两个UUID映射、原生并发、旧写零效果和实际HTTP/UI。集中审查发现并收紧空默认仓库与空授权的边界，仅对修改范围补验；无剩余阻断。

- `owner-red-20261024T004509Z.json`：3真实PG红灯均因预期Owner拒绝未发生；原失败保留。
- `owner-native-20261024T004850Z.json`：23/23，无跳过；15原生PG覆盖真实分页203条、历史单位/当前配方区分、跨租户/本人/删除/仓区、部门2–4独立映射与禁用、实际来源每条及源库位、隐藏字段、六旧写零效果、冻结输入/最终权限读取故障/外层事务/预跟踪拒绝、原生跨租户23514与迁移重放不授角色、真实Program/login/HTTP/no-store/撤权。另7既有非PG行为及1两租户种子。
- `owner-review-supplement-20261024T005348Z.json`：2/2，原来源测试补方向/原RelatedType不匹配，新增无默认仓库仅明确全仓权限可读；前者与原组去重，因此NET为24、原生PG为16。其余产品/依赖/环境未变范围复用前一组成功证据，不改称再次执行。原生双连接pg_stat_activity实际观测KitMasterComponent UPDATE等待Lock，读取保持旧值，提交后写者才继续。
- `owner-ui.json`：25/25，10套件页面行为与15IAM绑定（含两种套件UUID真实选择/保存）；`owner-types-result.json`实际exit0。
- 3个自有PG串行55435，ownership/stopped齐、无postmaster.pid；数据、失败、日志与TRX保留。无Actions、全仓回归、既有库迁移、镜像或部署；既有构建警告保持。未执行现场浏览器或正式业务验收。

## 采用、资源与剩余

待Main产品PR精确采用→Handoff DELIVERY/合并/准确分支删除/finish→同Main任务状态收尾PR/准确分支删除/finish；最后证据前不提前声明释放。固定目录保留复用，结束证明保存实际耗时、工作区与席位。

24/375整卡、125目标0完整交付、247正式运行NOT_RUN保持。完整WP10-26/27、KIT的K-G01–K-G06、15AC、准确工程Ref/冻结版本/UOM转换/Root/Claim/SourceBudget/共同QuantityUoW/真实物理闭合和恢复继续UNPROVEN/NOT_RUN。七天目标ACTIVE。

W89跨仓切换组合被自动审批以`blocked by policy`拒绝，CRM未开工；Main484仅准备文档采用，W89 Main/Handoff原分支可靠保存并已deferred释放，明确确认待答复。本任务只复用原仓席位，不重试或绕过bind。历史W51指定停止data删除也保留原拒绝边界，不重试。

## 实际采用与最后收尾

Main [PR486](https://github.com/GTX537/CP6/pull/486) source `5f3f648854a8d201d934d0a6e19bbdbf39094167`采用远端main `6f19fde5e3d9ea4483c8c04c9713c6743f835007`，30任务路径逐blob核对；Handoff [PR65](https://github.com/GTX537/CP6.AI-Handoff/pull/65)采用`194b2cdecad3322d7f5a87b1d5caf747913c7cc8`，准确分支删除、固定树干净detached、finish释放。20个产品路径与已测提交逐blob一致，复用有效结果，最后文档diff/台账生成单独检查。

Main同任务最终状态PR/finish及Issue485最后关闭待完成；当前约17.92min，原估45–65min＋独立前置4min，实际按finish不重复计Handoff。最终证明`C:\Users\tt\CP6-planning-reports\20261017-foundation\wave90-final-verification.json`。无Actions、既有库迁移或部署。七天目标ACTIVE，继续剩余独立Owner及D5/D6真实前置，正式KIT Composition仍未采用。
