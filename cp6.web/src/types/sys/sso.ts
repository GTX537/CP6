// S 类 #3 SSO（T9）：租户 SSO/OIDC 配置类型。

/** GET /api/sys/sso-config 投影（绝不含 ClientSecret，仅 hasClientSecret 标志是否已配密钥）。 */
export interface SsoConfig {
  authority: string
  clientId: string
  scopes: string
  emailClaim: string
  enabled: boolean
  enforced: boolean
  autoProvision: boolean
  defaultRoleId: number | null
  hasClientSecret: boolean
  tenantId: string
  actorId: string
  configVersion: string
  policyVersion: string
  admissionPolicy: SsoAdmissionPolicy | null
}

/** PUT /api/sys/sso-config 入参。clientSecret 留空=保留原密文（不修改）。 */
export interface SsoConfigInput {
  authority: string
  clientId: string
  clientSecret?: string
  scopes: string
  emailClaim: string
  enabled: boolean
  enforced: boolean
  autoProvision: boolean
  defaultRoleId: number | null
  expectedConfigVersion: string
  expectedPolicyVersion: string
  expectedPolicyRef?: string | null
  operationId: string
  reason: string
  admission: SsoAdmissionChange
}

export interface SsoAdmissionChange {
  action: 'preserve' | 'approve' | 'revoke'
  approvedIssuer?: string
  issuerIsEmailAuthority?: boolean
  emailClaimSemantic?: 'verified-email'
  allowInitialEmailLink?: boolean
  allowJit?: boolean
  approvedJitRoleIds?: number[]
}
export interface SsoAdmissionPolicy {
  policyRef: string
  version: string
  approvedIssuer: string
  authority: string
  clientId: string
  emailClaim: string
  allowInitialEmailLink: boolean
  allowJit: boolean
  approvedJitRoleIds: number[]
  approvedBy: string
  approvedAtUtc: string
}
export interface SsoConfigOperation {
  operationId: string
  state: 'COMMITTED' | 'CLOSED' | 'UNKNOWN'
  configVersion?: string
  policyVersion?: string
}
