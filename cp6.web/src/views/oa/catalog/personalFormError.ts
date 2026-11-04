const messages: Record<string, string> = {
  FORM_CONTEXT_CHANGED: '表单或审批配置已更新，请重新读取后再提交。',
  FORM_BINDING_VERSION_CHANGED: '表单版本与审批绑定不一致，请联系管理员核对配置。',
  FORM_BINDING_REQUIRED: '此表单需要有效审批流程，当前尚未配置。',
  FORM_DISABLED: '表单已停止新提交。已保存的草稿仍可查看。',
  FORM_USE_POLICY_REQUIRED: '此表单尚未配置使用权限，请联系管理员。',
  FORM_SERVICE_OWNER_REQUIRED: '此流程包含尚未接入的服务步骤，当前不能提交。',
  FORM_FILE_SOURCE_REQUIRED: '此表单的附件能力尚未接入，当前不能提交。',
  FORM_VALIDATION_FAILED: '请检查必填项、格式和明细行数后重试。',
  FORM_FIELD_TYPE_INVALID: '填写内容的类型与表单定义不符，请检查输入。',
  FORM_REFERENCE_INVALID: '所选人员或部门已不可用，请重新选择。',
  FORM_OPTION_VALUE_UNAVAILABLE: '所选字典值已失效或类型不匹配，请检查当前选项并明确重新选择。原值仍保留。',
  FORM_OPTION_SOURCE_UNAVAILABLE: '此字段的字典来源已停用或不存在，请联系管理员恢复来源。',
  FORM_OPTION_SOURCE_AMBIGUOUS: '字典来源存在重复定义或重复值，请联系管理员修正。',
  FORM_OPTION_SOURCE_INVALID: '此字段的选项来源配置不受支持，请联系管理员核对表单版本。',
  FORM_OPTION_SOURCE_COVERAGE_UNAVAILABLE: '当前无法确认字典选项的一致性，请稍后重新读取。',
  DRAFT_VERSION_CONFLICT: '草稿已被更新，请重新读取并核对修改。',
  DRAFT_ALREADY_SUBMITTED: '草稿已经提交，不能再次修改或删除。',
  DRAFT_DELETED: '草稿已经删除，请重新读取其当前状态。',
  DRAFT_REBASE_REQUIRED: '当前提交版本已改变，请先预览并确认重基。',
  DRAFT_RESTORE_UNAVAILABLE: '此草稿当前不能恢复，请核对留存政策。',
  FORM_RECOVERY_DISABLED: '当前策略不允许从历史快照恢复新草稿，原记录仍保留。',
  FORM_RECOVERY_RETENTION_EXPIRED: '此快照的恢复期限已过或留存策略已改变，原记录仍保留。',
  FORM_RECOVERY_WRITER_COVERAGE_UNAVAILABLE: '快照恢复暂时不可用，结果不确定时请查询原命令。',
  REBASE_PREVIEW_CHANGED: '重基预览已过期或条件已改变，请重新预览。',
  REBASE_TARGET_CHANGED: '目标发布版本已改变，请重新读取草稿。',
  REBASE_HIDDEN_LOSS_BLOCKED: '重基会影响当前无权查看的内容，已停止操作。',
  REBASE_CONFIRMATION_REQUIRED: '请先确认预览中列明的原值损失。',
  COMMAND_CLOSED: '原命令已经关闭，没有产生新的业务效果。',
  COMMAND_OUTCOME_UNKNOWN: '结果暂时无法确认，请查询原命令。',
  FORM_STORAGE_UNAVAILABLE: '当前无法确认操作结果，请查询原命令。',
  FORM_WRITER_COVERAGE_UNAVAILABLE: '表单存储暂时不可写，请稍后恢复原操作。',
  PRECONDITION_FAILED: '待办或流程已更新，请重新读取后明确办理。',
  TASK_NOT_PENDING: '此待办已不处于待办理状态，请重新读取。',
  TASK_DUTY_REVOKED: '您当前已不负责此待办，请重新核对办理权限。',
  DELEGATION_EXPIRED: '代理授权已失效，请重新核对当前身份。',
}
export function personalFormError(error: any): string {
  const status = error?.response?.status
  if (status === 401) return '登录状态已失效，请重新登录后查询原操作。'
  if (status === 403) return '当前没有此操作或内容的访问权限，已清除页面内容。'
  if (status === 404) return '当前无法访问此表单或草稿，请重新核对。'
  const code = error?.response?.data?.messageKey || error?.response?.data?.code
  if (typeof code === 'string' && messages[code]) return messages[code]
  if (code || error?.response) return '操作未完成，请核对当前状态；结果不确定时先查询原命令。'
  return error?.message || '连接中断，结果尚未确认，请查询原命令。'
}
