<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref } from 'vue'
import { ElMessageBox } from 'element-plus'
import { identityStamp, requireIdentity } from '@/auth/identity'
import { legacyDraftAdoptionApi as adoptionApi, type LegacyAdoptionPreview } from '@/api/oa/legacyDraftAdoption'
import type { PersonalDraft } from '@/api/oa/personalForm'
import { usePersonalCommand } from '../catalog/usePersonalCommand'
import { personalFormError } from '../catalog/personalFormError'
import PersonalCommandStatus from '../catalog/PersonalCommandStatus.vue'
import PersonalFormEditor from '../catalog/PersonalFormEditor.vue'
import { legacyPersonalDrafts, type LegacyPersonalDraft } from '@/api/oa/legacyPersonalDrafts'

const rows = ref<LegacyPersonalDraft[]>([]), total = ref(0), page = ref(1), loading = ref(false), error = ref('')
const preview = ref<LegacyAdoptionPreview>(), adopted = ref<PersonalDraft>(), reason = ref(''), previewLoading = ref(false), showEditor = ref(false)
let generation = 0, disposed = false, request: AbortController | undefined
function clearPrivate() { generation++; request?.abort(); rows.value = []; total.value = 0; preview.value = undefined; adopted.value = undefined; reason.value = ''; showEditor.value = false; previewLoading.value = false }
const command = usePersonalCommand(clearPrivate, result => { adopted.value = result.result as PersonalDraft; preview.value = undefined }, 'cp6-legacy-draft-adoption-pending')
async function readContent(id: string) {
  if (command.pending.value || command.busy.value || previewLoading.value) return
  const current = ++generation, identity = identityStamp(); request?.abort(); request = new AbortController(); previewLoading.value = true; preview.value = undefined; adopted.value = undefined; error.value = ''; reason.value = ''; showEditor.value = false
  try { const value = await adoptionApi.preview(id, request.signal); requireIdentity(identity); if (!disposed && generation === current) preview.value = value }
  catch (e) { if (!disposed && current === generation && identity === identityStamp()) error.value = personalFormError(e) }
  finally { if (!disposed && current === generation) previewLoading.value = false }
}
async function adopt() {
  const value = preview.value, current = generation, identity = identityStamp(), note = reason.value.trim()
  if (!value?.canAdopt || command.pending.value || command.busy.value || previewLoading.value || !note) return
  try { await ElMessageBox.confirm(`按已核对的当前映射，将旧记录复制为 ${value.targetKey} v${value.targetVersion} 的新本人草稿。旧记录保留，原历史发布身份仍未核实；不会提交或启动流程。`, '复制为新草稿') } catch { return }
  if (disposed || current !== generation || identity !== identityStamp()) return
  await command.execute('FORM_DRAFT_ADOPT_LEGACY', value.legacyDraftId, value.sourceVersion, JSON.stringify({ mappingVersion: value.mappingVersion, reason: note }))
}
async function load() {
  request?.abort(); const controller = new AbortController(); request = controller; const current = ++generation
  loading.value = true; error.value = ''; rows.value = []; total.value = 0; preview.value = undefined; previewLoading.value = false; reason.value = ''
  try {
    const value = await legacyPersonalDrafts(page.value, controller.signal)
    if (disposed || current !== generation) return
    rows.value = value.items; total.value = value.total
  } catch {
    if (disposed || current !== generation) return
    error.value = '历史草稿读取失败，请重试。'
  } finally { if (!disposed && current === generation) loading.value = false }
}
function state(value: number) { return value === 0 ? '未提交（历史）' : value === 1 ? '已提交（历史）' : `历史状态 ${value}` }
onMounted(async () => { await load(); if (command.pending.value) await command.recover() })
onBeforeUnmount(() => { disposed = true; generation++; request?.abort(); rows.value = [] })
</script>

<template>
  <section class="legacy-drafts" data-test="legacy-drafts">
    <p>以下为保留的历史草稿记录，不能从此处修改、提交或删除。内容按准确当前映射和字段权限读取；复制采用会创建新草稿，原历史身份仍未核实。</p>
    <router-link to="/oa/form-catalog">从当前表单目录发起</router-link>
    <el-button :loading="loading" @click="load">刷新</el-button>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <el-table v-else :data="rows" v-loading="loading" row-key="id">
      <el-table-column prop="title" label="标题" min-width="160" />
      <el-table-column prop="formKey" label="原表单" min-width="120" />
      <el-table-column label="记录版本" width="110"><template #default="{ row }">{{ row.recordedVersion ?? '无完整快照' }}</template></el-table-column>
      <el-table-column label="状态" width="150"><template #default="{ row }">{{ state(row.status) }}</template></el-table-column>
      <el-table-column label="来源" min-width="160"><template #default="{ row }">{{ row.source === 'LEGACY_VERSION_SNAPSHOT' ? '历史版本快照' : row.source === 'LEGACY_HEAD_BACKFILL' ? '历史补录，发布未核实' : '缺少版本快照' }}</template></el-table-column>
      <el-table-column prop="recordedUpdatedAt" label="原记录时间" min-width="190" />
      <el-table-column label="当前读取" width="160"><template #default="{ row }"><el-button :disabled="!!command.pending.value || command.busy.value || previewLoading" @click="readContent(row.id)">查看受控内容</el-button></template></el-table-column>
    </el-table>
    <PersonalCommandStatus :pending="command.pending.value" :busy="command.busy.value" :error="command.error.value" :message="command.message.value" :can-retry="command.canRetry()" @recover="command.recover" @retry="command.retry" @close="command.close" />
    <section v-if="preview" class="legacy-content">
      <h4>当前映射下的内容 · 历史仍未核实</h4>
      <p>当前目标：{{ preview.targetKey }} v{{ preview.targetVersion }} · 旧记录 {{ preview.legacyDraftId }}</p>
      <p>标题：{{ preview.title ?? '不可见或未记录' }}。只展示当前允许读取的字段，不代表未展示内容为空。</p>
      <pre>{{ preview.dataJson }}</pre>
      <p>当前采用窗口截止：{{ preview.adoptUntilUtc }}。源文件和当前资格在复制时再次核对。</p>
      <el-alert v-if="!preview.canAdopt" title="当前状态或授权不允许采用为新草稿；旧记录仍保留。" type="warning" :closable="false" />
      <template v-else><el-input v-model="reason" aria-label="旧草稿采用说明" maxlength="500" :disabled="!!command.pending.value || command.busy.value" /><el-button :disabled="!!command.pending.value || command.busy.value || !reason.trim()" @click="adopt">复制为新本人草稿</el-button></template>
    </section>
    <section v-if="adopted" class="legacy-adopted">
      <h4>已复制为新草稿</h4><p>{{ adopted.formKey }} v{{ adopted.formVersion }} · {{ adopted.draftId }}；旧记录和历史来源继续保留。</p>
      <el-button @click="showEditor = true">打开新草稿</el-button><PersonalFormEditor v-if="showEditor" :draft-id="adopted.draftId" />
    </section>
    <el-pagination v-if="!error && total > 20" v-model:current-page="page" :total="total" :page-size="20" layout="prev, pager, next" @current-change="load" />
  </section>
</template>

<style scoped>
.legacy-drafts { padding: 12px 0; } p { max-width: 850px; line-height: 1.7; } a { margin-right: 16px; } .legacy-content, .legacy-adopted { margin-top: 20px; } pre { white-space: pre-wrap; overflow-wrap: anywhere; }
</style>
