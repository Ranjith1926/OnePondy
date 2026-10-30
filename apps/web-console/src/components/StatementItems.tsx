import type { SettlementDetail } from '@/lib/apiTypes';
import { formatDate, formatMoney } from '@/lib/labels';

/** The totals and one row per booking or food order of a statement, as the administrator and the partner both read it. */
export function StatementItems({ detail }: { detail: SettlementDetail }) {
  const { settlement, items } = detail;
  const totals: [string, number][] = [
    ['Gross sales', settlement.grossAmount],
    ['Commission', settlement.commissionAmount],
    ['Refunds to customers', settlement.refundAmount],
    ['Net payable', settlement.netAmount],
  ];

  return (
    <>
      <dl className="mb-6 grid gap-4 sm:grid-cols-4">
        {totals.map(([label, amount], index) => (
          <div key={label}>
            <dt className="text-xs text-muted uppercase">{label}</dt>
            <dd
              className={`text-lg ${index === totals.length - 1 ? 'font-bold' : 'font-semibold'}`}
            >
              {formatMoney(amount)}
            </dd>
          </div>
        ))}
      </dl>

      <div className="overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-line text-xs text-muted uppercase">
            <tr>
              <th scope="col" className="py-2 pr-4">
                Service date
              </th>
              <th scope="col" className="py-2 pr-4">
                Listing
              </th>
              <th scope="col" className="py-2 pr-4">
                Type
              </th>
              <th scope="col" className="py-2 pr-4 text-right">
                Amount
              </th>
              <th scope="col" className="py-2 pr-4 text-right">
                Commission
              </th>
              <th scope="col" className="py-2 text-right">
                To partner
              </th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr
                key={`${item.bookingId ?? item.foodOrderId}-${item.kind}`}
                className="border-b border-line last:border-b-0"
              >
                <td className="py-3 pr-4 whitespace-nowrap">{formatDate(item.serviceStartAt)}</td>
                <td className="py-3 pr-4">
                  {item.listingTitle}
                  <span className="block font-mono text-xs text-muted">
                    {item.bookingId ?? item.foodOrderId}
                  </span>
                </td>
                <td className="py-3 pr-4">{item.kind === 'sale' ? 'Sale' : 'Refund'}</td>
                <td className="py-3 pr-4 text-right whitespace-nowrap">
                  {formatMoney(item.grossAmount)}
                </td>
                <td className="py-3 pr-4 text-right whitespace-nowrap">
                  {formatMoney(item.commissionAmount)}
                </td>
                <td className="py-3 text-right font-semibold whitespace-nowrap">
                  {formatMoney(item.partnerAmount)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
