import http from '../http'
import type {
  IApplySpaceWarehouseTemplateFloorRequest,
  IApplySpaceWarehouseTemplateFloorResponse,
  ICreateSpaceFloorRequest,
  ICreateSpaceFloorResponse,
  ICreateSpaceVersionRequest,
  ICreateSpaceVersionResponse,
  ICreateTenantSpaceWarehouseTemplateFromDraftRequest,
  ICreateTenantSpaceWarehouseTemplateRequest,
  ICreateTenantSpaceWarehouseTemplateResponse,
  ISpaceModelDto,
  ISpaceDraftWarehouseTemplatePreviewDto,
  ISpaceSceneFloorDto,
  ISpaceVersionDto,
  ISpaceWarehouseTemplateDto,
  ISpaceWarehouseTemplateInstantiationPreviewDto,
} from '../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

const root = '/space/design/v1'

export const designProjectApi = {
  getModel(siteId: string) {
    return http.get<unknown, ISpaceModelDto>(
      `${root}/sites/${encodeURIComponent(siteId)}/model`,
    )
  },

  getVersion(versionId: string) {
    return http.get<unknown, ISpaceVersionDto>(
      `${root}/versions/${encodeURIComponent(versionId)}`,
    )
  },

  getFloors(versionId: string) {
    return http.get<unknown, ISpaceSceneFloorDto[]>(
      `${root}/versions/${encodeURIComponent(versionId)}/floors`,
    )
  },

  createBlankVersion(
    siteId: string,
    name: string,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return this.createVersion(
      siteId,
      {
        name,
        basedOnVersionId: undefined,
        createMode: 'Blank',
      },
      idempotencyKey,
    )
  },

  createVersion(
    siteId: string,
    request: ICreateSpaceVersionRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ICreateSpaceVersionResponse>(
      `${root}/sites/${encodeURIComponent(siteId)}/versions`,
      request,
      {
        headers: { 'Idempotency-Key': idempotencyKey },
      },
    )
  },

  createFloor(
    versionId: string,
    request: ICreateSpaceFloorRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ICreateSpaceFloorResponse>(
      `${root}/versions/${encodeURIComponent(versionId)}/floors`,
      request,
      {
        headers: { 'Idempotency-Key': idempotencyKey },
      },
    )
  },

  getWarehouseTemplates(scope?: 'System' | 'Tenant') {
    return http.get<unknown, ISpaceWarehouseTemplateDto[]>(
      `${root}/templates`,
      scope ? { params: { scope } } : undefined,
    )
  },

  createTenantWarehouseTemplate(
    request: ICreateTenantSpaceWarehouseTemplateRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ICreateTenantSpaceWarehouseTemplateResponse>(
      `${root}/templates`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  previewTenantWarehouseTemplateFromDraft(
    versionId: string,
    expectedContentRevision: number,
  ) {
    return http.post<unknown, ISpaceDraftWarehouseTemplatePreviewDto>(
      `${root}/versions/${encodeURIComponent(versionId)}/tenant-template-preview`,
      { expectedContentRevision },
    )
  },

  createTenantWarehouseTemplateFromDraft(
    versionId: string,
    request: ICreateTenantSpaceWarehouseTemplateFromDraftRequest,
    idempotencyKey: string = crypto.randomUUID(),
  ) {
    return http.post<unknown, ICreateTenantSpaceWarehouseTemplateResponse>(
      `${root}/versions/${encodeURIComponent(versionId)}/tenant-templates`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    )
  },

  previewWarehouseTemplate(templateId: string, templateVersionId: string) {
    return http.post<unknown, ISpaceWarehouseTemplateInstantiationPreviewDto>(
      `${root}/templates/${encodeURIComponent(templateId)}/instantiate`,
      { templateVersionId },
    )
  },

  applyWarehouseTemplateFloor(
    versionId: string,
    floorLogicalId: string,
    templateId: string,
    request: IApplySpaceWarehouseTemplateFloorRequest,
  ) {
    return http.post<unknown, IApplySpaceWarehouseTemplateFloorResponse>(
      `${root}/versions/${encodeURIComponent(versionId)}` +
        `/floors/${encodeURIComponent(floorLogicalId)}` +
        `/templates/${encodeURIComponent(templateId)}:apply`,
      request,
    )
  },
}
