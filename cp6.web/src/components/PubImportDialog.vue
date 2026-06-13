<template>
  <el-dialog v-model="visible" :title="title || '数据导入'" width="640px" @open="reset">
    <el-steps :active="step" finish-status="success" simple style="margin-bottom: 16px">
      <el-step title="下载模板" />
      <el-step title="上传文件" />
      <el-step title="校验结果" />
    </el-steps>

    <!-- 步骤1：下载模板 -->
    <div v-if="step === 0" style="text-align: center; padding: 20px">
      <p style="color: #909399; margin-bottom: 16px">先下载模板，按列填写（必填列标 *），再上传。</p>
      <el-button type="primary" @click="onDownloadTemplate">下载导入模板</el-button>
      <div style="margin-top: 16px">
        <el-button @click="step = 1">下一步：上传</el-button>
      </div>
    </div>

    <!-- 步骤2：上传 -->
    <div v-else-if="step === 1" style="padding: 10px">
      <el-upload
        drag
        :auto-upload="false"
        :limit="1"
        :on-change="onFileChange"
        :on-exceed="onExceed"
        accept=".xlsx,.xls"
      >
        <el-icon class="el-icon--upload"><UploadFilled /></el-icon>
        <div class="el-upload__text">拖拽 Excel 到此或<em>点击选择</em></div>
      </el-upload>
      <div style="margin-top: 16px; text-align: right">
        <el-button @click="step = 0">上一步</el-button>
        <el-button type="primary" :disabled="!file" :loading="importing" @click="doImport">开始导入</el-button>
      </div>
    </div>

    <!-- 步骤3：结果 -->
    <div v-else style="padding: 10px">
      <el-result
        :icon="result && result.failed > 0 ? 'warning' : 'success'"
        :title="`成功 ${result?.success ?? 0} 行，失败 ${result?.failed ?? 0} 行`"
      />
      <el-table v-if="result?.errors?.length" :data="result.errors" border size="small" max-height="240">
        <el-table-column prop="row" label="行号" width="80" />
        <el-table-column prop="message" label="错误原因" show-overflow-tooltip />
      </el-table>
      <div style="margin-top: 16px; text-align: right">
        <el-button v-if="result?.errorFileUrl" type="warning" @click="downloadErrorFile">下载错误文件</el-button>
        <el-button @click="step = 1">重新上传</el-button>
        <el-button type="primary" @click="finish">完成</el-button>
      </div>
    </div>
  </el-dialog>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { ElMessage } from 'element-plus'
import { UploadFilled } from '@element-plus/icons-vue'
import http from '@/api/http'
import { usePubExcel } from '@/composables/usePubExcel'

interface ImportResultView {
  success: number
  failed: number
  errors: { row: number; message: string }[]
  errorFileUrl?: string
}

const props = defineProps<{
  title?: string
  templateUrl: string   // GET 模板下载
  importUrl: string     // POST multipart 导入
}>()

const visible = defineModel<boolean>('visible', { default: false })
const emit = defineEmits<{ done: [] }>()

const { downloadTemplate } = usePubExcel()
const step = ref(0)
const file = ref<File | null>(null)
const importing = ref(false)
const result = ref<ImportResultView | null>(null)

function reset() {
  step.value = 0
  file.value = null
  result.value = null
}

function onDownloadTemplate() {
  return downloadTemplate(props.templateUrl)
}

function onFileChange(f: any) {
  file.value = f.raw
}
function onExceed(files: any) {
  file.value = files[0]
}

async function doImport() {
  if (!file.value) return
  importing.value = true
  try {
    const fd = new FormData()
    fd.append('file', file.value)
    const res: any = await http.post(props.importUrl, fd)
    result.value = (res.data ?? res) as ImportResultView
    step.value = 2
  } finally {
    importing.value = false
  }
}

async function downloadErrorFile() {
  if (!result.value?.errorFileUrl) return
  const blob: any = await http.get(result.value.errorFileUrl, { responseType: 'blob' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = '导入错误.xlsx'
  a.click()
  URL.revokeObjectURL(url)
}

function finish() {
  ElMessage.success('导入完成')
  visible.value = false
  emit('done')
}
</script>
