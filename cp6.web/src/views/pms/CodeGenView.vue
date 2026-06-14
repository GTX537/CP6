<template>
  <div style="padding: 20px">
    <h2>{{ t('代码生成器') }}</h2>

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
      <el-button type="primary" @click="preview">{{ t('预览生成') }}</el-button>
      <el-button @click="save">{{ t('保存元数据') }}</el-button>
    </div>

    <el-card v-if="Object.keys(files).length">
      <template #header>{{ t('生成产物预览') }}</template>
      <el-tabs>
        <el-tab-pane v-for="(content, name) in files" :key="name" :label="name">
          <pre class="codegen-pre">{{ content }}</pre>
        </el-tab-pane>
      </el-tabs>
    </el-card>
  </div>
</template>

<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { codeGenApi } from '@/api/pub/codegen'

const { t } = useI18n()
const clrTypes = ['string', 'int', 'int?', 'decimal?', 'DateTime?', 'bool', 'Guid?']

const table = reactive<any>({
  entityName: '', module: 'Erp', tableName: '', resourceKey: '',
  seqBizKey: '', codeField: '', routePath: ''
})
const columns = ref<any[]>([])
const files = ref<Record<string, string>>({})

function addColumn() {
  columns.value.push({ name: '', clrType: 'string', label: '', required: false, inList: true, inForm: true, sort: columns.value.length + 1 })
}

async function preview() {
  files.value = await codeGenApi.previewInline(table, columns.value)
}

async function save() {
  await codeGenApi.save(table, columns.value)
  ElMessage.success(t('元数据已保存'))
}
</script>

<style scoped>
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
