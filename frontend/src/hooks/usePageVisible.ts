import { useSyncExternalStore } from 'react'

// Cuántas veces cambió la visibilidad: sirve de clave para que cada regreso a la página sea un intento nuevo.
let changes = 0
if (typeof document !== 'undefined') {
  document.addEventListener('visibilitychange', () => changes++)
}

function subscribe(onChange: () => void) {
  document.addEventListener('visibilitychange', onChange)
  return () => document.removeEventListener('visibilitychange', onChange)
}

const isVisible = () => document.visibilityState !== 'hidden'
const changeCount = () => changes

/**
 * ¿La página está a la vista? En el celular pasa a oculta al cambiar de app o bloquear la pantalla:
 * el vivo se corta para no mantener despierta (ni gastando batería) una cámara que nadie mira.
 * `epoch` cambia con cada ocultar/mostrar.
 */
export function usePageVisible() {
  const visible = useSyncExternalStore(subscribe, isVisible, () => true)
  const epoch = useSyncExternalStore(subscribe, changeCount, () => 0)
  return { visible, epoch }
}
