import { describe, expect, expectTypeOf, it } from 'vitest'
import {
  EDITOR_MESSAGE_KEYS,
  getEditorMessageFallback,
  getEditorToolFeedback,
  type EditorMessageKey,
  type EditorToolFeedback,
} from './toolFeedback'

const viewportMessageKeys = {
  controls: 'space.editor.viewport.controls',
  zoomOut: 'space.editor.viewport.zoomOut',
  zoomIn: 'space.editor.viewport.zoomIn',
  fitAllLabel: 'space.editor.viewport.fitAllLabel',
  fitAll: 'space.editor.viewport.fitAll',
  reset: 'space.editor.viewport.reset',
} as const

describe('getEditorToolFeedback', () => {
  it.each([
    ['select', 'space.editor.tool.select.title', 'space.editor.tool.select.message', 'tool-cursor-select'],
    ['drag', 'space.editor.tool.drag.title', 'space.editor.tool.drag.message', 'tool-cursor-drag'],
    ['marker', 'space.editor.tool.marker.title', 'space.editor.tool.marker.message', 'tool-cursor-crosshair'],
    ['zone', 'space.editor.tool.zone.title', 'space.editor.tool.zone.message', 'tool-cursor-crosshair'],
  ] as const)('maps %s to persistent guidance', (tool, titleKey, messageKey, cursorClass) => {
    expect(getEditorToolFeedback(tool, false)).toEqual({ titleKey, messageKey, cursorClass })
  })

  it('guides the rotate tool to select a rack first', () => {
    expect(getEditorToolFeedback('rotate', false)).toEqual({
      titleKey: 'space.editor.tool.rotate.title',
      messageKey: 'space.editor.tool.rotate.selectFirst',
      cursorClass: 'tool-cursor-crosshair',
    })
  })

  it('guides the rotate tool to rotate the selected rack', () => {
    expect(getEditorToolFeedback('rotate', true)).toEqual({
      titleKey: 'space.editor.tool.rotate.title',
      messageKey: 'space.editor.tool.rotate.message',
      cursorClass: 'tool-cursor-crosshair',
    })
  })

  it('falls back to select guidance for an unknown runtime value', () => {
    expect(getEditorToolFeedback('unknown' as never, false)).toEqual({
      titleKey: 'space.editor.tool.select.title',
      messageKey: 'space.editor.tool.select.message',
      cursorClass: 'tool-cursor-select',
    })
  })

  it('returns fresh select guidance objects for each call', () => {
    const first = getEditorToolFeedback('select', false)
    first.cursorClass = 'tool-cursor-drag'

    expect(getEditorToolFeedback('select', false)).toEqual({
      titleKey: 'space.editor.tool.select.title',
      messageKey: 'space.editor.tool.select.message',
      cursorClass: 'tool-cursor-select',
    })
    expect(getEditorToolFeedback('unknown' as never, false)).toEqual({
      titleKey: 'space.editor.tool.select.title',
      messageKey: 'space.editor.tool.select.message',
      cursorClass: 'tool-cursor-select',
    })
  })

  it.each(['ja', 'zh-CN', 'zh-TW', 'en', 'ko'] as const)(
    'provides a %s fallback for every dynamic editor message key',
    (locale) => {
      for (const key of EDITOR_MESSAGE_KEYS) {
        expect(getEditorMessageFallback(locale, key), `${locale}:${key}`).toBeTruthy()
        expect(getEditorMessageFallback(locale, key), `${locale}:${key}`).not.toBe(key)
      }
    },
  )

  it('keeps the Japanese and Simplified Chinese fallbacks locale-specific', () => {
    expect(getEditorMessageFallback('ja', 'space.editor.tool.select.title')).toBe('選択モード')
    expect(getEditorMessageFallback('ja', 'space.editor.tool.select.message')).toContain('ラック')
    expect(getEditorMessageFallback('ja', 'space.editor.export.success')).toBe('エクスポートしました')
    expect(getEditorMessageFallback('zh-CN', 'space.editor.tool.select.title')).toBe('选择模式')
    expect(getEditorMessageFallback('zh-CN', 'space.editor.tool.select.message')).toContain('货架')
    expect(getEditorMessageFallback('zh-CN', 'space.editor.export.success')).toBe('导出成功')
  })

  it('exports stable viewport message keys', () => {
    expect(EDITOR_MESSAGE_KEYS).toEqual(expect.arrayContaining(Object.values(viewportMessageKeys)))
  })

  it.each([
    ['ja', ['表示操作', '縮小', '拡大', 'すべての内容を表示', '全体表示', '表示をリセット']],
    ['zh-CN', ['视图控制', '缩小视图', '放大视图', '适配全部内容', '适配全部', '复位视图']],
    ['zh-TW', ['檢視控制', '縮小檢視', '放大檢視', '顯示全部內容', '顯示全部', '重設檢視']],
    ['en', ['View controls', 'Zoom out', 'Zoom in', 'Fit all content', 'Fit all', 'Reset view']],
    ['ko', ['보기 제어', '축소', '확대', '모든 콘텐츠 맞춤', '전체 맞춤', '보기 초기화']],
  ] as const)('provides localized viewport labels for %s', (locale, expected) => {
    const actual = Object.values(viewportMessageKeys).map(key => (
      getEditorMessageFallback(locale, key as EditorMessageKey)
    ))
    expect(actual).toEqual(expected)
  })

  it('constrains dynamic feedback messages to the exported key union', () => {
    expectTypeOf(EDITOR_MESSAGE_KEYS).toMatchTypeOf<readonly EditorMessageKey[]>()
    expectTypeOf<EditorToolFeedback['titleKey']>().toEqualTypeOf<EditorMessageKey>()
    expectTypeOf<EditorToolFeedback['messageKey']>().toEqualTypeOf<EditorMessageKey>()
    expectTypeOf<Parameters<typeof getEditorMessageFallback>[1]>().toEqualTypeOf<EditorMessageKey>()
  })
})
