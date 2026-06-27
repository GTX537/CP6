import type { Command, EditorContext } from '../Command'

/** 批量命令：do 顺序执行，undo 逆序执行 */
export class BatchCmd implements Command {
  label = 'Batch'

  constructor(private cmds: Command[]) {}

  do(ctx: EditorContext): void {
    for (const cmd of this.cmds) cmd.do(ctx)
  }

  undo(ctx: EditorContext): void {
    for (let i = this.cmds.length - 1; i >= 0; i--) this.cmds[i].undo(ctx)
  }
}
