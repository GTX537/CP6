<template>
  <div style="padding: 20px">
    <h2>{{ t('代码生成器') }}</h2>
    <el-alert :title="t('预览仅生成建议文件文本，不保存元数据、不应用代码、不占用业务号码。')" type="info" :closable="false" show-icon style="margin-bottom: 16px" />

    <el-card style="margin-bottom: 16px">
      <template #header>{{ t('表元数据') }}</template>
      <el-form :model="table" inline label-width="90px">
        <el-form-item :label="t('实体名')"><el-input v-model="table.entityName" placeholder="Demo" /></el-form-item>
        <el-form-item :label="t('模块')"><el-input v-model="table.module" placeholder="Erp" /></el-form-item>
        <el-form-item :label="t('表名')"><el-input v-model="table.tableName" placeholder="Erp_Demo" /></el-form-item>
        <el-form-item :label="t('资源键')"><el-input v-model="table.resourceKey" placeholder="demo" /></el-form-item>
        <el-form-item :label="t('采番键')"><el-input v-model="table.seqBizKey" :placeholder="t('DEMO（可空）')" /></el-form-item>
        <el-form-item :label="t('采番字段')"><el-input v-model="table.codeField" :placeholder="t('DemoNo（可空）')" /></el-form-item>
        <el-form-item :label="t('路由')"><el-input v-model="table.routePath" placeholder="api/erp/demo" /></el-form-item>
      </el-form>
    </el-card>

    <el-card style="margin-bottom: 16px">
      <template #header>
        <div style="display: flex; justify-content: space-between; align-items: center">
          <span>{{ t('列') }}</span>
          <el-button size="small" @click="addColumn">{{ t('添加列') }}</el-button>
        </div>
      </template>
      <el-table :data="columns" border size="small">
        <el-table-column :label="t('属性名')" width="160">
          <template #default="{ row }"><el-input v-model="row.name" /></template>
        </el-table-column>
        <el-table-column :label="t('类型')" width="140">
          <template #default="{ row }">
            <el-select v-model="row.clrType">
              <el-option v-for="ct in clrTypes" :key="ct" :label="ct" :value="ct" />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column :label="t('显示名')" width="140">
          <template #default="{ row }"><el-input v-model="row.label" /></template>
        </el-table-column>
        <el-table-column :label="t('必填')" width="70">
          <template #default="{ row }"><el-switch v-model="row.required" /></template>
        </el-table-column>
        <el-table-column :label="t('列表')" width="70">
          <template #default="{ row }"><el-switch v-model="row.inList" /></template>
        </el-table-column>
        <el-table-column :label="t('排序')" width="80">
          <template #default="{ row }"><el-input-number v-model="row.sort" :min="0" controls-position="right" size="small" /></template>
        </el-table-column>
        <el-table-column :label="t('操作')" width="70">
          <template #default="{ $index }"><el-button link type="danger" @click="columns.splice($index, 1)">{{ t('删') }}</el-button></template>
        </el-table-column>
      </el-table>
    </el-card>

    <div style="margin-bottom: 16px">
      <el-button v-permission="'pub-codegen:view'" type="primary" :loading="previewing" @click="preview">{{ t('预览生成') }}</el-button>
      <el-button v-permission="'pub-codegen:save'" @click="save">{{ t('保存元数据') }}</el-button>
    </div>

    <el-alert v-if="previewError" :title="previewError" type="error" :closable="false" show-icon style="margin-bottom: 16px" />
    <el-card v-if="previewResult">
      <template #header>
        <span>{{ t('生成产物预览') }}</span>
        <strong class="preview-unapplied">{{ t('未应用') }}</strong>
      </template>
      <dl class="preview-details">
        <dt>{{ t('模板') }}</dt><dd>{{ previewResult.templateRef }} · {{ previewResult.templateVersion }}</dd>
        <dt>{{ t('输入摘要') }}</dt><dd>{{ previewResult.inputDigest }}</dd>
      </dl>
      <p>{{ t('以下为建议文件名和只读文本，未写入任何文件。修改输入后需重新预览。') }}</p>
      <el-tabs>
        <el-tab-pane v-for="output in previewResult.outputs" :key="output.fileName" :label="output.fileName">
          <pre class="codegen-pre">{{ output.content }}</pre>
        </el-tab-pane>
      </el-tabs>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { onBeforeUnmount, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { codeGenApi, type CodeGenColumnInput, type CodeGenPreview, type CodeGenTableInput } from '@/api/pub/codegen'

const { t } = useI18n()
const clrTypes = ['string', 'int', 'int?', 'decimal?', 'DateTime?', 'bool', 'Guid?']

const table = reactive<CodeGenTableInput>({
  entityName: '', module: 'Erp', tableName: '', resourceKey: '',
  seqBizKey: '', codeField: '', routePath: ''
})
const columns = ref<CodeGenColumnInput[]>([])
const previewResult = ref<CodeGenPreview | null>(null)
const previewing = ref(false)
const previewError = ref('')
let inputRevision = 0
let disposed = false

watch([table, columns], () => {
  inputRevision++
  previewResult.value = null
  previewError.value = ''
}, { deep: true, flush: 'sync' })
onBeforeUnmount(() => { disposed = true })

function addColumn() {
  columns.value.push({ name: '', clrType: 'string', label: '', required: false, inList: true, inForm: true, sort: columns.value.length + 1 })
}

async function preview() {
  if (previewing.value) return
  const revision = inputRevision
  previewing.value = true
  previewResult.value = null
  previewError.value = ''
  try {
    const result = await codeGenApi.previewV1({ ...table }, columns.value.map(column => ({ ...column })))
    if (!disposed && revision === inputRevision) previewResult.value = result
  } catch (error: unknown) {
    if (!disposed && revision === inputRevision) {
      const message = (error as { response?: { data?: { message?: unknown } } }).response?.data?.message
      previewError.value = typeof message === 'string' ? message : t('预览失败，请检查输入后重试')
    }
  } finally {
    previewing.value = false
  }
}

async function save() {
  await codeGenApi.save(table, columns.value)
  ElMessage.success(t('元数据已保存'))
}
</script>

<style scoped>
.preview-unapplied { display: inline-block; margin-left: 12px; padding: 4px 10px; border-radius: 4px; background: #fff3cd; color: #664d03; font-size: 14px; }
.preview-details { display: grid; grid-template-columns: auto 1fr; gap: 8px 16px; }
.preview-details dd { margin: 0; overflow-wrap: anywhere; }
.codegen-pre {
  background: #1e1e1e;
  color: #d4d4d4;
  padding: 12px;
  border-radius: 6px;
  overflow: auto;
  max-height: 480px;
  font-size: 12px;
  line-height: 1.5;
}
</style>
