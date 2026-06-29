// Command 接口 + EditorContext（ch02 §9）
import type { EditorScene } from '@/types/space/scene'

/** 命令执行上下文：注入 scene 引用 + dirty 标记；单测可传 {} as any */
export interface EditorContext {
  scene: EditorScene
  markDirty: (id: string) => void
  markDirtyDelete: (id: string) => void
}

/**
 * Command 接口：do/undo 均接收 EditorContext，通过它改 scene 对象图。
 * merge?: 栈顶命令可选实现，返回 true 表示已吸收 next（同类型连续操作合为一步）。
 */
export interface Command {
  label: string
  do(ctx: EditorContext): void
  undo(ctx: EditorContext): void
  merge?(next: Command): boolean
}
