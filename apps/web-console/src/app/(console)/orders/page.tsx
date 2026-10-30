import type { Metadata } from 'next';
import Link from 'next/link';

import { Alert, Card, PageHeader, Pagination } from '@/components/Display';
import { api } from '@/lib/api';
import type { AdminOrderSummary, FoodOrderStatus, OrderAttention, Paged } from '@/lib/apiTypes';
import {
  attentionLabels,
  deliveryStatusLabels,
  foodAreaLabels,
  adminOrderStatusLabels,
  formatDateTime,
  formatMoney,
} from '@/lib/labels';
import { requireAdministrator } from '@/lib/session';

export const metadata: Metadata = { title: 'Orders' };

const attentionTone: Record<OrderAttention, string> = {
  noDriver: 'bg-danger-soft text-danger',
  pickupOverdue: 'bg-warn-soft text-warn',
  deliveryOverdue: 'bg-danger-soft text-danger',
  kitchenOverdue: 'bg-warn-soft text-warn',
  refundOverdue: 'bg-danger-soft text-danger',
};

interface Filters {
  status?: string;
  attention?: string;
  page?: string;
}

function query(filters: Filters, page?: number): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries({ ...filters, page: page?.toString() })) {
    if (value) {
      params.set(key, value);
    }
  }
  return params.toString();
}

export default async function OrdersPage({ searchParams }: { searchParams: Promise<Filters> }) {
  await requireAdministrator();
  const filters = await searchParams;
  const page = Math.max(1, Number(filters.page) || 1);
  const onlyAttention = filters.attention === '1';
  const params = new URLSearchParams({ page: String(page), pageSize: '25' });
  if (filters.status) {
    params.set('status', filters.status);
  }
  if (onlyAttention) {
    params.set('needsAttention', 'true');
  }

  const [result, attention] = await Promise.all([
    api<Paged<AdminOrderSummary>>(`/api/v1/admin/food-orders?${params.toString()}`),
    api<{ count: number }>('/api/v1/admin/food-orders/attention-count'),
  ]);

  return (
    <>
      <PageHeader
        title="Orders"
        description="Every food order across restaurants. Orders that look stuck are flagged so they can be cancelled or given to another driver."
      />

      {attention.count > 0 && !onlyAttention ? (
        <div className="mb-6">
          <Alert kind="warning">
            {attention.count} order{attention.count === 1 ? ' needs' : 's need'} attention.{' '}
            <Link href="/orders?attention=1" className="font-semibold underline">
              Show them
            </Link>
          </Alert>
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
              defaultValue={filters.status ?? ''}
              className="rounded-lg border border-line-strong bg-surface px-3 py-2 text-sm"
            >
              <option value="">Any</option>
              {(Object.entries(adminOrderStatusLabels) as [FoodOrderStatus, string][]).map(
                ([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ),
              )}
            </select>
          </div>
          <label className="flex items-center gap-2 pb-2 text-sm">
            <input type="checkbox" name="attention" value="1" defaultChecked={onlyAttention} />
            Needs attention only
          </label>
          <button
            type="submit"
            className="min-h-10 rounded-lg border border-line-strong px-4 text-sm font-semibold hover:bg-canvas"
          >
            Filter
          </button>
          <Link href="/orders" className="pb-2 text-sm font-semibold text-brand">
            Clear
          </Link>
        </form>
      </Card>

      <Card>
        {result.items.length === 0 ? (
          <p className="text-sm text-muted">
            {onlyAttention
              ? 'Nothing needs attention right now.'
              : 'No orders match these filters.'}
          </p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-line text-xs text-muted uppercase">
                <tr>
                  <th scope="col" className="py-2 pr-4">
                    Restaurant
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Status
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Delivery
                  </th>
                  <th scope="col" className="py-2 pr-4">
                    Total
                  </th>
                  <th scope="col" className="py-2">
                    Placed
                  </th>
                </tr>
              </thead>
              <tbody>
                {result.items.map((order) => (
                  <tr key={order.id} className="border-b border-line last:border-b-0">
                    <td className="py-3 pr-4">
                      <Link
                        href={`/orders/${order.id}`}
                        className="font-semibold text-brand hover:underline"
                      >
                        {order.restaurantName}
                      </Link>
                      <div className="text-xs text-muted">
                        {order.itemCount} item{order.itemCount === 1 ? '' : 's'} ·{' '}
                        {foodAreaLabels[order.deliveryArea] ?? order.deliveryArea}
                      </div>
                    </td>
                    <td className="py-3 pr-4">
                      <div>{adminOrderStatusLabels[order.status]}</div>
                      {order.attention ? (
                        <span
                          className={`mt-1 inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${attentionTone[order.attention]}`}
                        >
                          {attentionLabels[order.attention]}
                        </span>
                      ) : null}
                    </td>
                    <td className="py-3 pr-4">
                      {order.deliveryStatus ? (
                        <>
                          <div>{deliveryStatusLabels[order.deliveryStatus]}</div>
                          {order.driverFirstName ? (
                            <div className="text-xs text-muted">{order.driverFirstName}</div>
                          ) : null}
                        </>
                      ) : (
                        <span className="text-muted">—</span>
                      )}
                    </td>
                    <td className="py-3 pr-4 whitespace-nowrap">
                      {formatMoney(order.totalAmount, order.currency)}
                    </td>
                    <td className="py-3 whitespace-nowrap">{formatDateTime(order.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <Pagination
          page={result.page}
          pageSize={result.pageSize}
          totalCount={result.totalCount}
          href={(target) => `/orders?${query(filters, target)}`}
        />
      </Card>
    </>
  );
}
