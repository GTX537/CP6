<template>
  <div class="plate-mold">
    <!-- ヘッダー：操作種別 + 検索 -->
    <el-card shadow="never" class="header-card">
      <div class="header-row">
        <div class="header-left">
          <el-tag :type="opTagType" size="large" effect="dark">{{ opLabel }}</el-tag>
          <span v-if="store.dto.wdPtnNo" class="key">{{ t('sales.pm.no') }}: {{ store.dto.wdPtnNo }} / {{ t('sales.term.rev') }} {{ store.dto.wdRev }}</span>
          <el-tag v-else-if="store.isRegister" type="info" size="small">{{ t('sales.status.autoNumber') }}</el-tag>
          <el-tag v-if="store.dto.status === 9" type="success" size="small">{{ t('sales.status.transferred') }}</el-tag>
        </div>
        <div class="header-right">
          <el-radio-group v-model="opModel" size="small">
            <el-radio-button :value="PlateMoldOperationType.Register">{{ t('sales.op.register') }}</el-radio-button>
            <el-radio-button :value="PlateMoldOperationType.Revise">{{ t('sales.op.revise') }}</el-radio-button>
            <el-radio-button :value="PlateMoldOperationType.Edit">{{ t('sales.op.edit') }}</el-radio-button>
            <el-radio-button :value="PlateMoldOperationType.Delete" :disabled="!store.isAdmin">{{ t('sales.op.delete') }}</el-radio-button>
            <el-radio-button :value="PlateMoldOperationType.View">{{ t('sales.op.view') }}</el-radio-button>
          </el-radio-group>
        </div>
      </div>
      <el-form inline size="small" label-width="100px" style="margin-top: 8px;">
        <el-form-item v-if="!store.isRegister" :label="t('sales.pm.no')">
          <el-input v-model="searchNo" style="width: 200px" />
        </el-form-item>
        <el-form-item v-if="!store.isRegister" :label="t('sales.term.rev')">
          <el-input-number v-model="searchRev" :controls="false" :min="1" style="width: 100px" />
        </el-form-item>
        <el-form-item v-if="!store.isRegister">
          <el-button type="primary" @click="onLoadByNo" :loading="loading">{{ t('sales.btn.load') }}</el-button>
        </el-form-item>
        <el-form-item v-if="store.isRegister" :label="t('sales.pm.estimateNo')">
          <el-input v-model="searchEstimate" style="width: 200px" />
        </el-form-item>
        <el-form-item v-if="store.isRegister">
          <el-button type="primary" @click="onLoadByEstimate" :loading="loading">{{ t('sales.btn.import') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- 基本情報 -->
    <el-card shadow="never">
      <el-tabs v-model="tab">
        <el-tab-pane :label="t('sales.section.basicInfo')" name="basic">
          <el-form :model="store.dto" :disabled="store.isPageReadOnly" label-width="120px" size="small" inline>
            <el-form-item :label="t('sales.term.base')" required><el-input v-model="store.dto.baseCd" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.pm.estimateNo')" required><el-input v-model="store.dto.decisionEstimateNo" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
            <el-form-item :label="t('sales.pm.name')" required>
              <el-input v-model="store.dto.vrsnName" :disabled="!store.canEditVrsnName" style="width: 240px" />
            </el-form-item>
            <el-form-item :label="t('sales.pm.salesAvailable')">
              <el-select v-model="store.dto.earningsCd" :disabled="!store.canEdit" clearable style="width: 140px">
                <el-option :label="t('可(1)')" value="1" />
                <el-option :label="t('不可(2)')" value="2" />
                <el-option :label="t('商品込(3)')" value="3" />
                <el-option :label="t('社内処理(4)')" value="4" />
                <el-option :label="t('後報(5)')" value="5" />
              </el-select>
            </el-form-item>
            <el-form-item :label="t('sales.term.haibaiNo')"><el-input v-model="store.dto.arrangeNo" disabled style="width: 200px" /></el-form-item>
            <el-form-item :label="t('sales.term.rev')"><el-input-number v-model="store.dto.wdRev" disabled :controls="false" style="width: 100px" /></el-form-item>
            <el-form-item :label="t('sales.pm.newVersion')"><el-input v-model="store.dto.newVerCd" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.pm.applyStartDate')" required><el-date-picker v-model="store.dto.stDate" type="date" value-format="YYYY-MM-DD" :disabled="!store.canEdit" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.pm.applyEndDate')"><el-date-picker v-model="store.dto.endDate" type="date" value-format="YYYY-MM-DD" :disabled="!store.canEdit" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.term.customer')" required><el-input v-model="store.dto.customerCd" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
            <el-form-item :label="t('sales.pm.repProductCd')" required><el-input v-model="store.dto.representativeProductCd" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
            <el-form-item :label="t('sales.pm.process')">
              <el-select v-model="store.dto.processCd" :disabled="!store.canEdit" clearable style="width: 160px" @change="store.autoTypeClassFromProcess">
                <el-option :label="t('フレキソ印刷(0300)')" value="0300" />
                <el-option :label="t('オフセット印刷(0450)')" value="0450" />
                <el-option :label="t('トムソン(0600)')" value="0600" />
                <el-option :label="t('箔押(0650)')" value="0650" />
              </el-select>
            </el-form-item>
            <el-form-item :label="t('sales.pm.class')"><el-input v-model="store.dto.typeClass" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.pm.endProcessLoc')"><el-input v-model="store.dto.endPlaceCd" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.pm.lastUsedActual')"><el-date-picker v-model="store.dto.achieveDate" type="date" value-format="YYYY-MM-DD" disabled style="width: 150px" /></el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane :label="t('sales.section.composition')" name="comp">
          <el-form :model="store.dto" :disabled="store.isPageReadOnly" label-width="120px" size="small" inline>
            <el-form-item :label="t('シート段')"><el-input v-model="store.dto.sheetFlute" disabled style="width: 100px" :placeholder="t('製品マスタ参照')" /></el-form-item>
            <el-form-item :label="t('原紙 表/中/裏')">
              <el-input v-model="store.dto.paperCdF" :placeholder="t('表')" disabled style="width: 100px" />
              <el-input v-model="store.dto.paperCdC" :placeholder="t('中')" disabled style="width: 100px; margin-left: 4px" />
              <el-input v-model="store.dto.paperCdB" :placeholder="t('裏')" disabled style="width: 100px; margin-left: 4px" />
            </el-form-item>
            <el-form-item :label="t('抜方向')"><el-input v-model="store.dto.extractionDirect" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('複版')"><el-checkbox v-model="store.dto.duplicatePlateFlg" :disabled="!store.canEdit">{{ t('複版あり') }}</el-checkbox></el-form-item>
            <el-form-item :label="t('シート寸法 巾')"><el-input-number v-model="store.dto.sheetWidth" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('シート寸法 流れ')"><el-input-number v-model="store.dto.sheetFlow" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('刃渡り 巾')"><el-input-number v-model="store.dto.bladeWidth" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('刃渡り 流れ')"><el-input-number v-model="store.dto.bladeFlow" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('付数')"><el-input-number v-model="store.dto.compositionQty" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('個数')"><el-input-number v-model="store.dto.mfgQty" :disabled="!store.canEdit" :precision="0" :controls="false" :min="1" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('色数')"><el-input-number v-model="store.dto.colorQty" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('本数')"><el-input-number v-model="store.dto.bookQty" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('フレキソ版厚')"><el-input-number v-model="store.dto.flexoThick" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('シリンダーサイズ')"><el-input-number v-model="store.dto.cylinderSize" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.term.supplier')" required><el-input v-model="store.dto.supplierCd" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
            <el-form-item :label="t('加工予定先')"><el-input v-model="store.dto.processDestination" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
            <el-form-item :label="t('納期')"><el-date-picker v-model="store.dto.deliveryDate" type="date" value-format="YYYY-MM-DD" :disabled="!store.canEdit" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('入荷実績日')"><el-date-picker v-model="store.dto.arrivalActualDate" type="date" value-format="YYYY-MM-DD" :disabled="!store.canEdit" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('見積価格')"><el-input-number v-model="store.dto.estimateAmount" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('売上決定価格')"><el-input-number v-model="store.dto.decisionAmount" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('仕入予定金額')"><el-input-number v-model="store.dto.purchaseAmount" :disabled="!store.canEdit" :precision="2" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('売上日')"><el-date-picker v-model="store.dto.salesDate" type="date" value-format="YYYY-MM-DD" :disabled="!store.canEdit" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('落丁型')"><el-input v-model="store.dto.strippingCd" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('置き版')"><el-input v-model="store.dto.standingCd" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('ハンマー')"><el-input v-model="store.dto.hammerCd" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('見落とし型')"><el-input v-model="store.dto.oversightCd" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.pm.location')"><el-input v-model="store.dto.placeCd" disabled style="width: 130px" :placeholder="t('システム管理')" /></el-form-item>
            <el-form-item :label="t('sales.pm.shelfLine')"><el-input v-model="store.dto.shelfLineCd" disabled style="width: 130px" :placeholder="t('システム管理')" /></el-form-item>
            <el-form-item :label="t('sales.pm.passCount')"><el-input-number v-model="store.dto.wdQty" disabled :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.pm.limitPass')"><el-input-number v-model="store.dto.limitWdQty" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.pm.dispScheduled')"><el-date-picker v-model="store.dto.dispScheduleDate" type="date" value-format="YYYY-MM-DD" :disabled="!store.canEdit" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.pm.returnScheduled')"><el-date-picker v-model="store.dto.returnScheduleDate" type="date" value-format="YYYY-MM-DD" :disabled="!store.canEdit" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('sales.pm.returnDate')"><el-date-picker v-model="store.dto.returnDate" type="date" value-format="YYYY-MM-DD" :disabled="!store.canEdit" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('返却理由')"><el-input v-model="store.dto.returnReason" :disabled="!store.canEdit" style="width: 240px" /></el-form-item>
            <el-form-item :label="t('備考')"><el-input v-model="store.dto.memo" :disabled="!store.canEdit" type="textarea" :rows="2" style="width: 480px" /></el-form-item>
            <el-form-item :label="t('売上備考')"><el-input v-model="store.dto.salesNote" :disabled="!store.canEdit" type="textarea" :rows="2" style="width: 480px" /></el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane :label="t('sales.section.attachment')" name="attach">
          <el-form :model="store.dto" :disabled="store.isPageReadOnly" label-width="120px" size="small">
            <el-row :gutter="8">
              <el-col :span="6"><el-checkbox v-model="store.dto.atachInfoSheetFront" :disabled="!store.canEdit">{{ t('表図面') }}</el-checkbox></el-col>
              <el-col :span="6"><el-checkbox v-model="store.dto.atachInfoSheetBack" :disabled="!store.canEdit">{{ t('裏図面') }}</el-checkbox></el-col>
              <el-col :span="6"><el-checkbox v-model="store.dto.atachInfoActual" :disabled="!store.canEdit">{{ t('現物') }}</el-checkbox></el-col>
              <el-col :span="6"><el-checkbox v-model="store.dto.atachInfoBaseplate" :disabled="!store.canEdit">{{ t('版下') }}</el-checkbox></el-col>
              <el-col :span="6"><el-checkbox v-model="store.dto.atachInfoPositive" :disabled="!store.canEdit">{{ t('ポジ') }}</el-checkbox></el-col>
              <el-col :span="6"><el-checkbox v-model="store.dto.atachInfoNegative" :disabled="!store.canEdit">{{ t('ネガ') }}</el-checkbox></el-col>
              <el-col :span="6"><el-checkbox v-model="store.dto.atachInfoMo" :disabled="!store.canEdit">MO</el-checkbox></el-col>
              <el-col :span="6"><el-checkbox v-model="store.dto.atachInfoFd" :disabled="!store.canEdit">FD</el-checkbox></el-col>
            </el-row>
          </el-form>
        </el-tab-pane>

        <el-tab-pane :label="t('sales.section.required')" name="need">
          <el-form :model="store.dto" :disabled="store.isPageReadOnly" label-width="120px" size="small" inline>
            <el-form-item :label="t('タタキ')"><el-input-number v-model="store.dto.needDraft" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('マイラー')"><el-input-number v-model="store.dto.needMylar" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('ゲラ刷り')"><el-input-number v-model="store.dto.needGalley" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('校正')"><el-input-number v-model="store.dto.needProof" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('青焼')"><el-input-number v-model="store.dto.needBlueprint" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('カンプ')"><el-input-number v-model="store.dto.needComp" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('印刷設計書')"><el-input-number v-model="store.dto.needDesignSheet" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('その他名称')"><el-input v-model="store.dto.needOtherName" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
            <el-form-item :label="t('その他')"><el-input v-model="store.dto.needOther" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane v-if="!store.isRegister" :label="`${t('sales.section.history')} (${store.history.length})`" name="history">
          <el-table :data="store.history" border size="small" style="width: 100%">
            <el-table-column prop="wdRev" :label="t('sales.term.rev')" width="80" align="center" />
            <el-table-column prop="stDate" :label="t('sales.pm.applyStartDate')" width="120" />
            <el-table-column prop="endDate" :label="t('sales.pm.applyEndDate')" width="120" />
            <el-table-column prop="supplierCd" :label="t('sales.term.supplier')" width="120" />
            <el-table-column prop="supplierName" :label="t('sales.term.supplier')" min-width="160" />
            <el-table-column prop="wdQty" :label="t('sales.pm.passCount')" width="100" align="right" />
            <el-table-column prop="dispScheduleDate" :label="t('sales.pm.dispScheduled')" width="120" />
            <el-table-column prop="returnScheduleDate" :label="t('sales.pm.returnScheduled')" width="120" />
            <el-table-column prop="returnDate" :label="t('sales.pm.returnDate')" width="120" />
            <el-table-column prop="returnReason" :label="t('返却理由')" min-width="160" />
          </el-table>
        </el-tab-pane>
      </el-tabs>
    </el-card>

    <el-card shadow="never" class="footer-card">
      <div class="btn-row">
        <el-button v-if="store.isView" :icon="Document" @click="onIssueLabel">{{ t('sales.btn.label') }}</el-button>
        <el-button v-if="store.isView" :icon="Document" @click="onPurchaseOrder">{{ t('sales.btn.purchaseOrder') }}</el-button>
        <el-button v-if="store.isDelete" type="danger" :loading="saving" @click="onSave">{{ t('sales.btn.delete') }}</el-button>
        <el-button v-else-if="store.canEdit" type="success" :loading="saving" @click="onSave">{{ t('sales.btn.register') }}</el-button>
        <el-button @click="onClear">{{ t('sales.btn.clear') }}</el-button>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Document } from '@element-plus/icons-vue'
import { usePlateMoldStore } from '@/stores/plateMold'
import { PlateMoldOperationType } from '@/types/erp/plateMold'
import { plateMoldApi } from '@/api/erp/plateMold'

const { t } = useI18n()
const store = usePlateMoldStore()
const tab = ref('basic')
const searchNo = ref('')
const searchRev = ref<number | undefined>()
const searchEstimate = ref('')
const loading = ref(false)
const saving = ref(false)

const opModel = computed({
  get: () => store.operationType,
  set: v => {
    store.setOperationType(v)
    if (v === PlateMoldOperationType.Register) store.reset()
  },
})
const opLabel = computed(() => ({
  [PlateMoldOperationType.Register]: t('sales.op.register'),
  [PlateMoldOperationType.Revise]: t('sales.op.revise'),
  [PlateMoldOperationType.Edit]: t('sales.op.edit'),
  [PlateMoldOperationType.Delete]: t('sales.op.delete'),
  [PlateMoldOperationType.View]: t('sales.op.view'),
}[store.operationType] ?? ''))
const opTagType = computed<'primary' | 'warning' | 'danger' | 'info' | 'success'>(() => {
  const map: Record<number, 'primary' | 'warning' | 'danger' | 'info' | 'success'> = {
    [PlateMoldOperationType.Register]: 'primary',
    [PlateMoldOperationType.Revise]: 'warning',
    [PlateMoldOperationType.Edit]: 'warning',
    [PlateMoldOperationType.Delete]: 'danger',
    [PlateMoldOperationType.View]: 'info',
  }
  return map[store.operationType] ?? 'info'
})

async function onLoadByEstimate() {
  if (!searchEstimate.value) { ElMessage.warning(t('sales.err.E10022')); return }
  loading.value = true
  try {
    const r = await plateMoldApi.getByEstimateCalc(searchEstimate.value)
    if (r.code === 0 && r.data) {
      store.loadFromDto(r.data)
      ElMessage.success(t('sales.msg.loadSuccess'))
    } else {
      ElMessage.info(t('sales.err.E10008'))
    }
  } finally { loading.value = false }
}

async function onLoadByNo() {
  if (!searchNo.value) { ElMessage.warning(t('sales.err.E10022')); return }
  loading.value = true
  try {
    const r = await plateMoldApi.getByNo(searchNo.value, searchRev.value)
    if (r.code === 0 && r.data) {
      store.loadFromDto(r.data)
      const h = await plateMoldApi.history(searchNo.value)
      if (h.code === 0 && h.data) store.history = h.data
      ElMessage.success(t('sales.msg.loadSuccess'))
    } else {
      ElMessage.info(t('sales.err.E10008'))
    }
  } finally { loading.value = false }
}

async function onSave() {
  if (store.isDelete) {
    try {
      await ElMessageBox.confirm(
        `${t('sales.msg.deleteConfirm')} (${store.dto.wdPtnNo} ${t('sales.term.rev')} ${store.dto.wdRev})`,
        t('sales.msg.confirmTitle'), { type: 'warning' }
      )
    } catch { return }
    saving.value = true
    try {
      const r = await plateMoldApi.remove(store.dto.wdPtnNo!, store.dto.wdRev, store.dto.rowVersion)
      if (r.code === 0) { ElMessage.success(t('sales.msg.deleteSuccess')); store.reset() }
    } finally { saving.value = false }
    return
  }
  saving.value = true
  try {
    if (store.isRegister) {
      const r = await plateMoldApi.create(store.dto)
      if (r.code === 0 && r.data) {
        ElMessage.success(`${t('sales.msg.saveSuccess')}: ${r.data.wdPtnNo} / ${t('sales.term.rev')} ${r.data.wdRev}`)
        // 結果を反映して訂正モードへ
        store.dto.wdPtnNo = r.data.wdPtnNo
        store.dto.wdRev = r.data.wdRev
        store.dto.arrangeNo = r.data.orderLink.arrangeNo
        store.setOperationType(PlateMoldOperationType.Edit)
      }
    } else if (store.isRevise) {
      const r = await plateMoldApi.revise(store.dto.wdPtnNo!, store.dto)
      if (r.code === 0 && r.data) {
        ElMessage.success(`${t('sales.op.revise')}: ${t('sales.term.rev')} ${r.data.wdRev}`)
        store.dto.wdRev = r.data.wdRev
        store.setOperationType(PlateMoldOperationType.Edit)
      }
    } else if (store.isEdit) {
      const r = await plateMoldApi.update(store.dto.wdPtnNo!, store.dto.wdRev, store.dto)
      if (r.code === 0) ElMessage.success(t('sales.msg.saveSuccess'))
    }
  } finally { saving.value = false }
}

function onClear() { store.reset(); searchNo.value = ''; searchRev.value = undefined; searchEstimate.value = '' }

async function onIssueLabel() {
  if (!store.dto.wdPtnNo) return
  try {
    const blob = await plateMoldApi.issueLabel([{ wdPtnNo: store.dto.wdPtnNo, wdRev: store.dto.wdRev }]) as unknown as Blob
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a'); a.href = url
    a.download = `plate-mold-label_${store.dto.wdPtnNo}_${store.dto.wdRev}.csv`; a.click()
    URL.revokeObjectURL(url)
  } catch { /* */ }
}

function onPurchaseOrder() {
  // PE 版型発注書 URL（仕様書 §12 — 環境固有）
  ElMessage.info(t('版型発注書を表示します（PE 連携 URL — 環境設定が必要）。版型: {no} / Rev {rev}', { no: store.dto.wdPtnNo, rev: store.dto.wdRev }))
}
</script>

<style scoped>
.plate-mold { padding: 16px; }
.header-card { margin-bottom: 12px; }
.header-row { display: flex; justify-content: space-between; align-items: center; }
.header-left { display: flex; gap: 12px; align-items: center; }
.key { font-weight: 600; }
.footer-card { margin-top: 12px; }
.btn-row { display: flex; gap: 8px; justify-content: flex-end; }
:deep(.el-form-item) { margin-bottom: 8px; }
</style>
