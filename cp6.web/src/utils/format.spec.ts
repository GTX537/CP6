// @vitest-environment jsdom
import { afterEach, describe, expect, it } from 'vitest'
import i18n from '@/i18n'
import { formatDateTime, formatDateTimeCell } from './format'

const localeRef = i18n.global.locale as unknown as { value: string }
const initialLocale = localeRef.value

afterEach(() => {
  localeRef.value = initialLocale
})

describe('datetime display contract', () => {
  it.each(['zh-CN', 'zh-TW', 'en', 'ja', 'ko'])('%s keeps normal business UI at minute precision', (locale) => {
    localeRef.value = locale
    const value = new Date(2026, 3, 8, 22, 6, 21, 179)
    const expected = new Intl.DateTimeFormat(locale, {
      year: 'numeric',
      month: locale === 'en' ? 'short' : '2-digit',
      day: locale === 'en' ? 'numeric' : '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    }).format(value)

    expect(formatDateTime(value)).toBe(expected)
    expect(formatDateTime(value)).not.toContain('179')
  })

  it('normalizes high-precision .NET input and preserves empty/invalid cells', () => {
    localeRef.value = 'zh-CN'

    expect(formatDateTime('2026-04-08T22:06:21.1795134')).not.toContain('.179')
    expect(formatDateTimeCell({}, {}, null)).toBe('')
    expect(formatDateTime('not-a-date')).toBe('')
  })
})
