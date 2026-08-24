import { describe, expect, it } from 'vitest'
import { getLoginExperienceCopy } from './loginExperience'

describe('login experience copy', () => {
  it.each(['zh-CN', 'zh-TW', 'en', 'ja', 'ko'])('provides complete %s content', locale => {
    const copy = getLoginExperienceCopy(locale)

    expect(copy.heroLine).toBeTruthy()
    expect(copy.heroAccent).toBeTruthy()
    expect(copy.flowNodes).toHaveLength(5)
    expect(copy.foundations).toHaveLength(3)
    expect(copy.capabilities).toHaveLength(4)
    expect(copy.securityItems).toHaveLength(3)
  })

  it('uses English for pseudo and unknown locales', () => {
    const english = getLoginExperienceCopy('en')

    expect(getLoginExperienceCopy('pseudo')).toBe(english)
    expect(getLoginExperienceCopy('fr')).toBe(english)
  })
})
