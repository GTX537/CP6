<!--
  Phase 6 受注取消ダイアログ
  - 1段階：探查 (force=false) — DB 不变、列出关联 WO/Outbound 现状
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
              <el-tag v-if="row.autoCancellable" type="success" size="small">
                {{ t('sales.cancel.yes') }}
              </el-tag>
              <el-tag v-else type="danger" size="small">
                {{ t('sales.cancel.no_') }}
              </el-tag>
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
              <el-tag v-if="row.autoCancellable" type="success" size="small">
                {{ t('sales.cancel.yes') }}
              </el-tag>
              <el-tag v-else type="danger" size="small">
                {{ t('sales.cancel.no_') }}
              </el-tag>
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
          :disabled="!reason.trim()"
          @click="onProbe"
        >
          {{ t('sales.cancel.btnProbe') }}
        </el-button>

        <el-button
          v-if="step === 'decision'"
          type="danger"
          :loading="loading"
          @click="onForceConfirm"
        >
          {{ t('sales.cancel.btnForce') }}
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { orderApi } from '@/api/order'
import type { OrderCancelResult } from '@/types/order'

const { t } = useI18n()

const props = defineProps<{
  modelValue: boolean
  webOrderNo: string
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', v: boolean): void
  (e: 'cancelled'): void
}>()

const visible = ref(false)
const step = ref<'input' | 'decision' | 'done'>('input')
const reason = ref('')
const loading = ref(false)
const result = ref<OrderCancelResult | null>(null)

watch(() => props.modelValue, v => (visible.value = v))
watch(visible, v => emit('update:modelValue', v))

function reset() {
  step.value = 'input'
  reason.value = ''
  result.value = null
  loading.value = false
}

async function onProbe() {
  if (!reason.value.trim()) return
  loading.value = true
  try {
    const res = await orderApi.cancel(props.webOrderNo, reason.value.trim(), false)
    if (res.code !== 0 || !res.data) {
      ElMessage.error(res.message || 'Unknown error')
      return
    }
    result.value = res.data
    if (res.data.outcome === 'Cancelled' || res.data.outcome === 'PartiallyCancelled') {
      step.value = 'done'
      emit('cancelled')
    } else if (res.data.outcome === 'NeedsDecision') {
      step.value = 'decision'
    } else {
      // Rejected
      step.value = 'done'
    }
  } catch (e: any) {
    ElMessage.error(e?.message ?? 'Network error')
  } finally {
    loading.value = false
  }
}

async function onForceConfirm() {
  loading.value = true
  try {
    const res = await orderApi.cancel(props.webOrderNo, reason.value.trim(), true)
    if (res.code !== 0 || !res.data) {
      ElMessage.error(res.message || 'Unknown error')
      return
    }
    result.value = res.data
    step.value = 'done'
    emit('cancelled')
  } catch (e: any) {
    ElMessage.error(e?.message ?? 'Network error')
  } finally {
    loading.value = false
  }
}

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
