export type SpaceUploadSourceKind = 'CAD' | 'Excel' | '底图'

export function uploadReuseNotice(
  sourceKind: SpaceUploadSourceKind,
  reused: boolean | undefined,
): string | null {
  if (!reused) return null

  return `检测到重复${sourceKind}内容，已按 SHA-256 复用受控文件或当前来源，不会重复保存原文件。`
}
