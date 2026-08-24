<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { SpaceCreateLayoutRackLevelDto } from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import {
  rackLocationCount,
  rectangleCenterlineJson,
  rectanglePolygonJson,
  type LayoutCreateIntent,
  type LayoutParentOption,
} from './layoutCreate'

const props = defineProps<{
  zones: readonly LayoutParentOption[]
  aisles: readonly LayoutParentOption[]
  readonly: boolean
  busy: boolean
  pointer?: { x: number; y: number } | null
}>()

const emit = defineEmits<{
  create: [intent: LayoutCreateIntent]
}>()

type CreateKind = LayoutCreateIntent['type']
const activeKind = ref<CreateKind>('CreateZone')
const zone = reactive({
  code: '',
  name: '',
  zoneType: 1,
  x: 0,
  y: 0,
  width: 20_000,
  depth: 12_000,
  color: '#0cb5be',
})
const aisle = reactive({
  zoneLogicalId: '',
  code: '',
  name: '',
  direction: 1,
  x: 0,
  y: 0,
  width: 12_000,
  depth: 3_000,
})
const rack = reactive({
  zoneLogicalId: '',
  aisleLogicalId: '',
  code: '',
  name: '',
  rackType: 'Selective',
  x: 0,
  y: 0,
  z: 0,
  rotationZ: 0,
  width: 2_400,
  depth: 1_000,
  height: 4_000,
})
const filteredAisles = computed(() =>
  props.aisles.filter(
    (candidate) => candidate.zoneLogicalId === rack.zoneLogicalId,
  ),
)
const generatedLocationCount = computed(() => rackLocationCount(rackLevels.value))
const canCreateZone = computed(() =>
  Boolean(zone.code.trim()) &&
  [zone.x, zone.y, zone.width, zone.depth].every(Number.isInteger) &&
  zone.width > 0 && zone.depth > 0,
)
const canCreateAisle = computed(() =>
  Boolean(aisle.zoneLogicalId && aisle.code.trim()) &&
  [aisle.x, aisle.y, aisle.width, aisle.depth].every(Number.isInteger) &&
  aisle.width > 0 && aisle.depth > 0,
)
const rackValidationMessage = computed(() => {
  if (!rack.zoneLogicalId) return '请选择所属库区。'
  if (!rack.code.trim()) return '请填写货架编码。'
  if (![rack.x, rack.y, rack.z, rack.width, rack.depth, rack.height].every(Number.isInteger)) {
    return '坐标和尺寸必须使用整数毫米。'
  }
  if (rack.width <= 0 || rack.depth <= 0 || rack.height <= 0) {
    return '货架宽、深、高必须大于 0。'
  }
  if (!Number.isFinite(rack.rotationZ) || rack.rotationZ < 0 || rack.rotationZ >= 360) {
    return '旋转角度必须在 0（含）到 360（不含）之间。'
  }
  if (rackLevels.value.length === 0 || !rackLevels.value.every(validLevel)) {
    return '请检查逐层尺寸、数量和载荷。'
  }
  let previousTop = 0
  for (const level of rackLevels.value) {
    if (level.binCount * level.cellWidth > rack.width) {
      return `第 ${level.levelNo} 层列宽超过货架宽度。`
    }
    if (level.depthCount * level.cellDepth > rack.depth) {
      return `第 ${level.levelNo} 层深位超过货架深度。`
    }
    const top = level.bottomZ + level.beamHeight + level.clearHeight
    if (top > rack.height) return `第 ${level.levelNo} 层超过货架高度。`
    if (level.bottomZ < previousTop) return `第 ${level.levelNo} 层与上一层重叠。`
    previousTop = top
  }
  if (generatedLocationCount.value > 5_000) return '单次最多生成 5,000 个库位。'
  return ''
})
const canCreateRack = computed(() =>
  rackValidationMessage.value === '',
)
const canSubmit = computed(() => {
  if (props.readonly || props.busy) return false
  if (activeKind.value === 'CreateZone') return canCreateZone.value
  if (activeKind.value === 'CreateAisle') return canCreateAisle.value
  return canCreateRack.value
})

watch(
  () => props.zones,
  (zones) => {
    const first = zones[0]?.logicalId ?? ''
    if (!zones.some((item) => item.logicalId === aisle.zoneLogicalId)) {
      aisle.zoneLogicalId = first
    }
    if (!zones.some((item) => item.logicalId === rack.zoneLogicalId)) {
      rack.zoneLogicalId = first
    }
  },
  { immediate: true },
)

watch(
  () => rack.zoneLogicalId,
  () => {
    if (!filteredAisles.value.some((item) => item.logicalId === rack.aisleLogicalId)) {
      rack.aisleLogicalId = ''
    }
  },
)

interface EditableRackLevel {
  levelNo: number
  bottomZ: number
  clearHeight: number
  binCount: number
  depthCount: number
  cellWidth: number
  cellDepth: number
  beamHeight: number
  maxLoad?: number
  locationCodePrefix?: string
}

const rackLevels = ref<EditableRackLevel[]>([
  newLevel(1),
  newLevel(2),
])

function newLevel(levelNo: number): EditableRackLevel {
  return {
    levelNo,
    bottomZ: (levelNo - 1) * 1_700,
    clearHeight: 1_600,
    binCount: 2,
    depthCount: 1,
    cellWidth: 1_200,
    cellDepth: 1_000,
    beamHeight: 100,
    maxLoad: 1_000,
  }
}

function validLevel(level: EditableRackLevel): boolean {
  return [
    level.levelNo,
    level.bottomZ,
    level.clearHeight,
    level.binCount,
    level.depthCount,
    level.cellWidth,
    level.cellDepth,
    level.beamHeight,
  ].every(Number.isInteger) &&
    level.levelNo > 0 &&
    level.bottomZ >= 0 &&
    level.clearHeight > 0 &&
    level.binCount > 0 && level.binCount <= 500 &&
    level.depthCount > 0 && level.depthCount <= 20 &&
    level.cellWidth > 0 && level.cellDepth > 0 &&
    level.beamHeight >= 0 &&
    Number.isFinite(level.maxLoad ?? 0) && (level.maxLoad ?? 0) >= 0
}

function addLevel(): void {
  if (rackLevels.value.length >= 50) return
  rackLevels.value.push(newLevel(rackLevels.value.length + 1))
}

function removeLevel(index: number): void {
  if (rackLevels.value.length === 1) return
  rackLevels.value.splice(index, 1)
  rackLevels.value.forEach((level, levelIndex) => {
    level.levelNo = levelIndex + 1
  })
}

function usePointer(): void {
  if (!props.pointer) return
  const x = Math.round(props.pointer.x)
  const y = Math.round(props.pointer.y)
  if (activeKind.value === 'CreateZone') Object.assign(zone, { x, y })
  if (activeKind.value === 'CreateAisle') Object.assign(aisle, { x, y })
  if (activeKind.value === 'CreateRack') Object.assign(rack, { x, y })
}

function submit(): void {
  if (!canSubmit.value) return
  if (activeKind.value === 'CreateZone') {
    emit('create', {
      type: 'CreateZone',
      payload: {
        zoneCode: zone.code.trim(),
        name: zone.name.trim() || undefined,
        zoneType: zone.zoneType,
        polygonJson: rectanglePolygonJson(zone.x, zone.y, zone.width, zone.depth),
        color: zone.color || undefined,
      },
    })
    return
  }
  if (activeKind.value === 'CreateAisle') {
    emit('create', {
      type: 'CreateAisle',
      payload: {
        zoneLogicalId: aisle.zoneLogicalId,
        aisleCode: aisle.code.trim(),
        name: aisle.name.trim() || undefined,
        direction: aisle.direction,
        polygonJson: rectanglePolygonJson(aisle.x, aisle.y, aisle.width, aisle.depth),
        centerlineJson: rectangleCenterlineJson(
          aisle.x,
          aisle.y,
          aisle.width,
          aisle.depth,
          aisle.direction,
        ),
      },
    })
    return
  }
  emit('create', {
    type: 'CreateRack',
    payload: {
      zoneLogicalId: rack.zoneLogicalId,
      aisleLogicalId: rack.aisleLogicalId || undefined,
      rackCode: rack.code.trim(),
      name: rack.name.trim() || undefined,
      rackType: rack.rackType.trim() || undefined,
      x: rack.x,
      y: rack.y,
      z: rack.z,
      rotationZ: rack.rotationZ,
      width: rack.width,
      depth: rack.depth,
      height: rack.height,
      levels: rackLevels.value.map((level) => new SpaceCreateLayoutRackLevelDto({
        ...level,
        locationCodePrefix: level.locationCodePrefix?.trim() || undefined,
      })),
    },
  })
}
</script>

<template>
  <section class="layout-create" data-test="layout-create-panel">
    <div class="kind-tabs" role="tablist" aria-label="业务构件类型">
      <button
        v-for="item in [
          ['CreateZone', '库区'],
          ['CreateAisle', '巷道'],
          ['CreateRack', '货架'],
        ] as const"
        :key="item[0]"
        type="button"
        role="tab"
        :aria-selected="activeKind === item[0]"
        :class="{ active: activeKind === item[0] }"
        @click="activeKind = item[0]"
      >
        {{ item[1] }}
      </button>
    </div>

    <form @submit.prevent="submit">
      <template v-if="activeKind === 'CreateZone'">
        <label>库区编码<input v-model="zone.code" required maxlength="100" data-test="zone-code" /></label>
        <label>名称<input v-model="zone.name" maxlength="200" /></label>
        <label>类型
          <select v-model.number="zone.zoneType">
            <option :value="1">存储区</option>
            <option :value="2">收货区</option>
            <option :value="3">发货区</option>
            <option :value="4">暂存区</option>
          </select>
        </label>
        <div class="field-grid">
          <label>X mm<input v-model.number="zone.x" type="number" /></label>
          <label>Y mm<input v-model.number="zone.y" type="number" /></label>
          <label>宽 mm<input v-model.number="zone.width" type="number" min="1" /></label>
          <label>深 mm<input v-model.number="zone.depth" type="number" min="1" /></label>
        </div>
        <label>识别色<input v-model="zone.color" type="color" /></label>
      </template>

      <template v-else-if="activeKind === 'CreateAisle'">
        <p v-if="zones.length === 0" class="blocking" role="alert">请先创建库区。</p>
        <label>所属库区
          <select v-model="aisle.zoneLogicalId" required data-test="aisle-zone">
            <option value="" disabled>选择库区</option>
            <option v-for="item in zones" :key="item.logicalId" :value="item.logicalId">
              {{ item.code }}{{ item.name ? ` · ${item.name}` : '' }}
            </option>
          </select>
        </label>
        <label>巷道编码<input v-model="aisle.code" required maxlength="100" data-test="aisle-code" /></label>
        <label>名称<input v-model="aisle.name" maxlength="200" /></label>
        <label>中心线方向
          <select v-model.number="aisle.direction">
            <option :value="1">沿 X 轴</option>
            <option :value="2">沿 Y 轴</option>
          </select>
        </label>
        <div class="field-grid">
          <label>X mm<input v-model.number="aisle.x" type="number" /></label>
          <label>Y mm<input v-model.number="aisle.y" type="number" /></label>
          <label>宽 mm<input v-model.number="aisle.width" type="number" min="1" /></label>
          <label>深 mm<input v-model.number="aisle.depth" type="number" min="1" /></label>
        </div>
      </template>

      <template v-else>
        <p v-if="zones.length === 0" class="blocking" role="alert">请先创建库区。</p>
        <label>所属库区
          <select v-model="rack.zoneLogicalId" required data-test="rack-zone">
            <option value="" disabled>选择库区</option>
            <option v-for="item in zones" :key="item.logicalId" :value="item.logicalId">{{ item.code }}</option>
          </select>
        </label>
        <label>所属巷道（可选）
          <select v-model="rack.aisleLogicalId">
            <option value="">不关联巷道</option>
            <option v-for="item in filteredAisles" :key="item.logicalId" :value="item.logicalId">{{ item.code }}</option>
          </select>
        </label>
        <label>货架编码<input v-model="rack.code" required maxlength="100" data-test="rack-code" /></label>
        <label>名称<input v-model="rack.name" maxlength="200" /></label>
        <label>货架类型<input v-model="rack.rackType" maxlength="100" /></label>
        <div class="field-grid">
          <label>X mm<input v-model.number="rack.x" type="number" /></label>
          <label>Y mm<input v-model.number="rack.y" type="number" /></label>
          <label>Z mm<input v-model.number="rack.z" type="number" /></label>
          <label>旋转 °<input v-model.number="rack.rotationZ" type="number" step="0.1" /></label>
          <label>宽 mm<input v-model.number="rack.width" type="number" min="1" /></label>
          <label>深 mm<input v-model.number="rack.depth" type="number" min="1" /></label>
          <label>高 mm<input v-model.number="rack.height" type="number" min="1" /></label>
        </div>

        <div class="level-heading">
          <strong>逐层规格</strong>
          <button type="button" :disabled="rackLevels.length >= 50" @click="addLevel">+ 层</button>
        </div>
        <fieldset v-for="(level, index) in rackLevels" :key="level.levelNo" class="rack-level">
          <legend>第 {{ level.levelNo }} 层</legend>
          <button class="remove-level" type="button" :disabled="rackLevels.length === 1" :aria-label="`删除第 ${level.levelNo} 层`" @click="removeLevel(index)">×</button>
          <div class="field-grid">
            <label>底标高<input v-model.number="level.bottomZ" type="number" min="0" /></label>
            <label>净高<input v-model.number="level.clearHeight" type="number" min="1" /></label>
            <label>列数<input v-model.number="level.binCount" type="number" min="1" max="500" /></label>
            <label>深位数<input v-model.number="level.depthCount" type="number" min="1" max="20" /></label>
            <label>单元宽<input v-model.number="level.cellWidth" type="number" min="1" /></label>
            <label>单元深<input v-model.number="level.cellDepth" type="number" min="1" /></label>
            <label>横梁高<input v-model.number="level.beamHeight" type="number" min="0" /></label>
            <label>载荷 kg<input v-model.number="level.maxLoad" type="number" min="0" /></label>
          </div>
          <label>库位编码前缀（可选）<input v-model="level.locationCodePrefix" maxlength="150" placeholder="留空，后续批量编码" /></label>
        </fieldset>
        <div class="location-preview" aria-live="polite">
          将生成 {{ generatedLocationCount }} 个库位；编码前缀留空时只创建未编码库位。
        </div>
        <p v-if="rackValidationMessage" class="blocking" role="alert">
          {{ rackValidationMessage }}
        </p>
      </template>

      <button v-if="pointer" type="button" class="pointer" @click="usePointer">使用画布指针坐标</button>
      <button class="submit" type="submit" :disabled="!canSubmit" data-test="submit-layout">
        {{ busy ? '保存中…' : '创建并保存' }}
      </button>
    </form>
  </section>
</template>

<style scoped>
.layout-create { margin-top:14px; padding-top:14px; border-top:1px solid var(--space-studio-border); }
.kind-tabs { display:grid; grid-template-columns:repeat(3,1fr); gap:4px; }
.kind-tabs button { min-height:44px; margin:0; padding:0 4px; }
.kind-tabs button.active { border-color:var(--space-studio-accent); color:var(--space-studio-accent); background:rgba(12,181,190,.10); }
form { margin-top:12px; }
label { display:flex; flex-direction:column; gap:5px; margin:10px 0; color:var(--space-studio-text); font-size:13px; }
input,select { width:100%; min-height:44px; padding:0 9px; border:1px solid var(--space-studio-border); border-radius:5px; box-sizing:border-box; color:var(--space-studio-text); background:var(--space-studio-panel-raised); font:inherit; }
input[type='color'] { padding:5px; }
input:focus-visible,select:focus-visible,button:focus-visible { outline:3px solid var(--space-studio-focus); outline-offset:2px; }
.field-grid { display:grid; grid-template-columns:1fr 1fr; gap:0 8px; }
button { width:100%; min-height:44px; border:1px solid var(--space-studio-border); border-radius:6px; color:var(--space-studio-text); background:var(--space-studio-panel-raised); cursor:pointer; }
button:disabled { cursor:not-allowed; opacity:.55; }
.submit { margin-top:8px; border-color:var(--space-studio-accent); color:#062f33; background:var(--space-studio-accent); font-weight:700; }
.pointer { margin-top:8px; }
.blocking { color:var(--space-studio-blocking); font-size:14px; line-height:1.45; }
.level-heading { display:flex; align-items:center; justify-content:space-between; margin:16px 0 8px; }
.level-heading strong { font-size:14px; }
.level-heading button { width:64px; }
.rack-level { position:relative; margin:10px 0; padding:10px; border:1px solid var(--space-studio-border); border-radius:6px; }
.rack-level legend { padding:0 5px; font-size:13px; font-weight:700; }
.remove-level { position:absolute; top:4px; right:6px; width:44px; min-height:44px; border:0; background:transparent; color:var(--space-studio-blocking); font-size:20px; }
.location-preview { padding:10px; border-radius:6px; color:var(--space-studio-muted); background:rgba(148,163,184,.08); font-size:13px; line-height:1.5; }
</style>
