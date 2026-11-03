// Shapes returned by the OnePondy API (backend/src/OnePondy.Application). Enums arrive in camelCase.

export type VerificationStatus = 'draft' | 'pending' | 'verified' | 'rejected';

export type VisitorCategory =
  'standard' | 'indianAdult' | 'foreignAdult' | 'child' | 'student' | 'senior';

export type MediaLicence =
  'owned' | 'partnerSupplied' | 'ccBy' | 'ccBySa' | 'cc0' | 'licensedStock' | 'other';

export type ModuleStatus = 'available' | 'comingSoon' | 'hidden';

export type StaffRole = 'administrator' | 'content_editor' | 'support_agent';

export type DayOfWeek =
  'monday' | 'tuesday' | 'wednesday' | 'thursday' | 'friday' | 'saturday' | 'sunday';

export interface UserRef {
  id: string;
  name: string;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface Category {
  code: string;
  name: string;
}

export interface PlaceSummary {
  id: string;
  name: string;
  slug: string;
  categoryCode: string;
  status: VerificationStatus;
  isPublished: boolean;
  isReviewDue: boolean;
  updatedAt: string;
  lastReviewedAt: string | null;
}

export interface PlaceCounts {
  draft: number;
  pending: number;
  verified: number;
  rejected: number;
  published: number;
  reviewDue: number;
}

export interface OpeningPeriod {
  day: DayOfWeek;
  opens: string;
  closes: string;
}

export interface OpeningHours {
  alwaysOpen: boolean;
  periods: OpeningPeriod[];
  notes: string | null;
}

export interface EntryFee {
  category: VisitorCategory;
  amount: number;
  currency?: string;
}

export interface PlaceTranslation {
  language: 'ta' | 'fr';
  name: string;
  description: string | null;
  visitorGuidance: string | null;
}

export interface MediaItem {
  id: string;
  altText: string;
  caption: string | null;
  licence: MediaLicence;
  attribution: string | null;
  sourceUrl: string | null;
  sortOrder: number;
  width: number;
  height: number;
  urls: Record<'large' | 'medium' | 'thumb', string>;
}

export interface PlaceDetail {
  id: string;
  slug: string;
  version: number;
  name: string;
  categoryCode: string;
  description: string | null;
  visitorGuidance: string | null;
  address: string | null;
  latitude: number | null;
  longitude: number | null;
  openingHours: OpeningHours | null;
  entryFees: EntryFee[];
  translations: PlaceTranslation[];
  status: VerificationStatus;
  isPublished: boolean;
  isReadyForReview: boolean;
  isReviewDue: boolean;
  verifiedBy: UserRef | null;
  verifiedAt: string | null;
  verificationSource: string | null;
  lastReviewedAt: string | null;
  reviewNote: string | null;
  createdAt: string;
  createdBy: UserRef;
  updatedAt: string;
  updatedBy: UserRef;
  media: MediaItem[];
}

/** The body of POST /admin/places and the "place" part of PUT /admin/places/{id}. */
export interface PlaceInput {
  name: string;
  categoryCode: string;
  description: string | null;
  visitorGuidance: string | null;
  address: string | null;
  latitude: number | null;
  longitude: number | null;
  openingHours: { alwaysOpen: boolean; periods: OpeningPeriod[]; notes: string | null } | null;
  entryFees: { category: VisitorCategory; amount: number }[];
  translations: {
    language: 'ta' | 'fr';
    name: string;
    description: string | null;
    visitorGuidance: string | null;
  }[];
}

export interface ModuleFlag {
  key: string;
  status: ModuleStatus;
  updatedAt: string | null;
}

export interface AdminConfig {
  modules: ModuleFlag[];
  minSupportedAppVersion: string;
}

export interface StaffMember {
  userId: string;
  displayName: string;
  email: string | null;
  role: StaffRole;
}

export interface AuditEntry {
  id: string;
  occurredAt: string;
  actor: UserRef | null;
  action: string;
  entityType: string;
  entityId: string;
  details: string | null;
}

export interface SessionTokens {
  accessToken: string;
  accessTokenExpiresAt: string;
  refreshToken: string;
  refreshTokenExpiresAt: string;
  user: { id: string; displayName: string; roles: string[] };
}

export interface CodeRequested {
  challengeId: string;
  expiresAt: string;
  resendAfterSeconds: number;
  maskedDestination: string;
}

export type OtpChannel = 'sms' | 'email';

export interface CustomerProfile {
  id: string;
  displayName: string;
  phoneNumber: string | null;
  email: string | null;
  preferredLanguage: string;
  createdAt: string;
}

export type BusinessType =
  'restaurant' | 'hotel' | 'activity' | 'shop' | 'tourOperator' | 'service';

export type BusinessApprovalStatus = 'pending' | 'approved' | 'rejected' | 'suspended';

export type BusinessDocumentKind = 'gstin' | 'fssaiLicence' | 'other';

export interface BusinessDocument {
  id: string;
  kind: BusinessDocumentKind;
  contentType: string;
  uploadedAt: string;
}

export interface BusinessSummary {
  id: string;
  name: string;
  businessType: BusinessType;
  approvalStatus: BusinessApprovalStatus;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface BusinessDetail {
  id: string;
  version: number;
  ownerUserId: string;
  name: string;
  businessType: BusinessType;
  description: string | null;
  phoneNumber: string | null;
  email: string | null;
  address: string | null;
  latitude: number | null;
  longitude: number | null;
  gstin: string | null;
  fssaiLicenseNumber: string | null;
  approvalStatus: BusinessApprovalStatus;
  isActive: boolean;
  reviewNote: string | null;
  documents: BusinessDocument[];
  createdAt: string;
  updatedAt: string;
}

/** The body of POST /partner/businesses and the "business" part of PUT /partner/businesses/{id}. */
export interface BusinessApplicationInput {
  name: string;
  businessType: BusinessType;
  description: string | null;
  phoneNumber: string | null;
  email: string | null;
  address: string | null;
  lat: number | null;
  lng: number | null;
  gstin: string | null;
  fssaiLicenseNumber: string | null;
}

export type ListingType = 'activity' | 'stay' | 'event' | 'service' | 'shop';

export type ListingStatus = 'draft' | 'pending' | 'published' | 'suspended';

export interface CancellationPolicy {
  freeCancellationHours: number;
  refundPercentage: number;
  notes: string | null;
}

export interface ListingSummary {
  id: string;
  businessId: string;
  listingType: ListingType;
  title: string;
  isBookable: boolean;
  basePrice: number | null;
  currency: string;
  status: ListingStatus;
  createdAt: string;
  updatedAt: string;
}

export interface ListingDetail {
  id: string;
  version: number;
  businessId: string;
  listingType: ListingType;
  title: string;
  description: string | null;
  isBookable: boolean;
  basePrice: number | null;
  currency: string;
  cancellationPolicy: CancellationPolicy | null;
  status: ListingStatus;
  reviewNote: string | null;
  createdAt: string;
  updatedAt: string;
}

/** The body of POST .../listings and the "listing" part of PUT .../listings/{id}. */
export interface ListingApplicationInput {
  listingType: ListingType;
  title: string;
  description: string | null;
  isBookable: boolean;
  basePrice: number | null;
  freeCancellationHours: number | null;
  refundPercentage: number | null;
  cancellationNotes: string | null;
}

export interface AvailabilitySlot {
  id: string;
  listingId: string;
  version: number;
  startAt: string;
  endAt: string;
  capacity: number;
  reservedCount: number;
  availableCount: number;
}

export type BookingStatus =
  | 'pending'
  | 'awaitingPayment'
  | 'confirmed'
  | 'failed'
  | 'expired'
  | 'rejected'
  | 'cancelled'
  | 'refundPending'
  | 'refunded'
  | 'completed';

export type BookingActorType = 'customer' | 'partner' | 'admin' | 'system';

export interface PriceSnapshot {
  unitPrice: number;
  quantity: number;
  subtotal: number;
  fees: number;
  taxAmount: number;
  discountAmount: number;
  totalAmount: number;
  currency: string;
}

export interface BookingSummary {
  id: string;
  listingId: string;
  listingTitle: string;
  businessId: string;
  businessName: string;
  bookingType: ListingType;
  status: BookingStatus;
  expiresAt: string | null;
  startAt: string | null;
  endAt: string | null;
  guestCount: number;
  totalAmount: number;
  currency: string;
  createdAt: string;
  /** What an accept, reject, cancel or complete must send back, so a stale page cannot act on a booking that has changed. */
  version: number;
}

export interface BookingSlotView {
  slotId: string;
  startAt: string;
  endAt: string;
  quantity: number;
}

export interface BookingHistoryView {
  previousStatus: BookingStatus | null;
  newStatus: BookingStatus;
  actorType: BookingActorType;
  actorUserId: string | null;
  reason: string | null;
  createdAt: string;
}

export interface BookingDetail {
  id: string;
  version: number;
  listingId: string;
  listingTitle: string;
  businessId: string;
  businessName: string;
  bookingType: ListingType;
  status: BookingStatus;
  expiresAt: string | null;
  startAt: string | null;
  endAt: string | null;
  guestCount: number;
  priceSnapshot: PriceSnapshot;
  cancellationPolicySnapshot: CancellationPolicy;
  cancellationReason: string | null;
  slots: BookingSlotView[];
  history: BookingHistoryView[];
  createdAt: string;
  updatedAt: string;
}

// --- Payments, refunds, statements and reconciliation (spec Phase 7). Enums arrive in camelCase. ---

export type PaymentAttemptStatus = 'created' | 'captured' | 'failed' | 'expired';

export type RefundStatus = 'pending' | 'processed' | 'failed';

export type RefundReason =
  'rejected' | 'partnerCancelled' | 'customerCancelled' | 'latePayment' | 'adminCancelled';

export type SettlementStatus = 'draft' | 'approved' | 'paid';

export type SettlementItemKind = 'sale' | 'refund';

/** A payment, refund or statement line is for a booking or (step 8.3) a food order: exactly one of the two ids is set. */
export interface PaymentRow {
  id: string;
  bookingId: string | null;
  foodOrderId: string | null;
  /** The listing, or "Food order". */
  listingTitle: string;
  businessId: string;
  businessName: string;
  amount: number;
  currency: string;
  status: PaymentAttemptStatus;
  providerOrderId: string;
  providerPaymentId: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface RefundRow {
  id: string;
  bookingId: string | null;
  foodOrderId: string | null;
  listingTitle: string;
  businessName: string;
  amount: number;
  reason: RefundReason;
  status: RefundStatus;
  providerRefundId: string | null;
  requestedAt: string;
  processedAt: string | null;
}

export interface SettlementSummary {
  id: string;
  businessId: string;
  businessName: string;
  periodStart: string;
  periodEnd: string;
  grossAmount: number;
  commissionAmount: number;
  refundAmount: number;
  netAmount: number;
  status: SettlementStatus;
  payoutReference: string | null;
  createdAt: string;
  approvedAt: string | null;
  paidAt: string | null;
  version: number;
}

export interface SettlementItem {
  bookingId: string | null;
  foodOrderId: string | null;
  listingTitle: string;
  serviceStartAt: string | null;
  kind: SettlementItemKind;
  grossAmount: number;
  commissionAmount: number;
  partnerAmount: number;
}

export interface SettlementDetail {
  settlement: SettlementSummary;
  items: SettlementItem[];
}

export type ReconciliationStatus = 'open' | 'resolved' | 'autoResolved';

export type ReconciliationKind =
  | 'webhookNeedsAttention'
  | 'webhookUnprocessed'
  | 'paymentAppliedWithoutWebhook'
  | 'gatewayPaymentUnknown'
  | 'paymentMissingAtGateway'
  | 'paymentMismatch'
  | 'refundAppliedWithoutWebhook'
  | 'refundNotSent'
  | 'refundStuck'
  | 'refundFailed'
  | 'refundTotalMismatch'
  | 'ledgerProblem';

export interface ReconciliationExceptionView {
  id: string;
  kind: ReconciliationKind;
  status: ReconciliationStatus;
  bookingId: string | null;
  foodOrderId: string | null;
  providerReference: string | null;
  expectedAmount: number | null;
  actualAmount: number | null;
  detail: string;
  firstSeenAt: string;
  lastSeenAt: string;
  occurrences: number;
  resolvedAt: string | null;
  resolutionNote: string | null;
}

// --- Restaurants and menus (Phase 8) ---

export type FoodAreaCode =
  | 'puducherry'
  | 'whiteTown'
  | 'tamilQuarter'
  | 'auroville'
  | 'lawspet'
  | 'muthialpet'
  | 'reddiarpalayam'
  | 'ariyankuppam'
  | 'villianur';

export type PaymentMethod = 'online' | 'cashOnDelivery';

export interface RestaurantProfile {
  configured: boolean;
  businessId: string;
  prepTimeMinutes: number;
  minOrderAmount: number;
  isAcceptingOrders: boolean;
  deliveryAreas: FoodAreaCode[];
  /** Whether the restaurant takes cash on delivery too. Customers see it only once OnePondy has switched cash on delivery on. */
  acceptsCashOnDelivery: boolean;
  version: number;
}

export interface MenuItem {
  id: string;
  categoryId: string;
  name: string;
  description: string | null;
  price: number;
  currency: string;
  isVeg: boolean;
  taxRatePercent: number;
  isAvailable: boolean;
  isArchived: boolean;
  position: number;
  version: number;
}

export interface MenuCategory {
  id: string;
  name: string;
  position: number;
  version: number;
  items: MenuItem[];
}

export interface MenuView {
  categories: MenuCategory[];
}

// --- Food orders (Phase 8, the restaurant's inbox) ---

export type FoodOrderStatus =
  | 'placed'
  | 'awaitingPayment'
  | 'confirmed'
  | 'preparing'
  | 'readyForPickup'
  | 'outForDelivery'
  | 'delivered'
  | 'rejected'
  | 'cancelled'
  | 'expired'
  | 'failed'
  | 'refundPending'
  | 'refunded';

export interface FoodOrderLine {
  menuItemId: string;
  name: string;
  isVeg: boolean;
  unitPrice: number;
  quantity: number;
  taxRatePercent: number;
  subtotal: number;
  taxAmount: number;
}

export type DeliveryJobStatus = 'open' | 'accepted' | 'pickedUp' | 'delivered' | 'cancelled';

/** Who is collecting an order and how far along it is. It carries no position: where a driver is stays with OnePondy. */
export interface DeliveryProgress {
  status: DeliveryJobStatus;
  driverFirstName: string | null;
  vehicleType: VehicleType | null;
  locationUpdatedAt: string | null;
}

/** One order in the restaurant's inbox. It never carries the customer's address or phone: OnePondy's own drivers deliver. */
export interface PartnerOrderSummary {
  id: string;
  /** What every action must send back, so a stale page cannot act on an order that has changed. */
  version: number;
  status: FoodOrderStatus;
  expiresAt: string | null;
  estimatedReadyAt: string | null;
  itemCount: number;
  lines: FoodOrderLine[];
  totalAmount: number;
  currency: string;
  deliveryArea: FoodAreaCode;
  createdAt: string;
  /** Null until the order is ready for a driver. */
  delivery: DeliveryProgress | null;
  paymentMethod: PaymentMethod;
}

export type DriverApprovalStatus = 'pending' | 'approved' | 'rejected' | 'suspended';

export type VehicleType = 'bicycle' | 'scooter' | 'motorbike';

export interface DriverSummary {
  userId: string;
  fullName: string;
  vehicleType: VehicleType;
  approvalStatus: DriverApprovalStatus;
  isOnDuty: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface DriverDetail {
  userId: string;
  version: number;
  fullName: string;
  vehicleType: VehicleType;
  vehicleNumber: string | null;
  licenceNumber: string | null;
  approvalStatus: DriverApprovalStatus;
  reviewNote: string | null;
  isOnDuty: boolean;
  onDutySince: string | null;
  phoneNumber: string | null;
  email: string | null;
  createdAt: string;
  updatedAt: string;
}

// --- Administrators' view of food orders and deliveries (Phase 8, step 8.9) ---

/** Why an order needs an administrator's eye: stuck for longer than expected. */
export type OrderAttention =
  'noDriver' | 'pickupOverdue' | 'deliveryOverdue' | 'kitchenOverdue' | 'refundOverdue';

export interface AdminOrderSummary {
  id: string;
  version: number;
  status: FoodOrderStatus;
  restaurantName: string;
  deliveryArea: FoodAreaCode;
  itemCount: number;
  totalAmount: number;
  currency: string;
  createdAt: string;
  updatedAt: string;
  deliveryStatus: DeliveryJobStatus | null;
  driverFirstName: string | null;
  attention: OrderAttention | null;
  paymentMethod: PaymentMethod;
}

export interface AdminDriverPosition {
  latitude: number;
  longitude: number;
  at: string;
}

export interface AdminDeliveryView {
  jobId: string;
  /** What "offer to other drivers" must send back, so a stale page cannot act on a delivery that has moved on. */
  jobVersion: number;
  status: DeliveryJobStatus;
  driverUserId: string | null;
  driverName: string | null;
  driverPhone: string | null;
  vehicleType: VehicleType | null;
  openedAt: string;
  acceptedAt: string | null;
  pickedUpAt: string | null;
  deliveredAt: string | null;
  cancelledAt: string | null;
  cancelReason: string | null;
  /** Only while the delivery is under way, and only once the driver has reported one. */
  position: AdminDriverPosition | null;
  canRelease: boolean;
}

export type OrderActor = 'customer' | 'partner' | 'admin' | 'system' | 'driver';

export interface AdminFoodOrder {
  id: string;
  /** What "cancel" must send back. */
  version: number;
  businessId: string;
  restaurantName: string;
  fssaiLicenseNumber: string | null;
  status: FoodOrderStatus;
  deliveryArea: FoodAreaCode;
  deliveryAddress: string;
  deliveryNote: string | null;
  contactPhone: string;
  lines: FoodOrderLine[];
  price: {
    subtotal: number;
    deliveryFee: number;
    packagingFee: number;
    taxAmount: number;
    discountAmount: number;
    totalAmount: number;
    currency: string;
  };
  closedReason: string | null;
  history: {
    previousStatus: FoodOrderStatus | null;
    status: FoodOrderStatus;
    actorType: OrderActor;
    reason: string | null;
    at: string;
  }[];
  createdAt: string;
  updatedAt: string;
  payment: { amount: number; currency: string; paidAt: string; reference: string } | null;
  paymentMethod: PaymentMethod;
  /** For cash on delivery: the cash the driver took, once delivered. */
  cashCollected: number | null;
  refunds: {
    amount: number;
    reason: RefundReason;
    status: RefundStatus;
    requestedAt: string;
    processedAt: string | null;
  }[];
}

export interface AdminOrderDetail {
  order: AdminFoodOrder;
  delivery: AdminDeliveryView | null;
  attention: OrderAttention | null;
  /** False once the order has arrived or closed. */
  canCancel: boolean;
}

// --- Cash on delivery's cash (Phase 8, step 8.8) ---

/** One driver's cash: what they hold for OnePondy, and when they last took some and last handed some in. */
export interface DriverCashRow {
  driverUserId: string;
  driverName: string | null;
  phoneNumber: string | null;
  held: number;
  lastCollectedAt: string | null;
  lastDepositAt: string | null;
}

// --- Reviews (Phase 10, step 10.1) ---

export type ReviewStatus = 'pending' | 'published' | 'rejected' | 'removed';

/** What a review is about: a booking of a listing, or a delivered food order. */
export type ReviewKind = 'booking' | 'food_order';

export interface ReviewReply {
  text: string;
  repliedAt: string;
}

/** A review in the moderators' queue. It carries the author's first name and what the automatic check found, not their account. */
export interface AdminReview {
  id: string;
  kind: ReviewKind;
  businessId: string;
  businessName: string;
  listingId: string | null;
  listingTitle: string | null;
  rating: number;
  text: string | null;
  authorName: string;
  status: ReviewStatus;
  /** Codes from the automatic check: `phone_number`, `email`, `link`, `blocked_word`. */
  flags: string[];
  moderationReason: string | null;
  createdAt: string;
  editedAt: string | null;
  reply: ReviewReply | null;
  version: number;
}

/** A published review as the business sees it. */
export interface PartnerReview {
  id: string;
  kind: ReviewKind;
  listingId: string | null;
  listingTitle: string | null;
  rating: number;
  text: string | null;
  authorName: string;
  publishedAt: string;
  reply: ReviewReply | null;
  version: number;
}
