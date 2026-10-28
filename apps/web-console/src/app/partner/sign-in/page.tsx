import type { Metadata } from 'next';
import { redirect } from 'next/navigation';

import { Alert } from '@/components/Display';
import { getPartnerUser } from '@/lib/session';
import { safeNextPath } from '@/lib/sessionCookie';

import { SignInForm } from './SignInForm';

export const metadata: Metadata = { title: 'Partner sign-in' };

export default async function PartnerSignInPage({
  searchParams,
}: {
  searchParams: Promise<{ next?: string; reason?: string }>;
}) {
  const { next, reason } = await searchParams;
  const safe = safeNextPath(next);
  const target = safe === '/' ? '/partner' : safe;
  if (await getPartnerUser()) {
    redirect(target);
  }

  return (
    <main className="flex min-h-screen items-center justify-center px-4">
      <div className="w-full max-w-sm rounded-2xl border border-line bg-surface p-8 shadow-sm">
        <p className="text-sm font-bold tracking-wide text-brand uppercase">OnePondy</p>
        <h1 className="mt-1 mb-6 text-2xl font-bold">Partner sign-in</h1>
        {reason === 'expired' ? (
          <div className="mb-4">
            <Alert kind="warning">Your session has ended. Sign in again to continue.</Alert>
          </div>
        ) : null}
        <SignInForm next={target} />
        <p className="mt-6 text-xs text-muted">
          This is the same account you use in the OnePondy app. New here? Signing in creates your
          account automatically.
        </p>
      </div>
    </main>
  );
}
