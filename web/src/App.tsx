import { Heatmap } from './Heatmap'
import { useFrames } from './useFrames'

// ?cols=50 draws a tall grid for vertical video.
const cols = Number(new URLSearchParams(window.location.search).get('cols')) || 100
const n = (value: number) => value.toLocaleString('en-US')

export default function App() {
  const { frame, live } = useFrames()
  if (!frame) return <p className="wait">waiting for /frames…</p>

  return (
    <main>
      <header>
        <span className="online"><b>{n(frame.online)}</b> online</span>
        <span className="offline"><b>{n(frame.offline)}</b> offline</span>
        <span className="gone"><b>{n(frame.gone)}</b> gone</span>
        {!live && <span className="stale">reconnecting…</span>}
      </header>
      <Heatmap cells={frame.cells} cols={cols} />
    </main>
  )
}
