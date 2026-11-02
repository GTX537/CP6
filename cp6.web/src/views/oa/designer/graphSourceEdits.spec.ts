import { describe, expect, it } from 'vitest'
import { graphToSchema, schemaToGraph, type FlowSchemaDto } from './designerModel'
import { applyGraphEdits } from './graphSourceEdits'
import { preserveSchema } from './preserveSchema'

const raw = '{"start":"s","opaque":{"amount":12345678901234567890.0001},"nodes":[{"id":"s","type":"start"},{"id":"a","type":"approval"},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"a","condition":null,"opaque":{"amount":9007199254740993.0001}},{"from":"a","to":"e","condition":"ready","opaque":"second"}]}'
function source() { return JSON.parse(raw) as FlowSchemaDto }
describe('edits from a display graph preserve the authoritative source', () => {
  it('changes an edge condition while retaining untouched nulls, extensions and exact numeric lexemes', () => {
    const original = source(), before = graphToSchema(schemaToGraph(original)), after = structuredClone(before)
    after.edges[1]!.condition = 'approved'
    const value = applyGraphEdits(original, before, after), encoded = preserveSchema(raw, value)
    expect(value.edges[0]).toEqual(original.edges[0]); expect(value.edges[1]).toEqual({ ...original.edges[1], condition: 'approved' })
    expect(value.nodes).toEqual(original.nodes)
    expect(encoded).toContain('12345678901234567890.0001'); expect(encoded).toContain('9007199254740993.0001')
  })
  it('keeps the right edge source through reorder, removal and newly drawn connections', () => {
    const original = source(), before = graphToSchema(schemaToGraph(original)), after = structuredClone(before)
    after.edges = [after.edges[1]!, { id: 'new', from: 's', to: 'e', condition: 'bypass' }, after.edges[0]!]
    const reordered = applyGraphEdits(original, before, after)
    expect(reordered.edges).toEqual([original.edges[1], after.edges[1], original.edges[0]])
    after.edges = [after.edges[0]!]
    expect(applyGraphEdits(original, before, after).edges).toEqual([original.edges[1]])
  })
  it('returns to the exact source after positions are undone and preserves parent property edits on reload', () => {
    const original = source(), before = graphToSchema(schemaToGraph(original)), after = structuredClone(before)
    after.nodes[1]!.x = 220
    const moved = applyGraphEdits(original, before, after)
    expect(moved.nodes[1]!.x).toBe(220); expect(moved.nodes[0]).not.toHaveProperty('x')
    expect(preserveSchema(raw, applyGraphEdits(original, before, before))).toBe(raw)
    const renamed = { ...moved, nodes: moved.nodes.map(n => n.id === 'a' ? { ...n, name: 'New name' } : n) }
    const reloaded = graphToSchema(schemaToGraph(renamed))
    expect(applyGraphEdits(renamed, reloaded, reloaded)).toEqual(renamed)
  })
})
