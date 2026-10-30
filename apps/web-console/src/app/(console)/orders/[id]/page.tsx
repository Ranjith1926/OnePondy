import type { Metadata } from 'next';
import Link from 'next/link';
import { notFound } from 'next/navigation';

import { Alert, Card, PageHeader } from '@/components/Display';
import { api, ApiError } from '@/lib/api';
import type { AdminOrderDetail } from '@/lib/apiTypes';
import {
  attentionLabels,
  deliveryStatusLabels,
  foodAreaLabels,
  adminOrderStatusLabels,
  formatDateTime,
  formatMoney,
  orderActorLabels,
  refundReasonLabels,
  refundStatusLabels,
  vehicleTypeLabels,
} from '@/lib/labels';
import { requireAdministrator } from '@/lib/session';

import { OrderPanel } from './OrderPanel';

export const metadata: Metadata = { title: 'Order' };

const doneMessages: Record<string, string> = {
  cancel: 'Cancelled. The customer can see your reason.',
  release: 'The delivery is open to the other drivers again.',
};

async function loadOrder(id: string): Promise<AdminOrderDetail> {
  if (!/^[0-9a-f-]{36}$/i.test(id)) {
    notFound();
  }
  try {
    return await api<AdminOrderDetail>(`/api/v1/admin/food-orders/${id}`);
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      notFound();
    }
    throw error;
  }
}

export default async function OrderPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ done?: string }>;
}) {
  await requireAdministrator();
  const { id } = await params;
  const { done } = await searchParams;
  const detail = await loadOrder(id);
  const { order, delivery } = detail;
  const notice = done ? doneMessages[done] : undefined;

  return (
    <>
      <p className="mb-2 text-sm">
        <Link href="/orders" className="font-semibold text-brand hover:underline">
          ← All orders
        </Link>
      </p>
      <PageHeader
        title={order.restaurantName}
        description={`${adminOrderStatusLabels[order.status]} · placed ${formatDateTime(order.createdAt)} · ${formatMoney(order.price.totalAmount, order.price.currency)}`}
      />
      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}
      {detail.attention ? (
        <div className="mb-6">
          <Alert kind="warning">{attentionLabels[detail.attention]}.</Alert>
        </div>
      ) : null}

      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="flex min-w-0 flex-col gap-6">
          <Card title="Delivery">
            {delivery ? (
              <dl className="grid gap-3 text-sm sm:grid-cols-2">
                <div>
                  <dt className="font-semibold">State</dt>
                  <dd className="text-muted">{deliveryStatusLabels[delivery.status]}</dd>
                </div>
                <div>
                  <dt className="font-semibold">Driver</dt>
                  <dd className="text-muted">
                    {delivery.driverName
                      ? `${delivery.driverName}${delivery.vehicleType ? ` (${vehicleTypeLabels[delivery.vehicleType].toLowerCase()})` : ''}`
                      : '—'}
                  </dd>
                </div>
                <div>
                  <dt className="font-semibold">Driver phone</dt>
                  <dd className="text-muted">{delivery.driverPhone ?? '—'}</dd>
                </div>
                <div>
                  <dt className="font-semibold">Opened</dt>
                  <dd className="text-muted">{formatDateTime(delivery.openedAt)}</dd>
                </div>
                <div>
                  <dt className="font-semibold">Taken</dt>
                  <dd className="text-muted">{formatDateTime(delivery.acceptedAt)}</dd>
                </div>
                <div>
                  <dt className="font-semibold">Collected</dt>
                  <dd className="text-muted">{formatDateTime(delivery.pickedUpAt)}</dd>
                </div>
                {delivery.status === 'delivered' ? (
                  <div>
                    <dt className="font-semibold">Delivered</dt>
                    <dd className="text-muted">{formatDateTime(delivery.deliveredAt)}</dd>
                  </div>
                ) : null}
                {delivery.status === 'cancelled' ? (
                  <div>
                    <dt className="font-semibold">Cancelled</dt>
                    <dd className="text-muted">
                      {formatDateTime(delivery.cancelledAt)}
                      {delivery.cancelReason ? ` · ${delivery.cancelReason}` : ''}
                    </dd>
                  </div>
                ) : null}
                {delivery.position ? (
                  <div className="sm:col-span-2">
                    <dt className="font-semibold">Last reported position</dt>
                    <dd className="text-muted">
                      {delivery.position.latitude.toFixed(5)},{' '}
                      {delivery.position.longitude.toFixed(5)} ·{' '}
                      {formatDateTime(delivery.position.at)}
                      <span className="block text-xs">
                        Seen only by administrators, only while the delivery is under way, and
                        recorded in the audit log each time it is shown.
                      </span>
                    </dd>
                  </div>
                ) : null}
              </dl>
            ) : (
              <p className="text-sm text-muted">
                No delivery yet: it opens when the restaurant marks the order ready.
              </p>
            )}
          </Card>

          <Card title="Customer and address">
            <dl className="grid gap-3 text-sm sm:grid-cols-2">
              <div>
                <dt className="font-semibold">Deliver to</dt>
                <dd className="text-muted">{order.deliveryAddress}</dd>
              </div>
              <div>
                <dt className="font-semibold">Area</dt>
                <dd className="text-muted">
                  {foodAreaLabels[order.deliveryArea] ?? order.deliveryArea}
                </dd>
              </div>
              <div>
                <dt className="font-semibold">Customer phone</dt>
                <dd className="text-muted">{order.contactPhone}</dd>
              </div>
              <div>
                <dt className="font-semibold">Note</dt>
                <dd className="text-muted">{order.deliveryNote ?? '—'}</dd>
              </div>
            </dl>
          </Card>

          <Card title="Dishes and price">
            <ul className="flex flex-col gap-1 text-sm">
              {order.lines.map((line) => (
                <li key={line.menuItemId} className="flex justify-between gap-4">
                  <span>
                    {line.quantity} × {line.name}
                  </span>
                  <span>{formatMoney(line.subtotal, order.price.currency)}</span>
                </li>
              ))}
            </ul>
            <dl className="mt-3 flex flex-col gap-1 border-t border-line pt-3 text-sm">
              <div className="flex justify-between">
                <dt>GST</dt>
                <dd>{formatMoney(order.price.taxAmount, order.price.currency)}</dd>
              </div>
              <div className="flex justify-between">
                <dt>Delivery</dt>
                <dd>{formatMoney(order.price.deliveryFee, order.price.currency)}</dd>
              </div>
              <div className="flex justify-between">
                <dt>Packaging</dt>
                <dd>{formatMoney(order.price.packagingFee, order.price.currency)}</dd>
              </div>
              <div className="flex justify-between font-semibold">
                <dt>Total</dt>
                <dd>{formatMoney(order.price.totalAmount, order.price.currency)}</dd>
              </div>
            </dl>
          </Card>

          <Card title="Payment and refunds">
            {order.payment ? (
              <p className="text-sm">
                Paid {formatMoney(order.payment.amount, order.payment.currency)} on{' '}
                {formatDateTime(order.payment.paidAt)}{' '}
                <span className="text-muted">({order.payment.reference})</span>
              </p>
            ) : (
              <p className="text-sm text-muted">Nothing paid online for this order.</p>
            )}
            {order.refunds.length > 0 ? (
              <ul className="mt-3 flex flex-col gap-1 text-sm">
                {order.refunds.map((refund) => (
                  <li key={refund.requestedAt}>
                    Refund {formatMoney(refund.amount, order.price.currency)} ·{' '}
                    {refundReasonLabels[refund.reason]} · {refundStatusLabels[refund.status]} ·{' '}
                    {formatDateTime(refund.requestedAt)}
                  </li>
                ))}
              </ul>
            ) : null}
          </Card>

          <Card title="History">
            <ol className="flex flex-col gap-2 text-sm">
              {order.history.map((entry) => (
                <li key={`${entry.status}-${entry.at}`} className="flex justify-between gap-4">
                  <span>
                    {adminOrderStatusLabels[entry.status]}{' '}
                    <span className="text-muted">
                      · {orderActorLabels[entry.actorType]}
                      {entry.reason ? ` · ${entry.reason}` : ''}
                    </span>
                  </span>
                  <span className="whitespace-nowrap text-muted">{formatDateTime(entry.at)}</span>
                </li>
              ))}
            </ol>
          </Card>
        </div>

        <div className="flex flex-col gap-6">
          <OrderPanel detail={detail} />
        </div>
      </div>
    </>
  );
}
