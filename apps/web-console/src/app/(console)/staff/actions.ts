'use server';

import { redirect } from 'next/navigation';

import { api } from '@/lib/api';
import { failureState, type FormState } from '@/lib/formState';
import { requireAdministrator } from '@/lib/session';

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export async function addStaff(_previous: FormState, form: FormData): Promise<FormState> {
  await requireAdministrator();
  try {
    await api('/api/v1/admin/staff', {
      method: 'POST',
      json: { email: form.get('email'), role: form.get('role') },
    });
  } catch (error) {
    return failureState(error);
  }
  redirect('/staff?saved=added');
}

export async function changeRole(_previous: FormState, form: FormData): Promise<FormState> {
  await requireAdministrator();
  const userId = String(form.get('userId') ?? '');
  if (!guid.test(userId)) {
    return { status: 'error', message: 'Unknown person.' };
  }

  const remove = form.get('intent') === 'remove';
  try {
    if (remove) {
      await api(`/api/v1/admin/staff/${userId}`, { method: 'DELETE' });
    } else {
      await api(`/api/v1/admin/staff/${userId}`, {
        method: 'PUT',
        json: { role: form.get('role') },
      });
    }
  } catch (error) {
    return failureState(error);
  }
  redirect(`/staff?saved=${remove ? 'removed' : 'changed'}`);
}
