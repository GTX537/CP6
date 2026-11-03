# BUG #715 权限能力目录与互锁

[Issue #715](https://github.com/GTX537/CP6/issues/715)，基线`ee2c777335bfc7b444cc6f8c6601ea5c2d998e1a`，COORD单人DEV1 `codex/bug-715`。本次接W281正常远端采用/清理/席位释放后开工；24小时有效执行目标持续。

## 根因与修复

补齐单价订正原操作query能力目录；纠正权限互锁漏读WMS/MES仅目录OwnerActions及MES313锚定，不增加角色授权。

ERP的单价订正operations/v1原结果、scope和close端点要求erp-order-price-correction:query，但ErpPermissionSeed.ReadActions未登记209:query，授权界面缺少对应能力。只追加该目录项；历史写Actions和已有角色权限保持。目录本身不授予查询、关闭或价格修改能力。

WMS的expiry:view、stock:query、stocktake:query及MES的machine:query、work-order:query、plan-achievement:query/export早已在OwnerActions，仅目录登记且要求IAM显式授权。旧互锁只读Actions/ReadActions，遗漏OwnerActions，MES锚定表又漏313。互锁现读取实际Owner目录并补已存在的313菜单映射，错误消息明确能力目录缺失，不再直接推断“管理员403”。无豁免、无权限校验移除、无默认管理员授权扩大。

## 验证与一次集中审查

本地20项相关行为通过、0跳过：ERP/MES实际种子跨租户登记/幂等/授权保持，六领域贴点与能力目录互锁，以及实际Program登录→HTTP→PG的未授权拒绝、显式query授权读取/关闭原意图、CSRF拒绝和撤权再拒绝。新增种子期望先红后绿；原W281的3项互锁失败在修复后通过，原日志保留。

红测D:/CP6-local-test/bug715/foundation/red-20261103T040855Z.json；绿测D:/CP6-local-test/bug715/foundation/green-20261103T041127Z.json。原失败D:/CP6-local-test/wave281/foundation/review-fixes-20261103T034353Z.json不覆盖。实际PG执行真实ErpMenuSeed/ErpPermissionSeed两次，209:query单行且无角色授权；真实Cookie/CSRF请求先403、显式授权后scope/UNKNOWN读取成功、缺CSRF关闭403、正确关闭CLOSED、原结果可读、撤权再403。最终仅一个关闭journal、0审批意图，价格保持3。明确模拟用户/租户/业务/IAM输入；实际Owner/Program/PG执行。

一次作者SPEC/QUALITY审查完整diff：只新增一项能力目录，既有30项写授权保持；互锁反射包含实际目录且不隐藏缺键；测试验证真实种子副作用、当前权限与原命令关闭。检查相关文档链接和台账生成。无前端改动，未运行无关UI/全仓回归；无Actions、工作流变更、生产部署或既有环境迁移。测试PG/API进程已停止。

## 后续与生命周期

W281 Main PR716、Handoff PR257及准确任务分支删除、工作区干净和两席位finish已核销，累计有效46384秒；本BUG用新独立分支。原375仍27DONE/2IN_PROGRESS/346PLANNED，59局部余56、125/247正式状态不变。后续W282退回重送/转交加签与无人审批恢复；24h总目标不因本修复结束，排除已记录7558秒调度间隔，下一累计50400秒校准。

本提交为合并前快照；Issue关闭必须核对远端main采用。两仓远端采用、准确分支清理、工作区状态和官方finish/席位释放最终见C:/Users/tt/CP6-planning-reports/20261017-foundation/bug715-final-verification.json。
