import { createContext, useContext, useEffect, useRef } from 'react'
import type { HubConnection } from '@microsoft/signalr'
import type {
  CameraInfoMessage,
  CameraStatusMessage,
  HealthUpdatedMessage,
  RecordingCompletedMessage,
  RecordingStartedMessage,
} from '../api/types'

// Separado del proveedor: Fast Refresh solo funciona si un archivo exporta únicamente componentes.

export interface HubEvents {
  CameraAwake: CameraStatusMessage
  CameraAsleep: CameraStatusMessage
  CameraInfoUpdated: CameraInfoMessage
  RecordingStarted: RecordingStartedMessage
  RecordingCompleted: RecordingCompletedMessage
  HealthUpdated: HealthUpdatedMessage
}

export type HubStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

interface HubContextValue {
  connection: HubConnection | null
  status: HubStatus
}

export const HubContext = createContext<HubContextValue>({ connection: null, status: 'disconnected' })

/** Suscribe un manejador a un evento del hub mientras el componente esté montado. */
export function useEventsHub<E extends keyof HubEvents>(event: E, handler: (message: HubEvents[E]) => void) {
  const { connection } = useContext(HubContext)
  const handlerRef = useRef(handler)

  useEffect(() => {
    handlerRef.current = handler
  })

  useEffect(() => {
    if (!connection) return
    const listener = (message: HubEvents[E]) => {
      handlerRef.current(message)
    }
    connection.on(event, listener)
    return () => connection.off(event, listener)
  }, [connection, event])
}

export function useHubStatus() {
  return useContext(HubContext).status
}
