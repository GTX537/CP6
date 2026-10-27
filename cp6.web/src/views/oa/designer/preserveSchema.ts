import { parseStrictJson } from '@/contracts/quality/strictJson'
interface Node { raw: string; value: any; children?: Map<string, Node>; items?: Node[] }
/** Reuse untouched original fragments, including number spelling/precision and opaque extensions.
 * The visual editor receives ordinary values; it never reserializes untouched source subtrees.
 */
export function preserveSchema(original: string, next: unknown): string {
  parseStrictJson(original)
  let at = 0
  const white = () => { while (/\s/.test(original[at] ?? '') && at < original.length) at++ }
  function stringEnd() {
    at++
    while (at < original.length) { const c = original[at++]; if (c === '\\') at++; else if (c === '"') return }
  }
  function read(): Node {
    white(); const start = at; const node: Node = { raw: '', value: null }
    if (original[at] === '{') {
      node.children = new Map(); at++; white()
      while (original[at] !== '}') {
        const begin = at; stringEnd(); const key = JSON.parse(original.slice(begin, at)) as string
        white(); at++; node.children.set(key, read()); white()
        if (original[at] !== ',') break
        at++; white()
      }
      at++
    } else if (original[at] === '[') {
      node.items = []; at++; white()
      while (original[at] !== ']') { node.items.push(read()); white(); if (original[at] !== ',') break; at++ }
      at++
    } else if (original[at] === '"') stringEnd()
    else while (at < original.length && !/[\s,}\]]/.test(original[at]!)) at++
    node.raw = original.slice(start, at); node.value = JSON.parse(node.raw); return node
  }
  function write(value: any, source?: Node): string {
    if (source && JSON.stringify(source.value) === JSON.stringify(value)) return source.raw
    if (Array.isArray(value)) return '[' + value.map((item, index) => {
      const matching = item && typeof item === 'object' && typeof item.id === 'string'
        ? source?.items?.find(n => n.value?.id === item.id) : source?.items?.[index]
      return write(item, matching)
    }).join(',') + ']'
    if (value && typeof value === 'object') return '{' + Object.entries(value).filter(([, v]) => v !== undefined)
      .map(([k, v]) => JSON.stringify(k) + ':' + write(v, source?.children?.get(k))).join(',') + '}'
    if (typeof value === 'number' && !Number.isFinite(value)) throw new Error('流程数值无效')
    const encoded = JSON.stringify(value)
    if (encoded === undefined) throw new Error('流程结构无效')
    return encoded
  }
  return write(next, read())
}
