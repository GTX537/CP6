<!--
  シート単価取込／参照 —— Excel アップロード + 登録/参照デュアルモード + 行内選択グリッド + 一括更新の特殊页。
  検索駆動の単一 fetch 形態でなく（登録モードは検索なし・アップロード起点）、CpListPage 契約に載らないため
  「非表格特殊页」として token 化：el-upload/モード radio/el-table/一括更新フローは原様保全し、件数 el-tag → CpTag、
  インライン色値 #606266 → --cp-muted トークン化のみ。
-->
<template>
  <div class="sheet-unit-price">
    <el-card shadow="never" class="form-card">
      <el-form :model="form" label-width="120px" size="small" inline>
        <el-form-item :label="t('sales.sup.baseDate')" required>
          <el-date-picker v-model="form.baseDate" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.base')" required>
          <el-input v-model="form.baseCd" style="width: 130px" />
        </el-form-item>
        <el-form-item :label="t('sales.sup.importDiv')">
          <el-radio-group v-model="form.importDiv">
            <el-radio :value="SheetPriceImportDiv.Standard">{{ t('sales.sup.divStandard') }}</el-radio>
            <el-radio :value="SheetPriceImportDiv.Estimate">{{ t('sales.sup.divEstimate') }}</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item :label="t('sales.sup.opType')">
          <el-radio-group v-model="opType">
            <el-radio value="register">{{ t('sales.sup.import') }}</el-radio>
            <el-radio value="view">{{ t('sales.sup.refer') }}</el-radio>
          </el-radio-group>
        </el-form-item>
      </el-form>

      <!-- 登録モード：Excel ファイル選択 -->
      <el-form v-if="opType === 'register'" :model="form" label-width="120px" size="small" inline>
        <el-form-item :label="t('sales.sup.filePath')">
          <el-upload
            ref="uploadRef"
            :auto-upload="false"
            :on-change="onFileChange"
            :show-file-list="false"
            accept=".xls,.xlsx"
          >
            <el-button :icon="Upload" type="primary">{{ t('sales.sup.selectExcel') }}</el-button>
          </el-upload>
          <span v-if="selectedFile" class="file-name">{{ selectedFile.name }}</span>
        </el-form-item>
      </el-form>

      <!-- 参照モード：得意先・営業担当 -->
      <el-form v-else :model="form" label-width="120px" size="small" inline>
        <el-form-item :label="t('sales.term.customer')">
          <el-input v-model="form.customerCd" style="width: 160px" />
        </el-form-item>
        <el-form-item :label="t('sales.term.salesStaff')">
          <el-input v-model="form.salesStaffCd" style="width: 160px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="onSearch" :loading="loading">{{ t('sales.btn.show') }}</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px; display: flex; align-items: center; gap: 12px;">
        <CpTag tone="info">{{ t('sales.list.totalCount', { n: rows.length }) }}</CpTag>
        <el-checkbox v-if="opType === 'register'" v-model="allSelected" @change="onToggleAll">{{ t('sales.sup.allSelect') }}</el-checkbox>
        <el-button v-if="opType === 'register' && rows.length > 0" type="success" @click="onUpdate" :loading="updating">
          {{ t('sales.btn.update') }}（{{ checkedCount }}）
        </el-button>
        <el-button @click="reset">{{ t('sales.btn.clear') }}</el-button>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="600" style="width: 100%">
        <el-table-column prop="rowNo" :label="t('sales.list.no')" width="60" align="center" />
        <el-table-column v-if="opType === 'register'" label="☑" width="60" align="center">
          <template #default="{ row }">
            <el-checkbox v-model="row.selected" />
          </template>
        </el-table-column>
        <el-table-column prop="baseCd" :label="t('sales.term.base')" width="80" />
        <el-table-column prop="salesStaffCd" :label="t('sales.term.salesStaff')" width="100" />
        <el-table-column prop="customerCd" :label="t('sales.term.customer')" width="110" />
        <el-table-column prop="customerName" :label="t('sales.term.customer')" min-width="150" />
        <el-table-column prop="sheetFlute" :label="t('sales.sup.flute')" width="70" align="center" />
        <el-table-column prop="paperCdF" :label="t('sales.sup.paper') + '(' + t('sales.sup.front') + ')'" width="100" />
        <el-table-column prop="printCdF" :label="t('sales.sup.print') + '(' + t('sales.sup.front') + ')'" width="100" />
        <el-table-column prop="embossCdF" :label="t('sales.sup.emboss') + '(' + t('sales.sup.front') + ')'" width="110" />
        <el-table-column prop="paperCdC" :label="t('sales.sup.paper') + '(' + t('sales.sup.middle') + ')'" width="100" />
        <el-table-column prop="printCdC" :label="t('sales.sup.print') + '(' + t('sales.sup.middle') + ')'" width="100" />
        <el-table-column prop="embossCdC" :label="t('sales.sup.emboss') + '(' + t('sales.sup.middle') + ')'" width="110" />
        <el-table-column prop="paperCdB" :label="t('sales.sup.paper') + '(' + t('sales.sup.back') + ')'" width="100" />
        <el-table-column prop="printCdB" :label="t('sales.sup.print') + '(' + t('sales.sup.back') + ')'" width="100" />
        <el-table-column prop="embossCdB" :label="t('sales.sup.emboss') + '(' + t('sales.sup.back') + ')'" width="110" />
        <el-table-column prop="unitPrice" :label="t('sales.term.unitPrice')" width="120" align="right">
          <template #default="{ row }">{{ formatNumber(row.unitPrice, 'decimal') }}</template>
        </el-table-column>
        <el-table-column prop="revisionDate" :label="t('sales.sup.revisionDate')" width="110">
          <template #default="{ row }">{{ row.revisionDate?.slice(0, 10) }}</template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, reactive } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Upload } from '@element-plus/icons-vue'
import CpTag from '@/components/base/CpTag.vue'
import { sheetUnitPriceApi } from '@/api/erp/sheetUnitPrice'
import { formatNumber } from '@/utils/format'
import type { SheetUnitPriceDto, SheetPriceQueryDto } from '@/types/erp/sheetUnitPrice'
import { SheetPriceImportDiv } from '@/types/erp/sheetUnitPrice'

const { t } = useI18n()

const opType = ref<'register' | 'view'>('register')
const form = reactive<SheetPriceQueryDto>({
  baseDate: new Date().toISOString().slice(0, 10),
  baseCd: '',
  importDiv: SheetPriceImportDiv.Standard,
})
const rows = ref<SheetUnitPriceDto[]>([])
const loading = ref(false)
const updating = ref(false)
const allSelected = ref(true)
const selectedFile = ref<File | null>(null)
const uploadRef = ref()

const checkedCount = computed(() => rows.value.filter(r => r.selected).length)

function onToggleAll(v: boolean | string | number) {
  rows.value.forEach(r => (r.selected = !!v))
}

async function onFileChange(file: { raw: File }) {
  selectedFile.value = file.raw
  if (!form.baseCd) { ElMessage.warning(t('sales.err.E10022')); return }
  if (!form.baseDate) { ElMessage.warning(t('sales.err.E10022')); return }
  loading.value = true
  try {
    const r = await sheetUnitPriceApi.importExcel(
      file.raw, form.baseDate, form.baseCd, form.importDiv ?? SheetPriceImportDiv.Standard,
    )
    if (r.code === 0 && r.data) {
      if (r.data.errors.length > 0) {
        ElMessage.error(r.data.errors.slice(0, 3).join(' / '))
        rows.value = r.data.rows
        return
      }
      // 重複検出 → 上書き確認
      if (r.data.hasDuplicates) {
        try {
          await ElMessageBox.confirm(
            t('同じデータが登録されています。上書きしますか。'),
            t('sales.msg.confirmTitle'), { type: 'warning' }
          )
        } catch { rows.value = []; return }
      }
      rows.value = r.data.rows.map(row => ({ ...row, selected: true }))
      ElMessage.success(t('{n} 件のデータを取込みました', { n: rows.value.length }))
    }
  } finally {
    loading.value = false
  }
}

async function onSearch() {
  if (!form.baseCd || !form.baseDate) { ElMessage.warning(t('sales.err.E10022')); return }
  loading.value = true
  try {
    const r = await sheetUnitPriceApi.search(form)
    if (r.code === 0 && r.data) {
      rows.value = r.data
      if (r.data.length === 0) ElMessage.info(t('sales.err.E10008'))
    }
  } finally {
    loading.value = false
  }
}

async function onUpdate() {
  if (checkedCount.value === 0) { ElMessage.warning(t('sales.err.E10009')); return }
  try {
    await ElMessageBox.confirm(t('{n} 件を更新します。よろしいですか？', { n: checkedCount.value }), t('sales.msg.confirmTitle'), { type: 'warning' })
  } catch { return }
  updating.value = true
  try {
    const r = await sheetUnitPriceApi.batchUpdate({
      importDiv: form.importDiv ?? SheetPriceImportDiv.Standard,
      rows: rows.value,
    })
    if (r.code === 0 && r.data) {
      ElMessage.success(t('{n} 件を更新しました', { n: r.data.updated }))
      rows.value = []
      selectedFile.value = null
    }
  } finally {
    updating.value = false
  }
}

function reset() {
  rows.value = []
  selectedFile.value = null
  form.customerCd = undefined
  form.salesStaffCd = undefined
}
</script>

<style scoped>
.sheet-unit-price { padding: 16px; }
.form-card { margin-bottom: 12px; }
.file-name { margin-left: 12px; color: var(--cp-muted); }
</style>
