import { describe, it, expect } from 'vitest'
import { CommandStack } from './CommandStack'
import type { Command, EditorContext } from './Command'

const ctx = {} as EditorContext

/** 创建无 merge 的简单日志命令 */
function mkCmd(n: string, log: string[]): Command {
  return {
    label: n,
    do: () => log.push('do' + n),
    undo: () => log.push('un' + n),
  }
}

describe('CommandStack', () => {
  it('exec 执行命令并入栈', () => {
    const log: string[] = []
    const stack = new CommandStack()
    stack.exec(mkCmd('A', log), ctx)
    expect(log).toEqual(['doA'])
    expect(stack.undoStack).toHaveLength(1)
  })

  it('undo/redo + new exec 清 redo', () => {
    const log: string[] = []
    const stack = new CommandStack()
    stack.exec(mkCmd('A', log), ctx)
    stack.exec(mkCmd('B', log), ctx)

    stack.undo(ctx)
    expect(log.at(-1)).toBe('unB')

    stack.redo(ctx)
    expect(log.at(-1)).toBe('doB')

    // 清 redo：先 undo B，再 exec C
    stack.undo(ctx)
    stack.exec(mkCmd('C', log), ctx)
    // redo 栈已清，redo() 无事可做，最后一条仍是 doC
    stack.redo(ctx)
    expect(log.at(-1)).toBe('doC')
  })

  it('超容量丢最旧', () => {
    const log: string[] = []
    const stack = new CommandStack()
    for (let i = 0; i < 101; i++) stack.exec(mkCmd(String(i), log), ctx)
    expect(stack.undoStack).toHaveLength(100)
    // 最旧 (0) 已被移除，最新是 100
    expect(stack.undoStack[0]!.label).toBe('1')
    expect(stack.undoStack.at(-1)!.label).toBe('100')
  })

  it('merge 合并栈顶（连续同类型操作变一步）', () => {
    const log: string[] = []
    const stack = new CommandStack()

    // 可合并命令：记录 to 值
    let mergedTo = 0
    const mergeable = (from: number, to: number): Command => ({
      label: 'mv',
      do: () => { log.push('do' + to); mergedTo = to },
      undo: () => log.push('un' + from),
      merge(next) {
        if (next.label === 'mv') {
          // 吸收 next：把 do 更新为 next 的 to（简化：读 next.label，实际 MoveRackCmd 覆写 toXY）
          mergedTo = (next as any)._to ?? to
          return true
        }
        return false
      },
      _to: to,
    } as Command & { _to: number })

    stack.exec(mergeable(0, 10), ctx)  // 压栈
    stack.exec(mergeable(10, 20), ctx) // 合并到栈顶

    // 只有 1 条在栈
    expect(stack.undoStack).toHaveLength(1)
    // redo 已清
    expect(stack.redoStack).toHaveLength(0)
  })

  it('undo 空栈不报错', () => {
    const stack = new CommandStack()
    expect(() => stack.undo(ctx)).not.toThrow()
  })

  it('redo 空栈不报错', () => {
    const stack = new CommandStack()
    expect(() => stack.redo(ctx)).not.toThrow()
  })

  it('canUndo / canRedo 语义', () => {
    const log: string[] = []
    const stack = new CommandStack()
    expect(stack.canUndo).toBe(false)
    expect(stack.canRedo).toBe(false)
    stack.exec(mkCmd('A', log), ctx)
    expect(stack.canUndo).toBe(true)
    stack.undo(ctx)
    expect(stack.canRedo).toBe(true)
  })
})
