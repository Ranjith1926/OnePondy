import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Card, PageHeader } from '@/components/Display';
import { partnerApi } from '@/lib/api';
import type { BookingStatus, BookingSummary } from '@/lib/apiTypes';
import { bookingStatusLabels, formatDateTime } from '@/lib/labels';

import { BookingActions } from './BookingActions';

export const metadata: Metadata = { title: 'Bookings' };

const doneMessages: Record<string, string> = {
  accept: 'Booking accepted.',
  reject: 'Booking rejected.',
  cancel: 'Booking cancelled.',
  complete: 'Booking marked as completed.',
};

const statusTone: Record<BookingStatus, string> = {
  pending: 'bg-info-soft text-info',
  awaitingPayment: 'bg-info-soft text-info',
  confirmed: 'bg-ok-soft text-ok',
  failed: 'bg-danger-soft text-danger',
  expired: 'border border-line-strong bg-canvas text-muted',
  rejected: 'bg-danger-soft text-danger',
  cancelled: 'border border-line-strong bg-canvas text-muted',
  refundPending: 'bg-warn-soft text-warn',
  refunded: 'border border-line-strong bg-canvas text-muted',
  completed: 'bg-ok-soft text-ok',
};

export default async function PartnerBookingsPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ status?: string; done?: string }>;
}) {
  const { id: businessId } = await params;
  const { status, done } = await searchParams;
  const query = status ? `?status=${encodeURIComponent(status)}` : '';
  const bookings = await partnerApi<BookingSummary[]>(`/api/v1/partner/businesses/${businessId}/bookings${query}`);
  const notice = done ? doneMessages[done] : undefined;

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href={`/partner/businesses/${businessId}`} className="font-semibold text-brand hover:underline">
          ← Business
        </Link>
      </p>
      <PageHeader title="Bookings" description="Accept or reject requests, and mark completed bookings once the service is delivered." />
      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}

      <Card className="mb-6">
        <form className="flex flex-wrap items-end gap-3" role="search">
          <div className="flex flex-col gap-1">
            <label htmlFor="status" className="text-xs font-semibold">
              Status
            </label>
            <select
              id="status"
              name="status"
              defaultValue={status ?? ''}
              className="rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm"
            >
              <option value="">Any</option>
              {Object.entries(bookingStatusLabels).map(([value, label]) => (
                <option key={value} value={value}>
                  {label}
                </option>
              ))}
            </select>
          </div>
          <button
            type="submit"
            className="min-h-10 rounded-lg border border-line-strong px-4 text-sm font-semibold hover:bg-canvas"
          >
            Filter
          </button>
          <Link href={`/partner/businesses/${businessId}/bookings`} className="pb-2 text-sm font-semibold text-brand">
            Clear
          </Link>
        </form>
      </Card>

      {bookings.length === 0 ? (
        <Card>
          <p className="text-sm text-muted">No bookings match this filter.</p>
        </Card>
      ) : (
        <div className="flex flex-col gap-4">
          {bookings.map((booking) => (
            <Card key={booking.id}>
              <div className="flex flex-wrap items-start justify-between gap-4">
                <div>
                  <p className="font-semibold">{booking.listingTitle}</p>
                  <p className="text-sm text-muted">
                    {booking.guestCount} guest{booking.guestCount === 1 ? '' : 's'} ·{' '}
                    {booking.startAt ? formatDateTime(booking.startAt) : 'No date'} · {booking.currency} {booking.totalAmount}
                  </p>
                  <p className="text-xs text-muted">Requested {formatDateTime(booking.createdAt)}</p>
                </div>
                <span
                  className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${statusTone[booking.status]}`}
                >
                  {bookingStatusLabels[booking.status]}
                </span>
              </div>

              <BookingActions businessId={businessId} booking={booking} />
            </Card>
          ))}
        </div>
      )}
    </>
  );
}
