import type { Metadata } from 'next';
import Link from 'next/link';

import { Card, PageHeader } from '@/components/Display';

import { ListingForm } from '../ListingForm';

export const metadata: Metadata = { title: 'Add a listing' };

export default async function NewListingPage({ params }: { params: Promise<{ id: string }> }) {
  const { id: businessId } = await params;

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href={`/partner/businesses/${businessId}/listings`} className="font-semibold text-brand hover:underline">
          ← Listings
        </Link>
      </p>
      <PageHeader title="Add a listing" description="Describe what you offer. You can add availability once it is saved." />
      <Card className="max-w-2xl">
        <ListingForm businessId={businessId} />
      </Card>
    </>
  );
}
