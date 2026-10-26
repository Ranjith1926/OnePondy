import { cookies } from 'next/headers';
import { NextResponse, type NextRequest } from 'next/server';

import { SESSION_COOKIE, USER_COOKIE } from '@/lib/sessionCookie';

/** The API rejected the session (signed out elsewhere or role changed): clear it and sign in again. */
export async function GET(request: NextRequest) {
  const store = await cookies();
  store.delete(SESSION_COOKIE);
  store.delete(USER_COOKIE);
  return NextResponse.redirect(new URL('/sign-in?reason=expired', request.url));
}
