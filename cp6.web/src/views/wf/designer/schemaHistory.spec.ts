import { describe, it, expect } from 'vitest'
import { SchemaHistory } from './schemaHistory'

// OA 章09 §5 设计器撤销/重做快照栈。
describe('SchemaHistory', () => {
  it('undo/redo 回放快照', () => {
    const h = new SchemaHistory({ v: 0 })
    expect(h.canUndo).toBe(false)
    h.push({ v: 1 })
    h.push({ v: 2 })
    expect(h.canUndo).toBe(true)
    expect(h.canRedo).toBe(false)

    expect(h.undo()).toEqual({ v: 1 })
    expect(h.undo()).toEqual({ v: 0 })
    expect(h.canUndo).toBe(false)
    expect(h.undo()).toBeNull() // 到底返回 null

    expect(h.redo()).toEqual({ v: 1 })
    expect(h.redo()).toEqual({ v: 2 })
    expect(h.redo()).toBeNull() // 到顶返回 null
  })

  it('push 截断重做尾', () => {
    const h = new SchemaHistory({ v: 0 })
    h.push({ v: 1 })
    h.push({ v: 2 })
    h.undo() // 回到 v:1
    h.push({ v: 9 }) // 新分支 → 截断 v:2
    expect(h.canRedo).toBe(false)
    expect(h.undo()).toEqual({ v: 1 })
    expect(h.redo()).toEqual({ v: 9 })
  })

  it('相同快照不压栈', () => {
    const h = new SchemaHistory({ v: 0 })
    h.push({ v: 0 }) // 无变化
    expect(h.canUndo).toBe(false)
  })

  it('返回深拷贝（改返回值不污染栈）', () => {
    const h = new SchemaHistory({ a: { n: 1 } })
    h.push({ a: { n: 2 } })
    const snap = h.undo() as { a: { n: number } }
    snap.a.n = 999
    expect((h.redo() as { a: { n: number } }).a.n).toBe(2) // 栈内不被污染
  })

  it('reset 重置栈', () => {
    const h = new SchemaHistory({ v: 0 })
    h.push({ v: 1 })
    h.reset({ v: 5 })
    expect(h.canUndo).toBe(false)
    expect(h.canRedo).toBe(false)
  })
})
