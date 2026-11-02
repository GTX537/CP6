<template>
  <section class="comparison-panel" aria-label="文件差异预览">
    <h3>{{ t('文件差异预览') }}</h3>
    <p>{{ t('选择建议路径对应的原文件，或粘贴原文本。仅比较你提供的内容，未检查磁盘，也不会应用代码。') }}</p>
    <el-button v-if="!files.length" v-permission="'pub-codegen:view'" :loading="loading" @click="prepare">{{ t('选择比较文件') }}</el-button>
    <template v-else>
      <fieldset v-for="file in files" :key="file.targetPath" class="source-file">
        <legend>{{ file.targetPath }}</legend>
        <el-checkbox v-model="file.provided">{{ t('提供原文件') }}</el-checkbox>
        <label class="file-picker">{{ t('读取 UTF-8 文件') }}
          <input type="file" :aria-label="`${file.fileName} 原文件`" :disabled="loading || reading" @change="readFile(file, $event)" />
        </label>
        <el-input v-if="file.provided" v-model="file.content" type="textarea" :rows="5" :disabled="reading"
          :aria-label="`${file.fileName} 原文本`" :placeholder="t('空文本表示已提供空文件')" />
        <el-checkbox v-model="file.overwrite" :disabled="!file.provided">{{ t('允许覆盖此文件的框架区') }}</el-checkbox>
      </fieldset>
      <p>{{ t('未提供原文件时，只显示建议文本；不代表目标文件不存在。自定义区无法安全保留时，禁止覆盖。') }}</p>
      <el-button v-permission="'pub-codegen:view'" :loading="loading" :disabled="reading" @click="compare">{{ t('比较已提供文件') }}</el-button>
    </template>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <div v-if="result" class="comparison-result">
      <p><strong>{{ t('未应用') }}</strong> · {{ t('比较摘要') }}：{{ result.comparisonDigest }}</p>
      <article v-for="output in result.outputs" :key="output.targetPath">
        <h4>{{ output.targetPath }} — {{ statusText(output.status) }}</h4>
        <p v-if="output.reason">{{ output.reason }}</p>
        <p v-if="output.preservedCustomBlocks">{{ t('已保留自定义区') }}：{{ output.preservedCustomBlocks }}</p>
        <template v-if="output.difference && (output.difference.removedLines.length || output.difference.addedLines.length)">
          <p>{{ t('变化从原文件此行开始') }}：{{ output.difference.startLine }}</p>
          <div class="comparison-columns">
            <div><h5>{{ t('原文本区间') }}</h5><pre class="difference-before">{{ output.difference.removedLines.join('') }}</pre></div>
            <div><h5>{{ t('候选文本区间') }}</h5><pre class="difference-after">{{ output.difference.addedLines.join('') }}</pre></div>
          </div>
        </template>
        <details v-if="output.content !== null"><summary>{{ t('完整候选文本') }}</summary><pre>{{ output.content }}</pre></details>
      </article>
    </div>
  </section>
</template>

<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { codeGenApi, type CodeGenColumnInput, type CodeGenComparison, type CodeGenPreview, type CodeGenTableInput } from '@/api/pub/codegen'

const props = defineProps<{ table: CodeGenTableInput; columns: CodeGenColumnInput[]; preview: CodeGenPreview }>()
const { t } = useI18n()
interface SourceFile { targetPath: string; fileName: string; provided: boolean; content: string; overwrite: boolean }
const files = ref<SourceFile[]>([])
const result = ref<CodeGenComparison | null>(null)
const loading = ref(false), reading = ref(false), error = ref('')
let revision = 0, disposed = false
watch(files, () => { revision++; result.value = null; error.value = '' }, { deep: true, flush: 'sync' })
onBeforeUnmount(() => { disposed = true })

function statusText(status: CodeGenComparison['outputs'][number]['status']) {
  return t({ NoBaseline: '未提供原文件', Unchanged: '内容相同', OverwriteAllowed: '已允许覆盖框架区',
    OverwriteBlocked: '尚未允许覆盖', CustomBlockConflict: '自定义区冲突，禁止覆盖' }[status])
}

async function requestComparison(preparing: boolean) {
  if (loading.value) return
  const requestedRevision = revision
  loading.value = true; result.value = null; error.value = ''
  try {
    const supplied = preparing ? [] : files.value.filter(file => file.provided)
    const response = await codeGenApi.compare({ ...props.table }, props.columns.map(column => ({ ...column })), props.preview,
      supplied.map(file => ({ targetPath: file.targetPath, content: file.content })),
      supplied.filter(file => file.overwrite).map(file => file.targetPath))
    if (disposed || revision !== requestedRevision) return
    if (preparing) files.value = response.outputs.map(file => ({ targetPath: file.targetPath, fileName: file.fileName,
      provided: false, content: '', overwrite: false }))
    else result.value = response
  } catch (failure: unknown) {
    if (disposed || revision !== requestedRevision) return
    const message = (failure as { response?: { data?: { message?: unknown } } }).response?.data?.message
    error.value = typeof message === 'string' ? message : t('文件比较失败，请检查输入后重试')
  } finally { loading.value = false }
}
const prepare = () => requestComparison(true)
const compare = () => requestComparison(false)

async function readFile(target: SourceFile, event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return
  const requestedRevision = revision
  reading.value = true; error.value = ''
  try {
    if (file.size > 1024 * 1024) throw new Error('FILE_TOO_LARGE')
    const content = new TextDecoder('utf-8', { fatal: true, ignoreBOM: true }).decode(await file.arrayBuffer())
    if (disposed || revision !== requestedRevision) return
    target.content = content; target.provided = true; target.overwrite = false
  } catch {
    if (!disposed && revision === requestedRevision) error.value = t('文件读取失败，请选择不超过 1 MiB 的 UTF-8 文本文件')
  } finally { reading.value = false; input.value = '' }
}
</script>

<style scoped>
.comparison-panel { margin-top: 24px; }
.source-file { border: 1px solid var(--el-border-color); margin: 12px 0; padding: 12px; }
legend, h4 { overflow-wrap: anywhere; }
.file-picker { display: block; margin: 8px 0; }
.comparison-columns { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 12px; }
pre { white-space: pre-wrap; overflow-wrap: anywhere; max-height: 400px; overflow: auto; padding: 12px; background: var(--el-fill-color-light); }
.difference-before { border-left: 3px solid var(--el-color-danger); }
.difference-after { border-left: 3px solid var(--el-color-success); }
@media (max-width: 700px) { .comparison-columns { grid-template-columns: 1fr; } }
</style>
