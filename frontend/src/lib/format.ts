const timeFormat = new Intl.DateTimeFormat('es', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false })
const dayFormat = new Intl.DateTimeFormat('es', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
const shortDayFormat = new Intl.DateTimeFormat('es', { weekday: 'short', day: 'numeric', month: 'short' })

const pad = (n: number) => String(n).padStart(2, '0')

export const formatTime = (iso: string) => timeFormat.format(new Date(iso))

export const formatDay = (iso: string) => dayFormat.format(new Date(iso))

/** Reloj del encabezado: HH:mm:ss. */
export const formatClock = (d: Date) => `${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`

/** Duración corta para la miniatura: 1:05. */
export function formatDuration(seconds: number | null) {
  if (seconds == null) return '—'
  const s = Math.round(seconds)
  return `${Math.floor(s / 60)}:${pad(s % 60)}`
}

export function formatSize(bytes: number | null) {
  if (bytes == null) return ''
  return bytes < 1024 * 1024 ? `${Math.round(bytes / 1024)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

/** Gigabytes con coma decimal: "4,2". */
export const formatGb = (bytes: number) =>
  (bytes / 1024 ** 3).toLocaleString('es', { maximumFractionDigits: bytes < 10 * 1024 ** 3 ? 1 : 0 })

/** "hace 12 min", "hace 3 h", "hace 2 d". */
export function formatAgo(iso: string, now = Date.now()) {
  const minutes = Math.max(0, Math.round((now - new Date(iso).getTime()) / 60_000))
  if (minutes < 1) return 'hace un momento'
  if (minutes < 60) return `hace ${minutes} min`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `hace ${hours} h`
  return `hace ${Math.round(hours / 24)} d`
}

/** Clave de día local (yyyy-mm-dd). */
export function localDayKey(value: string | Date) {
  const d = new Date(value)
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
}

export const todayKey = () => localDayKey(new Date())

function parseDayKey(day: string) {
  const [y, m, d] = day.split('-').map(Number)
  return new Date(y, m - 1, d)
}

/** Día anterior/siguiente de una clave yyyy-mm-dd. */
export function shiftDay(day: string, delta: number) {
  const d = parseDayKey(day)
  d.setDate(d.getDate() + delta)
  return localDayKey(d)
}

/** Días completos entre dos claves (b - a). */
export function daysBetween(a: string, b: string) {
  return Math.round((parseDayKey(b).getTime() - parseDayKey(a).getTime()) / 86_400_000)
}

/** "Hoy", "Ayer" o "mié, 5 oct". */
export function dayLabel(day: string) {
  const diff = daysBetween(day, todayKey())
  if (diff === 0) return 'Hoy'
  if (diff === 1) return 'Ayer'
  return shortDayFormat.format(parseDayKey(day))
}

/** Rango [inicio, fin) del día local. */
export function localDayRange(day: string) {
  const from = parseDayKey(day)
  const to = new Date(from)
  to.setDate(to.getDate() + 1)
  return { from: from.toISOString(), to: to.toISOString() }
}

/** Segundos desde la medianoche local, para ubicar una marca en la línea de 24 h. */
export function secondsOfDay(iso: string) {
  const d = new Date(iso)
  return d.getHours() * 3600 + d.getMinutes() * 60 + d.getSeconds()
}

/** Cantidad de clips por hora local (24 posiciones). */
export function hourBuckets(items: { startedAt: string }[]) {
  const counts = Array<number>(24).fill(0)
  for (const item of items) counts[new Date(item.startedAt).getHours()]++
  return counts
}

/** "08:00": etiqueta del grupo por hora. */
export const hourLabel = (iso: string) => `${pad(new Date(iso).getHours())}:00`
