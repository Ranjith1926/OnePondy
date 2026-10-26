'use client';

import { useActionState } from 'react';

import { Button } from '@/components/Button';
import { Alert } from '@/components/Display';
import { SelectField, TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { StaffMember, StaffRole } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { roleDescriptions, roleLabels } from '@/lib/roles';

import { addStaff, changeRole } from './actions';

const roles = Object.keys(roleLabels) as StaffRole[];

function RoleOptions() {
  return roles.map((role) => (
    <option key={role} value={role}>
      {roleLabels[role]}
    </option>
  ));
}

export function AddStaffForm() {
  const [state, action] = useActionState<FormState, FormData>(addStaff, { status: 'idle' });
  return (
    <form action={action} className="flex flex-col gap-3" noValidate>
      {state.status === 'error' && state.message ? (
        <Alert kind="error">{state.message}</Alert>
      ) : null}
      <TextField
        id="email"
        label="Work email"
        type="email"
        errors={state.fieldErrors?.email}
        required
      />
      <SelectField
        id="role"
        label="Role"
        defaultValue="content_editor"
        errors={state.fieldErrors?.role}
      >
        <RoleOptions />
      </SelectField>
      <ul className="text-xs text-muted">
        {roles.map((role) => (
          <li key={role}>
            <strong>{roleLabels[role]}:</strong> {roleDescriptions[role]}
          </li>
        ))}
      </ul>
      <div>
        <SubmitButton>Give access</SubmitButton>
      </div>
      <p className="text-xs text-muted">They sign in with a code sent to this email address.</p>
    </form>
  );
}

export function StaffRow({ member, isYou }: { member: StaffMember; isYou: boolean }) {
  const [state, action] = useActionState<FormState, FormData>(changeRole, { status: 'idle' });
  return (
    <tr className="border-b border-line align-top last:border-b-0">
      <td className="py-3 pr-4">
        <p className="font-semibold">
          {member.displayName}
          {isYou ? ' (you)' : ''}
        </p>
        <p className="text-xs text-muted">{member.email}</p>
      </td>
      <td className="py-3">
        <form action={action} className="flex flex-wrap items-center gap-2">
          <input type="hidden" name="userId" value={member.userId} />
          <label className="sr-only" htmlFor={`role-${member.userId}`}>
            Role for {member.displayName}
          </label>
          <select
            id={`role-${member.userId}`}
            name="role"
            defaultValue={member.role}
            className="rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm"
          >
            <RoleOptions />
          </select>
          <SubmitButton variant="secondary" name="intent" value="change">
            Change role
          </SubmitButton>
          <Button type="submit" variant="ghost" name="intent" value="remove">
            Remove access
          </Button>
          {state.status === 'error' && state.message ? (
            <div className="w-full">
              <Alert kind="error">{state.message}</Alert>
            </div>
          ) : null}
        </form>
      </td>
    </tr>
  );
}
