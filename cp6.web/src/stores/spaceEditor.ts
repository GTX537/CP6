import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { EditorScene, RackVO, SceneSaveDto, DeleteKind } from '@/types/space/scene'
import { sceneApi } from '@/api/space/scene'
import { CommandStack } from '@/space-editor/command/CommandStack'
import type { EditorContext } from '@/space-editor/command/Command'

export const useSpaceEditorStore = defineStore('spaceEditor', () => {
  const scene = ref<EditorScene | null>(null)
  // upsert：待新增/更新 id 集；del：待删除 id → 种类（种类决定进 deletes 哪个桶）。
  // del 用 Map 直接记录 id+kind——被删实体已从 scene 移除，save 时无法再靠 scene 过滤反查（原 P1 死代码根因）。
  const dirty = ref({ upsert: new Set<string>(), del: new Map<string, DeleteKind | undefined>() })

  // Reactive selection (array so Vue tracks mutations)
  const selectionIds = ref<string[]>([])

  // Undo/redo state (updated manually after stack operations)
  const canUndo = ref(false)
  const canRedo = ref(false)

  // CommandStack — plain instance exposed for tools
  const stack = new CommandStack()

  function load(s: EditorScene): void {
    scene.value = s
    dirty.value = { upsert: new Set(), del: new Map() }
    selectionIds.value = []
    canUndo.value = false
    canRedo.value = false
    stack.undoStack.length = 0
    stack.redoStack.length = 0
  }

  function markDirty(id: string): void {
    dirty.value.upsert.add(id)
    // redo 收敛：id 重新变为 upsert（如 redo 新建）时，必须移出 del，避免同一 id 既发上行又发删除
    dirty.value.del.delete(id)
  }

  function markDirtyDelete(id: string, kind?: DeleteKind): void {
    dirty.value.del.set(id, kind)
    dirty.value.upsert.delete(id)
  }

  function rackById(id: string): RackVO | undefined {
    return scene.value?.racks.find(r => r.id === id)
  }

  /** Build EditorContext for passing to CommandStack */
  function buildEditorContext(): EditorContext {
    return {
      scene: scene.value!,
      markDirty,
      markDirtyDelete,
    }
  }

  /** Sync reactive canUndo/canRedo after any stack operation */
  function updateUndoRedo(): void {
    canUndo.value = stack.canUndo
    canRedo.value = stack.canRedo
  }

  // ── Selection helpers ──────────────────────────────────────────────────────

  function setSelection(ids: string[]): void {
    selectionIds.value = [...ids]
  }

  function clearSelection(): void {
    selectionIds.value = []
  }

  function toggleSelection(id: string, add: boolean): void {
    if (add) {
      if (!selectionIds.value.includes(id)) selectionIds.value = [...selectionIds.value, id]
    } else {
      selectionIds.value = selectionIds.value.filter(x => x !== id)
    }
  }

  function isSelected(id: string): boolean {
    return selectionIds.value.includes(id)
  }

  // ── Persistence ───────────────────────────────────────────────────────────

  async function save(floorId: string): Promise<void> {
    const s = scene.value
    if (!s) return

    const ids = dirty.value.upsert
    const del = dirty.value.del

    // deletes 直接由 del(Map) 按记录的 kind 分桶下发 id——不再过滤 scene（实体早已被 DeleteCmd/undo 移除，
    // 旧 s.racks.filter(delIds.has) 恒空 → 后端永远收不到删除，是原 P1 死代码）。
    const deletes = {
      racks: [] as string[],
      aisles: [] as string[],
      zones: [] as string[],
      markers: [] as string[],
      locations: [] as string[],
    }
    const unclassified: string[] = []
    for (const [id, kind] of del) {
      switch (kind) {
        case 'rack': deletes.racks.push(id); break
        case 'aisle': deletes.aisles.push(id); break
        case 'zone': deletes.zones.push(id); break
        case 'marker': deletes.markers.push(id); break
        case 'location': deletes.locations.push(id); break
        default: unclassified.push(id); break
      }
    }
    if (unclassified.length > 0) {
      // 安全网：markDirtyDelete 未带 kind（理论上不应发生——所有调用点已补 kind）。
      // 回退旧 filter 行为（已从 scene 移除的实体在此恒不命中，仅防遗漏）并告警。
      console.warn('[spaceEditor] markDirtyDelete 缺少 kind，回退 scene 过滤分桶：', unclassified)
      const rest = new Set(unclassified)
      for (const r of s.racks) if (rest.has(r.id)) deletes.racks.push(r.id)
      for (const a of s.aisles) if (rest.has(a.id)) deletes.aisles.push(a.id)
      for (const z of s.zones) if (rest.has(z.id)) deletes.zones.push(z.id)
      for (const m of s.markers) if (rest.has(m.id)) deletes.markers.push(m.id)
      for (const l of s.locations) if (rest.has(l.id)) deletes.locations.push(l.id)
    }

    const dto: SceneSaveDto = {
      racks: s.racks.filter(r => ids.has(r.id)),
      zones: s.zones.filter(z => ids.has(z.id)),
      aisles: s.aisles.filter(a => ids.has(a.id)),
      markers: s.markers.filter(m => ids.has(m.id)),
      locations: s.locations.filter(l => ids.has(l.id)),
      deletes,
    }

    await sceneApi.save(floorId, dto)
    dirty.value = { upsert: new Set(), del: new Map() }
  }

  return {
    scene,
    dirty,
    selectionIds,
    canUndo,
    canRedo,
    stack,
    load,
    markDirty,
    markDirtyDelete,
    rackById,
    buildEditorContext,
    updateUndoRedo,
    setSelection,
    clearSelection,
    toggleSelection,
    isSelected,
    save,
  }
})
