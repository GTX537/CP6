import http from '../http'
import type {
  Envelope,
  CodeRuleVO,
  CodeSegmentDef,
  CodePreviewResp,
  CodePrecheckResp,
} from '@/types/space/scene'

// GET /space/code-rule 直出实体：segments 是 JSON 字符串（camelCase 序列化后键为小写
// `segments`），其余字段亦为 camelCase。此处容错 PascalCase 键与非法串。
type RawRuleEntity = {
  id?: string
  Id?: string
  ruleName?: string
  RuleName?: string
  scopeType?: number
  ScopeType?: number
  scopeId?: string | null
  ScopeId?: string | null
  isDefault?: boolean
  IsDefault?: boolean
  segments?: string
  Segments?: string
  [k: string]: unknown
}

/**
 * 实体 → VO 归一：segments 字符串 JSON.parse 为数组；空/"[]"/非法串/非数组 一律 []（不抛）。
 * 兼容后端 camelCase（小写 segments）与偶发 PascalCase 键。
 */
export function parseRuleEntity(raw: RawRuleEntity): CodeRuleVO {
  const rawSeg: unknown = raw.segments ?? raw.Segments
  let segments: CodeSegmentDef[] = []
  if (Array.isArray(rawSeg)) {
    segments = rawSeg as CodeSegmentDef[]
  } else if (typeof rawSeg === 'string' && rawSeg.trim()) {
    try {
      const parsed = JSON.parse(rawSeg)
      if (Array.isArray(parsed)) segments = parsed as CodeSegmentDef[]
    } catch {
      segments = []
    }
  }
  return {
    id: (raw.id ?? raw.Id) as string | undefined,
    ruleName: (raw.ruleName ?? raw.RuleName ?? '') as string,
    scopeType: (raw.scopeType ?? raw.ScopeType ?? 0) as number,
    scopeId: (raw.scopeId ?? raw.ScopeId ?? null) as string | null,
    segments,
    isDefault: (raw.isDefault ?? raw.IsDefault ?? false) as boolean,
  }
}

export const codeRuleApi = {
  // GET 直出实体列表 → 内部 parseRuleEntity 归一为 CodeRuleVO[]（segments 恒为数组）
  async list(): Promise<Envelope<CodeRuleVO[]>> {
    const res = await http.get<unknown, Envelope<RawRuleEntity[]>>('/space/code-rule')
    return { ...res, data: (res.data ?? []).map(parseRuleEntity) }
  },
  // 提交 CodeRuleDto：segments 为数组直发（服务端序列化为 JSON 存库）
  create(d: CodeRuleVO) {
    return http.post<unknown, Envelope<{ id: string }>>('/space/code-rule', d)
  },
  update(id: string, d: CodeRuleVO) {
    return http.put<unknown, Envelope<unknown>>(
      `/space/code-rule/${encodeURIComponent(id)}`,
      d,
    )
  },
  remove(id: string) {
    return http.delete<unknown, Envelope<unknown>>(`/space/code-rule/${encodeURIComponent(id)}`)
  },
  // 只发 segments，其余 CodePreviewReq 字段服务端不读
  preview(segments: CodeSegmentDef[]) {
    return http.post<unknown, Envelope<CodePreviewResp>>('/space/code-rule/preview', { segments })
  },
  // 批量生成库位编码；返回本次生成的编码列表
  generate(floorId: string, mode: 'fill-empty' | 'rebuild', scopeZoneId?: string) {
    return http.post<unknown, Envelope<string[]>>(
      `/space/floor/${encodeURIComponent(floorId)}/generate-codes`,
      { mode, scopeZoneId },
    )
  },
  // 发布前编码预检（zoneId 可空 = 整层）
  precheck(floorId: string, zoneId?: string) {
    return http.get<unknown, Envelope<CodePrecheckResp>>(
      `/space/floor/${encodeURIComponent(floorId)}/code-precheck`,
      { params: { zoneId } },
    )
  },
  // 单格生成
  genSingle(locationId: string) {
    return http.post<unknown, Envelope<{ code: string }>>(
      `/space/location/${encodeURIComponent(locationId)}/gen-code`,
    )
  },
}
