'use server';

import { redirect } from 'next/navigation';

import { ApiError, apiWithToken, publicApi } from '@/lib/api';
import type { CodeRequested, CustomerProfile, SessionTokens } from '@/lib/apiTypes';
import { savePartnerSession } from '@/lib/session';
import { safeNextPath } from '@/lib/sessionCookie';

export interface PartnerSignInState {
  step: 'email' | 'code';
  email?: string;
  challengeId?: string;
  maskedDestination?: string;
  message?: string;
  fieldErrors?: Record<string, string[]>;
}

function problem(
  step: PartnerSignInState['step'],
  error: unknown,
  extra: Partial<PartnerSignInState> = {},
): PartnerSignInState {
  if (error instanceof ApiError) {
    return { step, ...extra, message: error.message, fieldErrors: error.fieldErrors };
  }
  console.error(error);
  return { step, ...extra, message: 'The OnePondy API could not be reached. Try again shortly.' };
}

/** Both steps of partner sign-in: the same one-time code flow a customer uses in the app (spec §1). */
export async function signIn(previous: PartnerSignInState, form: FormData): Promise<PartnerSignInState> {
  const intent = form.get('intent');

  if (intent === 'restart') {
    return { step: 'email', email: previous.email };
  }

  if (intent === 'request') {
    const email = String(form.get('email') ?? '').trim();
    if (!email) {
      return { step: 'email', fieldErrors: { email: ['Enter your email address.'] } };
    }
    try {
      const requested = await publicApi<CodeRequested>('/api/v1/auth/otp/request', {
        method: 'POST',
        json: { channel: 'email', destination: email },
      });
      return {
        step: 'code',
        email,
        challengeId: requested.challengeId,
        maskedDestination: requested.maskedDestination,
      };
    } catch (error) {
      return problem('email', error, { email });
    }
  }

  const code = String(form.get('code') ?? '').trim();
  const context = {
    email: previous.email,
    challengeId: previous.challengeId,
    maskedDestination: previous.maskedDestination,
  };
  if (!/^\d{6}$/.test(code)) {
    return { step: 'code', ...context, fieldErrors: { code: ['Enter the 6-digit code.'] } };
  }

  let tokens: SessionTokens;
  let profile: CustomerProfile;
  try {
    tokens = await publicApi<SessionTokens>('/api/v1/auth/otp/verify', {
      method: 'POST',
      json: { challengeId: previous.challengeId, code, deviceName: 'Partner console' },
    });
    profile = await apiWithToken<CustomerProfile>('/api/v1/profile', tokens.accessToken);
  } catch (error) {
    return problem('code', error, context);
  }

  await savePartnerSession(tokens, {
    id: profile.id,
    name: profile.displayName,
    email: profile.email,
    phoneNumber: profile.phoneNumber,
  });
  redirect(safeNextPath(String(form.get('next') ?? '/partner')));
}
