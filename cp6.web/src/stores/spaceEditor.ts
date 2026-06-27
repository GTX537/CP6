import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { EditorScene, RackVO } from '@/types/space/scene'

export const useSpaceEditorStore = defineStore('spaceEditor', () => {
  const scene = ref<EditorScene | null>(null)
  const dirty = ref({ upsert: new Set<string>(), del: new Set<string>() })
  const selection = ref<{ kind: string; ids: Set<string> }>({ kind: 'rack', ids: new Set() })

  function load(s: EditorScene): void {
    scene.value = s
    dirty.value = { upsert: new Set(), del: new Set() }
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

  return { scene, dirty, selection, load, markDirty, markDirtyDelete, rackById }
})
