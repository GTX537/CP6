<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { nativeTaskOperationsApi as api, type ReturnedContext } from '@/api/oa/nativeTaskOperations'
import { formObject, readFormJson, writeFormJson, schemaFields, type FormObject } from '../catalog/personalFormJson'
import PersonalFormFields from '../catalog/PersonalFormFields.vue'
import NativeOperationRecovery from './NativeOperationRecovery.vue'
import { useNativeOperation } from './useNativeOperation'
const props = defineProps<{ instanceId: string }>(), emit = defineEmits<{ completed: []; privateClear: [] }>()
const context = ref<ReturnedContext>(), data = ref<FormObject>({}), reason = ref(''), valid = ref(true), loading = ref(false), baseline = ref('')
let generation = 0
const op = useNativeOperation('cp6-returned-form-operation', clear, () => emit('completed'), () => emit('privateClear'))
const { pending, busy, canRetry, error, message, locked } = op
const fields = computed(() => context.value ? schemaFields(context.value.schemaJson) : [])
const dirty = computed(() => writeFormJson(data.value) !== baseline.value)
function clear() { generation++; context.value = undefined; data.value = {}; reason.value = ''; loading.value = false; baseline.value = '' }
async function load() {
  if (!props.instanceId || locked.value || loading.value) return
  const check = op.capture(); clear(); const request = generation; loading.value = true; error.value = ''
  try { const value = await api.returned(props.instanceId); check(); if (request !== generation) return; context.value = value; data.value = formObject(readFormJson(value.dataJson)); baseline.value = writeFormJson(data.value) }
  catch (e) { try { check(); if (request === generation) op.failed(e) } catch { /* stale */ } }
  finally { if (request === generation) loading.value = false }
}
async function save() {
  const c = context.value; if (!c?.canEdit || locked.value || loading.value || !valid.value) return
  const before = formObject(readFormJson(baseline.value)), patch: FormObject = {}
  for (const [name, value] of Object.entries(data.value))
    if (c.fieldMask[name] === 'edit' && value !== undefined && (before[name] === undefined || writeFormJson(value) !== writeFormJson(before[name]!))) patch[name] = value
  const body = `{"returnRevision":${c.returnRevision},"expectedFormDataVersion":${JSON.stringify(c.formDataVersion)},"dataPatch":${writeFormJson(patch)},"reason":${JSON.stringify(reason.value)}}`
  await op.begin('SFS_RETURNED_EDIT', props.instanceId, c.instanceVersion, body)
}
async function resubmit() {
  const c = context.value; if (!c?.canResubmit || locked.value || loading.value || dirty.value) return
  await op.begin('SFS_RETURNED_RESUBMIT', props.instanceId, c.instanceVersion, JSON.stringify({ returnRevision: c.returnRevision, expectedFormDataVersion: c.formDataVersion, comment: reason.value }))
}
watch(() => props.instanceId, clear)
</script>
<template>
  <section v-if="instanceId || pending || error" aria-label="退回表单重送">
    <h3>修改并重送退回表单</h3><p>仅原发起人可修改。修改后先保存，再读取最新内容进行重送。</p>
    <NativeOperationRecovery :pending="pending" :busy="busy" :can-retry="canRetry" :error="error" :message="message" @recover="op.recover($event)" />
    <el-button v-if="instanceId" :disabled="locked || loading" @click="load">读取退回表单</el-button>
    <template v-if="context">
      <h4>{{ context.name }}</h4><p>第 {{ context.returnRevision }} 次退回。流程与表单保持原固定版本。</p>
      <PersonalFormFields v-model="data" :fields="fields" :mask="context.fieldMask" :allow-unset="false" :disabled="locked || loading || !context.canEdit" @validity="valid = $event" @forbidden="clear(); emit('privateClear')" />
      <label>说明 <textarea v-model="reason" :disabled="locked || loading" maxlength="1000" /></label>
      <p v-if="dirty">有未保存的修改，保存后才能重送。</p>
      <el-button :disabled="locked || loading || !context.canEdit || !valid" @click="save">保存退回修改</el-button>
      <el-button :disabled="locked || loading || !context.canResubmit || dirty" @click="resubmit">重送同一表单</el-button>
      <p v-if="context.reasons.length">当前限制：{{ context.reasons.join('、') }}</p>
    </template>
  </section>
</template>
<style scoped>section { padding: 12px; border: 1px solid var(--el-border-color); margin: 12px 0; }label { display: block; margin: 8px 0; }textarea { display: block; width: min(500px, 100%); }</style>
