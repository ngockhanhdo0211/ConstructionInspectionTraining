import { describe, expect, it } from 'vitest'
import { canonicalToViewport, clampGeometry, isUsableRectangle, viewportToCanonical, type PdfViewportTransform } from './mappingGeometry'

const box = [0, 0, 600, 800] as const
function rotated(rotation: 0 | 90 | 180 | 270, scale = 1): PdfViewportTransform {
  const forward = (x: number, y: number): [number, number] => {
    if (rotation === 0) return [x * scale, (800 - y) * scale]
    if (rotation === 90) return [y * scale, x * scale]
    if (rotation === 180) return [(600 - x) * scale, y * scale]
    return [(800 - y) * scale, (600 - x) * scale]
  }
  const inverse = (x: number, y: number): [number, number] => {
    if (rotation === 0) return [x / scale, 800 - y / scale]
    if (rotation === 90) return [y / scale, x / scale]
    if (rotation === 180) return [600 - x / scale, y / scale]
    return [600 - y / scale, 800 - x / scale]
  }
  return { convertToViewportPoint: forward, convertToPdfPoint: inverse }
}

describe('mapping geometry', () => {
  it.each([0, 90, 180, 270] as const)('round trips canonical geometry at %s degrees', rotation => {
    const viewport = rotated(rotation, 1.75)
    const source = { left: .1, bottom: .2, right: .45, top: .6 }
    const displayed = canonicalToViewport(source, box, viewport)
    const result = viewportToCanonical([[displayed.x, displayed.y], [displayed.x + displayed.width, displayed.y + displayed.height]], box, viewport)
    expect(result).toEqual(source)
  })

  it('is independent of zoom, fit scale, resize, and device-pixel backing size', () => {
    const source = { left: .2, bottom: .3, right: .5, top: .7 }
    const small = canonicalToViewport(source, box, rotated(0, .5))
    const large = canonicalToViewport(source, box, rotated(0, 2))
    expect(large).toEqual({ x: small.x * 4, y: small.y * 4, width: small.width * 4, height: small.height * 4 })
  })

  it('normalizes all four pointer drag directions', () => {
    const viewport = rotated(0)
    const expected = { left: .1, bottom: .25, right: .8, top: .875 }
    for (const points of [[60, 100, 480, 600], [480, 100, 60, 600], [60, 600, 480, 100], [480, 600, 60, 100]]) {
      expect(viewportToCanonical([[points[0], points[1]], [points[2], points[3]]], box, viewport)).toEqual(expected)
    }
  })

  it('clamps moved rectangles and rejects only rectangles that collapse after six-decimal rounding', () => {
    expect(clampGeometry({ left: -.2, bottom: .9, right: .2, top: 1.2 })).toEqual({ left: 0, bottom: .7, right: .4, top: 1 })
    expect(isUsableRectangle({ left: .1, bottom: .1, right: .1000004, top: .5 })).toBe(false)
    expect(isUsableRectangle({ left: .1, bottom: .1, right: .100001, top: .5 })).toBe(true)
  })
})
