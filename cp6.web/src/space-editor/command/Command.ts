// Command 接口 + EditorContext（ch02 §9）
import type { EditorScene, DeleteKind } from '@/types/space/scene'

/** 命令执行上下文：注入 scene 引用 + dirty 标记；单测可传 {} as any */
export interface EditorContext {
  scene: EditorScene
  markDirty: (id: string) => void
  /** 记录一个待删除 id；kind 指明落入 deletes 的哪个桶（省略则 save 时回退 scene 过滤分桶并告警） */
  markDirtyDelete: (id: string, kind?: DeleteKind) => void
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
