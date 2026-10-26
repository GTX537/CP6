# W157 版型原订单关联共同事务与原键恢复

COORD单人，原七天ACTIVE。W156两仓采用/分支清理/席位释放核实，本轮仅归档W156并关闭WP05-04.wave156_tool_sources。

## 原范围与直接前置

已读WP05-04/05、WP04-32卡、ENG-TOOL相容原§3–6和ERP-ORD当前身份/Owner边界。旧PlateMoldService仍直接改原Order/Detail并在缺关联时重建；OrderWriteOwnerV1当前只有自有事务。直接采用需增加原Owner无内部commit的参与能力，不能包一层新事务或在工具服务继续直改交易表。

W157采用W156真实原工具UUID/完整BaseCd/WdPtnNo/WdRev/观察Ref/当前原生令牌，以及已交付Main原会话/final权限/持久原操作/审计基础。范围为既有真实版型的CREATE_OR_LINK，落实原OrderService/OrderWriteOwner在同Main事务创建原购买品草稿、准确关联与原结果。不是完整WP04-32稳定Sales Line/Schedule/ReleaseLineage/H10/H32，也不以限定采用关闭完整WP05-04/05。

新关联必须当前工具query/edit、原订单add/字段权限及实际对象/部门范围；读取/原结果恢复要求当前工具和原订单query。准确工具来源与expectedNativeRowVersion必须显式传入；同键同体恢复、异体拒绝、UNKNOWN查询/close禁止晚写。原订单与行真实UUID/并发令牌/原件摘要和完整工具身份在不可变关联记录保存；原ArrangeNo回填、原订单效果、审计/原结果同事务。旧完整编码与金额精度不缩小，15位商业映射保持其历史意义，不当新工具身份。

已有原ArrangeNo只按当前明确指针和真实订单图核对；丢失、多义或其他工具共享冲突不自动另造订单。已有真实关联可登记准确引用；不能把操作回执丢失当作新创建。技术批准、实物资格、正式Sales状态和PE未执行分别保留。新路由/来源页实际操作及恢复；无外部PE调用、无WMS/MES自动发单或发布资格声称。

## 任务与执行预算

预计70–120分钟：原Order Owner共同事务参与20–35；准确关联/原键/API/页面30–50；定向PG/HTTP/UI、一次SPEC/QUALITY与两仓收尾20–35。按Main START至finish一次计时，Handoff不叠加。

- [x] 原订单Owner共同事务参加与原来源/当前授权。
- [x] CREATE_OR_LINK准确原件、版本和原键恢复及实际页面。
- [x] 定向验证、一次集中审查、台账和四状态同步。
- [ ] 两仓正常PR、远端采用、准确分支删除及官方finish。

继续WP05-04全部旧writer的双版本、合法修订/有效期、原回执和原Owner商业变更/取消；当前窗口不能当这些写者已收敛。完整工具控制/实物/DOC、Sales稳定来源、ROU/BOM与WO/SCHED及原D2/D5/D6/D7出口保持。无Actions/既有环境迁移/部署。

## 实际交付

[单份报告](../../evidence/W157-01/DELIVERY.md)，登记26.16分钟；实现、定向验证及集中审查完成。Handoff已采用释放，Main正常PR/finish待最终proof。
