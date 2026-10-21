<template>
  <div class="wms-prod-inbound">
    <el-alert v-if="savedReceiptNo" :title="savedReceiptNo" type="success" :closable="false" />
    <el-alert v-if="intent.error.value" :title="intent.error.value" type="warning" :closable="false" />
    <el-row :gutter="12">
      <!-- 左：簡易入庫フォーム -->
      <el-col :xs="24" :md="14">
        <el-card shadow="never" class="big-card">
          <template #header>
            <div class="card-hd">
              <el-icon><Box /></el-icon>
              <span>{{ t('wms.prodIn.title') }}</span>
              <CpTag tone="ok" style="margin-left: auto">{{ t('wms.prodIn.sourceProduction') }}</CpTag>
            </div>
          </template>

          <el-form :model="form" label-width="140px" size="large" @submit.prevent>
            <!-- WO スキャン入力（フォーカス枠） -->
            <el-form-item :label="t('wms.prodIn.fld.workOrder')">
              <el-input
                ref="woInputRef" v-model="form.workOrderNo"
                :placeholder="t('wms.prodIn.msg.scanWO')"
                size="large" clearable
                @keyup.enter="onWoEnter"
              >
                <template #prefix><el-icon><Aim /></el-icon></template>
              </el-input>
            </el-form-item>

            <el-form-item :label="t('wms.common.product')" required>
              <el-row :gutter="8" style="width: 100%">
                <el-col :span="10"><el-input v-model="form.productCd" :placeholder="t('wms.common.code')" size="large" /></el-col>
                <el-col :span="14"><el-input v-model="form.productName" :placeholder="t('wms.common.name')" size="large" /></el-col>
              </el-row>
            </el-form-item>

            <el-form-item :label="t('wms.common.lot')" required>
              <el-row :gutter="8" style="width: 100%">
                <el-col :span="16"><el-input v-model="form.lotNo" size="large" /></el-col>
                <el-col :span="8"><el-button @click="genLot" size="large">{{ t('wms.prodIn.btn.autoLot') }}</el-button></el-col>
              </el-row>
            </el-form-item>

            <el-form-item :label="t('wms.prodIn.fld.qty')" required>
              <el-input-number v-model="form.receivedQty" :min="0" :precision="2"
                controls-position="right" size="large" style="width: 100%" />
            </el-form-item>

            <el-form-item :label="t('wms.prodIn.fld.quality')" required>
              <el-radio-group v-model="form.quality" size="large">
                <el-radio-button value="GOOD">{{ t('wms.prodIn.quality.good') }}</el-radio-button>
                <el-radio-button value="DEFECTIVE">{{ t('wms.prodIn.quality.defective') }}</el-radio-button>
              </el-radio-group>
            </el-form-item>

            <el-form-item :label="t('wms.common.warehouse')" required>
              <el-input v-model="form.warehouseCd" size="large">
                <template #append>
                  <span style="color: var(--cp-muted); font-size: 12px">{{ qualityWhHint }}</span>
                </template>
              </el-input>
            </el-form-item>

            <el-form-item :label="t('wms.common.location')" required>
              <el-input v-model="form.locationCd" size="large" />
            </el-form-item>

            <el-form-item :label="t('wms.common.expiryDate')">
              <el-date-picker v-model="form.expiryDate" type="date" value-format="YYYY-MM-DD" style="width: 100%" size="large" />
            </el-form-item>

            <el-form-item :label="t('wms.common.remarks')">
              <el-input v-model="form.remarks" type="textarea" :rows="2" />
            </el-form-item>

            <el-form-item>
              <el-button v-permission="'wms-inbound-receipt:post'" type="primary" size="large" style="width: 100%; font-size: 18px; height: 56px"
                @click="onConfirm" :loading="saving" :icon="Check">
                {{ t('wms.prodIn.btn.confirm') }}
              </el-button>
            </el-form-item>
          </el-form>
        </el-card>
      </el-col>

      <!-- 右：直近入庫履歴 -->
      <el-col :xs="24" :md="10">
        <el-card shadow="never">
          <template #header>
            <div class="card-hd">
              <el-icon><List /></el-icon>
              <span>{{ t('wms.prodIn.title.recent') }}</span>
              <el-button text :icon="Refresh" @click="reload" style="margin-left: auto" size="small">{{ t('wms.common.refresh') }}</el-button>
            </div>
          </template>
          <el-table :data="recent" border stripe size="small" max-height="700">
            <el-table-column prop="receiptNo" :label="t('wms.prodIn.fld.receiptNo')" width="180" />
            <el-table-column prop="workOrderNo" :label="t('wms.prodIn.fld.workOrder')" width="140" />
            <el-table-column prop="warehouseCd" :label="t('wms.common.warehouse')" width="80" />
            <el-table-column prop="receiveDateTime" :label="t('wms.prodIn.fld.receivedAt')" width="180" :formatter="formatDateTimeCell" />
          </el-table>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, nextTick, onMounted } from 'vue'
import { useOperationsIntent } from '@/composables/useOperationsIntent'
import { ElMessage } from 'element-plus'
import { Box, Aim, Check, List, Refresh } from '@element-plus/icons-vue'
import { useI18n } from 'vue-i18n'
import CpTag from '@/components/base/CpTag.vue'
import { inboundReceiptApi } from '@/api/wms/inboundReceipt'
import type { InboundReceipt } from '@/types/wms/wms'
import { formatDateTimeCell } from '@/utils/format'

const { t } = useI18n()

const woInputRef = ref()

const form = reactive({
  workOrderNo: '',
  productCd: '',
  productName: '',
  lotNo: '',
  receivedQty: 0,
  quality: 'GOOD' as 'GOOD' | 'DEFECTIVE',
  warehouseCd: 'W03', // 完成品倉庫デフォルト
  locationCd: '',
  expiryDate: undefined as string | undefined,
  remarks: '',
})

const qualityWhHint = computed(() =>
  form.quality === 'GOOD' ? t('wms.prodIn.msg.finishedWh') : t('wms.prodIn.msg.defectiveWh'))

// 良品/不良品切換で倉庫を自動切換
import { watch } from 'vue'
watch(() => form.quality, (q) => {
  form.warehouseCd = q === 'GOOD' ? 'W03' : 'W04'
})

const recent = ref<InboundReceipt[]>([])
const savedReceiptNo = ref('')
let reads = 0, revision = 0
const intent = useOperationsIntent(() => { reads++; reset(); form.quality = 'GOOD'; form.warehouseCd = 'W03'; form.locationCd = ''; recent.value = []; savedReceiptNo.value = '' })
const saving = intent.busy
watch(form, () => { revision++ }, { deep: true, flush: 'sync' })

function onWoEnter() {
  // WO 入力 → 製品コード入力欄にフォーカス移動（実 MES 連携は将来拡張）
  if (form.workOrderNo) {
    // 簡易：WO 番号からロット名サジェスト
    if (!form.lotNo) form.lotNo = `${form.workOrderNo}-${new Date().toISOString().slice(0, 10).replace(/-/g, '')}`
  }
}

function genLot() {
  const d = new Date()
  const ymd = `${d.getFullYear()}${String(d.getMonth() + 1).padStart(2, '0')}${String(d.getDate()).padStart(2, '0')}`
  const rnd = Math.floor(Math.random() * 9000) + 1000
  form.lotNo = `${form.workOrderNo || 'WO'}-${ymd}-${rnd}`
}

async function reload() {
  const token = intent.capture(), id = ++reads
  try {
    const r = await inboundReceiptApi.search({ pageSize: 20 })
    if (!intent.current(token) || id !== reads) return
    recent.value = (r.data || []).filter(x => x.sourceType === 'PRODUCTION').slice(0, 20)
  } catch { if (intent.current(token) && id === reads) recent.value = [] }
}

function reset() {
  form.workOrderNo = ''
  form.productCd = ''
  form.productName = ''
  form.lotNo = ''
  form.receivedQty = 0
  form.expiryDate = undefined
  form.remarks = ''
  nextTick(() => woInputRef.value?.focus())
}

async function onConfirm() {
  if (!form.productCd || !form.lotNo || form.receivedQty <= 0 || !form.warehouseCd || !form.locationCd) {
    ElMessage.warning(t('wms.common.required'))
    return
  }
  const token = intent.begin(); if (!token) return
  const draft = JSON.parse(JSON.stringify(form)) as typeof form, edit = revision
  try {
    const dto: InboundReceipt = {
      sourceType: 'PRODUCTION',
      workOrderNo: draft.workOrderNo || undefined,
      receiveDateTime: new Date().toISOString(),
      warehouseCd: draft.warehouseCd,
      status: 1, // Confirmed
      remarks: draft.remarks || undefined,
      details: [{
        lineNo: 1,
        productCd: draft.productCd,
        productName: draft.productName || undefined,
        lotNo: draft.lotNo,
        receivedQty: draft.receivedQty,
        locationCd: draft.locationCd,
        expiryDate: draft.expiryDate,
      }],
    }
    const res = await inboundReceiptApi.confirm(dto)
    if (!intent.current(token)) return
    savedReceiptNo.value = res.data.receiptNo
    ElMessage.success(`${t('wms.common.success')}: ${res.data.receiptNo}`)
    if (revision === edit) reset()
    await reload()
  } catch (e: any) {
    intent.failed(token, e)
  } finally { intent.finish(token) }
}

onMounted(() => {
  reload()
  nextTick(() => woInputRef.value?.focus())
})
</script>

<style scoped>
.wms-prod-inbound { padding: 16px; }
.big-card :deep(.el-card__header) { background: var(--cp-brand-bg); }
.card-hd { display: flex; align-items: center; gap: 8px; }
</style>
