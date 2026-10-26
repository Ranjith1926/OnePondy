'use client';

import { useFormStatus } from 'react-dom';

import { Button, type ButtonProps } from './Button';

/** A submit button that disables itself and shows progress while its form is being sent. */
export function SubmitButton({
  children,
  pendingLabel,
  ...props
}: ButtonProps & { pendingLabel?: string }) {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" {...props} disabled={pending || props.disabled} aria-busy={pending}>
      {pending ? (pendingLabel ?? 'Saving…') : children}
    </Button>
  );
}
