import Konva from 'konva'

export const ACTIVE_ROTATE_HANDLE_STYLE = {
  anchorSize: 18,
  anchorCornerRadius: 99,
  anchorFill: '#10bfc8',
  anchorStroke: '#ffffff',
  anchorStrokeWidth: 3,
  anchorShadowColor: '#075f65',
  anchorShadowBlur: 3,
  anchorShadowOpacity: 0.9,
  borderStroke: '#087d84',
  borderStrokeWidth: 2,
  rotateAnchorOffset: 42,
} as const

export const INACTIVE_ROTATE_HANDLE_STYLE = {
  anchorSize: 10,
  anchorCornerRadius: 0,
  anchorFill: '#ffffff',
  anchorStroke: '#0099ff',
  anchorStrokeWidth: 1,
  anchorShadowColor: 'transparent',
  anchorShadowBlur: 0,
  anchorShadowOpacity: 0,
  borderStroke: '#0099ff',
  borderStrokeWidth: 1.5,
  rotateAnchorOffset: 50,
} as const

export function setRotateHandleVisibility(transformer: Konva.Transformer, active: boolean): void {
  transformer.setAttrs(active ? ACTIVE_ROTATE_HANDLE_STYLE : INACTIVE_ROTATE_HANDLE_STYLE)
}
