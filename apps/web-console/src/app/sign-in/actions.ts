'use server';

import { redirect } from 'next/navigation';

import { ApiError, apiWithToken, publicApi } from '@/lib/api';
import type { CodeRequested, SessionTokens, StaffMember } from '@/lib/apiTypes';
import { staffRoleFrom } from '@/lib/roles';
import { saveSession } from '@/lib/session';
import { safeNextPath } from '@/lib/sessionCookie';

export interface SignInState {
  step: 'email' | 'code';
  email?: string;
  challengeId?: string;
  maskedDestination?: string;
  message?: string;
  fieldErrors?: Record<string, string[]>;
}

function problem(
  step: SignInState['step'],
  error: unknown,
  extra: Partial<SignInState> = {},
): SignInState {
  if (error instanceof ApiError) {
    return { step, ...extra, message: error.message, fieldErrors: error.fieldErrors };
  }
  console.error(error);
  return { step, ...extra, message: 'The OnePondy API could not be reached. Try again shortly.' };
}

/** Both steps of staff sign-in, chosen by the form's "intent" field. */
export async function signIn(previous: SignInState, form: FormData): Promise<SignInState> {
  const intent = form.get('intent');

  if (intent === 'restart') {
    return { step: 'email', email: previous.email };
  }

  if (intent === 'request') {
    const email = String(form.get('email') ?? '').trim();
    if (!email) {
      return { step: 'email', fieldErrors: { email: ['Enter your work email address.'] } };
    }
    try {
      const requested = await publicApi<CodeRequested>('/api/v1/admin/auth/otp/request', {
        method: 'POST',
        json: { email },
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
  let member: StaffMember;
  try {
    tokens = await publicApi<SessionTokens>('/api/v1/admin/auth/otp/verify', {
      method: 'POST',
      json: { challengeId: previous.challengeId, code, deviceName: 'Web console' },
    });
    member = await apiWithToken<StaffMember>('/api/v1/admin/me', tokens.accessToken);
  } catch (error) {
    return problem('code', error, context);
  }

  const role = staffRoleFrom(tokens.user.roles);
  if (!role) {
    return { step: 'email', message: 'This account does not have console access.' };
  }

  await saveSession(tokens, {
    id: member.userId,
    name: member.displayName,
    email: member.email,
    role,
  });
  redirect(safeNextPath(String(form.get('next') ?? '/')));
}
