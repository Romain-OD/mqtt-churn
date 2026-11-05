import { useEffect, useRef } from 'react'

const PITCH = 16 // px per client
const COLOR: Record<string, string> = { '.': '#3A3D42', o: '#3DFF7A', x: '#FF3B5C', '-': '#8A8F98' }

// One canvas for the whole fleet, redrawn per frame: no 5,000 elements for React to reconcile 10 times a second.
export function Heatmap({ cells, cols }: { cells: string; cols: number }) {
  const canvas = useRef<HTMLCanvasElement>(null)

  useEffect(() => {
    const ctx = canvas.current?.getContext('2d')
    if (!ctx) return
    ctx.clearRect(0, 0, ctx.canvas.width, ctx.canvas.height)
    for (let i = 0; i < cells.length; i++) {
      ctx.fillStyle = COLOR[cells[i]]
      ctx.beginPath()
      ctx.arc(((i % cols) + 0.5) * PITCH, (Math.floor(i / cols) + 0.5) * PITCH, PITCH * 0.36, 0, 2 * Math.PI)
      ctx.fill()
    }
  }, [cells, cols])

  return <canvas ref={canvas} width={cols * PITCH} height={Math.ceil(cells.length / cols) * PITCH} />
}
