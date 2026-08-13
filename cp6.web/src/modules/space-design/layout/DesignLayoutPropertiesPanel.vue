<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { SpaceUpdateLayoutRackLevelDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import type {
  ISpaceSceneAisleDto,
  ISpaceSceneRackDto,
  ISpaceSceneRackLevelDto,
  ISpaceSceneZoneDto,
  ISpaceUpdateLayoutAisleDto,
  ISpaceUpdateLayoutRackDto,
  ISpaceUpdateLayoutZoneDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import type { LayoutParentOption } from './layoutCreate'

const props = defineProps<{
  zone?: ISpaceSceneZoneDto | null
  aisle?: ISpaceSceneAisleDto | null
  rack?: ISpaceSceneRackDto | null
  rackLevels: readonly ISpaceSceneRackLevelDto[]
  zones: readonly LayoutParentOption[]
  aisles: readonly LayoutParentOption[]
  readonly: boolean
  busy: boolean
}>()

const emit = defineEmits<{
  saveZone: [payload: ISpaceUpdateLayoutZoneDto]
  saveAisle: [payload: ISpaceUpdateLayoutAisleDto]
  saveRack: [payload: ISpaceUpdateLayoutRackDto]
  remove: []
}>()

const zoneForm = reactive({ code: '', name: '', zoneType: 1, polygonJson: '[]', color: '', capabilityFlags: '' })
const aisleForm = reactive({ zoneLogicalId: '', code: '', name: '', direction: 1, polygonJson: '[]', centerlineJson: '[]' })
const rackForm = reactive({
  zoneLogicalId: '', aisleLogicalId: '', code: '', name: '', rackType: '', templateVersionId: '',
  x: 0, y: 0, z: 0, rotationZ: 0, width: 1, depth: 1, height: 1,
})
const levels = ref<EditableLevel[]>([])
const selectedKind = computed(() => props.zone ? 'Zone' : props.aisle ? 'Aisle' : props.rack ? 'Rack' : '')
const filteredAisles = computed(() => props.aisles.filter((item) => item.zoneLogicalId === rackForm.zoneLogicalId))
const validationMessage = computed(() => {
  if (props.zone && !zoneForm.code.trim()) return '请填写库区编码。'
  if (props.aisle && (!aisleForm.zoneLogicalId || !aisleForm.code.trim())) return '请选择库区并填写巷道编码。'
  if (!props.rack) return ''
  if (!rackForm.zoneLogicalId || !rackForm.code.trim()) return '请选择库区并填写货架编码。'
  if (![rackForm.x, rackForm.y, rackForm.z, rackForm.width, rackForm.depth, rackForm.height].every(Number.isInteger)) return '坐标和尺寸必须使用整数毫米。'
  if (rackForm.width <= 0 || rackForm.depth <= 0 || rackForm.height <= 0) return '货架宽、深、高必须大于 0。'
  if (!Number.isFinite(rackForm.rotationZ) || rackForm.rotationZ < 0 || rackForm.rotationZ >= 360) return '旋转角度必须在 0（含）到 360（不含）之间。'
  if (levels.value.length === 0 || levels.value.length > 50) return '货架必须包含 1–50 层。'
  const levelNumbers = new Set<number>()
  let locationCount = 0
  let previousTop = 0
  for (const level of [...levels.value].sort((left, right) => left.bottomZ - right.bottomZ)) {
    if (levelNumbers.has(level.levelNo) || !validLevel(level)) return '逐层编号、尺寸、数量或载荷无效。'
    levelNumbers.add(level.levelNo)
    if (level.binCount * level.cellWidth > rackForm.width) return `第 ${level.levelNo} 层列宽超过货架宽度。`
    if (level.depthCount * level.cellDepth > rackForm.depth) return `第 ${level.levelNo} 层深位超过货架深度。`
    const top = level.bottomZ + level.beamHeight + level.clearHeight
    if (top > rackForm.height) return `第 ${level.levelNo} 层超过货架高度。`
    if (level.bottomZ < previousTop) return `第 ${level.levelNo} 层与上一层重叠。`
    previousTop = top
    locationCount += level.binCount * level.depthCount
  }
  if (locationCount > 5_000) return '单次最多维护 5,000 个库位。'
  return ''
})
const canSave = computed(() => !props.readonly && !props.busy && validationMessage.value === '')

interface EditableLevel {
  levelNo: number
  bottomZ: number
  clearHeight: number
  binCount: number
  depthCount: number
  cellWidth: number
  cellDepth: number
  beamHeight: number
  maxLoad?: number
}

watch(() => props.zone, (value) => {
  if (!value) return
  Object.assign(zoneForm, {
    code: value.zoneCode ?? '', name: value.name ?? '', zoneType: value.zoneType ?? 1,
    polygonJson: value.polygonJson ?? '[]', color: value.color ?? '', capabilityFlags: value.capabilityFlags ?? '',
  })
}, { immediate: true })

watch(() => props.aisle, (value) => {
  if (!value) return
  Object.assign(aisleForm, {
    zoneLogicalId: value.zoneLogicalId ?? '', code: value.aisleCode ?? '', name: value.name ?? '',
    direction: value.direction ?? 1, polygonJson: value.polygonJson ?? '[]', centerlineJson: value.centerlineJson ?? '[]',
  })
}, { immediate: true })

watch([() => props.rack, () => props.rackLevels], ([value, rackLevels]) => {
  if (!value) return
  Object.assign(rackForm, {
    zoneLogicalId: value.zoneLogicalId ?? '', aisleLogicalId: value.aisleLogicalId ?? '',
    code: value.rackCode ?? '', name: value.name ?? '', rackType: value.rackType ?? '',
    templateVersionId: value.templateVersionId ?? '', x: value.x ?? 0, y: value.y ?? 0, z: value.z ?? 0,
    rotationZ: value.rotationZ ?? 0, width: value.width ?? 1, depth: value.depth ?? 1, height: value.height ?? 1,
  })
  levels.value = rackLevels.map((level) => ({
    levelNo: level.levelNo ?? 1, bottomZ: level.bottomZ ?? 0, clearHeight: level.clearHeight ?? 1,
    binCount: level.binCount ?? 1, depthCount: level.depthCount ?? 1, cellWidth: level.cellWidth ?? 1,
    cellDepth: level.cellDepth ?? 1, beamHeight: level.beamHeight ?? 0, maxLoad: level.maxLoad,
  })).sort((a, b) => a.levelNo - b.levelNo)
}, { immediate: true, deep: true })

watch(() => rackForm.zoneLogicalId, () => {
  if (!filteredAisles.value.some((item) => item.logicalId === rackForm.aisleLogicalId)) rackForm.aisleLogicalId = ''
})

function addLevel(): void {
  if (levels.value.length >= 50) return
  const levelNo = Math.max(0, ...levels.value.map((level) => level.levelNo)) + 1
  const bottomZ = levels.value.reduce((top, level) => Math.max(top, level.bottomZ + level.beamHeight + level.clearHeight), 0)
  levels.value.push({ levelNo, bottomZ, clearHeight: 1600, binCount: 2, depthCount: 1, cellWidth: 1200, cellDepth: 1000, beamHeight: 100, maxLoad: 1000 })
}

function removeLevel(index: number): void {
  if (levels.value.length <= 1) return
  levels.value.splice(index, 1)
}

function validLevel(level: EditableLevel): boolean {
  return [level.levelNo, level.bottomZ, level.clearHeight, level.binCount, level.depthCount, level.cellWidth, level.cellDepth, level.beamHeight].every(Number.isInteger) &&
    level.levelNo > 0 && level.bottomZ >= 0 && level.clearHeight > 0 &&
    level.binCount > 0 && level.binCount <= 500 && level.depthCount > 0 && level.depthCount <= 20 &&
    level.cellWidth > 0 && level.cellDepth > 0 && level.beamHeight >= 0 &&
    Number.isFinite(level.maxLoad ?? 0) && (level.maxLoad ?? 0) >= 0
}

function save(): void {
  if (!canSave.value) return
  if (props.zone) emit('saveZone', {
    zoneCode: zoneForm.code.trim(), name: zoneForm.name.trim() || undefined, zoneType: zoneForm.zoneType,
    polygonJson: zoneForm.polygonJson, color: zoneForm.color || undefined,
    capabilityFlags: zoneForm.capabilityFlags.trim() || undefined,
  })
  if (props.aisle) emit('saveAisle', {
    zoneLogicalId: aisleForm.zoneLogicalId, aisleCode: aisleForm.code.trim(), name: aisleForm.name.trim() || undefined,
    direction: aisleForm.direction, polygonJson: aisleForm.polygonJson, centerlineJson: aisleForm.centerlineJson,
  })
  if (props.rack) emit('saveRack', {
    zoneLogicalId: rackForm.zoneLogicalId, aisleLogicalId: rackForm.aisleLogicalId || undefined,
    rackCode: rackForm.code.trim(), name: rackForm.name.trim() || undefined, rackType: rackForm.rackType.trim() || undefined,
    templateVersionId: rackForm.templateVersionId || undefined, x: rackForm.x, y: rackForm.y, z: rackForm.z,
    rotationZ: rackForm.rotationZ, width: rackForm.width, depth: rackForm.depth, height: rackForm.height,
    levels: levels.value.map((level) => new SpaceUpdateLayoutRackLevelDto(level)),
  })
}
</script>

<template>
  <section class="layout-properties" data-test="layout-properties-panel">
    <header><strong>{{ selectedKind }}</strong><span>设计态业务构件</span></header>
    <form @submit.prevent="save">
      <template v-if="zone">
        <label>库区编码<input v-model="zoneForm.code" required maxlength="100" /></label>
        <label>名称<input v-model="zoneForm.name" maxlength="200" /></label>
        <label>类型<input v-model.number="zoneForm.zoneType" type="number" /></label>
        <label>识别色<input v-model="zoneForm.color" type="color" /></label>
        <label>能力标记<input v-model="zoneForm.capabilityFlags" maxlength="1000" /></label>
      </template>
      <template v-else-if="aisle">
        <label>所属库区<select v-model="aisleForm.zoneLogicalId"><option v-for="item in zones" :key="item.logicalId" :value="item.logicalId">{{ item.code }}</option></select></label>
        <label>巷道编码<input v-model="aisleForm.code" required maxlength="100" /></label>
        <label>名称<input v-model="aisleForm.name" maxlength="200" /></label>
        <label>方向<select v-model.number="aisleForm.direction"><option :value="1">沿 X 轴</option><option :value="2">沿 Y 轴</option></select></label>
      </template>
      <template v-else-if="rack">
        <label>所属库区<select v-model="rackForm.zoneLogicalId"><option v-for="item in zones" :key="item.logicalId" :value="item.logicalId">{{ item.code }}</option></select></label>
        <label>所属巷道<select v-model="rackForm.aisleLogicalId"><option value="">不关联</option><option v-for="item in filteredAisles" :key="item.logicalId" :value="item.logicalId">{{ item.code }}</option></select></label>
        <label>货架编码<input v-model="rackForm.code" required maxlength="100" data-test="layout-property-rack-code" /></label>
        <label>名称<input v-model="rackForm.name" maxlength="200" /></label>
        <label>货架类型<input v-model="rackForm.rackType" maxlength="64" /></label>
        <div class="grid"><label>X<input v-model.number="rackForm.x" type="number" /></label><label>Y<input v-model.number="rackForm.y" type="number" /></label><label>Z<input v-model.number="rackForm.z" type="number" /></label><label>旋转<input v-model.number="rackForm.rotationZ" type="number" step="0.1" /></label><label>宽<input v-model.number="rackForm.width" type="number" min="1" /></label><label>深<input v-model.number="rackForm.depth" type="number" min="1" /></label><label>高<input v-model.number="rackForm.height" type="number" min="1" /></label></div>
        <div class="level-title"><strong>逐层规格</strong><button type="button" @click="addLevel">+ 层</button></div>
        <fieldset v-for="(level, index) in levels" :key="level.levelNo"><legend>第 {{ level.levelNo }} 层</legend><button type="button" :disabled="levels.length <= 1" @click="removeLevel(index)">删除层</button><div class="grid"><label>底标高<input v-model.number="level.bottomZ" type="number" min="0" /></label><label>净高<input v-model.number="level.clearHeight" type="number" min="1" /></label><label>列数<input v-model.number="level.binCount" type="number" min="1" max="500" /></label><label>深位<input v-model.number="level.depthCount" type="number" min="1" max="20" /></label><label>单元宽<input v-model.number="level.cellWidth" type="number" min="1" /></label><label>单元深<input v-model.number="level.cellDepth" type="number" min="1" /></label><label>横梁高<input v-model.number="level.beamHeight" type="number" min="0" /></label><label>载荷<input v-model.number="level.maxLoad" type="number" min="0" /></label></div></fieldset>
        <p class="hint">规格变更保留仍存在库位的 LogicalId 与编码；新增库位保持未编码，后续由批量编码处理。</p>
      </template>
      <p v-if="validationMessage" class="validation" role="alert">{{ validationMessage }}</p>
      <div class="actions"><button type="button" class="danger" :disabled="readonly || busy" data-test="remove-layout" @click="emit('remove')">删除…</button><button type="submit" :disabled="!canSave" data-test="save-layout-properties">{{ busy ? '保存中…' : '保存修改' }}</button></div>
    </form>
  </section>
</template>

<style scoped>
.layout-properties{padding:16px;color:var(--space-studio-text,#e2e8f0)}header,.actions,.level-title{display:flex;align-items:center;justify-content:space-between;gap:12px}header span,.hint{font-size:13px;color:var(--space-studio-muted,#94a3b8)}form,label{display:grid;gap:6px}form{gap:12px;margin-top:16px}input,select{min-height:44px;padding:0 10px;border:1px solid #334155;border-radius:6px;background:#0f172a;color:inherit}.grid{display:grid;grid-template-columns:1fr 1fr;gap:10px}fieldset{display:grid;gap:10px;border:1px solid #334155;border-radius:8px;padding:10px}button{min-height:44px;padding:0 12px}.danger{color:#fecaca;border-color:#ef4444;background:#450a0a}.validation{color:#fecaca;font-size:14px}
</style>
