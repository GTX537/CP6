import type { FieldMask, FieldPerm } from '@/types/wf/wf'

/**
 * 计算节点字段权限掩码（OA 章04 D-1）。
 * 默认：发起节点全 edit；审批节点全 readonly。节点 fieldPerms 覆盖默认。
 * 与 PUB B1 角色字段权限取交集（更严的赢）留 OA-D5，PUB 字段权限落地后接。
 */
export function buildFieldMask(
  fieldNames: string[],
  nodeFieldPerms?: Record<string, FieldPerm>,
  isStarterNode = false,
): FieldMask {
  const fallback: FieldPerm = isStarterNode ? 'edit' : 'readonly'
  const mask: FieldMask = {}
  for (const name of fieldNames) {
    mask[name] = nodeFieldPerms?.[name] ?? fallback
  }
  return mask
}

/** 安全解析 JSON 对象，失败返回 {} */
export function safeParseObject(json?: string | null): Record<string, any> {
  if (!json) return {}
  try {
    const v = JSON.parse(json)
    return v && typeof v === 'object' ? v : {}
  } catch {
    return {}
  }
}
