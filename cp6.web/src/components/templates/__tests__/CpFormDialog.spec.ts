// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'
import { ElMessage } from 'element-plus'
import CpFormDialog, { type FormField } from '../CpFormDialog.vue'

const fields: FormField[] = [
  { key: 'name', label: '名称', type: 'text', required: true },
  { key: 'qty', label: '数量', type: 'number' }
]

// el-dialog teleports its content to document.body — query there.
function bodyButton(text: string): HTMLButtonElement {
  return Array.from(document.body.querySelectorAll('.el-dialog button')).find((b) =>
    b.textContent?.includes(text)
  ) as HTMLButtonElement
}

// attachTo document.body so the teleported dialog content actually renders in jsdom.
function mountDialog(props: Record<string, unknown>) {
  return mount(CpFormDialog, { props, attachTo: document.body })
}

// Reach the internal <el-form> instance the component holds via `formRef`.
// Element Plus' async validation does not settle under jsdom, so we drive the
// validate() outcome at this seam to deterministically exercise onConfirm's gate
// (validate → submit). The required-rule *wiring* is asserted separately through
// the rendered is-required asterisk.
function elFormOf(w: ReturnType<typeof mountDialog>) {
  return (w.vm as unknown as { $: { setupState: { formRef: { validate: unknown } } } }).$
    .setupState.formRef
}

afterEach(() => {
  document.body.innerHTML = ''
})

describe('CpFormDialog', () => {
  it('打开后渲染 title、fields 标签，且 required 字段生成必填规则（asterisk）', async () => {
    const w = mountDialog({ modelValue: true, title: '新建物料', fields, form: { name: '', qty: 0 }, submit: vi.fn() })
    await flushPromises()
    expect(document.body.textContent).toContain('新建物料')
    expect(document.body.textContent).toContain('名称')
    expect(document.body.textContent).toContain('数量')
    // required:true → mergedRules → el-form-item 标记必填
    const items = w.findAll('.el-form-item')
    expect(items[0].classes()).toContain('is-required') // name 必填
    expect(items[1].classes()).not.toContain('is-required') // qty 非必填
  })

  it('校验失败：不调用 submit，不 emit saved / close', async () => {
    const submit = vi.fn().mockResolvedValue(undefined)
    const w = mountDialog({ modelValue: true, title: 't', fields, form: { name: '', qty: 0 }, submit })
    await flushPromises()
    // 模拟 el-form 校验失败（真实场景中 validate() 对空必填项 reject）
    elFormOf(w).validate = vi.fn().mockRejectedValue({ name: [{ message: '名称为必填项' }] })
    bodyButton('确认').click()
    await flushPromises()
    expect(submit).not.toHaveBeenCalled()
    expect(w.emitted('saved')).toBeUndefined()
    expect(w.emitted('update:modelValue')).toBeUndefined()
  })

  it('submit resolve：先 validate 再 submit → emit saved + update:modelValue(false)', async () => {
    const form = { name: 'M-1', qty: 10 }
    const submit = vi.fn().mockResolvedValue(undefined)
    const w = mountDialog({ modelValue: true, title: 't', fields, form, submit })
    await flushPromises()
    const validate = vi.fn().mockResolvedValue(true)
    elFormOf(w).validate = validate
    bodyButton('确认').click()
    await flushPromises()
    expect(validate).toHaveBeenCalled() // 提交前先校验
    expect(submit).toHaveBeenCalledTimes(1)
    expect(submit).toHaveBeenCalledWith(form)
    expect(w.emitted('saved')).toEqual([[]])
    expect(w.emitted('update:modelValue')).toEqual([[false]])
  })

  it('submit reject：ElMessage.error 且不关闭（无 saved）', async () => {
    const errSpy = vi.spyOn(ElMessage, 'error').mockImplementation((() => undefined) as never)
    const submit = vi.fn().mockRejectedValue(new Error('保存失败'))
    const w = mountDialog({ modelValue: true, title: 't', fields, form: { name: 'M-1', qty: 10 }, submit })
    await flushPromises()
    elFormOf(w).validate = vi.fn().mockResolvedValue(true)
    bodyButton('确认').click()
    await flushPromises()
    expect(errSpy).toHaveBeenCalledWith('保存失败')
    expect(w.emitted('saved')).toBeUndefined()
    expect(w.emitted('update:modelValue')).toBeUndefined()
    errSpy.mockRestore()
  })

  it('submit reject 非 Error：ElMessage.error 收到字符串（不产生 [object Object]）', async () => {
    const errSpy = vi.spyOn(ElMessage, 'error').mockImplementation((() => undefined) as never)
    const submit = vi.fn().mockRejectedValue('网络异常')
    const w = mountDialog({ modelValue: true, title: 't', fields, form: { name: 'M-1', qty: 10 }, submit })
    await flushPromises()
    elFormOf(w).validate = vi.fn().mockResolvedValue(true)
    bodyButton('确认').click()
    await flushPromises()
    expect(errSpy).toHaveBeenCalledWith('网络异常')
    errSpy.mockRestore()
  })

  it('防双提交：validate 在途期间二次 onConfirm → submit 仅一次', async () => {
    const submit = vi.fn().mockResolvedValue(undefined)
    const w = mountDialog({ modelValue: true, title: 't', fields, form: { name: 'M-1', qty: 10 }, submit })
    await flushPromises()
    const state = (w.vm as unknown as { $: { setupState: Record<string, any> } }).$.setupState
    // validate 保持 pending，模拟校验尚未完成时的第二次点击
    let resolveValidate!: (v: boolean) => void
    state.formRef.validate = vi.fn(() => new Promise((r) => { resolveValidate = r }))
    state.onConfirm()
    state.onConfirm() // 二次快速触发：应被 submitting 守卫拦下
    resolveValidate(true)
    await flushPromises()
    expect(submit).toHaveBeenCalledTimes(1)
  })

  it('提交进行中：确认按钮进入 loading 态，结束后解除', async () => {
    let release!: () => void
    const submit = vi.fn(() => new Promise<void>((r) => { release = r }))
    const w = mountDialog({ modelValue: true, title: 't', fields, form: { name: 'M-1', qty: 10 }, submit })
    await flushPromises()
    elFormOf(w).validate = vi.fn().mockResolvedValue(true)
    bodyButton('确认').click()
    await flushPromises()
    expect(bodyButton('确认').classList.contains('is-loading')).toBe(true)
    release()
    await flushPromises()
    expect(bodyButton('确认').classList.contains('is-loading')).toBe(false)
  })

  it('提供默认 slot 时替代 fields 自组表单', async () => {
    const w = mountDialog({
      modelValue: true, title: 't', fields, form: {}, submit: vi.fn(),
      // 具名传入默认插槽
    })
    await flushPromises()
    w.unmount()
    // 重新挂载并带默认 slot
    const w2 = mount(CpFormDialog, {
      props: { modelValue: true, title: 't', fields, form: {}, submit: vi.fn() },
      slots: { default: '<div class="custom-body">自定义表单</div>' },
      attachTo: document.body
    })
    await flushPromises()
    expect(document.body.querySelector('.custom-body')).not.toBeNull()
    // fields 生成的 el-form-item 不应渲染
    expect(document.body.querySelectorAll('.el-form-item').length).toBe(0)
    w2.unmount()
  })
})
