<template>
  <el-dialog :model-value="modelValue" :title="editing ? t('common.edit') : t('oa.flowtrigger.new')"
             width="560px" @close="onClose">
    <el-form label-width="120px">
      <el-form-item :label="t('oa.flowtrigger.form.type')">
        <el-radio-group v-model="form.triggerType" :disabled="!!editing">
          <el-radio v-for="ty in TRIGGER_TYPES" :key="ty.value" :value="ty.value">{{ t(ty.labelKey) }}</el-radio>
        </el-radio-group>
      </el-form-item>
      <el-form-item :label="t('oa.flowtrigger.form.flowKey')">
        <el-input v-model="form.flowKey" />
      </el-form-item>
      <el-form-item :label="t('oa.flowtrigger.form.starter')">
        <el-input v-model="form.starterUserId" :placeholder="t('oa.flowtrigger.form.starterHint')" />
      </el-form-item>

      <!-- timer -->
      <template v-if="form.triggerType === 0">
        <el-form-item :label="t('oa.flowtrigger.form.cronPreset')">
          <el-select v-model="preset" clearable @change="applyPreset">
            <el-option v-for="p in CRON_PRESETS" :key="p.cron" :value="p.cron" :label="t(p.labelKey)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('oa.flowtrigger.form.cron')">
          <el-input v-model="form.cron" placeholder="0 9 * * *" @change="loadPreview" />
          <div class="cron-preview">
            <div>{{ t('oa.flowtrigger.form.previewTz') }}</div>
            <div v-for="d in preview" :key="d">{{ formatDateTime(d) }}</div>
          </div>
        </el-form-item>
        <el-form-item :label="t('oa.flowtrigger.form.varsJson')">
          <el-input v-model="form.varsJson" type="textarea" :rows="2" placeholder='{"a":1}' />
        </el-form-item>
      </template>

      <!-- event -->
      <template v-if="form.triggerType === 1">
        <el-form-item :label="t('oa.flowtrigger.form.eventKey')">
          <el-input v-model="form.eventKey" placeholder="WMS|OnShipmentConfirmedAsync" />
        </el-form-item>
        <el-form-item :label="t('oa.flowtrigger.form.varsMap')">
          <div v-for="(pair, i) in varsMapPairs" :key="i" class="kv-row">
            <el-input v-model="pair.k" :placeholder="t('oa.flowtrigger.form.varName')" />
            <el-input v-model="pair.v" placeholder="$.OutboundNo" />
            <el-button link type="danger" @click="varsMapPairs.splice(i, 1)">✕</el-button>
          </div>
          <el-button link type="primary" @click="varsMapPairs.push({ k: '', v: '' })">+ {{ t('common.add') }}</el-button>
        </el-form-item>
      </template>

      <!-- message -->
      <template v-if="form.triggerType === 2">
        <el-form-item :label="t('oa.flowtrigger.form.varsSchema')">
          <el-input v-model="varsSchemaText" :placeholder="t('oa.flowtrigger.form.varsSchemaHint')" />
        </el-form-item>
        <el-alert v-if="!editing" type="info" :closable="false" show-icon>{{ t('oa.flowtrigger.keyCreateHint') }}</el-alert>
      </template>

      <el-form-item :label="t('oa.flowtrigger.col.enabled')">
        <el-switch v-model="form.enabled" />
      </el-form-item>
    </el-form>
    <template #footer>
      <el-button @click="onClose">{{ t('common.cancel') }}</el-button>
      <el-button type="primary" :loading="saving" @click="onSave">{{ t('common.save') }}</el-button>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { flowTriggerApi, type FlowTriggerItem } from '@/api/oa/flowTrigger'
import { TRIGGER_TYPES, CRON_PRESETS, validateTriggerForm, buildConfigJson } from './flowTriggerModel'
import { formatDateTime } from '@/utils/format'

const props = defineProps<{ modelValue: boolean; editing: FlowTriggerItem | null }>()
const emit = defineEmits<{ 'update:modelValue': [boolean]; saved: [string | null | undefined] }>()
const { t } = useI18n()

const form = reactive({ triggerType: 0, flowKey: '', starterUserId: '', cron: '', varsJson: '', eventKey: '', enabled: true })
const varsMapPairs = ref<{ k: string; v: string }[]>([])
const varsSchemaText = ref('')
const preset = ref('')
const preview = ref<string[]>([])
const saving = ref(false)

watch(() => props.modelValue, open => { if (open) hydrate() })

function hydrate() {
  preview.value = []; preset.value = ''
  const e = props.editing
  if (!e) {
    Object.assign(form, { triggerType: 0, flowKey: '', starterUserId: '', cron: '', varsJson: '', eventKey: '', enabled: true })
    varsMapPairs.value = []; varsSchemaText.value = ''
    return
  }
  const cfg = JSON.parse(e.configJson || '{}')
  Object.assign(form, {
    triggerType: e.triggerType, flowKey: e.flowKey, starterUserId: e.starterUserId,
    cron: cfg.cron ?? '', varsJson: cfg.varsJson ?? '', eventKey: e.eventKey ?? '', enabled: e.enabled,
  })
  varsMapPairs.value = Object.entries(cfg.varsMap ?? {}).map(([k, v]) => ({ k, v: String(v) }))
  varsSchemaText.value = (cfg.varsSchema ?? []).join(',')
  if (form.cron) loadPreview()
}

function applyPreset(cron: string) { if (cron) { form.cron = cron; loadPreview() } }

async function loadPreview() {
  if (!form.cron) { preview.value = []; return }
  try { preview.value = (await flowTriggerApi.cronPreview(form.cron)).next } catch { preview.value = [] }
}

function onClose() { emit('update:modelValue', false) }

async function onSave() {
  const errs = validateTriggerForm({ ...form, triggerType: form.triggerType })
  if (errs.length) { ElMessage.warning(t(errs[0]!)); return }
  const body = {
    flowKey: form.flowKey, triggerType: form.triggerType, enabled: form.enabled,
    eventKey: form.triggerType === 1 ? form.eventKey : null,
    starterUserId: form.starterUserId,
    configJson: buildConfigJson({
      triggerType: form.triggerType, cron: form.cron, varsJson: form.varsJson || undefined,
      varsMap: Object.fromEntries(varsMapPairs.value.filter(p => p.k).map(p => [p.k, p.v])),
      varsSchema: varsSchemaText.value.split(',').map(s => s.trim()).filter(Boolean),
    }),
  }
  saving.value = true
  try {
    if (props.editing) { await flowTriggerApi.update(props.editing.id, body); emit('saved', null) }
    else { const r = await flowTriggerApi.create(body); emit('saved', r.apiKeyPlain) }
    onClose()
  } catch {
    // http 拦截器已 toast，无需重复提示
  }
  finally { saving.value = false }
}
</script>

<style scoped>
.kv-row { display: flex; gap: 8px; margin-bottom: 6px; }
.cron-preview { font-size: 12px; opacity: 0.75; margin-top: 4px; }
</style>
