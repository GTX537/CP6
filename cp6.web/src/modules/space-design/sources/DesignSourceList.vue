<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  designSourcesApi,
  type SpaceDesignSource,
  type SpaceSourceRemovalPreview,
} from '@/api/space/designSources'

const props = defineProps<{
  versionId: string
  readonly: boolean
  refreshKey?: number
}>()

const emit = defineEmits<{
  sourceRemoved: [sourceId: string, versionContentRevision: number]
}>()

const sources = ref<SpaceDesignSource[]>([])
const previews = ref<Record<string, SpaceSourceRemovalPreview>>({})
const loading = ref(false)
const checkingId = ref('')
const removingId = ref('')

const referenceLabels: Record<string, string> = {
  VERSION_NOT_DRAFT: '当前版本不是可编辑 Draft',
  SOURCE_IN_PROGRESS: '来源仍在扫描或解析',
  ACTIVE_JOB_REFERENCE: '后台任务仍在运行',
  JOB_AUDIT_REFERENCE: '已完成任务审计',
  ARTIFACT_REFERENCE: '解析/预览工件',
  ISSUE_REFERENCE: '问题与诊断记录',
  CAD_PREPARATION_REFERENCE: 'CAD 启动确认记录',
  ACTIVE_GENERATION_REFERENCE: '生成任务仍处于活动状态',
  GENERATION_AUDIT_REFERENCE: '历史生成审计',
  UNDERLAY_REFERENCE: '底图或标定记录',
  DESIGN_REVISION_REFERENCE: '当前设计对象',
  DESIGN_METADATA_REFERENCE: '业务绑定或设计属性',
  IMPORT_AUDIT_REFERENCE: '导入命令审计',
}

onMounted(() => void load())
watch(
  () => [props.versionId, props.refreshKey] as const,
  () => void load(),
)

async function load(): Promise<void> {
  if (!props.versionId) return
  loading.value = true
  try {
    const page = await designSourcesApi.list(props.versionId)
    sources.value = page.items ?? []
    const activeIds = new Set(sources.value.map(source => source.id))
    previews.value = Object.fromEntries(
      Object.entries(previews.value).filter(([sourceId]) => activeIds.has(sourceId)),
    )
  } catch {
    sources.value = []
    previews.value = {}
  } finally {
    loading.value = false
  }
}

async function inspect(source: SpaceDesignSource): Promise<void> {
  checkingId.value = source.id
  try {
    previews.value = {
      ...previews.value,
      [source.id]: await designSourcesApi.getRemovalPreview(
        props.versionId,
        source.id,
      ),
    }
  } finally {
    checkingId.value = ''
  }
}

async function remove(source: SpaceDesignSource): Promise<void> {
  const preview = previews.value[source.id]
  if (!preview?.canRemove || props.readonly) return
  try {
    await ElMessageBox.confirm(
      preview.physicalFileRetained
        ? `确认将“${source.displayName}”移出当前工作台？来源墓碑、物理文件和审计证据会继续保留，不会级联删除历史。`
        : `确认将“${source.displayName}”移出当前工作台？审计证据会继续保留，不会级联删除历史。`,
      '移除来源',
      {
        type: 'warning',
        confirmButtonText: '确认移除',
        cancelButtonText: '返回',
      },
    )
  } catch (error) {
    if (error === 'cancel' || error === 'close') return
    throw error
  }

  removingId.value = source.id
  try {
    const result = await designSourcesApi.remove(
      props.versionId,
      source.id,
      {
        expectedContentRevision: preview.versionContentRevision,
        expectedSourceRowVersion: preview.sourceRowVersion,
      },
    )
    ElMessage.success(
      result.idempotentReplay ? '已恢复同一次来源移除结果' : '来源已移出工作台，证据仍保留',
    )
    emit('sourceRemoved', source.id, result.versionContentRevision)
    await load()
  } catch (error) {
    await inspect(source)
    throw error
  } finally {
    removingId.value = ''
  }
}

function referenceLabel(code: string): string {
  return referenceLabels[code] ?? code
}

function previewFor(sourceId: string): SpaceSourceRemovalPreview {
  const preview = previews.value[sourceId]
  if (!preview) throw new Error('Source removal preview is unavailable')
  return preview
}
</script>

<template>
  <section class="source-list" data-test="design-source-list" v-loading="loading">
    <header>
      <h3>已导入来源</h3>
      <button type="button" :disabled="loading" @click="load">刷新</button>
    </header>
    <p v-if="!loading && sources.length === 0" class="empty">当前 Draft 尚无来源。</p>
    <article v-for="source in sources" :key="source.id" class="source-item">
      <div class="source-item__heading">
        <strong :title="source.displayName">{{ source.displayName }}</strong>
        <span>{{ source.sourceType }} · {{ source.state }}</span>
      </div>
      <button
        type="button"
        :data-test="`inspect-source-${source.id}`"
        :disabled="checkingId === source.id || removingId === source.id"
        @click="inspect(source)"
      >{{ previews[source.id] ? '重新检查' : '检查移除条件' }}</button>

      <div
        v-if="previews[source.id]"
        class="removal-preview"
        :data-test="`source-removal-preview-${source.id}`"
        aria-live="polite"
      >
        <p :class="previewFor(source.id).canRemove ? 'ready' : 'blocked'">
          {{ previewFor(source.id).canRemove ? '可安全移出工作台' : '存在阻断引用，当前不能移除' }}
        </p>
        <ul v-if="previewFor(source.id).references.length">
          <li
            v-for="reference in previewFor(source.id).references"
            :key="`${reference.code}-${reference.blocksRemoval}`"
            :class="{ blocking: reference.blocksRemoval }"
          >
            {{ reference.blocksRemoval ? '阻断' : '保留' }} ·
            {{ referenceLabel(reference.code) }} × {{ reference.count }}
          </li>
        </ul>
        <p v-else class="evidence">未发现草稿、任务、元素或审计引用。</p>
        <p v-if="previewFor(source.id).physicalFileRetained" class="evidence">
          物理文件仍由保留策略管理，本操作不会立即删除文件。
        </p>
        <button
          type="button"
          class="danger"
          :data-test="`remove-source-${source.id}`"
          :disabled="readonly || !previewFor(source.id).canRemove || removingId === source.id"
          @click="remove(source)"
        >移出工作台</button>
      </div>
    </article>
  </section>
</template>

<style scoped>
.source-list { margin-top:16px; border-top:1px solid var(--space-studio-border); padding-top:12px; }
.source-list header { display:flex; align-items:center; justify-content:space-between; gap:8px; }
.source-list h3 { margin:0; color:var(--space-studio-text); font-size:14px; }
.source-list button { min-height:44px; margin-top:8px; padding:0 12px; border:1px solid var(--space-studio-border); border-radius:6px; color:var(--space-studio-text); background:var(--space-studio-panel); cursor:pointer; }
.source-list button:disabled { cursor:not-allowed; opacity:.55; }
.source-list button:focus-visible { outline:3px solid var(--space-studio-focus); outline-offset:2px; }
.source-list header button { width:auto; min-width:64px; margin:0; }
.source-item { margin-top:10px; padding:10px; border:1px solid var(--space-studio-border); border-radius:7px; background:var(--space-studio-panel-raised); }
.source-item__heading { min-width:0; }
.source-item__heading strong,.source-item__heading span { display:block; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
.source-item__heading strong { color:var(--space-studio-text); font-size:14px; }
.source-item__heading span { margin-top:3px; color:var(--space-studio-muted); font-size:13px; }
.removal-preview { margin-top:10px; padding-top:8px; border-top:1px solid var(--space-studio-border); }
.removal-preview p { margin:4px 0; font-size:13px; }
.removal-preview ul { margin:8px 0; padding-left:18px; }
.removal-preview li { margin:4px 0; color:var(--space-studio-muted); font-size:13px; line-height:1.4; }
.removal-preview li.blocking,.blocked { color:var(--space-studio-blocking); }
.ready { color:var(--space-studio-success); }
.evidence,.empty { color:var(--space-studio-muted); }
button.danger { border-color:var(--space-studio-blocking); color:var(--space-studio-blocking); background:transparent; }
</style>
