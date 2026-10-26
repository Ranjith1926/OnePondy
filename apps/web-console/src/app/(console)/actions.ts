'use server';

import { redirect } from 'next/navigation';

import { api } from '@/lib/api';
import { clearSession } from '@/lib/session';

export async function signOut(): Promise<void> {
  try {
    await api<void>('/api/v1/auth/logout', { method: 'POST' });
  } catch (error) {
    // Best effort: the cookies are cleared either way and the session expires on its own.
    console.error(error);
  }
  await clearSession();
  redirect('/sign-in');
}
