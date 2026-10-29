'use client';

import { useActionState } from 'react';

import { Alert } from '@/components/Display';
import { SelectField, TextArea, TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { BusinessType } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { businessTypeLabels } from '@/lib/labels';

import { applyAsPartner } from './actions';

const businessTypes = Object.keys(businessTypeLabels) as BusinessType[];

export function ApplyForm() {
  const [state, action] = useActionState<FormState, FormData>(applyAsPartner, { status: 'idle' });

  return (
    <form action={action} className="flex flex-col gap-4" noValidate>
      {state.status === 'error' && state.message ? (
        <Alert kind="error">{state.message}</Alert>
      ) : null}
      <TextField
        id="name"
        label="Business name"
        errors={state.fieldErrors?.name}
        required
        autoFocus
      />
      <SelectField
        id="businessType"
        label="Business type"
        defaultValue="activity"
        errors={state.fieldErrors?.businessType}
      >
        {businessTypes.map((type) => (
          <option key={type} value={type}>
            {businessTypeLabels[type]}
          </option>
        ))}
      </SelectField>
      <TextArea id="description" label="Description" errors={state.fieldErrors?.description} />
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField id="phoneNumber" label="Phone number" errors={state.fieldErrors?.phoneNumber} />
        <TextField
          id="email"
          label="Business email"
          type="email"
          errors={state.fieldErrors?.email}
        />
      </div>
      <TextField id="address" label="Address" errors={state.fieldErrors?.address} />
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField
          id="lat"
          label="Latitude"
          hint="Optional; both latitude and longitude, or neither."
          errors={state.fieldErrors?.latitude}
        />
        <TextField id="lng" label="Longitude" errors={state.fieldErrors?.longitude} />
      </div>
      <TextField
        id="gstin"
        label="GSTIN"
        hint="Your GST registration number, if you have one."
        errors={state.fieldErrors?.gstin}
      />
      <TextField
        id="fssaiLicenseNumber"
        label="FSSAI licence number"
        hint="Required before a restaurant can be approved."
        errors={state.fieldErrors?.fssaiLicenseNumber}
      />
      <div>
        <SubmitButton pendingLabel="Submitting…">Submit application</SubmitButton>
      </div>
      <p className="text-xs text-muted">
        An administrator reviews every application before it goes live. You can upload GSTIN and
        licence documents once it is created.
      </p>
    </form>
  );
}
