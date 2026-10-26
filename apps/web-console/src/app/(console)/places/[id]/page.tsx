import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';

import { Alert, Card, PageHeader } from '@/components/Display';
import { api, ApiError } from '@/lib/api';
import type { AuditEntry, Category, Paged, PlaceDetail } from '@/lib/apiTypes';
import { auditActionLabels, formatDateTime } from '@/lib/labels';
import { canEditContent } from '@/lib/roles';
import { requireUser } from '@/lib/session';

import { PlaceForm } from '../PlaceForm';
import { MediaManager } from './MediaManager';
import { WorkflowPanel } from './WorkflowPanel';

export const metadata: Metadata = { title: 'Place' };

const doneMessages: Record<string, string> = {
  submit: 'Submitted for review.',
  verify: 'Verified. Publish it when it should appear in the app.',
  reject: 'Rejected. The editor can see your reason.',
  publish: 'Published. It now appears in the app.',
  unpublish: 'Unpublished. It no longer appears in the app.',
  'confirm-review': 'Confirmed as still accurate.',
};

const photoMessages: Record<string, string> = {
  added: 'Photo added.',
  saved: 'Photo details saved.',
  removed: 'Photo removed.',
};

async function loadPlace(id: string): Promise<PlaceDetail> {
  if (!/^[0-9a-f-]{36}$/i.test(id)) {
    notFound();
  }
  try {
    return await api<PlaceDetail>(`/api/v1/admin/places/${id}`);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      notFound();
    }
    throw error;
  }
}

export default async function PlacePage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ saved?: string; done?: string; photo?: string }>;
}) {
  const user = await requireUser();
  const { id } = await params;
  const { saved, done, photo } = await searchParams;
  const [place, categories, history] = await Promise.all([
    loadPlace(id),
    api<Category[]>('/api/v1/admin/places/categories'),
    api<Paged<AuditEntry>>(`/api/v1/admin/places/${id}/history?pageSize=50`),
  ]);
  const canEdit = canEditContent(user.role);
  const notice = saved
    ? 'Changes saved.'
    : done
      ? doneMessages[done]
      : photo
        ? photoMessages[photo]
        : undefined;

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href="/places" className="font-semibold text-brand hover:underline">
          ← All places
        </Link>
      </p>
      <PageHeader
        title={place.name}
        description={`Created by ${place.createdBy.name} on ${formatDateTime(place.createdAt)} · last changed by ${place.updatedBy.name} on ${formatDateTime(place.updatedAt)}`}
      />
      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="flex min-w-0 flex-col gap-6">
          {canEdit ? (
            <PlaceForm key={place.version} place={place} categories={categories} />
          ) : (
            <Card title="Details">
              <dl className="grid gap-3 text-sm">
                <div>
                  <dt className="font-semibold">Description</dt>
                  <dd className="text-muted">{place.description ?? '—'}</dd>
                </div>
                <div>
                  <dt className="font-semibold">Address</dt>
                  <dd className="text-muted">{place.address ?? '—'}</dd>
                </div>
                <div>
                  <dt className="font-semibold">Location</dt>
                  <dd className="text-muted">
                    {place.latitude !== null ? `${place.latitude}, ${place.longitude}` : '—'}
                  </dd>
                </div>
              </dl>
            </Card>
          )}
          <MediaManager placeId={place.id} media={place.media} canEdit={canEdit} />
        </div>

        <div className="flex flex-col gap-6">
          <WorkflowPanel place={place} canEdit={canEdit} />
          <Card title="History">
            <ol className="flex flex-col gap-3 text-sm">
              {history.items.map((entry) => (
                <li key={entry.id}>
                  <p className="font-semibold">{auditActionLabels[entry.action] ?? entry.action}</p>
                  <p className="text-xs text-muted">
                    {entry.actor?.name ?? 'System'} · {formatDateTime(entry.occurredAt)}
                  </p>
                </li>
              ))}
            </ol>
          </Card>
        </div>
      </div>
    </>
  );
}
