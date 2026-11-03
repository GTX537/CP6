import http from '@/api/http'
export interface FormRulePreview {
  mode: 'EXPLANATION_ONLY'; definitionId: string; draftVersionId: string; expectedVersion: string; dataJson: string; validationErrors: string[]
  presentation: { name: string; visible: boolean; required: boolean; disabled: boolean; optionsJson: string | null }[]
  appliedEffects: { path: string; action: string; target: string; valueJson: string | null }[]; unproven: string[]
}
export const formRulePreviewApi = {
  preview: async (key: string, input: { draftVersionId: string; expectedVersion: string; dataJson: string }, signal?: AbortSignal): Promise<FormRulePreview> => {
    const response: any = await http.post(`/oa/v2/definitions/FORM/${encodeURIComponent(key)}/rule-preview`, input, { signal })
    return response?.data ?? response
  },
}
