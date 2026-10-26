import type { Metadata } from 'next';

import { PageHeader } from '@/components/Display';
import { api } from '@/lib/api';
import type { Category } from '@/lib/apiTypes';
import { requireEditor } from '@/lib/session';

import { PlaceForm } from '../PlaceForm';

export const metadata: Metadata = { title: 'Add a place' };

export default async function NewPlacePage() {
  await requireEditor();
  const categories = await api<Category[]>('/api/v1/admin/places/categories');

  return (
    <>
      <PageHeader
        title="Add a place"
        description="New places start as drafts. Submit them for review when the details are complete."
      />
      <PlaceForm place={null} categories={categories} />
    </>
  );
}
