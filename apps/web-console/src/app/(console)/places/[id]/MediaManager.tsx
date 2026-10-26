'use client';

import { useActionState, useState } from 'react';

import { Button } from '@/components/Button';
import { Alert, Card } from '@/components/Display';
import { SelectField, TextField } from '@/components/Field';
import { SubmitButton } from '@/components/SubmitButton';
import type { MediaItem, MediaLicence } from '@/lib/apiTypes';
import type { FormState } from '@/lib/formState';
import { licenceLabels, licenceNeedsCredit } from '@/lib/labels';

import { movePhoto, removePhoto, updatePhoto, uploadPhoto } from '../actions';

const licences = Object.keys(licenceLabels) as MediaLicence[];

function DetailsFields({
  media,
  errors,
  idPrefix,
}: {
  media?: MediaItem;
  errors: Record<string, string[]>;
  idPrefix: string;
}) {
  const [licence, setLicence] = useState<MediaLicence>(media?.licence ?? 'owned');
  return (
    <div className="grid gap-3 md:grid-cols-2">
      <div className="md:col-span-2">
        <TextField
          id={`${idPrefix}-altText`}
          name="altText"
          label="Description for screen readers"
          hint="What the photo shows, e.g. Fishing boats on Paradise Beach at sunrise."
          defaultValue={media?.altText}
          errors={errors.altText}
          maxLength={250}
          required
        />
      </div>
      <TextField
        id={`${idPrefix}-caption`}
        name="caption"
        label="Caption (optional)"
        defaultValue={media?.caption ?? ''}
        errors={errors.caption}
        maxLength={300}
      />
      <SelectField
        id={`${idPrefix}-licence`}
        name="licence"
        label="Licence"
        value={licence}
        onChange={(event) => setLicence(event.target.value as MediaLicence)}
        errors={errors.licence}
      >
        {licences.map((value) => (
          <option key={value} value={value}>
            {licenceLabels[value]}
          </option>
        ))}
      </SelectField>
      <TextField
        id={`${idPrefix}-attribution`}
        name="attribution"
        label={licenceNeedsCredit(licence) ? 'Credit (required)' : 'Credit (optional)'}
        hint="Shown with the photo, e.g. Photo: A. Kumar, CC BY 4.0."
        defaultValue={media?.attribution ?? ''}
        errors={errors.attribution}
        maxLength={300}
      />
      <TextField
        id={`${idPrefix}-sourceUrl`}
        name="sourceUrl"
        label="Source link (optional)"
        type="url"
        defaultValue={media?.sourceUrl ?? ''}
        errors={errors.sourceUrl}
        maxLength={500}
      />
    </div>
  );
}

function PhotoRow({
  placeId,
  media,
  order,
  index,
  canEdit,
}: {
  placeId: string;
  media: MediaItem;
  order: string[];
  index: number;
  canEdit: boolean;
}) {
  const [state, action] = useActionState<FormState, FormData>(updatePhoto, { status: 'idle' });
  const swap = (from: number, to: number) => {
    const next = [...order];
    [next[from], next[to]] = [next[to], next[from]];
    return next.join(',');
  };

  return (
    <li className="flex flex-col gap-3 border-t border-line py-4 first:border-t-0 md:flex-row">
      {/* Photos are already resized by the API, so Next.js image optimisation adds nothing here. */}
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img
        src={media.urls.thumb}
        alt={media.altText}
        width={160}
        height={Math.round((160 * media.height) / media.width)}
        className="h-auto w-40 rounded-lg object-cover"
      />
      <div className="flex min-w-0 flex-1 flex-col gap-1 text-sm">
        <p className="font-semibold">
          {index === 0 ? 'Cover photo · ' : ''}
          {media.altText}
        </p>
        <p className="text-muted">
          {licenceLabels[media.licence]}
          {media.attribution ? ` · ${media.attribution}` : ''} · {media.width}×{media.height}
        </p>
        {canEdit ? (
          <div className="mt-2 flex flex-wrap gap-2">
            {index > 0 ? (
              <form action={movePhoto}>
                <input type="hidden" name="placeId" value={placeId} />
                <input type="hidden" name="order" value={swap(index, index - 1)} />
                <Button type="submit" variant="secondary">
                  Move up
                </Button>
              </form>
            ) : null}
            {index < order.length - 1 ? (
              <form action={movePhoto}>
                <input type="hidden" name="placeId" value={placeId} />
                <input type="hidden" name="order" value={swap(index, index + 1)} />
                <Button type="submit" variant="secondary">
                  Move down
                </Button>
              </form>
            ) : null}
            <details className="w-full">
              <summary className="cursor-pointer font-semibold text-brand">
                Edit details or remove
              </summary>
              <form action={action} className="mt-3 flex flex-col gap-3" noValidate>
                <input type="hidden" name="placeId" value={placeId} />
                <input type="hidden" name="mediaId" value={media.id} />
                {state.status === 'error' && state.message ? (
                  <Alert kind="error">{state.message}</Alert>
                ) : null}
                <DetailsFields
                  media={media}
                  errors={state.fieldErrors ?? {}}
                  idPrefix={`photo-${media.id}`}
                />
                <div>
                  <SubmitButton variant="secondary">Save photo details</SubmitButton>
                </div>
              </form>
              <form action={removePhoto} className="mt-3">
                <input type="hidden" name="placeId" value={placeId} />
                <input type="hidden" name="mediaId" value={media.id} />
                <Button type="submit" variant="danger">
                  Remove this photo
                </Button>
              </form>
            </details>
          </div>
        ) : null}
      </div>
    </li>
  );
}

export function MediaManager({
  placeId,
  media,
  canEdit,
}: {
  placeId: string;
  media: MediaItem[];
  canEdit: boolean;
}) {
  const [state, action] = useActionState<FormState, FormData>(uploadPhoto, { status: 'idle' });
  const order = media.map((item) => item.id);

  return (
    <Card title="Photos">
      <div id="photos" className="flex flex-col gap-4">
        {media.length === 0 ? <p className="text-sm text-muted">No photos yet.</p> : null}
        <ul>
          {media.map((item, index) => (
            <PhotoRow
              key={item.id}
              placeId={placeId}
              media={item}
              order={order}
              index={index}
              canEdit={canEdit}
            />
          ))}
        </ul>
        {canEdit ? (
          <form
            action={action}
            className="flex flex-col gap-3 border-t border-line pt-4"
            noValidate
          >
            <h3 className="font-bold">Add a photo</h3>
            <p className="text-sm text-muted">
              JPEG, PNG or WebP up to 10 MB, at least 800 pixels on the long side. Location data in
              the file is removed. Photo changes go live at once; they do not need review.
            </p>
            <input type="hidden" name="placeId" value={placeId} />
            {state.status === 'error' && state.message ? (
              <Alert kind="error">{state.message}</Alert>
            ) : null}
            <div className="flex flex-col gap-1">
              <label htmlFor="file" className="text-sm font-semibold">
                Photo
              </label>
              <input
                id="file"
                name="file"
                type="file"
                accept="image/jpeg,image/png,image/webp"
                className="text-sm"
                required
              />
              {state.fieldErrors?.file?.map((error) => (
                <p key={error} className="text-xs font-medium text-danger">
                  {error}
                </p>
              ))}
            </div>
            <DetailsFields errors={state.fieldErrors ?? {}} idPrefix="new" />
            <div>
              <SubmitButton pendingLabel="Uploading…">Upload photo</SubmitButton>
            </div>
          </form>
        ) : null}
      </div>
    </Card>
  );
}
