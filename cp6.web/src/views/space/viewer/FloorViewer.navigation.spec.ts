import { flushPromises, shallowMount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import FloorViewer from './FloorViewer.vue'

const viewer = vi.hoisted(() => ({
  start: vi.fn(),
  onProgress: vi.fn(),
  onReady: vi.fn(),
  requestRender: vi.fn(),
  dispose: vi.fn(),
  home: vi.fn(),
  pick: vi.fn(),
  select: vi.fn(),
  focusSelected: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { siteId: 'site-1' }, query: {} }),
}))
vi.mock('vue-i18n', async (importOriginal) => ({
  ...await importOriginal<typeof import('vue-i18n')>(),
  useI18n: () => ({ t: (value: string) => value }),
}))
vi.mock('@/space-viewer/SpaceViewer', () => ({
  SpaceViewer: class { constructor() { return viewer } },
}))
vi.mock('@/api/http', () => ({
  default: {
    get: vi.fn().mockResolvedValue({
      schemaVersion: 1,
      authority: 'DesignRevision',
      runtimeOverlayIncluded: false,
      siteId: 'site-1',
      publishedVersionId: 'published-1',
      floors: [],
    }),
  },
}))

let wrapper: ReturnType<typeof shallowMount> | undefined

beforeEach(() => vi.clearAllMocks())
afterEach(() => {
  wrapper?.unmount()
  wrapper = undefined
})

async function mountViewer() {
  wrapper = shallowMount(FloorViewer)
  await flushPromises()
  return wrapper
}

describe('FloorViewer navigation affordance', () => {
  it('renders visible navigation guidance linked to the focusable canvas', async () => {
    const page = await mountViewer()
    const canvas = page.get('canvas')

    expect(canvas.attributes('tabindex')).toBe('0')
    expect(canvas.attributes('aria-describedby')).toBe(
      'viewer-navigation-help viewer-keyboard-help',
    )
    expect(page.get('#viewer-navigation-help').text()).toBe(
      '左键旋转 · 右键平移 · 滚轮指向缩放 · 双击聚焦',
    )
    expect(page.get('#viewer-keyboard-help').text()).toContain('鼠标操作：左键旋转，右键平移')
  })

  it('prevents the browser context menu on the canvas during right-button navigation', async () => {
    const page = await mountViewer()
    const event = new MouseEvent('contextmenu', { bubbles: true, cancelable: true })

    page.get('canvas').element.dispatchEvent(event)

    expect(event.defaultPrevented).toBe(true)
  })

  it('keeps Home reset and double-click focus connected to the viewer', async () => {
    const page = await mountViewer()
    const canvas = page.get('canvas')
    await canvas.trigger('keydown', { key: 'Home' })
    expect(viewer.home).toHaveBeenCalledOnce()

    const pick = { locationId: 'location-1' }
    viewer.pick.mockReturnValue(pick)
    viewer.select.mockReturnValue('location-1')
    await canvas.trigger('dblclick', { clientX: 20, clientY: 20 })

    expect(viewer.select).toHaveBeenCalledWith(pick)
    expect(viewer.focusSelected).toHaveBeenCalledOnce()
  })
})
