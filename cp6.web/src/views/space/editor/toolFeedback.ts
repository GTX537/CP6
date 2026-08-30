import type { ToolType } from '@/space-editor/interact/InteractionManager'

export interface EditorToolFeedback {
  titleKey: string
  messageKey: string
  cursorClass: 'tool-cursor-select' | 'tool-cursor-drag' | 'tool-cursor-crosshair'
}

const selectFeedback: EditorToolFeedback = {
  titleKey: '选择模式',
  messageKey: '单击选择货架；拖动空白区域可框选',
  cursorClass: 'tool-cursor-select',
}

export function getEditorToolFeedback(tool: ToolType, hasSelectedRack: boolean): EditorToolFeedback {
  switch (tool) {
    case 'drag':
      return {
        titleKey: '拖拽模式',
        messageKey: '拖动画布可平移视角；拖动货架可移动货架',
        cursorClass: 'tool-cursor-drag',
      }
    case 'marker':
      return {
        titleKey: '打点模式',
        messageKey: '单击画布添加标注点，可使用撤销取消',
        cursorClass: 'tool-cursor-crosshair',
      }
    case 'zone':
      return {
        titleKey: '新建库区',
        messageKey: '在画布上拖出矩形范围，然后填写库区信息',
        cursorClass: 'tool-cursor-crosshair',
      }
    case 'rotate':
      return {
        titleKey: '旋转模式',
        messageKey: hasSelectedRack
          ? '拖动高亮圆形手柄旋转；按住 Ctrl 可关闭 15° 吸附'
          : '先单击一个货架，再拖动高亮圆形手柄',
        cursorClass: 'tool-cursor-crosshair',
      }
    case 'select':
    default:
      return { ...selectFeedback }
  }
}
