# OnePondy — Functional Specification & Phased Development Plan

| | |
|---|---|
| **Document type** | Product requirements, UI/UX, architecture, state-management and implementation guide |
| **Project** | OnePondy |
| **Platform** | React Native mobile app first; ASP.NET Core API and PostgreSQL backend; Next.js web console for admins and partners; Windows/IIS deployment; no Docker |
| **Version** | 1.1 (draft) |
| **Last updated** | 2026-10-26 |
| **Owner** | TBD (D16) |
| **Status** | Initial implementation specification; open decisions are listed in §22 |
| **Product vision** | One app for Puducherry — tourism, food, rides, stays, activities, local shopping and services. |

### Change log

| Version | Date | Summary |
|---|---|---|
| 1.0 | — | Initial specification. |
| 1.1 | 2026-10-26 | Added platform surfaces (web console, driver app), release plan, guest access, booking state machine, account deletion, AI planner and reviews workflows. Reordered phases so the admin console, payments, notifications and rate limiting come before the features that need them. Corrected the reference schema (listings, availability slots, snapshots, idempotency, append-only history, translations, deferrable stop order). Fixed EF Core tooling commands. Added non-functional requirements (§20), India compliance checklist (§21) and open decisions register (§22). Repaired list formatting in §10. |

------------------------------------------------------------------------

## 1. Product Vision

OnePondy is an all-in-one local super app for residents and visitors to
Puducherry. Customers can discover places, plan trips, browse
restaurants, order food, request eligible transport, book stays and
activities, explore local businesses, manage payments and contact
support through one account.

The product must feel polished, modern, fast, accessible and
trustworthy. It must not feel like a collection of unrelated mini-apps.
Shared navigation, design tokens, booking history, notifications and
account management should provide a consistent experience across all
services.

### Primary user groups

1.  **Customer / Tourist** — discovers and books services.
2.  **Resident** — uses food, transport, shopping and local services.
3.  **Business Partner** — manages restaurant, hotel, shop or activity
    listings, availability and incoming bookings and orders through the
    partner area of the web console (Phase 6).
4.  **Driver / Delivery Partner** — accepts eligible assignments
    through a dedicated driver app (Phase 8).
5.  **Administrator / Content Editor / Support Agent** — manages place
    content and verification, partner onboarding, service quality,
    disputes and platform operations through the admin area of the web
    console (Phase 3).

Each surface is described under Platform surfaces in §2.

### Product principles

-   Mobile-first and location-aware, with explicit location permission
    and a manual location fallback.
-   One customer identity and one consistent booking history.
-   Clear prices, fees, cancellation terms and booking status.
-   No fake inventory, fabricated reviews, simulated payment success or
    unverified claims.
-   Every asynchronous action has loading, success, empty, offline and
    error states.
-   Build a modular monolith backend first; do not start with
    microservices.
-   Build and test locally on Windows without Docker, then deploy to IIS
    and a Windows Service where appropriate.
-   Release features incrementally; do not pretend all services are
    operational before partners and integrations are ready.

------------------------------------------------------------------------

## 2. Product Scope

### Customer mobile app — planned capabilities

1.  Onboarding, language selection, permission education and guest
    browsing of public content.
2.  Sign up, login, logout, session expiry, account profile and in-app
    account deletion.
3.  Home dashboard with location, search, service shortcuts and curated
    content.
4.  Tourism discovery: attractions, beaches, heritage, museums, cultural
    locations and local guides.
5.  Search and filters by category, distance, opening hours and other
    verified attributes.
6.  Map and list views, place details, directions and saved places.
7.  AI-assisted itinerary planning based only on available and verified
    place information; user must review the plan.
8.  Restaurant discovery, menu, cart, checkout, order status and order
    history.
9.  Ride request and trip status for transport services that are legally
    approved and operational.
10. Hotel and accommodation discovery and booking requests/reservations.
11. Activities and events with availability, time slots, booking and
    cancellation terms.
12. Local shopping and service listings (discovery only; see D13).
13. Payments, receipts, refunds status and transaction history.
14. Notifications, support tickets, FAQs and issue reporting.
15. Favorites, moderated reviews after eligible completed transactions,
    and account settings.

### Platform surfaces

| Surface | Users | Technology | First phase |
|---|---|---|---|
| Customer mobile app | Customers, tourists, residents | React Native + Expo (Android first; iOS per D4) | Phase 0 |
| Web console, admin area | Administrators, content editors, support agents | Next.js (TypeScript) behind IIS, using the same API | Phase 3 |
| Web console, partner area | Business partners | Same Next.js app, partner role with separate navigation | Phase 6 |
| Driver app | Drivers and delivery partners | React Native + Expo, separate app in the monorepo | Phase 8 |
| API and worker | All surfaces | ASP.NET Core API + .NET Worker Service | Phase 0 |

A customer feature cannot launch before the tool that supplies it: place
content needs the admin area, bookings need the partner area, and
delivery and rides need the driver app.

### Release plan

| Release | Phases | Customer-facing scope |
|---|---|---|
| Release 1 (public launch) | 0–4 | Tourism discovery, map and list, place details, saved places, manual itineraries, guest browsing and accounts. Food, rides and stays are shown as "Coming soon" or hidden. |
| Release 2 | 5–7 | Paid bookings for activities, events and stays; shop and service listings; push notifications; AI trip planner (may follow later without blocking the release). |
| Release 3 | 8 | Food ordering and delivery in approved service areas. |
| Release 4 | 9 | Rides, only after legal and operational approval. |
| Continuous | 10 | Reviews, support and trust hardening, from Release 2 onwards. |

Every release passes the release readiness gate at the end of §10.

### Not included in Release 1 (first MVP)

-   Real-money settlement to partners before legal, provider and payment
    arrangements are approved.
-   Live ride or food dispatch before qualified providers are onboarded
    and operational processes are tested.
-   Guaranteed hotel inventory without a reliable inventory/confirmation
    source.
-   Background location tracking when a driver is off duty or has no
    active authorized job.
-   AI-generated opening hours, prices, availability or booking
    confirmations without verification.
-   Ordering and delivery from shops (D13).
-   Microservices, Kubernetes or Docker.
-   Storing raw card details, payment secrets or API credentials in the
    mobile app.

------------------------------------------------------------------------

## 3. Technology Stack

### Mobile

-   React Native with TypeScript.
-   Expo with development builds for native integrations. Create the app
    from the `blank-typescript` template and do not install Expo Router:
    navigation uses React Navigation, and Expo Router would treat
    `src/app/` as a routes directory.
-   EAS Build for iOS builds, because iOS cannot be built or simulated
    on Windows (D4). Android builds run locally with Android Studio.
-   EAS Update for over-the-air JavaScript fixes only; changes to native
    code or store-reviewed behaviour go through the stores.
-   React Navigation.
-   React Context + `useReducer` for shared, relatively low-frequency
    application state.
-   TanStack Query for server state, API caching, retries and
    invalidation.
-   React Hook Form + Zod for forms and validation.
-   SecureStore for tokens and other sensitive session material;
    AsyncStorage for non-sensitive preferences.
-   SQLite only for carefully selected offline content if needed.
-   i18next + react-i18next with expo-localization; every user-facing
    string is externalized from Phase 1.
-   NetInfo connected to TanStack Query's online manager for offline
    detection.
-   A consistent icon library and reusable OnePondy design system.
-   Map provider per D6 (Google Maps Platform or an alternative),
    subject to API key restrictions, cost and data-storage terms.
-   Push notifications per D7 (Expo push service, or Firebase Cloud
    Messaging and APNs directly).
-   SignalR client (`@microsoft/signalr`) for authorized live order and
    trip updates while the app is in the foreground; push notifications
    cover the app while it is in the background.
-   Crash reporting and product analytics per D9, with personal data
    scrubbed before sending.
-   Jest + React Native Testing Library for unit and component tests;
    Maestro for end-to-end flows (runs on Windows against Android).

### Backend and data

-   ASP.NET Core Web API with C# on .NET 10 (LTS), with the SDK pinned
    in `global.json`.
-   Clean Architecture principles in a modular monolith (§9).
-   Entity Framework Core with Npgsql, NetTopologySuite for spatial
    types and EFCore.NamingConventions for `snake_case` names.
-   PostgreSQL with PostGIS for geospatial queries.
-   ASP.NET Core Identity, with the token strategy chosen in D2.
-   Role- and resource-based authorization.
-   ASP.NET Core rate limiting middleware, enabled on authentication
    endpoints from Phase 2.
-   SignalR for real-time events.
-   Transactional outbox: domain events (notifications, SignalR
    broadcasts, partner alerts) are written in the same transaction as
    the state change, so a committed change always produces its event.
-   .NET Worker Service for outbox dispatch, scheduled work, booking-hold
    expiry and payment reconciliation. Jobs claim work with
    `SELECT … FOR UPDATE SKIP LOCKED` so several instances cannot
    process the same item.
-   Media storage per D8 behind an `IMediaStorage` interface, with
    server-side validation and image resizing.
-   OpenAPI/Swagger for development.
-   xUnit for automated tests.
-   Serilog structured logging with personal-data redaction, and health
    checks.

### Web console (admin and partner)

-   Next.js with TypeScript, in `apps/web-console`.
-   Calls the same `/api/v1` API with role-based authorization; the
    console never holds database credentials.
-   Shares design tokens with the mobile app where practical; it does
    not share React Native components.
-   Playwright for end-to-end tests.

### Hosting

-   Local Windows development, no Docker.
-   PostgreSQL installed as a Windows service or hosted on a separate
    development server.
-   IIS + ASP.NET Core Hosting Bundle for the API, with the IIS
    WebSocket Protocol feature enabled for SignalR.
-   Next.js web console hosted as a Node.js process behind IIS reverse
    proxy (§13).
-   .NET Worker deployed as a Windows Service.
-   Production HTTPS, protected secrets, backups, monitoring and tested
    recovery.

### State management rule

Use React Context + `useReducer` for app-wide client state that changes
relatively infrequently, such as session summary, selected location,
locale, theme and UI preferences. Use local component state for local UI
interactions. Use TanStack Query for API/server state such as
restaurants, attractions, bookings, order status and profile data. Do
not copy all server data into a global reducer. Expose state and
dispatch through separate contexts (§6) so that components which only
dispatch do not re-render on every state change.

------------------------------------------------------------------------

## 4. UI/UX Direction — Premium Quality

The design must feel like a high-quality travel and local-commerce
product, not a generic admin template.

### Visual direction

-   Clear typography hierarchy, spacious layouts and consistent
    alignment.
-   Warm coastal-inspired palette used with restraint: deep ocean/teal
    as the primary brand color, a warm sand background, coral or amber
    accent, neutral surfaces and accessible text contrast.
-   Use design tokens; never scatter raw color values throughout the
    app.
-   Strong photography for Puducherry destinations, food and
    experiences, with correct image cropping and fallback states.
-   Subtle motion for navigation, loading and feedback; avoid excessive
    animation that delays common tasks.
-   Consistent rounded cards, restrained shadows, clean iconography and
    tactile button states.
-   Avoid overcrowded home screens, unnecessary gradients, excessive
    glassmorphism, tiny text and competing accent colors.
-   Use skeleton placeholders for loading lists where useful.
-   Provide accessible labels, adequate touch targets, scalable text,
    contrast and reduced-motion handling.
-   Use realistic content; mark sample/demo data clearly in
    non-production environments.

### Suggested initial design tokens

These are starting values to refine during visual implementation, not
rigid final branding:

-   `color.brand.primary`: deep teal / ocean green.
-   `color.brand.secondary`: warm amber.
-   `color.background`: warm off-white.
-   `color.surface`: white or near-white.
-   `color.text.primary`: deep charcoal.
-   `color.text.secondary`: slate grey.
-   `color.border`: light neutral grey.
-   `color.success`, `color.warning`, `color.error`, `color.info`:
    semantic colors with accessible contrast.
-   Spacing scale: 4, 8, 12, 16, 20, 24, 32.
-   Corner radii: small, medium, large and pill tokens.
-   Typography: display, heading, body, label and caption variants.
-   Motion: short press feedback, standard transitions and
    reduced-motion fallback.

### Main navigation

Use a simple bottom tab bar with five destinations:

1.  **Home**
2.  **Explore**
3.  **Bookings**
4.  **Saved**
5.  **Profile**

Food, rides, stays, activities, shopping and AI trip planning should be
accessible through the home screen and Explore. Do not create a bottom
tab for every service.

-   **Bookings** shows one combined history of bookings, food orders and
    rides, newest first, served by the customer history read model
    (§17.4). Until Phase 6 it shows an empty state that points to
    Explore.
-   **Saved** shows saved places, saved listings and the user's
    itineraries.

### Home screen layout

1.  Location selector and notifications.
2.  Search bar: "Where do you want to go?"
3.  Primary service shortcuts: Food, Rides, Stays, Activities. Explore
    is already a tab, so it is not repeated here.
4.  Context-aware hero area for tourism or local needs.
5.  Popular nearby places.
6.  Curated local experiences.
7.  Restaurant discovery or food offers, when available.
8.  Events and seasonal collections.
9.  AI "Plan my Pondy trip" entry point.
10. Trust/support entry point and persistent bottom navigation.

Do not show a service as "available now" unless the backend has current,
reliable availability data. If a module is not launched, show a
transparent "Coming soon" state or hide it according to the module flags
returned by `/api/v1/config`.

### Guest access

Customers can browse without an account. Sign-in is requested only when
an action needs an identity, and the app returns the user to that action
afterwards.

| Area | Guest | Signed in |
|---|---|---|
| Home, Explore, search, map, place and listing details | Yes | Yes |
| FAQs and contact information | Yes | Yes |
| Saved places, itineraries, AI trip planner | Prompt to sign in | Yes |
| Bookings, orders, rides, payments | Prompt to sign in | Yes |
| Profile, notifications, support tickets | Sign-in screen | Yes |

------------------------------------------------------------------------

## 5. Atomic Design System

Use Atomic Design to organize reusable presentation components. Atomic
Design is a UI organization approach, not a replacement for
feature/domain architecture.

### Atoms

Small, reusable primitives with minimal business knowledge.

Examples:

-   `OPText`
-   `OPButton`
-   `OPIconButton`
-   `OPInput`
-   `OPSearchField`
-   `OPAvatar`
-   `OPBadge`
-   `OPDivider`
-   `OPSpinner`
-   `OPSkeleton`
-   `OPCheckbox`
-   `OPRadio`
-   `OPChip`
-   `OPImage`
-   `OPPrice`
-   `OPRating`
-   `OPErrorText`

Atoms should accept typed props, support accessibility, and read styling
from design tokens. Avoid embedding API calls or business rules in
atoms.

### Molecules

Small combinations of atoms that perform one UI function.

Examples:

-   `LocationHeader`
-   `SearchBar`
-   `ServiceShortcut`
-   `PlaceCard`
-   `RestaurantCard`
-   `ActivityCard`
-   `HotelCard`
-   `PriceBreakdown`
-   `RatingSummary`
-   `AddressSelector`
-   `QuantityStepper`
-   `EmptyState`
-   `ErrorState`
-   `LoadingState`
-   `BookingStatusBadge`
-   `FormField`
-   `NotificationRow`

Molecules may format display data but should not independently own
complex workflows. They take display-shaped props (title, image, price,
rating), not API response types; feature code maps API models to these
props.

### Organisms

Larger sections composed of molecules and atoms. Only domain-agnostic
organisms live in `design-system/organisms`. Organisms that know about a
feature live in that feature's `components/` folder. The design system
must never import from `features/`; otherwise features and the design
system import each other.

Design-system organisms (domain-agnostic):

-   `SectionWithHeader`
-   `HorizontalCarousel`
-   `MapWithList`
-   `StatusTimeline`
-   `FilterSheet`
-   `FormSection`
-   `ModalSheet`

Feature organisms (in `features/*/components`):

-   Home: `HomeHeader`, `ServiceGrid`, `NearbyPlacesSection`,
    `FeaturedExperiencesCarousel`
-   Tourism: `TripItineraryBuilder`
-   Food: `RestaurantListing`, `RestaurantMenu`, `CartSummary`,
    `CheckoutForm`, `OrderTrackingPanel`
-   Rides: `RideRequestPanel`
-   Bookings: `BookingCardList`, `BookingTimeline`
-   Profile: `ProfileSummary`
-   Support: `SupportRequestForm`

Feature organisms may coordinate local UI interactions and call feature
hooks, but API business rules remain in services and the backend.

### Templates

Reusable screen layouts that define page structure without hard-coding a
specific record.

Examples:

-   `MainTabTemplate`
-   `ListingTemplate`
-   `DetailsTemplate`
-   `SearchResultsTemplate`
-   `CheckoutTemplate`
-   `BookingDetailsTemplate`
-   `MapExploreTemplate`
-   `FormTemplate`
-   `SettingsTemplate`
-   `AuthTemplate`

Templates control spacing, safe areas, headers, scroll behavior, loading
boundaries and responsive layout.

### Pages / Screens

Screens compose templates with feature data and navigation.

Examples:

-   `HomeScreen`
-   `ExploreScreen`
-   `PlaceDetailsScreen`
-   `RestaurantDetailsScreen`
-   `CartScreen`
-   `CheckoutScreen`
-   `OrderDetailsScreen`
-   `RideBookingScreen`
-   `HotelDetailsScreen`
-   `ActivityDetailsScreen`
-   `BookingsScreen`
-   `SavedScreen`
-   `ProfileScreen`
-   `LoginScreen`

### Suggested folder structure

Monorepo layout. Create each app folder only when its phase starts.

``` text
OnePondy/
├── apps/
│   ├── customer-mobile/      # Phase 0
│   ├── web-console/          # Next.js admin and partner areas, Phase 3
│   └── driver-mobile/        # Phase 8
├── backend/                  # ASP.NET Core solution (§9)
├── database/
│   └── scripts/              # extensions, reference SQL, DBA-reviewed scripts (§18.5)
├── deploy/
│   └── iis/                  # web.config templates, publish profiles, migration scripts
├── docs/
├── .config/
│   └── dotnet-tools.json     # pinned dotnet-ef version
├── package.json              # npm workspaces for the JavaScript apps
└── README.md
```

Customer mobile app:

``` text
apps/customer-mobile/
├── src/
│   ├── app/
│   │   ├── App.tsx
│   │   └── providers/
│   │       ├── AppProviders.tsx
│   │       ├── QueryProvider.tsx
│   │       └── SessionBootstrap.tsx
│   ├── navigation/
│   │   ├── RootNavigator.tsx
│   │   ├── AuthNavigator.tsx
│   │   └── MainTabNavigator.tsx
│   ├── design-system/
│   │   ├── tokens/
│   │   ├── atoms/
│   │   ├── molecules/
│   │   ├── organisms/
│   │   └── templates/
│   ├── features/
│   │   ├── auth/
│   │   │   ├── screens/
│   │   │   ├── components/
│   │   │   ├── hooks/
│   │   │   ├── services/
│   │   │   ├── schemas/
│   │   │   └── types/
│   │   ├── home/
│   │   ├── tourism/
│   │   ├── food/
│   │   ├── rides/
│   │   ├── stays/
│   │   ├── activities/
│   │   ├── shopping/
│   │   ├── bookings/
│   │   ├── payments/
│   │   ├── notifications/
│   │   ├── support/
│   │   └── profile/
│   ├── state/
│   │   ├── rootReducer.ts
│   │   ├── rootActions.ts
│   │   ├── actionTypes.ts
│   │   ├── appState.ts
│   │   ├── AppStateContext.tsx
│   │   └── index.ts
│   ├── i18n/
│   │   ├── index.ts
│   │   └── locales/          # en.json, ta.json, fr.json
│   ├── api/
│   │   ├── apiClient.ts
│   │   ├── endpoints.ts
│   │   └── apiErrors.ts
│   ├── hooks/
│   ├── storage/
│   ├── utils/
│   ├── constants/
│   └── types/
├── assets/
├── app.json
├── package.json
└── tsconfig.json
```

`src/app/` holds the app entry point and providers. It is not an Expo
Router routes directory; do not install Expo Router.

------------------------------------------------------------------------

## 6. React Context + useReducer Architecture

Use a single root reducer for app-wide client state, with feature
reducers combined underneath it as the project grows. Do not use one
giant reducer for every form, screen and API response.

### Responsibilities

-   `actionTypes.ts`: central, typed action type constants.
-   `rootActions.ts`: action creators and action payload types.
-   `rootReducer.ts`: root reducer that delegates to feature reducers
    and returns the next immutable state.
-   `appState.ts`: state interfaces and initial state.
-   `AppStateContext.tsx`: owns `useReducer` and exposes state and
    dispatch through two separate contexts, so components that only
    dispatch do not re-render on state changes. Exports
    `AppStateProvider`, `useAppState` and `useAppDispatch`. This is the
    only provider of app state.
-   `SessionBootstrap.tsx`: on launch, reads tokens from SecureStore,
    validates the session with the API and dispatches
    `AUTH/SESSION_RESTORED` or `AUTH/SESSION_CLEARED`. It holds no state
    of its own.
-   `AppProviders.tsx`: composes the app state provider with query,
    navigation, i18n and session bootstrap.
-   Feature-specific reducers can be added when a domain has enough
    client state to justify one.

### State boundaries

Appropriate for Context + `useReducer`:

-   Current user/session summary (never store passwords or payment
    secrets). `unauthenticated` means a guest; public screens stay
    usable (§4 Guest access).
-   Selected service area or manually selected location.
-   Language and theme.
-   Global UI preferences and transient banners.
-   A small cross-screen client workflow where needed.

Keep in TanStack Query rather than root reducer:

-   Restaurant and menu responses.
-   Attraction and hotel lists.
-   Booking and order API records.
-   Ride status from the server.
-   Paginated search results.
-   Server-driven availability and prices.

Keep local to a component or feature:

-   Current input text.
-   Expanded/collapsed card.
-   Temporary modal visibility.
-   Form field values before submission.
-   Local carousel index.

### Action flow

``` text
Screen / Component
      |
      v
useAppDispatch()
      |
      v
Action creator from rootActions.ts
      |
      v
Typed action with action type + payload
      |
      v
rootReducer()
      |
      +--> authReducer()
      +--> locationReducer()
      +--> preferencesReducer()
      +--> uiReducer()
      |
      v
New immutable state
      |
      v
Context provider updates consumers
```

### Example action type convention

Use namespaced, consistent action type strings:

-   `AUTH/SESSION_RESTORED`
-   `AUTH/SESSION_CLEARED`
-   `LOCATION/SET_SELECTED`
-   `PREFERENCES/SET_LANGUAGE`
-   `PREFERENCES/SET_THEME`
-   `UI/SHOW_BANNER`
-   `UI/HIDE_BANNER`

Actions must be typed discriminated unions. Avoid arbitrary strings and
`any`.

### Reducer rules

1.  Reducers must be pure and synchronous.
2.  Never call APIs, storage, timers, navigation or analytics inside a
    reducer.
3.  Never mutate existing state.
4.  Put asynchronous operations in feature services/hooks or query
    mutations; dispatch the resulting success/failure action if global
    client state needs updating.
5.  Use exhaustive TypeScript handling where practical.
6.  Keep action payloads small and serializable.
7.  Persist only explicitly approved values: preferences in
    AsyncStorage, tokens and session material in SecureStore.
8.  Never treat client state as the authority for authorization, payment
    success or booking availability.

### Suggested initial root state

``` ts
export interface AppState {
  auth: {
    status: "loading" | "authenticated" | "unauthenticated"; // unauthenticated = guest
    user: {
      id: string;
      displayName: string;
      roles: string[];
    } | null;
  };
  location: {
    selectedPlaceId: string | null;
    latitude: number | null;
    longitude: number | null;
    source: "gps" | "manual" | null;
  };
  preferences: {
    language: "en" | "ta" | "fr"; // final list per D10
    theme: "light" | "dark" | "system";
  };
  ui: {
    banner: {
      message: string;
      kind: "info" | "success" | "warning" | "error";
    } | null;
  };
}
```

The exact fields may evolve. Avoid storing a full GPS history or
sensitive identity documents in global client state.

### Index/barrel file conventions

Use `index.ts` only where it improves discoverability and does not
create circular imports. Prefer explicit exports.

Example:

``` text
design-system/atoms/
├── OPButton/
│   ├── OPButton.tsx
│   ├── OPButton.types.ts
│   ├── OPButton.test.tsx
│   └── index.ts
└── index.ts
```

Feature barrels should export public feature interfaces only. Do not
create deep, circular barrel chains. The `state/index.ts` may export
`AppStateProvider`, `useAppState`, `useAppDispatch`, and public state
types.

------------------------------------------------------------------------

## 7. Navigation and UX Workflows

### Authentication workflow

1.  Launch app and restore a valid session if one exists.
2.  If there is no session, show onboarding once, then continue as a
    guest (§4 Guest access). Show login/signup when the user chooses to
    or when an action requires it.
3.  Sign in with the method chosen in D1 (recommended: mobile number and
    OTP, with email as a secondary option).
4.  Validate inputs on device and server. OTP requests and verification
    attempts are rate-limited per number, device and IP.
5.  Submit credentials securely over HTTPS.
6.  Store tokens in SecureStore.
7.  Fetch the user profile and permissions from the API.
8.  Return to the screen or action that triggered sign-in.
9.  On logout or session revocation, clear session state, the TanStack
    Query cache and other protected cached data.

### Account deletion workflow

Google Play and the App Store both require account deletion inside the
app.

1.  User opens Profile → Account → Delete account.
2.  The app explains what is deleted and what must be kept, such as
    invoices and payment records required by tax law.
3.  User re-authenticates and confirms.
4.  The server refuses deletion while bookings, orders or refunds are in
    progress, and tells the user what to resolve first.
5.  The server revokes all sessions, deletes or anonymizes personal data
    per the retention policy, and keeps financial records linked to the
    anonymized account (§17.3 notes).
6.  A confirmation is shown and sent to the user's verified contact.

The same request must also be available through a public web page,
because Google Play requires a deletion route outside the app.

### Tourism workflow

1.  User opens Explore.
2.  App requests location permission with an explanation; manual
    location selection is available.
3.  Fetch nearby attractions and categories.
4.  Display list/map with loading, empty, offline and error states.
5.  Open place details with verified description, directions and
    available facilities.
6.  Save a place or add it to an itinerary (guests are asked to sign in
    first).
7.  If the place offers a paid activity, proceed through the booking
    engine rather than assuming discovery equals availability.

### Food workflow

1.  Select delivery location.
2.  Discover restaurants that currently serve the area.
3.  View menu and item availability.
4.  Add items to cart and validate cart rules.
5.  Show item prices, fees, taxes, discounts and total before
    submission.
6.  Create the order and payment attempt through the backend, using a
    payment method the restaurant accepts (UPI, cards and cash on
    delivery per D3).
7.  Confirm order according to payment and restaurant acceptance rules.
8.  Display status updates and support/cancellation options according to
    policy.

### Ride workflow

1.  Choose pickup and destination.
2.  Request location permission or enter address manually.
3.  Request an authoritative fare quote from the backend.
4.  Show eligible vehicle options, estimate and cancellation terms.
5.  Submit the ride request.
6.  Show searching, assigned driver, arrival and trip progress states.
7.  Complete trip and reconcile fare/payment on the server, including
    cash fares if approved in D3.
8.  Provide safety and support entry points.

Only implement live rides after transport eligibility, provider
onboarding and applicable permissions have been verified.

### Booking workflow

1.  Select listing, date, party/quantity and time slot.
2.  Fetch live availability from the server.
3.  Reconfirm price and cancellation terms; the server stores a snapshot
    of both on the booking.
4.  Reserve inventory with server-side concurrency protection. The
    booking starts as `pending`, holding inventory until `expires_at`.
5.  Process payment and provider confirmation.
6.  Show the booking status truthfully, using the states below.
7.  Provide receipt and support options.

### Booking states

| Status | Meaning |
|---|---|
| `pending` | Inventory held; waiting for payment to start or for provider review. |
| `awaiting_payment` | Payment attempt created; waiting for a verified gateway result. |
| `confirmed` | Payment verified (where required) and provider confirmed. |
| `failed` | Payment failed or was abandoned; hold released. |
| `expired` | Hold timed out before payment or confirmation; hold released. |
| `rejected` | Provider declined; any captured payment is refunded. |
| `cancelled` | Cancelled by customer, partner or admin under the snapshotted policy. |
| `refund_pending` | Refund initiated with the gateway. |
| `refunded` | Refund verified. |
| `completed` | Service delivered; eligible for review. |

Allowed transitions. The server rejects every other transition, and
writes each accepted one to `booking_status_history` in the same
transaction.

| From | To |
|---|---|
| `pending` | `awaiting_payment`, `confirmed` (free or pay-at-venue listings), `rejected`, `expired`, `cancelled` |
| `awaiting_payment` | `confirmed`, `failed`, `expired`, `rejected`, `cancelled` (by the customer only; decided 2026-10-29) |
| `confirmed` | `cancelled`, `completed` |
| `rejected` | `refund_pending` (when payment was captured) |
| `cancelled` | `refund_pending` (when a refund is due) |
| `refund_pending` | `refunded` |

`failed`, `expired`, `refunded` and `completed` are final.

### AI trip planner workflow

1.  A signed-in user opens "Plan my Pondy trip" and enters dates,
    interests, pace, budget band and mobility needs.
2.  The server selects candidate places from published, verified
    records only, filtered by opening hours and distance.
3.  The server asks the AI provider (D5) to order and group those
    candidates. The response must reference place IDs from the candidate
    set; anything else is discarded.
4.  The server validates the result (place IDs exist and are published,
    stops fit opening hours and dates) and returns a draft itinerary
    labelled "AI-assisted — review before use".
5.  The user edits, reorders or removes stops, then saves. Nothing is
    booked automatically.
6.  Requests are rate-limited and cost-capped per user and per day.
    Prompts and responses are logged without personal data.

### Reviews workflow

1.  After a booking or order reaches `completed`, or a ride completes,
    the customer may leave one rating and review for that transaction
    within a set window.
2.  Reviews are checked for abuse and personal data before they are
    published. Partners can reply but cannot edit or delete reviews.
3.  Displayed ratings are computed only from published reviews of
    eligible transactions. Imported or fabricated reviews are never
    shown.

------------------------------------------------------------------------

## 8. API Integration Standards

-   Use a single configurable API base URL per environment.
-   Local development must support the Android emulator and physical
    Android devices with the correct host address; `localhost` on a
    device is not the Windows development PC. iOS simulators and builds
    need EAS Build or a Mac (D4).
-   Use HTTPS in staging and production.
-   Add typed request/response models and consistent error parsing.
-   Use TanStack Query for server data, caching and invalidation.
-   Use mutations for writes and prevent accidental duplicate
    submissions.
-   Use an idempotency key for operations such as order creation and
    payment initiation where supported.
-   Handle timeouts, offline state, retries and session expiry.
-   Do not blindly retry non-idempotent payments or bookings.
-   Do not put server secrets or privileged API keys in the mobile
    bundle.
-   Validate all prices, permissions, availability and payment results
    on the backend.
-   Use SignalR for live updates, with reconnect handling and API
    refetch after reconnection.
-   Version the API in the URL (`/api/v1`). Keep a version working for
    as long as any supported app release depends on it.
-   `/api/v1/config` returns module flags and `minSupportedAppVersion`.
    Below that version the app blocks use and links to the store.

Suggested API areas:

-   `/api/v1/auth`
-   `/api/v1/profile`
-   `/api/v1/discovery`
-   `/api/v1/places`
-   `/api/v1/itineraries`
-   `/api/v1/restaurants`
-   `/api/v1/orders`
-   `/api/v1/rides`
-   `/api/v1/stays`
-   `/api/v1/activities`
-   `/api/v1/bookings`
-   `/api/v1/payments`
-   `/api/v1/notifications`
-   `/api/v1/support`
-   `/api/v1/config` (module flags, minimum app version; public)
-   `/api/v1/account` (account deletion)
-   `/api/v1/history` (combined bookings, orders and rides)
-   `/api/v1/reviews`
-   `/api/v1/media` (console uploads)
-   `/api/v1/partner/*` (partner area of the web console)
-   `/api/v1/admin/*` (admin area of the web console)
-   `/api/v1/webhooks/payments` (gateway callbacks; verified by
    signature, not user authentication)

------------------------------------------------------------------------

## 9. Backend and Database Architecture

Use a modular monolith initially: one deployable API and one worker,
with code organized by module inside each layer.

``` text
backend/
├── OnePondy.sln
├── global.json
├── src/
│   ├── OnePondy.Api/            # endpoints grouped by module, auth, rate limiting
│   ├── OnePondy.Domain/         # Domain/<Module>/...
│   ├── OnePondy.Application/    # Application/<Module>/... use cases, validators
│   ├── OnePondy.Infrastructure/ # persistence, gateways, storage, outbox
│   └── OnePondy.Worker/         # outbox dispatch, hold expiry, reconciliation
└── tests/
    ├── OnePondy.UnitTests/
    ├── OnePondy.IntegrationTests/
    └── OnePondy.ArchitectureTests/
```

### Module boundaries

-   Each module owns a namespace (for example
    `OnePondy.Application.Bookings`) and its own tables.
-   A module uses another module only through that module's public
    application interfaces or domain events, never by querying its
    tables directly.
-   Layer direction: Domain depends on nothing; Application depends on
    Domain; Infrastructure and Api depend on Application.
-   `OnePondy.ArchitectureTests` enforces these rules, for example with
    NetArchTest.
-   Split a module into its own service only with a documented,
    justified need (§15).

### Domain modules

-   Identity and access control.
-   Businesses and provider onboarding.
-   Tourism and places.
-   Restaurants, menus and food orders.
-   Delivery dispatch.
-   Transport, drivers and trips.
-   Accommodation.
-   Activities and events.
-   Bookings and inventory.
-   Payments, refunds, commissions and ledger.
-   Notifications, support and audit logs.
-   Reviews and moderation.
-   Content, media and configuration (module flags, minimum app
    version).
-   AI trip planning.

### Database principles

-   PostgreSQL + PostGIS.
-   Store money as `numeric(12,2)` in INR, mapped to C# `decimal`;
    never use floating point (D14).
-   Store immutable price and cancellation-policy snapshots on orders
    and bookings.
-   Use optimistic concurrency (PostgreSQL `xmin` through Npgsql) on
    records that are edited concurrently, such as bookings, listings and
    availability slots.
-   Write domain events to an outbox table in the same transaction as
    the state change; the worker publishes them.
-   Use database constraints and transactions for inventory and
    financial integrity.
-   Keep status history for bookings, orders, rides and payment events.
-   Store UTC timestamps and format for the relevant local timezone in
    the UI.
-   Protect personal data and apply a documented retention policy.
-   Back up with WAL archiving and base backups for point-in-time
    recovery (§20).
-   Use migrations reviewed and applied deliberately in each
    environment.
-   Never commit real secrets, production data or database backups to
    Git.

------------------------------------------------------------------------

## 10. Phased Implementation Plan

Phases run in order unless a phase says it can run in parallel. A phase
is done when its acceptance criteria and the Definition of Done (§14)
are met. The release readiness gate at the end of this section applies
before every public release (see the release plan in §2). Do not start a
task that depends on an open decision in §22.

### Phase 0 — Repository, tooling and CI

**Goal:** A clean, reproducible local development setup.

**Tasks:**

-   Create the OnePondy monorepo with the layout in §5 and npm
    workspaces for the JavaScript apps.
-   Add the customer mobile app using React Native, TypeScript and Expo
    development builds (`blank-typescript` template, React Navigation).
-   Add design-system and feature folder conventions.
-   Add ESLint, Prettier, TypeScript strict mode, Jest and React Native
    Testing Library.
-   Add `.gitignore`, `.env.example` and README.
-   Set up the ASP.NET Core solution on .NET 10 with `global.json`, a
    local `dotnet-ef` tool manifest and PostgreSQL connection
    configuration.
-   Add a CI pipeline that runs lint, type-check, unit tests and the
    backend build on every pull request.
-   Verify the app starts on an Android emulator and the API runs on
    Windows.

**Acceptance criteria:**

-   A fresh clone can be configured from documented steps.
-   No secrets are committed.
-   Mobile app, API and database connection can be verified locally.
-   Lint, type-check and baseline tests pass locally and in CI.

### Phase 1 — Design system and app shell

**Goal:** Premium, consistent navigation and reusable components.

**Tasks:**

-   Define tokens, typography, spacing, colors, elevations and radii.
-   Implement atoms, molecules, domain-agnostic organisms and templates.
-   Implement the state module (§6) with the `preferences`, `location`
    and `ui` slices; the `auth` slice follows in Phase 2.
-   Set up i18next with all English strings externalized. Tamil and
    French fall back to English until reviewed translations exist.
-   Build splash/loading, onboarding, bottom tabs, search, location
    selector and profile shell.
-   Implement accessible buttons, inputs, cards, badges, skeletons,
    empty/error states and modal patterns.
-   Add light/dark theme support only if it can be implemented
    consistently.
-   Add reduced-motion behavior and screen-reader labels.

**Acceptance criteria:**

-   Core screens use design-system components rather than duplicated
    styles.
-   No user-facing string is hard-coded outside the locale files.
-   Layout works on small and large phones.
-   Touch targets, contrast, loading, error and empty states are
    verified.
-   UI looks coherent and production-oriented with clearly labelled
    sample content.

### Phase 2 — App state, authentication and accounts

**Goal:** Predictable app-wide client state, guest browsing and secure
sign-in.

**Depends on:** D1 (login method), D2 (token strategy).

**Tasks:**

-   Add the `auth` slice and keep the typed `useAppState` and
    `useAppDispatch` hooks.
-   Implement `SessionBootstrap`, session restoration, login, logout and
    profile retrieval.
-   Implement guest mode and the sign-in prompts in §4 Guest access.
-   Apply rate limits to OTP, login and token refresh endpoints, with
    alerts on OTP abuse.
-   Implement account deletion in the app and through a public web
    request page (§7), with the retention rules it depends on.
-   Integrate TanStack Query for server state.
-   Add protected navigation and session-expiry handling.

**Acceptance criteria:**

-   Reducers are pure and tested.
-   No API or storage side effects occur inside reducers.
-   Guests can use public screens; protected screens and actions require
    a valid session.
-   Session clear removes protected user data and the query cache, and
    handles navigation safely.
-   OTP and login endpoints reject requests above the configured limits.
-   Account deletion revokes sessions and removes or anonymizes personal
    data per the retention policy.
-   Server state is not duplicated unnecessarily in the root reducer.

### Phase 3 — Web console and content management

**Goal:** Give the team a tool to create, verify and publish place
content before customers depend on it.

**Depends on:** D8 (media storage), D11 (content sourcing and photo
licensing).

**Tasks:**

-   Create the Next.js web console with staff sign-in and role-based
    navigation (administrator, content editor, support agent).
-   Place management: create, edit, translate and categorize places, and
    set coordinates, opening hours and entry fees.
-   Verification workflow (draft → pending → verified or rejected),
    recording who verified, when and from what source. Flag places whose
    last review is older than the agreed interval.
-   Media upload with validation, resizing, and licence and attribution
    fields.
-   Module flags and minimum app version, served from `/api/v1/config`.
-   Audit log of content and configuration changes.

**Acceptance criteria:**

-   Public APIs return only verified, published places.
-   Every verification records the user, time and source.
-   Uploaded images are validated, resized and shown with attribution
    where required.
-   Console actions are authorized server-side by role; a content editor
    cannot change configuration.
-   Changing a module flag changes what the customer app shows without a
    new app release.

### Phase 4 — Tourism discovery and itineraries

**Goal:** Deliver the first valuable user experience without needing
live delivery operations. Completing this phase completes Release 1.

**Tasks:**

-   Create attractions, categories, place detail and search APIs.
-   Build Explore list/map, filters, saved places and directions.
-   Show verified photos, descriptions, coordinates, hours, entry fees
    and visitor guidance in the user's language, falling back to
    English.
-   Add the manual itinerary builder with drag-to-reorder, and
    save/share by link.
-   Add graceful fallback when location is denied or maps are
    unavailable.
-   Use verified content or clearly labelled seed data.

**Acceptance criteria:**

-   Users can search, filter, view and save attractions.
-   Manual location works without GPS permission.
-   Reordering itinerary stops never fails or loses a stop.
-   A shared itinerary link shows the itinerary only, not the owner's
    personal data.
-   No invented live opening hours or booking availability.
-   Offline and API failure states are useful and clear.

### Phase 5 — AI trip planner

**Goal:** Draft itineraries from verified places, which the user reviews
before saving.

**Depends on:** D5 (AI provider). **Can run in parallel with:**
Phase 6.

**Tasks:**

-   Implement the AI trip planner workflow (§7) behind an
    `IItineraryPlanner` interface.
-   Restrict candidates to published, verified places and validate every
    returned place ID.
-   Add per-user and daily cost caps, rate limits and a kill switch in
    the module flags.
-   Log prompts and responses without personal data for quality review.

**Acceptance criteria:**

-   A generated itinerary contains only places from the verified
    candidate set.
-   Drafts are labelled AI-assisted and are never saved or booked
    without user action.
-   The feature falls back to the manual builder when the provider is
    unavailable or the cap is reached.

### Phase 6 — Partner listings and booking engine

**Goal:** A reusable booking engine for activities, events, stays and
other reservable services, plus shop and service listings.

**Tasks:**

-   Partner area of the web console: onboarding, document upload (GSTIN
    and required licences) and admin approval.
-   Listings for activities, events, stays, shops and services. Shops
    and services are discovery-only (D13).
-   Availability slots, capacity and cancellation policies.
-   Booking creation with idempotency keys, holds that expire through
    the worker, confirmation and cancellation.
-   Booking state machine (§7) enforced on the server, with history
    written in the same transaction.
-   Prevent double booking through transactional server-side rules.
-   Booking history in the Bookings tab, status timeline and saved
    listings.
-   Partner booking inbox with accept and reject.

**Acceptance criteria:**

-   Two concurrent requests cannot reserve the same exclusive inventory.
-   Retrying a booking request with the same idempotency key returns the
    original booking.
-   Expired holds release their inventory.
-   Every booking transition is validated on the server and recorded in
    history.
-   The price and cancellation terms shown at booking match the stored
    snapshot.

### Phase 7 — Payments, ledger and notifications

**Status (2026-10-29):** built in sandbox as steps 7.1 to 7.9 (see
`docs/phase-7-plan.md` and the README verification); live payments are
intentionally off until the go-live gate in the plan is met.

**Goal:** Take real payments safely, with records that reconcile, before
any paid feature launches. Completing this phase completes Release 2
together with Phases 5 and 6.

**Depends on:** D3 (payment gateway and methods), D7 (push provider),
legal and tax review (§21).

**Tasks:**

-   Integrate the payment gateway in sandbox: payment attempts,
    signature-verified webhooks and refunds.
-   Record gateway events, ledger entries and commissions, and reconcile
    them daily against the gateway's settlement reports.
-   Partner settlement process and statements.
-   Push notifications and deep links with authorization checks, sent
    through the outbox.
-   Receipts and transaction history.
-   Switch to live payments only after reconciliation and settlement
    have been tested end to end.

**Acceptance criteria:**

-   Payment results are verified server-side; a success reported by the
    app is never trusted.
-   Payment failure or timeout does not create a false confirmed
    booking.
-   Duplicate webhooks and retried requests are handled idempotently.
-   Reconciliation finds and resolves failed or delayed callbacks.
-   Ledger entries balance for every payment, refund and settlement.
-   Sensitive values are never logged.
-   Notifications do not expose private booking details to unauthorized
    recipients.

### Phase 8 — Food ordering and driver app (delivery)

**Goal:** End-to-end food ordering with controlled operational
assumptions. This is Release 3.

**Depends on:** Phase 7; restaurant onboarding with FSSAI licence
numbers; a delivery operations plan.

**Tasks:**

-   Restaurant profiles, menus, item availability and service areas,
    managed in the partner area.
-   Cart, checkout, price breakdown and order creation with idempotency
    keys.
-   Restaurant accept/reject and preparation states in the partner area.
-   Driver app, delivery mode: onboarding, duty status, assignment
    acceptance, pickup and drop-off, with location collected only during
    an active job.
-   Delivery assignment and customer tracking.
-   Cash on delivery, if approved in D3, including reconciliation of
    collected cash.
-   Cancellation, refund and customer support rules.
-   Admin order monitoring.
-   Extend the customer history view with food orders.

**Acceptance criteria:**

-   Price is revalidated on the server at checkout.
-   Duplicate submissions do not create duplicate orders.
-   Restaurant and delivery states cannot be changed arbitrarily by a
    customer.
-   An assignment cannot be accepted by more than one delivery partner.
-   GST on restaurant orders is calculated and recorded as required for
    an e-commerce operator (§21).
-   Refund and settlement records are auditable.

### Phase 9 — Transport

**Goal:** Ride requests and authorized live tracking. This is
Release 4.

**Depends on:** aggregator licence and legal approval (§21), driver
supply and insurance.

**Tasks:**

-   Driver onboarding, document review and vehicle eligibility, added to
    the driver app as ride mode.
-   Driver availability and job dispatch.
-   Fare quotes, ride requests, assignment and trip lifecycle.
-   Location permission, foreground/background constraints and privacy
    controls.
-   SignalR updates, reconnect behavior and API refetch.
-   Safety (SOS, trip sharing), cancellation, incident reporting and
    support.
-   Extend the customer history view with rides.

**Acceptance criteria:**

-   Only eligible and approved providers receive relevant jobs.
-   Assignment cannot be accepted by multiple drivers.
-   Location collection follows permission and active-duty rules.
-   Live updates recover after a disconnect.
-   Do not launch publicly until applicable legal and operational
    requirements are met.

### Phase 10 — Reviews, support and trust hardening

**Goal:** Trustworthy reviews, mature support and abuse protection.

**Can run in parallel with:** Phases 8 and 9, once Phase 7 is complete.

**Tasks:**

-   Reviews and ratings for completed transactions (§7), with moderation
    and partner replies.
-   Support tickets with agent tools in the web console, response-time
    targets and escalation.
-   Abuse prevention and rate limiting across all public endpoints.
-   Monitoring dashboards, alerting and audit log review.

**Acceptance criteria:**

-   Only a customer with an eligible completed transaction can review
    it, and only once.
-   Partners cannot edit or delete reviews.
-   Support agents see only the data a ticket needs, and every access is
    audited.
-   Rate limits and alerts cover all public endpoints.

### Release readiness gate (before every public release)

**Tasks:**

-   Run the release's critical end-to-end flows (§11) on real devices,
    including a low-end Android phone.
-   Test accessibility, performance and low-network behaviour against
    the targets in §20.
-   Prepare release builds, signing, store listings, the privacy policy,
    the Google Play Data safety form and App Store privacy details.
-   Publish the backend to IIS and configure production API URLs.
-   Deploy worker processes as Windows Services.
-   Configure HTTPS, backups and monitoring, and run a recovery drill.
-   Confirm the §21 compliance items that apply to the release.

**Acceptance criteria:**

-   Critical workflows pass automated and manual regression tests.
-   Production configuration contains no development secrets.
-   Backups and restore procedures are tested.
-   Deployment and rollback steps are documented.
-   No feature is presented as live if its operational dependencies are
    missing.

------------------------------------------------------------------------

## 11. Testing Strategy

### Unit tests

-   Root reducer state transitions.
-   Action payload types and state invariants.
-   Validation schemas and formatters.
-   Price display and total calculation helpers.
-   Navigation guard decisions.
-   Feature-level state transitions.
-   Booking state transition rules (§7).

### Component tests

-   Button loading/disabled behavior.
-   Search input and clear action.
-   Place/restaurant cards.
-   Empty/error/retry states.
-   Form validation and submit behavior.
-   Accessibility labels and touch interactions.

### Integration tests

-   API authentication and authorization.
-   Booking and inventory concurrency.
-   Order creation and status transitions.
-   Payment webhook signature and idempotency.
-   Session expiry and logout.
-   SignalR authorization and reconnect behavior.
-   Idempotent booking and order retries.
-   Hold expiry and slot release by the worker.
-   Outbox delivery after a committed status change.
-   Itinerary stop reordering.
-   Account deletion and anonymization.

### End-to-end scenarios

Use Maestro for mobile flows (runs on Windows against Android) and
Playwright for the web console.

1.  New customer opens the app and browses tourism as a guest.
2.  Guest tries to save an attraction, signs in, and the attraction is
    saved.
3.  Customer creates, reorders and shares an itinerary.
4.  Content editor verifies and publishes a place in the web console,
    and it appears in the app.
5.  Customer requests a booking and sees the correct pending/confirmed
    state.
6.  Customer completes a food order through a sandbox flow.
7.  Driver accepts a simulated assignment in the driver app and location
    updates reach only authorized users.
8.  Customer cancels according to policy and sees refund status.
9.  Network loss and retry do not create duplicate bookings or payments.
10. Customer deletes their account and can no longer sign in; their past
    bookings remain visible to the partner in anonymized form.

------------------------------------------------------------------------

## 12. Security and Privacy Requirements

-   Use HTTPS in staging and production.
-   Keep access tokens short-lived and rotate/revoke refresh sessions
    securely.
-   Use secure storage for sensitive session material.
-   Enforce authorization on every protected API resource.
-   Do not rely on client-side roles for access control.
-   Validate payment callbacks and never trust the mobile app to report
    payment success.
-   Keep API keys and secrets out of the app bundle and source control.
-   Restrict map provider keys by application and API.
-   Collect location only for a clearly explained purpose and within
    applicable permission rules.
-   Do not track drivers outside authorized operational periods.
-   Minimize logs containing personal information.
-   Rate-limit OTP and login by phone number, device and IP from
    Phase 2; cap daily SMS spend and alert on spikes.
-   Add rate limits, abuse protection, audit records and dependency
    scanning.
-   Keep production secrets outside the repository and the web root (IIS
    application pool environment variables or a secrets vault, D12),
    readable only by the service account.
-   Define retention, deletion, consent and incident-response processes.
    Offer account deletion in the app and through a public web page.
-   Complete the India compliance checklist (§21) before each public
    release.

------------------------------------------------------------------------

## 13. Local Development and IIS Deployment

### Local development

-   Windows PC without Docker.
-   Node.js and package manager for mobile tooling.
-   Android Studio and Android SDK for Android emulator/device builds.
-   .NET SDK for API and worker.
-   PostgreSQL/PostGIS installed directly or on a separate development
    host.
-   API runs on a documented local URL.
-   The Android emulator reaches the PC at `10.0.2.2`. A physical phone
    uses the PC's LAN IP, with the API port allowed through Windows
    Firewall.
-   iOS builds use EAS Build; iOS devices install test builds through
    TestFlight or EAS internal distribution (D4).
-   Use test credentials and payment sandbox.
-   Keep environment configuration outside source control.

### Production deployment

-   Publish ASP.NET Core API in Release mode.
-   Host API behind IIS with the correct ASP.NET Core Hosting Bundle.
-   Enable the IIS WebSocket Protocol feature for SignalR. Set the API
    application pool to start mode `AlwaysRunning` with idle time-out 0,
    and schedule recycling outside peak hours, because a recycle drops
    live connections.
-   Run a single API server until a SignalR backplane is chosen; more
    than one server requires one.
-   Run the Next.js web console as a Node.js process managed as a
    Windows Service, behind IIS URL Rewrite and Application Request
    Routing.
-   Run background workers as Windows Services.
-   Configure PostgreSQL WAL archiving and base backups, least-privilege
    accounts and secure networking; meet the RPO/RTO targets in §20.
-   Configure production HTTPS and approved CORS origins.
-   Use versioned database migrations and a rollback plan.
-   Test mobile builds against staging before switching to production.
-   Keep development, staging and production settings separate.

------------------------------------------------------------------------

## 14. Definition of Done for Every Feature

A feature is complete only when:

-   Requirements and user flow are documented.
-   UI follows Atomic Design and shared design tokens.
-   Loading, empty, error, offline and success states exist.
-   User-facing strings are externalized for translation.
-   A module flag controls visibility if the feature can be hidden or
    shown as "Coming soon".
-   TypeScript strict checks and lint pass.
-   Unit/component/integration tests are added where applicable.
-   API validation and authorization are enforced server-side.
-   No secrets or sensitive information are committed or logged.
-   Accessibility and small-screen layout are checked.
-   Navigation, retries and duplicate submissions are handled.
-   Manual test steps are recorded.
-   The feature is integrated with the correct backend contract.
-   It is clearly labelled unavailable if required external providers or
    legal approvals are not ready.

------------------------------------------------------------------------

## 15. AI Coding Agent Instructions

When implementing OnePondy:

1.  Read this specification and the existing repository before making
    changes.
2.  Implement only the current phase and avoid creating unused
    scaffolding for every future feature.
3.  Preserve the monorepo boundaries and use TypeScript strict mode.
4.  Use Atomic Design for reusable presentation components: atoms,
    molecules, organisms and templates.
5.  Use `rootReducer.ts`, `rootActions.ts`, `actionTypes.ts`,
    `appState.ts` and `AppStateContext.tsx` for the agreed Context +
    `useReducer` architecture.
6.  Keep server state in TanStack Query; do not mirror all API responses
    into Context.
7.  Keep API calls, persistence, analytics, timers and navigation
    outside reducers.
8.  Use typed action unions and immutable reducer updates; do not use
    `any` to bypass type safety.
9.  Never fake a successful payment, booking, ride, delivery or provider
    availability.
10. Do not add Docker or Kubernetes. Local development and deployment
    must follow the Windows/IIS plan.
11. Do not introduce microservices without a documented, justified need.
12. Add tests and run the relevant lint, type-check, build and test
    commands after changes.
13. Report files changed, behavior implemented, commands executed,
    results and known limitations.
14. If a required API, credential, provider or legal decision is
    unavailable, implement a clearly isolated interface/mock for
    development and document the dependency; never silently pretend it
    is production-ready.
15. Work in small, reviewable steps and do not overwrite unrelated user
    changes.
16. Check the open decisions register (§22). If a task depends on an
    open decision, stop and ask instead of choosing silently.

------------------------------------------------------------------------

## 16. First Milestone

Start with **Phase 0 and Phase 1 only**:

1.  Inspect the repository and installed tool versions.
2.  Create the mobile app skeleton with TypeScript and Expo development
    builds.
3.  Add linting, formatting, strict TypeScript, test tooling and a CI
    pipeline that runs them.
4.  Implement design tokens, the Atomic Design folders, i18n setup and
    the state module with the preferences, location and UI slices.
5.  Build the app shell, bottom navigation, polished home screen and
    Explore placeholder.
6.  Add accessible loading, empty, error and retry states.
7.  Run the app on an Android emulator or physical device.
8.  Record setup instructions and test results in the README.

Do not begin real payment, live GPS tracking, food dispatch or ride
dispatch until the foundation is stable.

Decisions D4 (iOS), D6 (maps), D9 (crash reporting and analytics) and
D10 (languages) affect this milestone. Resolve them first, or build
behind interfaces that keep the choice open.

**Success criteria for the first milestone:** the app launches locally,
navigation works, the UI is visually consistent and responsive, reusable
components are tested, and the repository is ready for incremental
development.


---

## 17. Database Schema, SQL Queries and Migration Files

This section defines the initial database implementation approach for OnePondy. Use PostgreSQL with PostGIS and Entity Framework Core (EF Core). The schema should evolve through reviewed, version-controlled migrations rather than ad hoc production edits.

### 17.1 Database naming and conventions

- Database: `onepondy_dev` locally; use separate database names or instances for staging and production.
- Schema: use PostgreSQL's `public` schema initially, or introduce domain schemas only when there is a clear operational benefit.
- Table and column names: use `snake_case`, produced by `EFCore.NamingConventions` (`UseSnakeCaseNamingConvention()`); EF Core's default is PascalCase.
- Translatable content: English values live on the base table; Tamil and French (and any language added under D10) live in a `*_translations` table keyed by record and language.
- Primary keys: UUIDs (`uuid`) generated by the application or database.
- Timestamps: store as `timestamptz` in UTC.
- Money: `numeric(12,2)` in INR, mapped to C# `decimal` (D14); never use floating-point types for money.
- Soft deletion: only where a business requirement exists; do not use it as a substitute for retention policy.
- Foreign keys, unique constraints, check constraints and indexes must be defined for integrity.
- Do not store raw card numbers, CVVs, payment gateway secrets or passwords in application tables.
- Use parameterized queries and EF Core LINQ; never concatenate untrusted input into SQL.

### 17.2 Initial core schema

The schema arrives in phase-specific migrations:

| Migration | Phase | Contents |
|---|---|---|
| 1 | 2–4 | Accounts, place categories, places, translations, entry fees, saved places, itineraries |
| 2 | 6 | Businesses, listings, availability slots, bookings, booking history, customer history view |
| 3 | 7 | Payment attempts, gateway events, refunds, ledger entries, settlements, outbox |
| Later | 8–10 | Restaurants, menus, orders, delivery assignments, drivers, vehicles, rides, location events, reviews |

Example SQL for a local development database:

```sql
-- Run as a PostgreSQL administrator in a local development environment.
CREATE DATABASE onepondy_dev;
```

Connect to `onepondy_dev`, then enable PostGIS if it is installed:

```sql
CREATE EXTENSION IF NOT EXISTS postgis;
```

If the local PostgreSQL installation does not have PostGIS available, install a compatible PostGIS package before running this command. Do not silently remove geographic requirements from production.

### 17.3 Example foundational SQL migrations

The following is an illustrative SQL baseline for the core data model. In the actual application, EF Core migrations should be the source of truth. Keep this SQL in `database/scripts/reference/` or use it as a reviewed schema design reference; do not independently maintain conflicting SQL and EF migration histories.

#### Migration 1 — accounts, places and itineraries (Phases 2–4)

```sql
-- ApplicationUser : IdentityUser<Guid> maps to this table. Identity adds its own
-- credential columns here; only OnePondy-specific columns are shown.
CREATE TABLE IF NOT EXISTS app_users (
    id uuid PRIMARY KEY,
    email varchar(320),
    phone_number varchar(32),
    display_name varchar(120) NOT NULL,
    preferred_language varchar(8) NOT NULL DEFAULT 'en',
    is_active boolean NOT NULL DEFAULT true,
    deleted_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_app_users_language
        CHECK (preferred_language IN ('en', 'ta', 'fr')),
    CONSTRAINT ck_app_users_contact
        CHECK (deleted_at IS NOT NULL OR email IS NOT NULL OR phone_number IS NOT NULL),
    CONSTRAINT ck_app_users_phone_e164
        CHECK (phone_number IS NULL OR phone_number ~ '^\+[1-9][0-9]{7,14}$')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_app_users_email_lower
    ON app_users (lower(email))
    WHERE email IS NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_app_users_phone
    ON app_users (phone_number)
    WHERE phone_number IS NOT NULL;

CREATE TABLE IF NOT EXISTS place_categories (
    code varchar(40) PRIMARY KEY,
    name varchar(80) NOT NULL,
    sort_order integer NOT NULL DEFAULT 0,
    is_active boolean NOT NULL DEFAULT true
);

CREATE TABLE IF NOT EXISTS place_category_translations (
    category_code varchar(40) NOT NULL REFERENCES place_categories(code) ON DELETE CASCADE,
    language varchar(8) NOT NULL,
    name varchar(80) NOT NULL,
    PRIMARY KEY (category_code, language),
    CONSTRAINT ck_place_category_translation_language
        CHECK (language IN ('ta', 'fr'))
);

CREATE TABLE IF NOT EXISTS places (
    id uuid PRIMARY KEY,
    name varchar(180) NOT NULL,
    slug varchar(200) NOT NULL UNIQUE,
    category_code varchar(40) NOT NULL REFERENCES place_categories(code),
    description text,
    visitor_guidance text,
    address text,
    latitude double precision,
    longitude double precision,
    -- Derived from latitude/longitude so the two can never disagree.
    location geography(Point, 4326) GENERATED ALWAYS AS (
        ST_SetSRID(ST_MakePoint(longitude, latitude), 4326)::geography
    ) STORED,
    opening_hours jsonb,
    verification_status varchar(24) NOT NULL DEFAULT 'draft',
    verified_by_user_id uuid REFERENCES app_users(id),
    verified_at timestamptz,
    verification_source varchar(500),
    last_reviewed_at timestamptz,
    is_published boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_places_coordinates
        CHECK ((latitude IS NULL) = (longitude IS NULL)),
    CONSTRAINT ck_places_latitude
        CHECK (latitude IS NULL OR latitude BETWEEN -90 AND 90),
    CONSTRAINT ck_places_longitude
        CHECK (longitude IS NULL OR longitude BETWEEN -180 AND 180),
    CONSTRAINT ck_places_verification_status
        CHECK (verification_status IN ('draft', 'pending', 'verified', 'rejected')),
    CONSTRAINT ck_places_verification_provenance
        CHECK (verification_status <> 'verified'
               OR (verified_by_user_id IS NOT NULL
                   AND verified_at IS NOT NULL
                   AND verification_source IS NOT NULL)),
    CONSTRAINT ck_places_publish_requires_verified
        CHECK (NOT is_published OR verification_status = 'verified')
);

CREATE INDEX IF NOT EXISTS ix_places_public_category
    ON places (category_code)
    WHERE is_published;

CREATE INDEX IF NOT EXISTS ix_places_public_location
    ON places USING gist (location)
    WHERE is_published;

CREATE INDEX IF NOT EXISTS ix_places_review_due
    ON places (last_reviewed_at)
    WHERE is_published;

CREATE TABLE IF NOT EXISTS place_translations (
    place_id uuid NOT NULL REFERENCES places(id) ON DELETE CASCADE,
    language varchar(8) NOT NULL,
    name varchar(180) NOT NULL,
    description text,
    visitor_guidance text,
    PRIMARY KEY (place_id, language),
    CONSTRAINT ck_place_translation_language
        CHECK (language IN ('ta', 'fr'))
);

CREATE TABLE IF NOT EXISTS place_entry_fees (
    place_id uuid NOT NULL REFERENCES places(id) ON DELETE CASCADE,
    visitor_category varchar(24) NOT NULL,
    amount numeric(12,2) NOT NULL,
    currency char(3) NOT NULL DEFAULT 'INR',
    PRIMARY KEY (place_id, visitor_category),
    CONSTRAINT ck_place_entry_fee_category
        CHECK (visitor_category IN
          ('standard', 'indian_adult', 'foreign_adult', 'child', 'student', 'senior')),
    CONSTRAINT ck_place_entry_fee_amount
        CHECK (amount >= 0)
);

CREATE TABLE IF NOT EXISTS saved_places (
    user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    place_id uuid NOT NULL REFERENCES places(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, place_id)
);

CREATE TABLE IF NOT EXISTS itineraries (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    title varchar(180) NOT NULL,
    start_date date,
    end_date date,
    status varchar(24) NOT NULL DEFAULT 'draft',
    source varchar(16) NOT NULL DEFAULT 'manual',
    share_token varchar(64) UNIQUE,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_itinerary_dates
        CHECK (end_date IS NULL OR start_date IS NULL OR end_date >= start_date),
    CONSTRAINT ck_itinerary_status
        CHECK (status IN ('draft', 'saved', 'archived')),
    CONSTRAINT ck_itinerary_source
        CHECK (source IN ('manual', 'ai_assisted'))
);

CREATE TABLE IF NOT EXISTS itinerary_stops (
    id uuid PRIMARY KEY,
    itinerary_id uuid NOT NULL REFERENCES itineraries(id) ON DELETE CASCADE,
    place_id uuid NOT NULL REFERENCES places(id),
    day_number integer NOT NULL DEFAULT 1,
    stop_order integer NOT NULL,
    planned_arrival timestamptz,
    notes varchar(1000),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_itinerary_stop_day CHECK (day_number >= 1),
    CONSTRAINT ck_itinerary_stop_order CHECK (stop_order >= 0),
    -- Deferred so that stops can be reordered inside one transaction.
    CONSTRAINT ux_itinerary_stop_order UNIQUE (itinerary_id, day_number, stop_order)
        DEFERRABLE INITIALLY DEFERRED
);
```

#### Migration 2 — partners, listings, availability and bookings (Phase 6)

```sql
CREATE TABLE IF NOT EXISTS businesses (
    id uuid PRIMARY KEY,
    owner_user_id uuid NOT NULL REFERENCES app_users(id),
    name varchar(180) NOT NULL,
    business_type varchar(40) NOT NULL,
    description text,
    phone_number varchar(32),
    email varchar(320),
    address text,
    latitude double precision,
    longitude double precision,
    location geography(Point, 4326) GENERATED ALWAYS AS (
        ST_SetSRID(ST_MakePoint(longitude, latitude), 4326)::geography
    ) STORED,
    -- Optional link when the business is also a tourism place (for example, a heritage hotel).
    place_id uuid REFERENCES places(id),
    gstin varchar(15),
    fssai_license_number varchar(14),
    approval_status varchar(24) NOT NULL DEFAULT 'pending',
    is_active boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_business_type
        CHECK (business_type IN
          ('restaurant', 'hotel', 'activity', 'shop', 'tour_operator', 'service')),
    CONSTRAINT ck_business_approval_status
        CHECK (approval_status IN ('pending', 'approved', 'rejected', 'suspended')),
    CONSTRAINT ck_business_coordinates
        CHECK ((latitude IS NULL) = (longitude IS NULL)),
    CONSTRAINT ck_business_active_requires_approval
        CHECK (NOT is_active OR approval_status = 'approved'),
    CONSTRAINT ck_business_restaurant_fssai
        CHECK (business_type <> 'restaurant'
               OR approval_status <> 'approved'
               OR fssai_license_number IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS ix_businesses_type_status
    ON businesses (business_type, approval_status, is_active);

CREATE TABLE IF NOT EXISTS listings (
    id uuid PRIMARY KEY,
    business_id uuid NOT NULL REFERENCES businesses(id),
    listing_type varchar(24) NOT NULL,
    title varchar(180) NOT NULL,
    description text,
    is_bookable boolean NOT NULL DEFAULT false,
    base_price numeric(12,2),
    currency char(3) NOT NULL DEFAULT 'INR',
    cancellation_policy jsonb,
    status varchar(24) NOT NULL DEFAULT 'draft',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_listings_id_business UNIQUE (id, business_id),
    CONSTRAINT ck_listing_type
        CHECK (listing_type IN ('activity', 'stay', 'event', 'service', 'shop')),
    CONSTRAINT ck_listing_status
        CHECK (status IN ('draft', 'pending', 'published', 'suspended')),
    CONSTRAINT ck_listing_price
        CHECK (base_price IS NULL OR base_price >= 0),
    CONSTRAINT ck_listing_bookable_type
        CHECK (NOT is_bookable OR listing_type IN ('activity', 'stay', 'event', 'service')),
    CONSTRAINT ck_listing_bookable_policy
        CHECK (NOT is_bookable OR cancellation_policy IS NOT NULL)
);

CREATE INDEX IF NOT EXISTS ix_listings_business_status
    ON listings (business_id, status);

CREATE TABLE IF NOT EXISTS saved_listings (
    user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    listing_id uuid NOT NULL REFERENCES listings(id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, listing_id)
);

CREATE TABLE IF NOT EXISTS availability_slots (
    id uuid PRIMARY KEY,
    listing_id uuid NOT NULL REFERENCES listings(id),
    start_at timestamptz NOT NULL,
    end_at timestamptz NOT NULL,
    capacity integer NOT NULL,
    reserved_count integer NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_slot_listing_start UNIQUE (listing_id, start_at),
    CONSTRAINT ck_slot_dates CHECK (end_at > start_at),
    CONSTRAINT ck_slot_capacity
        CHECK (capacity >= 0 AND reserved_count >= 0 AND reserved_count <= capacity)
);

CREATE TABLE IF NOT EXISTS bookings (
    id uuid PRIMARY KEY,
    customer_user_id uuid NOT NULL REFERENCES app_users(id),
    business_id uuid NOT NULL,
    listing_id uuid NOT NULL,
    booking_type varchar(32) NOT NULL,
    status varchar(32) NOT NULL DEFAULT 'pending',
    idempotency_key varchar(64) NOT NULL,
    expires_at timestamptz,
    start_at timestamptz,
    end_at timestamptz,
    guest_count integer NOT NULL DEFAULT 1,
    currency char(3) NOT NULL DEFAULT 'INR',
    subtotal numeric(12,2) NOT NULL,
    fees numeric(12,2) NOT NULL DEFAULT 0,
    tax_amount numeric(12,2) NOT NULL DEFAULT 0,
    discount_amount numeric(12,2) NOT NULL DEFAULT 0,
    total_amount numeric(12,2) NOT NULL,
    price_snapshot jsonb NOT NULL,
    cancellation_policy_snapshot jsonb NOT NULL,
    cancellation_reason varchar(500),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    -- One foreign key for both columns, so a booking cannot point at another business's listing.
    CONSTRAINT fk_bookings_listing_business
        FOREIGN KEY (listing_id, business_id) REFERENCES listings (id, business_id),
    CONSTRAINT ux_bookings_idempotency UNIQUE (customer_user_id, idempotency_key),
    CONSTRAINT ck_booking_type
        CHECK (booking_type IN ('activity', 'stay', 'event', 'service')),
    CONSTRAINT ck_booking_status
        CHECK (status IN
          ('pending', 'awaiting_payment', 'confirmed', 'failed', 'expired',
           'rejected', 'cancelled', 'refund_pending', 'refunded', 'completed')),
    CONSTRAINT ck_booking_hold
        CHECK (status NOT IN ('pending', 'awaiting_payment') OR expires_at IS NOT NULL),
    CONSTRAINT ck_booking_guest_count CHECK (guest_count > 0),
    CONSTRAINT ck_booking_amounts
        CHECK (subtotal >= 0 AND fees >= 0 AND tax_amount >= 0
               AND discount_amount >= 0 AND total_amount >= 0),
    CONSTRAINT ck_booking_total
        CHECK (total_amount = subtotal + fees + tax_amount - discount_amount),
    CONSTRAINT ck_booking_dates
        CHECK (end_at IS NULL OR start_at IS NULL OR end_at > start_at)
);

CREATE INDEX IF NOT EXISTS ix_bookings_customer_created
    ON bookings (customer_user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS ix_bookings_business_status
    ON bookings (business_id, status, start_at);

CREATE INDEX IF NOT EXISTS ix_bookings_hold_expiry
    ON bookings (expires_at)
    WHERE status IN ('pending', 'awaiting_payment');

-- A booking reserves one or more slots (a stay reserves one slot per night).
CREATE TABLE IF NOT EXISTS booking_slots (
    booking_id uuid NOT NULL REFERENCES bookings(id),
    slot_id uuid NOT NULL REFERENCES availability_slots(id),
    quantity integer NOT NULL,
    PRIMARY KEY (booking_id, slot_id),
    CONSTRAINT ck_booking_slot_quantity CHECK (quantity > 0)
);

-- Append-only audit trail; never cascade-deleted.
CREATE TABLE IF NOT EXISTS booking_status_history (
    id uuid PRIMARY KEY,
    booking_id uuid NOT NULL REFERENCES bookings(id) ON DELETE RESTRICT,
    previous_status varchar(32),
    new_status varchar(32) NOT NULL,
    changed_by_type varchar(16) NOT NULL,
    changed_by_user_id uuid REFERENCES app_users(id),
    reason varchar(500),
    created_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_booking_history_status
        CHECK (new_status IN
                 ('pending', 'awaiting_payment', 'confirmed', 'failed', 'expired',
                  'rejected', 'cancelled', 'refund_pending', 'refunded', 'completed')
               AND (previous_status IS NULL OR previous_status IN
                 ('pending', 'awaiting_payment', 'confirmed', 'failed', 'expired',
                  'rejected', 'cancelled', 'refund_pending', 'refunded', 'completed'))),
    CONSTRAINT ck_booking_history_actor_type
        CHECK (changed_by_type IN ('customer', 'partner', 'admin', 'system')),
    CONSTRAINT ck_booking_history_actor
        CHECK ((changed_by_type = 'system') = (changed_by_user_id IS NULL))
);

CREATE INDEX IF NOT EXISTS ix_booking_status_history_booking_time
    ON booking_status_history (booking_id, created_at);
```

**Important schema notes:**

- `ApplicationUser : IdentityUser<Guid>` maps to `app_users`. Identity's `Email` and `PhoneNumber` are the `email` and `phone_number` columns, and Identity adds its credential columns (`password_hash`, `security_stamp`, `normalized_email`, …) to the same table. Do not create a second users table or duplicate password/session management.
- Account deletion sets `deleted_at`, replaces `display_name` with a placeholder, clears `email` and `phone_number`, deletes saved places, saved listings and itineraries, and revokes sessions. Bookings and financial records stay linked to the anonymized row for as long as the retention policy requires, which is why `bookings` does not cascade from `app_users`.
- IDs are supplied by the application (for example UUIDv7, which keeps index inserts ordered). If PostgreSQL generates them instead, configure one strategy consistently.
- `location` is generated from `latitude` and `longitude`, so the two cannot disagree. EF Core maps it with `HasComputedColumnSql(..., stored: true)`. Verify the generated expression on the installed PostGIS version during Phase 0.
- `ux_itinerary_stop_order` is `DEFERRABLE INITIALLY DEFERRED`. A non-deferrable unique constraint rejects the intermediate state of a swap, so reordering two stops would fail. EF Core cannot express `DEFERRABLE`; add it with `migrationBuilder.Sql(...)` (§18.5).
- Once referenced, places are never hard-deleted; unpublish them instead.
- `place_entry_fees`: no row means the fee is unknown; an amount of `0` means free.
- `opening_hours` JSON must be validated against a versioned schema. A normalized table may be preferable for complex recurring schedules and holiday exceptions.
- Public APIs return only places with `is_published = true`. The check constraints make it impossible to publish a place without verification and its provenance.
- `booking_status_history` is append-only: grant the application role `INSERT` and `SELECT` only on it.
- Stays use one availability slot per night; a stay booking reserves every night in its range in one transaction through `booking_slots`.
- Add payment attempts, gateway event records, refunds, ledger entries, settlements and the outbox (Migration 3, Phase 7) before enabling live payments.
- Add restaurants, menu items, orders, delivery assignments, drivers, vehicles, rides and location events in separate, phase-specific migrations, and extend the `customer_history` view for each.
- Review data protection, retention and operational requirements before storing real user or driver data.

### 17.4 Useful SQL queries for development and diagnostics

Nearby published places, when `location` has been populated:

```sql
-- ST_MakePoint takes (longitude, latitude). Replace the sample point with the user-selected location.
SELECT
    id,
    name,
    category_code,
    ST_Distance(
        location,
        ST_SetSRID(ST_MakePoint(79.8, 11.93), 4326)::geography
    ) AS distance_meters
FROM places
WHERE is_published
  AND location IS NOT NULL
ORDER BY location <-> ST_SetSRID(ST_MakePoint(79.8, 11.93), 4326)::geography
LIMIT 20;
```

Place details in the user's language, falling back to English:

```sql
SELECT p.id,
       COALESCE(t.name, p.name) AS name,
       COALESCE(t.description, p.description) AS description,
       COALESCE(t.visitor_guidance, p.visitor_guidance) AS visitor_guidance
FROM places p
LEFT JOIN place_translations t
       ON t.place_id = p.id
      AND t.language = @language
WHERE p.slug = @slug
  AND p.is_published;
```

Reserve a slot atomically, in the same transaction as the `bookings`, `booking_slots` and `booking_status_history` inserts:

```sql
-- Zero rows updated means the slot does not have enough capacity left.
UPDATE availability_slots
SET reserved_count = reserved_count + @quantity,
    updated_at = now()
WHERE id = @slot_id
  AND reserved_count + @quantity <= capacity
RETURNING reserved_count;
```

Worker: claim expired holds without blocking other worker instances:

```sql
SELECT id, status
FROM bookings
WHERE status IN ('pending', 'awaiting_payment')
  AND expires_at < now()
ORDER BY expires_at
LIMIT 100
FOR UPDATE SKIP LOCKED;
```

For each claimed booking, in the same transaction: set its status to `expired`, release its slots, and insert a history row with `changed_by_type = 'system'`.

```sql
UPDATE availability_slots s
SET reserved_count = s.reserved_count - bs.quantity,
    updated_at = now()
FROM booking_slots bs
WHERE bs.booking_id = @booking_id
  AND s.id = bs.slot_id;
```

Before expiring an `awaiting_payment` booking, ask the gateway for the payment status. A payment that succeeds after its booking expired is refunded, or the booking is re-confirmed if the inventory is still available.

Customer history read model for the Bookings tab. Create it with `migrationBuilder.Sql(...)`; later migrations add `UNION ALL` branches for food orders (Phase 8) and rides (Phase 9).

```sql
CREATE OR REPLACE VIEW customer_history AS
SELECT b.id,
       'booking'::varchar(16) AS kind,
       b.customer_user_id,
       l.title,
       b.status,
       b.start_at AS scheduled_at,
       b.total_amount,
       b.currency,
       b.created_at
FROM bookings b
JOIN listings l ON l.id = b.listing_id;

SELECT id, kind, title, status, scheduled_at, total_amount, currency, created_at
FROM customer_history
WHERE customer_user_id = @customer_user_id
ORDER BY created_at DESC
LIMIT 50;
```

Pending business approvals:

```sql
SELECT id, name, business_type, approval_status, created_at
FROM businesses
WHERE approval_status = 'pending'
ORDER BY created_at ASC;
```

Booking status history:

```sql
SELECT previous_status, new_status, changed_by_type, reason, created_at
FROM booking_status_history
WHERE booking_id = @booking_id
ORDER BY created_at ASC;
```

Use parameter binding in application code. The `@parameter` syntax above is illustrative: Npgsql accepts it, while other PostgreSQL clients use `$1`-style placeholders. Do not paste untrusted values into SQL strings.

### 17.5 Seed data

Create development-only seed data for:
- place categories with English names and reviewed translations;
- a small number of clearly labelled sample Puducherry attractions;
- demo business accounts, listings and availability slots (from Phase 6);
- sample bookings with no real payments (from Phase 6).

Seed scripts must be idempotent or safely resettable, must not contain real personal data, and must never automatically create demo users or credentials in production. Any geographic coordinates and opening hours must be verified before being presented as factual.

---

## 18. EF Core Migration Files and Workflow

EF Core migrations are the canonical mechanism for schema evolution. Check generated migrations into Git, review them, and apply them intentionally in each environment.

### 18.1 Suggested migration folder layout

```text
backend/
└── src/
    └── OnePondy.Infrastructure/
        └── Persistence/
            ├── OnePondyDbContext.cs
            ├── Configurations/
            │   ├── AppUserConfiguration.cs
            │   ├── PlaceConfiguration.cs
            │   ├── ItineraryConfiguration.cs
            │   ├── BusinessConfiguration.cs
            │   ├── ListingConfiguration.cs
            │   └── BookingConfiguration.cs
            ├── Converters/
            ├── Interceptors/                # e.g. sets updated_at on save
            └── Migrations/
                ├── 20261026090000_InitialCoreSchema.cs              # Migration 1, Phases 2–4
                ├── 20261026090000_InitialCoreSchema.Designer.cs
                ├── OnePondyDbContextModelSnapshot.cs
                ├── 20261201090000_AddListingsAndBookings.cs         # Migration 2, Phase 6
                ├── 20261201090000_AddListingsAndBookings.Designer.cs
                ├── 20261215090000_AddPaymentsAndLedger.cs           # Migration 3, Phase 7
                └── ...
```

The timestamps and filenames above are examples. Let EF Core generate the actual names and files. Do not manually create fake migration designer files or edit the model snapshot by hand.

### 18.2 Configure EF Core with PostgreSQL

Use compatible versions of:
- `Microsoft.EntityFrameworkCore`
- `Microsoft.EntityFrameworkCore.Design` (in the startup project, `OnePondy.Api`)
- `Npgsql.EntityFrameworkCore.PostgreSQL`
- `Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite` (PostGIS types)
- `EFCore.NamingConventions` (`snake_case` names)
- `dotnet-ef` tool, pinned in a local tool manifest

Keep EF Core and Npgsql provider major versions compatible with the selected .NET target. Confirm the currently supported package versions during setup.

Example commands from the repository root, adjusting paths to the actual solution:

```powershell
# Pin dotnet-ef in the repository. After cloning, run: dotnet tool restore
dotnet new tool-manifest
dotnet tool install dotnet-ef

dotnet add backend/src/OnePondy.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add backend/src/OnePondy.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite
dotnet add backend/src/OnePondy.Infrastructure package EFCore.NamingConventions

# dotnet-ef requires the Design package in the startup project. It is a
# development dependency, so a reference in Infrastructure does not flow to Api.
dotnet add backend/src/OnePondy.Api package Microsoft.EntityFrameworkCore.Design

dotnet ef migrations add InitialCoreSchema `
  --project backend/src/OnePondy.Infrastructure `
  --startup-project backend/src/OnePondy.Api `
  --context OnePondyDbContext `
  --output-dir Persistence/Migrations
```

If the packages are already installed, do not install duplicate versions. Ensure the API project references Infrastructure and that the API startup configuration can construct the `OnePondyDbContext`:

```csharp
builder.Services.AddDbContext<OnePondyDbContext>(options =>
    options.UseNpgsql(
            builder.Configuration.GetConnectionString("OnePondy"),
            npgsql => npgsql.UseNetTopologySuite())
        .UseSnakeCaseNamingConvention());
```

### 18.3 Apply a migration locally

For local development only, after reviewing the generated migration:

```powershell
dotnet ef database update `
  --project backend/src/OnePondy.Infrastructure `
  --startup-project backend/src/OnePondy.Api `
  --context OnePondyDbContext
```

Do not apply schema changes to production automatically during every API startup. For production, generate and review an idempotent SQL script or migration bundle, back up the database, test the migration against staging and apply it through a controlled deployment process.

Generate a reviewable SQL script:

```powershell
dotnet ef migrations script `
  --project backend/src/OnePondy.Infrastructure `
  --startup-project backend/src/OnePondy.Api `
  --context OnePondyDbContext `
  --output deploy/iis/OnePondy.Migrations.sql
```

For a deployment pipeline, generate an idempotent script when appropriate:

```powershell
dotnet ef migrations script --idempotent `
  --project backend/src/OnePondy.Infrastructure `
  --startup-project backend/src/OnePondy.Api `
  --context OnePondyDbContext `
  --output deploy/iis/OnePondy.Migrations.Idempotent.sql
```

Treat generated scripts as deployment artifacts: review destructive operations, test against a restored staging backup, and confirm the database role has only the permissions required for the migration.

### 18.4 Migration rules

1. Change the EF Core entity/configuration first, then generate a migration.
2. Review every `Up` and `Down` method.
3. Check in migration files and the model snapshot together.
4. Never edit a migration that has already been applied to a shared environment; create a corrective migration instead.
5. Do not delete migration history to resolve a schema mismatch.
6. Back up important data before destructive changes.
7. Prefer expand-and-contract migrations for changes requiring zero/low downtime.
8. Use separate connection strings for local, staging and production.
9. Never commit production connection strings.
10. Test both migration application and application behavior against the migrated schema.

### 18.5 SQL scripts vs EF Core migrations

Use EF Core migrations for normal schema evolution. Use reviewed SQL scripts for database extensions such as PostGIS, carefully optimized indexes or DBA-reviewed operations where EF Core generation is insufficient. Document which mechanism owns each operation to avoid drift:

| Operation | Owner |
|---|---|
| Tables, columns, keys, check constraints, indexes (including partial and GiST via `HasFilter` / `HasMethod`) | EF Core migrations |
| Generated `location` columns | EF Core (`HasComputedColumnSql(..., stored: true)`) |
| `DEFERRABLE` constraints and the `customer_history` view | `migrationBuilder.Sql(...)` inside the EF migration that owns them |
| PostGIS extension | DBA script in `database/scripts/`, run before the first migration, so the migration role does not need the elevated rights that creating an extension usually requires |
| Append-only grants on history tables | DBA script in `database/scripts/` |

---

## 19. Additional Acceptance Criteria for Database Work

A database-related feature is not complete until:

- Entities and EF Core configurations are reviewed.
- A migration is generated and committed.
- Foreign keys, unique constraints, checks and indexes are included where required.
- Migration applies successfully to a clean local database.
- Existing data is preserved during non-destructive upgrades.
- Seed scripts are repeatable and do not contain real user data.
- Queries are parameterized and checked for reasonable performance.
- Transaction boundaries and concurrency behavior are tested.
- Migration SQL is reviewed before staging or production deployment.
- Rollback or forward-recovery steps are documented.

---

## 20. Non-Functional Requirements

These are proposed targets. Confirm or replace them under D15 before Phase 1 ends; the release readiness gate (§10) tests against them.

| Area | Proposed target |
|---|---|
| API latency | p95 under 500 ms for read endpoints and under 1 s for writes, measured at the server. |
| App start | Cold start to an interactive Home screen in under 3 s on a mid-range Android phone. |
| Stability | At least 99.5% crash-free sessions. |
| Availability | 99.5% monthly for the API (single IIS server). |
| Data recovery | RPO of 15 minutes or less (WAL archiving); RTO of 4 hours or less, proven by a recovery drill before each release. |
| Supported devices | Android: the minimum version supported by the pinned Expo SDK. iOS: per D4. |
| Network | Core browsing stays usable on a slow mobile connection; images are sized for the screen density. |
| Capacity | Size Release 1 from a written usage forecast (monthly active users, peak concurrent sessions) and revisit it before Release 2. |
| Accessibility | WCAG 2.2 AA criteria that apply to mobile; works with TalkBack and VoiceOver and with system font scaling up to 200%. |
| Observability | Structured logs with correlation IDs across app, API and worker; alerts on error rate, latency, job backlog and failed webhooks. |

---

## 21. India Compliance Checklist

These are pointers for legal and tax review, not legal advice. Confirm each item with a lawyer or chartered accountant before the release it applies to.

| Area | Item | Needed by |
|---|---|---|
| Privacy | Digital Personal Data Protection Act 2023 and its Rules: notice and consent, purpose limitation, deletion on request, breach notification, grievance contact. | Release 1 |
| App stores | Account deletion in the app and on the web; Google Play Data safety form; App Store privacy details. | Release 1 |
| SMS | TRAI DLT registration of the sender ID and OTP message templates. | Release 1, if phone OTP is chosen (D1) |
| Content | Licences and attribution for photos; map provider terms, including limits on storing the provider's place data. | Release 1 |
| E-commerce | Consumer Protection (E-Commerce) Rules 2020: grievance officer, partner/seller details, clear price and cancellation disclosure, no manipulated reviews. | Release 2 |
| Payments | Use an RBI-authorised payment aggregator; OnePondy does not hold customer funds outside the aggregator's settlement process. | Release 2 |
| Tax | GST registration as an e-commerce operator; tax collected at source on partner sales (CGST Act Section 52); platform liability for GST on restaurant and passenger transport services booked through it (Section 9(5)). | Release 2 (TCS); Releases 3–4 (Section 9(5)) |
| Partners | Partner agreements covering commission, settlement, cancellations, refunds and liability. | Release 2 |
| Food | FSSAI licence number displayed for every restaurant; food safety responsibilities in partner agreements. | Release 3 |
| Transport | MoRTH Motor Vehicle Aggregator Guidelines as adopted in Puducherry; aggregator licence from the transport authority; driver verification and insurance. | Release 4 |

---

## 22. Open Decisions Register

Every decision has an owner and a phase by which it must be made. Implementation that depends on an open decision waits for it, or is built behind an interface that keeps the choice open (§15, item 16).

| ID | Decision | Options | Recommendation | Needed by | Status |
|---|---|---|---|---|---|
| D1 | Login method | Mobile number + OTP; email + password; social sign-in | Mobile OTP first, email as a secondary option | Phase 2 | Decided 2026-10-26: passwordless one-time codes by SMS (Indian +91 numbers at first, to limit SMS cost and fraud) or by email; no passwords are stored |
| D2 | Token strategy | ASP.NET Core Identity bearer tokens; JWT access tokens with rotating refresh tokens; OpenIddict | Short-lived JWT access tokens with rotating, server-tracked refresh tokens | Phase 2 | Decided 2026-10-26: 15-minute JWT access tokens; 30-day refresh tokens stored hashed and rotated on every use; reuse of a rotated token revokes the session |
| D3 | Payment gateway and methods | Indian payment aggregators (for example Razorpay, Cashfree, PayU); UPI, cards, net banking, wallets, cash | A gateway with UPI, cards and signed webhooks; decide cash on delivery per restaurant | Phase 7 | Decided 2026-10-29: Razorpay with UPI, cards and net banking; no wallets; cash on delivery left to Phase 8. The customer pays after the partner accepts a booking (proposed 30-minute payment window, to be confirmed). Payments stay in sandbox until legal review and an end-to-end settlement test are complete. See `docs/phase-7-plan.md`. |
| D4 | iOS in Release 1 | Android only; Android and iOS | Android first; add iOS once EAS Build and an Apple Developer account are set up | Phase 0 | Open |
| D5 | AI provider | Hosted LLM APIs | A provider with structured output, called from the server only | Phase 5 | Implemented 2026-10-28 with Google Gemini behind `IItineraryPlanner`, called from the server only, with a per-user and per-day cap; the owner has not yet confirmed the provider, and replacing it touches only the Infrastructure implementation |
| D6 | Map provider | Google Maps Platform; Mapbox; OpenStreetMap-based | Choose on cost and data-storage terms | Phase 1 | Decided 2026-10-26 for now: list only, with no map SDK and no API key. A place opens the device's Maps app for directions, and the nearby list sorts by straight-line distance (computed in memory). A map view can be added later once a provider is chosen on cost and storage terms |
| D7 | Push provider | Expo push service; Firebase Cloud Messaging and APNs directly | Expo push service unless a direct FCM feature is needed | Phase 7 | Decided 2026-10-29: Expo push service. |
| D8 | Media storage | Windows file share served by IIS; S3-compatible object storage with a CDN | Object storage with a CDN, behind `IMediaStorage` | Phase 3 | Decided 2026-10-26: start behind `IMediaStorage` with a local-disk implementation (`LocalMediaStorage`), served by the API; move to S3-compatible storage with a CDN later without changing callers |
| D9 | Crash reporting and analytics | Sentry; Firebase Crashlytics and Analytics; others | — | Phase 1 | Open |
| D10 | Supported languages | English, Tamil, French; add Hindi; add Telugu or Malayalam | English and Tamil at launch; consider Hindi for domestic tourists | Phase 1 | Open |
| D11 | Content sourcing and photos | In-house team; partners; licensed stock; commissioned photography | — | Phase 3 | Decided 2026-10-26: kept open on sourcing; the schema and console support any source through generic licence, attribution and source-link fields on each photo, so the decision does not block implementation |
| D12 | Hosting topology and secrets | Single server or separate database server; environment variables or a secrets vault | Separate database server; secrets in a vault or application pool environment variables | First deployment | Open |
| D13 | Shopping scope | Discovery-only listings; ordering from shops | Discovery-only until food ordering is stable | Phase 6 | Applied 2026-10-29 as the recommendation: a shop listing is discovery-only and can never be bookable (a database check enforces it). **Owner to confirm one inconsistency:** Phase 6 says "shops and services are discovery-only", but the reference schema (§17.3) lets `service` listings be bookable (`ck_listing_bookable_type`, `booking_type`). The schema was followed, so a service can take bookings; restricting that is a one-line change in the listing validator and the check constraint |
| D14 | Money representation | `numeric(12,2)`; integer paise | `numeric(12,2)` in INR | — | Decided in v1.1 |
| D15 | Non-functional targets | Values proposed in §20 | — | Phase 1 | Open |
| D16 | Document owner and approvers | — | — | Now | Open |
| D17 | App identifiers (Android package, iOS bundle ID) | `com.onepondy.customer` (provisional, used since Phase 0); a domain-based ID such as `in.<your-domain>.onepondy` | Use a reverse domain you own; cannot change after the first store release | Before the first store upload | Open |
| D18 | Brand identity: logo, app icon, splash artwork, typeface | Placeholders since Phase 1: a sun-over-waves mark on brand teal, and Plus Jakarta Sans (SIL Open Font License); final brand design | Commission final artwork; keep the token structure so it swaps in one place | Before Release 1 | Open |
| D19 | SMS and email provider for one-time codes | An India SMS aggregator with DLT-registered templates (for example MSG91, Gupshup, Twilio) plus a transactional email service (for example Amazon SES, SendGrid, Postmark) | One SMS provider that supports DLT templates and one email provider, behind the existing `IOtpSender` interface | Before real users sign in (Phase 2 release) | SMS decided 2026-10-29: MSG91 for production (needs DLT registration of the sender ID and templates; the sender is not built yet) and Twilio Verify for development and test servers (built; `Auth:Otp:Delivery=TwilioVerify`, or `Twilio` for plain SMS). Email provider still open. Until MSG91 is live, production reports "codes unavailable". |
| D20 | Web console staff sign-in method | Passwordless one-time codes by email (reusing D1's approach); email and password with authenticator-app two-factor | Passwordless codes by email; an administrator assigns roles | Phase 3 | Decided 2026-10-26: one-time codes sent to the staff member's own email (`/api/v1/admin/auth`), reusing the Phase 2 OTP and session machinery; depends on D19 for real email delivery in production, same as customer sign-in by email |
| D21 | Commission and partner payout model | Razorpay Route (split settlement at payment time); OnePondy collects and pays partners in periodic settlements | Route, so OnePondy does not hold partner money (§21), subject to legal review; commission percentage set by the owner | Phase 7 | Open: a provisional 10% (`Payments:CommissionPercent`) is booked to the ledger for sandbox testing only. Partner statements (draft, approved, paid) are built from the ledger, and an administrator records the bank transfer (step 7.9, 2026-10-29); Razorpay Route payouts are not built, and the rate is still the owner's to set. |
| D22 | Refund rule on cancellation | Full refund before the free-cancellation deadline and the policy's refund percentage after it; always full for rejection, partner cancellation and a payment that arrives after the hold expired | The snapshotted listing policy, with 100% for the cases the customer did not cause | Phase 7 | Open: implemented provisionally in step 7.5 (2026-10-29) and switched off by default (`Payments:RefundsEnabled`); while off, a paid booking cannot be cancelled. The owner still has to approve the policy before it is enabled anywhere real. |
