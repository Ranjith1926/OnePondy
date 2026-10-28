'use server';

import { redirect } from 'next/navigation';

import { partnerApi } from '@/lib/api';
import { clearPartnerSession } from '@/lib/session';

export async function signOutPartner(): Promise<void> {
  try {
    await partnerApi<void>('/api/v1/auth/logout', { method: 'POST' });
  } catch (error) {
    // Best effort: the cookies are cleared either way and the session expires on its own.
    console.error(error);
  }
  await clearPartnerSession();
  redirect('/partner/sign-in');
}
