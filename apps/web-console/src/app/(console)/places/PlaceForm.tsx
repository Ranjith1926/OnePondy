'use client';

import { useActionState } from 'react';

import { Alert, Card } from '@/components/Display';
import { SelectField, TextArea, TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { Category, PlaceDetail } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { dayLabels, days, visitorCategories, visitorCategoryLabels } from '@/lib/labels';
import {
  feeField,
  formDefaults,
  periodField,
  PERIODS_PER_DAY,
  translationField,
  translationLanguages,
} from '@/lib/placeForm';

import { savePlace } from './actions';

const languageNames: Record<(typeof translationLanguages)[number], string> = {
  ta: 'Tamil',
  fr: 'French',
};
const timeInput = 'rounded-lg border border-line-strong bg-surface px-2 py-1.5 text-sm';

export function PlaceForm({
  place,
  categories,
}: {
  place: PlaceDetail | null;
  categories: Category[];
}) {
  const [state, action] = useActionState<FormState, FormData>(savePlace, { status: 'idle' });
  const errors = state.fieldErrors ?? {};
  const defaults = formDefaults(place);
  const isLive = place?.status === 'verified';

  return (
    <form action={action} className="flex flex-col gap-6" noValidate>
      {place ? (
        <>
          <input type="hidden" name="id" value={place.id} />
          <input type="hidden" name="version" value={place.version} />
        </>
      ) : null}

      {state.status === 'error' && state.message ? (
        <Alert kind="error">{state.message}</Alert>
      ) : null}
      {isLive ? (
        <Alert kind="warning">
          This place is verified{place?.isPublished ? ' and published' : ''}. Saving changes returns
          it to draft and takes it off the app until it is verified again.
        </Alert>
      ) : null}

      <Card title="Basics">
        <div className="grid gap-4 md:grid-cols-2">
          <TextField
            id="name"
            label="Name (English)"
            defaultValue={place?.name}
            errors={errors.name}
            required
            maxLength={180}
          />
          <SelectField
            id="categoryCode"
            label="Category"
            defaultValue={place?.categoryCode ?? ''}
            errors={errors.categoryCode}
            required
          >
            <option value="" disabled>
              Choose a category
            </option>
            {categories.map((category) => (
              <option key={category.code} value={category.code}>
                {category.name}
              </option>
            ))}
          </SelectField>
          <div className="md:col-span-2">
            <TextField
              id="address"
              label="Address"
              defaultValue={place?.address ?? ''}
              errors={errors.address}
              maxLength={500}
            />
          </div>
        </div>
      </Card>

      <Card title="Description">
        <div className="flex flex-col gap-4">
          <TextArea
            id="description"
            label="Description"
            hint="What visitors will find. Facts only; anything that changes, such as prices, belongs below."
            defaultValue={place?.description ?? ''}
            errors={errors.description}
            rows={5}
            maxLength={5000}
          />
          <TextArea
            id="visitorGuidance"
            label="Visitor guidance"
            hint="Dress code, accessibility, best time to visit, what to carry."
            defaultValue={place?.visitorGuidance ?? ''}
            errors={errors.visitorGuidance}
            rows={3}
            maxLength={2000}
          />
        </div>
      </Card>

      <Card title="Location">
        <div className="grid gap-4 md:grid-cols-2">
          <TextField
            id="latitude"
            label="Latitude"
            inputMode="decimal"
            hint="For example 11.9341. In Google Maps, right-click the place to copy its coordinates."
            defaultValue={place?.latitude ?? ''}
            errors={errors.latitude}
          />
          <TextField
            id="longitude"
            label="Longitude"
            inputMode="decimal"
            hint="For example 79.8306."
            defaultValue={place?.longitude ?? ''}
            errors={errors.longitude}
          />
        </div>
      </Card>

      <Card title="Opening hours">
        <div className="flex flex-col gap-4">
          <label className="flex items-center gap-2 text-sm font-semibold">
            <input
              type="checkbox"
              name="alwaysOpen"
              defaultChecked={place?.openingHours?.alwaysOpen ?? false}
              className="size-4"
            />
            Always open (for example a beach or promenade)
          </label>
          <fieldset aria-describedby={errors.openingHours ? 'hours-error' : undefined}>
            <legend className="mb-2 text-sm text-muted">
              Times in India time. Leave a day empty when the place is closed. Use the second slot
              for a lunch break.
            </legend>
            <div className="flex flex-col gap-2">
              {days.map((day) => (
                <div key={day} className="flex flex-wrap items-center gap-2">
                  <span className="w-24 text-sm font-semibold">{dayLabels[day]}</span>
                  {Array.from({ length: PERIODS_PER_DAY }, (_, index) => (
                    <span key={index} className="flex items-center gap-1 text-sm">
                      <input
                        type="time"
                        name={periodField(day, index, 'opens')}
                        aria-label={`${dayLabels[day]} slot ${index + 1} opens`}
                        defaultValue={defaults.period(day, index, 'opens')}
                        className={timeInput}
                      />
                      <span aria-hidden="true">–</span>
                      <input
                        type="time"
                        name={periodField(day, index, 'closes')}
                        aria-label={`${dayLabels[day]} slot ${index + 1} closes`}
                        defaultValue={defaults.period(day, index, 'closes')}
                        className={timeInput}
                      />
                    </span>
                  ))}
                </div>
              ))}
            </div>
          </fieldset>
          {errors.openingHours ? (
            <div id="hours-error" className="text-xs font-medium text-danger">
              {errors.openingHours.map((error) => (
                <p key={error}>{error}</p>
              ))}
            </div>
          ) : null}
          <TextField
            id="hoursNotes"
            label="Notes"
            hint="For example: Closed on public holidays."
            defaultValue={place?.openingHours?.notes ?? ''}
            maxLength={500}
          />
        </div>
      </Card>

      <Card title="Entry fees">
        <p className="mb-4 text-sm text-muted">
          In rupees. Enter 0 for free entry; leave empty when a category does not apply.
        </p>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {visitorCategories.map((category) => (
            <TextField
              key={category}
              id={feeField(category)}
              label={visitorCategoryLabels[category]}
              inputMode="decimal"
              defaultValue={defaults.fee(category)}
            />
          ))}
        </div>
        {errors.entryFees ? (
          <div className="mt-2 text-xs font-medium text-danger" role="alert">
            {errors.entryFees.map((error) => (
              <p key={error}>{error}</p>
            ))}
          </div>
        ) : null}
      </Card>

      <Card title="Translations">
        <p className="mb-4 text-sm text-muted">
          Optional. The app shows English wherever a translation is missing. Translations are
          published with the place once it is verified.
        </p>
        <div className="grid gap-6 lg:grid-cols-2">
          {translationLanguages.map((language) => {
            const key = (field: string) => `translations.${language}.${field}`;
            return (
              <fieldset key={language} className="flex flex-col gap-3">
                <legend className="mb-1 text-sm font-bold">{languageNames[language]}</legend>
                <TextField
                  id={translationField(language, 'name')}
                  label="Name"
                  lang={language}
                  defaultValue={defaults.translation(language, 'name')}
                  errors={errors[key('name')]}
                />
                <TextArea
                  id={translationField(language, 'description')}
                  label="Description"
                  lang={language}
                  rows={4}
                  defaultValue={defaults.translation(language, 'description')}
                  errors={errors[key('description')]}
                />
                <TextArea
                  id={translationField(language, 'visitorGuidance')}
                  label="Visitor guidance"
                  lang={language}
                  rows={2}
                  defaultValue={defaults.translation(language, 'visitorGuidance')}
                  errors={errors[key('visitorGuidance')]}
                />
              </fieldset>
            );
          })}
        </div>
        {errors.translations ? (
          <div className="mt-2 text-xs font-medium text-danger" role="alert">
            {errors.translations.map((error) => (
              <p key={error}>{error}</p>
            ))}
          </div>
        ) : null}
      </Card>

      <div className="flex justify-end">
        <SubmitButton>{place ? 'Save changes' : 'Create place'}</SubmitButton>
      </div>
    </form>
  );
}
