import { describe, it, expect } from 'vitest'
import { evaluate, compute, applyRules } from './ruleEngine'
import type { FormSchema } from '@/types/wf/wf'

// OA 章06 §5 规则引擎（B-2）。evaluate/compute 与后端 ExpressionEvaluator.cs 同语义 + applyRules 显隐/必填/联动/计算。

describe('evaluate/compute — 与后端 ExpressionEvaluator 同语义', () => {
  it('比较/逻辑/括号', () => {
    expect(evaluate('days > 3', { days: 5 })).toBe(true)
    expect(evaluate("type == 'annual' && days <= 3", { type: 'annual', days: 3 })).toBe(true)
    expect(evaluate("(days > 3 || type == 'x') && days < 10", { days: 5, type: 'y' })).toBe(true)
    expect(evaluate('', { days: 5 })).toBe(true) // 空=无条件 true
  })

  it('算术 + 优先级 + 一元', () => {
    expect(compute('1 + 2 * 3', {})).toBe(7)
    expect(compute('(1 + 2) * 3', {})).toBe(9)
    expect(compute('10 / 5 % 3', {})).toBe(2)
    expect(compute('-3 + 5', {})).toBe(2)
    expect(evaluate('!(days > 3)', { days: 1 })).toBe(true)
    expect(compute('price * qty', { price: 120, qty: 5 })).toBe(600)
  })

  it('内置函数 dateDiff/sum/min/max/abs/round/len/if', () => {
    expect(compute("dateDiff('2026-06-15','2026-06-10')", {})).toBe(5)
    expect(compute('dateDiff(end, start) + 1', { end: '2026-06-15', start: '2026-06-10' })).toBe(6)
    expect(compute("dateDiff('2026-06-15 23:00:00','2026-06-14 01:00:00')", {})).toBe(1)
    expect(compute('sum(a, b, c)', { a: 3, b: 5, c: 7 })).toBe(15)
    expect(compute('min(8, 3, 5)', {})).toBe(3)
    expect(compute('max(8, 3, 5)', {})).toBe(8)
    expect(compute('abs(-3)', {})).toBe(3)
    expect(compute('round(3.6)', {})).toBe(4)
    expect(compute('round(3.14159, 2)', {})).toBe(3.14)
    expect(compute("len('abc')", {})).toBe(3)
    expect(compute("if(days > 3, 'long', 'short')", { days: 5 })).toBe('long')
    expect(compute("if(days > 3, 'long', 'short')", { days: 2 })).toBe('short')
  })

  it('字符串拼接 + Compute≠Evaluate', () => {
    expect(compute("type + '-' + days", { type: 'annual', days: 5 })).toBe('annual-5')
    expect(evaluate('sum(a, b)', { a: 1, b: 2 })).toBe(true) // 3→true
    expect(evaluate('sum(a, b)', { a: 0, b: 0 })).toBe(false) // 0→false
  })

  it('安全失败：白名单/未知函数/错参/非法/非数字算术/不 eval', () => {
    expect(evaluate('missing > 3', { days: 5 })).toBe(false)
    expect(compute('missing + 1', { days: 5 })).toBeNull()
    expect(compute("evil('x')", {})).toBeNull()
    expect(evaluate("System.IO.File.Delete('x')", {})).toBe(false)
    expect(compute('abs()', {})).toBeNull()
    expect(compute('if(true, 1)', {})).toBeNull()
    expect(compute('days >> 3 ;; drop', { days: 5 })).toBeNull()
    expect(compute("'a' - 'b'", {})).toBeNull()
    expect(compute("dateDiff('not-a-date','2026-06-10')", {})).toBeNull()
  })

  it('空表达式：compute→null / evaluate→true', () => {
    expect(compute('', {})).toBeNull()
    expect(compute(null, {})).toBeNull()
    expect(evaluate('', {})).toBe(true)
  })
})

describe('applyRules — 显隐/必填/禁用/联动/计算', () => {
  const leave: FormSchema = {
    fields: [
      { name: 'type', type: 'select', required: true },
      { name: 'start', type: 'date', required: true },
      { name: 'end', type: 'date', required: true },
      { name: 'days', type: 'number' },
      { name: 'reason', type: 'input' },
    ],
    rules: [
      { when: '', then: [{ action: 'compute', target: 'days', expr: 'dateDiff(end, start) + 1' }] },
      { when: "type == 'other'", then: [{ action: 'require', target: 'reason' }] },
    ],
  }

  it('compute 写回 model（单轮前向）', () => {
    const model: Record<string, any> = { type: 'annual', start: '2026-06-10', end: '2026-06-15' }
    applyRules(leave, model)
    expect(model.days).toBe(6) // dateDiff+1
  })

  it('条件 require 命中', () => {
    const eff = applyRules(leave, { type: 'other', start: '2026-06-10', end: '2026-06-11' })
    expect(eff.reason!.required).toBe(true)
  })

  it('条件 require 未命中 → 保持非必填', () => {
    const eff = applyRules(leave, { type: 'annual', start: '2026-06-10', end: '2026-06-11' })
    expect(eff.reason!.required).toBe(false)
  })

  it('show/hide + disable + setOptions', () => {
    const schema: FormSchema = {
      fields: [
        { name: 'needInvoice', type: 'checkbox' },
        { name: 'invoiceTitle', type: 'input', required: true },
        { name: 'city', type: 'select' },
      ],
      rules: [
        { when: 'needInvoice == false', then: [{ action: 'hide', target: 'invoiceTitle' }] },
        { when: 'needInvoice == true', then: [{ action: 'disable', target: 'invoiceTitle' }] },
        {
          when: "country == 'JP'",
          then: [{ action: 'setOptions', target: 'city', options: [{ label: '東京', value: 'tokyo' }] }],
        },
      ],
    }
    const hiddenEff = applyRules(schema, { needInvoice: false })
    expect(hiddenEff.invoiceTitle!.visible).toBe(false)

    const shownEff = applyRules(schema, { needInvoice: true, country: 'JP' })
    expect(shownEff.invoiceTitle!.visible).toBe(true)
    expect(shownEff.invoiceTitle!.disabled).toBe(true)
    expect(shownEff.city!.options).toEqual([{ label: '東京', value: 'tokyo' }])
  })

  it('无 rules → 仅静态 schema 效果', () => {
    const eff = applyRules({ fields: [{ name: 'a', type: 'input', required: true }] }, {})
    expect(eff.a).toEqual({ visible: true, required: true, disabled: false, options: undefined })
  })

  it('循环依赖一轮不级联（不死循环）', () => {
    const schema: FormSchema = {
      fields: [
        { name: 'a', type: 'number' },
        { name: 'b', type: 'number' },
      ],
      rules: [
        { when: '', then: [{ action: 'compute', target: 'a', expr: 'b + 1' }] },
        { when: '', then: [{ action: 'compute', target: 'b', expr: 'a + 1' }] },
      ],
    }
    const model: Record<string, any> = { a: 0, b: 0 }
    applyRules(schema, model) // 单轮：a=b+1=1，随后 b=a+1=2；不再迭代
    expect(model.a).toBe(1)
    expect(model.b).toBe(2)
  })
})
