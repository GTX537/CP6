<template>
  <div class="dictionary-page">
    <div class="toolbar">
      <el-input v-model="keyword" clearable placeholder="查找字典类型" @keyup.enter="search" @clear="search" />
      <el-button @click="search">查询</el-button>
      <el-button v-if="permissions.has('dict:add')" :disabled="!!pending" @click="open('create-type')">新增类型</el-button>
      <el-button v-if="selected?.type && permissions.has('dict:add')" :disabled="!!pending" @click="open('create-data')">新增字典值</el-button>
    </div>
    <el-alert v-if="status" :title="status" :type="statusType" :closable="false" />
    <div v-if="pending" class="toolbar">
      <span>原操作 {{ pending.operationRef }}</span>
      <el-button :loading="saving" @click="recover">查询原结果</el-button>
      <el-button v-if="absent" :disabled="saving" @click="submit(pending)">重试原操作</el-button>
    </div>
    <el-table :data="types" row-key="id">
      <el-table-column prop="typeCode" :label="t('dict.typeCode', '类型编码')" min-width="110" />
      <el-table-column prop="typeName" :label="t('dict.typeName', '名称')" min-width="120" />
      <el-table-column prop="orderNo" :label="t('dict.orderNo', '顺序')" width="76" />
      <el-table-column label="状态" width="92">
        <template #default="{ row }">{{ row.enable ? '启用' : '停用' }}</template>
      </el-table-column>
      <el-table-column label="操作" width="200">
        <template #default="{ row }">
          <el-button link @click="select(row.typeCode)">管理字典值</el-button>
          <el-button v-if="permissions.has('dict:edit')" link :disabled="!!pending" @click="openType('update-type', row.typeCode)">编辑</el-button>
          <el-button v-if="permissions.has('dict:delete')" link type="danger" :disabled="!!pending" @click="openType('delete-type', row.typeCode)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-pagination v-model:current-page="page" :page-size="20" :total="total" layout="prev, pager, next, total" @current-change="loadTypes" />
    <el-card v-if="selected?.type" :header="`${selected.type.typeName} · ${selected.type.typeCode}`">
      <p>当前字典版本 {{ selected.dictionaryVersion }}</p>
      <el-table :data="selected.allItems" row-key="id">
        <el-table-column prop="value" :label="t('dict.value', '业务值')" min-width="110" />
        <el-table-column prop="label" :label="t('dict.label', '显示标签')" min-width="120" />
        <el-table-column prop="orderNo" label="顺序" width="76" />
        <el-table-column label="新单据使用" width="144">
          <template #default="{ row }">{{ row.selectable ? '可选' : '历史值，当前不可选' }}</template>
        </el-table-column>
        <el-table-column label="操作" width="132">
          <template #default="{ row }">
            <el-button v-if="permissions.has('dict:edit')" link :disabled="!!pending" @click="open('update-data', row)">编辑</el-button>
            <el-button v-if="permissions.has('dict:delete')" link type="danger" :disabled="!!pending" @click="open('delete-data', row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
    <el-dialog v-model="editing" :title="deleting ? '确认删除' : '保存字典'" width="min(530px, 96vw)">
      <el-form label-width="92px" @submit.prevent="confirm">
        <el-form-item label="类型编码"><el-input v-model="edit.typeCode" :disabled="edit.kind !== 'create-type'" maxlength="100" /></el-form-item>
        <el-form-item v-if="dataEdit" label="业务值"><el-input v-model="edit.value" :disabled="edit.kind !== 'create-data'" maxlength="100" /></el-form-item>
        <template v-if="!deleting">
          <el-form-item label="显示名称"><el-input v-model="edit.label" :maxlength="dataEdit ? 200 : 100" /></el-form-item>
          <el-form-item label="顺序"><el-input-number v-model="edit.orderNo" /></el-form-item>
          <el-form-item label="启用"><el-switch v-model="edit.enable" /></el-form-item>
        </template>
        <el-form-item label="变更原因"><el-input v-model="edit.reason" maxlength="256" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editing = false">取消</el-button>
        <el-button :type="deleting ? 'danger' : 'primary'" :loading="saving" :disabled="!edit.reason.trim()" @click="confirm">确认</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { isAxiosError } from 'axios'
import { useI18n } from 'vue-i18n'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { dictionaryApi, type DictionaryChange, type DictionaryReceipt } from '@/api/sys/dict'
import { usePermissionStore } from '@/stores/permission'

const permissions = usePermissionStore()
const { t } = useI18n()
const pendingKey = 'cp6-dictionary-owner-pending'
type Options = Awaited<ReturnType<typeof dictionaryApi.options>>
type Item = Options['allItems'][number]
const types = ref<Awaited<ReturnType<typeof dictionaryApi.types>>['rows']>([])
const selected = ref<Options>()
const edit = ref({ kind: 'create-type' as DictionaryChange['kind'], typeCode: '', version: '', id: undefined as number | undefined,
  value: '', label: '', enable: true, orderNo: 0, reason: '' })
const dataEdit = computed(() => edit.value.kind.endsWith('-data'))
const deleting = computed(() => edit.value.kind.startsWith('delete-'))
const keyword = ref('')
const page = ref(1)
const total = ref(0)
const editing = ref(false)
const saving = ref(false)
const absent = ref(false)
const status = ref('')
const statusType = ref<'warning' | 'error' | 'success'>('success')
const pending = ref<DictionaryChange>()
let identity = identityStamp()
let selection = 0
let listRequest = 0

function show(text: string, type: typeof statusType.value = 'success') {
  status.value = text
  statusType.value = type
}
function statusOf(error: unknown) { return isAxiosError(error) ? error.response?.status : undefined }
function failure(error: unknown) {
  const reason = isAxiosError(error) && error.response?.data?.message
  show(typeof reason === 'string' ? reason : '字典请求未完成。', 'error')
}
async function loadTypes() {
  const request = ++listRequest
  try {
    const result = await dictionaryApi.types({ page: page.value, pageSize: 20, keyword: keyword.value || undefined })
    if (request === listRequest) {
      types.value = result.rows
      total.value = result.total
    }
  } catch (error) { if (request === listRequest) failure(error) }
}
function search() {
  page.value = 1
  return loadTypes()
}
async function select(typeCode: string) {
  const request = ++selection
  try {
    const next = await dictionaryApi.options(typeCode)
    if (request !== selection) return undefined
    selected.value = next
    if (next.unavailableReason === 'DICT_AMBIGUOUS') show('字典存在重复业务编码。', 'warning')
    return next
  } catch (error) {
    if (request === selection) failure(error)
    return undefined
  }
}
async function openType(kind: 'update-type' | 'delete-type', typeCode: string) {
  if ((await select(typeCode))?.type) open(kind)
}
function open(kind: DictionaryChange['kind'], item?: Item) {
  const type = kind === 'create-type' ? undefined : selected.value?.type
  edit.value = {
    kind, typeCode: type?.typeCode || '', id: kind.endsWith('-type') ? type?.id : item?.id,
    version: selected.value?.dictionaryVersion || 'absent', value: item?.value || '',
    label: item?.label || (kind.endsWith('-type') ? type?.typeName || '' : ''),
    enable: item?.enable ?? type?.enable ?? true, orderNo: item?.orderNo ?? (kind.endsWith('-type') ? type?.orderNo ?? 0 : 0),
    reason: '',
  }
  editing.value = true
}
async function confirm() {
  if (pending.value || saving.value) return
  const input = { ...edit.value }
  if (!input.typeCode || (!deleting.value && !input.label) || (dataEdit.value && !input.value)) {
    show('请填写业务编码和显示名称。', 'warning')
    return
  }
  const stamp = identity
  saving.value = true
  try {
    const expectedVersion = input.kind === 'create-type'
      ? (await dictionaryApi.options(input.typeCode)).dictionaryVersion : input.version
    if (stamp !== identityStamp()) return
    const change: DictionaryChange = {
      operationRef: crypto.randomUUID(), kind: input.kind, typeCode: input.typeCode,
      expectedVersion, id: input.id, reason: input.reason.trim(),
      ...(input.kind.startsWith('delete-') ? {} : { label: input.label, orderNo: input.orderNo,
        enable: input.enable, ...(input.kind.endsWith('-data') ? { value: input.value } : {}) }),
    }
    sessionStorage.setItem(pendingKey, JSON.stringify({ stamp, change }))
    pending.value = change
    editing.value = false
    await submit(change)
  } catch (error) { if (stamp === identityStamp()) failure(error) }
  finally { saving.value = false }
}
async function accepted(receipt: DictionaryReceipt, stamp: string) {
  if (stamp !== identityStamp() || receipt.operationRef !== pending.value?.operationRef) return
  pending.value = undefined
  absent.value = false
  sessionStorage.removeItem(pendingKey)
  show(`已保存版本 ${receipt.dictionaryVersion}`)
  await Promise.all([loadTypes(), select(receipt.typeCode)])
}
async function submit(change: DictionaryChange) {
  const stamp = identity
  saving.value = true
  absent.value = false
  try { await accepted(await dictionaryApi.save(change), stamp) }
  catch (error) {
    if (stamp !== identityStamp()) return
    const response = statusOf(error)
    if (response && [400, 404, 409, 412].includes(response)) {
      pending.value = undefined
      sessionStorage.removeItem(pendingKey)
      if (response === 412) {
        show('字典版本已改变。已重新读取当前值，请核对后再提交。', 'warning')
        await select(change.typeCode)
      } else failure(error)
    } else show('保存结果待确认。请查询原操作。', 'warning')
  } finally { saving.value = false }
}
async function recover() {
  if (!pending.value || saving.value) return
  const stamp = identity
  saving.value = true
  try { await accepted(await dictionaryApi.recover(pending.value.operationRef), stamp) }
  catch (error) {
    if (stamp !== identityStamp()) return
    absent.value = statusOf(error) === 404
    if (absent.value) show('当前尚无原操作回执。可使用原操作号和原内容重试。', 'warning')
    else failure(error)
  } finally { saving.value = false }
}
function refreshIdentity() {
  const stamp = identityStamp()
  if (stamp === identity) return
  identity = stamp
  selected.value = undefined
  pending.value = undefined
  types.value = []
  editing.value = false
  status.value = ''
  selection++
  void search()
}
onMounted(() => {
  window.addEventListener(AUTH_EVENT, refreshIdentity)
  window.addEventListener('storage', refreshIdentity)
  void loadTypes()
  try {
    const stored = JSON.parse(sessionStorage.getItem(pendingKey) || 'null')
    if (stored?.stamp === identity) pending.value = stored.change
  } catch { sessionStorage.removeItem(pendingKey) }
  if (pending.value) void recover()
})
onUnmounted(() => {
  window.removeEventListener(AUTH_EVENT, refreshIdentity)
  window.removeEventListener('storage', refreshIdentity)
})
</script>

<style scoped>
.dictionary-page { display: grid; gap: 12px; }
.toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.toolbar :deep(.el-input) { width: 220px; }
</style>
