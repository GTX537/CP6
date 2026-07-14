import { describe, it, expect } from 'vitest'
import { buildMatrixState, toNotifyPatch, type NotifyMatrixRow } from './notifyMatrixModel'

const rows: NotifyMatrixRow[] = [
  { typeKey: 'todoCreated',  typeValue: 1, inAppSupported: true,  emailSupported: true },
  { typeKey: 'flowApproved', typeValue: 2, inAppSupported: true,  emailSupported: true },
  { typeKey: 'flowRejected', typeValue: 3, inAppSupported: true,  emailSupported: true },
  { typeKey: 'timeout',      typeValue: 4, inAppSupported: false, emailSupported: false },
]

describe('notifyMatrixModel', () => {
  it('三态坍缩：空/缺键/畸形 → 全 true', () => {
    for (const json of ['', '{}', '{"notify":{}}', 'NOT_JSON{{{']) {
      const s = buildMatrixState(json, rows)
      expect(s.todoCreated).toEqual({ inApp: true, email: true })
      expect(s.timeout).toEqual({ inApp: true, email: true })
    }
  })

  it('新矩阵形态逐格解析（仅字面 false 为关）', () => {
    const s = buildMatrixState('{"notify":{"flowRejected":{"inApp":true,"email":false}}}', rows)
    expect(s.flowRejected).toEqual({ inApp: true, email: false })
    expect(s.flowApproved).toEqual({ inApp: true, email: true })
  })

  it('遗留扁平形态回落（镜像后端 NotifyMatrix.IsEnabled）', () => {
    const s = buildMatrixState('{"notify":{"todo":false,"email":false,"approved":true}}', rows)
    expect(s.todoCreated).toEqual({ inApp: false, email: false })   // 事件关 → 双关
    expect(s.flowApproved).toEqual({ inApp: true, email: false })   // 全局 email 关 → 仅邮件关
  })

  it('toNotifyPatch 产出可回读的 notify patch', () => {
    const s = buildMatrixState('{}', rows)
    s.flowRejected!.email = false
    const patch = JSON.parse(toNotifyPatch(s))
    expect(patch.notify.flowRejected).toEqual({ inApp: true, email: false })
    expect(patch.notify.todoCreated).toEqual({ inApp: true, email: true })
    expect(Object.keys(patch)).toEqual(['notify'])                  // 只 patch notify 顶层键
  })
})

// ── 波②反射轴自增第 5 行：branchPruned（无遗留键，逐位镜像后端 IsEnabled line 63） ──
const rows5: NotifyMatrixRow[] = [
  ...rows,
  { typeKey: 'branchPruned', typeValue: 5, inAppSupported: true, emailSupported: true },
]

describe('notifyMatrixModel · branchPruned（新类型无遗留形态）', () => {
  it('三态坍缩 → 全 true', () => {
    for (const json of ['', '{}', '{"notify":{}}', 'NOT_JSON{{{']) {
      expect(buildMatrixState(json, rows5).branchPruned).toEqual({ inApp: true, email: true })
    }
  })

  it('遗留扁平 email:false 不波及新类型（镜像后端：无遗留键 → 双开）', () => {
    const s = buildMatrixState('{"notify":{"todo":false,"email":false}}', rows5)
    expect(s.branchPruned).toEqual({ inApp: true, email: true })    // 后端 line 63 无条件 true
    expect(s.todoCreated).toEqual({ inApp: false, email: false })   // 既有类型仍受遗留键约束
  })

  it('新矩阵形态逐格解析对新类型同样生效', () => {
    const s = buildMatrixState('{"notify":{"branchPruned":{"inApp":false,"email":true}}}', rows5)
    expect(s.branchPruned).toEqual({ inApp: false, email: true })
  })
})
