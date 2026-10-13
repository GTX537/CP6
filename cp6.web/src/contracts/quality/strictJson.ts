/** JSON grammar comes from the platform parser; a lexical pass rejects duplicate decoded names.
 * Validation completes before any typed contract is returned. No Unicode normalization is applied.
 */
export function parseStrictJson(json: string): unknown {
  if (typeof json !== 'string') throw new TypeError('JSON_TEXT_REQUIRED')
  const parsed: unknown = JSON.parse(json)
  const frames: (Set<string> | null)[] = []
  for (let index = 0; index < json.length; index++) {
    const token = json[index]
    if (token === '{') frames.push(new Set())
    else if (token === '[') frames.push(null)
    else if (token === '}' || token === ']') frames.pop()
    else if (token === '"') {
      const start = index
      for (index++; index < json.length; index++) {
        if (json[index] === '\\') index++
        else if (json[index] === '"') break
      }
      let next = index + 1
      while (next < json.length && /[\t\n\r ]/.test(json[next]!)) next++
      if (json[next] === ':') {
        const name = JSON.parse(json.slice(start, index + 1)) as string
        const names = frames[frames.length - 1]!
        if (names!.has(name)) throw new TypeError('DUPLICATE_PROPERTY_NAME')
        names!.add(name)
      }
    }
  }
  return parsed
}
