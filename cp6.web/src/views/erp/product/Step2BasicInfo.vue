<template>
  <el-card shadow="never">
    <!-- 見積計算書 NO による基本情報引入 -->
    <div style="margin-bottom: 12px; display: flex; gap: 8px; align-items: center" v-if="store.canEdit">
      <span style="font-weight: 600">見積計算書 NO 引入：</span>
      <el-input
        v-model="estimateCalcNoInput"
        size="small"
        :placeholder="t('例: 00000001-01')"
        clearable
        style="width: 200px"
      />
      <el-button size="small" type="primary" :icon="Search" @click="onLoadFromEstimateCalc">引入</el-button>
      <span style="color: #909399; font-size: 12px">
        ※ 見積計算書から基本情報を読み込みます（既存の入力値は上書きされます）
      </span>
    </div>

    <el-form
      :model="store.basicInfo"
      label-width="120px"
      :disabled="store.isPageReadOnly"
      size="small"
    >
      <!-- ========== 区块 1：顧客・案件 ========== -->
      <el-divider content-position="left">{{ t('顧客・案件') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="8">
          <el-form-item :label="t('得意先 CD')" required>
            <el-input v-model="store.basicInfo.customerCd" :disabled="!store.canEdit">
              <template #append>
                <el-button :icon="Search" :disabled="!store.canEdit" @click="customerPickerVisible = true" />
              </template>
            </el-input>
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item :label="t('親子区分')">
            <el-select v-model="store.basicInfo.parentChildDiv" :disabled="!store.canEdit">
              <el-option :label="t('0=親')" value="0" />
              <el-option :label="t('1=子')" value="1" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item :label="t('セット比率')">
            <el-input-number v-model="store.basicInfo.setRatio" :min="0.0001" :precision="4" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ========== 区块 2：品名・品番 ========== -->
      <el-divider content-position="left">{{ t('品名・品番') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="8">
          <el-form-item :label="t('顧客品名 1')" required>
            <el-input v-model="store.basicInfo.customerItemName1" :disabled="!store.canEdit" maxlength="32" />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item :label="t('顧客品名 2')">
            <el-input v-model="store.basicInfo.customerItemName2" :disabled="!store.canEdit" maxlength="12" />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item :label="t('顧客品番')">
            <el-input v-model="store.basicInfo.customerPartNo" :disabled="!store.canEdit" maxlength="10" />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item :label="t('CP 品名 1')">
            <el-input v-model="store.basicInfo.cpItemName1" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item :label="t('CP 品名 2')">
            <el-input v-model="store.basicInfo.cpItemName2" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="8">
          <el-form-item :label="t('JAN コード')">
            <el-input v-model="store.basicInfo.janCode" :disabled="!store.canEdit" maxlength="13" />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ========== 区块 3：原紙構成 ========== -->
      <el-divider content-position="left">{{ t('原紙構成（表/中/裏）') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="6">
          <el-form-item :label="t('段')">
            <el-input v-model="store.basicInfo.sheetFlute" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('原紙(表)')">
            <el-input v-model="store.basicInfo.paperCdF" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('印刷(表)')">
            <el-input v-model="store.basicInfo.printCdF" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('ﾒｰｶ(表)')">
            <el-input v-model="store.basicInfo.makerCdF" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('原紙(中)')">
            <el-input v-model="store.basicInfo.paperCdC" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('印刷(中)')">
            <el-input v-model="store.basicInfo.printCdC" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('原紙(裏)')">
            <el-input v-model="store.basicInfo.paperCdB" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('印刷(裏)')">
            <el-input v-model="store.basicInfo.printCdB" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ========== 区块 4：寸法 ========== -->
      <el-divider content-position="left">{{ t('寸法') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="6">
          <el-form-item :label="t('刃渡り 巾(mm)')">
            <el-input-number v-model="store.basicInfo.bladeWidth" :precision="2" :disabled="!store.canEdit" style="width: 100%" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('刃渡り 流れ(mm)')">
            <el-input-number v-model="store.basicInfo.bladeFlow" :precision="2" :disabled="!store.canEdit" style="width: 100%" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('シート 巾(mm)')">
            <el-input-number v-model="store.basicInfo.sheetDimW" :precision="2" :disabled="!store.canEdit" style="width: 100%" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('シート 流れ(mm)')">
            <el-input-number v-model="store.basicInfo.sheetDimF" :precision="2" :disabled="!store.canEdit" style="width: 100%" />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ========== 区块 5：売価・運賃 ========== -->
      <el-divider content-position="left">{{ t('売価・運賃・単位') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="6">
          <el-form-item :label="t('売価区分')" required>
            <el-select v-model="store.basicInfo.salesPriceDiv" :disabled="!store.canEdit">
              <el-option :label="t('1=ｾｯﾄｳﾘ')" value="1" />
              <el-option :label="t('2=ﾀﾝﾋﾟﾝｳﾘ')" value="2" />
              <el-option :label="t('3=ｱｿｰﾄ')" value="3" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('運賃請求')" required>
            <el-select v-model="store.basicInfo.freightBilling" :disabled="!store.canEdit">
              <el-option :label="t('0=込み')" value="0" />
              <el-option :label="t('1=別途')" value="1" />
            </el-select>
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('数量単位')">
            <el-tooltip
              v-if="store.basicInfo.isReadOnlyByOrder"
              content="受注実績があるため変更できません"
              placement="top"
            >
              <el-input v-model="store.basicInfo.qtyUnit" disabled :placeholder="t('受注後変更不可')" />
            </el-tooltip>
            <el-input
              v-else
              v-model="store.basicInfo.qtyUnit"
              :disabled="!store.canEdit"
              :placeholder="t('例: 個 / kg')"
            />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('単価単位')">
            <el-tooltip
              v-if="store.basicInfo.isReadOnlyByOrder"
              content="受注実績があるため変更できません"
              placement="top"
            >
              <el-input v-model="store.basicInfo.unitPriceUnit" disabled />
            </el-tooltip>
            <el-input
              v-else
              v-model="store.basicInfo.unitPriceUnit"
              :disabled="!store.canEdit"
            />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ========== 区块 6：FSC・容リ法 ========== -->
      <el-divider content-position="left">{{ t('FSC・容リ法') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="6">
          <el-form-item :label="t('FSC 製品区分')">
            <el-input v-model="store.basicInfo.fscProductDiv" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('FSC 材質区分')">
            <el-input v-model="store.basicInfo.fscMaterialDiv" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('FSC 管理 NO')">
            <el-input v-model="store.basicInfo.fscManagementNo" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="6">
          <el-form-item :label="t('容リ支払')">
            <el-select v-model="store.basicInfo.recyclingPayment" :disabled="!store.canEdit" clearable>
              <el-option label="0" value="0" />
              <el-option label="1" value="1" />
              <el-option label="2" value="2" />
            </el-select>
          </el-form-item>
        </el-col>
      </el-row>
      <el-row :gutter="12">
        <el-col :span="4">
          <el-form-item :label="t('紙(g)')">
            <el-input-number v-model="store.basicInfo.paperUsageG" :precision="4" :disabled="!store.canEdit" style="width: 100%" />
          </el-form-item>
        </el-col>
        <el-col :span="4">
          <el-form-item :label="t('プラ(g)')">
            <el-input-number v-model="store.basicInfo.plasticUsageG" :precision="4" :disabled="!store.canEdit" style="width: 100%" />
          </el-form-item>
        </el-col>
        <el-col :span="4">
          <el-form-item :label="t('ガラス(g)')">
            <el-input-number v-model="store.basicInfo.glassUsageG" :precision="4" :disabled="!store.canEdit" style="width: 100%" />
          </el-form-item>
        </el-col>
        <el-col :span="4">
          <el-form-item label="PET(g)">
            <el-input-number v-model="store.basicInfo.petUsageG" :precision="4" :disabled="!store.canEdit" style="width: 100%" />
          </el-form-item>
        </el-col>
        <el-col :span="4">
          <el-form-item :label="t('梱包紙(g)')">
            <el-input-number v-model="store.basicInfo.packPaperUsageG" :precision="4" :disabled="!store.canEdit" style="width: 100%" />
          </el-form-item>
        </el-col>
        <el-col :span="4">
          <el-form-item :label="t('梱包プラ(g)')">
            <el-input-number v-model="store.basicInfo.packPlasticUsageG" :precision="4" :disabled="!store.canEdit" style="width: 100%" />
          </el-form-item>
        </el-col>
      </el-row>

      <!-- ========== 区块 7：戦略商品区分 01〜10 (Rev.5) ========== -->
      <el-divider content-position="left">{{ t('戦略商品区分（Rev.5）') }}</el-divider>
      <el-checkbox-group v-model="strategicSel" :disabled="!store.canEdit">
        <el-checkbox v-for="i in 10" :key="i" :value="i - 1">{{ String(i).padStart(2, '0') }}</el-checkbox>
      </el-checkbox-group>

      <!-- ========== 区块 8：備考 ========== -->
      <el-divider content-position="left">{{ t('備考') }}</el-divider>
      <el-row :gutter="12">
        <el-col :span="12">
          <el-form-item :label="t('印刷備考')">
            <el-input v-model="store.basicInfo.printNote" :disabled="!store.canEdit" maxlength="32" />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item :label="t('製造備考')">
            <el-input v-model="store.basicInfo.mfgNote" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item :label="t('伝票備考')">
            <el-input v-model="store.basicInfo.slipNote" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item :label="t('納入備考')">
            <el-input v-model="store.basicInfo.deliveryNote" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item :label="t('出荷備考 1')">
            <el-input v-model="store.basicInfo.shipNote1" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item :label="t('出荷備考 2')">
            <el-input v-model="store.basicInfo.shipNote2" :disabled="!store.canEdit" />
          </el-form-item>
        </el-col>
      </el-row>
    </el-form>

    <!-- ============ 共通 Master Popup ============ -->
    <MasterReferenceDialog
      v-model="customerPickerVisible"
      kind="customer"
      :initial-keyword="store.basicInfo.customerCd"
      @pick="onPickCustomer"
    />
  </el-card>
</template>

<script setup lang="ts">
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
import { computed, ref } from 'vue'
import { ElMessage } from 'element-plus'
import { Search } from '@element-plus/icons-vue'
import { useProductMasterStore } from '@/stores/productMaster'
import { productApi } from '@/api/product'
import MasterReferenceDialog from '@/components/master/MasterReferenceDialog.vue'
import type { CustomerLookupItem, ProductLookupItem } from '@/api/master'

const store = useProductMasterStore()

const estimateCalcNoInput = ref<string>('')

// ───── Master Popup ─────
const customerPickerVisible = ref(false)
function onPickCustomer(row: CustomerLookupItem | ProductLookupItem) {
  // 顧客マスタ専用のため CustomerLookupItem のみ受け付け
  const c = row as CustomerLookupItem
  if (c.customerCd === undefined) return
  store.basicInfo.customerCd = c.customerCd
  if (c.customerName) store.basicInfo.customerName = c.customerName
  store.markDirty()
}

/** 戦略商品区分 01〜10：bool[] ⇄ 选中 index 数组 */
const strategicSel = computed<number[]>({
  get() {
    return store.basicInfo.strategicDivs
      .map((v, i) => (v ? i : -1))
      .filter(i => i >= 0)
  },
  set(arr) {
    const next = Array(10).fill(false)
    arr.forEach(i => { if (i >= 0 && i < 10) next[i] = true })
    store.basicInfo.strategicDivs = next
    store.markDirty()
  },
})

async function onLoadFromEstimateCalc() {
  if (!estimateCalcNoInput.value) {
    ElMessage.warning('見積計算書 NO を入力してください')
    return
  }
  try {
    const res = await productApi.getBasicInfoByEstimateCalc(estimateCalcNoInput.value)
    if (res.code === 0 && res.data) {
      // 既存の strategicDivs / 既編集値を保護しつつマージ
      store.basicInfo = {
        ...store.basicInfo,
        ...res.data,
        strategicDivs: res.data.strategicDivs ?? store.basicInfo.strategicDivs ?? Array(10).fill(false),
      }
      store.markDirty()
      ElMessage.success('見積計算書から基本情報を引入しました')
    } else {
      ElMessage.warning(res.message || '見積計算書が見つかりません')
    }
  } catch {
    /* http interceptor toast */
  }
}

defineExpose({
  /**
   * 基本情報の必須項目バリデーション（仕様書 §第8章）
   * - 得意先 CD：必須
   * - 顧客品名 1：必須
   * - 親子区分：'0' or '1'
   * - 売価区分：'1' / '2' / '3'
   */
  validate(): boolean {
    const b = store.basicInfo
    if (!b.customerCd || !b.customerCd.trim()) {
      ElMessage.error('基本情報: 得意先 CD は必須です')
      return false
    }
    if (!b.customerItemName1 || !b.customerItemName1.trim()) {
      ElMessage.error('基本情報: 顧客品名 1 は必須です')
      return false
    }
    if (!['0', '1'].includes(b.parentChildDiv)) {
      ElMessage.error('基本情報: 親子区分の値が不正です')
      return false
    }
    if (!['1', '2', '3'].includes(b.salesPriceDiv)) {
      ElMessage.error('基本情報: 売価区分の値が不正です')
      return false
    }
    if (!(b.setRatio > 0)) {
      ElMessage.error('基本情報: セット比率は 0 より大きい数値で入力してください')
      return false
    }
    return true
  },
})
</script>

<style scoped>
:deep(.el-form-item) { margin-bottom: 8px; }
</style>
