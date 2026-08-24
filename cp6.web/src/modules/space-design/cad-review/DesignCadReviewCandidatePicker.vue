<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { isAxiosError } from 'axios'
import {
  designCadParseApi,
  type SpaceCadReviewCandidate,
} from '@/api/space/designCadParse'

const props = defineProps<{
  versionId: string
  floorLogicalId: string
  readonly: boolean
}>()

const emit = defineEmits<{
  close: []
  select: [sourceId: string, jobId: string]
  reparse: [sourceId: string]
}>()

const items = ref<SpaceCadReviewCandidate[]>([])
const loading = ref(true)
const error = ref('')
const truncated = ref(false)
const currentRevision = ref(0)

const currentItems = computed(() => items.value.filter(item => item.canLoadReview))
const historicalItems = computed(() => items.value.filter(item => !item.canLoadReview))

onMounted(() => void load())

async function load(): Promise<void> {
  loading.value = true
  error.value = ''
  try {
    const result = await designCadParseApi.listReviewCandidates(
      props.versionId,
      props.floorLogicalId,
    )
    items.value = result.items ?? []
    truncated.value = Boolean(result.truncated)
    currentRevision.value = result.currentContentRevision
  } catch (cause) {
    if (isAxiosError(cause)) {
      const detail = cause.response?.data?.detail
      error.value = typeof detail === 'string' && detail.trim()
        ? detail
        : cause.message
    } else {
      error.value = cause instanceof Error ? cause.message : '无法加载 CAD 解析结果。'
    }
  } finally {
    loading.value = false
  }
}

function finishedLabel(item: SpaceCadReviewCandidate): string {
  const value = item.finishedAtUtc || item.requestedAtUtc
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
</script>

<template>
  <div class="candidate-backdrop" role="presentation">
    <section
      class="candidate-dialog"
      role="dialog"
      aria-modal="true"
      aria-labelledby="cad-candidate-title"
      data-test="cad-review-candidate-picker"
    >
      <header>
        <div>
          <span class="eyebrow">CAD REVIEW HISTORY</span>
          <h2 id="cad-candidate-title">选择已有 CAD 解析结果</h2>
          <p>当前 Draft Revision {{ currentRevision }}。只有与它一致的结果可直接加载；历史结果必须重新解析。</p>
        </div>
        <button type="button" class="icon-button" aria-label="关闭 CAD 结果选择器" @click="emit('close')">×</button>
      </header>

      <div class="candidate-body" :aria-busy="loading">
        <p v-if="loading" class="state" role="status">正在加载当前楼层的 CAD 结果…</p>
        <p v-else-if="error" class="error" role="alert">{{ error }}</p>
        <template v-else>
          <section aria-labelledby="cad-current-results">
            <h3 id="cad-current-results">可直接加载</h3>
            <p v-if="!currentItems.length" class="state">暂无与当前 Draft Revision 一致的审核结果。</p>
            <article v-for="item in currentItems" :key="item.jobId" class="candidate current">
              <div>
                <strong>{{ item.sourceDisplayName }}</strong>
                <span>{{ item.sourceType }} · 完成于 {{ finishedLabel(item) }}</span>
                <small>首选路由 {{ item.preferredProviderKey || '未记录' }}{{ item.preferredProviderVersion ? ` @ ${item.preferredProviderVersion}` : '' }} · Mapping v{{ item.mappingProfileVersion }}</small>
              </div>
              <button
                type="button"
                class="primary"
                :aria-label="`加载 ${item.sourceDisplayName} 的审核结果`"
                @click="emit('select', item.sourceId, item.jobId)"
              >加载审核</button>
            </article>
          </section>

          <section aria-labelledby="cad-historical-results">
            <h3 id="cad-historical-results">历史结果</h3>
            <p v-if="!historicalItems.length" class="state">暂无历史结果。</p>
            <article v-for="item in historicalItems" :key="item.jobId" class="candidate stale">
              <div>
                <strong>{{ item.sourceDisplayName }}</strong>
                <span>{{ item.sourceType }} · 基于 Revision {{ item.baseContentRevision }} · {{ finishedLabel(item) }}</span>
                <small>{{ item.isCurrentRevision ? '审核工件当前不可用' : 'Draft 已前进，禁止直接加载或 Apply' }}</small>
              </div>
              <button
                type="button"
                :disabled="readonly"
                :title="readonly ? '只读模式不能启动重新解析' : '使用此来源重新确认坐标与映射'"
                :aria-label="`重新解析 ${item.sourceDisplayName}`"
                @click="emit('reparse', item.sourceId)"
              >重新解析</button>
            </article>
          </section>

          <p v-if="truncated" class="notice">仅显示最近 50 条匹配记录。</p>
        </template>
      </div>

      <footer>
        <span>选择操作只更新工作台上下文，不会写入 Draft。</span>
        <button type="button" @click="emit('close')">关闭</button>
      </footer>
    </section>
  </div>
</template>

<style scoped>
.candidate-backdrop { position:fixed; inset:0; z-index:1200; display:grid; place-items:center; padding:24px; background:rgba(2,8,18,.78); }
.candidate-dialog { width:min(920px,100%); max-height:calc(100vh - 48px); overflow:auto; border:1px solid #2a3950; border-radius:12px; color:#f4f7fb; background:#111a2b; box-shadow:0 28px 90px rgba(0,0,0,.55); }
header,footer { display:flex; align-items:center; justify-content:space-between; gap:24px; padding:18px 22px; border-bottom:1px solid #2a3950; }
footer { border-top:1px solid #2a3950; border-bottom:0; color:#aebbd0; font-size:14px; }
h2,h3,p { margin:0; }
h2 { margin:3px 0 5px; font-size:22px; }
h3 { margin-bottom:10px; font-size:17px; }
.eyebrow { color:#18c2c9; font-size:13px; font-weight:800; letter-spacing:.08em; }
.icon-button { width:44px; height:44px; padding:0; border:0; font-size:28px; background:transparent; }
.candidate-body { display:grid; gap:18px; padding:18px 22px; }
.candidate { display:flex; align-items:center; justify-content:space-between; gap:18px; padding:14px; border:1px solid #2a3950; border-radius:8px; background:#172236; }
.candidate + .candidate { margin-top:8px; }
.candidate > div { display:grid; gap:4px; min-width:0; }
.candidate strong { overflow-wrap:anywhere; font-size:16px; }
.candidate span,.candidate small,.state { color:#aebbd0; font-size:14px; line-height:1.45; }
.candidate.current { border-left:4px solid #18c2c9; }
.candidate.stale { border-left:4px solid #ffd27a; }
button { box-sizing:border-box; min-width:132px; min-height:44px; padding:8px 12px; border:1px solid #3b4d67; border-radius:6px; color:#f4f7fb; background:#172236; cursor:pointer; font:inherit; }
button:focus-visible { outline:3px solid #8cebf0; outline-offset:2px; }
button:disabled { cursor:not-allowed; opacity:.45; }
.primary { border-color:#18c2c9; color:#041014; background:#18c2c9; font-weight:800; }
.error { color:#ff8590; }
.notice { color:#ffd27a; font-size:14px; }
@media (max-width:680px) { header,footer,.candidate { align-items:flex-start; flex-direction:column; } .candidate button,footer button { width:100%; } }
</style>
