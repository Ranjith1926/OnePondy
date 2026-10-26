import Link from 'next/link';

import { Alert, Card, linkButton, PageHeader } from '@/components/Display';
import { api } from '@/lib/api';
import type { PlaceCounts } from '@/lib/apiTypes';
import { canEditContent } from '@/lib/roles';
import { requireUser } from '@/lib/session';

export default async function DashboardPage({
  searchParams,
}: {
  searchParams: Promise<{ denied?: string }>;
}) {
  const user = await requireUser();
  const { denied } = await searchParams;
  const counts = await api<PlaceCounts>('/api/v1/admin/places/counts');

  const tiles: { label: string; value: number; href: string; hint: string }[] = [
    {
      label: 'Waiting for review',
      value: counts.pending,
      href: '/places?status=pending',
      hint: 'Check and verify, or reject with a reason.',
    },
    {
      label: 'Review due',
      value: counts.reviewDue,
      href: '/places?reviewDue=true',
      hint: 'Verified a while ago; confirm they are still accurate.',
    },
    {
      label: 'Drafts',
      value: counts.draft,
      href: '/places?status=draft',
      hint: 'Not yet submitted for review.',
    },
    {
      label: 'Rejected',
      value: counts.rejected,
      href: '/places?status=rejected',
      hint: 'Need changes before another review.',
    },
    {
      label: 'Published',
      value: counts.published,
      href: '/places?published=true',
      hint: 'Visible in the customer app.',
    },
  ];

  return (
    <>
      <PageHeader
        title={`Hello, ${user.name}`}
        description="Places appear in the app only after they are verified and published."
        actions={
          canEditContent(user.role) ? (
            <Link className={linkButton} href="/places/new">
              Add a place
            </Link>
          ) : undefined
        }
      />
      {denied ? (
        <div className="mb-6">
          <Alert kind="warning">Your role does not include that page.</Alert>
        </div>
      ) : null}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
        {tiles.map((tile) => (
          <Link key={tile.label} href={tile.href} className="group">
            <Card className="h-full transition-colors group-hover:border-brand">
              <p className="text-sm font-semibold text-muted">{tile.label}</p>
              <p className="mt-1 text-3xl font-bold">{tile.value}</p>
              <p className="mt-2 text-xs text-muted">{tile.hint}</p>
            </Card>
          </Link>
        ))}
      </div>
    </>
  );
}
