import { useEffect, useState } from 'react'

export type Frame = { seq: number; online: number; offline: number; gone: number; cells: string }

// One EventSource per mounted component. The cleanup matters: React's dev mode mounts twice.
export function useFrames(url = '/frames') {
  const [frame, setFrame] = useState<Frame>()
  const [live, setLive] = useState(false)

  useEffect(() => {
    const source = new EventSource(url)
    source.onopen = () => setLive(true)
    source.onerror = () => setLive(false) // the browser reconnects on its own
    source.addEventListener('frame', (e) => setFrame(JSON.parse(e.data)))
    return () => source.close()
  }, [url])

  return { frame, live }
}
