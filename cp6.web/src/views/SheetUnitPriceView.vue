<template>
  <div class="sheet-unit-price">
    <el-card shadow="never" class="form-card">
      <el-form :model="form" label-width="120px" size="small" inline>
        <el-form-item label="基準日" required>
          <el-date-picker v-model="form.baseDate" type="date" value-format="YYYY-MM-DD" style="width: 150px" />
        </el-form-item>
        <el-form-item label="拠点" required>
          <el-input v-model="form.baseCd" style="width: 130px" />
        </el-form-item>
        <el-form-item label="取込区分">
          <el-radio-group v-model="form.importDiv">
            <el-radio :value="SheetPriceImportDiv.Standard">シート単価</el-radio>
            <el-radio :value="SheetPriceImportDiv.Estimate">シート単価(見積用)</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="操作種別">
          <el-radio-group v-model="opType">
            <el-radio value="register">登録</el-radio>
            <el-radio value="view">参照</el-radio>
          </el-radio-group>
        </el-form-item>
      </el-form>

      <!-- 登録モード：Excel ファイル選択 -->
      <el-form v-if="opType === 'register'" :model="form" label-width="120px" size="small" inline>
        <el-form-item label="ファイルパス">
          <el-upload
            ref="uploadRef"
            :auto-upload="false"
            :on-change="onFileChange"
            :show-file-list="false"
            accept=".xls,.xlsx"
          >
            <el-button :icon="Upload" type="primary">Excel選択</el-button>
          </el-upload>
          <span v-if="selectedFile" style="margin-left: 12px; color: #606266;">{{ selectedFile.name }}</span>
        </el-form-item>
      </el-form>

      <!-- 参照モード：得意先・営業担当 -->
      <el-form v-else :model="form" label-width="120px" size="small" inline>
        <el-form-item label="得意先">
          <el-input v-model="form.customerCd" style="width: 160px" />
        </el-form-item>
        <el-form-item label="営業担当">
          <el-input v-model="form.salesStaffCd" style="width: 160px" />
        </el-form-item>
        <el-form-item>
          <el-button type="primary" @click="onSearch" :loading="loading">表示</el-button>
        </el-form-item>
      </el-form>
    </el-card>

    <el-card shadow="never">
      <div style="margin-bottom: 8px; display: flex; align-items: center; gap: 12px;">
        <el-tag size="small">{{ rows.length }} 件</el-tag>
        <el-checkbox v-if="opType === 'register'" v-model="allSelected" @change="onToggleAll">全選択/全解除</el-checkbox>
        <el-button v-if="opType === 'register' && rows.length > 0" type="success" @click="onUpdate" :loading="updating">
          更新（{{ checkedCount }} 件）
        </el-button>
        <el-button @click="reset">クリア</el-button>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="600" style="width: 100%">
        <el-table-column prop="rowNo" label="NO" width="60" align="center" />
        <el-table-column v-if="opType === 'register'" label="選択" width="60" align="center">
          <template #default="{ row }">
            <el-checkbox v-model="row.selected" />
          </template>
        </el-table-column>
        <el-table-column prop="baseCd" label="拠点" width="80" />
        <el-table-column prop="salesStaffCd" label="営業担当" width="100" />
        <el-table-column prop="customerCd" label="得意先" width="110" />
        <el-table-column prop="customerName" label="得意先名" min-width="150" />
        <el-table-column prop="sheetFlute" label="段" width="70" align="center" />
        <el-table-column prop="paperCdF" label="原紙(表)" width="100" />
        <el-table-column prop="printCdF" label="印刷(表)" width="100" />
        <el-table-column prop="embossCdF" label="エンボス(表)" width="110" />
        <el-table-column prop="paperCdC" label="原紙(中)" width="100" />
        <el-table-column prop="printCdC" label="印刷(中)" width="100" />
        <el-table-column prop="embossCdC" label="エンボス(中)" width="110" />
        <el-table-column prop="paperCdB" label="原紙(裏)" width="100" />
        <el-table-column prop="printCdB" label="印刷(裏)" width="100" />
        <el-table-column prop="embossCdB" label="エンボス(裏)" width="110" />
        <el-table-column prop="unitPrice" label="単価" width="120" align="right">
          <template #default="{ row }">{{ Number(row.unitPrice).toFixed(2) }}</template>
        </el-table-column>
        <el-table-column prop="revisionDate" label="改定日" width="110">
          <template #default="{ row }">{{ row.revisionDate?.slice(0, 10) }}</template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, reactive } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Upload } from '@element-plus/icons-vue'
import { sheetUnitPriceApi } from '@/api/sheetUnitPrice'
import type { SheetUnitPriceDto, SheetPriceQueryDto } from '@/types/sheetUnitPrice'
import { SheetPriceImportDiv } from '@/types/sheetUnitPrice'

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
  if (!form.baseCd) { ElMessage.warning('W10001: 基準日・拠点を入力してください'); return }
  if (!form.baseDate) { ElMessage.warning('W10001: 基準日を入力してください'); return }
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
            'E10101: 同じデータが登録されています。上書きしますか。',
            '確認', { confirmButtonText: 'はい', cancelButtonText: 'いいえ', type: 'warning' }
          )
        } catch { rows.value = []; return }
      }
      rows.value = r.data.rows.map(row => ({ ...row, selected: true }))
      ElMessage.success(`${rows.value.length} 件のデータを取込みました`)
    }
  } finally {
    loading.value = false
  }
}

async function onSearch() {
  if (!form.baseCd || !form.baseDate) { ElMessage.warning('W10001: 必須項目に値が指定されていません（基準日、拠点）'); return }
  loading.value = true
  try {
    const r = await sheetUnitPriceApi.search(form)
    if (r.code === 0 && r.data) {
      rows.value = r.data
      if (r.data.length === 0) ElMessage.info('E10008: 検索結果がありませんでした')
    }
  } finally {
    loading.value = false
  }
}

async function onUpdate() {
  if (checkedCount.value === 0) { ElMessage.warning('更新対象が選択されていません'); return }
  try {
    await ElMessageBox.confirm(`${checkedCount.value} 件を更新します。よろしいですか？`, '確認', { type: 'warning' })
  } catch { return }
  updating.value = true
  try {
    const r = await sheetUnitPriceApi.batchUpdate({
      importDiv: form.importDiv ?? SheetPriceImportDiv.Standard,
      rows: rows.value,
    })
    if (r.code === 0 && r.data) {
      ElMessage.success(`${r.data.updated} 件を更新しました`)
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
</style>
