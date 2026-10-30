import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { act, screen, userEvent } from '@testing-library/react-native';

import { aBooking } from '../../../test-utils/bookingFixture';
import { orderId, restaurantId } from '../../../test-utils/foodFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { guestState, signedInState } from '../../../test-utils/signedIn';
import { foodApi } from '../../food/api/foodApi';
import { bookingsApi } from '../api/bookingsApi';
import { BookingsScreen } from './BookingsScreen';

// Captures the focus callbacks (bookings and food orders each register one) so a test can bring the screen back into focus.
const mockFocusCallbacks: (() => void)[] = [];
const mockNavigate = jest.fn();

jest.mock('@react-navigation/native', () => {
  const { useEffect } = jest.requireActual('react');
  return {
    useNavigation: () => ({ navigate: mockNavigate }),
    useFocusEffect: (effect: () => void) => {
      mockFocusCallbacks.push(effect);
      useEffect(effect, [effect]);
    },
  };
});
jest.mock('../api/bookingsApi', () => ({ bookingsApi: { list: jest.fn() } }));
jest.mock('../../food/api/foodApi', () => ({ foodApi: { orders: jest.fn() } }));

const list = bookingsApi.list as jest.Mock;
const orders = foodApi.orders as jest.Mock;

const summary = (overrides = {}) => {
  const { id, listingTitle, businessName, status } = aBooking(overrides);
  return { id, listingTitle, businessName, status };
};

const orderSummary = (overrides = {}) => ({
  id: orderId,
  businessId: restaurantId,
  restaurantName: 'Spice Route',
  status: 'awaitingPayment',
  expiresAt: null,
  itemCount: 3,
  totalAmount: 339.25,
  currency: 'INR',
  createdAt: '2026-10-30T10:00:00Z',
  ...overrides,
});

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
    orders.mockReset();
    orders.mockResolvedValue([]);
    mockNavigate.mockReset();
    mockFocusCallbacks.length = 0;
  });

  it('does not ask for bookings or orders while the customer is signed out', async () => {
    await renderScreen(guestState);

    expect(list).not.toHaveBeenCalled();
    expect(orders).not.toHaveBeenCalled();
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
    await act(async () => mockFocusCallbacks.forEach((focused) => focused()));

    expect(await screen.findByText(/Lakeside Tours · Confirmed/)).toBeOnTheScreen();
    expect(list).toHaveBeenCalledTimes(2);
  });

  it('lists food orders below the bookings, with their status and total', async () => {
    list.mockResolvedValue([summary()]);
    orders.mockResolvedValue([orderSummary()]);

    await renderScreen(signedInState);

    expect(await screen.findByText('Food orders')).toBeOnTheScreen();
    expect(screen.getByText('3 items · Awaiting payment · ₹339.25')).toBeOnTheScreen();
    expect(screen.getByText(/Lakeside Tours/)).toBeOnTheScreen();
  });

  it('shows food orders instead of the empty message when there are no bookings', async () => {
    list.mockResolvedValue([]);
    orders.mockResolvedValue([orderSummary({ status: 'delivered', itemCount: 1 })]);

    await renderScreen(signedInState);

    expect(await screen.findByText('1 item · Delivered · ₹339.25')).toBeOnTheScreen();
    expect(screen.queryByText('No bookings yet')).not.toBeOnTheScreen();
  });

  it('shows the empty message only when there are neither bookings nor food orders', async () => {
    list.mockResolvedValue([]);

    await renderScreen(signedInState);

    expect(await screen.findByText('No bookings yet')).toBeOnTheScreen();
    expect(screen.queryByText('Food orders')).not.toBeOnTheScreen();
  });

  it('opens an order when it is tapped', async () => {
    list.mockResolvedValue([]);
    orders.mockResolvedValue([orderSummary()]);
    await renderScreen(signedInState);

    await userEvent.setup().press(await screen.findByRole('button', { name: /Spice Route/ }));

    expect(mockNavigate).toHaveBeenCalledWith('OrderDetail', { id: orderId });
  });

  it('says so when food orders cannot be loaded, without hiding the bookings', async () => {
    list.mockResolvedValue([summary()]);
    orders.mockRejectedValue(new Error('offline'));

    await renderScreen(signedInState);

    expect(await screen.findByText("Couldn't load your food orders.")).toBeOnTheScreen();
    expect(screen.getByText(/Lakeside Tours/)).toBeOnTheScreen();
  });
});
