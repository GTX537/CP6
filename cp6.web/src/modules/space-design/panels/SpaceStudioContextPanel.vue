<script setup lang="ts">
import { ref } from 'vue'

defineProps<{
  parseStatus?: string
  parseProgress?: number
  parseElapsed?: string
  parseError?: string
  hasUnderlay: boolean
  calibrated: boolean
  readonly: boolean
}>()

const emit = defineEmits<{
  chooseUnderlay: []
  chooseCad: []
  downloadTemplate: []
  openCadReview: []
  cancelParse: []
  retryParse: []
  openRuleOnly: []
  createComponent: [elementType: string]
}>()

type Mode = 'source' | 'assets' | 'layers' | 'history' | 'settings'
const activeMode = ref<Mode>('source')
const modes: Array<{ id: Mode; label: string; glyph: string }> = [
  { id: 'source', label: '来源', glyph: '源' },
  { id: 'assets', label: '构件', glyph: '件' },
  { id: 'layers', label: '图层', glyph: '层' },
  { id: 'history', label: '历史', glyph: '史' },
  { id: 'settings', label: '设置', glyph: '设' },
]
</script>

<template>
  <aside class="studio-context" aria-label="Space Studio 模式与上下文">
    <nav class="studio-modebar" aria-label="建模模式">
      <button
        v-for="mode in modes"
        :key="mode.id"
        type="button"
        :class="{ active: activeMode === mode.id }"
        :aria-pressed="activeMode === mode.id"
        @click="activeMode = mode.id"
      >
        <span aria-hidden="true">{{ mode.glyph }}</span>
        <small>{{ mode.label }}</small>
      </button>
    </nav>

    <section class="studio-context-pane">
      <template v-if="activeMode === 'source'">
        <h2>来源</h2>
        <p>导入 CAD、Excel 或底图。后台解析完成后会自动加载审核空间。</p>
        <button type="button" class="primary" :disabled="readonly" @click="emit('chooseUnderlay')">
          PDF / 图片底图
        </button>
        <button type="button" class="primary" :disabled="readonly" @click="emit('chooseCad')">
          上传 DWG / DXF
        </button>
        <button type="button" @click="emit('downloadTemplate')">下载标准 Excel</button>
        <button type="button" @click="emit('openCadReview')">打开 CAD 审核</button>

        <div v-if="parseStatus" class="parse-card" aria-live="polite">
          <div class="parse-card__title">CAD 解析 · {{ parseStatus }}</div>
          <progress :value="parseProgress ?? 0" max="100" />
          <div>{{ parseProgress ?? 0 }}% · {{ parseElapsed || '0s' }}</div>
          <p v-if="parseError" class="blocking">{{ parseError }}</p>
          <button v-if="parseStatus === 'Queued' || parseStatus === 'Running'" type="button" @click="emit('cancelParse')">
            取消解析
          </button>
          <button v-else-if="parseStatus === 'Failed' || parseStatus === 'Cancelled'" type="button" @click="emit('retryParse')">
            重试解析
          </button>
        </div>
        <div class="source-state">
          底图：{{ hasUnderlay ? (calibrated ? '已标定' : '待标定') : '未导入' }}
        </div>
      </template>

      <template v-else-if="activeMode === 'assets'">
        <h2>构件库</h2>
        <p>墙、柱、门、月台、静态设备与货架模板。</p>
        <button type="button" class="primary" :disabled="readonly" @click="emit('openRuleOnly')">
          从 CAD 规则生成构件
        </button>
        <div class="component-grid" aria-label="快速创建构件">
          <button v-for="type in ['Wall', 'Column', 'Door', 'Dock', 'StaticEquipment']" :key="type" type="button" :disabled="readonly" @click="emit('createComponent', type)">
            + {{ type }}
          </button>
        </div>
        <div class="empty-note">构件会落在当前指针附近并通过同一租约、Revision 与 CommandBatch 权威链保存；创建后可在右侧属性面板精调。</div>
      </template>

      <template v-else-if="activeMode === 'layers'">
        <h2>图层</h2>
        <label><input type="checkbox" checked /> 底图</label>
        <label><input type="checkbox" checked /> 库区与巷道</label>
        <label><input type="checkbox" checked /> 货架与库位</label>
        <label><input type="checkbox" checked /> 问题标记</label>
      </template>

      <template v-else-if="activeMode === 'history'">
        <h2>历史</h2>
        <p>已保存命令通过顶部撤销/重做执行补偿批次。</p>
      </template>

      <template v-else>
        <h2>设置</h2>
        <p>工作台显示、快捷键和高级工件回退入口。</p>
        <slot name="settings" />
      </template>
    </section>
  </aside>
</template>

<style scoped>
.studio-context { display:grid; grid-template-columns:52px 244px; min-width:296px; min-height:0; border-right:1px solid var(--space-studio-border); background:var(--space-studio-panel); }
.studio-modebar { display:flex; flex-direction:column; align-items:stretch; background:var(--space-studio-rail); border-right:1px solid var(--space-studio-border); }
.studio-modebar button { min-height:58px; border:0; border-left:3px solid transparent; color:var(--space-studio-muted); background:transparent; cursor:pointer; }
.studio-modebar button.active { border-left-color:var(--space-studio-accent); color:var(--space-studio-text); background:rgba(12,181,190,.10); }
.studio-modebar span,.studio-modebar small { display:block; }
.studio-modebar span { font-size:15px; font-weight:700; }
.studio-modebar small { margin-top:3px; font-size:12px; }
.studio-context-pane { padding:16px; overflow:auto; color:var(--space-studio-text); }
h2 { margin:0 0 10px; font-size:16px; }
p,.source-state,.empty-note,label { font-size:14px; line-height:1.5; color:var(--space-studio-muted); }
button { width:100%; min-height:44px; margin-top:8px; padding:0 12px; border:1px solid var(--space-studio-border); border-radius:6px; color:var(--space-studio-text); background:var(--space-studio-panel-raised); cursor:pointer; }
button.primary { border-color:var(--space-studio-accent); background:var(--space-studio-accent); color:#062f33; font-weight:700; }
button:focus-visible,input:focus-visible { outline:3px solid var(--space-studio-focus); outline-offset:2px; }
.parse-card { margin-top:16px; padding:12px; border:1px solid var(--space-studio-border); border-radius:7px; background:var(--space-studio-panel-raised); font-size:13px; }
.parse-card__title { margin-bottom:8px; font-weight:700; }
progress { width:100%; accent-color:var(--space-studio-accent); }
.blocking { color:var(--space-studio-blocking); }
.source-state,.empty-note { margin-top:16px; padding:10px; border-radius:6px; background:rgba(148,163,184,.08); }
.component-grid { display:grid; grid-template-columns:1fr 1fr; gap:8px; margin-top:12px; }
.component-grid button { margin-top:0; }
label { display:block; margin:12px 0; color:var(--space-studio-text); }
@media (max-width:1279px) {
  .studio-context { grid-template-columns:52px 0; min-width:52px; overflow:hidden; }
  .studio-context-pane { display:none; }
}
</style>
