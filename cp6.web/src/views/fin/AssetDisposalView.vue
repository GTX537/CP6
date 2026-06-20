<template>
  <div class="asset-disposal">
    <div class="page-header">
      <h2>{{ t('资产处置') }}</h2>
      <span class="subtitle">{{ t('出售/报废/转让/盘亏（清理结转损益）') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-select v-model="statusFilter" :placeholder="t('状态')" clearable size="small" @change="load" style="width:140px">
          <el-option v-for="s in [0,1,2]" :key="s" :value="s" :label="t('asset.runStatus.' + s)" />
        </el-select>
        <el-button type="primary" size="small" @click="openAdd">{{ t('新建') }}</el-button>
        <el-button size="small" @click="load">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="620" v-loading="loading">
        <el-table-column prop="no" :label="t('asset.field.disposalNo')" width="180" />
        <el-table-column :label="t('asset.field.disposalType')" width="120">
          <template #default="{ row }">{{ t('asset.disposalType.' + row.disposalType) }}</template>
        </el-table-column>
        <el-table-column prop="disposalDate" :label="t('asset.field.disposalDate')" width="120" />
        <el-table-column prop="proceeds" :label="t('asset.field.proceeds')" align="right" width="130" />
        <el-table-column prop="netGainLoss" :label="t('asset.field.netGainLoss')" align="right" width="130" />
        <el-table-column :label="t('状态')" width="110" align="center">
          <template #default="{ row }">
            <el-tag :type="statusTag(row.status)" size="small">{{ t('asset.runStatus.' + row.status) }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="180" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" v-if="row.status === 0" @click="confirm(row)">{{ t('asset.action.confirm') }}</el-button>
            <el-button link type="danger" size="small" v-if="row.status === 1" @click="reverse(row)">{{ t('asset.action.reverse') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dlg" :title="t('新建')" width="600">
      <el-form :model="editing" label-width="140px" size="small">
        <el-form-item :label="t('asset.field.assetCard')">
          <el-select v-model="editing.assetCardId" filterable style="width:100%">
            <el-option v-for="c in disposableCards" :key="c.id" :value="c.id" :label="`${c.assetNo} ${c.name}`" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('asset.field.disposalType')">
          <el-select v-model="editing.disposalType" style="width:100%">
            <el-option v-for="d in [1,2,3,4]" :key="d" :value="d" :label="t('asset.disposalType.' + d)" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('asset.field.disposalDate')">
          <el-date-picker v-model="editing.disposalDate" value-format="YYYY-MM-DD" style="width:100%" />
        </el-form-item>
        <el-form-item :label="t('asset.field.period')">
          <el-select v-model="editing.fiscalPeriodId" filterable style="width:100%">
            <el-option v-for="p in periods" :key="p.id"
              :label="`${p.year}-${String(p.month).padStart(2, '0')}`" :value="p.id" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('asset.field.proceeds')">
          <el-input-number v-model="editing.proceeds" :min="0" :precision="2" />
        </el-form-item>
        <el-form-item :label="t('asset.field.taxAmount')">
          <el-input-number v-model="editing.taxAmount" :min="0" :precision="2" />
        </el-form-item>
        <el-form-item :label="t('asset.field.disposalExpense')">
          <el-input-number v-model="editing.disposalExpense" :min="0" :precision="2" />
        </el-form-item>
        <el-form-item v-if="editing.proceeds > 0 || editing.disposalExpense > 0"
          :label="t('asset.field.bank')">
          <el-select v-model="editing.receiptBankAccountId" filterable style="width:100%">
            <el-option v-for="a in accounts" :key="a.id" :value="a.id" :label="`${a.code} ${a.name}`" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('asset.field.reason')"><el-input v-model="editing.reason" type="textarea" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dlg = false">{{ t('取消') }}</el-button>
        <el-button type="primary" @click="save">{{ t('保存') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, reactive, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { assetDisposalApi, assetCardApi } from '@/api/fin/asset'
import { periodApi, glAccountApi } from '@/api/fin/fin'
import type { AssetDisposal, AssetCard } from '@/types/fin/asset'

const { t } = useI18n()
const rows = ref<AssetDisposal[]>([])
const cards = ref<AssetCard[]>([])
const periods = ref<any[]>([])
const accounts = ref<any[]>([])
const statusFilter = ref<number | undefined>()
const loading = ref(false)
const dlg = ref(false)

const editing = reactive<AssetDisposal>({
  assetCardId: '', disposalType: 1, disposalDate: '', fiscalPeriodId: '',
  proceeds: 0, taxAmount: 0, disposalExpense: 0, receiptBankAccountId: null, reason: '',
})

const disposableCards = computed(() => cards.value.filter(c => c.status === 1 || c.status === 2))

// 处置状态配色：0 草稿=info / 1 已确认=success / 2 已反冲=danger
function statusTag(status: number): 'success' | 'info' | 'danger' {
  return status === 1 ? 'success' : status === 0 ? 'info' : 'danger'
}

async function load() {
  loading.value = true
  try { rows.value = (await assetDisposalApi.list(statusFilter.value)).data ?? [] }
  finally { loading.value = false }
}
async function loadCards() { cards.value = (await assetCardApi.list()).data ?? [] }
async function loadPeriods() { periods.value = (await periodApi.list()).data ?? [] }
async function loadAccounts() {
  accounts.value = ((await glAccountApi.list()).data ?? []).filter((a: any) => a.isLeaf && a.code?.startsWith('1002'))
}

function openAdd() {
  Object.assign(editing, {
    id: undefined, assetCardId: '', disposalType: 1, disposalDate: new Date().toISOString().slice(0, 10),
    fiscalPeriodId: '', proceeds: 0, taxAmount: 0, disposalExpense: 0, receiptBankAccountId: null, reason: '',
  })
  dlg.value = true
}

async function save() {
  try { await assetDisposalApi.create(editing); ElMessage.success(t('操作成功')); dlg.value = false; await load() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? '操作失败')) }
}

async function confirm(row: AssetDisposal) {
  try { await assetDisposalApi.confirm(row.id!); ElMessage.success(t('操作成功')); await load() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? '操作失败')) }
}

async function reverse(row: AssetDisposal) {
  try {
    const { value } = await ElMessageBox.prompt(t('asset.action.reverseReason'), t('asset.action.reverse'))
    await assetDisposalApi.reverse(row.id!, value)
    ElMessage.success(t('操作成功'))
    await load()
  } catch (e: any) {
    if (e === 'cancel') return
    ElMessage.error(t(e?.response?.data?.message ?? '操作失败'))
  }
}

onMounted(() => { load(); loadCards(); loadPeriods(); loadAccounts() })
</script>

<style scoped>
.asset-disposal { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
</style>
