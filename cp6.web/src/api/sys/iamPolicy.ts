import http from '../http'
import type { IamOperation, IamScope } from './iam'
export type IamPolicyMode = 'actions' | 'scopes' | 'fields'
export interface PolicyAction { menuId: number; actionCode: string }
export interface PolicyScope { resourceKey: string; scopeType: number; customDeptIds: string[] }
export interface PolicyField { resourceKey: string; fieldName: string; access: number }
export interface IamPolicyEditor extends IamScope {
  mode: IamPolicyMode; roleId: number | null; version: string; referencesVersion: string
  roles: { roleId: number; roleName: string; enable: boolean }[]
  catalog: {
    version: string
    menus: { menuId: number; menuName: string; menuKey: string | null; parentId: number | null; enable: boolean }[]
    actions: (PolicyAction & { actionName: string; sort: number })[]
    scopes: { key: string; name: string; supports: number[]; default: number }[]
    fields: { key: string; fields: { name: string; label: string }[] }[]
    departments: { id: string; deptName: string; available: boolean }[]
  }
  function: { menuIds: number[]; actions: PolicyAction[] }; dataScopes: PolicyScope[]; fieldPerms: PolicyField[]; warnings: string[]
}
export interface IamPolicyInput {
  expectedVersion: string; expectedReferencesVersion: string; expectedCatalogVersion: string; reason: string
  menuIds?: number[]; actions?: PolicyAction[]; scopes?: PolicyScope[]; resourceKey?: string; fields?: PolicyField[]
}
export const iamPolicyApi = {
  async read(mode: IamPolicyMode, roleId?: number): Promise<IamPolicyEditor> {
    return await http.get(`/iam/role-policies/${mode}/editor`, { params: { roleId } }) as unknown as IamPolicyEditor
  },
  async save(mode: IamPolicyMode, roleId: number, input: IamPolicyInput, operationId: string): Promise<IamOperation> {
    return await http.put(`/iam/role-policies/${mode}/${roleId}`, { ...input, operationId }) as unknown as IamOperation
  }
}
