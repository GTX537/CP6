// @vitest-environment jsdom
import { createHash, webcrypto } from 'node:crypto'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const transport = vi.hoisted(() => ({ get: vi.fn() }))
vi.mock('axios', () => ({ default: { create: () => transport } }))

const modes = ['zh-CN', 'zh-TW', 'en', 'ja', 'ko']
function release(version: string, title: string) {
  const content = Object.fromEntries(modes.map(language => [language, JSON.stringify({
    'lang.fixture': title + ' ' + language,
    'plm.locale.added': '原值 {reference}',
    '旧キー': '同じ',
  })]))
  const packs = Object.fromEntries(modes.map(language => {
    const value = content[language]!
    return [language, { length: Buffer.byteLength(value), sha256: createHash('sha256').update(value).digest('hex') }]
  }))
  return { manifest: { version, langs: modes, packs }, content }
}
function bytes(raw: string) {
  return new TextEncoder().encode(raw).buffer
}

beforeEach(() => {
  vi.resetModules()
  vi.stubEnv('VITE_I18N_MODE', 'publish')
  vi.stubGlobal('crypto', webcrypto)
  localStorage.clear()
  sessionStorage.clear()
  localStorage.setItem('lang', 'ja')
  transport.get.mockReset()
})
afterEach(() => vi.unstubAllGlobals())

describe('按精确版消费语言包', () => {
  it('同次安装核验五包，目标缺最后一包时保留现有完整版', async () => {
    const first = release('vfixture-first', '第一版')
    const second = release('vfixture-second', '第二版')
    let available = first
    transport.get.mockImplementation(async (url: string) => {
      if (url === '/lang/manifest') return { data: available.manifest }
      const code = url.split('/').at(-1)!
      if (code === 'ko' && available === second) throw new Error('受控缺包')
      return { data: bytes(available.content[code]!) }
    })
    const { default: i18n, initI18n, changeLang, ensureNamespacesForPath } = await import('../index')
    await initI18n()
    expect(i18n.global.t('lang.fixture')).toBe('第一版 ja')
    expect(transport.get.mock.calls.filter(([path]) => path.startsWith('/lang/published/')).map(([path]) => path.split('/').at(-1)).sort()).toEqual([...modes].sort())
    available = second
    await changeLang('en')
    await ensureNamespacesForPath('/plm/design')
    expect(i18n.global.t('lang.fixture')).toBe('第一版 en')
    expect(i18n.global.t('plm.locale.added', { reference: 'REF-0003' })).toBe('原值 REF-0003')
    expect(i18n.global.t('missing.fixture.code')).toBe('missing.fixture.code')
    expect(transport.get.mock.calls.some(([path]) => path === '/lang/en')).toBe(false)
  })

  it('忽略发布采用前已发出的实时命名空间响应，离线仍复核整套缓存', async () => {
    const first = release('vfixture-exact', '核验版')
    let published = false
    let offline = false
    let releaseLegacy!: () => void
    const held = new Promise<void>(resolve => { releaseLegacy = resolve })
    transport.get.mockImplementation(async (url: string) => {
      if (offline) throw new Error('模拟离线')
      if (url === '/lang/manifest') return { data: published ? first.manifest : { version: '' } }
      if (url.includes('/ns/')) {
        if (url.endsWith('/wms')) await held
        return { data: { 'lang.fixture': '旧实时词条' } }
      }
      return { data: bytes(first.content[url.split('/').at(-1)!]!) }
    })
    const { default: i18n, initI18n, changeLang, ensureNamespacesForPath } = await import('../index')
    await initI18n()
    const outdated = ensureNamespacesForPath('/wms/tasks')
    published = true
    await changeLang('ja')
    releaseLegacy()
    await outdated
    expect(i18n.global.t('lang.fixture')).toBe('核验版 ja')
    offline = true
    vi.resetModules()
    const reopened = await import('../index')
    await reopened.initI18n()
    expect(reopened.default.global.t('lang.fixture')).toBe('核验版 ja')
    expect(reopened.languagePublicationUnavailable.value).toBe(true)
  })

  it('拒绝已核验版本的新摘要，并按约定回退语言解析缺译', async () => {
    const first = release('vfixture-fixed', '固定原版')
    first.content.en = JSON.stringify({ 'english.only': 'English' })
    first.manifest.packs.en = {
      length: Buffer.byteLength(first.content.en),
      sha256: createHash('sha256').update(first.content.en).digest('hex'),
    }
    const different = release('vfixture-fixed', '异字节')
    let live = first
    transport.get.mockImplementation(async (url: string) => {
      if (url === '/lang/manifest') return { data: live.manifest }
      return { data: bytes(live.content[url.split('/').at(-1)!]!) }
    })
    const { default: i18n, changeLang, languagePublicationUnavailable } = await import('../index')
    await changeLang('en')
    expect(i18n.global.t('lang.fixture')).toBe('固定原版 ja')
    live = different
    const fetched = transport.get.mock.calls.filter(([path]) => path.includes('/published/')).length
    await changeLang('en')
    expect(i18n.global.t('lang.fixture')).toBe('固定原版 ja')
    expect(languagePublicationUnavailable.value).toBe(true)
    expect(transport.get.mock.calls.filter(([path]) => path.includes('/published/'))).toHaveLength(fetched)
  })

  it('旧的切换与异摘要不会覆盖已核验的业务界面', async () => {
    const first = release('vfixture-first', '确认版')
    const second = release('vfixture-second', '坏摘要版')
    let useCorrupted = false
    let delayed = false
    let finishOld!: () => void
    const hold = new Promise<void>(resolve => { finishOld = resolve })
    transport.get.mockImplementation(async (url: string) => {
      if (url === '/lang/manifest') {
        if (delayed) { delayed = false; await hold }
        return { data: useCorrupted ? second.manifest : first.manifest }
      }
      const code = url.split('/').at(-1)!
      if (url.includes('vfixture-second')) return { data: bytes(first.content[code]!) }
      return { data: bytes(first.content[code]!) }
    })
    const { default: i18n, initI18n, changeLang } = await import('../index')
    await initI18n()
    useCorrupted = true
    delayed = true
    const old = changeLang('en')
    await changeLang('ko')
    finishOld()
    await old
    expect(i18n.global.locale.value).toBe('ko')
    expect(i18n.global.t('lang.fixture')).toBe('确认版 ko')
  })
})
