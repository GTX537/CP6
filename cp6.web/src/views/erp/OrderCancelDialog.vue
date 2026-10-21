<!--
  Phase 6 受注取消ダイアログ
  - 1段階：force=false — 全部可取消时直接执行；有半路状态时返回确认列表
  - 2段階：半路状态 → 营业看了再决定 force=true 强制取消
-->
<template>
  <el-dialog
    v-model="visible"
    :title="`${t('sales.cancel.title')} — ${webOrderNo}`"
    width="640px"
    :close-on-click-modal="false"
    :close-on-press-escape="!loading"
    @close="reset"
  >
    <!-- Step 1: 入力 reason -->
    <div v-if="step === 'input'">
      <el-alert
        :title="t('sales.cancel.warningTitle')"
        type="warning"
        :description="t('sales.cancel.warningDesc')"
        show-icon
        :closable="false"
        style="margin-bottom: 14px"
      />
      <el-form label-position="top">
        <el-form-item :label="t('sales.cancel.reasonLabel')" required>
          <el-input
            v-model="reason"
            type="textarea"
            :rows="3"
            maxlength="200"
            show-word-limit
            :placeholder="t('sales.cancel.reasonPlaceholder')"
          />
        </el-form-item>
      </el-form>
    </div>

    <!-- Step 2: 探查结果（半路状态） -->
    <div v-else-if="step === 'decision'">
      <el-alert
        :title="t('sales.cancel.needsDecisionTitle')"
        type="warning"
        :description="result?.message ?? ''"
        show-icon
        :closable="false"
        style="margin-bottom: 12px"
      />

      <div v-if="result?.relatedWorkOrders.length">
        <h4 style="margin: 8px 0">{{ t('sales.cancel.relatedWO') }}</h4>
        <el-table :data="result.relatedWorkOrders" size="small" border>
          <el-table-column prop="workOrderNo" :label="t('sales.cancel.no')" />
          <el-table-column prop="status" :label="t('sales.cancel.status')" width="100" align="center">
            <template #default="{ row }">
              {{ woStatusLabel(row.status) }}
            </template>
          </el-table-column>
          <el-table-column :label="t('sales.cancel.autoCancellable')" width="120" align="center">
            <template #default="{ row }">
              <CpTag v-if="row.autoCancellable" tone="ok">{{ t('sales.cancel.yes') }}</CpTag>
              <CpTag v-else tone="danger">{{ t('sales.cancel.no_') }}</CpTag>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <div v-if="result?.relatedOutbounds.length" style="margin-top: 10px">
        <h4 style="margin: 8px 0">{{ t('sales.cancel.relatedOutbound') }}</h4>
        <el-table :data="result.relatedOutbounds" size="small" border>
          <el-table-column prop="outboundNo" :label="t('sales.cancel.no')" />
          <el-table-column prop="status" :label="t('sales.cancel.status')" width="100" align="center">
            <template #default="{ row }">
              {{ outboundStatusLabel(row.status) }}
            </template>
          </el-table-column>
          <el-table-column :label="t('sales.cancel.autoCancellable')" width="120" align="center">
            <template #default="{ row }">
              <CpTag v-if="row.autoCancellable" tone="ok">{{ t('sales.cancel.yes') }}</CpTag>
              <CpTag v-else tone="danger">{{ t('sales.cancel.no_') }}</CpTag>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </div>

    <!-- Step 3: 完了結果 -->
    <div v-else-if="step === 'done'">
      <el-result
        :icon="result?.outcome === 'Cancelled' ? 'success' : 'warning'"
        :title="t(`sales.cancel.outcome.${result?.outcome}`)"
        :sub-title="result?.message ?? ''"
      />
    </div>

    <template #footer>
      <div>
        <el-button @click="visible = false" :disabled="loading">
          {{ t(step === 'done' ? 'sales.btn.close' : 'sales.btn.cancel') }}
        </el-button>

        <el-button
          v-if="step === 'input'"
          type="primary"
          :loading="loading"
          :disabled="loading || uncertain || !reason.trim()"
          @click="onProbe"
        >
          {{ t('sales.cancel.btnProbe') }}
        </el-button>

        <el-button
          v-if="step === 'decision'"
          type="danger"
          :loading="loading"
          :disabled="loading || uncertain"
          @click="onForceConfirm"
        >
          {{ t('sales.cancel.btnForce') }}
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { orderApi } from '@/api/erp/order'
import type { OrderCancelResult } from '@/types/erp/order'
import CpTag from '@/components/base/CpTag.vue'

const { t } = useI18n()

const props = defineProps<{
  modelValue: boolean
  webOrderNo: string
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', v: boolean): void
  (e: 'cancelled'): void
}>()

// modelValue から初期化：呼出元が v-if でマウント（modelValue 既に true）した場合でも
// 非 immediate watch では visible が false のまま開かない回帰を防ぐ。
const visible = ref(props.modelValue)
const step = ref<'input' | 'decision' | 'done'>('input')
const reason = ref('')
const loading = ref(false)
const result = ref<OrderCancelResult | null>(null)

const uncertain = ref(false)
let generation = 0, pageIdentity = identityStamp()
type CancelIntent = { number: string; reason: string; identity: string }
let decision: CancelIntent | null = null

function reset() {
  generation++
  step.value = 'input'; reason.value = ''; result.value = null; loading.value = false
  decision = null; uncertain.value = false
}
watch(() => props.modelValue, v => { if (v !== visible.value) reset(); visible.value = v }, { flush: 'sync' })
watch(() => props.webOrderNo, reset, { flush: 'sync' })
watch(visible, v => { if (!v) reset(); emit('update:modelValue', v) })
function identityChanged() {
  if (pageIdentity === identityStamp()) return
  pageIdentity = identityStamp(); reset(); visible.value = false
}
window.addEventListener(AUTH_EVENT, identityChanged)
window.addEventListener('storage', identityChanged)
onBeforeUnmount(() => {
  generation++
  window.removeEventListener(AUTH_EVENT, identityChanged)
  window.removeEventListener('storage', identityChanged)
})

async function submit(force: boolean) {
  if (loading.value || uncertain.value || !visible.value) return
  if (force && (step.value !== 'decision' || !decision)) return
  if (!force && (step.value !== 'input' || !reason.value.trim())) return
  const intent = force ? decision! : { number: props.webOrderNo, reason: reason.value.trim(), identity: identityStamp() }
  if (!intent.number || intent.number !== props.webOrderNo || intent.identity !== identityStamp()) { reset(); return }
  loading.value = true
  const id = ++generation
  const current = () => id === generation && visible.value && intent.number === props.webOrderNo && intent.identity === identityStamp()
  try {
    const res = await orderApi.cancel(intent.number, intent.reason, force)
    if (!current()) return
    if (res.code !== 0 || !res.data) { ElMessage.error(res.message || 'Unknown error'); return }
    result.value = res.data
    if (res.data.outcome === 'NeedsDecision') { decision = intent; step.value = 'decision' }
    else {
      decision = null; step.value = 'done'
      if (res.data.outcome === 'Cancelled' || res.data.outcome === 'PartiallyCancelled') emit('cancelled')
    }
  } catch (error: any) {
    if (!current()) return
    const code = String(error?.response?.data?.message ?? error?.message ?? '')
    // No automatic or repeated submit after an uncertain transport/commit result.
    uncertain.value = !error?.response || code.includes('ORDER_CANCEL_OUTCOME_UNKNOWN')
    decision = null; result.value = null; step.value = 'input'
    ElMessage.error(uncertain.value ? '取消结果尚未确认，请关闭并刷新订单状态后再操作。' : code || '取消未完成')
  } finally {
    if (id === generation) loading.value = false
  }
}
async function onProbe() { await submit(false) }
async function onForceConfirm() { await submit(true) }

// 状态码 → ラベル（暫定 inline、後で i18n key にしてもよい）
function woStatusLabel(s: number): string {
  switch (s) {
    case 0: return t('sales.cancel.wo.draft')
    case 1: return t('sales.cancel.wo.confirmed')
    case 2: return t('sales.cancel.wo.issued')
    case 3: return t('sales.cancel.wo.inProgress')
    case 4: return t('sales.cancel.wo.completed')
    case 9: return t('sales.cancel.wo.cancelled')
    default: return String(s)
  }
}
function outboundStatusLabel(s: number): string {
  switch (s) {
    case 0: return t('sales.cancel.ob.draft')
    case 1: return t('sales.cancel.ob.confirmed')
    case 2: return t('sales.cancel.ob.allocated')
    case 3: return t('sales.cancel.ob.picking')
    case 4: return t('sales.cancel.ob.completed')
    case 9: return t('sales.cancel.ob.cancelled')
    default: return String(s)
  }
}
</script>
