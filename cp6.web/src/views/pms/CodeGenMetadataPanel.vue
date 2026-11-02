<template>
  <el-card class="metadata-panel">
    <template #header>{{ t('版本化元数据') }}</template>
    <p>{{ t('保存当前表、列和模板版本，不应用生成文件。旧元数据可按原 ID 显式接入，不能按名称接管。') }}</p>
    <el-form inline label-width="100px">
      <el-form-item :label="t('元数据 ID')">
        <el-input v-model="lookupId" :disabled="busy || !!pending" :aria-label="t('元数据 ID')" class="metadata-id" />
      </el-form-item>
      <el-button v-permission="'pub-codegen:view'" :disabled="busy || !!pending" @click="load">{{ t('读取最新版本（替换草稿）') }}</el-button>
      <el-button v-permission="'pub-codegen:view'" :disabled="busy || !!pending" @click="loadLegacy">{{ t('读取旧原件（替换草稿）') }}</el-button>
      <el-button :disabled="busy || !!pending" @click="newDocument">{{ t('作为新元数据') }}</el-button>
    </el-form>
    <p>{{ t('当前保存目标') }}：<code>{{ metadataId }}</code> · {{ t('版本') }} {{ revision }}</p>
    <div v-if="legacy" class="metadata-legacy">
      <p>{{ t('已读取旧原件。接入将保留原 ID 和旧记录，后续由版本化入口保存；当前草稿修改不会包含在接入中。') }}</p>
      <el-button v-permission="'pub-codegen:save'" :disabled="busy || !!pending || conflict || !!storageError" @click="adopt">{{ t('将读取的旧原件接入 V1') }}</el-button>
    </div>
    <el-button v-permission="'pub-codegen:save'" type="primary" :disabled="busy || !!pending || !!legacy || conflict || !preview || !!storageError" @click="save">{{ t('保存当前版本') }}</el-button>
    <div v-if="pending" class="metadata-pending">
      <p>{{ t('原保存结果待确认；刷新页面后仍可恢复，确认前不能开始另一项保存。') }}</p>
      <p>{{ t('原操作') }}：<code>{{ pending.command.operationRef }}</code></p>
      <el-button v-permission="'pub-codegen:view'" :disabled="busy || !!storageError" @click="recover(false)">{{ t('查询原保存结果') }}</el-button>
      <el-button v-permission="'pub-codegen:view'" :disabled="busy || !!storageError" @click="recover(true)">{{ t('按原操作重试') }}</el-button>
    </div>
    <el-alert v-if="storageError || message" :title="storageError || message" :type="storageError || error ? 'error' : 'info'" :closable="false" show-icon />
  </el-card>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { codeGenApi, type CodeGenColumnInput, type CodeGenMetadataAdopt, type CodeGenMetadataLegacy, type CodeGenMetadataDocument, type CodeGenMetadataReceipt,
  type CodeGenMetadataSave, type CodeGenPreview, type CodeGenTableInput } from '@/api/pub/codegen'

const props = defineProps<{ table: CodeGenTableInput; columns: CodeGenColumnInput[]; preview: CodeGenPreview | null }>()
const emit = defineEmits<{ loaded: [document: CodeGenMetadataDocument] }>()
const { t } = useI18n()
// Keep the W262 normal-save shape readable across an upgrade in the same browser tab.
type Pending = { metadataId: string; command: CodeGenMetadataSave }
  | { metadataId: string; command: CodeGenMetadataAdopt; legacyDocument: CodeGenMetadataDocument }
const metadataId = ref(''), lookupId = ref(''), revision = ref(0)
const legacy = ref<CodeGenMetadataLegacy | null>(null)
const pending = ref<Pending | null>(null), busy = ref(false), conflict = ref(false)
const message = ref(''), error = ref(false), storageError = ref('')
let stamp = identityStamp(), disposed = false
const storageKey = () => `cp6-codegen-metadata-pending-v1:${stamp}`
const active = (started: string) => !disposed && started === stamp && started === identityStamp()
function notice(text: string, failed = false) { message.value = t(text); error.value = failed }
function reset() {
  metadataId.value = crypto.randomUUID(); lookupId.value = metadataId.value; revision.value = 0
  pending.value = null; legacy.value = null; conflict.value = false; busy.value = false; message.value = ''; storageError.value = ''
  try {
    const raw = sessionStorage.getItem(storageKey())
    if (raw) {
      pending.value = JSON.parse(raw) as Pending
      metadataId.value = pending.value.metadataId; lookupId.value = metadataId.value
      revision.value = 'legacyDocument' in pending.value ? 0 : pending.value.command.expectedRevision
      emit('loaded', 'legacyDocument' in pending.value ? pending.value.legacyDocument : pending.value.command.document)
    }
  } catch { storageError.value = t('无法读取本页原保存记录，请保留当前标签页并恢复浏览器存储后刷新。') }
}
function changed() { const next = identityStamp(); if (next !== stamp) { stamp = next; reset() } }
window.addEventListener(AUTH_EVENT, changed); window.addEventListener('storage', changed)
onBeforeUnmount(() => { disposed = true; window.removeEventListener(AUTH_EVENT, changed); window.removeEventListener('storage', changed) })
reset()

function clearPending() {
  // Keep the original request visible if browser storage cannot be updated.
  sessionStorage.removeItem(storageKey()); pending.value = null
}
function accept(receipt: CodeGenMetadataReceipt) {
  if (!Number.isSafeInteger(receipt.snapshot.revision)) throw new Error(t('版本超出页面支持范围，请保留原操作并使用版本化接口恢复。'))
  clearPending(); metadataId.value = receipt.snapshot.metadataId; lookupId.value = metadataId.value
  revision.value = receipt.snapshot.revision; legacy.value = null; conflict.value = false
  notice('原操作已确认保存；当前草稿保留，后续保存将基于此版本。')
}
function failure(cause: unknown) {
  const data = (cause as { response?: { data?: { code?: string; message?: string } } }).response?.data
  notice(data?.message || data?.code || (cause as Error).message || '请求失败', true)
}
function newDocument() {
  if (busy.value || pending.value) return
  metadataId.value = crypto.randomUUID(); lookupId.value = metadataId.value; revision.value = 0; legacy.value = null; conflict.value = false
  notice('已准备新的元数据 ID；同租户已有实体名不能重复创建。')
}
async function load() {
  if (busy.value || pending.value) return
  const started = stamp; busy.value = true
  try {
    const snapshot = await codeGenApi.metadata(lookupId.value)
    if (!active(started)) return
    if (!Number.isSafeInteger(snapshot.revision)) throw new Error(t('版本超出页面支持范围，请使用版本化接口读取。'))
    metadataId.value = snapshot.metadataId; lookupId.value = metadataId.value; revision.value = snapshot.revision
    conflict.value = false; legacy.value = null; emit('loaded', snapshot.document); notice('已读取最新版本，请重新预览后保存。')
  } catch (cause) { if (active(started)) failure(cause) }
  finally { if (active(started)) busy.value = false }
}
async function save() {
  if (busy.value || pending.value || legacy.value || conflict.value || !props.preview || storageError.value) return
  const original: Pending = { metadataId: metadataId.value, command: { operationRef: crypto.randomUUID(), expectedRevision: revision.value,
    document: { table: { ...props.table }, columns: props.columns.map(x => ({ ...x })),
      templateRef: props.preview.templateRef, templateVersion: props.preview.templateVersion } } }
  await submit(original)
}
async function loadLegacy() {
  if (busy.value || pending.value) return
  const started = stamp; busy.value = true
  try {
    const source = await codeGenApi.legacyMetadata(lookupId.value)
    if (!active(started)) return
    legacy.value = source; metadataId.value = source.metadataId; lookupId.value = metadataId.value; revision.value = 0
    conflict.value = false; emit('loaded', source.document); notice('已读取旧原件，核对后可明确接入。')
  } catch (cause) { if (active(started)) failure(cause) }
  finally { if (active(started)) busy.value = false }
}
async function adopt() {
  if (busy.value || pending.value || !legacy.value || conflict.value || storageError.value) return
  await submit({ metadataId: legacy.value.metadataId, command: { operationRef: crypto.randomUUID(), sourceFingerprint: legacy.value.sourceFingerprint },
    legacyDocument: legacy.value.document })
}
function send(original: Pending) {
  return 'legacyDocument' in original ? codeGenApi.adoptMetadata(original.metadataId, original.command)
    : codeGenApi.saveMetadata(original.metadataId, original.command)
}
async function submit(original: Pending) {
  const started = stamp
  try { sessionStorage.setItem(storageKey(), JSON.stringify(original)) }
  catch { storageError.value = t('无法保留原保存记录，本次尚未发送。恢复浏览器存储后刷新。'); return }
  pending.value = original; busy.value = true
  try {
    const receipt = await send(original)
    if (active(started)) accept(receipt)
  } catch (cause) {
    if (!active(started)) return
    const status = (cause as { response?: { status?: number } }).response?.status
    // Only a definitive first-attempt rejection proves this operation had no effect.
    if (status && [400, 401, 403, 404, 409, 412, 413, 422].includes(status)) {
      try { clearPending() } catch { storageError.value = t('无法清除原保存记录，请恢复浏览器存储后刷新。') }
      if (status === 412) {
        conflict.value = true
        notice('legacyDocument' in original ? '旧原件已变化，草稿已保留。请备份草稿后重新读取旧原件再接入。'
          : '版本已变化，草稿已保留。请备份草稿后显式读取最新版本再编辑。', true); return
      }
    }
    failure(cause)
  } finally { if (active(started)) busy.value = false }
}
async function recover(retry: boolean) {
  if (busy.value || !pending.value || storageError.value) return
  const started = stamp, original = pending.value; busy.value = true
  try {
    const receipt = retry ? await send(original)
      : await codeGenApi.originalMetadata(original.metadataId, original.command.operationRef)
    if (active(started)) accept(receipt)
  } catch (cause) {
    if (!active(started)) return
    // A later denial or missing result cannot disprove an earlier unknown commit.
    if (!retry && (cause as { response?: { status?: number } }).response?.status === 404)
      notice('尚未查到原结果，原记录仍保留；可按原操作重试。', true)
    else failure(cause)
  } finally { if (active(started)) busy.value = false }
}
</script>

<style scoped>
.metadata-panel { margin: 16px 0; }
.metadata-id { width: 340px; max-width: 100%; }
.metadata-panel code { overflow-wrap: anywhere; }
.metadata-panel .el-alert, .metadata-pending { margin-top: 12px; }
</style>
