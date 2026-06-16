// OA 章09 §5 设计器撤销/重做。schema 是普通对象，快照栈（JSON 序列化）最省事。
// 表单/流程设计器共用（泛型）。

const MAX = 100

/** schema 快照栈：push 截断重做尾、与栈顶相同不压；undo/redo 返回深拷贝快照。 */
export class SchemaHistory<T> {
  private stack: string[]
  private idx: number

  constructor(initial: T) {
    this.stack = [JSON.stringify(initial)]
    this.idx = 0
  }

  get canUndo(): boolean {
    return this.idx > 0
  }
  get canRedo(): boolean {
    return this.idx < this.stack.length - 1
  }

  /** 压入新快照（与当前相同则忽略；压入即清空重做尾）。 */
  push(state: T): void {
    const json = JSON.stringify(state)
    if (json === this.stack[this.idx]) return // 无变化不压
    this.stack = this.stack.slice(0, this.idx + 1)
    this.stack.push(json)
    this.idx++
    if (this.stack.length > MAX) {
      this.stack.shift()
      this.idx--
    }
  }

  /** 回到上一快照（深拷贝）；栈底返回 null。 */
  undo(): T | null {
    if (!this.canUndo) return null
    this.idx--
    return JSON.parse(this.stack[this.idx]!)
  }

  /** 前进一快照（深拷贝）；栈顶返回 null。 */
  redo(): T | null {
    if (!this.canRedo) return null
    this.idx++
    return JSON.parse(this.stack[this.idx]!)
  }

  /** 以新初值重置栈（如加载已有定义后）。 */
  reset(initial: T): void {
    this.stack = [JSON.stringify(initial)]
    this.idx = 0
  }
}
