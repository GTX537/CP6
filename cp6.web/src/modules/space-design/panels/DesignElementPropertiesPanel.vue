<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
import type {
  ISpaceSceneElementAttributeDto,
  ISpaceSceneElementDto,
} from '../../../../../sdk/typescript/space-design-v1/spaceDesignV1Client'
import {
  buildElementPropertiesPayload,
  createElementPropertiesDraft,
  SPACE_ELEMENT_TYPES,
  type ElementPropertiesDraft,
} from './elementProperties'

const props = defineProps<{
  element: ISpaceSceneElementDto
  attributes: readonly ISpaceSceneElementAttributeDto[]
  saving?: boolean
  readonly?: boolean
}>()
const emit = defineEmits<{
  save: [payload: ReturnType<typeof buildElementPropertiesPayload>]
  remove: []
}>()

const draft = reactive<ElementPropertiesDraft>(
  createElementPropertiesDraft(props.element, props.attributes),
)
const error = computed(() => {
  try {
    buildElementPropertiesPayload(props.element, draft)
    return ''
  } catch (cause) {
    return cause instanceof Error ? cause.message : 'Invalid element properties'
  }
})

watch(
  () => [props.element, props.attributes] as const,
  ([element, attributes]) => {
    Object.assign(draft, createElementPropertiesDraft(element, attributes))
  },
)

function addAttribute(): void {
  draft.attributes.push({
    namespace: 'design',
    key: '',
    valueType: 'String',
    value: '',
  })
}

function save(manualCorrectionLocked?: boolean): void {
  if (error.value) return
  emit('save', {
    ...buildElementPropertiesPayload(props.element, draft),
    ...(manualCorrectionLocked === undefined
      ? {}
      : { manualCorrectionLocked }),
  })
}

const sourceBacked = computed(() => Boolean(
  props.element.revision?.sourceId && props.element.revision?.sourceRef,
))
const correctionLocked = computed(() =>
  Boolean(props.element.isManualCorrectionLocked),
)
</script>

<template>
  <aside
    class="element-properties"
    aria-label="通用元素属性"
    data-test="design-element-properties"
  >
    <div class="panel-heading">
      <div>
        <strong>{{ element.elementType }}</strong>
        <div class="logical-id">{{ element.revision?.logicalId }}</div>
      </div>
      <el-tag size="small">{{ element.revision?.lifecycleState }}</el-tag>
    </div>

    <el-alert
      v-if="sourceBacked"
      class="correction-lock-state"
      :type="correctionLocked ? 'warning' : 'info'"
      :closable="false"
      :title="correctionLocked
        ? `人工校正已锁定 v${element.userCorrectionVersion ?? 0}`
        : '来源对象尚未锁定；重新解析可提出替换或删除。'"
      :description="correctionLocked
        ? '后续保存仍受保护并递增版本；CAD 重新解析只能产生 Blocking 冲突。'
        : '保存并锁定会把当前表单与锁状态原子写入同一命令批。'"
      data-test="manual-correction-lock-state"
    />

    <fieldset class="property-fields" :disabled="readonly || saving">
      <el-divider content-position="left">构件语义</el-divider>
      <label>构件类型
        <el-select
          v-model="draft.elementType"
          data-test="element-type"
          aria-label="构件类型"
        >
          <el-option
            v-for="type in SPACE_ELEMENT_TYPES"
            :key="type"
            :label="type"
            :value="type"
          />
        </el-select>
      </label>

      <el-divider content-position="left">位置与尺寸（mm）</el-divider>
      <div class="number-grid">
        <label>X <el-input-number v-model="draft.x" :step="100" /></label>
        <label>Y <el-input-number v-model="draft.y" :step="100" /></label>
        <label>Z <el-input-number v-model="draft.z" :step="100" /></label>
        <label>旋转 <el-input-number v-model="draft.rotationZ" :step="5" :min="0" :max="359.9999" /></label>
        <label>宽 <el-input-number v-model="draft.width" :step="100" :min="1" /></label>
        <label>高 <el-input-number v-model="draft.height" :step="100" :min="1" /></label>
        <label>深 <el-input-number v-model="draft.depth" :step="100" :min="1" /></label>
      </div>

      <el-divider content-position="left">业务关联</el-divider>
      <label>业务编码 <el-input v-model="draft.businessCode" maxlength="200" /></label>
      <label>关联类型 <el-input v-model="draft.linkedEntityType" maxlength="100" /></label>
      <label>关联 LogicalId <el-input v-model="draft.linkedLogicalId" /></label>

      <el-divider content-position="left">设计属性</el-divider>
      <div
        v-for="(attribute, index) in draft.attributes"
        :key="index"
        class="attribute-row"
      >
        <el-input v-model="attribute.namespace" placeholder="namespace" />
        <el-input v-model="attribute.key" placeholder="key" />
        <el-select v-model="attribute.valueType">
          <el-option
            v-for="type in ['String', 'Integer', 'Decimal', 'Boolean', 'DateTime', 'Guid', 'Json']"
            :key="type"
            :label="type"
            :value="type"
          />
        </el-select>
        <el-input v-model="attribute.value" placeholder="value" />
        <el-input v-model="attribute.unit" placeholder="unit" />
        <el-button
          v-permission="'space:model:edit'"
          text
          type="danger"
          @click="draft.attributes.splice(index, 1)"
        >
          移除
        </el-button>
      </div>
      <el-button
        v-permission="'space:model:edit'"
        text
        @click="addAttribute"
      >
        + 添加属性
      </el-button>
    </fieldset>

    <el-alert v-if="error" :title="error" type="warning" :closable="false" />
    <div class="panel-actions">
      <el-button
        v-permission="'space:model:edit'"
        data-test="delete-element"
        type="danger"
        plain
        :disabled="readonly || saving"
        @click="emit('remove')"
      >
        删除草稿元素
      </el-button>
      <el-button
        v-permission="'space:model:edit'"
        data-test="save-element"
        type="primary"
        :loading="saving"
        :disabled="readonly || Boolean(error)"
        @click="save()"
      >
        保存属性
      </el-button>
      <el-button
        v-if="sourceBacked && !correctionLocked"
        v-permission="'space:model:edit'"
        data-test="lock-manual-correction"
        type="warning"
        :loading="saving"
        :disabled="readonly || Boolean(error)"
        @click="save(true)"
      >
        保存并锁定
      </el-button>
      <el-button
        v-else-if="sourceBacked"
        v-permission="'space:model:edit'"
        data-test="unlock-manual-correction"
        plain
        :loading="saving"
        :disabled="readonly || Boolean(error)"
        @click="save(false)"
      >
        保存并解除锁定
      </el-button>
    </div>
  </aside>
</template>

<style scoped>
.element-properties {
  width: 390px;
  padding: 16px;
  overflow: auto;
  color: var(--space-studio-text, #101828);
  background: var(--space-studio-panel, #fff);
  border-left: 1px solid var(--space-studio-border, #dfe4ea);
}

.panel-heading,
.panel-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.logical-id {
  margin-top: 4px;
  color: var(--space-studio-muted, #64748b);
  font-family: monospace;
  font-size: 13px;
  word-break: break-all;
}

.correction-lock-state {
  margin: 12px 0;
}

.number-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}

.property-fields {
  min-width: 0;
  margin: 0;
  padding: 0;
  border: 0;
}

label {
  display: grid;
  gap: 4px;
  margin-bottom: 10px;
  color: var(--space-studio-muted, #475569);
  font-size: 14px;
}

.attribute-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px;
  margin-bottom: 10px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--space-studio-border, #eef2f6);
}

.panel-actions {
  position: sticky;
  bottom: -16px;
  margin: 18px -16px -16px;
  padding: 12px 16px;
  background: var(--space-studio-panel, #fff);
  border-top: 1px solid var(--space-studio-border, #dfe4ea);
}

.element-properties :deep(.el-button),
.element-properties :deep(.el-input__wrapper),
.element-properties :deep(.el-select__wrapper),
.element-properties :deep(.el-input-number) { min-height: 44px; }
.element-properties :deep(.el-button:focus-visible),
.element-properties :deep(.el-input__wrapper:focus-within),
.element-properties :deep(.el-select__wrapper:focus-within),
.element-properties :deep(.el-input-number:focus-within) { outline: 3px solid var(--space-studio-focus, #0e7490); outline-offset: 2px; }

@media (max-width: 900px) {
  .element-properties {
    width: 100%;
    max-height: 45vh;
    border-top: 1px solid var(--space-studio-border, #dfe4ea);
    border-left: 0;
  }
}
</style>
