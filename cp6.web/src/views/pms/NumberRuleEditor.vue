<template>
  <section class="number-editor">
    <p v-if="error" role="alert">{{ error }}</p>
    <p v-if="notice" role="status">{{ notice }}</p>
    <template v-if="info">
      <h3>{{ info.scope.bizKey }}</h3>
      <dl>
        <dt>{{ t('编号归属') }}</dt><dd>{{ info.scope.sourceOwner }}</dd>
        <dt>{{ t('租户') }}</dt><dd>{{ info.scope.tenantId }}</dd>
        <dt>{{ t('编号唯一范围') }}</dt><dd>{{ info.scope.uniqueDomain }}</dd>
        <dt>{{ t('原分配器') }}</dt><dd>{{ info.originalAllocator }}</dd>
        <dt>{{ t('登记方式') }}</dt><dd>{{ info.scope.cutoverBasis === 'APPROVED_PUB_OWNER_HISTORY' ? t('已按批准清单切换') : t('新登记编号域') }}</dd>
        <dt>{{ t('影响范围') }}</dt><dd>{{ info.scope.businessScope }} · {{ info.scope.company || '—' }} · {{ info.scope.warehouse || '—' }}</dd>
        <dt>{{ t('当前版本') }}</dt><dd>{{ info.rule.version }} · {{ info.effectiveAtUtc || t('初始登记时生效') }}</dd>
        <dt>{{ t('已分配数量') }}</dt><dd>{{ info.allocatedCount }}</dd>
        <dt>{{ t('周期和时区') }}</dt><dd>{{ period }} · {{ info.currentPeriod || t('全部期间') }} · {{ info.rule.timeZone }}</dd>
      </dl>
      <p>{{ t('格式发布后仅影响新分配。已登记域沿用原周期和时区，历史号码保留原版本。') }}</p>
      <el-form label-position="top" :disabled="busy || !!pending">
        <el-form-item :label="t('发布版本')"><el-input v-model="draft.newVersion" :aria-label="t('发布版本')" /></el-form-item>
        <el-form-item :label="t('前缀')"><el-input v-model="draft.prefix" :aria-label="t('前缀')" /></el-form-item>
        <el-form-item :label="t('日期格式')"><el-input v-model="draft.dateFormat" :aria-label="t('日期格式')" /></el-form-item>
        <el-form-item :label="t('流水最小宽度')"><el-input-number v-model="draft.minimumWidth" :min="0" :precision="0" /></el-form-item>
      </el-form>
      <el-button :disabled="busy || !!pending || conflict" @click="preview">{{ t('预览草稿') }}</el-button>
      <el-button v-if="conflict" :disabled="busy || !!pending" @click="rebase">{{ t('以当前版本重新核对保留的草稿') }}</el-button>
      <el-button v-permission="'pub-seq:edit'" type="primary"
        :disabled="busy || !!pending || conflict || !draft.newVersion || storageFailed" @click="save">{{ t('发布格式') }}</el-button>
      <p v-if="sample">{{ t('格式示例，未占号') }} <code>{{ sample.sample }}</code> · {{ sample.timeZone }}
        · {{ sample.periodKey || t('不重置') }} · {{ sample.wouldStartPeriod ? t('本周期尚未分配') : t('本周期已有分配') }}。
        {{ t('跨周期或其他人领号后，实际号码可能变化。') }}</p>
      <div v-if="pending">
        <p>{{ t('原提交结果待确认') }} <code>{{ pending.operationRef }}</code></p>
        <el-button :disabled="busy" @click="recover(false)">{{ t('按原键查询') }}</el-button>
        <el-button v-permission="'pub-seq:edit'" :disabled="busy" @click="recover(true)">{{ t('按原请求重试') }}</el-button>
      </div>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { identityStamp } from '@/auth/identity'
import { numberOwnerApi, type NumberChange, type NumberChangeReceipt, type NumberManagement, type NumberPreview } from '@/api/pub/seq'

const props = defineProps<{ ruleId: string }>()
const { t } = useI18n()
const identity = identityStamp()
const storageKey = 'cp6_number_rule_v1:' + identity + ':' + props.ruleId
const info = ref<NumberManagement | null>(null), sample = ref<NumberPreview | null>(null)
const pending = ref<NumberChange | null>(null), error = ref(''), notice = ref('')
const busy = ref(false), conflict = ref(false), storageFailed = ref(false)
const draft = reactive({ expectedRuleVersion: '', newVersion: '', prefix: '', dateFormat: '', minimumWidth: 0 })
const period = computed(() => [t('不重置'), t('每日'), t('每月'), t('每年')][info.value?.rule.resetCycle ?? 0] || t('不重置'))
let mounted = true
const active = () => mounted && identity === identityStamp()
const status = (cause: unknown) => (cause as { response?: { status?: number } })?.response?.status
watch(draft, () => { sample.value = null }, { flush: 'sync' })
onBeforeUnmount(() => { mounted = false })

async function read(resetDraft: boolean) {
  const result = await numberOwnerApi.management(props.ruleId)
  if (!active()) return
  info.value = result
  if (resetDraft) {
    const source = pending.value ?? result.rule
    Object.assign(draft, { expectedRuleVersion: pending.value?.expectedRuleVersion ?? result.rule.version,
      newVersion: pending.value?.newVersion ?? '', prefix: source.prefix,
      dateFormat: source.dateFormat ?? '', minimumWidth: source.minimumWidth })
  }
}
onMounted(async () => {
  busy.value = true
  try {
    const stored = sessionStorage.getItem(storageKey)
    if (stored) {
      const value = JSON.parse(stored) as NumberChange
      if (!value?.operationRef || !value.expectedRuleVersion || !value.newVersion || typeof value.prefix !== 'string'
        || !Number.isInteger(value.minimumWidth)) throw new Error()
      pending.value = value
    }
  } catch { storageFailed.value = true; error.value = t('无法读取原操作记录，请保留本标签页并恢复浏览器存储。') }
  try { await read(true) }
  catch { if (active()) error.value = t('无法读取当前规则和用量。') }
  finally { if (active()) busy.value = false }
})
function formatDraft() {
  return { expectedRuleVersion: draft.expectedRuleVersion, prefix: draft.prefix,
    dateFormat: draft.dateFormat || null, minimumWidth: draft.minimumWidth }
}
async function stale() {
  conflict.value = true
  notice.value = t('版本已变化，草稿已保留。重新核对当前版本后再发布。')
  await read(false)
}
async function rebase() {
  busy.value = true
  try {
    await read(false)
    if (active() && info.value) { draft.expectedRuleVersion = info.value.rule.version; conflict.value = false }
  } catch { if (active()) error.value = t('无法读取当前版本，草稿仍保留。') }
  finally { if (active()) busy.value = false }
}
async function preview() {
  if (busy.value || pending.value || conflict.value) return
  busy.value = true; error.value = ''
  try { const value = await numberOwnerApi.preview(props.ruleId, formatDraft()); if (active()) sample.value = value }
  catch (cause) {
    if (active()) {
      if (status(cause) === 412) { conflict.value = true; notice.value = t('版本已变化，草稿已保留。') }
      else error.value = t('预览失败，请核对格式。')
    }
  } finally { if (active()) busy.value = false }
}
function clearPending() {
  try {
    sessionStorage.removeItem(storageKey)
    pending.value = null
    return true
  } catch {
    storageFailed.value = true
    error.value = t('无法清理原操作记录，请保留本标签页并恢复浏览器存储。')
    return false
  }
}
async function confirmed(result: NumberChangeReceipt, original: NumberChange) {
  const context = info.value
  if (!context || !result?.changeId || result.ruleId !== props.ruleId || result.tenantId !== context.scope.tenantId
    || result.sourceOwner !== context.scope.sourceOwner || result.operationRef !== original.operationRef
    || result.previousVersion !== original.expectedRuleVersion || result.rule?.version !== original.newVersion
    || result.rule.prefix !== original.prefix || result.rule.dateFormat !== original.dateFormat || result.rule.minimumWidth !== original.minimumWidth)
    throw new Error('NUMBER_ORIGINAL_MISMATCH')
  notice.value = t('已确认原操作发布版本') + ' ' + result.rule.version
  if (!clearPending()) return
  try { await read(true) } catch { if (active()) error.value = t('已取得原回执，当前版本和用量须重新读取。') }
}
async function save() {
  if (busy.value || pending.value || conflict.value || storageFailed.value || !info.value || !draft.newVersion) return
  const original = { ...formatDraft(), newVersion: draft.newVersion, operationRef: crypto.randomUUID() }
  try { sessionStorage.setItem(storageKey, JSON.stringify(original)) }
  catch { storageFailed.value = true; error.value = t('无法保留原操作键，本次尚未发送。'); return }
  pending.value = original; busy.value = true; error.value = ''; notice.value = ''
  try { const response = await numberOwnerApi.change(props.ruleId, original); if (active()) await confirmed(response, original) }
  catch (cause) {
    if (!active()) return
    const code = status(cause)
    if (code && [400, 401, 403, 404, 409, 412, 422].includes(code)) {
      if (!clearPending()) return
      if (code === 412) { try { await stale() } catch { if (active()) error.value = t('暂时无法读取最新版本。') } }
      else error.value = t('本次发布被拒绝，草稿已保留。')
    } else error.value = t('发布结果未知，请按原键查询。')
  } finally { if (active()) busy.value = false }
}
async function recover(retry: boolean) {
  if (busy.value || !pending.value) return
  const original = pending.value; busy.value = true; error.value = ''
  try {
    const result = retry ? await numberOwnerApi.change(props.ruleId, original)
      : await numberOwnerApi.original(props.ruleId, original.operationRef)
    if (active()) await confirmed(result, original)
  } catch { if (active()) error.value = t('尚未确认原回执，原操作键继续保留。') }
  finally { if (active()) busy.value = false }
}
</script>

<style scoped>
.number-editor { max-width: 46rem; }
.number-editor dl { display: grid; grid-template-columns: max-content 1fr; gap: .4rem 1.25rem; }
.number-editor dd { margin: 0; overflow-wrap: anywhere; }
.number-editor code { overflow-wrap: anywhere; }
</style>
