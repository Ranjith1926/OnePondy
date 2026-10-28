'use server';

import { redirect } from 'next/navigation';

import { partnerApi } from '@/lib/api';
import type { BusinessDetail } from '@/lib/apiTypes';
import { failureState, type FormState } from '@/lib/formState';

function toNumber(value: FormDataEntryValue | null): number | null {
  const trimmed = String(value ?? '').trim();
  return trimmed ? Number(trimmed) : null;
}

export async function applyAsPartner(_previous: FormState, form: FormData): Promise<FormState> {
  let business: BusinessDetail;
  try {
    business = await partnerApi<BusinessDetail>('/api/v1/partner/businesses', {
      method: 'POST',
      json: {
        name: form.get('name'),
        businessType: form.get('businessType'),
        description: form.get('description') || null,
        phoneNumber: form.get('phoneNumber') || null,
        email: form.get('email') || null,
        address: form.get('address') || null,
        lat: toNumber(form.get('lat')),
        lng: toNumber(form.get('lng')),
        gstin: form.get('gstin') || null,
        fssaiLicenseNumber: form.get('fssaiLicenseNumber') || null,
      },
    });
  } catch (error) {
    return failureState(error);
  }
  redirect(`/partner/businesses/${business.id}?saved=applied`);
}
