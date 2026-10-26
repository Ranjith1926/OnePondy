'use server';

import { redirect } from 'next/navigation';

import { api } from '@/lib/api';
import type { PlaceDetail } from '@/lib/apiTypes';
import { failureState, validationState, type FormState } from '@/lib/formState';
import { placeInputFromForm } from '@/lib/placeForm';
import { requireEditor } from '@/lib/session';

const workflowSteps = [
  'submit',
  'verify',
  'reject',
  'publish',
  'unpublish',
  'confirm-review',
] as const;
type WorkflowStep = (typeof workflowSteps)[number];

const isWorkflowStep = (value: unknown): value is WorkflowStep =>
  typeof value === 'string' && (workflowSteps as readonly string[]).includes(value);

/** Only ids shaped like the API's GUIDs are put into request paths. */
function guid(value: FormDataEntryValue | null): string | null {
  const text = typeof value === 'string' ? value : '';
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(text) ? text : null;
}

function optional(form: FormData, name: string): string | null {
  const value = form.get(name);
  return typeof value === 'string' && value.trim() ? value.trim() : null;
}

export async function savePlace(_previous: FormState, form: FormData): Promise<FormState> {
  await requireEditor();
  const { input, errors } = placeInputFromForm(form);
  if (Object.keys(errors).length > 0) {
    return validationState(errors);
  }

  const id = guid(form.get('id'));
  let placeId: string;
  try {
    if (id) {
      await api<PlaceDetail>(`/api/v1/admin/places/${id}`, {
        method: 'PUT',
        json: { version: Number(form.get('version')), place: input },
      });
      placeId = id;
    } else {
      placeId = (await api<PlaceDetail>('/api/v1/admin/places', { method: 'POST', json: input }))
        .id;
    }
  } catch (error) {
    return failureState(error);
  }

  redirect(`/places/${placeId}?saved=1`);
}

export async function changeStatus(_previous: FormState, form: FormData): Promise<FormState> {
  await requireEditor();
  const id = guid(form.get('id'));
  const step = form.get('step');
  if (!id || !isWorkflowStep(step)) {
    return { status: 'error', message: 'Unknown action.' };
  }

  try {
    await api<PlaceDetail>(`/api/v1/admin/places/${id}/${step}`, {
      method: 'POST',
      json: {
        version: Number(form.get('version')),
        source: optional(form, 'source'),
        reason: optional(form, 'reason'),
      },
    });
  } catch (error) {
    return failureState(error);
  }

  redirect(`/places/${id}?done=${step}`);
}

export async function uploadPhoto(_previous: FormState, form: FormData): Promise<FormState> {
  await requireEditor();
  const id = guid(form.get('placeId'));
  const file = form.get('file');
  if (!id) {
    return { status: 'error', message: 'Unknown place.' };
  }
  if (!(file instanceof File) || file.size === 0) {
    return validationState({ file: ['Choose a photo to upload.'] });
  }

  const upload = new FormData();
  upload.append('file', file, file.name);
  for (const field of ['altText', 'caption', 'licence', 'attribution', 'sourceUrl']) {
    const value = optional(form, field);
    if (value) {
      upload.append(field, value);
    }
  }

  try {
    await api(`/api/v1/admin/places/${id}/media`, { method: 'POST', form: upload });
  } catch (error) {
    return failureState(error);
  }

  redirect(`/places/${id}?photo=added#photos`);
}

export async function updatePhoto(_previous: FormState, form: FormData): Promise<FormState> {
  await requireEditor();
  const id = guid(form.get('placeId'));
  const mediaId = guid(form.get('mediaId'));
  if (!id || !mediaId) {
    return { status: 'error', message: 'Unknown photo.' };
  }

  try {
    await api(`/api/v1/admin/places/${id}/media/${mediaId}`, {
      method: 'PUT',
      json: {
        altText: optional(form, 'altText'),
        caption: optional(form, 'caption'),
        licence: optional(form, 'licence'),
        attribution: optional(form, 'attribution'),
        sourceUrl: optional(form, 'sourceUrl'),
      },
    });
  } catch (error) {
    return failureState(error);
  }

  redirect(`/places/${id}?photo=saved#photos`);
}

export async function removePhoto(form: FormData): Promise<void> {
  await requireEditor();
  const id = guid(form.get('placeId'));
  const mediaId = guid(form.get('mediaId'));
  if (id && mediaId) {
    await api(`/api/v1/admin/places/${id}/media/${mediaId}`, { method: 'DELETE' });
    redirect(`/places/${id}?photo=removed#photos`);
  }
}

/** Moves a photo one place up or down; the first photo is the cover. */
export async function movePhoto(form: FormData): Promise<void> {
  await requireEditor();
  const id = guid(form.get('placeId'));
  const order = String(form.get('order') ?? '')
    .split(',')
    .map((value) => guid(value))
    .filter((value): value is string => value !== null);
  if (id && order.length > 0) {
    await api(`/api/v1/admin/places/${id}/media/order`, {
      method: 'PUT',
      json: { mediaIds: order },
    });
    redirect(`/places/${id}#photos`);
  }
}
