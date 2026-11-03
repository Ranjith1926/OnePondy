import type { Metadata } from 'next';

import { ApiError, publicApi } from '@/lib/api';
import type { SharedRide } from '@/lib/apiTypes';
import { formatDateTime, rideVehicleClassLabels } from '@/lib/labels';

export const metadata: Metadata = {
  title: 'Shared trip',
  // A private link: it must never be found by a search engine.
  robots: { index: false, follow: false },
};

export const dynamic = 'force-dynamic';

const statusText: Record<string, string> = {
  accepted: 'The driver is on the way to the pickup.',
  arrived: 'The driver has arrived at the pickup.',
  inProgress: 'The trip is under way.',
};

/**
 * What someone sees when a rider shares their trip with them: who is driving, which vehicle, where from and to, and, while the ride is on, where
 * the driver is. No sign-in: the secret in the link is the whole credential. When the ride ends, or the rider turns the link off, it shows nothing.
 */
export default async function SharedTripPage({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  let trip: SharedRide | null = null;
  try {
    trip = await publicApi<SharedRide>(`/api/v1/shared-rides/${encodeURIComponent(token)}`);
  } catch (error) {
    if (!(error instanceof ApiError && error.status === 404)) {
      throw error;
    }
  }

  return (
    <main className="mx-auto max-w-xl p-6 md:p-10">
      {/* Refreshes the page by itself so the position stays current. It has to be http-equiv: a plain name="refresh" is ignored by browsers. */}
      <meta httpEquiv="refresh" content="15" />
      <p className="text-xs font-bold tracking-wide text-brand uppercase">OnePondy</p>
      <h1 className="mt-1 text-2xl font-bold">Shared trip</h1>

      {trip === null ? (
        <p className="mt-4 text-sm text-muted">
          This link is no longer available. The person may have turned sharing off, or the trip has
          finished.
        </p>
      ) : trip.ended ? (
        <p className="mt-4 text-sm text-muted">This trip has ended.</p>
      ) : (
        <section
          className="mt-6 rounded-xl border border-line bg-surface p-5"
          aria-label="Trip details"
        >
          <p className="text-lg font-semibold">
            {trip.riderFirstName ? `${trip.riderFirstName}'s trip` : 'A trip'}
          </p>
          <p className="mt-1 text-sm">{statusText[trip.status] ?? 'The trip is on.'}</p>
          <dl className="mt-4 grid gap-3 text-sm">
            <div>
              <dt className="font-semibold">From</dt>
              <dd className="text-muted">{trip.pickupLabel}</dd>
            </div>
            <div>
              <dt className="font-semibold">To</dt>
              <dd className="text-muted">{trip.destinationLabel}</dd>
            </div>
            <div>
              <dt className="font-semibold">Driver and vehicle</dt>
              <dd className="text-muted">
                {trip.driverFirstName}
                {trip.vehicleClass ? ` · ${rideVehicleClassLabels[trip.vehicleClass]}` : ''}
                {trip.vehicleNumber ? ` · ${trip.vehicleNumber}` : ''}
              </dd>
            </div>
            {trip.driverLocation ? (
              <div>
                <dt className="font-semibold">Driver&apos;s last position</dt>
                <dd className="text-muted">
                  <a
                    className="font-semibold text-brand hover:underline"
                    target="_blank"
                    rel="noreferrer"
                    href={`https://www.openstreetmap.org/?mlat=${trip.driverLocation.latitude}&mlon=${trip.driverLocation.longitude}#map=16/${trip.driverLocation.latitude}/${trip.driverLocation.longitude}`}
                  >
                    Open on the map
                  </a>{' '}
                  (updated {formatDateTime(trip.driverLocation.at)})
                </dd>
              </div>
            ) : null}
          </dl>
          <p className="mt-4 text-xs text-muted">
            This page updates by itself every few seconds. If you are worried about this person,
            call them, or call 112.
          </p>
        </section>
      )}
    </main>
  );
}
