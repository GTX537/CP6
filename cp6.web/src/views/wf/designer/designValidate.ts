// OA 章09 §5 设计时校验。过不了不让坏 schema 进库（漏必填/重复 key/断头节点/审批人不完整）。
// 返回中文诊断串（瞬时提示，不走 i18n key）。纯函数，便于 vitest。
import type { FormSchema } from '@/types/wf/wf'
import { isOptionType } from './controlLibrary'

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
