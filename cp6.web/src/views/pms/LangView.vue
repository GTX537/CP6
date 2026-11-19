<template>
  <div class="language-page">
    <div class="toolbar">
      <el-select v-if="platform" v-model="scope" aria-label="词条范围" @change="changeScope">
        <el-option label="当前组织" value="tenant" />
        <el-option label="公共词条" value="global" />
      </el-select>
      <el-input v-model="keyword" clearable placeholder="查找词条" @keyup.enter="search" @clear="search" />
      <el-button @click="search">查询</el-button>
      <el-button v-if="permissions.has('lang:update')" @click="openEntry()">新增词条</el-button>
      <el-button v-if="permissions.has('lang:review')" :disabled="selection.length === 0" @click="reviewSelection">
        审校所选草稿
      </el-button>
      <el-button v-if="fields.length" @click="exportEntries">导出可见字段</el-button>
      <el-button v-if="permissions.has('lang:update')" @click="fileInput?.click()">导入词条</el-button>
      <input ref="fileInput" type="file" accept="application/json,.json" hidden @change="importEntries">
    </div>
    <el-alert v-if="status" role="status" :title="status" :type="statusType" :closable="false" />
    <el-table v-loading="loading" :data="rows" row-key="id" @selection-change="selection = $event">
      <el-table-column v-if="permissions.has('lang:review')" type="selection" :selectable="reviewable" width="45" />
      <el-table-column prop="langKey" :label="t('lang.langKey', '词条键')" min-width="180" show-overflow-tooltip />
      <el-table-column v-for="field in fields" :key="field.name" :label="labels[field.name]" min-width="140" show-overflow-tooltip>
        <template #default="{ row }">{{ row.translations[field.name] ?? '—' }}</template>
      </el-table-column>
      <el-table-column :label="t('lang.status', '审校状态')" width="100">
        <template #default="{ row }">{{ row.status === 'reviewed' ? '已审校' : '草稿' }}</template>
      </el-table-column>
      <el-table-column label="操作" width="130">
        <template #default="{ row }">
          <el-button v-if="permissions.has('lang:update')" link type="primary" @click="openEntry(row)">编辑</el-button>
          <el-button v-if="permissions.has('lang:delete') && !row.everReviewed" link type="danger" @click="deleteDraft(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
    <el-pagination v-model:current-page="page" :page-size="20" :total="total" layout="prev, pager, next, total" @current-change="loadEntries" />
    <el-card v-if="platform && permissions.has('lang:publish')" header="公共语言包">
      <el-alert v-if="languagePublicationUnavailable" title="新语言包暂不可用。保留当前完整版本，可重新检查。"
        type="warning" :closable="false" />
      <div v-if="preview" class="release-preview">
        <span>{{ t('lang.currentVersion', '当前版本') }} {{ preview.currentVersion || t('lang.notPublished', '尚未发布') }}</span>
        <span>{{ preview.count }} 条已审校词条</span>
        <span v-for="item in preview.missing" :key="item.code">{{ item.code }} 缺译 {{ item.count }} 条</span>
      </div>
      <div v-if="pending" class="toolbar">
        <span>原操作 {{ pending.operationRef }}</span>
        <el-button :loading="publishing" @click="readOriginal">查询原结果</el-button>
        <el-button v-if="absent" :disabled="publishing" @click="submitPublication(pending)">重试原操作</el-button>
      </div>
      <div v-else class="toolbar">
        <el-button @click="refreshPreview">刷新预览与语言包</el-button>
        <el-button type="primary" :disabled="!preview || publishing" @click="preparePublication('publish')">发布五语整包</el-button>
        <el-select v-model="rollbackVersion" class="rollback-picker" aria-label="回退目标版本" placeholder="选择回退版本">
          <el-option v-for="version in preview?.versions || []" :key="version" :label="version" :value="version" />
        </el-select>
        <el-button :disabled="!rollbackVersion || !preview || publishing" @click="preparePublication('rollback')">回退指针</el-button>
      </div>
    </el-card>
    <el-dialog v-model="editing" :title="editedRow ? '编辑词条' : '新增词条'" width="min(560px, 96vw)">
      <el-form label-width="92px" @submit.prevent="saveEntry">
        <el-form-item label="词条范围">{{ editingScope === 'global' ? '公共词条' : '当前组织' }}</el-form-item>
        <el-form-item label="词条键"><el-input v-model="editedKey" :disabled="!!editedRow" maxlength="200" /></el-form-item>
        <el-form-item v-for="field in fields" :key="field.name" :label="labels[field.name]">
          <el-input v-model="editedValues[field.name]" :disabled="field.access !== 1" maxlength="500" />
        </el-form-item>
        <el-form-item label="变更原因"><el-input v-model="editReason" maxlength="256" /></el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editing = false">取消</el-button>
        <el-button type="primary" :loading="saving" :disabled="!editedKey || !editReason.trim()" @click="saveEntry">保存草稿</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue'
import { ElMessageBox } from 'element-plus'
import { useI18n } from 'vue-i18n'
import { isAxiosError } from 'axios'
import { AUTH_EVENT, identityStamp } from '@/auth/identity'
import { languagePublicationUnavailable, refreshLanguagePublication } from '@/i18n'
import { usePermissionStore } from '@/stores/permission'
import { langApi, type LanguageEdition, type LanguageEntry, type LanguageField, type LanguageScope,
  type PublicationCommand, type PublicationPreview, type PublicationReceipt, type Translations } from '@/api/sys/lang'

const permissions = usePermissionStore()
const { t } = useI18n()
const reviewable = (row: LanguageEntry) => row.status === 'draft'
const identity = ref(identityStamp())
const platform = computed(() => {
  // 依赖身份事件，切换后即时刷新权限控件。
  return identity.value && localStorage.getItem('cp6_isPlatformAdmin') === '1'
})
const scope = ref<LanguageScope>(platform.value ? 'global' : 'tenant')
const labels: Record<LanguageField, string> = { ZhCN: '简体中文', ZhTW: '繁体中文', En: '英语', Ja: '日语', Ko: '韩语' }
const rows = ref<LanguageEntry[]>([])
const fields = ref<{ name: LanguageField; access: number }[]>([])
const selection = ref<LanguageEntry[]>([])
const keyword = ref('')
const page = ref(1)
const total = ref(0)
const loading = ref(false)
const fileInput = ref<HTMLInputElement>()
const status = ref('')
const statusType = ref<'error' | 'success' | 'warning'>('success')
let listing = 0

const editing = ref(false)
const saving = ref(false)
const editingScope = ref<LanguageScope>('tenant')
const editedRow = ref<LanguageEntry>()
const editedKey = ref('')
const editedValues = ref<Translations>({})
const editReason = ref('')

const preview = ref<PublicationPreview>()
const pending = ref<PublicationCommand>()
const publishing = ref(false)
const absent = ref(false)
const rollbackVersion = ref('')
const pendingKey = 'cp6-lang-publication-pending'

function message(text: string, type: typeof statusType.value = 'success') {
  status.value = text
  statusType.value = type
}

function failure(error: unknown) {
  if (code(error) === 412) message('数据版本已改变。请刷新后重新核对。', 'warning')
  else {
    const stable = isAxiosError(error) && error.response?.data?.code
    message(typeof stable === 'string' ? stable : '请求未完成，请核对当前状态。', 'error')
  }
}

const code = (error: unknown) => isAxiosError(error) ? error.response?.status : undefined

function changeScope() {
  selection.value = []
  rows.value = []
  fields.value = []
  editing.value = false
  void search()
}

function search() {
  page.value = 1
  return loadEntries()
}

async function loadEntries() {
  const id = ++listing
  loading.value = true
  try {
    const result = await langApi.list(scope.value, { page: page.value, pageSize: 20, keyword: keyword.value || undefined })
    if (id !== listing) return
    rows.value = result.rows
    fields.value = result.fields
    total.value = result.total
  } catch (error) { if (id === listing) failure(error) }
  finally { if (id === listing) loading.value = false }
}

function openEntry(row?: LanguageEntry) {
  editingScope.value = scope.value
  editedRow.value = row
  editedKey.value = row?.langKey || ''
  editedValues.value = { ...row?.translations }
  editReason.value = ''
  editing.value = true
}

async function saveEntry() {
  if (saving.value || editingScope.value !== scope.value) return
  const translations: Translations = {}
  for (const { name, access } of fields.value) {
    const current = editedValues.value[name] ?? null
    if (access === 1 && current !== (editedRow.value?.translations[name] ?? null)) translations[name] = current
  }
  if (!Object.keys(translations).length) return message('请填写修改的可编辑译文。', 'warning')
  const reason = editReason.value.trim()
  const command: LanguageEdition = editedRow.value
    ? { kind: 'update', id: editedRow.value.id, expectedVersion: editedRow.value.version, translations, reason }
    : { kind: 'create', langKey: editedKey.value, translations, reason }
  saving.value = true
  try {
    await langApi.save(editingScope.value, command)
    editing.value = false
    message('词条已保存为草稿。')
    await loadEntries()
  } catch (error) { failure(error) }
  finally { saving.value = false }
}

async function askReason(title: string): Promise<string | undefined> {
  try {
    const { value } = await ElMessageBox.prompt('填写变更原因', title, {
      inputValidator: value => !!value?.trim() && value.trim().length <= 256 || '请输入 1 到 256 个字符。',
    })
    return value.trim()
  } catch { return undefined }
}

async function reviewSelection() {
  const target = scope.value
  const stamp = identity.value
  const entries = selection.value.map(({ id, version }) => ({ id, version }))
  if (!entries.length) return
  const reason = await askReason(`审校选中的 ${entries.length} 条草稿`)
  if (!reason || target !== scope.value || stamp !== identityStamp()) return
  try {
    await langApi.review(target, entries, reason)
    message(`已审校 ${entries.length} 条词条。`)
    await loadEntries()
  } catch (error) { failure(error) }
}

async function deleteDraft(row: LanguageEntry) {
  const target = scope.value
  const stamp = identity.value
  const reason = await askReason(`删除草稿 ${row.langKey}`)
  if (!reason || target !== scope.value || stamp !== identityStamp()) return
  try {
    await langApi.save(target, { kind: 'delete', id: row.id, expectedVersion: row.version, reason })
    message('草稿已删除。')
    await loadEntries()
  } catch (error) { failure(error) }
}

async function exportEntries() {
  try {
    // 导出形状与导入相同，保留导出时的词条版本，以便拒绝过期提交。
    const exported = await langApi.export(scope.value, fields.value.map(field => field.name)) as unknown as { rows: LanguageEntry[] }
    const commands = exported.rows.map(row => ({
      kind: 'update', id: row.id, expectedVersion: row.version, reason: '导入审校前变更',
      translations: Object.fromEntries(Object.entries(row.translations).filter(([name]) =>
        fields.value.some(field => field.name === name && field.access === 1))),
    }))
    const link = document.createElement('a')
    link.href = URL.createObjectURL(new Blob([JSON.stringify({ ...exported, commands }, null, 2)], { type: 'application/json' }))
    link.download = `language-${scope.value}.json`
    link.click()
    URL.revokeObjectURL(link.href)
  } catch (error) { failure(error) }
}

async function importEntries(event: Event) {
  const target = scope.value
  const stamp = identity.value
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  input.value = ''
  if (!file) return
  try {
    if (file.size > 4 * 1024 * 1024) throw new Error('导入文件超过 4 MiB。')
    const parsed: unknown = JSON.parse(await file.text())
    const commands = Array.isArray(parsed) ? parsed : (parsed as { commands?: unknown; scope?: unknown })?.commands
    const exportedScope = !Array.isArray(parsed) && (parsed as { scope?: unknown })?.scope
    if (!Array.isArray(commands) || !commands.length || (exportedScope && exportedScope !== target))
      throw new Error('文件需要包含适用于当前范围的导入命令。')
    const reason = await askReason(`导入 ${commands.length} 条词条并保存为草稿`)
    if (!reason || target !== scope.value || stamp !== identityStamp()) return
    await langApi.import(target, commands.map(command => ({ ...command, reason })))
    message(`已导入 ${commands.length} 条草稿。`)
    await loadEntries()
  } catch (error) {
    if (error instanceof Error && !isAxiosError(error)) message(error.message, 'error')
    else failure(error)
  }
}

async function loadPreview() {
  try { preview.value = await langApi.preview() }
  catch (error) { failure(error) }
}

async function refreshPreview() {
  await Promise.all([loadPreview(), refreshLanguagePublication()])
}

async function preparePublication(kind: PublicationCommand['kind']) {
  if (!preview.value || pending.value) return
  const snapshot = preview.value
  const version = rollbackVersion.value
  const stamp = identity.value
  const reason = await askReason(kind === 'publish' ? '确认发布整个五语语言包' : `确认回退到 ${version}`)
  if (!reason || stamp !== identityStamp() || pending.value) return
  const command = {
    operationRef: crypto.randomUUID(), kind, expectedRevision: snapshot.revision, reason,
    ...(kind === 'publish' ? { sourceVersion: snapshot.sourceVersion } : { version }),
  }
  try { sessionStorage.setItem(pendingKey, JSON.stringify({ stamp, command })) }
  catch { return message('无法保存原操作号，发布尚未提交。', 'error') }
  pending.value = command
  await submitPublication(command)
}

async function accepted(receipt: PublicationReceipt, stamp: string) {
  if (stamp !== identityStamp() || receipt.operationRef !== pending.value?.operationRef) return
  sessionStorage.removeItem(pendingKey)
  pending.value = undefined
  absent.value = false
  message(`已确认原操作 ${receipt.operationRef}，版本 ${receipt.version}。`)
  await Promise.all([loadPreview(), refreshLanguagePublication()])
}

async function submitPublication(command: PublicationCommand) {
  const stamp = identity.value
  publishing.value = true
  absent.value = false
  try { await accepted(await langApi.apply(command), stamp) }
  catch (error) {
    if (stamp !== identityStamp()) return
    if (code(error) === 412) {
      sessionStorage.removeItem(pendingKey)
      pending.value = undefined
      failure(error)
      await loadPreview()
    } else {
      message('请查询原操作是否已提交。', 'warning')
    }
  } finally { publishing.value = false }
}

async function readOriginal() {
  if (!pending.value) return
  const stamp = identity.value
  publishing.value = true
  try { await accepted(await langApi.recover(pending.value.operationRef), stamp) }
  catch (error) {
    if (stamp !== identityStamp()) return
    absent.value = code(error) === 404
    if (absent.value) message('当前尚无原操作回执。可以用相同操作号和原确认重试。', 'warning')
    else failure(error)
  } finally { publishing.value = false }
}

function changedIdentity() {
  const stamp = identityStamp()
  if (stamp === identity.value) return
  identity.value = stamp
  pending.value = undefined
  preview.value = undefined
  status.value = ''
  scope.value = platform.value ? 'global' : 'tenant'
  changeScope()
}

onMounted(() => {
  window.addEventListener(AUTH_EVENT, changedIdentity)
  window.addEventListener('storage', changedIdentity)
  void loadEntries()
  if (platform.value && permissions.has('lang:publish')) {
    void loadPreview()
    try {
      const stored = JSON.parse(sessionStorage.getItem(pendingKey) || 'null')
      if (stored?.stamp === identity.value) pending.value = stored.command
    } catch { sessionStorage.removeItem(pendingKey) }
    if (pending.value) void readOriginal()
  }
})

onUnmounted(() => {
  window.removeEventListener(AUTH_EVENT, changedIdentity)
  window.removeEventListener('storage', changedIdentity)
})
</script>

<style scoped>
.language-page { display: grid; gap: 12px; }
.toolbar, .release-preview { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
.release-preview { margin-bottom: 12px; }
.toolbar :deep(.el-input), .toolbar :deep(.el-select) { width: 190px; }
</style>
