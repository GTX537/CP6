<template>
  <div class="bank-import-profile">
    <div class="page-header">
      <h2>{{ t('bankrecon.profile') }}</h2>
      <span class="subtitle">{{ t('bankrecon.profile.subtitle') }}</span>
    </div>

    <el-card shadow="never">
      <div class="table-toolbar">
        <el-button type="primary" size="small" @click="openEdit(null)">{{ t('bankrecon.btn.createProfile') }}</el-button>
        <el-button size="small" @click="load">{{ t('刷新') }}</el-button>
        <el-tag size="small" type="info">{{ t('共 {n} 条', { n: rows.length }) }}</el-tag>
      </div>

      <el-table :data="rows" border stripe size="small" max-height="520" v-loading="loading">
        <el-table-column prop="name" :label="t('bankrecon.field.profileName')" min-width="160" show-overflow-tooltip />
        <el-table-column :label="t('bankrecon.field.fileFormat')" width="90" align="center">
          <template #default="{ row }">
            <el-tag size="small" type="info">{{ row.fileFormat === 1 ? 'CSV' : 'Excel' }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="encoding" :label="t('bankrecon.field.encoding')" width="110" />
        <el-table-column :label="t('bankrecon.field.amountMode')" width="130">
          <template #default="{ row }">
            {{ row.amountMode === 1 ? t('bankrecon.amountMode.signed') : t('bankrecon.amountMode.splitCols') }}
          </template>
        </el-table-column>
        <el-table-column prop="skipHeaderRows" :label="t('bankrecon.field.skipHeaderRows')" width="100" align="center" />
        <el-table-column :label="t('bankrecon.field.isActive')" width="80" align="center">
          <template #default="{ row }">
            <el-tag :type="row.isActive ? 'success' : 'info'" size="small">
              {{ row.isActive ? t('启用') : t('停用') }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="110" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" size="small" @click="openEdit(row)">{{ t('编辑') }}</el-button>
            <el-button link type="danger" size="small" @click="doDelete(row)">{{ t('删除') }}</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <!-- 创建/编辑模板对话框 -->
    <el-dialog v-model="visible" :title="form.id ? t('bankrecon.btn.editProfile') : t('bankrecon.btn.createProfile')" width="640px">
      <el-form :model="form" label-width="140px" size="small">
        <el-row :gutter="12">
          <el-col :span="24">
            <el-form-item :label="t('bankrecon.field.profileName')" required>
              <el-input v-model="form.name" maxlength="50" :placeholder="t('bankrecon.field.profileNameHint')" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item :label="t('bankrecon.field.fileFormat')">
              <el-select v-model="form.fileFormat" style="width:100%">
                <el-option :value="1" label="CSV" />
                <el-option :value="2" label="Excel" />
              </el-select>
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('bankrecon.field.encoding')">
              <el-input v-model="form.encoding" :placeholder="'UTF-8 / Shift_JIS / GBK'" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item :label="t('bankrecon.field.delimiter')">
              <el-input v-model="form.delimiter" maxlength="2" :disabled="form.fileFormat === 2" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('bankrecon.field.skipHeaderRows')">
              <el-input-number v-model="form.skipHeaderRows" :min="0" :max="10" style="width:100%" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item :label="t('bankrecon.field.dateField')">
              <el-input v-model="form.dateField" :placeholder="t('bankrecon.field.dateFieldHint')" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('bankrecon.field.dateFormat')">
              <el-input v-model="form.dateFormat" :placeholder="'yyyy/MM/dd'" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item :label="t('bankrecon.field.amountMode')">
          <el-select v-model="form.amountMode" style="width:100%">
            <el-option :value="1" :label="t('bankrecon.amountMode.signed')" />
            <el-option :value="2" :label="t('bankrecon.amountMode.splitCols')" />
          </el-select>
        </el-form-item>
        <!-- 单列带符号模式 -->
        <template v-if="form.amountMode === 1">
          <el-row :gutter="12">
            <el-col :span="12">
              <el-form-item :label="t('bankrecon.field.amountField')">
                <el-input v-model="form.amountField" :placeholder="t('bankrecon.field.columnIndexHint')" />
              </el-form-item>
            </el-col>
            <el-col :span="12">
              <el-form-item :label="t('bankrecon.field.signRule')">
                <el-select v-model="form.signRule" style="width:100%">
                  <el-option :value="1" :label="t('bankrecon.signRule.positiveDeposit')" />
                  <el-option :value="2" :label="t('bankrecon.signRule.positiveWithdrawal')" />
                </el-select>
              </el-form-item>
            </el-col>
          </el-row>
        </template>
        <!-- 入款/出款双列模式 -->
        <template v-else>
          <el-row :gutter="12">
            <el-col :span="12">
              <el-form-item :label="t('bankrecon.field.depositAmountField')">
                <el-input v-model="form.depositAmountField" :placeholder="t('bankrecon.field.columnIndexHint')" />
              </el-form-item>
            </el-col>
            <el-col :span="12">
              <el-form-item :label="t('bankrecon.field.withdrawalAmountField')">
                <el-input v-model="form.withdrawalAmountField" :placeholder="t('bankrecon.field.columnIndexHint')" />
              </el-form-item>
            </el-col>
          </el-row>
        </template>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item :label="t('bankrecon.field.descriptionField')">
              <el-input v-model="form.descriptionField" :placeholder="t('bankrecon.field.columnIndexHint')" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('bankrecon.field.refNoField')">
              <el-input v-model="form.refNoField" :placeholder="t('bankrecon.field.columnIndexHint')" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-row :gutter="12">
          <el-col :span="12">
            <el-form-item :label="t('bankrecon.field.decimalSeparator')">
              <el-input v-model="form.decimalSeparator" maxlength="1" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item :label="t('bankrecon.field.thousandsSeparator')">
              <el-input v-model="form.thousandsSeparator" maxlength="1" />
            </el-form-item>
          </el-col>
        </el-row>
        <el-form-item :label="t('bankrecon.field.isActive')">
          <el-switch v-model="form.isActive" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="visible = false">{{ t('取消') }}</el-button>
        <el-button type="primary" :loading="saving" @click="doSave">{{ t('保存') }}</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { bankProfileApi } from '@/api/fin/bankRecon'
import type { BankImportProfile } from '@/types/fin/bankRecon'

const { t } = useI18n()

const rows = ref<BankImportProfile[]>([])
const loading = ref(false)
const saving = ref(false)
const visible = ref(false)

function emptyForm(): BankImportProfile {
  return {
    id: undefined,
    name: '',
    bankAccountId: null,
    fileFormat: 1,
    encoding: 'UTF-8',
    delimiter: ',',
    skipHeaderRows: 1,
    dateField: '0',
    dateFormat: 'yyyy/MM/dd',
    amountMode: 2,
    amountField: null,
    depositAmountField: null,
    withdrawalAmountField: null,
    signRule: 1,
    descriptionField: null,
    counterpartyField: null,
    refNoField: null,
    balanceField: null,
    decimalSeparator: '.',
    thousandsSeparator: ',',
    isActive: true,
    rowVersion: null,
  }
}

const form = reactive<BankImportProfile>(emptyForm())

async function load() {
  loading.value = true
  try {
    const r = await bankProfileApi.list()
    rows.value = r?.data || []
  } finally {
    loading.value = false
  }
}

function openEdit(row: BankImportProfile | null) {
  Object.assign(form, row ? { ...emptyForm(), ...row } : emptyForm())
  visible.value = true
}

async function doSave() {
  if (!form.name.trim()) {
    ElMessage.warning(t('bankrecon.field.profileName') + t('必填'))
    return
  }
  saving.value = true
  try {
    const r = await bankProfileApi.save({ ...form })
    if (r.code === 0) {
      ElMessage.success(t('bankrecon.msg.saved'))
      visible.value = false
      await load()
    } else {
      ElMessage.error(r.message)
    }
  } finally {
    saving.value = false
  }
}

async function doDelete(row: BankImportProfile) {
  try {
    await ElMessageBox.confirm(
      t('bankrecon.msg.deleteProfileConfirm', { name: row.name }),
      t('删除'),
      { type: 'warning' },
    )
  } catch { return }
  if (!row.id) return
  await bankProfileApi.remove(row.id)
  ElMessage.success(t('bankrecon.msg.deleted'))
  await load()
}

onMounted(load)
</script>

<style scoped>
.bank-import-profile { padding: 16px; }
.page-header { margin-bottom: 12px; }
.page-header h2 { margin: 0; color: #303133; font-size: 20px; font-weight: 650; }
.subtitle { color: #909399; font-size: 12px; }
.table-toolbar { margin-bottom: 8px; display: flex; gap: 8px; align-items: center; flex-wrap: wrap; }
</style>
