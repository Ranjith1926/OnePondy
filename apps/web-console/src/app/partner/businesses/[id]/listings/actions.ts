'use server';

import { redirect } from 'next/navigation';

import { ApiError, partnerApi } from '@/lib/api';
import type { AvailabilitySlot, ListingDetail } from '@/lib/apiTypes';
import { failureState, validationState, type FormState } from '@/lib/formState';

function toNumberOrNull(value: FormDataEntryValue | null): number | null {
  const trimmed = String(value ?? '').trim();
  return trimmed ? Number(trimmed) : null;
}

function listingBody(form: FormData) {
  const isBookable = form.get('isBookable') === 'on';
  return {
    listingType: form.get('listingType'),
    title: form.get('title'),
    description: form.get('description') || null,
    isBookable,
    basePrice: toNumberOrNull(form.get('basePrice')),
    freeCancellationHours: isBookable ? toNumberOrNull(form.get('freeCancellationHours')) : null,
    refundPercentage: isBookable ? toNumberOrNull(form.get('refundPercentage')) : null,
    cancellationNotes: isBookable ? form.get('cancellationNotes') || null : null,
  };
}

export async function createListing(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = String(form.get('businessId') ?? '');
  let listingId: string;
  try {
    listingId = (
      await partnerApi<ListingDetail>(`/api/v1/partner/businesses/${businessId}/listings`, {
        method: 'POST',
        json: listingBody(form),
      })
    ).id;
  } catch (error) {
    return failureState(error);
  }

  redirect(`/partner/businesses/${businessId}/listings/${listingId}?saved=1`);
}

export async function updateListing(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = String(form.get('businessId') ?? '');
  const id = String(form.get('id') ?? '');
  try {
    await partnerApi<ListingDetail>(`/api/v1/partner/businesses/${businessId}/listings/${id}`, {
      method: 'PUT',
      json: { version: Number(form.get('version')), listing: listingBody(form) },
    });
  } catch (error) {
    return failureState(error);
  }

  redirect(`/partner/businesses/${businessId}/listings/${id}?saved=1`);
}

export async function submitListing(form: FormData): Promise<void> {
  const businessId = String(form.get('businessId') ?? '');
  const id = String(form.get('id') ?? '');
  await partnerApi<ListingDetail>(
    `/api/v1/partner/businesses/${businessId}/listings/${id}/submit`,
    {
      method: 'POST',
      json: { version: Number(form.get('version')) },
    },
  );
  redirect(`/partner/businesses/${businessId}/listings/${id}?done=submit`);
}

export async function createSlot(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = String(form.get('businessId') ?? '');
  const listingId = String(form.get('listingId') ?? '');
  const startAt = String(form.get('startAt') ?? '');
  const endAt = String(form.get('endAt') ?? '');
  const capacity = Number(form.get('capacity'));
  if (!startAt || !endAt || !Number.isFinite(capacity) || capacity < 1) {
    return validationState({ capacity: ['Enter a start, an end and a capacity of at least 1.'] });
  }

  try {
    await partnerApi<AvailabilitySlot>(
      `/api/v1/partner/businesses/${businessId}/listings/${listingId}/slots`,
      {
        method: 'POST',
        json: {
          startAt: new Date(startAt).toISOString(),
          endAt: new Date(endAt).toISOString(),
          capacity,
        },
      },
    );
  } catch (error) {
    return failureState(error);
  }

  redirect(`/partner/businesses/${businessId}/listings/${listingId}#slots`);
}

export async function removeSlot(form: FormData): Promise<void> {
  const businessId = String(form.get('businessId') ?? '');
  const listingId = String(form.get('listingId') ?? '');
  const slotId = String(form.get('slotId') ?? '');
  let refused = false;
  try {
    await partnerApi<void>(
      `/api/v1/partner/businesses/${businessId}/listings/${listingId}/slots/${slotId}`,
      { method: 'DELETE' },
    );
  } catch (error) {
    // A time that ever had a booking keeps its history, so it cannot be removed.
    if (!(error instanceof ApiError && error.status === 409)) {
      throw error;
    }
    refused = true;
  }
  redirect(
    `/partner/businesses/${businessId}/listings/${listingId}${refused ? '?slot=kept' : ''}#slots`,
  );
}
