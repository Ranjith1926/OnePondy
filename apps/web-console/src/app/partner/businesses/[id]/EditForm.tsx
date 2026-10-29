'use client';

import { useActionState } from 'react';

import { Alert } from '@/components/Display';
import { SelectField, TextArea, TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { BusinessDetail, BusinessType } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { businessTypeLabels } from '@/lib/labels';

import { updateBusiness } from './actions';

const businessTypes = Object.keys(businessTypeLabels) as BusinessType[];

export function EditForm({ business }: { business: BusinessDetail }) {
  const [state, action] = useActionState<FormState, FormData>(updateBusiness, { status: 'idle' });

  return (
    <form action={action} className="flex flex-col gap-4" noValidate>
      <input type="hidden" name="id" value={business.id} />
      <input type="hidden" name="version" value={business.version} />
      {state.status === 'error' && state.message ? (
        <Alert kind="error">{state.message}</Alert>
      ) : null}
      {state.status === 'success' && state.message ? (
        <Alert kind="success">{state.message}</Alert>
      ) : null}
      <TextField
        id="name"
        label="Business name"
        defaultValue={business.name}
        errors={state.fieldErrors?.name}
        required
      />
      <SelectField
        id="businessType"
        label="Business type"
        defaultValue={business.businessType}
        errors={state.fieldErrors?.businessType}
      >
        {businessTypes.map((type) => (
          <option key={type} value={type}>
            {businessTypeLabels[type]}
          </option>
        ))}
      </SelectField>
      <TextArea
        id="description"
        label="Description"
        defaultValue={business.description ?? ''}
        errors={state.fieldErrors?.description}
      />
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField
          id="phoneNumber"
          label="Phone number"
          defaultValue={business.phoneNumber ?? ''}
          errors={state.fieldErrors?.phoneNumber}
        />
        <TextField
          id="email"
          label="Business email"
          type="email"
          defaultValue={business.email ?? ''}
          errors={state.fieldErrors?.email}
        />
      </div>
      <TextField
        id="address"
        label="Address"
        defaultValue={business.address ?? ''}
        errors={state.fieldErrors?.address}
      />
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField
          id="lat"
          label="Latitude"
          defaultValue={business.latitude ?? ''}
          errors={state.fieldErrors?.latitude}
        />
        <TextField
          id="lng"
          label="Longitude"
          defaultValue={business.longitude ?? ''}
          errors={state.fieldErrors?.longitude}
        />
      </div>
      <TextField
        id="gstin"
        label="GSTIN"
        defaultValue={business.gstin ?? ''}
        errors={state.fieldErrors?.gstin}
      />
      <TextField
        id="fssaiLicenseNumber"
        label="FSSAI licence number"
        hint="Required before a restaurant can be approved."
        defaultValue={business.fssaiLicenseNumber ?? ''}
        errors={state.fieldErrors?.fssaiLicenseNumber}
      />
      <div>
        <SubmitButton pendingLabel="Saving…">Save changes</SubmitButton>
      </div>
    </form>
  );
}
