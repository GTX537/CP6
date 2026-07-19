<template>
  <div class="form-initiate">
    <!-- Loading skeleton -->
    <el-skeleton v-if="loading" :rows="6" animated />

    <!-- No enabled flow configured for this form -->
    <el-alert
      v-else-if="noFlow"
      type="warning"
      :title="t('该表单未配置启用流程')"
      show-icon
      :closable="false"
      style="margin-bottom: 16px"
    />

    <!-- Main content -->
    <template v-else>
      <el-row :gutter="16" class="initiate-body">
        <!-- LEFT: editable form -->
        <el-col :span="forecastVisible ? 14 : 24" class="form-col">
          <div class="panel-title">{{ t('oa.initiate.fillForm') }}</div>
          <DynamicForm
            v-if="schema.fields.length"
            ref="dynamicFormRef"
            :schema="schema"
            v-model="model"
          />
          <CpEmpty v-else :text="t('oa.initiate.noFields')" />
        </el-col>

        <!-- RIGHT: forecast timeline (shown after preview) -->
        <el-col v-if="forecastVisible" :span="10" class="timeline-col">
          <div class="panel-title">{{ t('oa.initiate.forecastTitle') }}</div>
          <FlowTimeline :timeline="[]" :forecast="forecastSteps" />
        </el-col>
      </el-row>

      <!-- Action bar -->
      <div class="action-bar">
        <el-button size="small" :loading="previewing" @click="doPreview">
          {{ t('oa.initiate.preview') }}
        </el-button>
        <el-button v-permission="'oa-form-catalog:add'" size="small" :loading="saving" @click="doSave">
          {{ t('oa.initiate.saveDraft') }}
        </el-button>
        <el-button
          v-permission="'oa-form-catalog:submit'"
          type="primary"
          size="small"
          :loading="submitting"
          @click="doSubmit"
        >
          {{ t('oa.initiate.submit') }}
        </el-button>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { formApi } from '@/api/wf/form'
import { flowApi } from '@/api/wf/flow'
import { flowAdminApi } from '@/api/oa/flowAdmin'
import { forecastApi } from '@/api/oa/forecast'
import { draftApi } from '@/api/oa/draft'
import type { FormSchema } from '@/types/wf/wf'
import type { ForecastStep, FlowAdminItem } from '@/types/oa/inbox'
import CpEmpty from '@/components/base/CpEmpty.vue'
import DynamicForm from '@/views/wf/DynamicForm.vue'
import FlowTimeline from '@/views/oa/inbox/FlowTimeline.vue'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()

/** formKey from route query */
const formKey = (route.query.formKey as string) || ''

// ── State ────────────────────────────────────────────────────────
const loading = ref(false)
/** true when no enabled flow is configured for this formKey */
const noFlow = ref(false)
/** resolved flowKey from flowAdminApi */
const resolvedFlowKey = ref('')
const schema = ref<FormSchema>({ fields: [] })
const model = ref<Record<string, any>>({})
/** whether to show the forecast timeline panel */
const forecastVisible = ref(false)
const forecastSteps = ref<ForecastStep[]>([])
const previewing = ref(false)
const saving = ref(false)
const submitting = ref(false)

/** Ref to DynamicForm instance — expose.validate() for pre-submit validation */
const dynamicFormRef = ref<InstanceType<typeof DynamicForm> | null>(null)

// ── Initialise ───────────────────────────────────────────────────
async function load() {
  if (!formKey) return
  loading.value = true
  noFlow.value = false
  try {
    // 1. Find the enabled flow whose formKey matches this form (1:1 mapping)
    const adminRes = await flowAdminApi.list()
    const flows = ((adminRes as any).data as FlowAdminItem[]) || []
    const matched = flows.find((f) => f.formKey === formKey && f.enable)
    if (!matched) {
      noFlow.value = true
      return
    }
    resolvedFlowKey.value = matched.flowKey

    // 2. Load form field schema
    const formRes = await formApi.getDef(formKey)
    if (formRes.data?.schemaJson) {
      try {
        const parsed = JSON.parse(formRes.data.schemaJson) as FormSchema
        schema.value = { fields: parsed.fields ?? [], rules: parsed.rules }
      } catch {
        schema.value = { fields: [] }
      }
    }
  } finally {
    loading.value = false
  }
}

// ── Actions ──────────────────────────────────────────────────────

/** 预览流程: fetch forecast steps and show timeline panel */
async function doPreview() {
  if (!resolvedFlowKey.value) return
  previewing.value = true
  try {
    const res = await forecastApi.preview(
      resolvedFlowKey.value,
      JSON.stringify(model.value),
    )
    forecastSteps.value = ((res as any).data as ForecastStep[]) || []
    forecastVisible.value = true
  } catch {
    // HTTP errors already surfaced by the http interceptor
  } finally {
    previewing.value = false
  }
}

/** 存暂存: save current model as draft */
async function doSave() {
  if (!resolvedFlowKey.value) return
  saving.value = true
  try {
    await draftApi.save(resolvedFlowKey.value, JSON.stringify(model.value))
    ElMessage.success(t('oa.initiate.savedDraft'))
  } catch {
    // HTTP errors already surfaced by the http interceptor
  } finally {
    saving.value = false
  }
}

/** 提交: validate → save draft → submit → navigate to inbox */
async function doSubmit() {
  if (!resolvedFlowKey.value) return

  // validate form fields before submitting
  if (dynamicFormRef.value) {
    const valid = await dynamicFormRef.value.validate()
    if (!valid) return
  }

  submitting.value = true
  try {
    // save → get draft id → submit
    await flowApi.submit({
      flowKey: resolvedFlowKey.value,
      varsJson: JSON.stringify(model.value),
    })
    ElMessage.success(t('oa.initiate.submitOk'))
    router.push('/oa/inbox')
  } catch {
    // HTTP errors already surfaced by the http interceptor
  } finally {
    submitting.value = false
  }
}

onMounted(load)
</script>

<style scoped>
.form-initiate {
  padding: 4px 0;
  min-height: 200px;
}

.initiate-body {
  min-height: 200px;
}

.panel-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--cp-ink);
  margin-bottom: 10px;
  padding-bottom: 6px;
  border-bottom: 1px solid var(--cp-line-soft);
}

.form-col {
  padding-right: 16px;
}

.timeline-col {
  border-left: 1px solid var(--cp-line-soft);
  padding-left: 8px;
  overflow-y: auto;
  max-height: calc(100vh - 220px);
}

.action-bar {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 14px 0 4px;
  margin-top: 16px;
  border-top: 1px solid var(--cp-line-soft);
}
</style>
