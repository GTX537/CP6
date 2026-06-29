import { describe, it, expect, vi } from 'vitest'
import { Group } from 'three'
import { PathAnimator } from './PathAnimator'

function fakeViewer() {
  const root = new Group()
  return { root, getSceneRoot: () => root, requestRender: vi.fn() }
}
const L = [{ x: 0, y: 0 }, { x: 1000, y: 0 }, { x: 1000, y: 1000 }]

describe('PathAnimator', () => {
  it('setPath builds line+cart under sceneRoot', () => {
    const v = fakeViewer()
    const a = new PathAnimator(v as any)
    a.setPath(L)
    expect(v.root.children.length).toBe(1)        // 动画 Group 挂上
    expect(v.root.children[0]!.children.length).toBe(2) // line + cart
    expect(v.requestRender).toHaveBeenCalled()
    expect(a.progress).toBe(0)
  })

  it('stepNext advances along the polyline and never exceeds 1', () => {
    const v = fakeViewer()
    const a = new PathAnimator(v as any)
    a.setPath(L)
    a.stepNext()
    expect(a.progress).toBeGreaterThan(0)
    a.stepNext(); a.stepNext(); a.stepNext()
    expect(a.progress).toBeLessThanOrEqual(1)
  })

  it('clear removes the group and resets', () => {
    const v = fakeViewer()
    const a = new PathAnimator(v as any)
    a.setPath(L)
    a.clear()
    expect(v.root.children.length).toBe(0)
    expect(a.progress).toBe(0)
    expect(a.playing).toBe(false)
  })

  it('setPath with <2 points renders nothing', () => {
    const v = fakeViewer()
    const a = new PathAnimator(v as any)
    a.setPath([{ x: 1, y: 1 }])
    expect(v.root.children.length).toBe(0)
  })

  it('setComparisonPath adds a green line and null removes only it (keeps path+cart)', () => {
    const v = fakeViewer()
    const a = new PathAnimator(v as any)
    a.setPath(L)
    expect(v.root.children[0]!.children.length).toBe(2)   // line + cart
    a.setComparisonPath([{ x: 0, y: 0 }, { x: 500, y: 500 }])
    expect(v.root.children[0]!.children.length).toBe(3)   // + compare line
    a.setComparisonPath(null)
    expect(v.root.children[0]!.children.length).toBe(2)   // 只移除对比线
  })

  it('clear() also removes the comparison line', () => {
    const v = fakeViewer()
    const a = new PathAnimator(v as any)
    a.setPath(L)
    a.setComparisonPath([{ x: 0, y: 0 }, { x: 500, y: 500 }])
    a.clear()
    expect(v.root.children.length).toBe(0)
  })
})
