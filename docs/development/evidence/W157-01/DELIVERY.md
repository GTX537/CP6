# W157-01 版型原订单关联共同事务与原键恢复

COORD单人，Main基线5a21aa7b643384a90f9ea40d0f59fafcf6378753，固定DEV-1/DEV-2。[计划与范围](../../dispatch/20261026-wave157/README.md)。预计70–120分钟，Main START至本登记26.16分钟；实际结束以官方finish/proof计时，Handoff不叠加。原七天目标ACTIVE。

## 实际交付

在既有真实版型上提供CREATE_OR_LINK，采用W156原UUID、完整身份/观察Ref以及显式当前原生令牌。原OrderWriteOwnerV1新增程序集内同DbContext/实际事务参加，复用原订单add/字段权限、可信部门/完整对象范围和最终权限检查；原OrderService执行既有购买品草稿创建，不内部commit、不调用PE/WMS/MES桥。原OrderReadOwnerV1在同事务验证当前query、当前原订单及全部明细范围并返回最小原UUID/编号/令牌，不泄露价格金额。普通订单原有自有事务入口不变。

新关联要求当前工具query/edit、精确工具Ref/原生令牌，实际创建还要求原订单add及两金额字段写权限，当前原订单query用于关联与结果读取。原Order/Detail效果、工具ArrangeNo回填、不可变关联原件/摘要、操作输入/结果/审计共同提交；完整工具身份保持，旧15位商业映射不是工具身份，原金额15,4无损进入订单21,8。原订单主体和行UUID真实读取，未构造Sales Schedule/ReleaseLineage。

已有ArrangeNo只允许唯一存活原明细且没有其他工具共享，不存在/多义/共享不重建。已有登记以不可变真实UUID恢复，原指针漂移拒绝新创建并标记当前不适用。重复操作同键同体返回原结果，异体拒绝；提交响应丢失通过原键读取COMMITTED，UNKNOWN可以查询或close，CLOSED禁止晚到提交。恢复使用当前工具和原订单query/对象范围及原actor，不要求已撤销的写权限。原件变化不伪装为当前批准。PE状态固定NOT_EXECUTED，来源种类NATIVE_ORDER_LINK_ONLY；无NoOp成功或正式业务批准声称。

PG前向迁移20261027010000建立租户+工具原UUID唯一、原明细唯一的不可变关联；记录原工具Ref、订单/行UUID及令牌、工具更新后令牌和原结果，摘要校验及插入时真实原表/来源核对。无猜测回填、角色授权或既有环境迁移。工具ModifyDate参数显式Npgsql Timestamp，保留旧timestamp without time zone语义。

API /api/plate-molds/order-link-owner-v1提供scope、原关联、commands和operations查询/close，实际认证/CSRF/no-store。既有工具来源页通过“原订单关联”按钮采用；页面保存当前源Ref/原生令牌及明确部门，版本冲突保留输入，pending只存原操作/工具UUID并按租户actor隔离、Web Locks互斥，未知结果不重发。当前401/403及身份变更清理私有内容、过时响应丢弃；PE未执行与原件变化明确显示。

## 验证与审查

15项本地PG18/真实HTTP行为检查通过，覆盖真实原订单/精度/完整身份、前15位相同工具不合并、缺失/多义/共享引用不重建、原关联登记不改原订单、字段/动作/query/租户/部门范围、技术与原生令牌冲突、丢失提交ACK恢复/关闭晚写、当前读范围与金额不泄露、原指针漂移、最终权限失败全回滚、错DbContext/query-only参加拒绝、真实HTTP/CSRF/no-store/会话撤销、并发同键仅一单及数据库原件不可变。2项原Order读隐藏价格和写最终权限失败回滚兼容成功直接复用link-native-fixed，之后只有新工具路径参数类型改变，未改变旧路径输入。

37项UI/router/auth检查及vue-tsc/Vite通过，无跳过。来源为D:/CP6-local-test/wave157/foundation/final-validation.json。首次路由405、夹具AsyncLocal以及两次原时间参数类型失败均保留；修复明确Npgsql Timestamp，无权限/精度/约束放宽。隐藏金额夹具误用了非法Access0，改为真实隐藏Access3后只复测该项，产品与其余14项行为输入未变并复用成功记录。一次执行者SPEC/QUALITY集中审查完整差异，核对同事务、最终授权、原件/恢复、精度、UI以及范围，无待修必需项。无浏览器E2E或真实企业验收、无Actions/部署/既有环境迁移。

## 剩余范围与收尾

这是现有真实工具的原订单创建/准确关联采用，不是全部旧PlateMoldService写者收敛。旧create/revise/edit/delete的双版本、合法修订/有效期、原Order变更/取消和PE投递仍未完成；完整WP04-32稳定Sales源与发布参加保持原前置。正式工具控制、实物/DOC、ROU/BOM/WO/SCHED及原D2/D5/D6/D7出口均保留。Continue original WP05-04 legacy writer expected native/technical versions, legal revision/effectivity and original Order change/cancel participation; formal Sales sources and ROU/BOM/WO/SCHED plus all original D2/D5/D6/D7 exits remain. No fabricated external qualifications.

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/133，source fd3add8728a0bf55300f71e9749e30290dd4a106，目标86aa254f80559abe30ad3c6f84fb1e7cf5eee392；准确分支已删除、DEV-2官方finish释放。Main正常PR/远端采用/准确分支删除/finish由最终proof确认；本记录真实保留收尾前快照。固定目录与缓存保留。

W156仅归档一次并关闭WP05-04.wave156_tool_sources，本轮仅新增WP05-05.wave157_order_link限定记录。375整卡24DONE/2IN_PROGRESS/349PLANNED、125完整目标0、247运行NOT_RUN、41历史静态不变。仅D1/D4整阶段完成；2/7=28.6%为阶段比例，不能当作工作量完成百分比。
