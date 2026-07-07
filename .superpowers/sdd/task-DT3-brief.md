# Task D-T3: NodePropertyPanel 服务任务段 + EdgePropertyPanel 错误边 + 拉服务目录

（摘自 docs/superpowers/plans/2026-06-29-wfs-service-task.md；spec 章节 §5.3/§5.4/§5.5）

**Files:**
- Modify: `cp6.web/src/views/oa/designer/NodePropertyPanel.vue`
- Modify: `cp6.web/src/views/oa/designer/EdgePropertyPanel.vue`
- Modify: `cp6.web/src/api/oa/designer.ts`(Glob 确认)— 加 `getServiceCatalog()` API + 类型

- [ ] **Step 1: 实现**(spec §5.3/§5.4/§5.5)
  - `designer.ts`:`getServiceCatalog(): Promise<{actions:{name,label}[], connectors:{name,label}[]}>`(`http.get('/oa/designer/service-catalog')`,沿用既有 API 模式)。
  - `NodePropertyPanel.vue`:节点 `type==='serviceTask'` 时显示服务任务段(el-collapse),按 `serviceKind` 切换:dataWriteback(动作下拉=catalog.actions / mode / 参数模板 textarea / 重试)、webApi(连接器下拉=catalog.connectors / 路径 / 参数 / mode / 重试)、timer(延时模式 radio / 延时值 / 可选动作 / 重试)。onMounted 拉 catalog。
  - `EdgePropertyPanel.vue`:加「失败边(IsError)」`el-checkbox` 绑 `edge.isError`(patch 回 designerModel)。
  - 文案全 `t('oa.designer.svc.*')`(键在 P-E/E-T2 seed)。
- [ ] **Step 2: 验证** — `npm run type-check` + `npm run build`。
- [ ] **Step 3: commit** — `git commit -m "feat(wfs-service-task): D-T3 属性面板服务任务段+错误边复选+服务目录拉取"`

## 视觉与结构纪律（2026-07-05 补充）
- NodePropertyPanel/EdgePropertyPanel 刚经历 OA 批次4 token 化：所有颜色/圆角/间距用 `--cp-*` token，零硬编码。分段结构照既有面板惯例（既有 `isApproval` 计算属性旁插入 serviceTask 分支）。
- 下拉/radio/textarea 等表单控件照面板内既有 el-* 用法，不引入新组件形态。
- 主控代理会用 frontend-design skill 对面板布局把关。

## 落码纪律
- 工作目录 `C:\CP6`，分支 `feat/wfs-service-task-finish`。本地 commit 不 push。
- 零 Space 污染。文案全 `t()` 运行时键（键在 E-T2 才 seed，控制台裸键是预期）。
- type-check/build 用堆 8192：`$env:NODE_OPTIONS='--max-old-space-size=8192'`。
