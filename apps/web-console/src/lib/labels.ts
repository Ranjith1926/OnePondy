import type {
  BookingStatus,
  BusinessApprovalStatus,
  BusinessDocumentKind,
  BusinessType,
  DayOfWeek,
  DriverApprovalStatus,
  RideVehicleClass,
  SafetyCategory,
  SafetyKind,
  SafetyStatus,
  FoodAreaCode,
  DeliveryJobStatus,
  FoodOrderStatus,
  ListingStatus,
  ListingType,
  MediaLicence,
  OrderActor,
  PaymentMethod,
  OrderAttention,
  ModuleStatus,
  PaymentAttemptStatus,
  ReconciliationKind,
  ReconciliationStatus,
  ReviewKind,
  ReviewStatus,
  SupportStatus,
  SupportTopic,
  RefundReason,
  RefundStatus,
  SettlementStatus,
  VehicleType,
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
  payments: 'Online payment',
  cashOnDelivery: 'Cash on delivery',
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
  'review.published': 'Review published',
  'review.rejected': 'Review rejected',
  'review.removed': 'Review removed',
  'review.replied': 'Partner replied to a review',
  'support_ticket.viewed': 'Support request opened',
  'support_ticket.contact_viewed': 'Contact details shown',
  'support_ticket.assigned': 'Support request taken',
  'support_ticket.released': 'Support request released',
  'support_ticket.resolved': 'Support request resolved',
  'support_ticket.escalated': 'Support request escalated',
  'driver.applied': 'Driver applied',
  'driver.updated': 'Driver details edited',
  'driver.approved': 'Driver approved',
  'driver.rejected': 'Driver rejected',
  'driver.suspended': 'Driver suspended',
  'driver.reinstated': 'Driver reinstated',
  'food_order.cancelled_by_admin': 'Order cancelled by an administrator',
  'food_order.delivery_released_by_admin': 'Delivery taken off a driver',
  'food_order.driver_location_viewed': 'Driver location viewed',
  'driver_cash.deposit_recorded': 'Cash deposit recorded',
  'settlement.drafted': 'Statement drafted',
  'settlement.approved': 'Statement approved',
  'settlement.paid': 'Statement paid',
  'settlement.discarded': 'Statement discarded',
  'restaurant.updated': 'Restaurant edited',
  'ride_driver.applied': 'Ride driver applied',
  'ride_driver.updated': 'Ride driver details edited',
  'ride_driver.approved': 'Ride driver approved',
  'ride_driver.rejected': 'Ride driver rejected',
  'ride_driver.suspended': 'Ride driver suspended',
  'ride_driver.reinstated': 'Ride driver reinstated',
  'ride_safety.sos_raised': 'Emergency button pressed',
  'ride_safety.incident_reported': 'Ride incident reported',
  'ride_safety.share_created': 'Trip shared',
  'ride_safety.viewed': 'Safety report opened',
  'ride_safety.acknowledged': 'Safety report taken',
  'ride_safety.resolved': 'Safety report resolved',
};

export const rideVehicleClassLabels: Record<RideVehicleClass, string> = {
  auto: 'Auto',
  cab: 'Cab',
  bikeTaxi: 'Bike taxi',
};

export const safetyCategoryLabels: Record<SafetyCategory, string> = {
  emergency: 'Emergency',
  unsafeDriving: 'Unsafe driving',
  harassment: 'Harassment',
  accident: 'Accident',
  vehicleProblem: 'Vehicle problem',
  overcharge: 'Overcharge',
  other: 'Other',
};

export const safetyStatusLabels: Record<SafetyStatus, string> = {
  open: 'Waiting',
  acknowledged: 'Being handled',
  resolved: 'Resolved',
};

export const safetyKindLabels: Record<SafetyKind, string> = {
  sos: 'Emergency button',
  incident: 'Incident report',
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

export const driverStatusLabels: Record<DriverApprovalStatus, string> = {
  pending: 'Pending review',
  approved: 'Approved',
  rejected: 'Rejected',
  suspended: 'Suspended',
};

export const vehicleTypeLabels: Record<VehicleType, string> = {
  bicycle: 'Bicycle',
  scooter: 'Scooter',
  motorbike: 'Motorbike',
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
  adminCancelled: 'Cancelled by OnePondy',
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
  readyForPickup: 'Ready for pickup',
  outForDelivery: 'Out for delivery',
  delivered: 'Delivered',
  rejected: 'Rejected',
  cancelled: 'Cancelled',
  expired: 'Expired',
  failed: 'Payment failed',
  refundPending: 'Refund in progress',
  refunded: 'Refunded',
};

/** The same statuses in an administrator's words: the restaurant's list says "Waiting for you", which means nothing to OnePondy staff. */
export const adminOrderStatusLabels: Record<FoodOrderStatus, string> = {
  ...foodOrderStatusLabels,
  placed: 'Waiting for the restaurant',
};

export const deliveryStatusLabels: Record<DeliveryJobStatus, string> = {
  open: 'Waiting for a driver',
  accepted: 'Driver on the way to the restaurant',
  pickedUp: 'Out for delivery',
  delivered: 'Delivered',
  cancelled: 'Cancelled',
};

export const paymentMethodLabels: Record<PaymentMethod, string> = {
  online: 'Paid online',
  cashOnDelivery: 'Cash on delivery',
};

export const attentionLabels: Record<OrderAttention, string> = {
  noDriver: 'No driver has taken it',
  pickupOverdue: 'Driver has not collected it',
  deliveryOverdue: 'Out for delivery too long',
  kitchenOverdue: 'Kitchen is running late',
  refundOverdue: 'Refund is taking too long',
};

export const orderActorLabels: Record<OrderActor, string> = {
  customer: 'Customer',
  partner: 'Restaurant',
  admin: 'OnePondy',
  system: 'System',
  driver: 'Driver',
};

// --- Reviews (Phase 10) ---

export const reviewStatusLabels: Record<ReviewStatus, string> = {
  pending: 'Waiting for a moderator',
  published: 'Published',
  rejected: 'Rejected',
  removed: 'Taken down',
};

export const reviewKindLabels: Record<ReviewKind, string> = {
  booking: 'Booking',
  food_order: 'Food order',
};

/** What the automatic check found, in words a moderator can act on. A code the console does not know is shown as it came. */
export const reviewFlagLabels: Record<string, string> = {
  phone_number: 'Contains a phone number',
  email: 'Contains an e-mail address',
  link: 'Contains a link',
  blocked_word: 'Contains a word on the blocked list',
};

// --- Support (Phase 10) ---

export const supportStatusLabels: Record<SupportStatus, string> = {
  open: 'Waiting for support',
  waitingForRequester: 'Waiting for the person',
  resolved: 'Resolved',
};

export const supportTopicLabels: Record<SupportTopic, string> = {
  booking: 'A booking',
  foodOrder: 'A food order',
  payment: 'A payment or refund',
  account: 'An account',
  business: 'A business',
  other: 'Something else',
};
