import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';

import { Alert, PageHeader } from '@/components/Display';
import { ApiError, partnerApi } from '@/lib/api';
import type { RestaurantProfile } from '@/lib/apiTypes';

import { RestaurantForm } from './RestaurantForm';

export const metadata: Metadata = { title: 'Restaurant' };

export default async function PartnerRestaurantPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;
  if (!/^[0-9a-f-]{36}$/i.test(id)) {
    notFound();
  }

  let profile: RestaurantProfile;
  try {
    profile = await partnerApi<RestaurantProfile>(`/api/v1/partner/businesses/${id}/restaurant`);
  } catch (error) {
    // Not theirs, or not a restaurant: the API answers 404 or 400 and neither is worth showing more about.
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
        title="Restaurant"
        description="How you take orders: your preparation time, the areas you deliver to, and when you are open. Your menu is on the next page."
      />
      {!profile.configured ? (
        <div className="mb-6">
          <Alert kind="info">
            Set up your restaurant, add dishes on the{' '}
            <Link href={`/partner/businesses/${id}/menu`} className="font-semibold underline">
              menu page
            </Link>
            , then switch on &quot;Open for orders&quot;.
          </Alert>
        </div>
      ) : null}
      <RestaurantForm profile={profile} />
    </>
  );
}
