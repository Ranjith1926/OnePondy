'use server';

import { redirect } from 'next/navigation';

import { api } from '@/lib/api';
import { failureState, type FormState } from '@/lib/formState';
import { requireAdministrator } from '@/lib/session';

const moduleKey = /^[a-zA-Z]{1,40}$/;

export async function setModuleStatus(_previous: FormState, form: FormData): Promise<FormState> {
  await requireAdministrator();
  const key = String(form.get('key') ?? '');
  if (!moduleKey.test(key)) {
    return { status: 'error', message: 'Unknown module.' };
  }

  try {
    await api(`/api/v1/admin/config/modules/${key}`, {
      method: 'PUT',
      json: { status: form.get('status') },
    });
  } catch (error) {
    return failureState(error);
  }
  redirect(`/configuration?saved=${key}`);
}

export async function setMinAppVersion(_previous: FormState, form: FormData): Promise<FormState> {
  await requireAdministrator();
  try {
    await api('/api/v1/admin/config/min-app-version', {
      method: 'PUT',
      json: { minSupportedAppVersion: form.get('minSupportedAppVersion') },
    });
  } catch (error) {
    return failureState(error);
  }
  redirect('/configuration?saved=version');
}
