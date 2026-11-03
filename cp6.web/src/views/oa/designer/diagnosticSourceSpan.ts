/** Locate a validator path in the already parsed source, without reserializing numbers. */
export function diagnosticSourceSpan(raw: string, path: string): [number, number] | undefined {
  const parts = [...path.matchAll(/\.([^.[\]]+)|\[(\d+)\]/g)].map(x => x[2] === undefined ? x[1]! : Number(x[2]))
  const white = (at: number) => { while (at < raw.length && /\s/.test(raw[at]!)) at++; return at }
  function quoted(at: number) { for (at++; at < raw.length; at++) { if (raw[at] === '\\') at++; else if (raw[at] === '"') return at + 1 }; return raw.length }
  function end(at: number) {
    if (raw[at] === '"') return quoted(at)
    if (raw[at] !== '{' && raw[at] !== '[') { while (at < raw.length && !/[\s,}\]]/.test(raw[at]!)) at++; return at }
    let depth = 0
    for (; at < raw.length; at++) { const c = raw[at]; if (c === '"') at = quoted(at) - 1; else if (c === '{' || c === '[') depth++; else if ((c === '}' || c === ']') && --depth === 0) return at + 1 }
    return raw.length
  }
  let start = white(0), finish = end(start)
  for (const part of parts) {
    const object = raw[start] === '{', array = raw[start] === '['
    if (!object && !array) return undefined
    let at = white(start + 1), index = 0, found = false
    while (at < finish - 1) {
      let key: string | number = index++
      if (object) { const after = quoted(at); key = JSON.parse(raw.slice(at, after)); at = white(white(after) + 1) }
      const after = end(at)
      if (key === part) { start = at; finish = after; found = true; break }
      at = white(after); if (raw[at] !== ',') break; at = white(at + 1)
    }
    if (!found) return undefined
  }
  return [start, finish]
}
