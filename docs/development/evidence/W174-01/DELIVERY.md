# W174-01 — 准确发布版本的独立启停

COORD单人继续CP6-7D-20261022；[准入计划](../../dispatch/20261027-wave174/README.md)。Main基线e488c98cf4230e76f7231b4e57b45d78537df394，DEV1 codex/w174-01；Handoff基线11ea3bb7234a0f69bab2bb03652a58f9d494aa23，DEV2 codex/w174-handoff。W173归档一次，只关闭WP12-01.wave173_flow_publication历史窗口；WP12-02新增W173发布证据引用，归属修正不重复实现或改变完整卡状态。

## 实际实现

新FLOW批次现在有独立启停控制头、UUID activationRevision和不可变历史。初始disabled且未选择发布版；创建草稿同时初始化控制头，已有W173定义仅初始化disabled，不补造历史动作。启用只接受明确的本定义Published版本，可选旧版本，不读取latest或当前草稿替代。按原发布schema摘要及当前依赖重新检查；下一草稿、保存及发布新版不改变当前启停选择。停用保留当时所选版本，只关闭本配置，不删除定义或修改在途实例。历史原命令重放返回当时结果，不把原先enable成功重新作用于后来已disable的配置。

新增forward-only PostgreSQL迁移20261028090000_WorkflowActivationV2。activation_history外键绑定tenant/environment/head/version，UPDATE/DELETE/TRUNCATE拒绝；activation_current的修改必须匹配新的历史revision、previous_revision、enabled、准确版本和时间。删除/截断拒绝。历史及控制头、WFC原命令和实际授权audit同事务。原heads.enabled仅为旧disabled保留列，不被写作第二权威；新读取显式使用activation_current。

当前实际oa-flow-admin菜单和enable动作控制启停，独立于oa-designer编辑/发布，不授予任何角色或schema写权。仍核对当前实际会话、tenant/environment、资源Owner/部门/数据范围、字段来源和最后授权。启用要求准确发布内容的完整可读；停用不因外部引用当前不可用而强迫重证其执行资格。当前可读历史不依赖仍有写权限或Scope仍enabled。

锁序认证根→同一WF_AUTH Scope→原命令→启停控制头→准确发布源→历史/回执/audit。控制头更新writer在修改前取得同门并推进epoch，Owner验证修改前证明、更新后重新取得本次writer的证明，最后验证Scope/Actor/字段/控制头。缺实际保护、撤权、ABA、过时token或错误源均回滚。完整body使用WFC-CJ-1，enabled是显式必填boolean；重复同body返回原结果，异body409，提交未知503只能查询或重试原命令，NOT_OBSERVED不是未提交证明。audit保留实际Actor、当前角色/部门/字段源、前后Scope、准确发布摘要/依赖、前后启停及原请求摘要。

## 入口与恢复

新增管理列表、当前启停预览/版本选择、分页历史和POST activation；generic commands支持FLOW_SET_ACTIVATION。请求body为enabled、expectedActivationRevision、publishedVersionId、reason，外层expectedVersion必须与独立activation token相等，reason1..500。当前菜单、资源、HTTP登录和CSRF仍由真实宿主处理，无隐式latest或自动停其他流程。

现有流程管理页增加“准确版本启停”页签，要求明确选择版本和原因。停用始终使用当前选中的pin，不误用下拉框待启用版本。未知结果持久保留原命令/原token/原body，刷新可恢复；不匹配结果不能清pending。旧成功只显示原操作已确认，需重新读取当前配置，避免把历史结果显示成当前开关。401/403/身份切换清除私有内容和缓存，迟到响应丢弃。草稿/发布页面及API类型同步：enabled为boolean，保存/发布不再误报一定停用。

## 明确未完成

本轮交付独立启停配置，运行消费者仍NOT_ADOPTED。新批没有native可执行投影或APP可信发起入口；旧引擎latest/backfill路径不被当作准确来源采用。预览不伪造在途数量0或运行入口列表，页面明确尚不能据此发起审批。已有实例未被改动，但本轮没有“新v2实例继续运行”的验收，因为该消费者尚未交付。

FORM发布来源、子流程准确激活依赖和完整唯一表单绑定仍未交付，W173真实缺口门保留，不以旧native头推导来源或缩小最终合同。当前服务引用重核宿主登记身份，不代表完整外部执行/配置资格。完整WP12-02/APP及原D2/D3/D5/D6/D7全部出口继续开放；下一步接实际审批消费者，按接受的APP绑定、可信Owner事实与同事务要求实施，不无条件扩张到完整FORM/clone工具。

## 验证与集中审查

最终准确限定35项后端检查全部通过（34隔离PG18/actualHTTP、1canonical unit），0skip；22项Vue/身份恢复通过，前端类型检查及构建通过。后端覆盖独立token/旧版本选择、新草稿与发布不影响启停、历史不可变、原结果不重新启用、管理员无schema编辑权、提交未知恢复、最终撤权/Scope ABA回滚、缺保护、必填boolean、tenant/定义源隔离、真实跨连接竞争旧CAS及实际HTTP。

初始筛选过宽实际38项，37pass/1fail；失败是无关SpaceDesignV1OpenApi静态检查从共享构建输出路径找不到其设计文件，原失败日志保留。本任务初始33项均通过。集中SPEC/QUALITY补齐必填enabled、草稿创建前初始化保护、enabled boolean前端类型和不误报停用的文案，新增相关边界后准确限定35项最终通过。页面初始15项未改行为证据复用，另7项受影响设计器检查通过，按唯一用例计22而非重复累加。证据D:/CP6-local-test/wave174/foundation/final-validation.json记录真实来源和最终文件blob；编译/测试句柄结束后才改后端。

未运行浏览器到PG完整E2E、247真实业务验收、既有环境迁移、部署或Actions；没有取消其他任务或修改工作流/保护。迁移仅隔离数据库验证。

## 台账与生命周期

七天目标ACTIVE，D1/D4完成，其余阶段部分采用；2/7=28.6%只是阶段出口比例。375卡保持24DONE/2IN_PROGRESS/349PLANNED，125完整目标0交付、247真实运行NOT_RUN。仅WP12-02.wave174_flow_activation限定交付。

预计60–100分钟；本报告时Main官方START经过19.0分钟，实际最终按finish记录。Handoff [PR](https://github.com/GTX537/CP6.AI-Handoff/pull/150)已合并，source f8b5d795df76e717fb847506a7f529ff5b12163a、target db08425694da1dfdb35789d2fb570819cdde86b4；准确分支删除、DEV2释放。Main本提交是正常PR/finish前快照，未提前声称完成；最终远端采用、准确分支删除和席位释放见C:/Users/tt/CP6-planning-reports/20261017-foundation/wave174-final-verification.json。固定目录、缓存、主目录用户原始资料保留。下次准入只归档W174一次，关闭WP12-02.wave174_flow_activation限定窗口。
