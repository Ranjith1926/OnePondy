import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, screen } from '@testing-library/react-native';

import { aBooking } from '../../../test-utils/bookingFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { guestState, signedInState } from '../../../test-utils/signedIn';
import { bookingsApi } from '../api/bookingsApi';
import { BookingsScreen } from './BookingsScreen';

// Captures the focus callback so a test can bring the screen back into focus after the first read.
let focusCallback: (() => void) | undefined;

jest.mock('@react-navigation/native', () => {
  const { useEffect } = jest.requireActual('react');
  return {
    useNavigation: () => ({ navigate: jest.fn() }),
    useFocusEffect: (effect: () => void) => {
      focusCallback = effect;
      useEffect(effect, [effect]);
    },
  };
});
jest.mock('../api/bookingsApi', () => ({ bookingsApi: { list: jest.fn() } }));

const list = bookingsApi.list as jest.Mock;

const summary = (overrides = {}) => {
  const { id, listingTitle, businessName, status } = aBooking(overrides);
  return { id, listingTitle, businessName, status };
};

async function renderScreen(state: typeof signedInState) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: 30_000 } },
  });
  await renderWithProviders(
    <QueryClientProvider client={client}>
      <BookingsScreen />
    </QueryClientProvider>,
    { initialState: state },
  );
}

describe('<BookingsScreen />', () => {
  beforeEach(() => {
    list.mockReset();
    focusCallback = undefined;
  });

  it('does not ask for bookings while the customer is signed out', async () => {
    await renderScreen(guestState);

    expect(list).not.toHaveBeenCalled();
    expect(screen.getByText('Sign in to see your bookings')).toBeOnTheScreen();
  });

  it('lists the bookings of a signed-in customer', async () => {
    list.mockResolvedValue([summary()]);

    await renderScreen(signedInState);

    expect(await screen.findByText(/Lakeside Tours · Awaiting payment/)).toBeOnTheScreen();
    expect(list).toHaveBeenCalledTimes(1);
  });

  it('shows a booking that changed elsewhere when the screen comes back into focus', async () => {
    list.mockResolvedValueOnce([summary({ status: 'awaitingPayment' })]);
    await renderScreen(signedInState);
    expect(await screen.findByText(/Awaiting payment/)).toBeOnTheScreen();

    // The partner accepts, the customer pays elsewhere: the next read says confirmed.
    list.mockResolvedValueOnce([summary({ status: 'confirmed' })]);
    await act(async () => focusCallback?.());

    expect(await screen.findByText(/Lakeside Tours · Confirmed/)).toBeOnTheScreen();
    expect(list).toHaveBeenCalledTimes(2);
  });
});
