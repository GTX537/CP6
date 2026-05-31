<template>
  <el-form :model="store.bp" label-width="120px" size="small" :disabled="store.isPageReadOnly" inline>
    <el-divider content-position="left">取引先基本情報</el-divider>
    <el-form-item label="取引先略称"><el-input v-model="store.bp.bpAbbrev" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="標準企業コード"><el-input v-model="store.bp.stdCoCd" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="法人番号"><el-input v-model="store.bp.ein" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="法人番号区分">
      <el-select v-model="store.bp.einType" :disabled="!store.canEdit" clearable style="width: 140px">
        <el-option label="法人(1)" value="1" /><el-option label="個人(2)" value="2" />
      </el-select>
    </el-form-item>
    <el-form-item label="地方公共団体CD"><el-input v-model="store.bp.localPublicCd" :disabled="!store.canEdit" style="width: 140px" /></el-form-item>
    <el-form-item label="でんさい番号"><el-input v-model="store.bp.denzaiNo" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>

    <el-form-item label="郵便番号"><el-input v-model="store.bp.zipCd" :disabled="!store.canEdit" placeholder="123-4567" style="width: 160px" /></el-form-item>
    <el-form-item label="都道府県"><el-input v-model="store.bp.addr1" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="市区町村"><el-input v-model="store.bp.addr2" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="町域・番地"><el-input v-model="store.bp.addr3" :disabled="!store.canEdit" style="width: 240px" /></el-form-item>
    <el-form-item label="建物など"><el-input v-model="store.bp.addr4" :disabled="!store.canEdit" style="width: 240px" /></el-form-item>
    <el-form-item label="TEL"><el-input v-model="store.bp.tel" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="FAX"><el-input v-model="store.bp.fax" :disabled="!store.canEdit" style="width: 200px" /></el-form-item>
    <el-form-item label="地域CD"><el-input v-model="store.bp.areaCd" :disabled="!store.canEdit" style="width: 100px" /></el-form-item>
    <el-form-item label="営業担当"><el-input v-model="store.bp.salesStaffCd" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>
    <el-form-item label="業務担当"><el-input v-model="store.bp.businessStaffCd" :disabled="!store.canEdit" style="width: 160px" /></el-form-item>

    <el-divider content-position="left">9 個の属性 FLG（Tab 表示制御）</el-divider>
    <div class="flg-grid">
      <el-checkbox v-model="store.bp.customerFlg" :disabled="!flgEditable">得意先</el-checkbox>
      <el-checkbox v-model="store.bp.accountsReceivableFlg" :disabled="!flgEditable">売掛先</el-checkbox>
      <el-checkbox v-model="store.bp.billingFlg" :disabled="!flgEditable">請求先</el-checkbox>
      <el-checkbox v-model="store.bp.receiptFlg" :disabled="!flgEditable">入金先</el-checkbox>
      <el-checkbox v-model="store.bp.deliveryFlg" :disabled="!flgEditable">納品先</el-checkbox>
      <el-checkbox v-model="store.bp.supplierFlg" :disabled="!flgEditable">発注先</el-checkbox>
      <el-checkbox v-model="store.bp.accountsPayableFlg" :disabled="!flgEditable">買掛先</el-checkbox>
      <el-checkbox v-model="store.bp.paymentScheduleFlg" :disabled="!flgEditable">支払予定管理先</el-checkbox>
      <el-checkbox v-model="store.bp.paymentFlg" :disabled="!flgEditable">支払先</el-checkbox>
      <el-checkbox v-model="store.bp.creditMgmtFlg" :disabled="!flgEditable">与信管理先</el-checkbox>
      <el-checkbox v-model="store.bp.makerFlg" :disabled="!flgEditable">メーカ</el-checkbox>
      <el-checkbox v-model="store.bp.paidSupplyFlg" :disabled="!flgEditable || !store.isOutsourcing">有償支給先</el-checkbox>
      <el-checkbox v-model="store.bp.rebuyObligationFlg" :disabled="!flgEditable || !store.isOutsourcing">買戻義務</el-checkbox>
    </div>
    <el-alert v-if="store.isEdit && flgChanged.length > 0" type="warning" show-icon style="margin-top: 8px" :closable="false">
      訂正時に変更不可：{{ flgChanged.join(', ') }} （MSG-018）
    </el-alert>

    <el-divider content-position="left">取引先分類 1〜10</el-divider>
    <el-form-item v-for="i in 10" :key="i" :label="`分類${i}`">
      <el-input v-model="(store.bp as any)[`bpClass${String(i).padStart(2,'0')}`]" :disabled="!store.canEdit" style="width: 130px" />
    </el-form-item>

    <el-divider content-position="left">販売分析 1〜3</el-divider>
    <el-form-item label="販売分析1"><el-input v-model="store.bp.salesAnalysis1" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
    <el-form-item label="販売分析2"><el-input v-model="store.bp.salesAnalysis2" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
    <el-form-item label="販売分析3"><el-input v-model="store.bp.salesAnalysis3" :disabled="!store.canEdit" style="width: 130px" /></el-form-item>
  </el-form>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { useBpStore } from '@/stores/businessPartner'

const props = defineProps<{ store: ReturnType<typeof useBpStore> }>()
const store = props.store
// 訂正時は FLG 変更不可（仕様書 §4 注記）
const flgEditable = computed(() => store.canEdit && !store.isEdit)
const flgChanged = computed(() => store.flgChangedOnEdit())
</script>

<style scoped>
.flg-grid { display: grid; grid-template-columns: repeat(7, 1fr); gap: 8px; padding: 0 8px; }
:deep(.el-form-item) { margin-bottom: 8px; }
</style>
