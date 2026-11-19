import { useQuery } from '@tanstack/react-query'
import { api } from '../../api/client'
import { useCanManage } from '../account/useMe'

/** La API revisa cada 5 min y avisa por SignalR (HealthUpdated); el sondeo cubre lo que cambia sin abrir problemas. */
export function useSystemStatus() {
  // Un usuario de solo lectura no ve Estado: la API respondería 403.
  const enabled = useCanManage()
  return useQuery({
    queryKey: ['status'],
    queryFn: api.status,
    enabled,
    // Mientras la API no termina su primera revisión, se pregunta más seguido.
    refetchInterval: (query) => (query.state.data === null ? 5_000 : 60_000),
  })
}
