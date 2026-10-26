# W150-01 工程身份与双 Owner 经营映射

Main 基线 47e375e6899a580c7ac49d58aa4d25b2e4cc4607，COORD 本对话单人复用 DEV-1 / DEV-2，无新工作树。预计90–150分钟；Main START至当前登记36.99分钟，Handoff不叠加，最终耗时以官方finish及wave150-final-verification.json为准。本轮只WP04-33/WP05-02有限前置，不关闭完整任务或原七天目标。

## 范围与实现

WP05-02/ENG-PROD-01及11_SUP主册要求工程技术身份与ERP经营料号独立；同名/同码不自动合并，Site/用途和双方确认明确。新增 Main.Plm.EngineeringProduct.v1 原生 UUID/tenant/creator/原代码身份；代码和名称允许重复，技术定义和识别依据显式填写。追加不可变UTF8原文/digest/版本，CAS修订/停用，原代码不可重键；身份版本不冒充技术Revision。独立query/add/edit/confirm，scope1创建者UUID、2/3/4当前真实部门、5租户；部门创建票据/映射、身份、审计与原操作同一事务。

实际 Space_Site 仍由原 SpaceMasterService 拥有；新增不可变变动见证和当前同事务读适配，既有对象迁移从实际观察时刻开始，不发明过去版本。准确来源核当前tenant/id、源payload、摘要和捕获触发器，真实表锁持有至事务结束；缺捕获、缺源、跨租户或停用不能确认。它是物理园区/仓库Site，不是CRM公开表单Site，不把tenant/部门当法人。

Main.Plm.EngineeringMapping.v1 为独立UUID与工程原UUID稳定头，草稿允许缺ERP/Site或未解决差异；明确准确来源后独立CONFIRM。确认需工程confirm与原ERP confirm-engineering两侧当前权限及对象范围，工程/原ERP/Site版本摘要均精确当前、启用/存在，用途/复核依据明确并确认冲突已解决。同一实际操作者可持双方权限，不虚构两个自然人或第三方签名。保存双方真实Owner/Actor/权限版本确认事实，映射版本/原操作/审计一起提交；ERP原主键、价格和库存不复制、不修改。

确认范围严格为IDENTITY_SITE_ONLY，法人、技术Revision/Configuration、制造用途分别REQUIRED_FENCED。同一工程可对应两个Site/ERP原对象，保原多对多关系。CurrentExact只供已持同DbContext真实事务/存储锁的参与者；源变化、停用/再启用或映射修订均拒绝旧引用的新采用。历史保持原文，当前授权仍可读；修订草稿清双方确认事实，停用保历史。各来源当前读取权限和最终对象范围复核，撤权后恢复/历史/原键重放不能泄露数据。

PostgreSQL迁移保原部门映射类型，菜单极值安全分配，仅定义工程/ERP确认/Site读取能力，不授角色；不可变原文、单调头、摘要、原身份和延迟外键约束；数据库拒绝缺状态原文，回滚不发布新头，Down拒绝逆向清除历史。真实原操作同体返回原结果、异体冲突、UNKNOWN查询/close挡迟到，不自动重发。

独立页面提供身份目录、新建/修订/停用、历史、原操作恢复，以及映射草稿/原ERP与Site版本选择/冲突复核/双方确认/停用/历史。权限不具备双方确认则不显示确认按钮；身份变化清私有输入和迟到响应。浏览器只存最小公开恢复引用，Web Locks不排队重发；查到COMMITTED即清对应旧草稿。IAM明确选择工程原UUID，不能从相同代码推导身份。

## 实际验证与集中审查

- red-20261026T174726Z：实际Program/Kestrel冷入口期望200得到404，证明缺入口；first-20261026T175943Z为生成序列化类型名编译失败，无测试执行，修正为真实JsonNamingPolicy。原日志保留。
- scoped-20261026T180222Z：首轮真实PG/HTTP11通过。补齐当前ERP撤权/原源ABA、实际Site跨租户与捕获缺失、数据库缺状态拒绝及旧Site前向迁移后，final-20261026T181258Z最终16/16、0跳过，替代前11而非累计27。其中复用原CurrencyPolicy前向迁移行为作为共享部门映射/菜单兼容检查。
- 实际覆盖同名异规格不合并、CAS竞争、不可变原文、tenant/creator/部门2–4、真实HTTP创建/确认/重放/撤权、双方权限与两个Site原键、未解决草稿不确认、异体冲突/close、工程和Site/ERP源变化拒绝新采用、历史保持、确认提交失败共同回滚、真实事务/存储锁边界及无隐式授权。
- ui-first.json33/33、0跳过：工程页面6、原操作恢复5、IAM22。使用生产Vue/恢复实现，API/Web Locks为受控边界，无本轮真实浏览器或全链路E2E宣称。build-execution.json的vue-tsc --build、Vite build均exit0，前端运行时此后未变；既有体积警告保留，dev/unknown测试资产非发布制品。

原始证据D:/CP6-local-test/wave150/foundation，final-validation.json绑定实际运行时/测试blob与最终源。COORD一次完整SPEC/QUALITY审查覆盖准确来源/双方确认、原身份/多对多/版本、权限/租户/实际锁和共同事务、回滚/恢复、迁移兼容及页面身份隔离；核完整diff、状态范围和链接。审查发现SQL缺state的NULL路径已收紧，定向行为通过；未解决实质阻塞为零。无Actions、Docker、既有环境迁移或部署；隔离本地夹具不冒充业务验收。

## 收尾快照与剩余范围

Handoff正常PR https://github.com/GTX537/CP6.AI-Handoff/pull/126，source 9963b58921a3158525a8e181d77cd08cc88168e0，远端目标 d2c1c8798a3ca85cb9345de6f56bb4f2811391cf；准确分支已删除、DEV-2已官方finish释放。Main本提交仍待正常PR/远端准确采用/分支清理/finish，保留真实收尾前快照；最终proof由下轮准入归档一次，不在main追加自证提交。

仅新增WP04-33/WP05-02.wave150_engineering_mapping有限记录；W149按最终proof归档一次。375卡仍24 DONE/2 IN_PROGRESS/349 PLANNED，125完整目标0完成，247运行验收NOT_RUN，41历史静态记录不冒充运行验收。原七天ACTIVE，仅D1/D4整阶段闭合，2/7约28.6%只是完整阶段比例，不能换算工作量百分比。后继工程Revision/Configuration/冻结、完整映射适用性、正式WO五域与SCHED同UoW/UI及D2/D5/D6/D7全部原出口保留。
