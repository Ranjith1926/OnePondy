import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import { useAppState } from '../../../state';
import { supportApi, type NewTicketInput } from '../api/supportApi';

/** Every support query starts with this, so one invalidation refreshes the list and every open request. */
const supportKey = ['support'] as const;
export const ticketsQueryKey = [...supportKey, 'tickets'] as const;
export const ticketQueryKey = (id: string) => [...supportKey, 'ticket', id] as const;
export const grievanceQueryKey = [...supportKey, 'grievance'] as const;

/** How often an open request is re-read while its screen is shown, so an answer appears without leaving and coming back. */
const ticketRefetchMs = 30_000;

/** Only asks while signed in (a read made before signing in fails with "not signed in", and that failure would stay cached after), and only when the screen can show the result. */
export function useMyTickets(enabled = true) {
  const { auth } = useAppState();
  return useQuery({
    queryKey: ticketsQueryKey,
    queryFn: () => supportApi.list(),
    enabled: enabled && auth.status === 'authenticated',
  });
}

export function useTicket(id: string) {
  const { auth } = useAppState();
  return useQuery({
    queryKey: ticketQueryKey(id),
    queryFn: () => supportApi.get(id),
    enabled: auth.status === 'authenticated',
    refetchInterval: (query) => (query.state.data?.status === 'resolved' ? false : ticketRefetchMs),
  });
}

/** Who the grievance officer is. Public: shown to everyone, signed in or not. */
export function useGrievance() {
  return useQuery({
    queryKey: grievanceQueryKey,
    queryFn: () => supportApi.grievance(),
    staleTime: 10 * 60_000,
  });
}

export function useCreateTicket() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: NewTicketInput) => supportApi.create(input),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: supportKey }),
  });
}

export function useReplyToTicket(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: string) => supportApi.reply(id, body),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: supportKey }),
  });
}
