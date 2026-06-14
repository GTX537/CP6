<template>
  <div style="padding: 20px">
    <h2>{{ t('角色字段权限') }}</h2>

    <div style="display: flex; gap: 20px">
      <!-- 左：角色 -->
      <el-card style="width: 280px">
        <template #header>{{ t('选择角色') }}</template>
        <el-radio-group v-model="selectedRoleId" @change="reload">
          <el-radio
            v-for="role in roles"
            :key="role.roleId"
            :value="role.roleId"
            style="display: block; margin-bottom: 12px"
          >{{ role.roleName }}</el-radio>
        </el-radio-group>
      </el-card>

      <!-- 右：资源 + 字段访问级 -->
      <el-card style="flex: 1">
        <template #header>
          <div style="display: flex; justify-content: space-between; align-items: center; gap: 12px">
            <el-select v-model="selectedResourceKey" :placeholder="t('选择资源')" style="width: 200px" @change="applyAccessMap">
              <el-option v-for="r in resources" :key="r.key" :label="r.key" :value="r.key" />
            </el-select>
            <el-button type="primary" :disabled="!selectedRoleId || !selectedResourceKey" @click="save">{{ t('保存') }}</el-button>
          </div>
        </template>

        <el-table :data="currentFields" border>
          <el-table-column prop="label" :label="t('字段')" width="180" />
          <el-table-column prop="name" :label="t('属性名')" width="160" />
          <el-table-column :label="t('访问级')">
            <template #default="{ row }">
              <el-radio-group v-model="accessMap[row.name]" :disabled="!selectedRoleId">
                <el-radio :value="1">{{ t('可读写') }}</el-radio>
                <el-radio :value="2">{{ t('只读') }}</el-radio>
                <el-radio :value="3">{{ t('隐藏') }}</el-radio>
              </el-radio-group>
            </template>
          </el-table-column>
        </el-table>
      </el-card>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { ElMessage } from 'element-plus'
import { roleApi } from '@/api/sys/role'
import { rolePermApi } from '@/api/sys/rolePerm'
import type { FieldResourceDto, RoleFieldPermDto } from '@/types/sys/rolePerm'

const { t } = useI18n()
const roles = ref<any[]>([])
const selectedRoleId = ref<number | null>(null)
const resources = ref<FieldResourceDto[]>([])
const selectedResourceKey = ref<string>('')
const roleFieldPerms = ref<RoleFieldPermDto[]>([])
const accessMap = reactive<Record<string, number>>({})

const currentFields = computed(() =>
  resources.value.find(r => r.key === selectedResourceKey.value)?.fields ?? []
)

async function loadData() {
  const [roleRes, resRes]: any[] = await Promise.all([roleApi.getAll(), rolePermApi.getFieldResources()])
  roles.value = roleRes
  resources.value = resRes
  if (resRes.length) selectedResourceKey.value = resRes[0].key
}

async function reload(roleId: number) {
  roleFieldPerms.value = await rolePermApi.getRoleFieldPerms(roleId)
  applyAccessMap()
}

// 把已存权限套到当前资源的字段（未配置字段默认 1=可读写）
function applyAccessMap() {
  const saved = new Map(
    roleFieldPerms.value
      .filter(p => p.resourceKey === selectedResourceKey.value)
      .map(p => [p.fieldName, p.access])
  )
  for (const f of currentFields.value) accessMap[f.name] = saved.get(f.name) ?? 1
}

async function save() {
  const items: RoleFieldPermDto[] = currentFields.value.map(f => ({
    resourceKey: selectedResourceKey.value,
    fieldName: f.name,
    access: accessMap[f.name] ?? 1
  }))
  await rolePermApi.saveRoleFieldPerms(selectedRoleId.value!, items)
  ElMessage.success(t('已保存'))
  // 重新拉取以反映"可读写不落库"的归一化
  await reload(selectedRoleId.value!)
}

onMounted(loadData)
</script>
