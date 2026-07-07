# Hotfix Report — 编辑器差量删除死代码（波2终审 P1）

Branch: `fix/space-editor-deletes-deadcode` · Base: `main` @ fec8a12

## Bug
`spaceEditor.ts` 的 `save()` 用 `s.racks.filter(r => delIds.has(r.id))` 等从 **scene 集合** 反查被删实体来组装 `deletes`。但 `DeleteCmd.do` / `AddZoneCmd.undo` 早已把实体从 scene 移除，filter 恒空 → 后端永远收不到 deletes → 删货架/标注/库区保存"成功"后重开楼层实体复活（DB 从未删）。
连带：`markDirty` 不从 del 集移除 id，undo→redo 同一 id 可能既在 upsert 又在 del。

## 修法（一句话）
del 集从 `Set<string>` 改为 `Map<string, DeleteKind>`：`markDirtyDelete(id, kind)` 直接记录种类，`save()` 按 kind 分桶把 id 下发到 deletes 五桶，不再靠 scene 过滤；`markDirty` 同步 `del.delete(id)` 收敛 redo。

## 修法选择
采用任务方案 (a)+Map 内部存储（最干净）：
- `markDirtyDelete(id, kind?: DeleteKind)` — 加**可选**第二参，签名向后兼容（旧 mock `(id)=>...` 仍可用）。
- 内部 `dirty.del` 由 `Set` → `Map<string, DeleteKind|undefined>`。`dirty` 虽是 store 公开返回值，但全仓无任何外部读 `.del`（grep 确认 FloorEditor.vue 等零引用），改型安全。
- `save()` 遍历 Map 按 kind switch 分桶；无 kind 的条目走安全网：`console.warn` + 回退旧 scene 过滤（实战恒不命中，仅防遗漏）。
- 后端契约不变：`Deletes` 五列表 `Guid[]`，`FirstOrDefaultAsync` null-skip 幂等，直发 id 安全（含 undo 新建产生的未提交 id）。前端 `SceneSaveDto.deletes` 补上 `locations?`（对齐后端 `Deletes.Locations`，波1.5 已加）。

## 调用点清单（grep `markDirtyDelete(` 全量）
| 文件 | 调用 | 补的 kind |
|---|---|---|
| `DeleteCmd.ts:20` | `markDirtyDelete(rack.id)` | `'rack'` |
| `DeleteCmd.ts:25` | `markDirtyDelete(marker.id)` | `'marker'` |
| `AddZoneCmd.ts:17` (undo) | `markDirtyDelete(this.zone.id)` | `'zone'` |
| `AddMarkerCmd.ts:17` (undo) | `markDirtyDelete(this.marker.id)` | `'marker'` |
`commands.spec.ts` 的 mock ctx `(id)=>deleteIds.push(id)` 忽略新增 kind，无需改动。当前无 aisle/location 删除命令路径（DeleteCmd 只删 racks/markers），kind 已覆盖全五类以备后续。

## RED → GREEN 证据
新增 `src/stores/spaceEditor.spec.ts`（4 用例，走真实 DeleteCmd/AddZoneCmd + mock sceneApi.save，断言实际下发 DTO）：
1. **原 bug RED**：删已存在货架 → `scene.racks` 已空（旧 filter 必抓不到）→ 断言 `deletes.racks` 含 `r1`。修前恒空，修后 GREEN。
2. undo 新建库区 → `deletes.zones` 含 `z1`，且 upsert 不含（后端 null-skip 无害，与波2 Task 6 结论一致）。
3. redo/再 markDirty 同 id → `dirty.del` 不含、`upsert` 含 → deletes 不含、racks 含（收敛）。
4. save 后 `dirty.del`(Map)/`upsert` 清空。

## 验证
- type-check (NODE_OPTIONS=8192)：**0 error**
- `npm run test`：**337 passed** (52 files) = 基线 333 + 新增 4
- `npm run build`：**✓ built**
