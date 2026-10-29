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

export type RefundReason = 'rejected' | 'partnerCancelled' | 'customerCancelled' | 'latePayment';

export type SettlementStatus = 'draft' | 'approved' | 'paid';

export type SettlementItemKind = 'sale' | 'refund';

export interface PaymentRow {
  id: string;
  bookingId: string;
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
  bookingId: string;
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
  bookingId: string;
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

export interface RestaurantProfile {
  configured: boolean;
  businessId: string;
  prepTimeMinutes: number;
  minOrderAmount: number;
  isAcceptingOrders: boolean;
  deliveryAreas: FoodAreaCode[];
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
