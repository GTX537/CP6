<script setup lang="ts">
import { reactive, computed, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useSpaceEditorStore } from '@/stores/spaceEditor'
import { EditZoneCmd } from '@/space-editor/command/commands/EditZoneCmd'
import { EditMarkerCmd } from '@/space-editor/command/commands/EditMarkerCmd'
import { pointInPolygon } from '@/space-editor/interact/collide/CollisionHint'
import type { ZoneVO, MarkerVO, RackVO, AisleVO } from '@/types/space/scene'

/** 选中态描述——FloorEditor 从 selectionIds 解析后注入。三分支 zone/marker/rack + 空态(Aisle 一览)。 */
export type SelectionInfo =
  | { kind: 'none' }
  | { kind: 'zone'; zone: ZoneVO }
  | { kind: 'marker'; marker: MarkerVO }
  | { kind: 'rack'; rack: RackVO }

const props = defineProps<{ selection: SelectionInfo }>()
/** 命令改 store 后通知父级重渲染画布（保存仍走既有场景保存通道）。 */
const emit = defineEmits<{ changed: [] }>()

const { t } = useI18n()
const store = useSpaceEditorStore()

const zoneTypeOptions = [
  { value: 1, label: t('存储区') },
  { value: 2, label: t('拣选区') },
  { value: 3, label: t('收货区') },
  { value: 4, label: t('发货区') },
]
const markerTypeOptions = [
  { value: 0, label: t('普通标注') },
  { value: 1, label: t('提示') },
  { value: 2, label: t('警示') },
]

// ── Zone 编辑本地态（改名/改码/类型/色，全走 EditZoneCmd；不做几何编辑）────────
const zoneForm = reactive<{ zoneName: string; zoneCode: string; zoneType: number; color: string | null }>({
  zoneName: '', zoneCode: '', zoneType: 1, color: null,
})
watch(
  () => (props.selection.kind === 'zone' ? props.selection.zone : null),
  (z) => {
    if (!z) return
    zoneForm.zoneName = z.zoneName
    zoneForm.zoneCode = z.zoneCode
    zoneForm.zoneType = z.zoneType
    zoneForm.color = z.color ?? null
  },
  { immediate: true, deep: true },
)

/** 提交单个 zone 字段：以 store 现值为 before、表单值为 after，构 EditZoneCmd 进栈。 */
function commitZone(field: 'zoneName' | 'zoneCode' | 'zoneType' | 'color'): void {
  if (props.selection.kind !== 'zone') return
  const z = store.scene?.zones.find(x => x.id === (props.selection as { zone: ZoneVO }).zone.id)
  if (!z) return
  const next = zoneForm[field]
  const prev = (z[field] ?? null) as string | number | null
  const normNext = (next ?? null) as string | number | null
  if (prev === normNext) return
  // zoneName / zoneCode 空串回滚（禁清空业务码/名）
  if ((field === 'zoneName' || field === 'zoneCode') && String(next).trim() === '') {
    zoneForm[field] = z[field]
    return
  }
  const cmd = new EditZoneCmd(z.id, { [field]: z[field] }, { [field]: next })
  store.stack.exec(cmd, store.buildEditorContext())
  store.updateUndoRedo()
  emit('changed')
}

// ── Marker 编辑（复用 EditMarkerCmd；面板只是入口）───────────────────────────
const markerForm = reactive<{ text: string; markerType: number }>({ text: '', markerType: 0 })
watch(
  () => (props.selection.kind === 'marker' ? props.selection.marker : null),
  (m) => {
    if (!m) return
    markerForm.text = m.text
    markerForm.markerType = m.markerType
  },
  { immediate: true, deep: true },
)

function commitMarker(field: 'text' | 'markerType'): void {
  if (props.selection.kind !== 'marker') return
  const m = store.scene?.markers.find(x => x.id === (props.selection as { marker: MarkerVO }).marker.id)
  if (!m) return
  const next = markerForm[field]
  if (m[field] === next) return
  if (field === 'text' && String(next).trim() === '') {
    markerForm.text = m.text
    return
  }
  const cmd = new EditMarkerCmd(m.id, { [field]: m[field] }, { [field]: next })
  store.stack.exec(cmd, store.buildEditorContext())
  store.updateUndoRedo()
  emit('changed')
}

// ── Aisle 一览（只读：方向 / 所属库区 / 命中库位数）───────────────────────────
const aisles = computed<AisleVO[]>(() => store.scene?.aisles ?? [])

function aisleDirection(a: AisleVO): string {
  try {
    const cl = JSON.parse(a.centerline) as number[][]
    const p0 = cl[0]
    const p1 = cl[cl.length - 1]
    if (!p0 || !p1) return '—'
    const dx = Math.abs((p1[0] ?? 0) - (p0[0] ?? 0))
    const dy = Math.abs((p1[1] ?? 0) - (p0[1] ?? 0))
    return dx >= dy ? t('横向') : t('纵向')
  } catch {
    return '—'
  }
}

function aisleZoneName(a: AisleVO): string {
  return store.scene?.zones.find(z => z.id === a.zoneId)?.zoneName ?? a.zoneId
}

/** 命中库位数：库位中心(absX,absY)落入该巷道多边形者计数（几何命中，只读统计）。 */
function aisleHitCount(a: AisleVO): number {
  let poly: [number, number][]
  try {
    poly = JSON.parse(a.polygon) as [number, number][]
  } catch {
    return 0
  }
  const locs = store.scene?.locations ?? []
  let n = 0
  for (const l of locs) if (pointInPolygon(l.absX, l.absY, poly)) n++
  return n
}
</script>

<template>
  <div class="props-panel">
    <!-- Zone 分支 -->
    <template v-if="selection.kind === 'zone'">
      <div class="panel-title">{{ t('库区属性') }}</div>
      <el-form label-width="72px" size="small" class="props-form">
        <el-form-item :label="t('库区名称')">
          <el-input
            v-model="zoneForm.zoneName"
            data-test="zone-name"
            @blur="commitZone('zoneName')"
            @keyup.enter="commitZone('zoneName')"
          />
        </el-form-item>
        <el-form-item :label="t('库区编码')">
          <el-input
            v-model="zoneForm.zoneCode"
            data-test="zone-code"
            @blur="commitZone('zoneCode')"
            @keyup.enter="commitZone('zoneCode')"
          />
        </el-form-item>
        <el-form-item :label="t('库区类型')">
          <el-select v-model="zoneForm.zoneType" style="width: 100%" @change="commitZone('zoneType')">
            <el-option v-for="o in zoneTypeOptions" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
        <el-form-item :label="t('颜色')">
          <el-color-picker v-model="zoneForm.color" @change="commitZone('color')" />
        </el-form-item>
      </el-form>
      <div class="panel-hint">{{ t('几何形状请在画布上调整；此处仅改属性') }}</div>
    </template>

    <!-- Marker 分支 -->
    <template v-else-if="selection.kind === 'marker'">
      <div class="panel-title">{{ t('标注属性') }}</div>
      <el-form label-width="72px" size="small" class="props-form">
        <el-form-item :label="t('文本')">
          <el-input
            v-model="markerForm.text"
            data-test="marker-text"
            @blur="commitMarker('text')"
            @keyup.enter="commitMarker('text')"
          />
        </el-form-item>
        <el-form-item :label="t('类型')">
          <el-select v-model="markerForm.markerType" style="width: 100%" @change="commitMarker('markerType')">
            <el-option v-for="o in markerTypeOptions" :key="o.value" :label="o.label" :value="o.value" />
          </el-select>
        </el-form-item>
      </el-form>
    </template>

    <!-- Rack 分支（只读尺寸；反向建模入口保持在工具栏）-->
    <template v-else-if="selection.kind === 'rack'">
      <div class="panel-title">{{ t('货架属性') }}</div>
      <dl class="props-readonly">
        <dt>{{ t('货架编码') }}</dt><dd data-test="rack-code">{{ selection.rack.rackCode }}</dd>
        <dt>{{ t('列 × 层 × 深') }}</dt>
        <dd>{{ selection.rack.cols }} × {{ selection.rack.levels }} × {{ selection.rack.depthCount }}</dd>
        <dt>{{ t('单格尺寸mm') }}</dt>
        <dd>{{ selection.rack.cellW }} × {{ selection.rack.cellH }} × {{ selection.rack.cellD }}</dd>
      </dl>
      <div class="panel-hint">{{ t('库位码绑定请用工具栏「反向建模」') }}</div>
    </template>

    <!-- 空态：Aisle 一览 -->
    <template v-else>
      <div class="panel-title">{{ t('巷道一览') }}</div>
      <div v-if="aisles.length === 0" class="panel-hint">{{ t('暂无巷道（阵列生成时勾选「排间生成巷道」）') }}</div>
      <div v-else class="aisle-table">
        <div class="aisle-head">
          <span>{{ t('巷道') }}</span>
          <span>{{ t('方向') }}</span>
          <span>{{ t('所属库区') }}</span>
          <span>{{ t('命中库位') }}</span>
        </div>
        <div v-for="a in aisles" :key="a.id" class="aisle-row" data-test="aisle-row">
          <span class="a-code">{{ a.aisleCode }}</span>
          <span>{{ aisleDirection(a) }}</span>
          <span class="a-zone">{{ aisleZoneName(a) }}</span>
          <span>{{ aisleHitCount(a) }}</span>
        </div>
      </div>
      <div class="panel-hint">{{ t('点选画布上的库区 / 货架 / 标注以编辑属性') }}</div>
    </template>
  </div>
</template>

<style scoped>
.props-panel {
  padding: 12px;
  border-top: 1px solid var(--cp-line);
}
.panel-title {
  font-size: var(--cp-fs-base);
  font-weight: 600;
  margin-bottom: 8px;
  color: var(--cp-ink);
}
.panel-hint {
  font-size: var(--cp-fs-xs);
  color: var(--cp-muted);
  margin-top: 8px;
  line-height: 1.4;
}
.props-form {
  margin-top: 4px;
}
.props-readonly {
  margin: 0;
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 4px 10px;
  font-size: var(--cp-fs-xs);
}
.props-readonly dt {
  color: var(--cp-muted);
  white-space: nowrap;
}
.props-readonly dd {
  margin: 0;
  color: var(--cp-text);
}
.aisle-table {
  font-size: var(--cp-fs-xs);
}
.aisle-head,
.aisle-row {
  display: grid;
  grid-template-columns: 1.2fr 0.9fr 1.4fr 0.8fr;
  gap: 4px;
  padding: 4px 2px;
  align-items: center;
}
.aisle-head {
  color: var(--cp-muted);
  font-weight: 600;
  border-bottom: 1px solid var(--cp-line);
}
.aisle-row {
  color: var(--cp-text);
  border-bottom: 1px solid var(--cp-line-soft);
}
.aisle-row:hover {
  background: var(--cp-bg-hover);
}
.a-code {
  font-weight: 600;
  color: var(--cp-ink);
}
.a-zone {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
