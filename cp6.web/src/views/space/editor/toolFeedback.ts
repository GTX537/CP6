import type { ToolType } from '@/space-editor/interact/InteractionManager'

export const EDITOR_EXPORT_SUCCESS_KEY = 'space.editor.export.success'

export const EDITOR_MESSAGE_KEYS = [
  'space.editor.tool.select.title',
  'space.editor.tool.select.message',
  'space.editor.tool.drag.title',
  'space.editor.tool.drag.message',
  'space.editor.tool.marker.title',
  'space.editor.tool.marker.message',
  'space.editor.tool.zone.title',
  'space.editor.tool.zone.message',
  'space.editor.tool.rotate.title',
  'space.editor.tool.rotate.selectFirst',
  'space.editor.tool.rotate.message',
  'space.editor.viewport.controls',
  'space.editor.viewport.zoomOut',
  'space.editor.viewport.zoomIn',
  'space.editor.viewport.fitAllLabel',
  'space.editor.viewport.fitAll',
  'space.editor.viewport.reset',
  EDITOR_EXPORT_SUCCESS_KEY,
] as const

export type EditorMessageKey = typeof EDITOR_MESSAGE_KEYS[number]
type EditorLocale = 'ja' | 'zh-CN' | 'zh-TW' | 'en' | 'ko'

const editorMessageFallbacks: Record<EditorLocale, Record<EditorMessageKey, string>> = {
  ja: {
    'space.editor.tool.select.title': '選択モード',
    'space.editor.tool.select.message': 'クリックしてラックを選択します。空白領域をドラッグすると範囲選択できます',
    'space.editor.tool.drag.title': 'ドラッグモード',
    'space.editor.tool.drag.message': 'キャンバスをドラッグして表示を移動します。ラックをドラッグするとラックを移動できます',
    'space.editor.tool.marker.title': 'マーカーモード',
    'space.editor.tool.marker.message': 'キャンバスをクリックしてマーカーを追加します。「元に戻す」で取り消せます',
    'space.editor.tool.zone.title': '保管エリア作成',
    'space.editor.tool.zone.message': 'キャンバス上で矩形範囲をドラッグし、保管エリア情報を入力します',
    'space.editor.tool.rotate.title': '回転モード',
    'space.editor.tool.rotate.selectFirst': '先にラックをクリックしてから、ハイライトされた丸いハンドルをドラッグします',
    'space.editor.tool.rotate.message': 'ハイライトされた丸いハンドルをドラッグして回転します。Ctrl キーを押すと 15° スナップが解除されます',
    'space.editor.viewport.controls': '表示操作',
    'space.editor.viewport.zoomOut': '縮小',
    'space.editor.viewport.zoomIn': '拡大',
    'space.editor.viewport.fitAllLabel': 'すべての内容を表示',
    'space.editor.viewport.fitAll': '全体表示',
    'space.editor.viewport.reset': '表示をリセット',
    'space.editor.export.success': 'エクスポートしました',
  },
  'zh-CN': {
    'space.editor.tool.select.title': '选择模式',
    'space.editor.tool.select.message': '单击选择货架；拖动空白区域可框选',
    'space.editor.tool.drag.title': '拖拽模式',
    'space.editor.tool.drag.message': '拖动画布可平移视角；拖动货架可移动货架',
    'space.editor.tool.marker.title': '打点模式',
    'space.editor.tool.marker.message': '单击画布添加标注点，可使用撤销取消',
    'space.editor.tool.zone.title': '新建库区',
    'space.editor.tool.zone.message': '在画布上拖出矩形范围，然后填写库区信息',
    'space.editor.tool.rotate.title': '旋转模式',
    'space.editor.tool.rotate.selectFirst': '先单击一个货架，再拖动高亮圆形手柄',
    'space.editor.tool.rotate.message': '拖动高亮圆形手柄旋转；按住 Ctrl 可关闭 15° 吸附',
    'space.editor.viewport.controls': '视图控制',
    'space.editor.viewport.zoomOut': '缩小视图',
    'space.editor.viewport.zoomIn': '放大视图',
    'space.editor.viewport.fitAllLabel': '适配全部内容',
    'space.editor.viewport.fitAll': '适配全部',
    'space.editor.viewport.reset': '复位视图',
    'space.editor.export.success': '导出成功',
  },
  'zh-TW': {
    'space.editor.tool.select.title': '選擇模式',
    'space.editor.tool.select.message': '按一下選取貨架；拖曳空白區域可框選',
    'space.editor.tool.drag.title': '拖曳模式',
    'space.editor.tool.drag.message': '拖曳畫布可平移視角；拖曳貨架可移動貨架',
    'space.editor.tool.marker.title': '打點模式',
    'space.editor.tool.marker.message': '按一下畫布新增標記點，可使用復原取消',
    'space.editor.tool.zone.title': '新增庫區',
    'space.editor.tool.zone.message': '在畫布上拖出矩形範圍，然後填寫庫區資訊',
    'space.editor.tool.rotate.title': '旋轉模式',
    'space.editor.tool.rotate.selectFirst': '先按一下貨架，再拖曳醒目的圓形控制點',
    'space.editor.tool.rotate.message': '拖曳醒目的圓形控制點進行旋轉；按住 Ctrl 可關閉 15° 吸附',
    'space.editor.viewport.controls': '檢視控制',
    'space.editor.viewport.zoomOut': '縮小檢視',
    'space.editor.viewport.zoomIn': '放大檢視',
    'space.editor.viewport.fitAllLabel': '顯示全部內容',
    'space.editor.viewport.fitAll': '顯示全部',
    'space.editor.viewport.reset': '重設檢視',
    'space.editor.export.success': '匯出成功',
  },
  en: {
    'space.editor.tool.select.title': 'Select mode',
    'space.editor.tool.select.message': 'Click a rack to select it; drag an empty area to select a group',
    'space.editor.tool.drag.title': 'Drag mode',
    'space.editor.tool.drag.message': 'Drag the canvas to pan the view; drag a rack to move it',
    'space.editor.tool.marker.title': 'Marker mode',
    'space.editor.tool.marker.message': 'Click the canvas to add a marker; use Undo to remove it',
    'space.editor.tool.zone.title': 'Create zone',
    'space.editor.tool.zone.message': 'Drag a rectangle on the canvas, then enter the zone details',
    'space.editor.tool.rotate.title': 'Rotate mode',
    'space.editor.tool.rotate.selectFirst': 'Click a rack first, then drag the highlighted circular handle',
    'space.editor.tool.rotate.message': 'Drag the highlighted circular handle to rotate; hold Ctrl to disable 15° snapping',
    'space.editor.viewport.controls': 'View controls',
    'space.editor.viewport.zoomOut': 'Zoom out',
    'space.editor.viewport.zoomIn': 'Zoom in',
    'space.editor.viewport.fitAllLabel': 'Fit all content',
    'space.editor.viewport.fitAll': 'Fit all',
    'space.editor.viewport.reset': 'Reset view',
    'space.editor.export.success': 'Export complete',
  },
  ko: {
    'space.editor.tool.select.title': '선택 모드',
    'space.editor.tool.select.message': '랙을 클릭하여 선택합니다. 빈 영역을 드래그하면 범위 선택할 수 있습니다',
    'space.editor.tool.drag.title': '드래그 모드',
    'space.editor.tool.drag.message': '캔버스를 드래그하여 화면을 이동합니다. 랙을 드래그하면 랙을 이동할 수 있습니다',
    'space.editor.tool.marker.title': '마커 모드',
    'space.editor.tool.marker.message': '캔버스를 클릭하여 마커를 추가합니다. 실행 취소로 제거할 수 있습니다',
    'space.editor.tool.zone.title': '구역 만들기',
    'space.editor.tool.zone.message': '캔버스에서 사각형 영역을 드래그한 다음 구역 정보를 입력합니다',
    'space.editor.tool.rotate.title': '회전 모드',
    'space.editor.tool.rotate.selectFirst': '먼저 랙을 클릭한 다음 강조된 원형 핸들을 드래그합니다',
    'space.editor.tool.rotate.message': '강조된 원형 핸들을 드래그하여 회전합니다. Ctrl을 누르면 15° 스냅이 해제됩니다',
    'space.editor.viewport.controls': '보기 제어',
    'space.editor.viewport.zoomOut': '축소',
    'space.editor.viewport.zoomIn': '확대',
    'space.editor.viewport.fitAllLabel': '모든 콘텐츠 맞춤',
    'space.editor.viewport.fitAll': '전체 맞춤',
    'space.editor.viewport.reset': '보기 초기화',
    'space.editor.export.success': '내보내기가 완료되었습니다',
  },
}

function normalizeEditorLocale(locale: string): EditorLocale {
  const normalized = locale.toLowerCase()
  if (normalized.startsWith('zh-tw') || normalized.startsWith('zh-hant')) return 'zh-TW'
  if (normalized.startsWith('zh')) return 'zh-CN'
  if (normalized.startsWith('en')) return 'en'
  if (normalized.startsWith('ko')) return 'ko'
  return 'ja'
}

export function getEditorMessageFallback(locale: string, key: EditorMessageKey): string {
  return editorMessageFallbacks[normalizeEditorLocale(locale)][key] ?? key
}

export interface EditorToolFeedback {
  titleKey: EditorMessageKey
  messageKey: EditorMessageKey
  cursorClass: 'tool-cursor-select' | 'tool-cursor-drag' | 'tool-cursor-crosshair'
}

const selectFeedback: EditorToolFeedback = {
  titleKey: 'space.editor.tool.select.title',
  messageKey: 'space.editor.tool.select.message',
  cursorClass: 'tool-cursor-select',
}

export function getEditorToolFeedback(tool: ToolType, hasSelectedRack: boolean): EditorToolFeedback {
  switch (tool) {
    case 'drag':
      return {
        titleKey: 'space.editor.tool.drag.title',
        messageKey: 'space.editor.tool.drag.message',
        cursorClass: 'tool-cursor-drag',
      }
    case 'marker':
      return {
        titleKey: 'space.editor.tool.marker.title',
        messageKey: 'space.editor.tool.marker.message',
        cursorClass: 'tool-cursor-crosshair',
      }
    case 'zone':
      return {
        titleKey: 'space.editor.tool.zone.title',
        messageKey: 'space.editor.tool.zone.message',
        cursorClass: 'tool-cursor-crosshair',
      }
    case 'rotate':
      return {
        titleKey: 'space.editor.tool.rotate.title',
        messageKey: hasSelectedRack
          ? 'space.editor.tool.rotate.message'
          : 'space.editor.tool.rotate.selectFirst',
        cursorClass: 'tool-cursor-crosshair',
      }
    case 'select':
    default:
      return { ...selectFeedback }
  }
}
