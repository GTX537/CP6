// @vitest-environment jsdom
// 波5 单格补码 UI 单测：unplaced 行「补码」按钮 → genSingle 调用一次 + 行内展示新码；
// 连点期间禁用只调一次；缺 generate 权按钮从 DOM 移除。
// el-dialog 默认 teleport 到 body，故内容查询走 document（非 wrapper 内），点击用原生事件。
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import ElementPlus from 'element-plus'
import { sceneApi } from '@/api/space/scene'
import { codeRuleApi } from '@/api/space/codeRule'
import { permission } from '@/directives/permission'
import BindCodesDialog from '../panels/BindCodesDialog.vue'
import type { RackVO, UnplacedLocationDto } from '@/types/space/scene'

// v-permission store：默认全授权；单测内翻转 permHas.fn 隐藏指定键
const { permHas } = vi.hoisted(() => ({ permHas: { fn: (_k: string) => true } }))
vi.mock('@/stores/permission', () => ({
  usePermissionStore: () => ({ loaded: true, has: (k: string) => permHas.fn(k) }),
}))

vi.mock('@/api/space/scene', () => ({
  sceneApi: { getUnplaced: vi.fn(), bindCodes: vi.fn() },
}))
vi.mock('@/api/space/codeRule', () => ({
  codeRuleApi: { genSingle: vi.fn() },
}))

const rack: RackVO = {
  id: 'r1', zoneId: 'z1', floorId: 'f1', rackCode: 'R001',
  x: 0, y: 0, z: 0, rotationZ: 0,
  cols: 2, levels: 1, depthCount: 1, cellW: 1, cellH: 1, cellD: 1,
}

const unplaced: UnplacedLocationDto[] = [
  { id: 'L1', locationCode: 'A-01', status: 1 },
  { id: 'L2', locationCode: 'A-02', status: 1 },
]

function i18nPlugin() {
  return createI18n({ legacy: false, locale: 'ja', missingWarn: false, fallbackWarn: false, messages: {} })
}

let wrapper: VueWrapper | null = null

async function openDialog() {
  wrapper = mount(BindCodesDialog, {
    attachTo: document.body,
    props: { modelValue: false, rackId: 'r1', rack, floorId: 'f1' },
    global: { plugins: [i18nPlugin(), ElementPlus], directives: { permission } },
  })
  // watch 非 immediate：false→true 触发 loadUnplaced
  await wrapper.setProps({ modelValue: true })
  await flushPromises()
  return wrapper
}

const rowEls = () => Array.from(document.querySelectorAll('[data-test="unplaced-row"]'))
const genBtns = () => Array.from(document.querySelectorAll<HTMLButtonElement>('[data-test="gen-btn"]'))

describe('BindCodesDialog 单格补码', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    permHas.fn = () => true
    vi.mocked(sceneApi.getUnplaced).mockResolvedValue({ code: 0, message: '', data: [...unplaced] })
  })

  afterEach(() => {
    wrapper?.unmount()
    wrapper = null
    document.body.innerHTML = ''
  })

  it('点补码调用 genSingle(row.id) 一次并行内展示新码', async () => {
    vi.mocked(codeRuleApi.genSingle).mockResolvedValue({ code: 0, message: '', data: { code: 'A-01-NEW' } })
    await openDialog()

    expect(rowEls().length).toBe(2)

    genBtns()[0]!.click()
    await flushPromises()

    expect(codeRuleApi.genSingle).toHaveBeenCalledTimes(1)
    expect(codeRuleApi.genSingle).toHaveBeenCalledWith('L1')
    // 行内新码展示
    const tag = document.querySelector('[data-test="gen-code"]')
    expect(tag).not.toBeNull()
    expect(tag!.textContent).toContain('A-01-NEW')
    // 成功后刷新 unplaced 列表（打开 1 次 + 成功刷新 1 次）
    expect(sceneApi.getUnplaced).toHaveBeenCalledTimes(2)
  })

  it('连点期间按钮禁用且 genSingle 只调用一次', async () => {
    let resolveGen!: (v: unknown) => void
    vi.mocked(codeRuleApi.genSingle).mockReturnValue(
      new Promise((r) => { resolveGen = r }) as ReturnType<typeof codeRuleApi.genSingle>,
    )
    await openDialog()

    genBtns()[0]!.click()
    await flushPromises()

    // 进行中：按钮禁用
    expect(genBtns()[0]!.disabled).toBe(true)

    // 连点：disabled 按钮不再触发 + 组件内重入守卫
    genBtns()[0]!.click()
    await flushPromises()
    expect(codeRuleApi.genSingle).toHaveBeenCalledTimes(1)

    resolveGen({ code: 0, message: '', data: { code: 'X' } })
    await flushPromises()
    // 完成后恢复可点
    expect(genBtns()[0]!.disabled).toBe(false)
  })

  it('缺 space-code-rule:generate 权时补码按钮从 DOM 移除', async () => {
    vi.mocked(codeRuleApi.genSingle).mockResolvedValue({ code: 0, message: '', data: { code: 'X' } })
    permHas.fn = (k) => k !== 'space-code-rule:generate'
    await openDialog()
    expect(genBtns().length).toBe(0)
    // 行仍在（仅按钮被指令移除）
    expect(rowEls().length).toBe(2)
  })
})
