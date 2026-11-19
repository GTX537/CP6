export const publicationLanguages = ['zh-CN', 'zh-TW', 'en', 'ja', 'ko'] as const
type Language = typeof publicationLanguages[number]
export type LanguageMessages = Record<string, string>

interface PackIdentity {
  length: number
  sha256: string
}

interface PublicationManifest {
  version: string
  langs: Language[]
  packs: Record<Language, PackIdentity>
}

export interface VerifiedPublication {
  manifest: PublicationManifest
  messages: Record<Language, LanguageMessages>
  texts: Record<Language, string>
}

const cacheKey = 'cp6_i18n_verified_publication_v1'
const forbiddenSegments = new Set(['__proto__', 'prototype', 'constructor'])

function isRecord(value: unknown): value is Record<string, unknown> {
  return !!value && typeof value === 'object' && !Array.isArray(value)
}

export function readLanguageMessages(data: unknown): LanguageMessages {
  if (!isRecord(data)) throw new Error('语言包格式无效。')
  const projected: LanguageMessages = {}
  for (const [key, value] of Object.entries(data)) {
    if (typeof value !== 'string' || !key || key.split('.').some(part => forbiddenSegments.has(part)))
      throw new Error('语言包字段无效。')
    projected[key] = value
  }
  return projected
}

export function currentManifest(data: unknown): PublicationManifest | null {
  if (!isRecord(data) || typeof data.version !== 'string') throw new Error('语言发布清单无效。')
  // 尚无版本或历史格式清单通过兼容的实时词条入口接续。
  if (data.version === '' || (!data.packs && /^v[0-9]{14}$/.test(data.version))) return null
  const langs = data.langs
  if (!/^v[a-zA-Z0-9_-]{1,80}$/.test(data.version) ||
      !Array.isArray(langs) || langs.length !== publicationLanguages.length ||
      publicationLanguages.some(lang => !langs.includes(lang)) ||
      !isRecord(data.packs) || Object.keys(data.packs).length !== publicationLanguages.length)
    throw new Error('语言发布清单缺少完整的五语集合。')
  const packs = {} as Record<Language, PackIdentity>
  for (const language of publicationLanguages) {
    const identity = data.packs[language]
    if (!isRecord(identity) || typeof identity.length !== 'number' || !Number.isSafeInteger(identity.length) ||
        identity.length < 2 || identity.length > 32 * 1024 * 1024 ||
        typeof identity.sha256 !== 'string' || !/^[0-9a-f]{64}$/.test(identity.sha256))
      throw new Error('语言包摘要或长度无效。')
    packs[language] = { length: identity.length, sha256: identity.sha256 }
  }
  return { version: data.version, langs: [...publicationLanguages], packs }
}

async function checkedPack(identity: PackIdentity, bytes: Uint8Array<ArrayBuffer>) {
  if (bytes.length !== identity.length) throw new Error('语言包的实际长度与清单不同。')
  const sha = new Uint8Array(await crypto.subtle.digest('SHA-256', bytes))
  if (Array.from(sha, value => value.toString(16).padStart(2, '0')).join('') !== identity.sha256)
    throw new Error('语言包的实际摘要与清单不同。')
  const text = new TextDecoder('utf-8', { fatal: true }).decode(bytes)
  return { text, value: readLanguageMessages(JSON.parse(text)) }
}

export async function verifyPublication(manifest: PublicationManifest,
  readBytes: (language: Language) => Promise<ArrayBuffer>): Promise<VerifiedPublication> {
  const entries = await Promise.all(publicationLanguages.map(async language => (
    [language, await checkedPack(manifest.packs[language], new Uint8Array(await readBytes(language)))] as const
  )))
  return {
    manifest,
    messages: Object.fromEntries(entries.map(([lang, entry]) => [lang, entry.value])) as VerifiedPublication['messages'],
    texts: Object.fromEntries(entries.map(([lang, entry]) => [lang, entry.text])) as VerifiedPublication['texts'],
  }
}

export function samePublication(a: PublicationManifest, b: PublicationManifest) {
  return a.version === b.version && publicationLanguages.every(language =>
    a.packs[language].sha256 === b.packs[language].sha256 && a.packs[language].length === b.packs[language].length)
}

export async function recoverVerifiedPublication(): Promise<VerifiedPublication | undefined> {
  try {
    const raw = localStorage.getItem(cacheKey)
    if (!raw) return undefined
    const data = JSON.parse(raw)
    const manifest = currentManifest(data.manifest)
    if (!manifest || !isRecord(data.texts)) return undefined
    return await verifyPublication(manifest, async lang => {
      const text = data.texts[lang]
      if (typeof text !== 'string') throw new Error('缓存缺少语言包。')
      return new TextEncoder().encode(text).buffer
    })
  } catch { return undefined }
}

export function saveVerifiedPublication(value: VerifiedPublication) {
  try { localStorage.setItem(cacheKey, JSON.stringify({ manifest: value.manifest, texts: value.texts })) }
  catch { /* 浏览器仍保留本会话核验的整套消息。 */ }
}
