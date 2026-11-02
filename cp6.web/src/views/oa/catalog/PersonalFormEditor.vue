<script setup lang="ts">
import { personalFormError } from './personalFormError'
import { computed, ref, watch, onBeforeUnmount } from 'vue'
import { ElMessageBox } from 'element-plus'
import { identityStamp, requireIdentity } from '@/auth/identity'
import { personalFormApi as api, submissionContext, type PersonalContext, type PersonalDraft, type FormSubmission, type RebasePreview, type ValuePreview } from '@/api/oa/personalForm'
import { formObject, readFormJson, writeFormJson, schemaFields, initialFormData, type FormObject } from './personalFormJson'
import { usePersonalCommand } from './usePersonalCommand'
import PersonalCommandStatus from './PersonalCommandStatus.vue'
import PersonalFormFields from './PersonalFormFields.vue'
const props = defineProps<{ formKey?: string; draftId?: string }>()
const emit = defineEmits<{ changed: []; locked: [value: boolean] }>()
const context = ref<PersonalContext>(), draft = ref<PersonalDraft>(), submission = ref<FormSubmission>()
const model = ref<FormObject>({}), title = ref(''), loading = ref(false), readError = ref(''), valuesValid = ref(true)
const preview = ref<ValuePreview>(), rebase = ref<RebasePreview>(), confirmedLoss = ref(false)
let generation = 0, readGeneration = 0, live = true
function clearPrivate() { generation++; readGeneration++; loading.value = false; context.value = undefined; draft.value = undefined; model.value = {}; title.value = ''; submission.value = undefined; preview.value = undefined; rebase.value = undefined }
const commands = usePersonalCommand(clearPrivate, async result => {
  if (result.result && 'draftId' in result.result) {
    // Original receipts are immutable snapshots. Re-read current state for fresh CAS/capabilities.
    // Keep the accepted draft identity even if this follow-up read fails.
    applyDraft({ ...result.result, capabilities: [] }); submission.value = undefined
    const identity = identityStamp(), current = generation
    try {
      const value = await api.draft(result.result.draftId); requireIdentity(identity)
      if (!live || current !== generation) return
      applyDraft(value)
    } catch (e: any) {
      if (!live || current !== generation || identity !== identityStamp()) return
      if ([401, 403, 404].includes(e?.response?.status)) clearPrivate()
      readError.value = '原操作已完成，当前草稿读取失败：' + personalFormError(e)
    }
  } else if (result.result && 'formDataId' in result.result) {
    submission.value = result.result; model.value = {}; draft.value = undefined; context.value = undefined; title.value = ''; preview.value = undefined; rebase.value = undefined
  }
  emit('changed')
})
const { pending, busy, error, message, storageUnavailable } = commands
const locked = computed(() => busy.value || loading.value || !!pending.value || storageUnavailable.value || !!submission.value)
watch(locked, value => emit('locked', value), { immediate: true })
const current = computed(() => draft.value ?? context.value)
const fields = computed(() => current.value ? schemaFields(current.value.schemaJson) : [])
const active = computed(() => !draft.value || draft.value.status === 0)
const can = (capability: string) => current.value?.capabilities.includes(capability) === true
function applyDraft(value: PersonalDraft) { draft.value = value; context.value = undefined; model.value = formObject(readFormJson(value.dataJson)); title.value = value.title ?? ''; preview.value = undefined; rebase.value = undefined }
async function read(action: (check: () => void) => Promise<void>) {
  if (loading.value) return
  const identity = identityStamp(), current = generation, request = ++readGeneration; loading.value = true; readError.value = ''
  const check = () => { requireIdentity(identity); if (!live || current !== generation) throw new Error('STALE') }
  try { await action(check) }
  catch (e: any) {
    try { check(); if ([401, 403, 404].includes(e?.response?.status)) clearPrivate(); readError.value = personalFormError(e) } catch { /* obsolete response */ }
  } finally { if (live && request === readGeneration) loading.value = false }
}
async function load() {
  await read(async check => {
    if (props.draftId) { const value = await api.draft(props.draftId); check(); applyDraft(value) }
    else if (props.formKey) {
      const value = await api.context(props.formKey); check(); context.value = value; draft.value = undefined
      const initial = formObject(readFormJson(initialFormData(value.schemaJson)))
      for (const name of Object.keys(initial)) if (value.fieldMask[name] === 'hidden') delete initial[name]
      model.value = initial; title.value = ''; preview.value = undefined
    } else throw new Error('请选择表单或草稿')
  })
}
async function reload() {
  if (locked.value && !submission.value) return
  if (current.value) { try { await ElMessageBox.confirm('重新读取会放弃尚未保存的修改。', '重新读取') } catch { return } }
  submission.value = undefined; await load()
}
function bodyWithData(other: object) { return JSON.stringify(other).slice(0, -1) + ',"data":' + writeFormJson(model.value) + '}' }
async function save() {
  if (locked.value || !valuesValid.value || !current.value) return
  if (draft.value) await commands.execute('FORM_DRAFT_SAVE', draft.value.draftId, draft.value.rowVersion, bodyWithData({ title: title.value || null }))
  else await commands.execute('FORM_DRAFT_CREATE', context.value!.formKey, null, bodyWithData({ formVersionId: context.value!.formVersionId, title: title.value || null }))
}
async function submit() {
  if (locked.value || !valuesValid.value || !current.value) return
  let admission = context.value
  if (draft.value) {
    if (writeFormJson(model.value) !== writeFormJson(formObject(readFormJson(draft.value.dataJson))) || title.value !== (draft.value.title ?? '')) { readError.value = '请先保存当前修改，再提交草稿。'; return }
    await read(async check => { admission = await api.context(draft.value!.formKey); check() })
    if (!admission || readError.value || !draft.value) return
    if (admission.formVersionId !== draft.value.formVersionId) { readError.value = '当前准入版本已改变，请查看重基预览后明确更新草稿。'; return }
    await commands.execute('FORM_DRAFT_SUBMIT', draft.value.draftId, draft.value.rowVersion, JSON.stringify(submissionContext(admission, 'DRAFT')))
  } else await commands.execute('FORM_DIRECT_SUBMIT', admission!.formKey, null, bodyWithData(submissionContext(admission!, 'DIRECT')))
}
async function previewValues() {
  if (locked.value || !valuesValid.value || !current.value) return
  await read(async check => {
    const url = draft.value ? `/oa/v2/drafts/${draft.value.draftId}/preview` : `/oa/v2/forms/${encodeURIComponent(context.value!.formKey)}/preview`
    const value = await api.preview(url, bodyWithData({ formVersionId: current.value!.formVersionId })); check(); preview.value = value
  })
}
async function remove() {
  if (!draft.value || locked.value) return
  try { await ElMessageBox.confirm('仅删除此未提交草稿。内容按当前留存政策保留，可恢复能力以服务端为准。', '删除草稿', { type: 'warning' }) } catch { return }
  await commands.execute('FORM_DRAFT_DELETE', draft.value.draftId, draft.value.rowVersion, JSON.stringify({ reason: '本人删除未提交草稿' }))
}
async function restore() { if (draft.value && !locked.value) await commands.execute('FORM_DRAFT_RESTORE', draft.value.draftId, draft.value.rowVersion, JSON.stringify({ reason: '本人恢复留存草稿' })) }
async function previewRebase() {
  if (!draft.value || locked.value) return
  if (writeFormJson(model.value) !== writeFormJson(formObject(readFormJson(draft.value.dataJson))) || title.value !== (draft.value.title ?? '')) { readError.value = '请先保存当前修改，再预览重基。'; return }
  await read(async check => { const value = await api.rebasePreview(draft.value!.draftId, draft.value!.latestPublishedVersionId, draft.value!.rowVersion); check(); rebase.value = value; confirmedLoss.value = false })
}
async function applyRebase() {
  if (!draft.value || !rebase.value || locked.value || !valuesValid.value || rebase.value.requiresConfirmation && !confirmedLoss.value) return
  if (writeFormJson(model.value) !== writeFormJson(formObject(readFormJson(draft.value.dataJson))) || title.value !== (draft.value.title ?? '')) { readError.value = '请先保存当前修改，并重新预览重基。'; return }
  await commands.execute('FORM_DRAFT_REBASE', draft.value.draftId, rebase.value.expectedVersion, JSON.stringify({ previewId: rebase.value.previewId, previewDigest: rebase.value.digest, targetVersionId: rebase.value.targetVersionId, confirmRemovedValues: confirmedLoss.value }))
}
watch(() => [props.formKey, props.draftId], () => { clearPrivate(); void load() }, { immediate: true })
watch(model, () => { preview.value = undefined }, { deep: true })
onBeforeUnmount(() => { live = false; generation++ })
</script>
<template>
  <div class="personal-editor">
    <PersonalCommandStatus :pending="pending" :busy="busy" :error="error" :message="message" :can-retry="commands.canRetry()" @recover="commands.recover" @retry="commands.retry" @close="commands.close" />
    <el-alert v-if="readError" :title="readError" type="error" :closable="false" />
    <el-button v-if="!current && !submission" :loading="loading" @click="load">重新读取</el-button>
    <el-button v-if="current" :disabled="locked" @click="reload">重新读取</el-button>
    <template v-if="current">
      <h3>{{ current.name }} · v{{ current.formVersion }}</h3>
      <p v-if="context">{{ context.flowVersionId ? `审批流程版本 v${context.flowVersion}` : '独立表单：提交后不会发起审批' }}</p>
      <el-alert v-if="draft?.stale" title="已发布更新版本。草稿仍保留原版本；是否需要重基由当前提交上下文决定。" type="info" :closable="false" />
      <el-alert v-if="draft?.status === 2" title="草稿已删除；内容按留存政策保留。" type="warning" :closable="false" />
      <el-input v-if="current.titleAccess !== 'hidden'" v-model="title" placeholder="草稿标题（保存草稿时使用）" maxlength="200" :disabled="locked || !active || current.titleAccess !== 'edit'" />
      <PersonalFormFields :fields="fields" v-model="model" :mask="current.fieldMask" :form-key="current.formKey" :draft-id="draft?.draftId" :disabled="locked || !active" @validity="valuesValid = $event" @forbidden="clearPrivate" />
      <div class="actions">
        <el-button v-if="active" :disabled="locked || !valuesValid" @click="previewValues">预览计算与校验</el-button>
        <el-button v-if="can(draft ? 'SAVE' : 'CREATE_DRAFT')" :disabled="locked || !valuesValid" @click="save">保存草稿</el-button>
        <el-button v-if="can(draft ? 'SUBMIT_WITH_CURRENT_CONTEXT' : 'SUBMIT')" type="primary" :disabled="locked || !valuesValid" @click="submit">提交</el-button>
        <el-button v-if="can('DELETE')" type="danger" :disabled="locked" @click="remove">删除草稿</el-button>
        <el-button v-if="can('RESTORE')" :disabled="locked" @click="restore">恢复草稿</el-button>
        <el-button v-if="draft?.stale && can('REBASE')" :disabled="locked" @click="previewRebase">预览重基到 v{{ draft.latestPublishedVersion }}</el-button>
      </div>
      <section v-if="preview">
        <h4>计算结果预览</h4><p>{{ preview.valid ? '当前填写通过校验；提交时仍会核对当前资格。' : '仍有必填或格式问题，请检查填写。' }}</p>
        <PersonalFormFields :fields="fields" :model-value="formObject(readFormJson(preview.dataJson))" :mask="current.fieldMask" :hidden="preview.hiddenFields" disabled />
      </section>
      <section v-if="rebase">
        <h4>重基预览</h4><p>有效期至 {{ rebase.expiresAtUtc }}。重基使用已保存内容。</p>
        <ul><li v-for="(change, index) in rebase.changes" :key="index">{{ change.path }}：{{ change.kind }}{{ change.hasValue ? '（原值非空）' : '' }}</li></ul>
        <p v-if="rebase.validationErrors.length">新版本校验提示：{{ rebase.validationErrors.join('；') }}</p>
        <el-checkbox v-if="rebase.requiresConfirmation" v-model="confirmedLoss">确认预览中列明的有值字段损失</el-checkbox>
        <el-button :disabled="locked || (rebase.requiresConfirmation && !confirmedLoss)" @click="applyRebase">按此预览重基</el-button>
      </section>
    </template>
    <section v-if="submission" role="status">
      <h3>表单已提交</h3><p>{{ submission.flowInstanceId ? '已发起审批。' : '此表单已独立提交，未发起审批。' }}</p>
      <p>提交编号：{{ submission.formDataId }}</p><p>表单版本：v{{ submission.formVersion }}</p>
      <p v-if="submission.flowInstanceId">审批实例：{{ submission.flowInstanceId }}</p>
      <el-button v-if="formKey" :disabled="busy" @click="reload">另填一份</el-button>
    </section>
  </div>
</template>
<style scoped>.actions { display: flex; gap: 8px; flex-wrap: wrap; margin: 16px 0; } section { border-top: 1px solid var(--el-border-color); margin-top: 16px; padding-top: 8px; }</style>
