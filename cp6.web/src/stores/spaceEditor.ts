import { defineStore } from 'pinia'
import { ref } from 'vue'
import type { EditorScene, RackVO, SceneSaveDto } from '@/types/space/scene'
import { sceneApi } from '@/api/space/scene'

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

    // throws on error (including 409) — caller handles
    await sceneApi.save(floorId, dto)
    dirty.value = { upsert: new Set(), del: new Set() }
  }

  return { scene, dirty, selection, load, markDirty, markDirtyDelete, rackById, save }
})
