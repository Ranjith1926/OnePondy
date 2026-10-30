import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Card, PageHeader } from '@/components/Display';
import { partnerApi } from '@/lib/api';
import type { PartnerOrderSummary } from '@/lib/apiTypes';
import {
  deadlineFor,
  ordersForView,
  parseView,
  toneFor,
  waitingForYou,
  type OrderTone,
} from '@/lib/foodOrders';
import { foodAreaLabels, foodOrderStatusLabels, formatDateTime, formatMoney } from '@/lib/labels';

import { AutoRefresh } from './AutoRefresh';
import { OrderActions } from './OrderActions';

export const metadata: Metadata = { title: 'Orders' };

const doneMessages: Record<string, string> = {
  accept: 'Order accepted.',
  reject: 'Order rejected.',
  preparing: 'Marked as being prepared.',
  ready: 'Marked as ready for the driver.',
  cancel: 'Order cancelled.',
};

const toneStyles: Record<OrderTone, string> = {
  info: 'bg-info-soft text-info',
  ok: 'bg-ok-soft text-ok',
  warn: 'bg-warn-soft text-warn',
  danger: 'bg-danger-soft text-danger',
  muted: 'border border-line-strong bg-canvas text-muted',
};

const REFRESH_SECONDS = 15;

export default async function PartnerOrdersPage({
  params,
  searchParams,
}: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ view?: string; done?: string }>;
}) {
  const { id: businessId } = await params;
  const { view: requested, done } = await searchParams;
  const view = parseView(requested, Object.keys(foodOrderStatusLabels));
  const all = await partnerApi<PartnerOrderSummary[]>(
    `/api/v1/partner/businesses/${businessId}/orders`,
  );
  const orders = ordersForView(all, view);
  const waiting = waitingForYou(all);
  const notice = done ? doneMessages[done] : undefined;
  const here = `/partner/businesses/${businessId}/orders`;

  return (
    <>
      <AutoRefresh seconds={REFRESH_SECONDS} />
      <p className="mb-2 text-sm">
        <Link
          href={`/partner/businesses/${businessId}`}
          className="font-semibold text-brand hover:underline"
        >
          ← Business
        </Link>
      </p>
      <PageHeader
        title="Orders"
        description="Accept or reject new orders, then mark them being prepared and ready. OnePondy's drivers collect and deliver, so you never see the customer's address or phone. This page refreshes by itself."
      />

      {notice ? (
        <div className="mb-6">
          <Alert kind="success">{notice}</Alert>
        </div>
      ) : null}
      {waiting > 0 ? (
        <div className="mb-6">
          <Alert kind="warning">
            {waiting === 1 ? '1 order is' : `${waiting} orders are`} waiting for you to accept or
            reject.
          </Alert>
        </div>
      ) : null}

      <Card className="mb-6">
        <form className="flex flex-wrap items-end gap-3" role="search">
          <div className="flex flex-col gap-1">
            <label htmlFor="view" className="text-xs font-semibold">
              Show
            </label>
            <select
              id="view"
              name="view"
              defaultValue={view}
              className="rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm"
            >
              <option value="open">Open orders</option>
              <option value="all">All orders</option>
              {Object.entries(foodOrderStatusLabels).map(([value, label]) => (
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
            Show
          </button>
          <Link href={here} className="pb-2 text-sm font-semibold text-brand">
            Reset
          </Link>
        </form>
      </Card>

      {orders.length === 0 ? (
        <Card>
          <p className="text-sm text-muted">
            {view === 'open'
              ? 'No open orders right now. New ones appear here by themselves.'
              : 'No orders match this view.'}
          </p>
        </Card>
      ) : (
        <div className="flex flex-col gap-4">
          {orders.map((order) => {
            const deadline = deadlineFor(order);
            return (
              <Card key={order.id}>
                <div className="flex flex-wrap items-start justify-between gap-4">
                  <div>
                    <p className="font-semibold">
                      {order.itemCount} item{order.itemCount === 1 ? '' : 's'} ·{' '}
                      {formatMoney(order.totalAmount, order.currency)}
                    </p>
                    <p className="text-sm text-muted">
                      Delivering in {foodAreaLabels[order.deliveryArea] ?? order.deliveryArea}
                    </p>
                    <p className="text-xs text-muted">
                      Placed {formatDateTime(order.createdAt)}
                      {deadline ? ` · ${deadline.label} ${formatDateTime(deadline.at)}` : ''}
                    </p>
                  </div>
                  <span
                    className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${toneStyles[toneFor(order.status)]}`}
                  >
                    {foodOrderStatusLabels[order.status]}
                  </span>
                </div>

                <ul
                  className="mt-4 flex flex-col gap-1 border-t border-line pt-4 text-sm"
                  aria-label="Dishes"
                >
                  {order.lines.map((line) => (
                    <li key={line.menuItemId} className="flex justify-between gap-4">
                      <span>
                        <span className="font-semibold">{line.quantity} ×</span> {line.name}{' '}
                        <span className="text-xs text-muted">{line.isVeg ? 'Veg' : 'Non-veg'}</span>
                      </span>
                    </li>
                  ))}
                </ul>

                <OrderActions businessId={businessId} order={order} view={view} />
              </Card>
            );
          })}
        </div>
      )}
    </>
  );
}
