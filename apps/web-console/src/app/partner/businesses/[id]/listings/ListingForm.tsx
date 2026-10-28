'use client';

import { useActionState, useState } from 'react';

import { Alert } from '@/components/Display';
import { SelectField, TextArea, TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { ListingDetail, ListingType } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { listingTypeLabels } from '@/lib/labels';

import { createListing, updateListing } from './actions';

const listingTypes = Object.keys(listingTypeLabels) as ListingType[];

export function ListingForm({ businessId, listing }: { businessId: string; listing?: ListingDetail }) {
  const [state, action] = useActionState<FormState, FormData>(listing ? updateListing : createListing, {
    status: 'idle',
  });
  const [listingType, setListingType] = useState<ListingType>(listing?.listingType ?? 'activity');
  const [isBookable, setIsBookable] = useState(listing?.isBookable ?? false);

  return (
    <form action={action} className="flex flex-col gap-4" noValidate>
      <input type="hidden" name="businessId" value={businessId} />
      {listing ? (
        <>
          <input type="hidden" name="id" value={listing.id} />
          <input type="hidden" name="version" value={listing.version} />
        </>
      ) : null}
      {state.status === 'error' && state.message ? <Alert kind="error">{state.message}</Alert> : null}

      <TextField
        id="title"
        label="Title"
        defaultValue={listing?.title}
        errors={state.fieldErrors?.title}
        required
        autoFocus
      />
      <SelectField
        id="listingType"
        label="Type"
        value={listingType}
        onChange={(event) => setListingType(event.target.value as ListingType)}
        errors={state.fieldErrors?.listingType}
      >
        {listingTypes.map((type) => (
          <option key={type} value={type}>
            {listingTypeLabels[type]}
          </option>
        ))}
      </SelectField>
      <TextArea
        id="description"
        label="Description"
        defaultValue={listing?.description ?? ''}
        errors={state.fieldErrors?.description}
      />
      <TextField
        id="basePrice"
        label="Base price (INR)"
        type="number"
        min={0}
        step="0.01"
        defaultValue={listing?.basePrice ?? ''}
        errors={state.fieldErrors?.basePrice}
      />

      {listingType !== 'shop' ? (
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            name="isBookable"
            defaultChecked={isBookable}
            onChange={(event) => setIsBookable(event.target.checked)}
            className="size-4"
          />
          Customers can book this
        </label>
      ) : (
        <p className="text-sm text-muted">Shops are discovery-only and cannot be booked.</p>
      )}

      {isBookable && listingType !== 'shop' ? (
        <div className="grid gap-4 rounded-lg border border-line p-4 sm:grid-cols-2">
          <TextField
            id="freeCancellationHours"
            label="Free cancellation (hours before)"
            type="number"
            min={0}
            defaultValue={listing?.cancellationPolicy?.freeCancellationHours ?? 24}
            errors={state.fieldErrors?.freeCancellationHours}
          />
          <TextField
            id="refundPercentage"
            label="Refund after that (%)"
            type="number"
            min={0}
            max={100}
            defaultValue={listing?.cancellationPolicy?.refundPercentage ?? 0}
            errors={state.fieldErrors?.refundPercentage}
          />
          <div className="sm:col-span-2">
            <TextArea
              id="cancellationNotes"
              label="Cancellation notes (optional)"
              rows={2}
              defaultValue={listing?.cancellationPolicy?.notes ?? ''}
              errors={state.fieldErrors?.cancellationNotes}
            />
          </div>
        </div>
      ) : null}

      <div>
        <SubmitButton pendingLabel="Saving…">{listing ? 'Save changes' : 'Create listing'}</SubmitButton>
      </div>
    </form>
  );
}
