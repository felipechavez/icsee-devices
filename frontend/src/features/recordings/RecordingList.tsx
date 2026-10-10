import type { Recording } from '../../api/types'
import { hourLabel } from '../../lib/format'
import { RecordingCard } from './RecordingCard'

/** Agrupa por hora local, manteniendo el orden descendente que entrega la API. */
function groupByHour(recordings: Recording[]) {
  const groups: { label: string; items: Recording[] }[] = []
  for (const r of recordings) {
    const label = hourLabel(r.startedAt)
    const last = groups.at(-1)
    if (last?.label === label) last.items.push(r)
    else groups.push({ label, items: [r] })
  }
  return groups
}

export function RecordingList({ recordings, onOpen }: { recordings: Recording[]; onOpen: (r: Recording) => void }) {
  return (
    <>
      {groupByHour(recordings).map((group) => (
        <section key={group.label} className="hour-group">
          <div className="hour-label">{group.label}</div>
          <div className="clip-grid clip-grid-tight">
            {group.items.map((r) => (
              <RecordingCard key={r.id} recording={r} onOpen={onOpen} />
            ))}
          </div>
        </section>
      ))}
    </>
  )
}
