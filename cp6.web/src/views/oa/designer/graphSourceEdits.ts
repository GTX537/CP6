import type { FlowSchemaDto } from './designerModel'

/** Apply actual canvas edits to the source, keeping display-only defaults and
 * properties outside the graph projection out of the write contract. */
export function applyGraphEdits(source: FlowSchemaDto, before: FlowSchemaDto, after: FlowSchemaDto): FlowSchemaDto {
  function merge(source: any, before: any, after: any): any {
    if (JSON.stringify(before) === JSON.stringify(after)) return source
    if (Array.isArray(before) && Array.isArray(after)) {
      return after.map((item, index) => {
        const previous = typeof item?.id === 'string' ? before.findIndex(value => value.id === item.id) : index
        return previous < 0 ? item : merge(source?.[previous], before[previous], item)
      })
    }
    if (before && after && typeof before === 'object' && typeof after === 'object' && !Array.isArray(before) && !Array.isArray(after)) {
      const result = { ...source }
      for (const key of new Set([...Object.keys(before), ...Object.keys(after)])) {
        if (JSON.stringify(before[key]) === JSON.stringify(after[key])) continue
        if (Object.hasOwn(after, key)) result[key] = merge(source?.[key], before[key], after[key])
        else delete result[key]
      }
      return result
    }
    return after
  }
  return merge(source, before, after)
}
