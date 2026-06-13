<template>
  <div class="pub-upload">
    <el-upload
      :http-request="customUpload"
      :show-file-list="false"
      :multiple="multiple"
      :accept="accept"
      drag
    >
      <el-icon class="el-icon--upload"><UploadFilled /></el-icon>
      <div class="el-upload__text">拖拽文件到此或<em>点击上传</em></div>
      <template #tip>
        <div class="el-upload__tip">单文件 ≤ {{ maxSize }}MB<span v-if="accept">，类型：{{ accept }}</span></div>
      </template>
    </el-upload>

    <el-table v-if="fileList.length" :data="fileList" size="small" style="margin-top: 10px">
      <el-table-column prop="fileName" label="文件名" show-overflow-tooltip />
      <el-table-column label="大小" width="100">
        <template #default="{ row }">{{ humanSize(row.size) }}</template>
      </el-table-column>
      <el-table-column prop="uploader" label="上传人" width="120" />
      <el-table-column label="操作" width="180">
        <template #default="{ row }">
          <el-button link type="primary" @click="download(row)">下载</el-button>
          <el-button link type="primary" @click="preview(row)">预览</el-button>
          <el-button link type="danger" @click="remove(row)">删除</el-button>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { UploadFilled } from '@element-plus/icons-vue'
import { attachmentApi } from '@/api/pub/attachment'

const props = withDefaults(defineProps<{
  bizType: string
  bizId?: string
  maxSize?: number       // MB
  accept?: string
  multiple?: boolean
}>(), {
  maxSize: 20,
  accept: '',
  multiple: true,
})

const fileList = ref<any[]>([])
// 草稿期（无 bizId）生成临时 token，单据保存后由父组件调 rebind 回填
const draftToken = ref<string | undefined>(
  props.bizId ? undefined : (crypto.randomUUID?.() ?? String(Date.now()))
)
defineExpose({ draftToken })

function humanSize(n: number) {
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / 1024 / 1024).toFixed(1)} MB`
}

async function reload() {
  if (props.bizId) fileList.value = await attachmentApi.list(props.bizType, props.bizId)
}

async function customUpload(option: any) {
  const file: File = option.file
  if (file.size > props.maxSize * 1024 * 1024) {
    ElMessage.error(`文件超过 ${props.maxSize}MB`)
    option.onError?.(new Error('oversize'))
    return
  }
  try {
    const res: any = await attachmentApi.upload(file, props.bizType, props.bizId, draftToken.value)
    option.onSuccess?.(res)
    ElMessage.success('上传成功')
    if (props.bizId) await reload()
    else if (res?.data) fileList.value.push(res.data)   // 草稿期本地维护列表
  } catch (e) {
    option.onError?.(e)
  }
}

async function download(row: any) {
  await attachmentApi.download(row.id, row.fileName)
}

async function preview(row: any) {
  const url = await attachmentApi.previewObjectUrl(row.id)
  window.open(url, '_blank')
  // 注：blob URL 在新窗口加载后可延迟 revoke；此处交给浏览器回收
}

async function remove(row: any) {
  await ElMessageBox.confirm('确定删除该附件？', '提示', { type: 'warning' })
  await attachmentApi.remove(row.id)
  ElMessage.success('已删除')
  if (props.bizId) await reload()
  else fileList.value = fileList.value.filter(f => f.id !== row.id)
}

onMounted(reload)
</script>

<style scoped>
.pub-upload { width: 100%; }
</style>
