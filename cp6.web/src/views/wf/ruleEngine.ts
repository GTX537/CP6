// OA 章06 §3/§4/§5 表单规则引擎（B-2，OA2-D2）。
// evaluate/compute 与后端 CP6.Core/Services/Wf/ExpressionEvaluator.cs **同语义**（语言克制到 C#/TS 一致）：
// 白名单字段(=vars keys) + 比较 + 逻辑 + 算术(+ - * / %) + 一元(- !) + 括号 +
// 内置函数(dateDiff/sum/min/max/abs/round/len/if) + 数字/字符串/布尔字面量。
// 安全铁律：绝不 eval 代码；任何错误安全失败（evaluate→false / compute→null），不抛。
// 优先级（低→高）：|| && 比较 +/- *///% 一元 primary。

import type { FormSchema, FormFieldDef, RuleEffect } from '@/types/wf/wf'

type Val = number | string | boolean | null
type Vars = Record<string, any>

// ───────────────────────── 对外 ─────────────────────────

/** 布尔求值（条件/require 复核）。空表达式=true；任何错误→false。 */
export function evaluate(expr: string | null | undefined, vars: Vars): boolean {
  if (!expr || !expr.trim()) return true
  try {
    const p = new Parser(tokenize(expr), vars)
    const v = p.parseExpr()
    p.expectEnd()
    return toBool(v)
  } catch {
    return false
  }
}

/** 取值求值（compute 计算回写）。空表达式/任何错误→null（不写回，不抛）。 */
export function compute(expr: string | null | undefined, vars: Vars): Val {
  if (!expr || !expr.trim()) return null
  try {
    const p = new Parser(tokenize(expr), vars)
    const v = p.parseExpr()
    p.expectEnd()
    return v
  } catch {
    return null
  }
}

/** 字段生效效果（applyRules 输出，逐字段） */
export interface FieldEffect {
  visible: boolean
  required: boolean
  disabled: boolean
  options?: { label: string; value: string | number }[]
}

/**
 * 应用规则（章06 §4/§6）。<b>单轮前向</b>遍历 rules（命中即应用，compute 顺序写回 model，
 * 不做 fixpoint 级联 → 循环依赖也一轮收敛）。返回逐字段生效效果；compute 直接写回 model。
 */
export function applyRules(schema: FormSchema, model: Vars): Record<string, FieldEffect> {
  const eff: Record<string, FieldEffect> = {}
  for (const f of schema.fields || []) {
    eff[f.name] = { visible: true, required: !!f.required, disabled: false, options: f.options }
  }
  for (const rule of schema.rules || []) {
    if (!evaluate(rule.when, model)) continue
    for (const e of rule.then || []) applyEffect(eff, e, model)
  }
  return eff
}

function applyEffect(eff: Record<string, FieldEffect>, e: RuleEffect, model: Vars): void {
  const fe = eff[e.target]
  switch (e.action) {
    case 'show': if (fe) fe.visible = true; break
    case 'hide': if (fe) fe.visible = false; break
    case 'require': if (fe) fe.required = true; break
    case 'optional': if (fe) fe.required = false; break
    case 'disable': if (fe) fe.disabled = true; break
    case 'enable': if (fe) fe.disabled = false; break
    case 'setOptions': if (fe) fe.options = e.options || []; break
    case 'compute': {
      const v = compute(e.expr, model)
      if (v !== null) model[e.target] = v // 单轮前向写回，供后续规则取值
      break
    }
  }
}

// ───────────────────────── 词法 ─────────────────────────

type TokType =
  | 'num' | 'str' | 'ident' | 'bool'
  | '&&' | '||' | '!'
  | '>' | '<' | '>=' | '<=' | '==' | '!='
  | '+' | '-' | '*' | '/' | '%'
  | '(' | ')' | ','
interface Token { t: TokType; v?: Val }

function tokenize(s: string): Token[] {
  const out: Token[] = []
  let i = 0
  const n = s.length
  while (i < n) {
    const c = s[i]! // i < n，必有值（noUncheckedIndexedAccess）
    if (/\s/.test(c)) { i++; continue }
    switch (c) {
      case '(': case ')': case ',': case '+': case '-': case '*': case '/': case '%':
        out.push({ t: c as TokType }); i++; break
      case '&':
        if (s[i + 1] === '&') { out.push({ t: '&&' }); i += 2 } else throw new Error('bad &')
        break
      case '|':
        if (s[i + 1] === '|') { out.push({ t: '||' }); i += 2 } else throw new Error('bad |')
        break
      case '>':
        if (s[i + 1] === '=') { out.push({ t: '>=' }); i += 2 } else { out.push({ t: '>' }); i++ }
        break
      case '<':
        if (s[i + 1] === '=') { out.push({ t: '<=' }); i += 2 } else { out.push({ t: '<' }); i++ }
        break
      case '=':
        if (s[i + 1] === '=') { out.push({ t: '==' }); i += 2 } else throw new Error('bad =')
        break
      case '!':
        if (s[i + 1] === '=') { out.push({ t: '!=' }); i += 2 } else { out.push({ t: '!' }); i++ }
        break
      case "'":
      case '"': {
        const q = c; i++; const start = i
        while (i < n && s[i] !== q) i++
        if (i >= n) throw new Error('unterminated string')
        out.push({ t: 'str', v: s.slice(start, i) }); i++; break
      }
      default:
        if (/[0-9]/.test(c)) {
          const start = i; i++
          while (i < n && /[0-9.]/.test(s[i]!)) i++
          out.push({ t: 'num', v: parseFloat(s.slice(start, i)) })
        } else if (/[A-Za-z_]/.test(c)) {
          const start = i; i++
          while (i < n && /[A-Za-z0-9_.]/.test(s[i]!)) i++
          const id = s.slice(start, i)
          if (id === 'true') out.push({ t: 'bool', v: true })
          else if (id === 'false') out.push({ t: 'bool', v: false })
          else out.push({ t: 'ident', v: id })
        } else {
          throw new Error('illegal char: ' + c)
        }
        break
    }
  }
  return out
}

// ───────────────────────── 语法（递归下降，与 C# 同文法） ─────────────────────────
// expr := or ; or := and ('||' and)* ; and := comp ('&&' comp)*
// comp := add (cmpOp add)? ; add := mul (('+'|'-') mul)* ; mul := unary (('*'|'/'|'%') unary)*
// unary := ('-'|'!') unary | primary ; primary := num|str|bool|func|ident|'(' or ')'

class Parser {
  private pos = 0
  constructor(private t: Token[], private vars: Vars) {}

  private peek(): Token | undefined { return this.t[this.pos] }
  expectEnd(): void { if (this.pos !== this.t.length) throw new Error('trailing tokens') }

  parseExpr(): Val { return this.parseOr() }

  private parseOr(): Val {
    let l = this.parseAnd()
    while (this.peek()?.t === '||') { this.pos++; const r = this.parseAnd(); l = toBool(l) || toBool(r) }
    return l
  }
  private parseAnd(): Val {
    let l = this.parseComp()
    while (this.peek()?.t === '&&') { this.pos++; const r = this.parseComp(); l = toBool(l) && toBool(r) }
    return l
  }
  private parseComp(): Val {
    const l = this.parseAdd()
    const op = this.peek()?.t
    if (op === '>' || op === '<' || op === '>=' || op === '<=' || op === '==' || op === '!=') {
      this.pos++
      const r = this.parseAdd()
      return compare(op, l, r)
    }
    return l
  }
  private parseAdd(): Val {
    let l = this.parseMul()
    let op = this.peek()?.t
    while (op === '+' || op === '-') {
      this.pos++
      const r = this.parseMul()
      l = arith(op, l, r)
      op = this.peek()?.t
    }
    return l
  }
  private parseMul(): Val {
    let l = this.parseUnary()
    let op = this.peek()?.t
    while (op === '*' || op === '/' || op === '%') {
      this.pos++
      const r = this.parseUnary()
      l = arith(op, l, r)
      op = this.peek()?.t
    }
    return l
  }
  private parseUnary(): Val {
    if (this.peek()?.t === '-') { this.pos++; return -toNum(this.parseUnary()) }
    if (this.peek()?.t === '!') { this.pos++; return !toBool(this.parseUnary()) }
    return this.parsePrimary()
  }
  private parsePrimary(): Val {
    const tok = this.peek()
    if (!tok) throw new Error('unexpected end')
    switch (tok.t) {
      case 'num': case 'str': case 'bool':
        this.pos++; return tok.v as Val
      case 'ident': {
        this.pos++
        const name = tok.v as string
        if (this.peek()?.t === '(') return callFunc(name, this.parseArgs())
        if (!(name in this.vars)) throw new Error('unknown field: ' + name) // 白名单：未知字段→安全失败
        const v = this.vars[name]
        return v === undefined ? null : (v as Val)
      }
      case '(': {
        this.pos++
        const inner = this.parseOr()
        if (this.peek()?.t !== ')') throw new Error('missing )')
        this.pos++
        return inner
      }
      default:
        throw new Error('unexpected token: ' + tok.t)
    }
  }
  private parseArgs(): Val[] {
    this.pos++ // 吃 '('
    const args: Val[] = []
    if (this.peek()?.t !== ')') {
      args.push(this.parseOr())
      while (this.peek()?.t === ',') { this.pos++; args.push(this.parseOr()) }
    }
    if (this.peek()?.t !== ')') throw new Error('missing )')
    this.pos++ // 吃 ')'
    return args
  }
}

// ───────────────────────── 内置函数（白名单，与 C# 一致） ─────────────────────────

function callFunc(name: string, args: Val[]): Val {
  switch (name) {
    case 'dateDiff': if (args.length === 2) return (parseDate(args[0]!) - parseDate(args[1]!)) / 86400000; break
    case 'sum': if (args.length >= 1) return args.reduce<number>((a, x) => a + toNum(x), 0); break
    case 'min': if (args.length >= 1) return Math.min(...args.map(toNum)); break
    case 'max': if (args.length >= 1) return Math.max(...args.map(toNum)); break
    case 'abs': if (args.length === 1) return Math.abs(toNum(args[0]!)); break
    case 'round':
      if (args.length === 1) return roundHalfAwayFromZero(toNum(args[0]!), 0)
      if (args.length === 2) return roundHalfAwayFromZero(toNum(args[0]!), toNum(args[1]!)); break
    case 'len': if (args.length === 1) return toStr(args[0]!).length; break
    case 'if': if (args.length === 3) return toBool(args[0]!) ? args[1]! : args[2]!; break
  }
  throw new Error('unknown function or arity: ' + name + '/' + args.length) // 安全失败
}

/** 解析 'yyyy-MM-dd'（或带时间，取日期部分）→ UTC ms。非法→抛（上层安全失败）。 */
function parseDate(v: Val): number {
  const s = toStr(v).slice(0, 10)
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s)
  if (!m) throw new Error('bad date: ' + s)
  return Date.UTC(+m[1]!, +m[2]! - 1, +m[3]!)
}

function roundHalfAwayFromZero(x: number, digits: number): number {
  const f = Math.pow(10, digits)
  return Math.sign(x) * Math.round(Math.abs(x) * f) / f
}

// ───────────────────────── 求值原语（与 C# 一致） ─────────────────────────

function arith(op: '+' | '-' | '*' | '/' | '%', l: Val, r: Val): Val {
  if (op === '+' && !(typeof l === 'number' && typeof r === 'number')) return toStr(l) + toStr(r)
  const a = toNum(l), b = toNum(r)
  switch (op) {
    case '+': return a + b
    case '-': return a - b
    case '*': return a * b
    case '/': if (b === 0) throw new Error('div by zero'); return a / b
    case '%': if (b === 0) throw new Error('mod by zero'); return a % b
  }
}

function compare(op: '>' | '<' | '>=' | '<=' | '==' | '!=', l: Val, r: Val): boolean {
  if (op === '==') return valueEquals(l, r)
  if (op === '!=') return !valueEquals(l, r)
  if (typeof l === 'number' && typeof r === 'number') {
    switch (op) {
      case '>': return l > r
      case '<': return l < r
      case '>=': return l >= r
      case '<=': return l <= r
    }
  }
  throw new Error('magnitude compare requires numbers') // 非数字大小比较→安全失败
}

function valueEquals(l: Val, r: Val): boolean {
  if (l === null || r === null) return l === null && r === null
  if (typeof l !== typeof r) return false // 跨类型一律不等（与 C# 一致）
  return l === r
}

function toNum(v: Val): number {
  if (typeof v === 'number') return v
  if (typeof v === 'boolean') return v ? 1 : 0
  if (typeof v === 'string') {
    const n = Number(v)
    if (v.trim() === '' || Number.isNaN(n)) throw new Error('not a number: ' + v)
    return n
  }
  throw new Error('not a number')
}

function toStr(v: Val): string {
  if (v === null) return ''
  if (typeof v === 'string') return v
  if (typeof v === 'number') return String(v)
  if (typeof v === 'boolean') return v ? 'true' : 'false'
  return ''
}

function toBool(v: Val): boolean {
  if (typeof v === 'boolean') return v
  if (typeof v === 'number') return v !== 0
  if (typeof v === 'string') return v.length > 0
  return false
}
