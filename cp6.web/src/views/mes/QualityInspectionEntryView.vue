<template>
  <div class="quality-inspection-entry">
    <el-card shadow="never">
      <template #header>
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <div>
            <el-tag size="large">{{ isEdit ? t('訂正') : t('新規') }}</el-tag>
            <span style="margin-left: 12px; font-weight: 600;">
              {{ t('品質検査入力') }} (MSBBME060)
              <span v-if="form.inspectionNo" style="margin-left: 12px; color: #409EFF;">{{ form.inspectionNo }}</span>
            </span>
            <el-tag v-if="form.overallResult"
              :color="getResultColor(form.overallResult)" effect="dark" size="small" style="margin-left: 12px;">
              {{ getResultLabel(form.overallResult) }}
            </el-tag>
          </div>
          <div>
            <el-button @click="$router.back()">{{ t('戻る') }}</el-button>
          </div>
        </div>
      </template>

      <!-- 基本情報 -->
      <el-form :model="form" label-width="120px" size="small">
        <el-divider content-position="left">{{ t('基本情報') }}</el-divider>
        <el-row :gutter="16">
          <el-col :span="6">
            <el-form-item :label="t('指図NO')" required>
              <el-input v-model="form.workOrderNo" :disabled="isEdit">
                <template #append>
                  <el-button :icon="Search" @click="loadWorkOrder" :disabled="!form.workOrderNo" />
                </template>
              </el-input>
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item :label="t('工程CD')">
              <el-select v-model="form.processCd" clearable filterable>
                <el-option v-for="p in workOrderProcesses" :key="p.processCd"
                  :label="`${p.processCd} - ${p.processName}`" :value="p.processCd" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item :label="t('検査日')" required>
              <el-date-picker v-model="form.inspectionDate" type="date" value-format="YYYY-MM-DD" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item :label="t('検査種類')" required>
              <el-select v-model="form.inspectionType">
                <el-option v-for="o in INSPECTION_TYPE_OPTIONS" :key="o.value" :label="t(o.label)" :value="o.value" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item :label="t('検査者CD')" required>
              <el-input v-model="form.inspectorCd" />
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item :label="t('検査者名')">
              <el-input v-model="form.inspectorName" />
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item :label="t('テンプレートCD')">
              <el-select v-model="form.templateCd" clearable filterable :placeholder="t('選択してテンプレ引入')">
                <el-option v-for="cd in templateCodes" :key="cd" :label="cd" :value="cd" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item>
              <el-button type="primary" size="small" :icon="Download" @click="loadTemplate" :disabled="!form.templateCd">{{ t('テンプレ引入') }}</el-button>
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item :label="t('検査対象数量')">
              <el-input-number v-model="form.inspectionQty" :min="0" :precision="0" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item :label="t('サンプル数')">
              <el-input-number v-model="form.sampleQty" :min="0" :precision="0" style="width: 100%;" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('製品情報')">
              <span>{{ form.productCd }} / {{ form.productName }}</span>
            </el-form-item>
          </el-col>
        </el-row>

        <!-- 検査項目一覧 -->
        <el-divider content-position="left">
          {{ t('検査項目（合格 {passCount} / 全 {itemCount}、合格率 {passRate}%）', { passCount: form.passCount, itemCount: form.itemCount, passRate: form.passRate }) }}
        </el-divider>
        <div style="margin-bottom: 8px;">
          <el-button type="primary" size="small" :icon="Plus" @click="addItem">{{ t('項目追加') }}</el-button>
          <el-button size="small" :icon="MagicStick" @click="autoJudgeAll">{{ t('全項目 自動判定') }}</el-button>
        </div>
        <el-table :data="form.items" border stripe size="small" style="width: 100%;">
          <el-table-column :label="t('順')" width="60" align="center">
            <template #default="{ row }">{{ row.itemSeqNo }}</template>
          </el-table-column>
          <el-table-column :label="t('検査項目名')" min-width="160">
            <template #default="{ row }">
              <el-input v-model="row.itemName" size="small" />
            </template>
          </el-table-column>
          <el-table-column :label="t('検査方法')" min-width="140">
            <template #default="{ row }">
              <el-input v-model="row.inspectionMethod" size="small" />
            </template>
          </el-table-column>
          <el-table-column :label="t('規格値')" width="110">
            <template #default="{ row }">
              <el-input-number v-model="row.standardValue" :precision="4" controls-position="right" size="small" style="width: 100%;" />
            </template>
          </el-table-column>
          <el-table-column :label="t('下限')" width="110">
            <template #default="{ row }">
              <el-input-number v-model="row.lowerLimit" :precision="4" controls-position="right" size="small" style="width: 100%;" />
            </template>
          </el-table-column>
          <el-table-column :label="t('上限')" width="110">
            <template #default="{ row }">
              <el-input-number v-model="row.upperLimit" :precision="4" controls-position="right" size="small" style="width: 100%;" />
            </template>
          </el-table-column>
          <el-table-column :label="t('計測値')" width="110">
            <template #default="{ row }">
              <el-input-number v-model="row.measuredValue" :precision="4" controls-position="right" size="small" style="width: 100%;" @change="judgeItem(row)" />
            </template>
          </el-table-column>
          <el-table-column :label="t('計測(文字)')" width="120">
            <template #default="{ row }">
              <el-input v-model="row.measuredText" size="small" />
            </template>
          </el-table-column>
          <el-table-column :label="t('単位')" width="80">
            <template #default="{ row }">
              <el-input v-model="row.unit" size="small" />
            </template>
          </el-table-column>
          <el-table-column :label="t('判定')" width="100" align="center">
            <template #default="{ row }">
              <el-select v-model="row.result" size="small" clearable style="width: 100%;">
                <el-option v-for="o in ITEM_RESULT_OPTIONS" :key="o.value" :label="t(o.label)" :value="o.value" />
              </el-select>
            </template>
          </el-table-column>
          <el-table-column :label="t('備考')" min-width="140">
            <template #default="{ row }">
              <el-input v-model="row.remarks" size="small" />
            </template>
          </el-table-column>
          <el-table-column :label="t('操作')" width="80" align="center">
            <template #default="{ $index }">
              <el-button link type="danger" size="small" @click="removeItem($index)">{{ t('削除') }}</el-button>
            </template>
          </el-table-column>
        </el-table>

        <!-- 総合判定 -->
        <el-divider content-position="left">{{ t('総合判定') }}</el-divider>
        <el-row :gutter="16">
          <el-col :span="6">
            <el-form-item :label="t('総合判定')">
              <el-select v-model="form.overallResult" clearable :placeholder="t('自動判定')">
                <el-option v-for="o in OVERALL_RESULT_OPTIONS" :key="o.value" :label="t(o.label)" :value="o.value" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item :label="t('処置内容')" :required="form.overallResult === 2">
              <el-select v-model="form.dispositionAction" clearable :disabled="form.overallResult !== 2">
                <el-option v-for="o in DISPOSITION_OPTIONS" :key="o.value" :label="t(o.label)" :value="o.value" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('判定理由')">
              <el-input v-model="form.judgmentReason" />
            </el-form-item>
          </el-col>
          <el-col :span="24">
            <el-form-item :label="t('備考')">
              <el-input v-model="form.remarks" type="textarea" :rows="2" />
            </el-form-item>
          </el-col>
        </el-row>

        <div style="text-align: right; margin-top: 12px;">
          <el-button type="primary" :loading="saving" @click="onSave">{{ t('保存') }}</el-button>
        </div>
      </el-form>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Search, Plus, MagicStick, Download } from '@element-plus/icons-vue'
import { qualityInspectionApi, workOrderApi } from '@/api/mes/mes'
import {
  INSPECTION_TYPE_OPTIONS,
  OVERALL_RESULT_OPTIONS,
  DISPOSITION_OPTIONS,
  ITEM_RESULT_OPTIONS,
  type QualityInspectionDto,
  type QualityInspectionItemDto,
  type WorkOrderProcessDto,
} from '@/types/mes/mes'

const route = useRoute()
const { t } = useI18n()
const saving = ref(false)
const templateCodes = ref<string[]>([])
const workOrderProcesses = ref<WorkOrderProcessDto[]>([])

const emptyForm = (): QualityInspectionDto => ({
  inspectionNo: '',
  workOrderNo: '',
  productCd: '',
  productName: '',
  processCd: '',
  processName: '',
  inspectionDate: new Date().toISOString().slice(0, 10),
  inspectorCd: '',
  inspectorName: '',
  inspectionType: 3,
  templateCd: '',
  inspectionQty: null,
  sampleQty: null,
  overallResult: null,
  dispositionAction: null,
  judgmentReason: '',
  remarks: '',
  itemCount: 0,
  passCount: 0,
  passRate: 0,
  items: [],
})

const form = reactive<QualityInspectionDto>(emptyForm())
const isEdit = computed(() => !!form.inspectionNo)

function getResultLabel(v: number | null | undefined) {
  return t(OVERALL_RESULT_OPTIONS.find(o => o.value === v)?.label ?? '')
}
function getResultColor(v: number | null | undefined) {
  return OVERALL_RESULT_OPTIONS.find(o => o.value === v)?.color ?? '#909399'
}

async function loadWorkOrder() {
  if (!form.workOrderNo) return
  const res = await workOrderApi.get(form.workOrderNo)
  if (res.code === 0 && res.data) {
    form.productCd = res.data.productCd
    form.productName = res.data.productName || ''
    workOrderProcesses.value = res.data.processes || []
    if (form.inspectionQty == null) form.inspectionQty = res.data.completedQty
  }
}

async function loadTemplateCodes() {
  const res = await qualityInspectionApi.getAllTemplateCodes()
  templateCodes.value = res.data || []
}

async function loadTemplate() {
  if (!form.templateCd) return
  const res = await qualityInspectionApi.getTemplate(form.templateCd)
  const tpl = res.data || []
  if (tpl.length === 0) {
    ElMessage.warning(t('テンプレートに項目がありません'))
    return
  }
  form.items = tpl.map(t => ({
    inspectionNo: '',
    itemSeqNo: t.itemSeqNo,
    itemName: t.itemName,
    inspectionMethod: t.inspectionMethod,
    standardValue: t.standardValue,
    upperLimit: t.upperLimit,
    lowerLimit: t.lowerLimit,
    measuredValue: null,
    measuredText: null,
    result: null,
    unit: t.unit,
    remarks: null,
  }))
  ElMessage.success(t('{cnt}件の項目を引入しました', { cnt: tpl.length }))
}

async function loadByNo(no: string) {
  const res = await qualityInspectionApi.get(no)
  if (res.code === 0 && res.data) {
    Object.assign(form, emptyForm(), res.data)
    if (form.workOrderNo) await loadWorkOrder()
  }
}

function addItem() {
  form.items.push({
    inspectionNo: '',
    itemSeqNo: (form.items.length || 0) + 1,
    itemName: '',
    inspectionMethod: '',
    standardValue: null,
    upperLimit: null,
    lowerLimit: null,
    measuredValue: null,
    measuredText: null,
    result: null,
    unit: '',
    remarks: null,
  })
}

function removeItem(i: number) {
  form.items.splice(i, 1)
  form.items.forEach((it, idx) => { it.itemSeqNo = idx + 1 })
}

function judgeItem(item: QualityInspectionItemDto) {
  if (item.upperLimit != null && item.lowerLimit != null) {
    if (item.measuredValue != null) {
      item.result = (item.measuredValue >= item.lowerLimit && item.measuredValue <= item.upperLimit) ? 1 : 2
    }
  } else if (item.standardValue != null) {
    if (item.measuredValue != null) {
      item.result = (item.measuredValue === item.standardValue) ? 1 : 2
    }
  }
  computeStats()
}

function autoJudgeAll() {
  form.items.forEach(judgeItem)
  // 総合
  if (form.items.length > 0) {
    if (form.items.some(i => i.result === 2)) form.overallResult = 2
    else if (form.items.every(i => i.result === 1)) form.overallResult = 1
    else form.overallResult = 9
  }
  computeStats()
}

function computeStats() {
  form.itemCount = form.items.length
  form.passCount = form.items.filter(i => i.result === 1).length
  form.passRate = form.itemCount > 0
    ? Math.round((form.passCount / form.itemCount) * 10000) / 100
    : 0
}

async function onSave() {
  if (!form.workOrderNo) { ElMessage.error(t('ME-MSG-020: 指図NOが未入力です')); return }
  if (!form.inspectorCd) { ElMessage.error(t('ME-MSG-021: 検査者が未入力です')); return }
  if (form.overallResult === 2 && !form.dispositionAction) {
    ElMessage.error(t('ME-MSG-023: 不合格の場合、処置内容を選択してください'))
    return
  }
  if (form.items.some(i => i.measuredValue == null && !i.measuredText)) {
    ElMessage.warning(t('ME-MSG-022: 未入力の検査項目があります（このまま保存します）'))
  }

  saving.value = true
  try {
    if (isEdit.value) {
      await qualityInspectionApi.update(form.inspectionNo, form)
      ElMessage.success(t('ME-MSG-041: 保存が完了しました'))
      await loadByNo(form.inspectionNo)
    } else {
      const res = await qualityInspectionApi.create(form)
      const no = res.data?.inspectionNo
      ElMessage.success(t('ME-MSG-041: 保存が完了しました（{no}）', { no }))
      if (no) await loadByNo(no)
    }
  } finally {
    saving.value = false
  }
}

watch(() => form.items.map(i => i.result).join(','), computeStats)

onMounted(async () => {
  await loadTemplateCodes()
  const no = route.query.no as string | undefined
  if (no) await loadByNo(no)
})
</script>

<style scoped>
.quality-inspection-entry { padding: 16px; }
</style>
