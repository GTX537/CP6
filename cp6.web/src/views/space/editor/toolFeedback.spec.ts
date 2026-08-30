import { describe, expect, it } from 'vitest'
import { getEditorToolFeedback } from './toolFeedback'

describe('getEditorToolFeedback', () => {
  it.each([
    ['select', '选择模式', '单击选择货架；拖动空白区域可框选', 'tool-cursor-select'],
    ['drag', '拖拽模式', '拖动画布可平移视角；拖动货架可移动货架', 'tool-cursor-drag'],
    ['marker', '打点模式', '单击画布添加标注点，可使用撤销取消', 'tool-cursor-crosshair'],
    ['zone', '新建库区', '在画布上拖出矩形范围，然后填写库区信息', 'tool-cursor-crosshair'],
  ] as const)('maps %s to persistent guidance', (tool, titleKey, messageKey, cursorClass) => {
    expect(getEditorToolFeedback(tool, false)).toEqual({ titleKey, messageKey, cursorClass })
  })

  it('guides the rotate tool to select a rack first', () => {
    expect(getEditorToolFeedback('rotate', false)).toEqual({
      titleKey: '旋转模式',
      messageKey: '先单击一个货架，再拖动高亮圆形手柄',
      cursorClass: 'tool-cursor-crosshair',
    })
  })

  it('guides the rotate tool to rotate the selected rack', () => {
    expect(getEditorToolFeedback('rotate', true)).toEqual({
      titleKey: '旋转模式',
      messageKey: '拖动高亮圆形手柄旋转；按住 Ctrl 可关闭 15° 吸附',
      cursorClass: 'tool-cursor-crosshair',
    })
  })

  it('falls back to select guidance for an unknown runtime value', () => {
    expect(getEditorToolFeedback('unknown' as never, false)).toEqual({
      titleKey: '选择模式',
      messageKey: '单击选择货架；拖动空白区域可框选',
      cursorClass: 'tool-cursor-select',
    })
  })

  it('returns fresh select guidance objects for each call', () => {
    const first = getEditorToolFeedback('select', false)
    first.titleKey = '被调用方修改'
    first.messageKey = '被调用方修改'

    expect(getEditorToolFeedback('select', false)).toEqual({
      titleKey: '选择模式',
      messageKey: '单击选择货架；拖动空白区域可框选',
      cursorClass: 'tool-cursor-select',
    })
    expect(getEditorToolFeedback('unknown' as never, false)).toEqual({
      titleKey: '选择模式',
      messageKey: '单击选择货架；拖动空白区域可框选',
      cursorClass: 'tool-cursor-select',
    })
  })
})
