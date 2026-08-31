import Konva from 'konva'

export const ACTIVE_ROTATE_HANDLE_STYLE = {
  anchorSize: 18,
  anchorCornerRadius: 99,
  anchorFill: '#10bfc8',
  anchorStroke: '#ffffff',
  anchorStrokeWidth: 3,
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
  borderStroke: '#0099ff',
  borderStrokeWidth: 1.5,
  rotateAnchorOffset: 50,
} as const

const ACTIVE_ROTATE_HANDLE_SHADOW_STYLE = {
  shadowColor: '#075f65',
  shadowBlur: 3,
  shadowOpacity: 0.9,
}

const INACTIVE_ROTATE_HANDLE_SHADOW_STYLE = {
  shadowColor: 'transparent',
  shadowBlur: 0,
  shadowOpacity: 0,
}

export function applyRotateHandleStyle(transformer: Konva.Transformer, active: boolean): void {
  transformer.setAttrs(active ? ACTIVE_ROTATE_HANDLE_STYLE : INACTIVE_ROTATE_HANDLE_STYLE)
  transformer.findOne('.rotater')?.setAttrs(
    active ? ACTIVE_ROTATE_HANDLE_SHADOW_STYLE : INACTIVE_ROTATE_HANDLE_SHADOW_STYLE,
  )
}
