'use client';

import { useActionState } from 'react';

import { Alert, Card } from '@/components/Display';
import { TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { FoodAreaCode, RestaurantProfile } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { foodAreaLabels } from '@/lib/labels';

import { saveRestaurant } from '../menu/actions';

const areaCodes = Object.keys(foodAreaLabels) as FoodAreaCode[];

export function RestaurantForm({ profile }: { profile: RestaurantProfile }) {
  const [state, action] = useActionState<FormState, FormData>(saveRestaurant, { status: 'idle' });
  const errors = state.fieldErrors ?? {};

  return (
    <Card title="Restaurant">
      <form action={action} className="flex flex-col gap-4" noValidate>
        <input type="hidden" name="businessId" value={profile.businessId} />
        <input type="hidden" name="version" value={profile.version} />
        {state.status === 'error' && state.message ? (
          <Alert kind="error">{state.message}</Alert>
        ) : null}
        {state.status === 'success' ? <Alert kind="success">{state.message}</Alert> : null}

        <div className="grid gap-4 sm:grid-cols-2">
          <TextField
            id="prepTimeMinutes"
            name="prepTimeMinutes"
            type="number"
            label="Preparation time (minutes)"
            hint="About how long an order takes, from accepting it to it being ready."
            min={5}
            max={180}
            defaultValue={profile.prepTimeMinutes}
            errors={errors.prepTimeMinutes}
          />
          <TextField
            id="minOrderAmount"
            name="minOrderAmount"
            type="number"
            label="Smallest order (₹)"
            hint="0 for no minimum."
            min={0}
            defaultValue={profile.minOrderAmount}
            errors={errors.minOrderAmount}
          />
        </div>

        <fieldset className="flex flex-col gap-2">
          <legend className="text-sm font-semibold">Areas you deliver to</legend>
          <div className="grid gap-2 sm:grid-cols-2">
            {areaCodes.map((code) => (
              <label key={code} className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  name="deliveryAreas"
                  value={code}
                  defaultChecked={profile.deliveryAreas.includes(code)}
                  className="size-4"
                />
                {foodAreaLabels[code]}
              </label>
            ))}
          </div>
          {errors.deliveryAreas?.map((error) => (
            <p key={error} className="text-xs font-medium text-danger">
              {error}
            </p>
          ))}
        </fieldset>

        <label className="flex items-start gap-2 text-sm">
          <input
            type="checkbox"
            name="isAcceptingOrders"
            defaultChecked={profile.isAcceptingOrders}
            className="mt-0.5 size-4"
          />
          <span>
            <span className="font-semibold">Open for orders</span>
            <span className="block text-muted">
              Customers can only find you while this is on. It needs an approved business, at least
              one area and one available dish. Switch it off to close for the day.
            </span>
          </span>
        </label>

        <div>
          <SubmitButton pendingLabel="Saving…">Save</SubmitButton>
        </div>
      </form>
    </Card>
  );
}
