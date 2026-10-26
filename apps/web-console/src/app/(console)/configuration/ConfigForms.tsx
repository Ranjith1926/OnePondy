'use client';

import { useActionState } from 'react';

import { Alert } from '@/components/Display';
import { TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { ModuleFlag, ModuleStatus } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { formatDateTime, moduleLabels, moduleStatusLabels } from '@/lib/labels';

import { setMinAppVersion, setModuleStatus } from './actions';

const statuses = Object.keys(moduleStatusLabels) as ModuleStatus[];

export function ModuleFlagForm({ flag, canChange }: { flag: ModuleFlag; canChange: boolean }) {
  const [state, action] = useActionState<FormState, FormData>(setModuleStatus, { status: 'idle' });
  const label = moduleLabels[flag.key] ?? flag.key;

  return (
    <form
      action={action}
      className="flex flex-wrap items-center gap-3 border-b border-line py-3 last:border-b-0"
    >
      <input type="hidden" name="key" value={flag.key} />
      <div className="min-w-40 flex-1">
        <p className="font-semibold">{label}</p>
        <p className="text-xs text-muted">
          {flag.updatedAt ? `Changed ${formatDateTime(flag.updatedAt)}` : 'Not changed yet'}
        </p>
      </div>
      <label className="sr-only" htmlFor={`status-${flag.key}`}>
        {label} status
      </label>
      <select
        id={`status-${flag.key}`}
        name="status"
        defaultValue={flag.status}
        disabled={!canChange}
        className="rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm"
      >
        {statuses.map((status) => (
          <option key={status} value={status}>
            {moduleStatusLabels[status]}
          </option>
        ))}
      </select>
      {canChange ? (
        <SubmitButton variant="secondary" aria-label={`Save ${label}`}>
          Save
        </SubmitButton>
      ) : null}
      {state.status === 'error' && state.message ? (
        <div className="w-full">
          <Alert kind="error">{state.message}</Alert>
        </div>
      ) : null}
    </form>
  );
}

export function MinVersionForm({ version, canChange }: { version: string; canChange: boolean }) {
  const [state, action] = useActionState<FormState, FormData>(setMinAppVersion, { status: 'idle' });
  return (
    <form action={action} className="flex flex-wrap items-end gap-3" noValidate>
      <div className="w-48">
        <TextField
          id="minSupportedAppVersion"
          label="Minimum app version"
          defaultValue={version}
          readOnly={!canChange}
          errors={state.fieldErrors?.minSupportedAppVersion}
          placeholder="1.2.0"
        />
      </div>
      {canChange ? <SubmitButton variant="secondary">Save version</SubmitButton> : null}
      {state.status === 'error' && state.message && !state.fieldErrors ? (
        <div className="w-full">
          <Alert kind="error">{state.message}</Alert>
        </div>
      ) : null}
    </form>
  );
}
