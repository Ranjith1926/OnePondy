import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react-native';

import { renderWithProviders } from '../../../test-utils/render';
import { signedInState } from '../../../test-utils/signedIn';
import { listingsApi, type ListingDetail, type ListingSummary } from '../api/listingsApi';
import { ListingDetailScreen } from './ListingDetailScreen';
import { ListingsScreen } from './ListingsScreen';

const mockNavigate = jest.fn();

jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ navigate: mockNavigate, goBack: jest.fn() }),
}));
jest.mock('../api/listingsApi', () => ({
  listingsApi: { list: jest.fn(), get: jest.fn(), availability: jest.fn() },
}));
// Reviews and the saved heart have their own tests; here only the price text matters.
jest.mock('../../reviews/components/PublicReviews', () => ({ PublicReviews: () => null }));
jest.mock('../hooks/useSavedListings', () => ({
  useIsListingSaved: () => false,
  useToggleSavedListing: () => ({ isPending: false, mutate: jest.fn() }),
}));

const list = listingsApi.list as jest.Mock;
const get = listingsApi.get as jest.Mock;
const availability = listingsApi.availability as jest.Mock;

const summary = (overrides: Partial<ListingSummary> = {}): ListingSummary => ({
  id: 'listing-1',
  businessName: 'Heritage Guest House',
  listingType: 'stay',
  title: 'Garden room, White Town',
  isBookable: true,
  basePrice: 2500,
  currency: 'INR',
  ...overrides,
});

const detail = (overrides: Partial<ListingDetail> = {}): ListingDetail => ({
  id: 'listing-1',
  businessId: 'business-1',
  businessName: 'Heritage Guest House',
  businessAddress: 'White Town',
  listingType: 'stay',
  title: 'Garden room, White Town',
  description: 'A quiet room with a garden.',
  isBookable: true,
  basePrice: 2500,
  currency: 'INR',
  cancellationPolicy: null,
  ...overrides,
});

function inClient(ui: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(<QueryClientProvider client={client}>{ui}</QueryClientProvider>, {
    initialState: signedInState,
  });
}

beforeEach(() => {
  list.mockReset();
  get.mockReset();
  availability.mockReset();
  availability.mockResolvedValue([]);
});

describe('prices on the listing screens', () => {
  it('shows a listing’s starting price in rupees, as the rest of the app does, in the list', async () => {
    list.mockResolvedValue({
      items: [
        summary(),
        summary({ id: 'listing-2', title: 'Sunset Kayak Tour', basePrice: 499.5 }),
      ],
      page: 1,
      pageSize: 20,
      totalCount: 2,
    });
    await inClient(<ListingsScreen route={{ params: { type: 'stay' } } as never} />);

    expect(await screen.findByText(/From ₹2,500/)).toBeOnTheScreen();
    expect(screen.getByText(/From ₹499\.50/)).toBeOnTheScreen();
    expect(screen.queryByText(/INR/)).not.toBeOnTheScreen();
  });

  it('shows the same on the detail page', async () => {
    get.mockResolvedValue(detail());
    await inClient(<ListingDetailScreen route={{ params: { id: 'listing-1' } } as never} />);

    expect(await screen.findByText('From ₹2,500')).toBeOnTheScreen();
    expect(screen.queryByText(/INR/)).not.toBeOnTheScreen();
  });

  it('shows no price for a listing without one', async () => {
    get.mockResolvedValue(detail({ basePrice: null, isBookable: false }));
    await inClient(<ListingDetailScreen route={{ params: { id: 'listing-1' } } as never} />);

    expect(await screen.findByText('Garden room, White Town')).toBeOnTheScreen();
    expect(screen.queryByText(/From/)).not.toBeOnTheScreen();
  });
});
