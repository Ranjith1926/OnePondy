import type { SettlementStatus } from '@/lib/apiTypes';
import { settlementStatusLabels } from '@/lib/labels';

const tone: Record<SettlementStatus, string> = {
  draft: 'border border-line-strong bg-canvas text-muted',
  approved: 'bg-info-soft text-info',
  paid: 'bg-ok-soft text-ok',
};

export function SettlementBadge({ status }: { status: SettlementStatus }) {
  return (
    <span
      className={`inline-flex rounded-full px-2.5 py-0.5 text-xs font-semibold whitespace-nowrap ${tone[status]}`}
    >
      {settlementStatusLabels[status]}
    </span>
  );
}
