import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react-native';

import { aBooking, bookingId } from '../../../test-utils/bookingFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { bookingsApi } from '../api/bookingsApi';
import { BookingDetailScreen } from './BookingDetailScreen';

jest.mock('@react-navigation/native', () => ({ useNavigation: () => ({ goBack: jest.fn() }) }));
jest.mock('../api/bookingsApi', () => ({ bookingsApi: { get: jest.fn(), cancel: jest.fn() } }));
jest.mock('../../payments/api/paymentsApi', () => ({
  paymentsApi: { start: jest.fn(), refresh: jest.fn() },
}));

const get = bookingsApi.get as jest.Mock;

async function renderScreen() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const route = { key: 'k', name: 'BookingDetail', params: { id: bookingId } } as never;
  await renderWithProviders(
    <QueryClientProvider client={client}>
      <BookingDetailScreen route={route} />
    </QueryClientProvider>,
  );
}

describe('<BookingDetailScreen /> and payment', () => {
  beforeEach(() => get.mockReset());

  it('asks the customer to pay while the booking waits for payment, and lets them cancel', async () => {
    get.mockResolvedValue(
      aBooking({ expiresAt: new Date(Date.now() + 20 * 60_000).toISOString() }),
    );

    await renderScreen();

    expect(await screen.findByTestId('pay-button')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Cancel booking' })).toBeOnTheScreen();
    expect(screen.queryByTestId('receipt')).not.toBeOnTheScreen();
  });

  it('shows the receipt, and no pay button, once the booking is confirmed', async () => {
    get.mockResolvedValue(
      aBooking({
        status: 'confirmed',
        payment: {
          amount: 1000,
          currency: 'INR',
          paidAt: '2026-10-29T10:10:00Z',
          reference: 'pay_ABC123',
        },
      }),
    );

    await renderScreen();

    expect(await screen.findByTestId('receipt')).toBeOnTheScreen();
    expect(screen.queryByTestId('pay-button')).not.toBeOnTheScreen();
  });

  it('shows the refund, and offers no cancel, while a refund is pending', async () => {
    get.mockResolvedValue(
      aBooking({
        status: 'refundPending',
        payment: {
          amount: 1000,
          currency: 'INR',
          paidAt: '2026-10-29T10:10:00Z',
          reference: 'pay_ABC123',
        },
        refunds: [
          {
            amount: 500,
            reason: 'customerCancelled',
            status: 'pending',
            requestedAt: '2026-10-30T09:00:00Z',
            processedAt: null,
          },
        ],
      }),
    );

    await renderScreen();

    expect(await screen.findByTestId('refund')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Cancel booking' })).not.toBeOnTheScreen();
  });
});
