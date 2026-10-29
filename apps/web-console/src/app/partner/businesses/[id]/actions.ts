'use server';

import { revalidatePath } from 'next/cache';

import { partnerApi } from '@/lib/api';
import type { BusinessDetail, BusinessDocument } from '@/lib/apiTypes';
import { failureState, type FormState } from '@/lib/formState';

function toNumber(value: FormDataEntryValue | null): number | null {
  const trimmed = String(value ?? '').trim();
  return trimmed ? Number(trimmed) : null;
}

export async function updateBusiness(_previous: FormState, form: FormData): Promise<FormState> {
  const id = String(form.get('id') ?? '');
  try {
    await partnerApi<BusinessDetail>(`/api/v1/partner/businesses/${id}`, {
      method: 'PUT',
      json: {
        version: Number(form.get('version')),
        business: {
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
      },
    });
  } catch (error) {
    return failureState(error);
  }
  revalidatePath(`/partner/businesses/${id}`);
  return { status: 'success', message: 'Saved. Your application is back in review.' };
}

export async function uploadDocument(_previous: FormState, form: FormData): Promise<FormState> {
  const id = String(form.get('id') ?? '');
  const file = form.get('file');
  if (!(file instanceof File) || file.size === 0) {
    return { status: 'error', message: 'Choose a file to upload.' };
  }

  const body = new FormData();
  body.set('file', file);
  body.set('kind', String(form.get('kind') ?? 'other'));

  try {
    await partnerApi<BusinessDocument>(`/api/v1/partner/businesses/${id}/documents`, {
      method: 'POST',
      form: body,
    });
  } catch (error) {
    return failureState(error);
  }
  revalidatePath(`/partner/businesses/${id}`);
  return { status: 'success' };
}

export async function removeDocument(form: FormData): Promise<void> {
  const id = String(form.get('id') ?? '');
  const documentId = String(form.get('documentId') ?? '');
  await partnerApi<void>(`/api/v1/partner/businesses/${id}/documents/${documentId}`, {
    method: 'DELETE',
  });
  revalidatePath(`/partner/businesses/${id}`);
}
