<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { IPreviewSpaceLocationCodesResponse } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'

interface ZoneOption {
  logicalId: string
  code: string
  name?: string
}

const props = defineProps<{
  zones: readonly ZoneOption[]
  preview: IPreviewSpaceLocationCodesResponse | null
  busy?: boolean
  readonly?: boolean
}>()

const emit = defineEmits<{
  preview: [request: { mode: string; scopeZoneLogicalId?: string }]
  apply: []
}>()

const mode = ref('fill-empty')
const scopeZoneLogicalId = ref('')
const confirmed = ref(false)
const visibleItems = computed(() => props.preview?.items.slice(0, 200) ?? [])
const canApply = computed(
  () => Boolean(
    props.preview &&
    props.preview.changedCount > 0 &&
    confirmed.value &&
    !props.busy &&
    !props.readonly,
  ),
)

watch(
  () => props.preview?.proposalHash,
  () => { confirmed.value = false },
)

function requestPreview() {
  confirmed.value = false
  emit('preview', {
    mode: mode.value,
    scopeZoneLogicalId: scopeZoneLogicalId.value || undefined,
  })
}

function decisionLabel(decision: string) {
  if (decision === 'modify') return '将修改'
  if (decision === 'protected') return '受保护'
  return '保持不变'
}

function reasonLabel(reason: string) {
  return {
    'wms-bound': '已绑定 WMS',
    adopted: '采纳编码',
    imported: '导入编码',
    manual: '手工编码',
    'already-coded': '已有编码',
    'matches-rule': '符合规则',
    'fill-empty': '填充空编码',
    rebuild: '按规则重建',
  }[reason] ?? reason
}
</script>

<template>
  <section class="coding-panel" data-test="location-coding-panel">
    <header>
      <strong>库位批量编码</strong>
      <span>仅写入当前 Draft，不直接修改 Published / WMS</span>
    </header>

    <fieldset :disabled="busy || readonly">
      <legend>编码模式</legend>
      <label>
        <input v-model="mode" type="radio" value="fill-empty" />
        仅填充空编码
      </label>
      <label>
        <input v-model="mode" type="radio" value="rebuild" />
        重建可修改编码
      </label>
      <label class="scope">
        范围
        <select v-model="scopeZoneLogicalId" data-test="coding-scope">
          <option value="">当前楼层全部库区</option>
          <option v-for="zone in zones" :key="zone.logicalId" :value="zone.logicalId">
            {{ zone.code }} · {{ zone.name || zone.code }}
          </option>
        </select>
      </label>
      <button
        type="button"
        data-test="preview-location-codes"
        :disabled="busy || readonly || zones.length === 0"
        @click="requestPreview"
      >
        {{ busy ? '处理中…' : '生成编码预览' }}
      </button>
    </fieldset>

    <div v-if="preview" class="proposal" aria-live="polite">
      <div class="summary">
        <span class="changed">将修改 {{ preview.changedCount }}</span>
        <span>保持 {{ preview.unchangedCount }}</span>
        <span class="protected">受保护 {{ preview.protectedCount }}</span>
      </div>
      <p class="rule-summary">
        规则：{{ preview.rules.map((rule) => rule.ruleName).join('、') || '无' }}
      </p>
      <div class="table-scroll">
        <table>
          <thead>
            <tr><th>货架 / 坐标</th><th>当前编码</th><th>预览编码</th><th>决策</th></tr>
          </thead>
          <tbody>
            <tr v-for="item in visibleItems" :key="item.locationLogicalId">
              <td>{{ item.rackCode }} · C{{ item.columnNo }} L{{ item.levelNo }} D{{ item.depthNo }}</td>
              <td>{{ item.currentCode || '—' }}</td>
              <td>{{ item.proposedCode || '—' }}</td>
              <td :class="`decision-${item.decision}`">
                {{ decisionLabel(item.decision) }} · {{ reasonLabel(item.reason) }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <p v-if="preview.items.length > visibleItems.length" class="truncated">
        为保证检查器性能，仅展示前 {{ visibleItems.length }} 项；Apply 仍绑定完整 Proposal Hash。
      </p>
      <label class="confirm">
        <input v-model="confirmed" type="checkbox" data-test="confirm-location-codes" />
        我已复核预览，确认将 {{ preview.changedCount }} 个编码原子写入当前 Draft
      </label>
      <button
        v-permission="'space:model:edit'"
        type="button"
        class="apply"
        data-test="apply-location-codes"
        :disabled="!canApply"
        @click="emit('apply')"
      >
        确认 Apply
      </button>
    </div>
  </section>
</template>

<style scoped>
.coding-panel{display:flex;flex-direction:column;gap:12px;padding:16px;border-top:1px solid var(--space-studio-border,#cbd5e1);color:var(--space-studio-text,#f4f7fb)}
header{display:flex;flex-direction:column;gap:4px} header span,.rule-summary,.truncated{font-size:13px;color:var(--space-studio-muted,#aebbd0)}
fieldset{display:flex;flex-direction:column;gap:9px;margin:0;padding:12px;border:1px solid var(--space-studio-border,#cbd5e1);border-radius:8px} legend{padding:0 6px;font-size:13px}
label{display:flex;align-items:center;gap:8px;font-size:14px}.scope{align-items:stretch;flex-direction:column}.scope select{min-height:44px;padding:0 10px;border:1px solid var(--space-studio-border,#cbd5e1);border-radius:6px;background:var(--space-studio-panel-raised,#172236);color:inherit}
button{min-height:44px;border:1px solid var(--space-studio-border,#cbd5e1);border-radius:6px;background:var(--space-studio-panel-raised,#172236);color:inherit;cursor:pointer}button:disabled{cursor:not-allowed;opacity:.55}.apply{background:var(--space-studio-accent,#18c2c9);color:#062b2d;font-weight:700}
.proposal,.summary{display:flex;flex-direction:column;gap:9px}.summary{flex-direction:row;flex-wrap:wrap}.summary span{padding:4px 8px;border-radius:999px;background:var(--space-studio-panel-raised,#172236);font-size:13px}.changed{color:var(--space-studio-success,#45d391)}.protected{color:var(--space-studio-warning,#ffbf5b)}
.table-scroll{max-height:320px;overflow:auto;border:1px solid var(--space-studio-border,#cbd5e1);border-radius:6px}table{width:100%;border-collapse:collapse;font-size:13px}th,td{padding:8px;text-align:left;border-bottom:1px solid var(--space-studio-border,#cbd5e1)}th{position:sticky;top:0;background:var(--space-studio-panel-raised,#172236)}.decision-modify{color:var(--space-studio-success,#45d391)}.decision-protected{color:var(--space-studio-warning,#ffbf5b)}.confirm{align-items:flex-start;line-height:1.45}.confirm input{width:20px;height:20px;flex:0 0 auto}
</style>
