<template>
  <div class="plate-mold">
    <router-link v-permission="'erp-plate-mold:query'" to="/erp/plate-mold-sources">版型工具原来源与观察历史</router-link>
    <!-- ヘッダー：操作種別 + 検索 -->
    <el-card shadow="never" class="header-card">
      <div class="header-row">
        <div class="header-left">
          <el-tag :type="opTagType" size="large" effect="dark">{{ opLabel }}</el-tag>
          <span v-if="store.dto.wdPtnNo" class="key">{{ t('sales.pm.no') }}: {{ store.dto.wdPtnNo }} / {{ t('sales.term.rev') }} {{ store.dto.wdRev }}</span>
          <CpTag v-else-if="store.isRegister" tone="info">{{ t('sales.status.autoNumber') }}</CpTag>
          <CpTag v-if="store.dto.status === 9" tone="ok">{{ t('sales.status.transferred') }}</CpTag>
        </div>
        <div class="header-right">
          <el-radio-group v-model="opModel" size="small" :disabled="saving || loading || !!pending">
            <el-radio-button :value="PlateMoldOperationType.Register">{{ t('sales.op.register') }}</el-radio-button>
            <el-radio-button :value="PlateMoldOperationType.Revise">{{ t('sales.op.revise') }}</el-radio-button>
            <el-radio-button :value="PlateMoldOperationType.Edit">{{ t('sales.op.edit') }}</el-radio-button>
            <el-radio-button :value="PlateMoldOperationType.Delete" :disabled="!store.canDelete">{{ t('sales.op.delete') }}</el-radio-button>
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
            <el-form-item :label="t('シート寸法 巾')"><el-input-number v-model="store.dto.sheetWidth" :disabled="!store.canEdit" :precision="4" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('シート寸法 流れ')"><el-input-number v-model="store.dto.sheetFlow" :disabled="!store.canEdit" :precision="4" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('刃渡り 巾')"><el-input-number v-model="store.dto.bladeWidth" :disabled="!store.canEdit" :precision="4" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('刃渡り 流れ')"><el-input-number v-model="store.dto.bladeFlow" :disabled="!store.canEdit" :precision="4" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('付数')"><el-input-number v-model="store.dto.compositionQty" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('個数')"><el-input-number v-model="store.dto.mfgQty" :disabled="!store.canEdit" :precision="0" :controls="false" :min="1" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('色数')"><el-input-number v-model="store.dto.colorQty" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('本数')"><el-input-number v-model="store.dto.bookQty" :disabled="!store.canEdit" :precision="0" :controls="false" :min="0" style="width: 110px" /></el-form-item>
            <el-form-item :label="t('フレキソ版厚')"><el-input-number v-model="store.dto.flexoThick" :disabled="!store.canEdit" :precision="4" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('シリンダーサイズ')"><el-input-number v-model="store.dto.cylinderSize" :disabled="!store.canEdit" :precision="4" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('sales.term.supplier')" required><el-input v-model="store.dto.supplierCd" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
            <el-form-item :label="t('加工予定先')"><el-input v-model="store.dto.processDestination" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
            <el-form-item :label="t('納期')"><el-date-picker v-model="store.dto.deliveryDate" type="date" value-format="YYYY-MM-DD" :disabled="!store.canEdit" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('入荷実績日')"><el-date-picker v-model="store.dto.arrivalActualDate" type="date" value-format="YYYY-MM-DD" :disabled="!store.canEdit" style="width: 150px" /></el-form-item>
            <el-form-item :label="t('見積価格')"><el-input-number v-model="store.dto.estimateAmount" :disabled="!store.canEdit" :precision="4" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('売上決定価格')"><el-input-number v-model="store.dto.decisionAmount" :disabled="!store.canEdit" :precision="4" :controls="false" style="width: 130px" /></el-form-item>
            <el-form-item :label="t('仕入予定金額')"><el-input-number v-model="store.dto.purchaseAmount" :disabled="!store.canEdit" :precision="4" :controls="false" style="width: 130px" /></el-form-item>
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
      <p>原记录 Rev 与传输状态独立保留；技术批准未证实，实物资格未知，PE 未执行。</p>
      <p v-if="store.dto.expectedNativeRowVersion">当前原记录版本：{{ store.dto.expectedNativeRowVersion }}；技术控制版本：{{ store.dto.expectedTechnicalControl?.version }}（{{ store.dto.expectedTechnicalControl?.knowledge === 'TECHNICAL_SPECIFICATION_DRAFT' ? '专业规格草稿，尚未批准或冻结' : '原观察，专业技术控制未证明' }}）</p>
      <p v-if="error" role="alert">{{ error }}</p><p v-if="notice" role="status">{{ notice }}</p>
      <el-button v-if="completed" :disabled="saving || loading" @click="onLoadByNo">重新载入已确认原记录</el-button>
      <div v-if="pending"><p>存在待确认操作，请先查询原结果。</p>
        <el-button :disabled="saving || loading" @click="recover(false)">查询原操作</el-button>
        <el-button :disabled="saving || loading" @click="recover(true)">结束未提交操作</el-button></div>
      <label v-if="!store.isView">操作理由 <el-input v-model="store.dto.operationReason" aria-label="操作理由" maxlength="500" /></label>
      <label v-if="store.isRegister || store.isRevise">新建原订单部门编码 <el-input v-model="store.dto.orderDepartment" aria-label="原订单部门编码" maxlength="10" /></label>
      <p v-if="store.isEdit || store.isDelete">商业变更／取消只提交原订单请求，受理后仍需原订单处理。</p>
      <div class="btn-row">
        <el-button v-if="store.isView" :icon="Document" @click="onIssueLabel">{{ t('sales.btn.label') }}</el-button>
        <el-button v-if="store.isView" :icon="Document" @click="onPurchaseOrder">{{ t('sales.btn.purchaseOrder') }}</el-button>
        <el-button v-if="store.isDelete" v-permission="'erp-plate-mold:del'" type="danger" :loading="saving" :disabled="!canSave" @click="onSave">{{ t('sales.btn.delete') }}</el-button>
        <el-button v-else-if="store.canEdit" v-permission="savePermission" type="success" :loading="saving" :disabled="!canSave" @click="onSave">{{ t('sales.btn.register') }}</el-button>
        <el-button :disabled="saving || loading || !!pending" @click="onClear">{{ t('sales.btn.clear') }}</el-button>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { Document } from '@element-plus/icons-vue'
import { usePlateMoldStore } from '@/stores/plateMold'
import { PlateMoldOperationType } from '@/types/erp/plateMold'
import { plateMoldApi } from '@/api/erp/plateMold'
import CpTag from '@/components/base/CpTag.vue'
import { usePermissionStore } from '@/stores/permission'
import { AUTH_EVENT, identityStamp, requireIdentity } from '@/auth/identity'
import { plateMoldWriterApi, type ToolWriterScope, type ToolWriteOperation, type ToolWriteKind } from '@/api/erp/plateMoldWriter'
import { pendingToolWrite, saveToolWrite, resolveToolWrite, writerResponse, type PendingToolWrite } from '@/auth/plateMoldWriteRecovery'

const { t } = useI18n()
const store = usePlateMoldStore(), route = useRoute(), permissions = usePermissionStore()
const tab = ref('basic')
const searchNo = ref('')
const searchRev = ref<number | undefined>()
const searchEstimate = ref('')
const loading = ref(false)
const saving = ref(false), error = ref(''), notice = ref(''), completed = ref(false)
const scope = ref<ToolWriterScope | null>(null), pending = ref<PendingToolWrite | null>(null)
const savePermission = computed(() => `erp-plate-mold:${store.isRegister ? 'add' : store.isDelete ? 'del' : 'edit'}`)
const canSave = computed(() => !!scope.value && !pending.value && !completed.value && !loading.value && !saving.value && !store.isView && permissions.has(savePermission.value))
let live = true, generation = 0
function guard(stamp: string, current: number) { requireIdentity(stamp); if (!live || current !== generation) throw new Error('页面已变化。') }
function clearPrivate() { store.reset(); scope.value = null; pending.value = null; notice.value = ''; completed.value = false; searchNo.value = ''; searchRev.value = undefined; searchEstimate.value = '' }
async function run(write: boolean, action: (check: () => void) => Promise<void>) {
  if (loading.value || saving.value) return
  const stamp = identityStamp(), current = generation, check = () => guard(stamp, current)
  if (write) saving.value = true; else loading.value = true
  error.value = ''
  try { await action(check); check() }
  catch (failure: any) { try { check()
    if ([401, 403].includes(failure?.response?.status)) { generation++; clearPrivate(); error.value = '当前权限无法确认，请重新载入。' }
    else error.value = failure?.response?.status === 412 ? '版本已变化，输入和原版本已保留；请查询或结束原操作，再明确重新载入。'
      : failure?.response?.status === 409 ? '原关联或操作冲突，请查询原操作；不会自动重建订单。' : failure?.message || '结果暂未确认，请查询原操作。'
  } catch { /* Ignore responses from a previous identity or route. */ } }
  finally { try { check(); if (scope.value) pending.value = pendingToolWrite(scope.value) } catch { /* Keep durable recovery IDs. */ }
    if (current === generation || !scope.value) { saving.value = false; loading.value = false } }
}
async function currentScope(check: () => void) { const value = writerResponse(await plateMoldWriterApi.scope()); check(); scope.value = value; pending.value = pendingToolWrite(value); return value }
function newDraft() { if (scope.value && store.isRegister) { store.dto.expectedNativeRowVersion = 'absent'; store.dto.expectedTechnicalControl = { ...scope.value.newTechnicalControl }; store.dto.expectedOrder = undefined; store.dto.rowVersion = undefined } }
function showOperation(value: ToolWriteOperation) {
  if (value.state === 'COMMITTED' && value.result) {
    completed.value = true; const r = value.result
    searchNo.value = r.number; searchRev.value = r.revision
    notice.value = `原操作已提交：${r.number} / Rev ${r.revision}。${r.commercialState === 'REQUESTED' ? '原订单请求已受理，尚未应用。' : '原订单已创建。'} PE 未执行。请明确重新载入后继续。`
  } else notice.value = value.state === 'CLOSED' ? '未提交操作已结束，输入和原版本保留。' : '结果仍未知，请继续查询或结束未提交操作。'
}
async function recover(close: boolean) { const current = scope.value; if (!current) return
  await run(true, async check => { const value = await resolveToolWrite(current, close, check); check(); showOperation(value) })
}

const opModel = computed({
  get: () => store.operationType,
  set: v => {
    if (pending.value || loading.value || saving.value) return
    store.setOperationType(v)
    if (v === PlateMoldOperationType.Register) { store.reset(); completed.value = false; newDraft() }
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
  if (!searchEstimate.value || pending.value || !store.isRegister) return
  const estimate = searchEstimate.value
  await run(false, async check => { await currentScope(check); if (pending.value) return
    const response = await plateMoldApi.getByEstimateCalc(estimate); check()
    if (response.code === 0 && response.data) { store.loadFromDto(response.data); completed.value = false; newDraft() }
    else error.value = t('sales.err.E10008')
  })
}
async function readExact(number: string, revision: number | undefined, check: () => void) {
  const r = await plateMoldApi.getByNo(number, revision); check()
  if (r.code !== 0 || !r.data) { error.value = t('sales.err.E10008'); return }
  if (r.data.wdPtnNo !== number || revision !== undefined && r.data.wdRev !== revision || !r.data.expectedNativeRowVersion || !r.data.expectedTechnicalControl) throw new Error('无法核对原记录版本。')
  store.loadFromDto(r.data); completed.value = false; store.history = []
  if (store.isRegister) store.setOperationType(PlateMoldOperationType.View)
  const h = await plateMoldApi.history(number); check(); if (h.code === 0) store.history = h.data || []
}
async function onLoadByNo() {
  if (!searchNo.value || pending.value) return
  const number = searchNo.value, revision = searchRev.value
  await run(false, async check => { await currentScope(check); if (!pending.value) await readExact(number, revision, check) })
}
async function onSave() {
  if (!canSave.value || !scope.value) return
  const current = scope.value, kind: ToolWriteKind = store.isRegister ? 'CREATE' : store.isRevise ? 'REVISE' : store.isDelete ? 'DELETE' : 'UPDATE'
  const dto = JSON.parse(JSON.stringify(store.dto)) as typeof store.dto
  if (!dto.operationReason?.trim() || !dto.expectedNativeRowVersion || !dto.expectedTechnicalControl || kind !== 'CREATE' && !dto.wdPtnNo) {
    error.value = '请填写操作理由，并明确载入当前原记录版本。'; return
  }
  await run(true, async check => {
    const value = await saveToolWrite(current, kind, check, id => {
      check(); if (!permissions.has(savePermission.value)) throw new Error('当前没有此操作权限。')
      dto.operationId = id
      return kind === 'CREATE' ? plateMoldApi.create(dto) : kind === 'REVISE' ? plateMoldApi.revise(dto.wdPtnNo!, dto)
        : kind === 'UPDATE' ? plateMoldApi.update(dto.wdPtnNo!, dto.wdRev, dto) : plateMoldApi.remove(dto.wdPtnNo!, dto.wdRev, dto)
    }); check(); showOperation(value)
  })
}
function onClear() { if (pending.value || loading.value || saving.value) return; store.reset(); newDraft(); completed.value = false; notice.value = ''; searchNo.value = ''; searchRev.value = undefined; searchEstimate.value = '' }
async function initialize() { await run(false, async check => {
  await currentScope(check)
  const number = typeof route.query.wdPtnNo === 'string' ? route.query.wdPtnNo : ''
  const rev = Number(route.query.rev)
  if (number && Number.isSafeInteger(rev) && rev > 0) {
    searchNo.value = number; searchRev.value = rev; store.setOperationType(PlateMoldOperationType.View)
    await readExact(number, rev, check)
  } else { store.reset(); newDraft() }
}) }
function identityChanged() { generation++; clearPrivate(); error.value = ''; loading.value = false; saving.value = false }
watch(() => [route.query.wdPtnNo, route.query.rev], () => { identityChanged(); void initialize() })
onMounted(() => { store.reset(); window.addEventListener(AUTH_EVENT, identityChanged); window.addEventListener('storage', identityChanged); void initialize() })
onBeforeUnmount(() => { live = false; generation++; clearPrivate(); window.removeEventListener(AUTH_EVENT, identityChanged); window.removeEventListener('storage', identityChanged) })

async function onIssueLabel() {
  if (!store.dto.wdPtnNo) return
  const stamp = identityStamp(), current = generation
  try {
    const blob = await plateMoldApi.issueLabel([{ wdPtnNo: store.dto.wdPtnNo, wdRev: store.dto.wdRev }]) as unknown as Blob
    guard(stamp, current)
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
