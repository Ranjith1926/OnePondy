'use client';

import { useActionState } from 'react';

import { Button } from '@/components/Button';
import { Alert, Card } from '@/components/Display';
import { SubmitButton } from '@/components/SubmitButton';
import type { AvailabilitySlot } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { formatDateTime } from '@/lib/labels';

import { createSlot, removeSlot } from '../actions';

export function SlotManager({
  businessId,
  listingId,
  slots,
}: {
  businessId: string;
  listingId: string;
  slots: AvailabilitySlot[];
}) {
  const [state, action] = useActionState<FormState, FormData>(createSlot, { status: 'idle' });

  return (
    <Card title="Availability">
      <div id="slots" className="flex flex-col gap-4">
        {slots.length === 0 ? (
          <p className="text-sm text-muted">No availability yet. Add a slot below.</p>
        ) : (
          <ul className="flex flex-col gap-2 text-sm">
            {slots.map((slot) => (
              <li
                key={slot.id}
                className="flex flex-wrap items-center justify-between gap-2 border-t border-line pt-2 first:border-t-0 first:pt-0"
              >
                <span>
                  {formatDateTime(slot.startAt)} – {formatDateTime(slot.endAt)} · {slot.reservedCount}/{slot.capacity} booked
                </span>
                {slot.reservedCount === 0 ? (
                  <form action={removeSlot}>
                    <input type="hidden" name="businessId" value={businessId} />
                    <input type="hidden" name="listingId" value={listingId} />
                    <input type="hidden" name="slotId" value={slot.id} />
                    <Button type="submit" variant="danger">
                      Remove
                    </Button>
                  </form>
                ) : null}
              </li>
            ))}
          </ul>
        )}

        <form action={action} className="flex flex-col gap-3 border-t border-line pt-4" noValidate>
          <h3 className="font-bold">Add a slot</h3>
          <input type="hidden" name="businessId" value={businessId} />
          <input type="hidden" name="listingId" value={listingId} />
          {state.status === 'error' && state.message ? <Alert kind="error">{state.message}</Alert> : null}
          <div className="grid gap-3 sm:grid-cols-3">
            <div className="flex flex-col gap-1">
              <label htmlFor="startAt" className="text-sm font-semibold">
                Starts
              </label>
              <input
                id="startAt"
                name="startAt"
                type="datetime-local"
                required
                className="rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm"
              />
            </div>
            <div className="flex flex-col gap-1">
              <label htmlFor="endAt" className="text-sm font-semibold">
                Ends
              </label>
              <input
                id="endAt"
                name="endAt"
                type="datetime-local"
                required
                className="rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm"
              />
            </div>
            <div className="flex flex-col gap-1">
              <label htmlFor="capacity" className="text-sm font-semibold">
                Capacity
              </label>
              <input
                id="capacity"
                name="capacity"
                type="number"
                min={1}
                defaultValue={1}
                required
                className="rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm"
              />
              {state.fieldErrors?.capacity?.map((error) => (
                <p key={error} className="text-xs font-medium text-danger">
                  {error}
                </p>
              ))}
            </div>
          </div>
          <div>
            <SubmitButton pendingLabel="Adding…">Add slot</SubmitButton>
          </div>
        </form>
      </div>
    </Card>
  );
}
