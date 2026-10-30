import type {
  BookingStatus,
  BusinessApprovalStatus,
  BusinessDocumentKind,
  BusinessType,
  DayOfWeek,
  FoodAreaCode,
  FoodOrderStatus,
  ListingStatus,
  ListingType,
  MediaLicence,
  ModuleStatus,
  PaymentAttemptStatus,
  ReconciliationKind,
  ReconciliationStatus,
  RefundReason,
  RefundStatus,
  SettlementStatus,
  VerificationStatus,
  VisitorCategory,
} from './apiTypes';

export const statusLabels: Record<VerificationStatus, string> = {
  draft: 'Draft',
  pending: 'Pending review',
  verified: 'Verified',
  rejected: 'Rejected',
};

export const visitorCategoryLabels: Record<VisitorCategory, string> = {
  standard: 'Standard',
  indianAdult: 'Indian adult',
  foreignAdult: 'Foreign adult',
  child: 'Child',
  student: 'Student',
  senior: 'Senior citizen',
};

export const visitorCategories = Object.keys(visitorCategoryLabels) as VisitorCategory[];

export const licenceLabels: Record<MediaLicence, string> = {
  owned: 'Our own photo',
  partnerSupplied: 'Supplied by a partner',
  ccBy: 'Creative Commons BY',
  ccBySa: 'Creative Commons BY-SA',
  cc0: 'Public domain (CC0)',
  licensedStock: 'Licensed stock photo',
  other: 'Other licence',
};

/** Mirrors the API rule: credit is needed unless we own the photo or it is public domain. */
export const licenceNeedsCredit = (licence: MediaLicence): boolean =>
  licence !== 'owned' && licence !== 'cc0';

export const moduleStatusLabels: Record<ModuleStatus, string> = {
  available: 'Available',
  comingSoon: 'Coming soon',
  hidden: 'Hidden',
};

export const moduleLabels: Record<string, string> = {
  food: 'Food',
  rides: 'Rides',
  stays: 'Stays',
  activities: 'Activities',
  tripPlanner: 'Trip planner',
  notifications: 'Notifications',
  support: 'Help and support',
};

export const days: DayOfWeek[] = [
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
  'sunday',
];

export const dayLabels: Record<DayOfWeek, string> = {
  monday: 'Monday',
  tuesday: 'Tuesday',
  wednesday: 'Wednesday',
  thursday: 'Thursday',
  friday: 'Friday',
  saturday: 'Saturday',
  sunday: 'Sunday',
};

export const auditActionLabels: Record<string, string> = {
  'place.created': 'Created',
  'place.updated': 'Edited',
  'place.submitted': 'Submitted for review',
  'place.verified': 'Verified',
  'place.rejected': 'Rejected',
  'place.published': 'Published',
  'place.unpublished': 'Unpublished',
  'place.review_confirmed': 'Confirmed still accurate',
  'media.added': 'Photo added',
  'media.updated': 'Photo details changed',
  'media.removed': 'Photo removed',
  'config.module_flag_changed': 'Module setting changed',
  'config.min_app_version_changed': 'Minimum app version changed',
  'staff.role_granted': 'Staff role granted',
  'staff.role_revoked': 'Staff access removed',
  'business.applied': 'Applied as a partner',
  'business.updated': 'Application edited',
  'business.approved': 'Approved',
  'business.rejected': 'Rejected',
  'business.suspended': 'Suspended',
  'business.reinstated': 'Reinstated',
  'business.document_added': 'Document added',
  'business.document_removed': 'Document removed',
  'listing.created': 'Listing created',
  'listing.updated': 'Listing edited',
  'listing.submitted': 'Listing submitted for review',
  'listing.published': 'Listing published',
  'listing.sent_back': 'Listing sent back for changes',
  'listing.suspended': 'Listing suspended',
  'listing.reinstated': 'Listing reinstated',
};

export const listingTypeLabels: Record<ListingType, string> = {
  activity: 'Activity',
  stay: 'Stay',
  event: 'Event',
  service: 'Service',
  shop: 'Shop',
};

export const listingStatusLabels: Record<ListingStatus, string> = {
  draft: 'Draft',
  pending: 'Pending review',
  published: 'Published',
  suspended: 'Suspended',
};

export const bookingStatusLabels: Record<BookingStatus, string> = {
  pending: 'Awaiting partner response',
  awaitingPayment: 'Awaiting payment',
  confirmed: 'Confirmed',
  failed: 'Payment failed',
  expired: 'Expired',
  rejected: 'Rejected',
  cancelled: 'Cancelled',
  refundPending: 'Refund in progress',
  refunded: 'Refunded',
  completed: 'Completed',
};

export const businessTypeLabels: Record<BusinessType, string> = {
  restaurant: 'Restaurant',
  hotel: 'Hotel / stay',
  activity: 'Activity',
  shop: 'Shop',
  tourOperator: 'Tour operator',
  service: 'Service',
};

export const businessStatusLabels: Record<BusinessApprovalStatus, string> = {
  pending: 'Pending review',
  approved: 'Approved',
  rejected: 'Rejected',
  suspended: 'Suspended',
};

export const businessDocumentLabels: Record<BusinessDocumentKind, string> = {
  gstin: 'GSTIN certificate',
  fssaiLicence: 'FSSAI licence',
  other: 'Other document',
};

const dateTime = new Intl.DateTimeFormat('en-IN', {
  dateStyle: 'medium',
  timeStyle: 'short',
  timeZone: 'Asia/Kolkata',
});

const date = new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeZone: 'Asia/Kolkata' });

/** Times are stored in UTC and shown in India time (spec §9). */
export const formatDateTime = (iso: string | null): string =>
  iso ? dateTime.format(new Date(iso)) : '—';

export const formatDate = (iso: string | null): string => (iso ? date.format(new Date(iso)) : '—');

// --- Payments (Phase 7) ---

export const paymentStatusLabels: Record<PaymentAttemptStatus, string> = {
  created: 'Started',
  captured: 'Paid',
  failed: 'Failed',
  expired: 'Expired',
};

export const refundStatusLabels: Record<RefundStatus, string> = {
  pending: 'In progress',
  processed: 'Sent',
  failed: 'Failed',
};

export const refundReasonLabels: Record<RefundReason, string> = {
  rejected: 'Partner rejected',
  partnerCancelled: 'Partner cancelled',
  customerCancelled: 'Customer cancelled',
  latePayment: 'Paid after time ran out',
};

export const settlementStatusLabels: Record<SettlementStatus, string> = {
  draft: 'Draft',
  approved: 'Approved',
  paid: 'Paid',
};

export const reconciliationKindLabels: Record<ReconciliationKind, string> = {
  webhookNeedsAttention: 'Webhook could not be applied',
  webhookUnprocessed: 'Webhook never finished',
  paymentAppliedWithoutWebhook: 'Payment found without a webhook',
  gatewayPaymentUnknown: 'Gateway payment we do not know',
  paymentMissingAtGateway: 'Payment missing at the gateway',
  paymentMismatch: 'Payment differs from the gateway',
  refundAppliedWithoutWebhook: 'Refund found without a webhook',
  refundNotSent: 'Refund never reached the gateway',
  refundStuck: 'Refund waiting too long',
  refundFailed: 'Refund failed',
  refundTotalMismatch: 'Refund totals differ',
  ledgerProblem: 'Ledger problem',
};

export const reconciliationStatusLabels: Record<ReconciliationStatus, string> = {
  open: 'Needs attention',
  resolved: 'Resolved',
  autoResolved: 'Fixed automatically',
};

const rupees = new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR' });

/** Money in rupees, for example "₹1,250.00". Everything in the console is INR for now. */
export const formatMoney = (amount: number, currency = 'INR'): string =>
  currency === 'INR' ? rupees.format(amount) : `${currency} ${amount.toFixed(2)}`;

// --- Food (Phase 8): the same areas the customer app's picker offers ---

export const foodAreaLabels: Record<FoodAreaCode, string> = {
  puducherry: 'Whole city (every area)',
  whiteTown: 'White Town',
  tamilQuarter: 'Tamil Quarter',
  auroville: 'Auroville',
  lawspet: 'Lawspet',
  muthialpet: 'Muthialpet',
  reddiarpalayam: 'Reddiarpalayam',
  ariyankuppam: 'Ariyankuppam',
  villianur: 'Villianur',
};

export const foodOrderStatusLabels: Record<FoodOrderStatus, string> = {
  placed: 'Waiting for you',
  awaitingPayment: 'Waiting for the customer to pay',
  confirmed: 'Confirmed',
  preparing: 'Being prepared',
  readyForPickup: 'Ready, waiting for a driver',
  outForDelivery: 'Out for delivery',
  delivered: 'Delivered',
  rejected: 'Rejected',
  cancelled: 'Cancelled',
  expired: 'Expired',
  failed: 'Payment failed',
  refundPending: 'Refund in progress',
  refunded: 'Refunded',
};
