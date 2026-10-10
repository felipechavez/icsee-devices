import { useEffect, useState } from 'react'

/** Fecha actual que se renueva cada `intervalMs` (reloj del encabezado, "hace 3 min"). */
export function useNow(intervalMs = 1000) {
  const [now, setNow] = useState(() => new Date())

  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), intervalMs)
    return () => clearInterval(timer)
  }, [intervalMs])

  return now
}
