// H-2 命令单测（ch02 §4/§5/§7）
import { describe, it, expect } from 'vitest'
import { MoveRackCmd } from './commands/MoveRackCmd'
import { RotateRackCmd } from './commands/RotateRackCmd'
import { AddMarkerCmd } from './commands/AddMarkerCmd'
import { AddZoneCmd } from './commands/AddZoneCmd'
import { MoveMarkerCmd } from './commands/MoveMarkerCmd'
import { EditMarkerCmd } from './commands/EditMarkerCmd'
import { EditZoneCmd } from './commands/EditZoneCmd'
import { DeleteCmd } from './commands/DeleteCmd'
import { BatchCmd } from './commands/BatchCmd'
import type { EditorContext } from './Command'
import type { EditorScene, RackVO, MarkerVO, ZoneVO } from '@/types/space/scene'

function makeScene(racks: Partial<RackVO>[] = [], markers: Partial<MarkerVO>[] = []): EditorScene {
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
    racks: racks as RackVO[],
    locations: [],
    markers: markers as MarkerVO[],
  }
}

function makeCtx(scene: EditorScene): EditorContext & { dirtyIds: string[]; deleteIds: string[] } {
  const dirtyIds: string[] = []
  const deleteIds: string[] = []
  return {
    scene,
    markDirty: (id) => dirtyIds.push(id),
    markDirtyDelete: (id) => deleteIds.push(id),
    dirtyIds,
    deleteIds,
  }
}

// ─── MoveRackCmd ──────────────────────────────────────────────────────────────
describe('MoveRackCmd', () => {
  it('do 设 rack 到 toXY', () => {
    const scene = makeScene([{ id: 'r1', x: 0, y: 0 }])
    const ctx = makeCtx(scene)
    const cmd = new MoveRackCmd('r1', { x: 0, y: 0 }, { x: 100, y: 200 })
    cmd.do(ctx)
    expect(scene.racks[0]!.x).toBe(100)
    expect(scene.racks[0]!.y).toBe(200)
    expect(ctx.dirtyIds).toContain('r1')
  })

  it('undo 回 fromXY', () => {
    const scene = makeScene([{ id: 'r1', x: 100, y: 200 }])
    const ctx = makeCtx(scene)
    const cmd = new MoveRackCmd('r1', { x: 0, y: 0 }, { x: 100, y: 200 })
    cmd.undo(ctx)
    expect(scene.racks[0]!.x).toBe(0)
    expect(scene.racks[0]!.y).toBe(0)
  })

  it('merge 同 rackId 合并为一步（undo 直接回原点）', () => {
    const scene = makeScene([{ id: 'r1', x: 0, y: 0 }])
    const ctx = makeCtx(scene)
    const cmd1 = new MoveRackCmd('r1', { x: 0, y: 0 }, { x: 50, y: 0 })
    const cmd2 = new MoveRackCmd('r1', { x: 50, y: 0 }, { x: 100, y: 0 })
    expect(cmd1.merge!(cmd2)).toBe(true)
    // 合并后 do 应移到最终位置
    cmd1.do(ctx)
    expect(scene.racks[0]!.x).toBe(100)
    // undo 应回到最初 fromXY
    cmd1.undo(ctx)
    expect(scene.racks[0]!.x).toBe(0)
  })

  it('merge 不同 rackId 返回 false', () => {
    const cmd1 = new MoveRackCmd('r1', { x: 0, y: 0 }, { x: 10, y: 0 })
    const cmd2 = new MoveRackCmd('r2', { x: 0, y: 0 }, { x: 10, y: 0 })
    expect(cmd1.merge!(cmd2)).toBe(false)
  })
})

// ─── RotateRackCmd ────────────────────────────────────────────────────────────
describe('RotateRackCmd', () => {
  it('do 设位姿到 to（x/y/rotationZ 三值齐改）', () => {
    const scene = makeScene([{ id: 'r1', x: 0, y: 0, rotationZ: 0 }])
    const ctx = makeCtx(scene)
    new RotateRackCmd('r1', { x: 0, y: 0, rotationZ: 0 }, { x: 100, y: 50, rotationZ: 90 }).do(ctx)
    expect(scene.racks[0]!.x).toBe(100)
    expect(scene.racks[0]!.y).toBe(50)
    expect(scene.racks[0]!.rotationZ).toBe(90)
    expect(ctx.dirtyIds).toContain('r1')
  })

  it('undo 回 from（三值齐还原）', () => {
    const scene = makeScene([{ id: 'r1', x: 100, y: 50, rotationZ: 90 }])
    const ctx = makeCtx(scene)
    new RotateRackCmd('r1', { x: 0, y: 0, rotationZ: 0 }, { x: 100, y: 50, rotationZ: 90 }).undo(ctx)
    expect(scene.racks[0]!.x).toBe(0)
    expect(scene.racks[0]!.y).toBe(0)
    expect(scene.racks[0]!.rotationZ).toBe(0)
  })
})

// ─── AddMarkerCmd ─────────────────────────────────────────────────────────────
describe('AddMarkerCmd', () => {
  it('do 插入 marker；undo 删除', () => {
    const scene = makeScene()
    const ctx = makeCtx(scene)
    const marker: MarkerVO = { id: 'm1', floorId: 'f1', x: 10, y: 20, z: 0, markerType: 1, text: 'A' }
    const cmd = new AddMarkerCmd(marker)
    cmd.do(ctx)
    expect(scene.markers).toHaveLength(1)
    expect(scene.markers[0]!.id).toBe('m1')
    expect(ctx.dirtyIds).toContain('m1')
    cmd.undo(ctx)
    expect(scene.markers).toHaveLength(0)
    expect(ctx.deleteIds).toContain('m1')
  })
})

// ─── AddZoneCmd ───────────────────────────────────────────────────────────────
describe('AddZoneCmd', () => {
  it('do 插入 zone；undo 删除', () => {
    const scene = makeScene()
    const ctx = makeCtx(scene)
    const zone: ZoneVO = {
      id: 'z1', floorId: 'f1', zoneCode: 'Z-001', zoneName: '库区A',
      zoneType: 1, polygon: '[[0,0],[1000,0],[1000,1000],[0,1000]]', enable: true,
    }
    const cmd = new AddZoneCmd(zone)
    cmd.do(ctx)
    expect(scene.zones).toHaveLength(1)
    expect(scene.zones[0]!.id).toBe('z1')
    expect(ctx.dirtyIds).toContain('z1')
    cmd.undo(ctx)
    expect(scene.zones).toHaveLength(0)
    expect(ctx.deleteIds).toContain('z1')
  })
})

// ─── MoveMarkerCmd ────────────────────────────────────────────────────────────
describe('MoveMarkerCmd', () => {
  it('do/undo 改 marker x/y', () => {
    const scene = makeScene([], [{ id: 'm1', x: 0, y: 0 }])
    const ctx = makeCtx(scene)
    const cmd = new MoveMarkerCmd('m1', { x: 0, y: 0 }, { x: 50, y: 80 })
    cmd.do(ctx)
    expect(scene.markers[0]!.x).toBe(50)
    expect(scene.markers[0]!.y).toBe(80)
    cmd.undo(ctx)
    expect(scene.markers[0]!.x).toBe(0)
    expect(scene.markers[0]!.y).toBe(0)
  })
})

// ─── EditMarkerCmd ────────────────────────────────────────────────────────────
describe('EditMarkerCmd', () => {
  it('do 应用新字段；undo 还原快照', () => {
    const scene = makeScene([], [{ id: 'm1', text: 'before', markerType: 1 }])
    const ctx = makeCtx(scene)
    const cmd = new EditMarkerCmd('m1', { text: 'before', markerType: 1 }, { text: 'after', markerType: 2 })
    cmd.do(ctx)
    expect(scene.markers[0]!.text).toBe('after')
    expect(scene.markers[0]!.markerType).toBe(2)
    cmd.undo(ctx)
    expect(scene.markers[0]!.text).toBe('before')
    expect(scene.markers[0]!.markerType).toBe(1)
  })
})

// ─── EditZoneCmd ──────────────────────────────────────────────────────────────
describe('EditZoneCmd', () => {
  function sceneWithZone(z: Partial<ZoneVO>): EditorScene {
    const s = makeScene()
    s.zones.push({
      id: 'z1', floorId: 'f1', zoneCode: 'Z-001', zoneName: '库区A',
      zoneType: 1, polygon: '[[0,0],[1,0],[1,1],[0,1]]', enable: true, ...z,
    } as ZoneVO)
    return s
  }

  it('do 应用新字段（名/码/类型/色）；undo 还原快照', () => {
    const scene = sceneWithZone({})
    const ctx = makeCtx(scene)
    const cmd = new EditZoneCmd(
      'z1',
      { zoneName: '库区A', zoneCode: 'Z-001', zoneType: 1, color: null },
      { zoneName: '库区B', zoneCode: 'Z-002', zoneType: 2, color: '#67c23a' },
    )
    cmd.do(ctx)
    expect(scene.zones[0]!.zoneName).toBe('库区B')
    expect(scene.zones[0]!.zoneCode).toBe('Z-002')
    expect(scene.zones[0]!.zoneType).toBe(2)
    expect(scene.zones[0]!.color).toBe('#67c23a')
    expect(ctx.dirtyIds).toContain('z1')

    cmd.undo(ctx)
    expect(scene.zones[0]!.zoneName).toBe('库区A')
    expect(scene.zones[0]!.zoneCode).toBe('Z-001')
    expect(scene.zones[0]!.zoneType).toBe(1)
    expect(scene.zones[0]!.color).toBe(null)
    expect(ctx.dirtyIds).toContain('z1')
  })

  it('部分补丁（仅改名）只动指定字段', () => {
    const scene = sceneWithZone({ zoneCode: 'KEEP' })
    const ctx = makeCtx(scene)
    new EditZoneCmd('z1', { zoneName: '库区A' }, { zoneName: '改后' }).do(ctx)
    expect(scene.zones[0]!.zoneName).toBe('改后')
    expect(scene.zones[0]!.zoneCode).toBe('KEEP') // 未在补丁内 → 不动
  })

  it('目标 zone 不存在时静默无操作', () => {
    const scene = makeScene()
    const ctx = makeCtx(scene)
    expect(() => new EditZoneCmd('nope', { zoneName: 'a' }, { zoneName: 'b' }).do(ctx)).not.toThrow()
  })
})

// ─── DeleteCmd ────────────────────────────────────────────────────────────────
describe('DeleteCmd', () => {
  it('do 删除；undo 快照还原', () => {
    const rack: RackVO = { id: 'r1', zoneId:'z', floorId:'f', rackCode:'R1', x:0, y:0, z:0, rotationZ:0, cols:1, levels:1, depthCount:1, cellW:1000, cellH:1000, cellD:1000 }
    const marker: MarkerVO = { id: 'm1', floorId:'f', x:0, y:0, z:0, markerType:1, text:'T' }
    const scene = makeScene([rack], [marker])
    const ctx = makeCtx(scene)

    const cmd = new DeleteCmd({ racks: [rack], markers: [marker] })
    cmd.do(ctx)
    expect(scene.racks).toHaveLength(0)
    expect(scene.markers).toHaveLength(0)
    expect(ctx.deleteIds).toContain('r1')
    expect(ctx.deleteIds).toContain('m1')

    // undo 还原
    cmd.undo(ctx)
    expect(scene.racks).toHaveLength(1)
    expect(scene.markers).toHaveLength(1)
    expect(ctx.dirtyIds).toContain('r1')
    expect(ctx.dirtyIds).toContain('m1')
  })
})

// ─── BatchCmd ─────────────────────────────────────────────────────────────────
describe('BatchCmd', () => {
  it('do 顺序执行；undo 逆序执行', () => {
    const log: string[] = []
    const cmds = [
      { label: 'A', do: () => log.push('doA'), undo: () => log.push('unA') },
      { label: 'B', do: () => log.push('doB'), undo: () => log.push('unB') },
      { label: 'C', do: () => log.push('doC'), undo: () => log.push('unC') },
    ]
    const batch = new BatchCmd(cmds)
    const ctx = {} as EditorContext
    batch.do(ctx)
    expect(log).toEqual(['doA', 'doB', 'doC'])
    batch.undo(ctx)
    expect(log).toEqual(['doA', 'doB', 'doC', 'unC', 'unB', 'unA'])
  })

  it('空 BatchCmd 不报错', () => {
    const batch = new BatchCmd([])
    expect(() => batch.do({} as EditorContext)).not.toThrow()
    expect(() => batch.undo({} as EditorContext)).not.toThrow()
  })
})
