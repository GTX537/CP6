import { GlobalWorkerOptions, getDocument } from 'pdfjs-dist'
import pdfWorkerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'

GlobalWorkerOptions.workerSrc = pdfWorkerUrl

const maxPdfPages = 200
const maxRasterDimension = 4096
const maxRasterPixels = 16_000_000

export interface DecodedUnderlay {
  image: CanvasImageSource
  width: number
  height: number
}

export function releaseDecodedUnderlay(
  decoded: DecodedUnderlay | null | undefined,
): void {
  const image = decoded?.image
  if (
    typeof ImageBitmap !== 'undefined' &&
    image instanceof ImageBitmap
  ) {
    image.close()
  }
}

export async function decodeUnderlay(
  blob: Blob,
): Promise<DecodedUnderlay> {
  if (blob.type === 'application/pdf') return decodePdf(blob)
  if (blob.type === 'image/png' || blob.type === 'image/jpeg') {
    return decodeImage(blob)
  }
  throw new Error(`Unsupported underlay content type: ${blob.type || 'unknown'}`)
}

async function decodePdf(blob: Blob): Promise<DecodedUnderlay> {
  const loadingTask = getDocument({
    data: new Uint8Array(await blob.arrayBuffer()),
    isEvalSupported: false,
    enableXfa: false,
  })
  const pdf = await loadingTask.promise
  try {
    if (pdf.numPages < 1 || pdf.numPages > maxPdfPages) {
      throw new Error(`PDF page count must be between 1 and ${maxPdfPages}`)
    }
    const page = await pdf.getPage(1)
    const base = page.getViewport({ scale: 1 })
    const scale = Math.min(
      2,
      maxRasterDimension / Math.max(base.width, base.height),
      Math.sqrt(maxRasterPixels / (base.width * base.height)),
    )
    const viewport = page.getViewport({ scale })
    const canvas = document.createElement('canvas')
    canvas.width = Math.max(1, Math.floor(viewport.width))
    canvas.height = Math.max(1, Math.floor(viewport.height))
    const context = canvas.getContext('2d', { alpha: false })
    if (!context) throw new Error('Canvas 2D is unavailable')
    await page.render({
      canvas,
      canvasContext: context,
      viewport,
    }).promise
    return {
      image: canvas,
      width: canvas.width,
      height: canvas.height,
    }
  } finally {
    await pdf.destroy()
  }
}

async function decodeImage(blob: Blob): Promise<DecodedUnderlay> {
  const bitmap = await createImageBitmap(blob)
  if (
    bitmap.width < 1 ||
    bitmap.height < 1 ||
    bitmap.width > maxRasterDimension * 8 ||
    bitmap.height > maxRasterDimension * 8 ||
    bitmap.width * bitmap.height > maxRasterPixels
  ) {
    bitmap.close()
    throw new Error('Image dimensions exceed the safe underlay raster limit')
  }
  return {
    image: bitmap,
    width: bitmap.width,
    height: bitmap.height,
  }
}
