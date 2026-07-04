// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { nextTick } from 'vue'
import { ElMessage } from 'element-plus'
import CpListPage from '../CpListPage.vue'
import type { FilterField } from '../CpFilterBar.vue'
const cols = [{ prop: 'no', label: '单号', kind: 'mono' }, { prop: 'qty', label: '数量', kind: 'num' }]
function makeFetch(rows = [{ no: 'SHP-1', qty: 1000 }]) {
  return vi.fn().mockResolvedValue({ rows, total: rows.length })
}
describe('CpListPage', () => {
  it('mounted 自动调用 fetch(page=1)', async () => {
    const f = makeFetch(); mount(CpListPage, { props: { columns: cols, fetch: f } })
    await flushPromises()
    expect(f).toHaveBeenCalledWith(expect.objectContaining({ page: 1 }))
  })
  it('渲染行数据与列格式', async () => {
    const w = mount(CpListPage, { props: { columns: cols, fetch: makeFetch() } })
    await flushPromises()
    expect(w.text()).toContain('SHP-1')
  })
  it('fetch 空结果显示 CpEmpty', async () => {
    const w = mount(CpListPage, { props: { columns: cols, fetch: makeFetch([]) } })
    await flushPromises()
    expect(w.findComponent({ name: 'CpEmpty' }).exists()).toBe(true)
  })
  it('fetch 失败保留 UI 不崩', async () => {
    const f = vi.fn().mockRejectedValue(new Error('boom'))
    const w = mount(CpListPage, { props: { columns: cols, fetch: f } })
    await flushPromises()
    expect(w.find('.cp-list').exists()).toBe(true)
  })
  it('切换状态卡以 statusKey 重新 fetch', async () => {
    const f = makeFetch()
    const w = mount(CpListPage, { props: { columns: cols, fetch: f,
      statusTabs: [{ key: 'all', label: '全部', count: 1 }, { key: 'wait', label: '未出库', count: 1 }] } })
    await flushPromises()
    await w.findAll('.ss')[1].trigger('click'); await flushPromises()
    expect(f).toHaveBeenLastCalledWith(expect.objectContaining({ statusKey: 'wait' }))
  })
  it('selection-change 透传', async () => {
    const w = mount(CpListPage, { props: { columns: cols, fetch: makeFetch(), selectable: true } })
    await flushPromises()
    w.findComponent({ name: 'ElTable' }).vm.$emit('selection-change', [{ no: 'SHP-1' }])
    expect(w.emitted('selection-change')![0][0]).toHaveLength(1)
  })
})

// —— 行为契约补充测试（错误保留旧行 / 翻页与 search 重置 / 乱序守卫 / loading）——
const searchFields: FilterField[] = [{ key: 'q', label: '单号', type: 'text' }]

describe('CpListPage 行为契约补充', () => {
  it('fetch 失败：ElMessage.error(错误消息) 且表格保留旧行', async () => {
    const errSpy = vi.spyOn(ElMessage, 'error').mockImplementation((() => undefined) as never)
    const f = vi.fn()
      .mockResolvedValueOnce({ rows: [{ no: 'SHP-1', qty: 1000 }], total: 1 })
      .mockRejectedValueOnce(new Error('boom'))
    const w = mount(CpListPage, { props: { columns: cols, fetch: f, searchFields } })
    await flushPromises()
    expect(w.text()).toContain('SHP-1')

    await w.findAll('button').find((b) => b.text().includes('查询'))!.trigger('click')
    await flushPromises()
    expect(errSpy).toHaveBeenCalledWith('boom')
    expect(w.text()).toContain('SHP-1') // 旧数据仍在
    errSpy.mockRestore()
  })

  it('fetch reject 非 Error：ElMessage.error 收到字符串（与 CpFormDialog 同一硬化契约，不产生 undefined）', async () => {
    const errSpy = vi.spyOn(ElMessage, 'error').mockImplementation((() => undefined) as never)
    const f = vi.fn().mockRejectedValue('网络异常') // 非 Error（无 .message）
    const w = mount(CpListPage, { props: { columns: cols, fetch: f } })
    await flushPromises()
    expect(errSpy).toHaveBeenCalledWith('网络异常')
    expect(w.find('.cp-list').exists()).toBe(true)
    errSpy.mockRestore()
  })

  it('emptyText 透传 CpEmpty', async () => {
    const w = mount(CpListPage, {
      props: { columns: cols, fetch: makeFetch([]), emptyText: '該当データなし' }
    })
    await flushPromises()
    const empty = w.findComponent({ name: 'CpEmpty' })
    expect(empty.exists()).toBe(true)
    expect(empty.text()).toContain('該当データなし')
  })

  it('翻页以对应 page 重新 fetch；search 将 page 重置为 1', async () => {
    const f = vi.fn().mockResolvedValue({ rows: [{ no: 'SHP-1', qty: 1000 }], total: 100 })
    const w = mount(CpListPage, { props: { columns: cols, fetch: f, searchFields } })
    await flushPromises()

    await w.findAll('.el-pager li').find((li) => li.text() === '2')!.trigger('click')
    await flushPromises()
    expect(f).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 }))

    await w.findAll('button').find((b) => b.text().includes('查询'))!.trigger('click')
    await flushPromises()
    expect(f).toHaveBeenLastCalledWith(expect.objectContaining({ page: 1 }))
  })

  it('reset 清空筛选、page=1 并重新 fetch', async () => {
    const f = vi.fn().mockResolvedValue({ rows: [{ no: 'SHP-1', qty: 1000 }], total: 100 })
    const w = mount(CpListPage, { props: { columns: cols, fetch: f, searchFields } })
    await flushPromises()
    await w.get('.fld input').setValue('SHP-9')
    await w.findAll('.el-pager li').find((li) => li.text() === '2')!.trigger('click')
    await flushPromises()

    await w.findAll('button').find((b) => b.text().includes('重置'))!.trigger('click')
    await flushPromises()
    const q = f.mock.calls.at(-1)![0]
    expect(q.page).toBe(1)
    expect(q.filters.q).toBeUndefined()
  })

  it('乱序响应：慢的旧请求不得覆盖新结果', async () => {
    let resolveFirst!: (v: { rows: unknown[]; total: number }) => void
    const f = vi.fn()
      .mockImplementationOnce(() => new Promise((r) => { resolveFirst = r }))
      .mockResolvedValueOnce({ rows: [{ no: 'SHP-NEW', qty: 2 }], total: 1 })
    const w = mount(CpListPage, { props: { columns: cols, fetch: f,
      statusTabs: [{ key: 'all', label: '全部', count: 1 }, { key: 'wait', label: '未出库', count: 1 }] } })
    await flushPromises()
    await w.findAll('.ss')[1].trigger('click')
    await flushPromises()
    expect(w.text()).toContain('SHP-NEW')

    resolveFirst({ rows: [{ no: 'SHP-OLD', qty: 1 }], total: 1 })
    await flushPromises()
    expect(w.text()).not.toContain('SHP-OLD')
    expect(w.text()).toContain('SHP-NEW')
  })

  it('fetch 期间表格显示 v-loading 遮罩', async () => {
    let resolve!: (v: { rows: unknown[]; total: number }) => void
    const f = vi.fn(() => new Promise<{ rows: unknown[]; total: number }>((r) => { resolve = r }))
    const w = mount(CpListPage, { props: { columns: cols, fetch: f } })
    await nextTick()
    expect(w.find('.el-loading-mask').exists()).toBe(true)
    resolve({ rows: [{ no: 'SHP-1', qty: 1000 }], total: 1 })
    await flushPromises()
    expect(w.text()).toContain('SHP-1')
  })

  it('kind 渲染：mono→.cp-mono、num→.num、tag→CpTag', async () => {
    const tagCols = [
      { prop: 'no', label: '单号', kind: 'mono' },
      { prop: 'qty', label: '数量', kind: 'num' },
      { prop: 'st', label: '状态', kind: 'tag' }
    ]
    const f = vi.fn().mockResolvedValue({ rows: [{ no: 'SHP-1', qty: 1000, st: '已出库' }], total: 1 })
    const w = mount(CpListPage, { props: { columns: tagCols, fetch: f } })
    await flushPromises()
    expect(w.find('td .cp-mono').text()).toBe('SHP-1')
    expect(w.find('td .num').text()).toBe('1000')
    // 断言真实单元格里的 CpTag（el-table-column 会额外渲染一个空 row 的隐藏占位，不取它）
    const tag = w.find('td .cp-tag')
    expect(tag.exists()).toBe(true)
    expect(tag.text()).toBe('已出库')
  })

  it('col-<prop> slot 优先于默认渲染', async () => {
    const w = mount(CpListPage, {
      props: { columns: cols, fetch: makeFetch() },
      slots: { 'col-no': `<template #col-no="{ row }"><b class="custom">C-{{ row.no }}</b></template>` }
    })
    await flushPromises()
    expect(w.find('.custom').text()).toBe('C-SHP-1')
    expect(w.find('.cp-mono').exists()).toBe(false)
  })
})
