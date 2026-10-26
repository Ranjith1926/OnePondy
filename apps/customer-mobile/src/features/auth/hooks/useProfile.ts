import { useQuery } from '@tanstack/react-query';

import { useAppState } from '../../../state';
import { authApi } from '../api/authApi';

export const profileQueryKey = ['profile'] as const;

/** The signed-in user's profile: server state, kept in TanStack Query rather than app state. */
export function useProfile() {
  const { auth } = useAppState();
  return useQuery({
    queryKey: profileQueryKey,
    queryFn: authApi.getProfile,
    enabled: auth.status === 'authenticated',
  });
}

/** "+919876543210" -> "+91 98765 43210"; other numbers are shown as stored. */
export function formatPhone(phone: string): string {
  const match = /^\+91(\d{5})(\d{5})$/.exec(phone);
  return match ? `+91 ${match[1]} ${match[2]}` : phone;
}
