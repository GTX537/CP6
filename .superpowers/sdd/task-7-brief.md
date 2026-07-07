### Task 7: 回归 + 真库 QA（波4 DoD）

- [ ] Step 1: 回归门四项（后端 1559 级 / 前端 364+新 / type-check / build）
- [ ] Step 2: 真库（容器 curl/sqlcmd 模式照前波）：执行 space-roleaction-seed.sql → 验证 MenuAction/RoleAction 行数；**403 验证**：建一个无 Space 动作权限的测试角色+用户（或用既有非 admin），调 POST /api/space/site → 403 `{code:403,message:无权限...}`；admin 调 → 200。
- [ ] Step 3: 审计验证：admin 改一个 site 的 SiteName → 查 Sys_FieldAuditLogs 有 Modified 行（EntityName=Space_Site）。
- [ ] Step 4: BizException 验证：无库存前提下停用一个草稿库位（Status=0）→ 400 且 message 为**词条译文**（ja culture 下日文）而非「E-SPACE-004: 中文」原串——验证 middleware+种子链路。409/告警链路有波1-3 证据不重验。
- [ ] Step 5: SpaceHub 冒烟：容器网络内 SignalR 客户端不便——降级为「发布一次 → 检查 cp6-api 日志无推送异常」+ 前端单测已锁订阅逻辑；真浏览器验证并入波5 视觉走查票。清理义务同前波。
- [ ] Step 6: 证据入报告；缺陷则 fix commit。

---

## 自检记录

- **覆盖对照 2026-07-05 横切基准探查**：审计=Task 1（11 实体，拦截器免改）；权限=Task 4/5（RequirePermission+MenuAction/RoleAction 种子+v-permission；MenuKey 波2/3 已备）；错误码=Task 2/3（C# 种子+BizException+catch 清理；E-7xx/8xx 段是 W-SPACE-701/702/801 库存叠加/路径**警告**——属 P2.5 未实现功能的预留码，本波不种[无消费方]，记波5）；SignalR=Task 6（SpaceHub+接口注入）。菜单种子已在波2/3 完成（含 MenuKey），本波无菜单改动。
- **决策留档**：BizException 迁 Core 保留 namespace（零涟漪）；编辑器域变更端点统一 space-floor:edit；WmsBin 不挂审计；SpaceHub 无分组全播（低频 YAGNI）；W-SPACE-404 是 throw 路径要进种子，W-701/702/801 不种。
- **执行顺序**：1→7 串行（3 依赖 2 的迁移；5 依赖 4 的映射表；6 独立但排 5 后；7 收尾）。Task 3 是重灾区（56+36+47 处机械替换）——executor 按文件逐个提交前编译，防大爆炸。
