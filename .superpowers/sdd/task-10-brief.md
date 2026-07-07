### Task 10: CpFormDialog + CpDetailPanel

**Files:**
- Create: `cp6.web/src/components/templates/CpFormDialog.vue`、`CpDetailPanel.vue`
- Test: `cp6.web/src/components/templates/__tests__/CpFormDialog.spec.ts`

**Interfaces:**
- Produces:
  - `CpFormDialog` props `{ modelValue:boolean; title:string; fields?:FormField[]; form:Record<string,unknown>; rules?:FormRules; submit:(form)=>Promise<void>; width?:string }`，emits `update:modelValue`、`saved`；`FormField = { key,label,type:'text'|'number'|'select'|'date'|'textarea',options?,required? }`；默认 slot 替代 fields 自组复杂表单。行为：提交前 `elFormRef.validate()`；`submit` resolve → emit saved + 关闭；reject → ElMessage.error 且不关闭；提交期间确认钮 loading。
  - `CpDetailPanel` props `{ items: { label:string; value:unknown; kind?:'text'|'num'|'mono'|'tag' }[]; cols?:number }`（描述栅格，默认 2 列）
- [ ] **Step 1:** 失败测试：打开渲染 title/fields；必填空提交不触发 submit；submit resolve 后 emit saved + update:modelValue(false)；reject 不关闭。
- [ ] **Step 2:** 实现（el-dialog + el-form 组合，视觉由 overrides 保证，footer = ghost 取消 + primary 确认）。
- [ ] **Step 3:** 测试 PASS → Commit：`feat(ui): CpFormDialog 表单弹窗 + CpDetailPanel 详情面板`。

---

