<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage, ElMessageBox } from 'element-plus'
import { connectorApi } from '@/api/space/connector'
import type { ConnectorVO } from '@/types/space/connector'

const { t } = useI18n()

const props = defineProps<{ siteId: string; floorId: string }>()
const emit = defineEmits<{
  requestPlace: [connectorId: string]
}>()

const connectors = ref<ConnectorVO[]>([])
const loading = ref(false)

const typeOptions = [
  { value: 1, label: t('电梯') },
  { value: 2, label: t('楼梯') },
  { value: 3, label: t('坡道') },
]

function typeLabel(tp: number): string {
  return typeOptions.find(o => o.value === tp)?.label ?? String(tp)
}

const form = ref({ connectorCode: '', connectorType: 1, name: '' })

async function load(): Promise<void> {
  if (!props.siteId) return
  loading.value = true
  try {
    const res = await connectorApi.listBySite(props.siteId)
    connectors.value = res.data ?? []
  } catch {
    ElMessage.error(t('加载连接体失败'))
  } finally {
    loading.value = false
  }
}

onMounted(load)
watch(() => props.siteId, load)

async function createConnector(): Promise<void> {
  const code = form.value.connectorCode.trim()
  if (!code) {
    ElMessage.warning(t('请输入连接体编码'))
    return
  }
  try {
    await connectorApi.create({
      siteId: props.siteId,
      connectorCode: code,
      connectorType: form.value.connectorType,
      name: form.value.name.trim(),
    })
    ElMessage.success(t('新建成功'))
    form.value = { connectorCode: '', connectorType: 1, name: '' }
    await load()
  } catch {
    ElMessage.error(t('新建失败'))
  }
}

function placeHere(c: ConnectorVO): void {
  emit('requestPlace', c.id)
}

async function removeStop(c: ConnectorVO, floorId: string): Promise<void> {
  try {
    await connectorApi.deleteStop(c.id, floorId)
    ElMessage.success(t('落点已删除'))
    await load()
  } catch {
    ElMessage.error(t('删除落点失败'))
  }
}

async function removeConnector(c: ConnectorVO): Promise<void> {
  try {
    await ElMessageBox.confirm(t('确认删除该连接体及其所有落点？'), t('确认'), { type: 'warning' })
  } catch {
    return // user cancelled
  }
  try {
    await connectorApi.remove(c.id)
    ElMessage.success(t('删除成功'))
    await load()
  } catch {
    ElMessage.error(t('删除失败'))
  }
}

// Exposed so FloorEditor can refresh after a stop is placed on the canvas.
defineExpose({ refresh: load })
</script>

<template>
  <div class="connector-panel">
    <div class="panel-title">{{ t('连接体') }}</div>

    <!-- New connector form -->
    <div class="panel-section">{{ t('新建连接体') }}</div>
    <el-form label-width="64px" size="small" class="conn-form">
      <el-form-item :label="t('编码')">
        <el-input v-model="form.connectorCode" :placeholder="t('如 ELV-01')" />
      </el-form-item>
      <el-form-item :label="t('类型')">
        <el-select v-model="form.connectorType" style="width: 100%">
          <el-option v-for="o in typeOptions" :key="o.value" :label="o.label" :value="o.value" />
        </el-select>
      </el-form-item>
      <el-form-item :label="t('名称')">
        <el-input v-model="form.name" />
      </el-form-item>
    </el-form>
    <el-button type="primary" size="small" style="width: 100%" @click="createConnector">
      {{ t('新建连接体') }}
    </el-button>

    <!-- Connector list -->
    <div class="panel-section">{{ t('连接体列表') }}</div>
    <div v-loading="loading" class="conn-list">
      <div v-for="c in connectors" :key="c.id" class="conn-item">
        <div class="conn-head">
          <span class="conn-name">{{ c.name || c.connectorCode }}</span>
          <span class="conn-type">{{ typeLabel(c.connectorType) }}</span>
        </div>
        <div class="conn-code">{{ c.connectorCode }}</div>

        <div class="stop-list">
          <div v-for="s in c.stops" :key="s.floorId" class="stop-row">
            <span :class="['stop-floor', { current: s.floorId === floorId }]">
              {{ s.floorId === floorId ? t('本层') : t('其他层') + ' ' + s.floorId.slice(0, 8) }}
            </span>
            <span class="stop-xy">({{ Math.round(s.x) }}, {{ Math.round(s.y) }})</span>
            <el-button link type="danger" size="small" @click="removeStop(c, s.floorId)">
              {{ t('删除') }}
            </el-button>
          </div>
          <div v-if="c.stops.length === 0" class="empty-tip">{{ t('暂无落点') }}</div>
        </div>

        <div class="conn-actions">
          <el-button size="small" type="primary" plain @click="placeHere(c)">
            {{ t('在本层放置') }}
          </el-button>
          <el-button size="small" type="danger" plain @click="removeConnector(c)">
            {{ t('删除连接体') }}
          </el-button>
        </div>
      </div>
      <div v-if="connectors.length === 0 && !loading" class="empty-tip">{{ t('暂无连接体') }}</div>
    </div>
  </div>
</template>

<style scoped>
.connector-panel {
  padding: 12px;
  border-top: 1px solid #e0e0e0;
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
.conn-form {
  margin-top: 4px;
}
.conn-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-height: 24px;
}
.conn-item {
  border: 1px solid #e0e0e0;
  border-radius: 4px;
  padding: 8px;
}
.conn-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
}
.conn-name {
  font-size: 13px;
  font-weight: 500;
}
.conn-type {
  font-size: 11px;
  color: #4070cc;
  background: #e6f0ff;
  border-radius: 3px;
  padding: 1px 6px;
}
.conn-code {
  font-size: 11px;
  color: #999;
  margin-top: 2px;
}
.stop-list {
  margin: 6px 0;
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.stop-row {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 11px;
}
.stop-floor {
  color: #999;
}
.stop-floor.current {
  color: #2a9d4a;
  font-weight: 600;
}
.stop-xy {
  color: #666;
  flex: 1;
}
.conn-actions {
  display: flex;
  gap: 6px;
  margin-top: 4px;
}
.empty-tip {
  font-size: 12px;
  color: #bbb;
  text-align: center;
  padding: 12px;
}
</style>
