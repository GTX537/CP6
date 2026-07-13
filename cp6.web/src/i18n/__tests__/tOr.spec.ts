// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { createI18n } from 'vue-i18n'
import { tOr } from '../tOr'

// flatJson:true 匹配生产 i18n；global composer 暴露 te/t，作为 I18nLike 直接注入纯函数
function composer() {
  const i18n = createI18n({
    legacy: false,
    locale: 'ja',
    flatJson: true,
    missingWarn: false,
    fallbackWarn: false,
    messages: { ja: { 'e.space.307': '発行前チェックに失敗しました' } },
  })
  return i18n.global as unknown as { te: (k: string) => boolean; t: (k: string) => string }
}

describe('tOr', () => {
  it('已注册 key → 返回译文', () => {
    expect(tOr(composer(), 'e.space.307')).toBe('発行前チェックに失敗しました')
  })

  it('未注册 key + fallback → 返回 fallback', () => {
    expect(tOr(composer(), 'e.space.999', 'boom')).toBe('boom')
  })

  it('未注册 key 且无 fallback → 返回 key 本身', () => {
    expect(tOr(composer(), 'E-SPACE-999-RAW')).toBe('E-SPACE-999-RAW')
  })
})
