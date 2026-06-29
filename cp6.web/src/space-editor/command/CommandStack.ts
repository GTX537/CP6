// CommandStack — 双栈撤销/重做（ch02 §9.1）
import type { Command, EditorContext } from './Command'

const CAP = 100

export class CommandStack {
  readonly undoStack: Command[] = []
  readonly redoStack: Command[] = []

  /**
   * 执行命令：先 do，再尝试 merge 到栈顶（同类型连续操作合一步），清 redo 栈。
   * 超过 CAP 则丢最旧。
   */
  exec(cmd: Command, ctx: EditorContext): void {
    cmd.do(ctx)

    // 尝试合并到栈顶
    const top = this.undoStack.at(-1)
    if (top?.merge && top.merge(cmd)) {
      // 已合并——top 已吸收 cmd 的 "to" 信息，不再压栈
      this.redoStack.length = 0
      return
    }

    this.undoStack.push(cmd)
    if (this.undoStack.length > CAP) this.undoStack.shift()
    this.redoStack.length = 0
  }

  /** 撤销：弹 undoStack 顶，调 undo，压 redoStack */
  undo(ctx: EditorContext): void {
    const cmd = this.undoStack.pop()
    if (!cmd) return
    cmd.undo(ctx)
    this.redoStack.push(cmd)
  }

  /** 重做：弹 redoStack 顶，调 do，压 undoStack */
  redo(ctx: EditorContext): void {
    const cmd = this.redoStack.pop()
    if (!cmd) return
    cmd.do(ctx)
    this.undoStack.push(cmd)
  }

  get canUndo(): boolean { return this.undoStack.length > 0 }
  get canRedo(): boolean { return this.redoStack.length > 0 }
}
