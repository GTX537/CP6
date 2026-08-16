import { describe, expect, it } from 'vitest'
import { uploadReuseNotice } from './uploadReuseNotice'

describe('uploadReuseNotice', () => {
  it('returns no notice for a newly stored upload', () => {
    expect(uploadReuseNotice('CAD', false)).toBeNull()
    expect(uploadReuseNotice('底图', undefined)).toBeNull()
  })

  it('explains that duplicate CAD and underlay content reused server authority', () => {
    expect(uploadReuseNotice('CAD', true)).toBe(
      '检测到重复CAD内容，已按 SHA-256 复用受控文件或当前来源，不会重复保存原文件。',
    )
    expect(uploadReuseNotice('底图', true)).toContain('重复底图内容')
  })
})
