import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { EditorScene, RackVO, SceneSaveDto } from '@/types/space/scene'
import { sceneApi } from '@/api/space/scene'
import { CommandStack } from '@/space-editor/command/CommandStack'
import type { EditorContext } from '@/space-editor/command/Command'

export const useSpaceEditorStore = defineStore('spaceEditor', () => {
  const scene = ref<EditorScene | null>(null)
  const dirty = ref({ upsert: new Set<string>(), del: new Set<string>() })

  // Reactive selection (array so Vue tracks mutations)
  const selectionIds = ref<string[]>([])

  // Undo/redo state (updated manually after stack operations)
  const canUndo = ref(false)
  const canRedo = ref(false)

  // CommandStack — plain instance exposed for tools
  const stack = new CommandStack()

  function load(s: EditorScene): void {
    scene.value = s
    dirty.value = { upsert: new Set(), del: new Set() }
    selectionIds.value = []
    canUndo.value = false
    canRedo.value = false
    stack.undoStack.length = 0
    stack.redoStack.length = 0
  }

  function markDirty(id: string): void {
    dirty.value.upsert.add(id)
  }

  function markDirtyDelete(id: string): void {
    dirty.value.del.add(id)
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
    const delIds = dirty.value.del

    const dto: SceneSaveDto = {
      racks: s.racks.filter(r => ids.has(r.id)),
      zones: s.zones.filter(z => ids.has(z.id)),
      aisles: s.aisles.filter(a => ids.has(a.id)),
      markers: s.markers.filter(m => ids.has(m.id)),
      locations: s.locations.filter(l => ids.has(l.id)),
      deletes: {
        racks: s.racks.filter(r => delIds.has(r.id)).map(r => r.id),
        aisles: s.aisles.filter(a => delIds.has(a.id)).map(a => a.id),
        zones: s.zones.filter(z => delIds.has(z.id)).map(z => z.id),
        markers: s.markers.filter(m => delIds.has(m.id)).map(m => m.id),
      },
    }

    await sceneApi.save(floorId, dto)
    dirty.value = { upsert: new Set(), del: new Set() }
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
