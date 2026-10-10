import type { TimelineItem } from '../../api/types'
import { formatTime, secondsOfDay } from '../../lib/format'

/** Barra de 24 h con una marca por clip (roja si falló); cada marca abre su clip. */
export function DayTimeline({ items, onOpen }: { items: TimelineItem[]; onOpen: (id: string) => void }) {
  return (
    <div className="timeline">
      <div className="timeline-track">
        {items.map((item) => (
          <button
            key={item.id}
            type="button"
            className="tick"
            data-failed={item.status === 'Failed' || undefined}
            style={{ left: `${(secondsOfDay(item.startedAt) / 86_400) * 100}%` }}
            title={formatTime(item.startedAt)}
            aria-label={`Clip de las ${formatTime(item.startedAt)}`}
            onClick={() => onOpen(item.id)}
          />
        ))}
      </div>
      <div className="axis">
        <span>00:00</span>
        <span>06:00</span>
        <span>12:00</span>
        <span>18:00</span>
        <span>24:00</span>
      </div>
    </div>
  )
}
