<template>
  <div class="page">
    <el-card>
      <div class="toolbar" style="display:flex;gap:8px;margin-bottom:12px">
        <el-select v-model="statusFilter" :placeholder="t('common.status')" clearable @change="load" style="width:160px">
          <el-option v-for="s in [0,1,2]" :key="s" :value="s" :label="t('asset.runStatus.' + s)" />
        </el-select>
        <el-button type="primary" @click="openAdd">{{ t('common.add') }}</el-button>
      </div>
      <el-table :data="rows" border>
        <el-table-column prop="no" :label="t('asset.field.disposalNo')" width="180" />
        <el-table-column :label="t('asset.field.disposalType')" width="120">
          <template #default="{ row }">{{ t('asset.disposalType.' + row.disposalType) }}</template>
        </el-table-column>
        <el-table-column prop="disposalDate" :label="t('asset.field.disposalDate')" width="120" />
        <el-table-column prop="proceeds" :label="t('asset.field.proceeds')" align="right" width="130" />
        <el-table-column prop="netGainLoss" :label="t('asset.field.netGainLoss')" align="right" width="130" />
        <el-table-column :label="t('common.status')" width="100">
          <template #default="{ row }">{{ t('asset.runStatus.' + row.status) }}</template>
        </el-table-column>
        <el-table-column :label="t('common.action')" width="240">
          <template #default="{ row }">
            <el-button size="small" v-if="row.status === 0" type="primary" @click="confirm(row)">{{ t('asset.action.confirm') }}</el-button>
            <el-button size="small" v-if="row.status === 1" type="danger" @click="reverse(row)">{{ t('asset.action.reverse') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dlg" :title="t('common.add')" width="600px">
      <el-form :model="editing" label-width="140px">
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
        <el-button @click="dlg = false">{{ t('common.cancel') }}</el-button>
        <el-button type="primary" @click="save">{{ t('common.save') }}</el-button>
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
const dlg = ref(false)

const editing = reactive<AssetDisposal>({
  assetCardId: '', disposalType: 1, disposalDate: '', fiscalPeriodId: '',
  proceeds: 0, taxAmount: 0, disposalExpense: 0, receiptBankAccountId: null, reason: '',
})

const disposableCards = computed(() => cards.value.filter(c => c.status === 1 || c.status === 2))

async function load() { rows.value = (await assetDisposalApi.list(statusFilter.value)).data ?? [] }
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
  try { await assetDisposalApi.create(editing); ElMessage.success(t('common.ok')); dlg.value = false; await load() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? 'common.fail')) }
}

async function confirm(row: AssetDisposal) {
  try { await assetDisposalApi.confirm(row.id!); ElMessage.success(t('common.ok')); await load() }
  catch (e: any) { ElMessage.error(t(e?.response?.data?.message ?? 'common.fail')) }
}

async function reverse(row: AssetDisposal) {
  try {
    const { value } = await ElMessageBox.prompt(t('asset.action.reverseReason'), t('asset.action.reverse'))
    await assetDisposalApi.reverse(row.id!, value)
    ElMessage.success(t('common.ok'))
    await load()
  } catch (e: any) {
    if (e === 'cancel') return
    ElMessage.error(t(e?.response?.data?.message ?? 'common.fail'))
  }
}

onMounted(() => { load(); loadCards(); loadPeriods(); loadAccounts() })
</script>
