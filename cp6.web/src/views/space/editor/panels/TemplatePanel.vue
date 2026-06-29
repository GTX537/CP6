<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { templateApi } from '@/api/space/template'
import type { TemplateVO } from '@/types/space/scene'
import type { RackTemplate } from '@/space-editor/generate/genRack'

const { t } = useI18n()

export interface TemplatePanelSelection {
  template: RackTemplate
  arrayParams: {
    rows: number
    racksPerRow: number
    rowGap: number
    rackGap: number
    aisleBetweenRows: boolean
  }
}

const emit = defineEmits<{
  select: [sel: TemplatePanelSelection]
}>()

const templates = ref<TemplateVO[]>([])
const selected = ref<TemplateVO | null>(null)

const form = ref({
  rows: 1,
  racksPerRow: 1,
  rowGap: 2000,
  rackGap: 1000,
  aisleBetweenRows: false,
})

onMounted(async () => {
  try {
    const res = await templateApi.list()
    templates.value = res.data ?? []
  } catch {
    ElMessage.error(t('加载模板失败'))
  }
})

function selectTemplate(tpl: TemplateVO): void {
  selected.value = tpl
}

interface RawParams {
  cols?: number
  levels?: number
  depthCount?: number
  cellW?: number
  cellH?: number
  cellD?: number
}

function parseTpl(tpl: TemplateVO): RackTemplate | null {
  try {
    const p = JSON.parse(tpl.params) as RawParams
    if (!tpl.id) return null
    return {
      id: tpl.id,
      cols: p.cols ?? 4,
      levels: p.levels ?? 5,
      depthCount: p.depthCount ?? 1,
      cellW: p.cellW ?? 1000,
      cellH: p.cellH ?? 1200,
      cellD: p.cellD ?? 800,
    }
  } catch {
    return null
  }
}

function startPlace(): void {
  if (!selected.value) {
    ElMessage.warning(t('请先选择模板'))
    return
  }
  const tpl = parseTpl(selected.value)
  if (!tpl) {
    ElMessage.error(t('模板参数格式错误'))
    return
  }
  emit('select', {
    template: tpl,
    arrayParams: { ...form.value },
  })
}
</script>

<template>
  <div class="template-panel">
    <div class="panel-title">{{ t('模板库') }}</div>

    <div class="tpl-list">
      <div
        v-for="tpl in templates"
        :key="tpl.id"
        :class="['tpl-item', { active: selected?.id === tpl.id }]"
        @click="selectTemplate(tpl)"
      >
        <span class="tpl-name">{{ tpl.templateName }}</span>
        <span class="tpl-code">{{ tpl.templateCode }}</span>
      </div>
      <div v-if="templates.length === 0" class="empty-tip">{{ t('暂无模板') }}</div>
    </div>

    <template v-if="selected">
      <div class="panel-section">{{ t('阵列参数') }}</div>
      <el-form label-width="80px" size="small" class="array-form">
        <el-form-item :label="t('排数')">
          <el-input-number v-model="form.rows" :min="1" :max="50" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="t('每排架数')">
          <el-input-number v-model="form.racksPerRow" :min="1" :max="100" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="t('排间距mm')">
          <el-input-number v-model="form.rowGap" :min="0" :step="500" style="width: 100%" />
        </el-form-item>
        <el-form-item :label="t('架间距mm')">
          <el-input-number v-model="form.rackGap" :min="0" :step="200" style="width: 100%" />
        </el-form-item>
        <el-form-item>
          <el-checkbox v-model="form.aisleBetweenRows">{{ t('排间生成巷道') }}</el-checkbox>
        </el-form-item>
      </el-form>

      <el-button type="primary" style="width: 100%; margin-top: 8px" @click="startPlace">
        {{ t('点击画布放置') }}
      </el-button>
    </template>
  </div>
</template>

<style scoped>
.template-panel {
  padding: 12px;
}
.panel-title {
  font-size: 13px;
  font-weight: 600;
  margin-bottom: 8px;
  color: #333;
}
.panel-section {
  font-size: 12px;
  font-weight: 600;
  margin: 12px 0 6px;
  color: #666;
}
.tpl-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  max-height: 200px;
  overflow-y: auto;
}
.tpl-item {
  padding: 6px 8px;
  border: 1px solid #e0e0e0;
  border-radius: 4px;
  cursor: pointer;
  display: flex;
  flex-direction: column;
  gap: 2px;
  transition: background 0.15s;
}
.tpl-item:hover {
  background: #f0f5ff;
}
.tpl-item.active {
  background: #e6f0ff;
  border-color: #4070cc;
}
.tpl-name {
  font-size: 13px;
}
.tpl-code {
  font-size: 11px;
  color: #999;
}
.empty-tip {
  font-size: 12px;
  color: #bbb;
  text-align: center;
  padding: 12px;
}
.array-form {
  margin-top: 4px;
}
</style>
