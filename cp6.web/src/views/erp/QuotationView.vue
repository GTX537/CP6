<template>
  <div class="quotation-view" :class="{ 'is-mobile': isMobile }">
    <!-- ============ Top bar：操作种别 + No + 状态 ============ -->
    <el-card shadow="never" class="header-card">
      <div class="header-row">
        <div class="header-left">
          <el-tag :type="opTagType" :size="isMobile ? 'default' : 'large'" effect="dark">{{ opLabel }}</el-tag>
          <span v-if="form.qtnNo" class="qtn-no">{{ t('御見積書 No.') }} {{ form.qtnNo }}</span>
          <el-tag v-if="statusLabel" :type="statusTagType" effect="plain" :size="isMobile ? 'small' : 'large'">
            {{ statusLabel }}
          </el-tag>
        </div>
        <div class="header-right">
          <el-radio-group v-model="opModel" size="small" :disabled="!form.qtnNo && op !== 10">
            <el-radio-button :value="Op.New">{{ t('新規') }}</el-radio-button>
            <el-radio-button :value="Op.Edit" :disabled="!form.qtnNo">{{ t('訂正') }}</el-radio-button>
            <el-radio-button :value="Op.Copy" :disabled="!form.qtnNo">{{ t('流用') }}</el-radio-button>
            <el-radio-button :value="Op.View" :disabled="!form.qtnNo">{{ t('照会') }}</el-radio-button>
            <el-radio-button :value="Op.Delete" :disabled="!form.qtnNo">{{ t('削除') }}</el-radio-button>
          </el-radio-group>
        </div>
      </div>
    </el-card>

    <!-- ============ 检索区（非新规时） ============ -->
    <el-card v-if="op !== Op.New" shadow="never" class="search-card">
      <el-form inline>
        <el-form-item :label="t('御見積書No')">
          <el-input
            v-model="searchNo"
            :placeholder="t('例: Q00000001')"
            clearable
            style="width: 200px"
            @keyup.enter="loadByNo"
          />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" :loading="loading" @click="loadByNo">{{ t('読込') }}</el-button>
          <el-button @click="onNewClick">{{ t('新規') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- ============ Tabs ============ -->
    <el-card shadow="never" class="body-card">
      <el-form
        ref="formRef"
        :model="form"
        :rules="rules"
        label-width="110px"
        label-position="right"
        size="small"
        :disabled="isPageReadOnly"
      >
        <el-tabs v-model="activeTab" type="border-card" class="qtn-tabs">
          <!-- Tab1: ヘッダー ================================ -->
          <el-tab-pane :label="t('① ヘッダー / 案件')" name="header">
            <el-divider content-position="left">{{ t('拠点・担当') }}</el-divider>
            <el-row :gutter="12">
              <el-col :span="8">
                <el-form-item :label="t('拠点')" prop="baseCd" required>
                  <el-select v-model="form.baseCd" :disabled="isPkRo" clearable>
                    <el-option
                      v-for="b in bases"
                      :key="b.baseCd"
                      :value="b.baseCd"
                      :label="`${b.baseCd} ${b.baseName}`"
                    />
                  </el-select>
                </el-form-item>
              </el-col>
              <el-col :span="8">
                <el-form-item :label="t('担当者')" prop="staffCd" required>
                  <el-select
                    v-model="form.staffCd"
                    :disabled="isPkRo || !form.baseCd"
                    clearable
                  >
                    <el-option
                      v-for="s in staffs"
                      :key="s.staffCd"
                      :value="s.staffCd"
                      :label="`${s.staffCd} ${s.staffName}`"
                    />
                  </el-select>
                </el-form-item>
              </el-col>
              <el-col :span="8">
                <el-form-item :label="t('参照元No')">
                  <el-input v-model="form.refQtnNo" disabled />
                </el-form-item>
              </el-col>
            </el-row>

            <el-divider content-position="left">{{ t('顧客・案件') }}</el-divider>
            <el-row :gutter="12">
              <el-col :span="8">
                <el-form-item :label="t('顧客コード')" prop="customerCd" required>
                  <el-input v-model="form.customerCd" :disabled="isPkRo" />
                </el-form-item>
              </el-col>
              <el-col :span="16">
                <el-form-item :label="t('顧客名')">
                  <el-input v-model="form.customerName" />
                </el-form-item>
              </el-col>
              <el-col :span="8">
                <el-form-item :label="t('親案件No')">
                  <el-input v-model="form.projectNoParent" />
                </el-form-item>
              </el-col>
              <el-col :span="8">
                <el-form-item :label="t('子案件No')">
                  <el-input v-model="form.projectNoChild" />
                </el-form-item>
              </el-col>
              <el-col :span="8">
                <el-form-item :label="t('材質No')">
                  <el-input v-model="form.projectNoMaterial" />
                </el-form-item>
              </el-col>
            </el-row>

            <el-divider content-position="left">FSC</el-divider>
            <el-row :gutter="12">
              <el-col :span="8">
                <el-form-item :label="t('FSC管理No')">
                  <el-input v-model="form.fscMgmtNo" />
                </el-form-item>
              </el-col>
              <el-col :span="8">
                <el-form-item :label="t('CoC確認日')">
                  <el-date-picker
                    v-model="form.fscChecklistDate"
                    type="date"
                    value-format="YYYY-MM-DD"
                    style="width: 100%"
                  />
                </el-form-item>
              </el-col>
            </el-row>

            <el-divider content-position="left">{{ t('発行・金額') }}</el-divider>
            <el-row :gutter="12">
              <el-col :span="6">
                <el-form-item :label="t('御見積書発行日')">
                  <el-input :value="fmtDate(form.qtnIssueDate)" disabled :placeholder="t('発行後に自動設定')" />
                </el-form-item>
              </el-col>
              <el-col :span="6">
                <el-form-item :label="t('計算書発行日')">
                  <el-input :value="fmtDate(form.calcIssueDate)" disabled />
                </el-form-item>
              </el-col>
              <el-col :span="6">
                <el-form-item :label="t('合計金額')">
                  <el-input :value="fmtMoney(form.totalAmount)" disabled>
                    <template #append>{{ t('円') }}</template>
                  </el-input>
                </el-form-item>
              </el-col>
              <el-col :span="6">
                <el-form-item :label="t('合計印字')">
                  <el-switch v-model="form.printTotalFlg" />
                </el-form-item>
              </el-col>
            </el-row>
          </el-tab-pane>

          <!-- Tab2: 御見積書ヘッダー + 備考 15 =========== -->
          <el-tab-pane :label="t('② 御見積書')" name="quotation">
            <el-divider content-position="left">{{ t('御見積書ヘッダー') }}</el-divider>
            <el-row :gutter="12">
              <el-col :span="12">
                <el-form-item :label="t('担当者名')">
                  <el-input v-model="form.contactPerson" />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item :label="t('納入場所')">
                  <el-input v-model="form.deliveryLocation" />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item :label="t('納期')">
                  <el-input v-model="form.deliveryDeadline" />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item :label="t('運賃')">
                  <el-input v-model="form.freight" />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item :label="t('支払条件')">
                  <el-input v-model="form.paymentCondition" />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item :label="t('見積有効期限')">
                  <el-input v-model="form.validityPeriod" />
                </el-form-item>
              </el-col>
            </el-row>

            <el-divider content-position="left">{{ t('備考（15 行）') }}</el-divider>
            <el-row :gutter="8">
              <el-col v-for="(_, i) in 15" :key="i" :span="12">
                <el-form-item :label="`${t('備考')}${String(i + 1).padStart(2, '0')}`" label-width="80px">
                  <el-input v-model="form.qtnNotes[i]" maxlength="100" show-word-limit />
                </el-form-item>
              </el-col>
            </el-row>
          </el-tab-pane>

          <!-- Tab3: 関連見積計算書 ========================= -->
          <el-tab-pane name="calcs">
            <template #label>
              <span>{{ t('③ 関連見積計算書') }}</span>
              <el-badge v-if="form.calcs.length" :value="form.calcs.length" class="tab-badge" />
            </template>

            <div class="toolbar">
              <el-button size="small" :loading="loadingCalcs" @click="refreshCalcCandidates">
                {{ t('候補再取得') }}
              </el-button>
              <el-text type="info" size="small" style="margin-left: 8px">
                {{ t('顧客コード + 案件No（親/子/材質）を変更すると自動リフレッシュ') }}
              </el-text>
            </div>

            <el-table
              :data="calcCandidates"
              border
              stripe
              size="small"
              v-loading="loadingCalcs"
              style="margin-top: 8px"
            >
              <el-table-column :label="t('使用')" width="70" align="center">
                <template #default="{ row }">
                  <el-checkbox
                    :model-value="isLinked(row.qtnCalcNo)"
                    :disabled="isPageReadOnly || isConfirmed"
                    @change="(v: string | number | boolean) => toggleLinked(row, !!v)"
                  />
                </template>
              </el-table-column>
              <el-table-column prop="qtnCalcNo" :label="t('見積計算書No')" width="140" />
              <el-table-column prop="qtnCalcDate" :label="t('見積日')" width="110">
                <template #default="{ row }">{{ fmtDate(row.qtnCalcDate) }}</template>
              </el-table-column>
              <el-table-column prop="customerProductName1" :label="t('顧客品名1')" min-width="180" show-overflow-tooltip />
              <el-table-column prop="customerProductName2" :label="t('顧客品名2')" min-width="160" show-overflow-tooltip />
              <el-table-column prop="estimateQty" :label="t('数量')" width="100" align="right">
                <template #default="{ row }">{{ fmtNum(row.estimateQty) }}</template>
              </el-table-column>
              <el-table-column prop="confirmedUnitPrice" :label="t('単価')" width="110" align="right">
                <template #default="{ row }">{{ fmtMoney(row.confirmedUnitPrice) }}</template>
              </el-table-column>
              <el-table-column prop="unit" :label="t('単位')" width="80" />
              <el-table-column prop="amount" :label="t('金額')" width="130" align="right">
                <template #default="{ row }">{{ fmtMoney(row.amount) }}</template>
              </el-table-column>
              <el-table-column prop="qtnDiv" :label="t('区分')" width="90" />
              <el-table-column v-if="op === Op.Confirm" :label="t('確定')" width="70" align="center">
                <template #default="{ row }">
                  <el-checkbox
                    v-if="isLinked(row.qtnCalcNo)"
                    v-model="confirmSelection[row.qtnCalcNo]"
                  />
                </template>
              </el-table-column>
            </el-table>

            <el-empty
              v-if="!loadingCalcs && calcCandidates.length === 0"
              :description="t('顧客・案件を設定してください')"
              :image-size="60"
            />
          </el-tab-pane>

          <!-- Tab4: 印刷明细 ============================== -->
          <el-tab-pane name="details">
            <template #label>
              <span>{{ t('④ 印刷明細') }}</span>
              <el-badge v-if="form.details.length" :value="form.details.length" class="tab-badge" />
            </template>

            <div class="toolbar">
              <el-button size="small" type="primary" :disabled="isPageReadOnly" @click="addDetailRow">
                {{ t('+ 行追加') }}
              </el-button>
              <el-button size="small" :disabled="isPageReadOnly" @click="recalcTotalAmount">
                {{ t('合計再計算') }}
              </el-button>
            </div>

            <el-table
              :data="form.details"
              border
              stripe
              size="small"
              style="margin-top: 8px"
            >
              <el-table-column label="No" prop="detailNo" width="60" align="center" />
              <el-table-column :label="t('品名1')" min-width="200">
                <template #default="{ row }">
                  <el-input v-model="row.itemName1" :disabled="isPageReadOnly" size="small" />
                </template>
              </el-table-column>
              <el-table-column :label="t('品名2')" min-width="160">
                <template #default="{ row }">
                  <el-input v-model="row.itemName2" :disabled="isPageReadOnly" size="small" />
                </template>
              </el-table-column>
              <el-table-column :label="t('数量')" width="120" align="right">
                <template #default="{ row }">
                  <el-input-number
                    v-model="row.quantity"
                    :controls="false"
                    :precision="2"
                    :disabled="isPageReadOnly"
                    size="small"
                    style="width: 100%"
                    @change="computeRowAmount(row)"
                  />
                </template>
              </el-table-column>
              <el-table-column :label="t('単価')" width="140" align="right">
                <template #default="{ row }">
                  <el-input-number
                    v-model="row.unitPrice"
                    :controls="false"
                    :precision="2"
                    :disabled="isPageReadOnly"
                    size="small"
                    style="width: 100%"
                    @change="computeRowAmount(row)"
                  />
                </template>
              </el-table-column>
              <el-table-column :label="t('単位')" width="90">
                <template #default="{ row }">
                  <el-input v-model="row.unit" :disabled="isPageReadOnly" size="small" />
                </template>
              </el-table-column>
              <el-table-column :label="t('金額')" width="140" align="right">
                <template #default="{ row }">
                  {{ fmtMoney(row.amount) }}
                </template>
              </el-table-column>
              <el-table-column :label="t('合計印字')" width="90" align="center">
                <template #default="{ row }">
                  <el-checkbox v-model="row.printTotalFlg" :disabled="isPageReadOnly" />
                </template>
              </el-table-column>
              <el-table-column :label="t('関連見積No')" width="130">
                <template #default="{ row }">
                  <el-text v-if="row.qtnCalcNo" size="small" type="info">
                    {{ row.qtnCalcNo }}
                  </el-text>
                </template>
              </el-table-column>
              <el-table-column :label="t('操作')" width="80" align="center" fixed="right">
                <template #default="{ $index, row }">
                  <el-button
                    link
                    type="danger"
                    size="small"
                    :disabled="isPageReadOnly || !!row.qtnCalcNo"
                    @click="removeDetailRow($index)"
                  >
                    {{ t('削除') }}
                  </el-button>
                </template>
              </el-table-column>
            </el-table>

            <div v-if="form.details.length === 0" style="text-align:center; padding: 16px; color:#909399">
              {{ t('明細がありません。「使用」チェック or 「行追加」で明細を追加してください。') }}
            </div>
          </el-tab-pane>

          <!-- Tab5: 提出用計算書 ============================ -->
          <el-tab-pane :label="t('⑤ 提出用計算書')" name="submit">
            <el-row :gutter="12">
              <el-col :span="24">
                <el-form-item :label="t('寸法印字')">
                  <el-input v-model="form.dimensionPrint" />
                </el-form-item>
              </el-col>
            </el-row>
            <el-divider content-position="left">{{ t('計算書メモ（8 行）') }}</el-divider>
            <el-row :gutter="8">
              <el-col v-for="(_, i) in 8" :key="i" :span="12">
                <el-form-item :label="`${t('メモ')}${String(i + 1).padStart(2, '0')}`" label-width="80px">
                  <el-input v-model="form.calcNotes[i]" maxlength="100" show-word-limit />
                </el-form-item>
              </el-col>
            </el-row>
          </el-tab-pane>

          <!-- Tab6: メモ ============================= -->
          <el-tab-pane :label="t('⑥ メモ')" name="memo">
            <el-form-item :label="t('メモ 1')">
              <el-input v-model="form.memo1" type="textarea" :rows="3" maxlength="500" show-word-limit />
            </el-form-item>
            <el-form-item :label="t('メモ 2')">
              <el-input v-model="form.memo2" type="textarea" :rows="3" maxlength="500" show-word-limit />
            </el-form-item>
            <el-form-item :label="t('メモ 3')">
              <el-input v-model="form.memo3" type="textarea" :rows="3" maxlength="500" show-word-limit />
            </el-form-item>
          </el-tab-pane>
        </el-tabs>
      </el-form>
    </el-card>

    <!-- ============ Footer 操作按钮 ============ -->
    <el-card shadow="never" class="footer-card">
      <div class="btn-row">
        <el-button v-if="showSave" type="success" :loading="saving" @click="onSave">
          {{ t('保存') }}
        </el-button>
        <el-button
          v-if="showConfirm"
          type="warning"
          :loading="saving"
          @click="onConfirm"
        >
          {{ t('確定登録') }}
        </el-button>
        <el-button
          v-if="showCancelConfirm"
          :loading="saving"
          @click="onCancelConfirm"
        >
          {{ t('確定取消') }}
        </el-button>
        <el-button
          v-if="showIssue"
          type="primary"
          :loading="saving"
          @click="onIssue"
        >
          {{ t('発行') }}
        </el-button>
        <el-button v-if="showDelete" type="danger" :loading="saving" @click="onDelete">
          {{ t('削除') }}
        </el-button>
        <el-button @click="onClose">{{ isStandalone ? t('閉じる') : t('戻る') }}</el-button>
      </div>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { ref, reactive, computed, watch, onMounted, onBeforeUnmount, nextTick } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules } from 'element-plus'
import { quotationApi } from '@/api/erp/quotation'
import { masterApi } from '@/api/erp/master'
import type {
  QuotationDto,
  QuotationCalcCandidate,
  QuotationCalcDto,
  QuotationDetailDto,
} from '@/types/erp/quotation'
import { QuotationOperationType } from '@/types/erp/quotation'
import type { MasterBase, MasterStaff } from '@/types/erp/estimateCalc'
import type { AxiosError } from 'axios'
import { useBreakpoint } from '@/composables/useBreakpoint'
import { formatQty, formatNumber } from '@/utils/format'

const { isMobile } = useBreakpoint()
const Op = QuotationOperationType
const route = useRoute()
const router = useRouter()

// ============== State ==============
const formRef = ref<FormInstance | null>(null)
const op = ref<QuotationOperationType>(Op.New)
const opModel = computed({
  get: () => op.value,
  set: (v: QuotationOperationType) => onOpChange(v),
})
const activeTab = ref<'header' | 'quotation' | 'calcs' | 'details' | 'submit' | 'memo'>('header')
const searchNo = ref('')
const loading = ref(false)
const loadingCalcs = ref(false)
const saving = ref(false)

const bases = ref<MasterBase[]>([])
const staffs = ref<MasterStaff[]>([])

const calcCandidates = ref<QuotationCalcCandidate[]>([])
/** 確定登録モード下のチェック状態 { qtnCalcNo: boolean } */
const confirmSelection = reactive<Record<string, boolean>>({})

function emptyForm(): QuotationDto {
  return {
    baseCd: '',
    staffCd: '',
    customerCd: '',
    qtnNotes: Array(15).fill(''),
    calcNotes: Array(8).fill(''),
    printTotalFlg: true,
    estimateCheckFlg: 0,
    masterConfirmFlg: 0,
    calcs: [],
    details: [],
  }
}
const form = reactive<QuotationDto>(emptyForm())

const rules: FormRules = {
  baseCd: [{ required: true, message: t('拠点を選択してください'), trigger: 'change' }],
  staffCd: [{ required: true, message: t('担当者を選択してください'), trigger: 'change' }],
  customerCd: [{ required: true, message: t('顧客コードを入力してください'), trigger: 'blur' }],
}

// ============== Computed ==============
const isStandalone = computed(() => !!route.meta?.standalone)
const isNew = computed(() => op.value === Op.New)
const isEdit = computed(() => op.value === Op.Edit)
const isView = computed(() => op.value === Op.View)
const isDelete = computed(() => op.value === Op.Delete)
const isCopy = computed(() => op.value === Op.Copy)
const isConfirmOp = computed(() => op.value === Op.Confirm)
const isCancelConfirmOp = computed(() => op.value === Op.CancelConfirm)

const isPageReadOnly = computed(() => isView.value || isDelete.value)
/** 主键类字段：PK in Edit → RO；未采番 → disabled */
const isPkRo = computed(() => isEdit.value || isView.value || isDelete.value)

/** 承認/確定状态标签 */
const isConfirmed = computed(() => form.masterConfirmFlg >= 2)
const statusLabel = computed(() => {
  if (form.masterConfirmFlg >= 2) return t('見積確定済')
  if (form.estimateCheckFlg >= 1) return t('承認済')
  if (form.qtnNo) return t('未承認')
  return ''
})
const statusTagType = computed<'info' | 'success' | 'warning' | 'primary'>(() => {
  if (form.masterConfirmFlg >= 2) return 'success'
  if (form.estimateCheckFlg >= 1) return 'warning'
  return 'info'
})

const opLabel = computed(() => {
  switch (op.value) {
    case Op.New: return t('新規')
    case Op.Edit: return t('訂正')
    case Op.Copy: return t('流用')
    case Op.View: return t('照会')
    case Op.Delete: return t('削除')
    case Op.Confirm: return t('確定登録')
    case Op.CancelConfirm: return t('確定取消')
    default: return ''
  }
})
const opTagType = computed<'primary' | 'success' | 'warning' | 'info' | 'danger'>(() => {
  switch (op.value) {
    case Op.New: return 'primary'
    case Op.Edit: return 'warning'
    case Op.Copy: return 'success'
    case Op.View: return 'info'
    case Op.Delete: return 'danger'
    case Op.Confirm: return 'warning'
    case Op.CancelConfirm: return 'info'
    default: return 'info'
  }
})

// 按钮显隐
const showSave = computed(() => (isNew.value || isEdit.value || isCopy.value) && !isConfirmed.value)
const showDelete = computed(() => isDelete.value && !isConfirmed.value)
const showConfirm = computed(() => !!form.qtnNo && !isConfirmed.value && (isEdit.value || isConfirmOp.value))
const showCancelConfirm = computed(() => !!form.qtnNo && isConfirmed.value && (isEdit.value || isCancelConfirmOp.value))
const showIssue = computed(() => !!form.qtnNo && !isNew.value && !isCopy.value && !isDelete.value)

// ============== Helpers ==============
function fmtDate(v?: string) {
  return v ? v.slice(0, 10) : ''
}
function fmtNum(v?: number) {
  return v == null ? '' : formatQty(v)
}
function fmtMoney(v?: number) {
  return v == null ? '' : formatNumber(v, 'decimal')
}

function resetForm() {
  Object.assign(form, emptyForm())
  calcCandidates.value = []
  Object.keys(confirmSelection).forEach(k => delete confirmSelection[k])
  activeTab.value = 'header'
}

function loadForm(dto: QuotationDto) {
  // 保证数组长度
  const qtnNotes = Array.from({ length: 15 }, (_, i) => dto.qtnNotes?.[i] ?? '')
  const calcNotes = Array.from({ length: 8 }, (_, i) => dto.calcNotes?.[i] ?? '')
  Object.assign(form, {
    ...emptyForm(),
    ...dto,
    qtnNotes,
    calcNotes,
    calcs: dto.calcs ?? [],
    details: dto.details ?? [],
  })
  searchNo.value = dto.qtnNo ?? ''
}

function isLinked(qtnCalcNo: string) {
  return form.calcs.some(c => c.qtnCalcNo === qtnCalcNo)
}

/** 使用✓ 切换：同步到 form.calcs + 自动增删 details */
function toggleLinked(cand: QuotationCalcCandidate, linked: boolean) {
  if (linked) {
    if (!isLinked(cand.qtnCalcNo)) {
      const newCalc: QuotationCalcDto = {
        qtnCalcNo: cand.qtnCalcNo,
        estimateCheckFlg: 0,
        masterConfirmFlg: 0,
        qtnCalcDate: cand.qtnCalcDate,
        customerProductName1: cand.customerProductName1,
        customerProductName2: cand.customerProductName2,
        estimateQty: cand.estimateQty,
        confirmedUnitPrice: cand.confirmedUnitPrice,
        unit: cand.unit,
        amount: cand.amount,
        qtnDiv: cand.qtnDiv,
      }
      form.calcs.push(newCalc)
      // 自动生成 details 行
      const nextNo = (form.details[form.details.length - 1]?.detailNo ?? 0) + 1
      form.details.push({
        detailNo: nextNo,
        itemName1: cand.customerProductName1,
        itemName2: cand.customerProductName2,
        quantity: cand.estimateQty,
        unitPrice: cand.confirmedUnitPrice,
        unit: cand.unit,
        amount: cand.amount,
        printTotalFlg: true,
        qtnCalcNo: cand.qtnCalcNo,
      })
    }
  } else {
    form.calcs = form.calcs.filter(c => c.qtnCalcNo !== cand.qtnCalcNo)
    form.details = form.details.filter(d => d.qtnCalcNo !== cand.qtnCalcNo)
    renumberDetails()
    delete confirmSelection[cand.qtnCalcNo]
  }
  recalcTotalAmount()
}

function renumberDetails() {
  form.details.forEach((d, i) => (d.detailNo = i + 1))
}

function addDetailRow() {
  const nextNo = (form.details[form.details.length - 1]?.detailNo ?? 0) + 1
  form.details.push({
    detailNo: nextNo,
    printTotalFlg: true,
    quantity: 0,
    unitPrice: 0,
    amount: 0,
  })
}

function removeDetailRow(idx: number) {
  form.details.splice(idx, 1)
  renumberDetails()
  recalcTotalAmount()
}

function computeRowAmount(row: QuotationDetailDto) {
  const q = Number(row.quantity ?? 0)
  const p = Number(row.unitPrice ?? 0)
  row.amount = Math.round(q * p * 100) / 100
  recalcTotalAmount()
}

function recalcTotalAmount() {
  const total = form.details
    .filter(d => d.printTotalFlg)
    .reduce((sum, d) => sum + Number(d.amount ?? 0), 0)
  form.totalAmount = Math.round(total * 100) / 100
}

// ============== Loaders ==============
async function loadBases() {
  try {
    const res = await masterApi.getBases()
    bases.value = res.data ?? []
  } catch {
    /* http interceptor toast already shown */
  }
}
async function loadStaffs(baseCd?: string) {
  if (!baseCd) {
    staffs.value = []
    return
  }
  try {
    const res = await masterApi.getStaffs(baseCd)
    staffs.value = res.data ?? []
  } catch {
    /* */
  }
}

watch(() => form.baseCd, async (v, ov) => {
  await loadStaffs(v || undefined)
  // 拠点切换 → 清空之前的担当者（若不在新 staffs 列表中）
  if (v !== ov && form.staffCd && !staffs.value.some(s => s.staffCd === form.staffCd)) {
    form.staffCd = ''
  }
})

async function refreshCalcCandidates() {
  if (!form.customerCd) {
    calcCandidates.value = []
    return
  }
  try {
    loadingCalcs.value = true
    const res = await quotationApi.getCalcCandidates({
      customerCd: form.customerCd,
      projectNoParent: form.projectNoParent || undefined,
      projectNoChild: form.projectNoChild || undefined,
      projectNoMaterial: form.projectNoMaterial || undefined,
      currentQuotationNo: form.qtnNo || undefined,
    })
    calcCandidates.value = res.data ?? []
  } catch {
    calcCandidates.value = []
  } finally {
    loadingCalcs.value = false
  }
}

// 顾客/案件 变化 → 联动刷新候选
let candDebounce: number | null = null
watch(
  () => [form.customerCd, form.projectNoParent, form.projectNoChild, form.projectNoMaterial],
  () => {
    if (candDebounce) window.clearTimeout(candDebounce)
    candDebounce = window.setTimeout(() => {
      refreshCalcCandidates()
    }, 300)
  }
)

// ============== 读取 / 切换 op ==============
async function loadByNo() {
  if (!searchNo.value) {
    ElMessage.warning(t('御見積書Noを入力してください'))
    return
  }
  try {
    loading.value = true
    const res = await quotationApi.getByNo(searchNo.value, op.value === Op.View)
    if (res.code === 0) {
      loadForm(res.data)
      await nextTick()
      await refreshCalcCandidates()
      ElMessage.success(t('読込成功'))
    } else {
      ElMessage.warning(res.message)
    }
  } finally {
    loading.value = false
  }
}

async function onOpChange(target: QuotationOperationType) {
  const from = op.value
  // 流用
  if (target === Op.Copy && form.qtnNo) {
    try {
      loading.value = true
      const res = await quotationApi.copy(form.qtnNo)
      if (res.code === 0) {
        loadForm(res.data)
        op.value = Op.Edit // 流用後は編集モード扱い
        ElMessage.success(t('流用副本を生成しました'))
        await refreshCalcCandidates()
      }
    } finally {
      loading.value = false
    }
    return
  }
  // 新規：クリア
  if (target === Op.New) {
    resetForm()
  }
  // 他のモードでは No 必須
  if ((target === Op.Edit || target === Op.View || target === Op.Delete) && !form.qtnNo) {
    ElMessage.info(t('先に御見積書Noを入力して「読込」してください'))
    op.value = from
    return
  }
  op.value = target
}

function onNewClick() {
  resetForm()
  op.value = Op.New
  searchNo.value = ''
}

// ============== Save / Update ==============
async function onSave() {
  const ok = await formRef.value?.validate().catch(() => false)
  if (!ok) {
    ElMessage.warning(t('必須項目を入力してください'))
    return
  }
  try {
    saving.value = true
    if (isNew.value || isCopy.value) {
      const res = await quotationApi.create(cleanPayload())
      if (res.code === 0) {
        loadForm(res.data)
        op.value = Op.Edit
        ElMessage.success(t('登録しました'))
        notifyOpener('saved')
      }
    } else if (isEdit.value) {
      const res = await quotationApi.update(form.qtnNo!, cleanPayload())
      if (res.code === 0) {
        loadForm(res.data)
        ElMessage.success(t('更新しました'))
        notifyOpener('saved')
      }
    }
  } catch (e) {
    await handleConflict(e)
  } finally {
    saving.value = false
  }
}

async function onDelete() {
  try {
    await ElMessageBox.confirm(
      t('御見積書 {no} を論理削除します。削除後は復旧できません。よろしいですか？', { no: form.qtnNo }),
      t('削除確認'),
      { type: 'warning' }
    )
  } catch {
    return
  }
  try {
    saving.value = true
    const res = await quotationApi.remove(form.qtnNo!, form.rowVersion)
    if (res.code === 0) {
      ElMessage.success(t('削除しました'))
      notifyOpener('deleted')
      resetForm()
      op.value = Op.New
    }
  } catch (e) {
    await handleConflict(e)
  } finally {
    saving.value = false
  }
}

async function onConfirm() {
  // 确认モードに入っていない場合は切替
  if (op.value !== Op.Confirm) {
    op.value = Op.Confirm
    ElMessage.info(t('確定する見積計算書にチェックを入れてから、もう一度「確定登録」を押してください'))
    // 既に関連する行を初期にチェック
    form.calcs.forEach(c => (confirmSelection[c.qtnCalcNo] = false))
    activeTab.value = 'calcs'
    return
  }

  const picked = Object.entries(confirmSelection)
    .filter(([, v]) => v)
    .map(([k]) => k)
  if (picked.length === 0) {
    ElMessage.warning(t('確定する見積計算書を1件以上選択してください'))
    return
  }
  try {
    saving.value = true
    const res = await quotationApi.confirm(form.qtnNo!, {
      qtnCalcNos: picked,
      rowVersion: form.rowVersion,
    })
    if (res.code === 0) {
      loadForm(res.data)
      op.value = Op.Edit
      ElMessage.success(t('確定登録しました'))
      notifyOpener('saved')
    }
  } catch (e) {
    await handleConflict(e)
  } finally {
    saving.value = false
  }
}

async function onCancelConfirm() {
  try {
    await ElMessageBox.confirm(
      t('御見積書 {no} の確定を取消します。よろしいですか？', { no: form.qtnNo }),
      t('確定取消'),
      { type: 'warning' }
    )
  } catch {
    return
  }
  try {
    saving.value = true
    const res = await quotationApi.cancelConfirm(form.qtnNo!, form.rowVersion)
    if (res.code === 0) {
      loadForm(res.data)
      op.value = Op.Edit
      ElMessage.success(t('確定を取消しました'))
      notifyOpener('saved')
    }
  } catch (e) {
    await handleConflict(e)
  } finally {
    saving.value = false
  }
}

async function onIssue() {
  try {
    const { value: choices } = await ElMessageBox.prompt(
      t('発行する帳票を選択してください'),
      t('発行'),
      {
        inputType: 'text',
        inputValue: 'Q,SC,C',
        inputPlaceholder: t('Q=御見積書 / SC=提出用計算書 / C=計算書（カンマ区切り）'),
        confirmButtonText: t('発行'),
        cancelButtonText: t('キャンセル'),
      }
    )
    const set = new Set(String(choices).split(',').map(s => s.trim().toUpperCase()))
    saving.value = true
    const res = await quotationApi.issue(form.qtnNo!, {
      issueQuotation: set.has('Q'),
      issueSubmitCalc: set.has('SC'),
      issueCalc: set.has('C'),
    })
    if (res.code === 0) {
      ElMessage.success(t('発行しました：{files}', { files: res.data.files.join(' , ') || '—' }))
      // 発行日が更新されるので再読込
      if (form.qtnNo) {
        const r = await quotationApi.getByNo(form.qtnNo)
        if (r.code === 0) loadForm(r.data)
      }
    }
  } catch {
    /* ユーザーキャンセル or 409 */
  } finally {
    saving.value = false
  }
}

// ============== 409 乐观锁冲突处理 ==============
async function handleConflict(err: unknown): Promise<boolean> {
  const axErr = err as AxiosError<{ message?: string; msgId?: string }>
  if (axErr?.response?.status !== 409) return false
  const body = axErr.response?.data
  try {
    await ElMessageBox({
      title: t('排他制御エラー'),
      message: `[${body?.msgId ?? 'MSG-W10002'}] ${body?.message ?? t('他のユーザが更新しました')}`,
      type: 'warning',
      confirmButtonText: t('最新版を取得'),
      cancelButtonText: t('キャンセル'),
      showCancelButton: true,
    })
    if (form.qtnNo) {
      const r = await quotationApi.getByNo(form.qtnNo)
      if (r.code === 0) {
        loadForm(r.data)
        op.value = Op.Edit
        ElMessage.success(t('最新データを取得しました。もう一度保存してください'))
      }
    }
  } catch {
    /* cancel */
  }
  return true
}

// 保存時のペイロード整形
function cleanPayload(): QuotationDto {
  // 去除只读 JOIN 展示字段（calcs 中）以免被后端当作更新目标
  const calcs = form.calcs.map(c => ({
    qtnCalcNo: c.qtnCalcNo,
    estimateCheckFlg: c.estimateCheckFlg,
    estimateCheckDate: c.estimateCheckDate,
    masterConfirmFlg: c.masterConfirmFlg,
    masterConfirmDate: c.masterConfirmDate,
  }))
  return { ...form, calcs } as QuotationDto
}

// ============== popup / 返回 ==============
function notifyOpener(type: 'saved' | 'deleted') {
  try {
    if (window.opener && !window.opener.closed) {
      window.opener.postMessage(
        { source: 'cp6-quotation', type },
        window.location.origin
      )
    }
  } catch {
    /* ignore */
  }
}

function onClose() {
  if (isStandalone.value) {
    window.close()
  } else {
    router.back()
  }
}

// ============== Mount：URL 驱动 ==============
onMounted(async () => {
  await loadBases()

  const opParam = String(route.query.op || '').toLowerCase()
  const noParam = route.query.no ? String(route.query.no) : ''
  const opMap: Record<string, QuotationOperationType> = {
    new: Op.New,
    edit: Op.Edit,
    view: Op.View,
    delete: Op.Delete,
    copy: Op.Copy,
    confirm: Op.Confirm,
  }
  const target = opMap[opParam] ?? Op.New

  if (target === Op.New) {
    op.value = Op.New
    return
  }

  if (!noParam) {
    op.value = Op.New
    return
  }

  if (target === Op.Copy) {
    try {
      loading.value = true
      const res = await quotationApi.copy(noParam)
      if (res.code === 0) {
        loadForm(res.data)
        op.value = Op.Edit
        await refreshCalcCandidates()
      }
    } finally {
      loading.value = false
    }
  } else {
    try {
      loading.value = true
      const res = await quotationApi.getByNo(noParam, target === Op.View)
      if (res.code === 0) {
        loadForm(res.data)
        op.value = target
        await refreshCalcCandidates()
      } else {
        ElMessage.warning(res.message || t('No={no} が未登録です', { no: noParam }))
      }
    } finally {
      loading.value = false
    }
  }

  if (isStandalone.value) {
    const labels: Record<number, string> = {
      [Op.New]: t('新規'),
      [Op.Edit]: t('訂正'),
      [Op.View]: t('照会'),
      [Op.Delete]: t('削除'),
      [Op.Copy]: t('流用'),
      [Op.Confirm]: t('確定登録'),
    }
    document.title = `${t('御見積書')} - ${labels[op.value] ?? ''}${noParam ? ` - ${noParam}` : ''}`
  }
})

onBeforeUnmount(() => {
  if (candDebounce) window.clearTimeout(candDebounce)
})
</script>

<style scoped>
.quotation-view {
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.header-card :deep(.el-card__body),
.search-card :deep(.el-card__body),
.body-card :deep(.el-card__body),
.footer-card :deep(.el-card__body) {
  padding: 12px 16px;
}
.header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.header-left {
  display: flex;
  align-items: center;
  gap: 12px;
}
.qtn-no {
  font-size: 16px;
  font-weight: 600;
  color: #409eff;
}
.btn-row {
  display: flex;
  gap: 8px;
  justify-content: flex-end;
}
.qtn-tabs :deep(.el-tabs__content) {
  padding: 12px 4px;
}
.tab-badge {
  margin-left: 6px;
  vertical-align: top;
}
.toolbar {
  display: flex;
  align-items: center;
  margin-bottom: 4px;
}

/* ============ 手机端优化 ============ */
.quotation-view.is-mobile {
  padding: 10px;
  padding-bottom: 80px;
  gap: 10px;
}
.quotation-view.is-mobile .header-card :deep(.el-card__body),
.quotation-view.is-mobile .search-card :deep(.el-card__body),
.quotation-view.is-mobile .body-card :deep(.el-card__body) {
  padding: 10px 12px;
}
.quotation-view.is-mobile .header-row {
  flex-direction: column;
  align-items: stretch;
  gap: 8px;
}
.quotation-view.is-mobile .header-left {
  font-size: 13px;
  gap: 6px;
  flex-wrap: wrap;
}
.quotation-view.is-mobile .qtn-no {
  font-size: 14px;
}
.quotation-view.is-mobile .header-right :deep(.el-radio-group) {
  display: flex;
  width: 100%;
}
.quotation-view.is-mobile .header-right :deep(.el-radio-button) {
  flex: 1;
}
.quotation-view.is-mobile .header-right :deep(.el-radio-button__inner) {
  width: 100%;
  padding: 8px 2px;
  font-size: 12px;
}
.quotation-view.is-mobile .footer-card {
  position: fixed;
  bottom: 0;
  left: 0;
  right: 0;
  margin: 0;
  border-radius: 0;
  z-index: 50;
  box-shadow: 0 -2px 8px rgba(0,0,0,0.08);
  padding-bottom: env(safe-area-inset-bottom, 0);
}
.quotation-view.is-mobile .footer-card :deep(.el-card__body) {
  padding: 10px 12px;
}
.quotation-view.is-mobile .btn-row {
  justify-content: stretch;
  flex-wrap: wrap;
  gap: 6px;
}
.quotation-view.is-mobile .btn-row :deep(.el-button) {
  flex: 1 1 auto;
  margin-left: 0 !important;
  min-width: 0;
  padding: 8px 6px;
}
/* tabs 在手机端紧凑 */
.quotation-view.is-mobile :deep(.el-tabs__item) {
  padding: 0 10px;
  font-size: 13px;
}
</style>
