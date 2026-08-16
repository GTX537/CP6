<script setup lang="ts">
import { ref } from 'vue'
import {
  spaceStudioComponentGroups,
  spaceStudioComponentPresets,
  type SpaceStudioComponentPresetId,
} from '@/modules/space-design/components/staticComponentCatalog'
import DesignSourceList from '@/modules/space-design/sources/DesignSourceList.vue'

defineProps<{
  versionId?: string
  sourceRefreshKey?: number
  parseStatus?: string
  parseProgress?: number
  parseElapsed?: string
  parseError?: string
  hasUnderlay: boolean
  calibrated: boolean
  readonly: boolean
  underlayVisible: boolean
  underlayOpacity: number
  underlayLocked: boolean
}>()

const emit = defineEmits<{
  chooseUnderlay: []
  calibrateUnderlay: []
  removeUnderlay: []
  chooseCad: []
  downloadTemplate: []
  openCadReview: []
  cancelParse: []
  retryParse: []
  openRuleOnly: []
  createComponent: [presetId: SpaceStudioComponentPresetId]
  underlayVisibilityChange: [visible: boolean]
  underlayOpacityChange: [opacity: number]
  underlayLockChange: [locked: boolean]
  sourceRemoved: [sourceId: string, versionContentRevision: number]
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

function emitChecked(
  event: Event,
  name: 'underlayVisibilityChange' | 'underlayLockChange',
): void {
  const checked = (event.currentTarget as HTMLInputElement).checked
  if (name === 'underlayVisibilityChange') {
    emit('underlayVisibilityChange', checked)
  } else {
    emit('underlayLockChange', checked)
  }
}

function emitOpacity(event: Event): void {
  const value = Number((event.currentTarget as HTMLInputElement).value)
  if (Number.isInteger(value) && value >= 0 && value <= 100) {
    emit('underlayOpacityChange', value)
  }
}

function sourceRemoved(sourceId: string, versionContentRevision: number): void {
  emit('sourceRemoved', sourceId, versionContentRevision)
}
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
        <button
          v-if="hasUnderlay"
          type="button"
          data-test="calibrate-underlay"
          :disabled="readonly || underlayLocked"
          :title="underlayLocked ? '请先在图层中解锁底图' : undefined"
          @click="emit('calibrateUnderlay')"
        >{{ calibrated ? '重新标定底图' : '标定底图' }}</button>
        <button
          v-if="hasUnderlay"
          type="button"
          data-test="remove-underlay"
          :disabled="readonly"
          @click="emit('removeUnderlay')"
        >移除底图</button>
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
        <DesignSourceList
          :version-id="versionId ?? ''"
          :readonly="readonly"
          :refresh-key="sourceRefreshKey"
          @source-removed="sourceRemoved"
        />
      </template>

      <template v-else-if="activeMode === 'assets'">
        <h2>构件库</h2>
        <p>库区、巷道、货架、托盘与静态建筑/设备构件。</p>
        <button type="button" class="primary" :disabled="readonly" @click="emit('openRuleOnly')">
          从 CAD 规则生成构件
        </button>
        <section
          v-for="group in spaceStudioComponentGroups"
          :key="group.id"
          class="component-group"
          :aria-label="group.label"
        >
          <h3>{{ group.label }}</h3>
          <div class="component-grid">
            <button
              v-for="preset in spaceStudioComponentPresets.filter(item => item.group === group.id)"
              :key="preset.id"
              type="button"
              :data-test="`component-preset-${preset.id}`"
              :disabled="readonly"
              :aria-label="`创建${preset.label}`"
              @click="emit('createComponent', preset.id)"
            >
              + {{ preset.label }}
            </button>
          </div>
        </section>
        <div class="empty-note">所有设备预设均为静态几何、业务编码和自定义属性，不含实时状态或运动。构件通过同一租约、Revision 与 CommandBatch 权威链保存。</div>
        <slot name="assets" />
      </template>

      <template v-else-if="activeMode === 'layers'">
        <h2>图层</h2>
        <fieldset class="underlay-layer-controls">
          <legend>底图</legend>
          <label class="layer-toggle">
            <input
              data-test="underlay-visible"
              type="checkbox"
              :checked="underlayVisible"
              :disabled="!hasUnderlay"
              @change="emitChecked($event, 'underlayVisibilityChange')"
            />
            显示底图
          </label>
          <label class="opacity-label" for="space-underlay-opacity">
            <span>透明度</span>
            <output for="space-underlay-opacity">{{ underlayOpacity }}%</output>
          </label>
          <input
            id="space-underlay-opacity"
            data-test="underlay-opacity"
            type="range"
            min="0"
            max="100"
            step="5"
            :value="underlayOpacity"
            :disabled="!hasUnderlay"
            aria-label="底图透明度"
            @input="emitOpacity"
          />
          <label class="layer-toggle">
            <input
              data-test="underlay-locked"
              type="checkbox"
              :checked="underlayLocked"
              :disabled="!hasUnderlay"
              @change="emitChecked($event, 'underlayLockChange')"
            />
            锁定底图
          </label>
          <p class="layer-state" aria-live="polite">
            {{ hasUnderlay
              ? (underlayLocked ? '底图已锁定，解锁后可重新标定' : '底图已解锁，可进行标定')
              : '导入底图后可调整显示' }}
          </p>
        </fieldset>
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
.studio-modebar small { margin-top:3px; font-size:13px; }
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
.component-group { margin-top:14px; }
.component-group h3 { margin:0; color:var(--space-studio-text); font-size:13px; }
label { display:block; margin:12px 0; color:var(--space-studio-text); }
.underlay-layer-controls { margin:0 0 14px; padding:10px 12px; border:1px solid var(--space-studio-border); border-radius:7px; }
.underlay-layer-controls legend { padding:0 5px; color:var(--space-studio-text); font-size:14px; font-weight:700; }
.layer-toggle { box-sizing:border-box; display:flex; align-items:center; gap:10px; min-height:44px; margin:0; cursor:pointer; }
.layer-toggle input { width:20px; height:20px; margin:0; accent-color:var(--space-studio-accent); }
.opacity-label { display:flex; justify-content:space-between; margin:8px 0 0; }
.opacity-label output { color:var(--space-studio-text); font-variant-numeric:tabular-nums; }
input[type='range'] { box-sizing:border-box; width:100%; min-height:44px; margin:0; accent-color:var(--space-studio-accent); cursor:pointer; }
input:disabled { cursor:not-allowed; opacity:.55; }
.layer-state { min-height:42px; margin:4px 0 0; font-size:13px; }
@media (max-width:1279px) {
  .studio-context { grid-template-columns:52px 0; min-width:52px; overflow:hidden; }
  .studio-context-pane { display:none; }
}
</style>
