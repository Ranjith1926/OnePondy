import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent } from '@testing-library/react-native';

import { renderWithProviders } from '../../../test-utils/render';
import { guestState } from '../../../test-utils/signedIn';
import { placesApi, type PlaceDetail, type PlaceSummary } from '../api/placesApi';
import { ExploreScreen } from './ExploreScreen';
import { PlaceDetailScreen } from './PlaceDetailScreen';

const mockNavigate = jest.fn();
const mockGoBack = jest.fn();

jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ navigate: mockNavigate, goBack: mockGoBack }),
}));
jest.mock('../api/placesApi', () => ({
  ...jest.requireActual('../api/placesApi'),
  placesApi: { list: jest.fn(), get: jest.fn() },
}));

const list = placesApi.list as jest.Mock;
const get = placesApi.get as jest.Mock;

const aSummary = (overrides: Partial<PlaceSummary> = {}): PlaceSummary => ({
  id: 'p1',
  slug: 'rock-beach',
  name: 'Rock Beach',
  categoryCode: 'beach',
  latitude: 11.9341,
  longitude: 79.8302,
  coverImage: null,
  distanceKm: null,
  ...overrides,
});

const aDetail = (overrides: Partial<PlaceDetail> = {}): PlaceDetail => ({
  id: 'p1',
  slug: 'rock-beach',
  language: 'en',
  name: 'Rock Beach',
  categoryCode: 'beach',
  description: null,
  visitorGuidance: null,
  address: null,
  latitude: 11.9341,
  longitude: 79.8302,
  openingHours: null,
  entryFees: [],
  images: [],
  verifiedAt: null,
  lastReviewedAt: null,
  ...overrides,
});

const page = (items: PlaceSummary[]) => ({
  items,
  page: 1,
  pageSize: 50,
  totalCount: items.length,
});

async function renderInClient(ui: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(<QueryClientProvider client={client}>{ui}</QueryClientProvider>, {
    initialState: guestState,
  });
}

beforeEach(() => {
  list.mockReset();
  get.mockReset();
  mockNavigate.mockReset();
  mockGoBack.mockReset();
});

describe('<ExploreScreen /> map view', () => {
  it('starts as a list and shows no map until Map is chosen', async () => {
    list.mockResolvedValue(page([aSummary()]));
    await renderInClient(<ExploreScreen />);

    expect(await screen.findByText('Rock Beach')).toBeOnTheScreen();
    expect(screen.queryByTestId('tile-map')).not.toBeOnTheScreen();
  });

  it('pins the places that have a position, and shows a card for the pin that is pressed', async () => {
    list.mockResolvedValue(
      page([
        aSummary(),
        aSummary({
          id: 'p2',
          slug: 'no-position',
          name: 'Hidden Temple',
          latitude: null,
          longitude: null,
        }),
      ]),
    );
    const user = userEvent.setup();
    await renderInClient(<ExploreScreen />);
    await screen.findByText('Rock Beach');

    await user.press(screen.getByRole('button', { name: 'Map' }));

    expect(screen.getByLabelText('Map of places')).toBeOnTheScreen();
    expect(screen.getByTestId('map-marker-p1')).toBeOnTheScreen();
    expect(screen.queryByTestId('map-marker-p2')).not.toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Rock Beach, Beach' })).not.toBeOnTheScreen();

    await user.press(screen.getByTestId('map-marker-p1'));
    await user.press(await screen.findByRole('button', { name: 'Rock Beach, Beach' }));

    expect(mockNavigate).toHaveBeenCalledWith('PlaceDetail', { idOrSlug: 'rock-beach' });
  });

  it('says so when none of the places has a position', async () => {
    list.mockResolvedValue(page([aSummary({ latitude: null, longitude: null })]));
    const user = userEvent.setup();
    await renderInClient(<ExploreScreen />);
    await screen.findByText('Rock Beach');

    await user.press(screen.getByRole('button', { name: 'Map' }));

    expect(screen.getByText(/None of these places has a location/)).toBeOnTheScreen();
    expect(screen.queryByTestId('tile-map')).not.toBeOnTheScreen();
  });

  it('goes back to the list when List is chosen again', async () => {
    list.mockResolvedValue(page([aSummary()]));
    const user = userEvent.setup();
    await renderInClient(<ExploreScreen />);
    await screen.findByText('Rock Beach');

    await user.press(screen.getByRole('button', { name: 'Map' }));
    await user.press(screen.getByRole('button', { name: 'List' }));

    expect(screen.queryByTestId('tile-map')).not.toBeOnTheScreen();
    expect(screen.getByText('Rock Beach')).toBeOnTheScreen();
  });
});

describe('<PlaceDetailScreen /> map', () => {
  const route = { key: 'k', name: 'PlaceDetail', params: { idOrSlug: 'rock-beach' } } as never;

  it('shows a small map with one pin when the place has a position', async () => {
    get.mockResolvedValue(aDetail());
    await renderInClient(<PlaceDetailScreen route={route} />);

    expect(await screen.findByLabelText('Map showing where Rock Beach is')).toBeOnTheScreen();
    expect(screen.getByTestId('map-marker-p1')).toBeOnTheScreen();
    // A picture only: it sits in a scrolling page, so it has no zoom buttons to get in the way.
    expect(screen.queryByRole('button', { name: 'Zoom in' })).not.toBeOnTheScreen();
  });

  it('shows no map when the place has no position', async () => {
    get.mockResolvedValue(aDetail({ latitude: null, longitude: null }));
    await renderInClient(<PlaceDetailScreen route={route} />);

    expect(await screen.findByRole('header', { name: 'Rock Beach' })).toBeOnTheScreen();
    expect(screen.queryByTestId('tile-map')).not.toBeOnTheScreen();
  });
});
