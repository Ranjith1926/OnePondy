'use server';

import { revalidatePath } from 'next/cache';

import { partnerApi } from '@/lib/api';
import type { MenuCategory, MenuItem, RestaurantProfile } from '@/lib/apiTypes';
import { failureState, validationState, type FormState } from '@/lib/formState';

const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Only ids shaped like the API's GUIDs are put into request paths. */
function guid(value: FormDataEntryValue | null): string | null {
  return typeof value === 'string' && guidPattern.test(value) ? value : null;
}

function text(form: FormData, name: string): string {
  const value = form.get(name);
  return typeof value === 'string' ? value.trim() : '';
}

function numberOrNull(form: FormData, name: string): number | null {
  const raw = text(form, name);
  if (!raw) {
    return null;
  }
  const value = Number(raw);
  return Number.isFinite(value) ? value : null;
}

function refresh(businessId: string): void {
  revalidatePath(`/partner/businesses/${businessId}/menu`);
  revalidatePath(`/partner/businesses/${businessId}/restaurant`);
}

const unknown: FormState = { status: 'error', message: 'Unknown item.' };

/** Sets up or changes the restaurant: how long it takes, the smallest order, where it delivers, and whether it is open. */
export async function saveRestaurant(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = guid(form.get('businessId'));
  if (!businessId) {
    return unknown;
  }

  const prepTimeMinutes = numberOrNull(form, 'prepTimeMinutes');
  const minOrderAmount = numberOrNull(form, 'minOrderAmount') ?? 0;
  if (prepTimeMinutes === null) {
    return validationState({ prepTimeMinutes: ['Enter how many minutes an order takes.'] });
  }

  const version = numberOrNull(form, 'version');
  try {
    await partnerApi<RestaurantProfile>(`/api/v1/partner/businesses/${businessId}/restaurant`, {
      method: 'PUT',
      json: {
        prepTimeMinutes,
        minOrderAmount,
        isAcceptingOrders: form.get('isAcceptingOrders') === 'on',
        deliveryAreas: form.getAll('deliveryAreas').map(String),
        version: version && version > 0 ? version : null,
      },
    });
  } catch (error) {
    return failureState(error);
  }

  refresh(businessId);
  return { status: 'success', message: 'Saved.' };
}

export async function addCategory(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = guid(form.get('businessId'));
  if (!businessId) {
    return unknown;
  }
  const name = text(form, 'name');
  if (!name) {
    return validationState({ name: ['Enter a name for the category.'] });
  }

  try {
    await partnerApi<MenuCategory>(`/api/v1/partner/businesses/${businessId}/menu/categories`, {
      method: 'POST',
      json: { name },
    });
  } catch (error) {
    return failureState(error);
  }

  refresh(businessId);
  return { status: 'success', message: 'Category added.' };
}

export async function renameCategory(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = guid(form.get('businessId'));
  const categoryId = guid(form.get('categoryId'));
  if (!businessId || !categoryId) {
    return unknown;
  }
  const name = text(form, 'name');
  if (!name) {
    return validationState({ name: ['Enter a name for the category.'] });
  }

  try {
    await partnerApi<MenuCategory>(
      `/api/v1/partner/businesses/${businessId}/menu/categories/${categoryId}`,
      {
        method: 'PUT',
        json: {
          name,
          position: numberOrNull(form, 'position'),
          version: numberOrNull(form, 'version'),
        },
      },
    );
  } catch (error) {
    return failureState(error);
  }

  refresh(businessId);
  return { status: 'success', message: 'Saved.' };
}

export async function deleteCategory(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = guid(form.get('businessId'));
  const categoryId = guid(form.get('categoryId'));
  if (!businessId || !categoryId) {
    return unknown;
  }

  try {
    await partnerApi<void>(
      `/api/v1/partner/businesses/${businessId}/menu/categories/${categoryId}`,
      { method: 'DELETE' },
    );
  } catch (error) {
    return failureState(error);
  }

  refresh(businessId);
  return { status: 'success', message: 'Category removed.' };
}

function dishBody(form: FormData) {
  return {
    categoryId: guid(form.get('categoryId')),
    name: text(form, 'name'),
    description: text(form, 'description') || null,
    price: numberOrNull(form, 'price'),
    isVeg: form.get('isVeg') === 'on',
    taxRatePercent: numberOrNull(form, 'taxRatePercent'),
  };
}

export async function addDish(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = guid(form.get('businessId'));
  if (!businessId) {
    return unknown;
  }
  const body = dishBody(form);
  if (!body.categoryId) {
    return validationState({ categoryId: ['Choose a category.'] });
  }

  try {
    await partnerApi<MenuItem>(`/api/v1/partner/businesses/${businessId}/menu/items`, {
      method: 'POST',
      json: body,
    });
  } catch (error) {
    return failureState(error);
  }

  refresh(businessId);
  return { status: 'success', message: 'Dish added.' };
}

export async function updateDish(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = guid(form.get('businessId'));
  const itemId = guid(form.get('itemId'));
  if (!businessId || !itemId) {
    return unknown;
  }
  const body = dishBody(form);
  if (!body.categoryId) {
    return validationState({ categoryId: ['Choose a category.'] });
  }

  try {
    await partnerApi<MenuItem>(`/api/v1/partner/businesses/${businessId}/menu/items/${itemId}`, {
      method: 'PUT',
      json: { ...body, version: numberOrNull(form, 'version') },
    });
  } catch (error) {
    return failureState(error);
  }

  refresh(businessId);
  return { status: 'success', message: 'Saved.' };
}

/** The sold-out switch, and taking a dish off the menu for good (`archive`). */
export async function changeDish(_previous: FormState, form: FormData): Promise<FormState> {
  const businessId = guid(form.get('businessId'));
  const itemId = guid(form.get('itemId'));
  const step = form.get('step');
  if (
    !businessId ||
    !itemId ||
    (step !== 'sold-out' && step !== 'available' && step !== 'archive')
  ) {
    return unknown;
  }

  const version = numberOrNull(form, 'version');
  try {
    if (step === 'archive') {
      await partnerApi<MenuItem>(
        `/api/v1/partner/businesses/${businessId}/menu/items/${itemId}/archive`,
        {
          method: 'POST',
          json: { version },
        },
      );
    } else {
      await partnerApi<MenuItem>(
        `/api/v1/partner/businesses/${businessId}/menu/items/${itemId}/availability`,
        {
          method: 'POST',
          json: { isAvailable: step === 'available', version },
        },
      );
    }
  } catch (error) {
    return failureState(error);
  }

  refresh(businessId);
  return { status: 'success' };
}
