// OA 章09 §5 设计时校验。过不了不让坏 schema 进库（漏必填/重复 key/断头节点/审批人不完整）。
// 返回中文诊断串（瞬时提示，不走 i18n key）。纯函数，便于 vitest。
import type { FormSchema, FlowDesignSchema } from '@/types/wf/wf'
import { isOptionType } from './controlLibrary'
import { reachableEndIds } from './flowGraph'

/** 校验表单 schema：字段标识非空且不重复、选项类控件至少 1 个选项。返回错误清单（空=通过）。 */
export function validateFormSchema(schema: FormSchema): string[] {
  const errors: string[] = []
  const seen = new Set<string>()
  for (const f of schema.fields || []) {
    const name = (f.name || '').trim()
    if (!name) {
      errors.push('存在未命名字段（字段标识不能为空）')
    } else if (seen.has(name)) {
      errors.push(`字段标识重复：${name}`)
    } else {
      seen.add(name)
    }
    if (isOptionType(f.type) && !(f.options && f.options.length > 0)) {
      errors.push(`${f.label || name} 缺少选项`)
    }
  }
  return errors
}

/**
 * 校验流程 schema（章09 §3/§5）：需至少 1 结束节点；连线两端节点须存在；审批节点须能到达结束
 * （断头节点）；审批节点须配齐审批人规则（策略 + 对应取值）。返回错误清单（空=通过）。
 */
export function validateFlowSchema(schema: FlowDesignSchema): string[] {
  const errors: string[] = []
  const nodes = schema.nodes || []
  const edges = schema.edges || []
  const nameOf = (id: string) => nodes.find((n) => n.id === id)?.name || id

  if (!nodes.some((n) => n.type === 'end')) errors.push('流程缺少结束节点')
  if (!nodes.some((n) => n.type === 'approval')) errors.push('流程缺少审批节点')

  const ids = new Set(nodes.map((n) => n.id))
  for (const e of edges) {
    if (!ids.has(e.from) || !ids.has(e.to)) errors.push(`存在悬空连线：${e.from} → ${e.to}`)
  }

  const canReach = reachableEndIds(nodes, edges)
  for (const n of nodes) {
    if (n.type !== 'approval') continue
    if (!canReach.has(n.id)) errors.push(`节点「${n.name || n.id}」无法到达结束节点`)

    const strat = n.approverStrategy
    if (!strat) {
      errors.push(`节点「${nameOf(n.id)}」未配置审批人`)
    } else if (strat === 'Specified' && !n.approverUserId) {
      errors.push(`节点「${nameOf(n.id)}」指定审批人为空`)
    } else if (strat === 'Role' && !n.approverRoleId) {
      errors.push(`节点「${nameOf(n.id)}」未指定角色`)
    }
    if (n.timeoutAction === 'escalate' && !n.escalateTo) {
      errors.push(`节点「${nameOf(n.id)}」超时升级对象为空`)
    }
  }
  return errors
}
