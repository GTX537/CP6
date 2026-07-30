// spaceEditor store 差量删除单测（波2终审 P1 回归）——deletes 直发记录的 id 而非按 scene 过滤
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSpaceEditorStore } from './spaceEditor'
import { sceneApi } from '@/api/space/scene'
import { DeleteCmd } from '@/space-editor/command/commands/DeleteCmd'
import { AddZoneCmd } from '@/space-editor/command/commands/AddZoneCmd'
import type { EditorScene, RackVO, ZoneVO, SceneSaveDto } from '@/types/space/scene'

vi.mock('@/api/space/scene', () => ({
  sceneApi: { save: vi.fn() },
}))

const rack = (id: string): RackVO => ({
  id, zoneId: 'z', floorId: 'f', rackCode: id, x: 0, y: 0, z: 0, rotationZ: 0,
  cols: 1, levels: 1, depthCount: 1, cellW: 1000, cellH: 1000, cellD: 1000,
})
const zone = (id: string): ZoneVO => ({
  id, floorId: 'f', zoneCode: id, zoneName: id, zoneType: 1,
  polygon: '[[0,0],[1000,0],[1000,1000],[0,1000]]', enable: true,
})

function makeScene(): EditorScene {
  return {
    source: {
      kind: 'Real',
      dataSourceId: 'TEST_SPACE',
      observedAtUtc: '2026-07-25T00:00:00Z',
      isSimulated: false,
      isAvailable: true,
    },
    floor: {} as any,
    zones: [],
    aisles: [],
    racks: [],
    locations: [],
    markers: [],
  }
}

/** 抓取 save() 实际下发给后端的 DTO */
function lastDto(): SceneSaveDto {
  return vi.mocked(sceneApi.save).mock.calls.at(-1)![1]
}

describe('spaceEditor.save — 差量删除载荷', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    vi.mocked(sceneApi.save).mockResolvedValue({ code: 0, message: '', data: { idMap: {} } } as any)
  })

  it('删除已存在货架 → deletes.racks 含该 id（原 P1 死代码 RED：过去恒空）', async () => {
    const store = useSpaceEditorStore()
    const scene = makeScene()
    scene.racks.push(rack('r1'))
    store.load(scene)

    // 走真实命令：DeleteCmd.do 会把 rack 从 scene 移除并 markDirtyDelete
    new DeleteCmd({ racks: [rack('r1')] }).do(store.buildEditorContext())
    expect(store.scene!.racks).toHaveLength(0) // 实体已离场——旧 filter 在此必然抓不到

    await store.save('f1')
    expect(lastDto().deletes!.racks).toContain('r1')
  })

  it('undo 新建库区（AddZoneCmd.undo → markDirtyDelete）→ deletes.zones 含 id（后端 null-skip 无害）', async () => {
    const store = useSpaceEditorStore()
    store.load(makeScene())
    const ctx = store.buildEditorContext()
    const cmd = new AddZoneCmd(zone('z1'))
    cmd.do(ctx)   // 新建
    cmd.undo(ctx) // 撤销 → 记入 del(zone)

    await store.save('f1')
    expect(lastDto().deletes!.zones).toContain('z1')
    // 撤销后 upsert 不应再含该 id
    expect(lastDto().zones).not.toContainEqual(expect.objectContaining({ id: 'z1' }))
  })

  it('redo/再 markDirty 同一 id → del 集不再含它（收敛，避免同 id 既上行又删）', async () => {
    const store = useSpaceEditorStore()
    store.load(makeScene())
    store.markDirtyDelete('x1', 'rack') // 先记删除
    store.markDirty('x1')               // redo 新建 → 应从 del 移出
    expect(store.dirty.del.has('x1')).toBe(false)
    expect(store.dirty.upsert.has('x1')).toBe(true)

    store.scene!.racks.push(rack('x1'))
    await store.save('f1')
    expect(lastDto().deletes!.racks).not.toContain('x1')
    expect(lastDto().racks).toContainEqual(expect.objectContaining({ id: 'x1' }))
  })

  it('save 后 dirty 清空（del 为 Map）', async () => {
    const store = useSpaceEditorStore()
    const scene = makeScene()
    scene.racks.push(rack('r1'))
    store.load(scene)
    new DeleteCmd({ racks: [rack('r1')] }).do(store.buildEditorContext())
    await store.save('f1')
    expect(store.dirty.del.size).toBe(0)
    expect(store.dirty.upsert.size).toBe(0)
  })
})
