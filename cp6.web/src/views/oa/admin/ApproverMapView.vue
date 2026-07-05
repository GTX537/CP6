<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { approverMapApi, type ApproverMap } from '@/api/oa/approverMap'
import { userApi } from '@/api/sys/user'
import { roleApi } from '@/api/sys/role'

const { t } = useI18n()
const keys = ref<string[]>([])
const curKey = ref<string>('')
const rows = ref<ApproverMap[]>([])
const userOpts = ref<{ label: string; value: string }[]>([])
const roleOpts = ref<{ label: string; value: number }[]>([])

async function loadKeys() { keys.value = (await approverMapApi.keys() as any).data ?? [] }
async function loadRows() { rows.value = (await approverMapApi.list(curKey.value || undefined) as any).data ?? [] }
async function searchUsers(kw: string) {
  if (!kw) { userOpts.value = []; return }
  const res = await userApi.getList({ page: 1, pageSize: 20, keyword: kw }) as any
  userOpts.value = (res.rows ?? []).map((u: any) => ({ label: u.nickName || u.userName, value: String(u.id) }))
}
async function loadRoles() {
  const res = await roleApi.getAll() as any
  const list: any[] = Array.isArray(res) ? res : (res.rows ?? [])
  roleOpts.value = list.map((r: any) => ({ label: r.roleName ?? r.name ?? '', value: Number(r.roleId ?? r.id ?? 0) }))
}
function addRow() { rows.value.push({ id: '', mapKey: curKey.value, matchValue: '', approverUserId: null, approverRoleId: null, orderNo: 0, enable: true }) }
async function save(r: ApproverMap) {
  try {
    if (r.id) await approverMapApi.update(r.id, r)
    else await approverMapApi.create(r)
    ElMessage.success(t('common.saveSuccess'))
    await loadKeys(); await loadRows()
  } catch { /* http 拦截器已 toast E-WF-015 译文 */ }
}
async function del(r: ApproverMap) { if (r.id) { await approverMapApi.remove(r.id); await loadRows() } else { rows.value = rows.value.filter(x => x !== r) } }

onMounted(async () => { await loadKeys(); await loadRoles(); await loadRows() })
</script>

<template>
  <div class="approver-map">
    <div class="am-toolbar">
      <el-select v-model="curKey" filterable allow-create clearable :placeholder="t('oa.approverMap.key')" class="am-key" @change="loadRows">
        <el-option v-for="k in keys" :key="k" :label="k" :value="k" />
      </el-select>
      <el-button type="primary" @click="addRow">{{ t('oa.approverMap.addRow') }}</el-button>
    </div>
    <div class="tcard">
    <el-table :data="rows" border size="small">
      <el-table-column :label="t('oa.approverMap.matchValue')">
        <template #default="{ row }"><el-input v-model="row.matchValue" /></template>
      </el-table-column>
      <el-table-column :label="t('oa.approverMap.approverUser')">
        <template #default="{ row }">
          <el-select v-model="row.approverUserId" filterable remote :remote-method="searchUsers" clearable style="width:100%">
            <el-option v-for="u in userOpts" :key="u.value" :label="u.label" :value="u.value" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column :label="t('oa.approverMap.approverRole')">
        <template #default="{ row }">
          <el-select v-model="row.approverRoleId" clearable style="width:100%">
            <el-option v-for="r in roleOpts" :key="r.value" :label="r.label" :value="r.value" />
          </el-select>
        </template>
      </el-table-column>
      <el-table-column :label="t('oa.approverMap.enable')" width="80">
        <template #default="{ row }"><el-switch v-model="row.enable" /></template>
      </el-table-column>
      <el-table-column :label="t('common.operation')" width="140">
        <template #default="{ row }">
          <el-button link type="primary" size="small" @click="save(row)">{{ t('common.save') }}</el-button>
          <el-button link type="danger" size="small" @click="del(row)">{{ t('common.delete') }}</el-button>
        </template>
      </el-table-column>
    </el-table>
    </div>
  </div>
</template>

<style scoped>
.approver-map { display: flex; flex-direction: column; gap: 12px; padding: 12px; }
.am-toolbar { display: flex; gap: 8px; align-items: center; }
.am-key { width: 240px; }
.tcard { background: var(--cp-card); border-radius: var(--cp-r-md);
  box-shadow: var(--cp-shadow-1); overflow: hidden; }
</style>
