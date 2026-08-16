import http from '@/api/http'

const root = '/space/design/v1'

export interface SpaceDesignSource {
  id: string
  modelVersionId: string
  sourceType: string
  fileId?: string | null
  displayName: string
  sha256: string
  state: string
  rowVersion: string
}

export interface SpaceSourceRemovalReference {
  code: string
  count: number
  blocksRemoval: boolean
}

export interface SpaceSourceRemovalPreview {
  sourceId: string
  fileId?: string | null
  displayName: string
  sourceType: string
  state: string
  versionContentRevision: number
  sourceRowVersion: string
  canRemove: boolean
  physicalFileRetained: boolean
  references: SpaceSourceRemovalReference[]
}

export interface RemoveSpaceSourceResponse {
  sourceId: string
  versionContentRevision: number
  physicalFileRetained: boolean
  idempotentReplay: boolean
}

export const designSourcesApi = {
  list(versionId: string, limit = 200) {
    return http.get<unknown, { items: SpaceDesignSource[]; nextCursor?: string }>(
      `${root}/versions/${versionId}/sources`,
      { params: { limit } },
    )
  },

  getRemovalPreview(versionId: string, sourceId: string) {
    return http.get<unknown, SpaceSourceRemovalPreview>(
      `${root}/versions/${versionId}/sources/${sourceId}/removal-preview`,
    )
  },

  remove(
    versionId: string,
    sourceId: string,
    request: {
      expectedContentRevision: number
      expectedSourceRowVersion: string
    },
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, RemoveSpaceSourceResponse>(
      `${root}/versions/${versionId}/sources/${sourceId}:remove`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },
}
