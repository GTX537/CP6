import { createServer } from 'vite'
import { chromium, expect } from '@playwright/test'
import { mkdir, realpath, writeFile } from 'node:fs/promises'
import path from 'node:path'

const output = process.env.CP6_CANVAS_EVIDENCE
if (!output) throw new Error('CP6_CANVAS_EVIDENCE_REQUIRED')
await mkdir(output, { recursive: true })
// Isolated component harness: real Vue Flow and actual pointer interactions, no API.
const original = '{"start":"s","extension":{"amount":12345678901234567890.0001},"nodes":[{"id":"s","type":"start"},{"id":"a","type":"approval","approverStrategy":"Specified","name":"Approve","extension":{"keep":true}},{"id":"e","type":"end"}],"edges":[{"from":"s","to":"a","condition":null,"extension":{"amount":9007199254740993.0001}},{"from":"a","to":"e"}]}'
const html = `<html><body><div id="app"></div><script type="module">
import {createApp,h,ref} from 'vue';
import ElementPlus from 'element-plus';
import 'element-plus/dist/index.css';
import Canvas from '/src/views/oa/designer/DesignerCanvas.vue';
import {preserveSchema} from '/src/views/oa/designer/preserveSchema.ts';
const original=${JSON.stringify(original)},schema=ref(JSON.parse(original));window.canvasResults={emitted:[],schema:schema.value};
createApp({setup(){return()=>h('div',{style:'width:1400px;height:850px'},[h(Canvas,{modelValue:schema.value,'onUpdate:modelValue':value=>{window.canvasResults.emitted.push(preserveSchema(original,value));schema.value=value;window.canvasResults.schema=value;}})])}}).use(ElementPlus).mount('#app');
</script></body></html>`
let browser, server, page
const errors = []
try {
  server = await createServer({ root: process.cwd(), server: { host: '127.0.0.1', port: 0, strictPort: true, fs: { allow: [process.cwd(), await realpath('node_modules')] } }, logLevel: 'error', plugins: [{ name: 'owned-canvas-regression', configureServer(s) { s.middlewares.use('/__canvas-regression', async (_req, res) => { res.setHeader('Content-Type', 'text/html'); res.end(await s.transformIndexHtml('/__canvas-regression', html)) }) } }] })
  await server.listen()
  browser = await chromium.launch({ channel: 'chrome', headless: true })
  page = await browser.newPage({ viewport: { width: 1440, height: 950 } })
  page.on('pageerror', error => errors.push(error.message))
  await page.goto(`http://127.0.0.1:${server.httpServer.address().port}/__canvas-regression`)
  await page.waitForLoadState('networkidle')
  const node = page.locator('.vue-flow__node[data-id="a"]')
  await expect(node).toBeVisible()
  // Settle the canvas's documented 120ms debounce, not a task-duration delay.
  await page.waitForTimeout(250)
  expect(await page.evaluate(() => window.canvasResults.emitted)).toEqual([])
  await node.click(); await page.waitForTimeout(250)
  expect(await page.evaluate(() => window.canvasResults.emitted)).toEqual([])
  const box = await node.boundingBox()
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2)
  await page.mouse.down(); await page.mouse.move(box.x + box.width / 2 + 80, box.y + box.height / 2 + 40, { steps: 8 }); await page.mouse.up()
  await page.waitForFunction(() => window.canvasResults.emitted.length > 0)
  const result = await page.evaluate(() => window.canvasResults)
  expect(result.schema.nodes.find(n => n.id === 'a').x).toBeDefined()
  expect(result.schema.nodes.find(n => n.id === 's')).not.toHaveProperty('x')
  expect(result.schema.nodes.find(n => n.id === 'e')).not.toHaveProperty('y')
  expect(result.schema.edges).toEqual(JSON.parse(original).edges)
  expect(result.emitted.at(-1)).toContain('12345678901234567890.0001')
  expect(result.emitted.at(-1)).toContain('9007199254740993.0001')
  expect(errors).toEqual([])
  await page.screenshot({ path: path.join(output, 'canvas.png') })
  await writeFile(path.join(output, 'result.json'), JSON.stringify({ result: 'PASS', assertions: ['load and selection do not write', 'actual drag changes only selected position', 'untouched root/edge extensions, nulls and numeric lexemes survive'], pageErrors: errors }, null, 2))
  process.stdout.write('PASS: real canvas load, selection and drag preserve the original source\n')
} catch (error) {
  await writeFile(path.join(output, 'failure.json'), JSON.stringify({ error: String(error), pageErrors: errors }, null, 2))
  if (page) { await page.screenshot({ path: path.join(output, 'failure.png') }); await writeFile(path.join(output, 'snapshot.json'), JSON.stringify(await page.evaluate(() => window.canvasResults ?? null), null, 2)) }
  throw error
} finally { await browser?.close(); await server?.close() }
