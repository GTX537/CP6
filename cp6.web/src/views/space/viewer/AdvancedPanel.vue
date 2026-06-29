<!-- cp6.web/src/views/space/viewer/AdvancedPanel.vue -->
<template>
  <div class="advanced-panel">
    <div class="ap-section">
      <div class="ap-title">{{ t('拣货路径') }}</div>
      <div class="ap-row">
        <input v-model="taskNo" class="ap-input" :placeholder="t('拣货单号')" />
        <button class="ap-btn" @click="$emit('load-path', taskNo)">{{ t('加载') }}</button>
      </div>
      <div class="ap-row" v-if="pathLoaded">
        <button class="ap-btn" @click="$emit('play')">▶</button>
        <button class="ap-btn" @click="$emit('pause')">⏸</button>
        <button class="ap-btn" @click="$emit('step')">⏭</button>
        <button class="ap-btn" @click="$emit('replay')">↺</button>
        <select class="ap-input" @change="onSpeed">
          <option value="2000">0.5x</option>
          <option value="4000" selected>1x</option>
          <option value="8000">2x</option>
        </select>
      </div>
      <div class="ap-info" v-if="pathInfo">{{ pathInfo }}</div>
    </div>

    <div class="ap-section">
      <div class="ap-title">{{ t('作业热图') }}</div>
      <div class="ap-row">
        <label class="ap-check"><input type="checkbox" :checked="workloadOn" @change="$emit('toggle-workload')" />{{ t('开启') }}</label>
        <input type="date" v-model="from" class="ap-input" />
        <input type="date" v-model="to" class="ap-input" />
        <button class="ap-btn" @click="$emit('apply-workload', { from, to })">{{ t('应用') }}</button>
      </div>
    </div>

    <div class="ap-section">
      <div class="ap-title">{{ t('设备示意') }}</div>
      <label class="ap-check"><input type="checkbox" :checked="deviceOn" @change="$emit('toggle-device')" />{{ t('显示设备') }}</label>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
const { t } = useI18n()
defineProps<{ pathLoaded: boolean; pathInfo: string; workloadOn: boolean; deviceOn: boolean }>()
const emit = defineEmits<{
  (e: 'load-path', taskNo: string): void
  (e: 'play'): void; (e: 'pause'): void; (e: 'step'): void; (e: 'replay'): void
  (e: 'speed', v: number): void
  (e: 'toggle-workload'): void
  (e: 'apply-workload', win: { from: string; to: string }): void
  (e: 'toggle-device'): void
}>()
const taskNo = ref('')
const today = new Date().toISOString().slice(0, 10)
const from = ref(today)
const to = ref(today)
function onSpeed(ev: Event): void {
  emit('speed', Number((ev.target as HTMLSelectElement).value))
}
</script>

<style scoped>
.advanced-panel { position: absolute; right: 16px; bottom: 16px; background: rgba(12,12,28,.92);
  border: 1px solid rgba(126,87,194,.4); border-radius: 6px; color: #e0e0e0; font-size: 12px; padding: 8px 10px; z-index: 10; width: 240px; }
.ap-section { margin-bottom: 8px; }
.ap-title { color: #b39ddb; font-weight: 600; margin-bottom: 4px; }
.ap-row { display: flex; align-items: center; gap: 4px; flex-wrap: wrap; margin-bottom: 4px; }
.ap-input { background: #1a1a2e; color: #e0e0e0; border: 1px solid #37474f; border-radius: 4px; padding: 2px 4px; width: 70px; }
.ap-btn { background: transparent; color: #b39ddb; border: 1px solid #5e35b1; border-radius: 4px; cursor: pointer; padding: 2px 6px; }
.ap-btn:hover { background: rgba(126,87,194,.2); }
.ap-check { display: flex; align-items: center; gap: 4px; }
.ap-info { color: #80cbc4; margin-top: 2px; }
</style>
