import type { DrawingRectangle } from '../api/types'

export type PdfViewportTransform = {
  convertToViewportPoint(x: number, y: number): number[]
  convertToPdfPoint(x: number, y: number): number[]
}

export type PageBox = readonly [number, number, number, number]
export type ViewportRectangle = { x: number; y: number; width: number; height: number }

const clamp01 = (value: number) => Math.min(1, Math.max(0, value))
const round = (value: number) => Math.round(value * 1_000_000) / 1_000_000

export function canonicalToViewport(geometry: DrawingRectangle, pageBox: PageBox, viewport: PdfViewportTransform): ViewportRectangle {
  const [x0, y0, x1, y1] = pageBox
  const points = [
    viewport.convertToViewportPoint(x0 + geometry.left * (x1 - x0), y0 + geometry.bottom * (y1 - y0)),
    viewport.convertToViewportPoint(x0 + geometry.right * (x1 - x0), y0 + geometry.bottom * (y1 - y0)),
    viewport.convertToViewportPoint(x0 + geometry.left * (x1 - x0), y0 + geometry.top * (y1 - y0)),
    viewport.convertToViewportPoint(x0 + geometry.right * (x1 - x0), y0 + geometry.top * (y1 - y0)),
  ]
  const xs = points.map(point => point[0]); const ys = points.map(point => point[1])
  const left = Math.min(...xs); const top = Math.min(...ys)
  return { x: left, y: top, width: Math.max(...xs) - left, height: Math.max(...ys) - top }
}

export function viewportToCanonical(points: readonly [[number, number], [number, number]], pageBox: PageBox, viewport: PdfViewportTransform): DrawingRectangle {
  const [x0, y0, x1, y1] = pageBox
  const corners = [
    viewport.convertToPdfPoint(points[0][0], points[0][1]),
    viewport.convertToPdfPoint(points[1][0], points[1][1]),
  ]
  const xs = corners.map(point => clamp01((point[0] - x0) / (x1 - x0)))
  const ys = corners.map(point => clamp01((point[1] - y0) / (y1 - y0)))
  return { left: round(Math.min(...xs)), bottom: round(Math.min(...ys)), right: round(Math.max(...xs)), top: round(Math.max(...ys)) }
}

export function clampGeometry(geometry: DrawingRectangle): DrawingRectangle {
  const width = geometry.right - geometry.left; const height = geometry.top - geometry.bottom
  const left = Math.min(Math.max(geometry.left, 0), 1 - width)
  const bottom = Math.min(Math.max(geometry.bottom, 0), 1 - height)
  return { left: round(left), bottom: round(bottom), right: round(left + width), top: round(bottom + height) }
}

export const isUsableRectangle = (geometry: DrawingRectangle) =>
  round(geometry.right) > round(geometry.left) && round(geometry.top) > round(geometry.bottom)
