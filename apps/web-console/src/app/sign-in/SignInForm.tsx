'use client';

import { useActionState } from 'react';

import { Button } from '@/components/Button';
import { Alert } from '@/components/Display';
import { TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';

import { signIn, type SignInState } from './actions';

const initialState: SignInState = { step: 'email' };

export function SignInForm({ next }: { next: string }) {
  const [state, action] = useActionState(signIn, initialState);

  if (state.step === 'email') {
    return (
      <form action={action} className="flex flex-col gap-4" noValidate>
        <input type="hidden" name="intent" value="request" />
        {state.message ? <Alert kind="error">{state.message}</Alert> : null}
        <TextField
          id="email"
          label="Work email"
          type="email"
          autoComplete="email"
          defaultValue={state.email}
          errors={state.fieldErrors?.email}
          required
          autoFocus
        />
        <SubmitButton pendingLabel="Sending code…">Send sign-in code</SubmitButton>
      </form>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <form action={action} className="flex flex-col gap-4" noValidate>
        <input type="hidden" name="intent" value="verify" />
        <input type="hidden" name="next" value={next} />
        <p className="text-sm text-muted" role="status">
          If {state.email} has console access, we have sent a 6-digit code to{' '}
          {state.maskedDestination}. It expires in 5 minutes.
        </p>
        {state.message ? <Alert kind="error">{state.message}</Alert> : null}
        <TextField
          id="code"
          label="Sign-in code"
          inputMode="numeric"
          autoComplete="one-time-code"
          maxLength={6}
          errors={state.fieldErrors?.code}
          required
          autoFocus
        />
        <SubmitButton pendingLabel="Checking…">Sign in</SubmitButton>
      </form>
      <form action={action}>
        <input type="hidden" name="intent" value="restart" />
        <Button type="submit" variant="ghost" className="w-full">
          Use a different email
        </Button>
      </form>
    </div>
  );
}
