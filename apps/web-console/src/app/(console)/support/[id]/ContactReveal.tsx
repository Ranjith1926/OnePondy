'use client';

import { useActionState } from 'react';

import { Alert } from '@/components/Display';
import { SubmitButton } from '@/components/SubmitButton';
import type { ContactState } from '@/lib/support';

import { revealContact } from '../actions';

/**
 * How to reach the person, only on request. Opening a request does not show it, because the conversation is where support talks to them;
 * each time it is shown the API records who asked, and the button says so beforehand.
 */
export function ContactReveal({ id }: { id: string }) {
  const [state, action] = useActionState<ContactState, FormData>(revealContact, { status: 'idle' });

  if (state.status === 'shown') {
    return (
      <div className="text-sm" data-testid="contact">
        <p>
          <span className="text-muted">Phone: </span>
          {state.phoneNumber ?? 'none on file'}
        </p>
        <p>
          <span className="text-muted">E-mail: </span>
          {state.email ?? 'none on file'}
        </p>
        <p className="mt-1 text-xs text-muted">This was recorded in the audit log.</p>
      </div>
    );
  }

  return (
    <form action={action} className="flex flex-col gap-2">
      <input type="hidden" name="id" value={id} />
      {state.status === 'error' && state.message ? (
        <Alert kind="error">{state.message}</Alert>
      ) : null}
      <div>
        <SubmitButton variant="secondary" pendingLabel="Showing…">
          Show contact details
        </SubmitButton>
      </div>
      <p className="text-xs text-muted">
        Only if the request cannot be settled in the conversation. Asking is recorded.
      </p>
    </form>
  );
}
