export interface GrantUser { userId: string; userName: string }
export interface MyGrants { iCanActAs: GrantUser[]; canActForMe: GrantUser[] }
export interface DelegateItem {
  id: string
  grantorId: string
  delegateId: string
  delegateName: string
  validFrom: string
  validTo: string
  enable: boolean
  scope?: string
  remark?: string
}
export interface FormCard { formKey: string; formName: string; category?: string; subCategory?: string; favorite: boolean }
export interface CatalogSub { subCategory: string; forms: FormCard[] }
export interface CatalogNode { category: string; subs: CatalogSub[] }
export interface FormQueryFilter {
  starterId?: string
  handlerId?: string
  flowKey?: string
  keyword?: string
  status?: number
  from?: string
  to?: string
  page?: number
  pageSize?: number
}
export interface FormQueryItem {
  instanceId: string
  flowKey: string
  flowName?: string
  starterId: string
  starterName: string
  status: number
  currentNode: string
  createDate: string
}
