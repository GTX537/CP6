# W29-01 角色生命周期与恢复：单份交付

2026-10-20，COORD本聊天单独执行；基线563850b2db83bff6587109e5f636be7df2642847，codex/w29-01。角色安全写入和对应页面本地可合并：Yes。[BUG351](https://github.com/GTX537/CP6/issues/351)和[BUG352](https://github.com/GTX537/CP6/issues/352)待正常PR集成、远端采用核对后关闭。完整整数域的跨仓传播缺口另见[BUG353](https://github.com/GTX537/CP6/issues/353)，保持Open；不把本窗标为完整IAM交付。

## 实现及明确边界

新增/api/iam/roles创建、编辑、引用及原操作查询/封闭。RoleId和可信TenantId定位，路径与命令RoleId必须一致；白名单仅角色名称、说明、Enable、OrderNo，CreateDate由服务端设置。角色编号1或名称admin不会授予平台管理员。PG旧无版本新增/更新明确400并提供新入口，旧硬删除409；非PG历史行为保留，本轮未新增SQL Server实现或验证。

实际认证共同事务门内重新核当前actor、租户、权限及角色/引用版本。共用ReferencesVersion加入已有AuthenticationPolicyRevision，涵盖SSO默认角色和审批变化/ABA；不增加迁移。普通SQL writer及角色/成员/授权变化沿用既有门和revision。角色启停的身份快照/Outbox、角色记录、强审计和原操作回执原子保存；纯名称/说明/排序不伪造权限语义事件。回执失败全部回滚；提交ack丢失准确查询原operation，关闭屏障拒绝迟到重放，输入变更不能复用原operation。

引用报告列出主/附角色用户、菜单、动作、范围、字段、SSO默认及已批准角色。停用保留历史关联；缺完整业务/审批/PRIV留存权威时物理删除明确拒绝。历史已删角色的身份revision或遗留引用阻止静默重建同号身份。没有重启冻结的物理删除方向。

角色页面改用版本化API，编辑编号不可变；412保留非敏感草稿，原操作封闭后重读版本再明确保存。UNKNOWN只查询或封闭，跨标签页Web Locks及公开operation定位不会存草稿或重放保存；失权/身份切换清页面，迟到响应丢弃。保存后刷新失败仍说明已保存，可单独刷新。用户页面发现同类弹窗遮挡恢复按钮，真实Chrome红测后修正为活动弹窗内可操作，保持初始密码清除。

**未完成边界#353**：现有Main/CRM固定v1事件Schema及validator仅接受正整数role身份，而Sys_Role为自定义int。没有缩小DTO整数域或以另一个身份替代、跳过身份捕获；不兼容的传播明确503 IAM_IDENTITY_CONTRACT_UNAVAILABLE且事务完整回滚。实测int.MinValue/0未成功创建，1/int.MaxValue成功。完整域需要合同Owner版本演进及Main/CRM消费者采用，不能只放宽单仓校验；本窗没有宣称此项完成。

## 实际验证

证据根D:/CP6-local-test/wave29/foundation，不复制仓库/依赖/构建目录。最终适用：

- iam-role-integration-20261020T055645Z：**61/61 .NET通过，0失败/跳过**。包含真实PG/实际Program角色新旧入口、Cookie/CSRF、租户白名单与同号隔离、平台身份不提升、当前权限撤销、SSO ABA、用户/组织共享引用CAS回归、回滚/丢ack/准确恢复及历史InMemory角色/字段审计回归。
- iam-lifecycle-components-final：Vitest4.1.11 **12/12通过**；iam-lifecycle-typecheck-final：vue-tsc --build通过。
- iam-lifecycle-browser-final：实际Chrome/Vue/ElementPlus受控HTTP **6/6通过**，角色CAS弹窗可封闭/重读/重确认、另一标签刷新后恢复原提交、身份切换抛弃迟到资料；用户一次提交/密码不落盘、跨Tab恢复、当前弹窗恢复。页面夹具不冒充真实DEV。
- 原失败保留：iam-role-red（2个实际HTTP红测）；iam-role-core（发现固定合同域限制及两项测试查询未限定tenant）；iam-role-browser（关闭动画期间重复提示）、iam-user-modal-red（真实弹窗缺按钮）、首轮typecheck（测试空值检查）。已修正项有局部复查；#353仍明确Open，受控拒绝测试不等于全域支持。
- W27认证跨Tab/SSO首次批准与映射及W28未受影响证据按原范围复用；本窗没有重新执行完整认证/CRM、SQL Server、全仓测试、既有DB迁移或真实业务验收。既有依赖告警未改动。

## 一次作者SPEC/QUALITY及资源收尾

集中审查完整分支：HTTP旧入口不能绕过版本、新白名单/当前授权/共同门、SSO及tenant引用、身份/审计/回执原子性、原操作owner与输入冲突、实际UI恢复及敏感数据边界。实质发现已按上述证据修复并局部复查；在明确列出的Main角色安全写入范围内无剩余合并阻断。#353为固定跨仓合同缺口，保留单独可复核Issue，不借此提升完整角色/IAM状态。

自有PG55435各测试根已停止，HTTP fixture结束；自有Vite58427 PID30616核对命令后停止，端口无监听。DEV-1登记W29-01、DEV-2仅W29-HANDOFF，无新增工作树/代理/完整副本。候选待正常PR远端采用后manage finish定向清理本地及远端任务分支，释放固定席位再领取W29-02。最终实际分支/席位处置由本轮计划及Handoff记录。

无Actions、部署、真实租户审批或现有环境迁移。完整IAM、CRM/Owner、125目标与247真实运行验收不提升。
