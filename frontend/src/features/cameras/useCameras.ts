import { useMutation, useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { localDayRange, todayKey } from '../../lib/format'

/**
 * La clave es solo ['cameras'] porque EventsHubProvider la parchea con los eventos del hub;
 * el rango de "hoy" se calcula en cada consulta.
 */
export function useCameras() {
  return useQuery({ queryKey: ['cameras'], queryFn: () => api.cameras(localDayRange(todayKey())) })
}

export function useTestCamera() {
  return useMutation({ mutationFn: api.testCamera })
}
