import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';

import { PageHeader } from '@/components/Display';
import { ApiError, partnerApi } from '@/lib/api';
import type { MenuView } from '@/lib/apiTypes';

import { MenuManager } from './MenuManager';

export const metadata: Metadata = { title: 'Menu' };

export default async function PartnerMenuPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  if (!/^[0-9a-f-]{36}$/i.test(id)) {
    notFound();
  }

  let menu: MenuView;
  try {
    menu = await partnerApi<MenuView>(`/api/v1/partner/businesses/${id}/menu`);
  } catch (error) {
    if (error instanceof ApiError && (error.status === 404 || error.status === 400)) {
      notFound();
    }
    throw error;
  }

  return (
    <>
      <p className="mb-2 text-sm">
        <Link
          href={`/partner/businesses/${id}`}
          className="font-semibold text-brand hover:underline"
        >
          ← Business
        </Link>
      </p>
      <PageHeader
        title="Menu"
        description="Your categories and dishes. A dish marked sold out stays on the menu, greyed out. A dish you remove is hidden from customers but never deleted, so past orders keep their record of it."
      />
      <MenuManager businessId={id} categories={menu.categories} />
    </>
  );
}
