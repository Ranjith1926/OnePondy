import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent } from '@testing-library/react-native';

import { ApiError } from '../../../api/apiErrors';
import { anOrder, orderId } from '../../../test-utils/foodFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { signedInState } from '../../../test-utils/signedIn';
import { foodApi, type OrderDetail } from '../api/foodApi';
import { OrderDetailScreen } from './OrderDetailScreen';

const mockGoBack = jest.fn();

jest.mock('@react-navigation/native', () => {
  const { useEffect } = jest.requireActual('react');
  return {
    useNavigation: () => ({ goBack: mockGoBack }),
    // A screen is focused as soon as it is shown.
    useFocusEffect: (effect: () => void) => useEffect(effect, [effect]),
  };
});
jest.mock('../api/foodApi', () => ({
  foodApi: {
    order: jest.fn(),
    cancel: jest.fn(),
    startPayment: jest.fn(),
    refreshPayment: jest.fn(),
  },
}));

const order = foodApi.order as jest.Mock;
const cancel = foodApi.cancel as jest.Mock;

const receipt = {
  amount: 339.25,
  currency: 'INR',
  paidAt: '2026-10-30T10:10:00Z',
  reference: 'pay_FOOD123',
};

async function renderScreen() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const route = { key: 'k', name: 'OrderDetail', params: { id: orderId } } as never;
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <OrderDetailScreen route={route} />
    </QueryClientProvider>,
    { initialState: signedInState },
  );
}

const ordered = (overrides: Partial<OrderDetail>) => order.mockResolvedValue(anOrder(overrides));

beforeEach(() => {
  order.mockReset();
  cancel.mockReset();
  mockGoBack.mockReset();
});

describe('<OrderDetailScreen />', () => {
  it('asks the customer to pay once the restaurant accepts, and lets them cancel', async () => {
    ordered({ expiresAt: new Date(Date.now() + 8 * 60_000).toISOString() });

    await renderScreen();

    expect(await screen.findByTestId('pay-button')).toBeOnTheScreen();
    expect(
      screen.getByText('The restaurant accepted your order. Pay now to confirm it.'),
    ).toBeOnTheScreen();
    expect(screen.getByText('Awaiting payment')).toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Cancel order' })).toBeOnTheScreen();
    expect(screen.queryByTestId('receipt')).not.toBeOnTheScreen();
  });

  it('says the restaurant has yet to answer, and offers no payment, while the order is placed', async () => {
    ordered({ status: 'placed' });

    await renderScreen();

    expect(await screen.findByText('Waiting for the restaurant')).toBeOnTheScreen();
    expect(screen.queryByTestId('pay-button')).not.toBeOnTheScreen();
    expect(screen.getByRole('button', { name: 'Cancel order' })).toBeOnTheScreen();
  });

  it('shows the receipt, the whole price and the licence number once the order is paid', async () => {
    ordered({ status: 'confirmed', payment: receipt, expiresAt: null });

    await renderScreen();

    expect(await screen.findByTestId('receipt')).toBeOnTheScreen();
    expect(screen.getByText(/^Paid and confirmed\./)).toBeOnTheScreen();
    expect(screen.getByText('Payment reference: pay_FOOD123')).toBeOnTheScreen();
    expect(screen.queryByTestId('pay-button')).not.toBeOnTheScreen();
    expect(screen.getByTestId('price-breakdown')).toBeOnTheScreen();
    expect(screen.getByText('Restaurant FSSAI licence: 12345678901234')).toBeOnTheScreen();
    expect(screen.getByText('12 Rue Suffren, White Town')).toBeOnTheScreen();
    expect(screen.getByText('White Town')).toBeOnTheScreen();
  });

  it('never says "paid" for a confirmed order that has no payment (online payment was off)', async () => {
    ordered({ status: 'confirmed', payment: null, expiresAt: null });

    await renderScreen();

    expect(
      await screen.findByText(/^Confirmed\. The restaurant will start cooking soon/),
    ).toBeOnTheScreen();
    expect(screen.queryByText(/Paid and confirmed/)).not.toBeOnTheScreen();
    expect(screen.queryByTestId('receipt')).not.toBeOnTheScreen();
  });

  it('shows when it should be ready while it is being cooked, and offers no cancel', async () => {
    ordered({
      status: 'preparing',
      canCancel: false,
      payment: receipt,
      estimatedReadyAt: '2026-10-30T10:40:00Z',
    });

    await renderScreen();

    expect(await screen.findByText('Being prepared')).toBeOnTheScreen();
    expect(screen.getByText(/Estimated ready by/)).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Cancel order' })).not.toBeOnTheScreen();
  });

  it('shows the refund, and offers no cancel, while a refund is on its way', async () => {
    ordered({
      status: 'refundPending',
      canCancel: false,
      payment: receipt,
      refunds: [
        {
          amount: 339.25,
          reason: 'customerCancelled',
          status: 'pending',
          requestedAt: '2026-10-30T11:00:00Z',
          processedAt: null,
        },
      ],
    });

    await renderScreen();

    expect(await screen.findByTestId('refund')).toBeOnTheScreen();
    expect(screen.getByText('Your refund of ₹339.25 is on its way.')).toBeOnTheScreen();
    expect(screen.getByText('Refund in progress')).toBeOnTheScreen();
    expect(screen.queryByRole('button', { name: 'Cancel order' })).not.toBeOnTheScreen();
  });

  it('explains a declined order and says nothing was charged', async () => {
    ordered({ status: 'rejected', canCancel: false, closedReason: 'Out of batter' });

    await renderScreen();

    expect(await screen.findByText('Declined by the restaurant')).toBeOnTheScreen();
    expect(screen.getByText(/You haven't been charged/)).toBeOnTheScreen();
    expect(screen.getByText('Reason: Out of batter')).toBeOnTheScreen();
  });

  it('cancels with the order’s version after the customer confirms, and warns about the refund when paid', async () => {
    ordered({ status: 'confirmed', payment: receipt, version: 7 });
    cancel.mockResolvedValue(
      anOrder({ status: 'refundPending', canCancel: false, payment: receipt }),
    );
    const user = userEvent.setup();
    await renderScreen();

    await user.press(await screen.findByRole('button', { name: 'Cancel order' }));
    expect(screen.getByText(/You'll get a full refund/)).toBeOnTheScreen();
    await user.press(screen.getAllByRole('button', { name: 'Cancel order' }).at(-1)!);

    expect(cancel).toHaveBeenCalledWith(orderId, 7, undefined);
  });

  it('keeps the order when the customer changes their mind', async () => {
    ordered({ status: 'placed' });
    const user = userEvent.setup();
    await renderScreen();

    await user.press(await screen.findByRole('button', { name: 'Cancel order' }));
    await user.press(screen.getByRole('button', { name: 'Keep order' }));

    expect(cancel).not.toHaveBeenCalled();
  });

  it('says so when cancelling a paid order is not available yet', async () => {
    ordered({ status: 'confirmed', payment: receipt });
    cancel.mockRejectedValue(new ApiError(503, 'refunds_not_enabled'));
    const user = userEvent.setup();
    await renderScreen();

    await user.press(await screen.findByRole('button', { name: 'Cancel order' }));
    await user.press(screen.getAllByRole('button', { name: 'Cancel order' }).at(-1)!);

    expect(
      await screen.findByText(/Cancelling a paid order isn't available yet/),
    ).toBeOnTheScreen();
  });

  it('says so, and lets the customer retry, when the order cannot be loaded', async () => {
    order.mockRejectedValueOnce(new Error('offline'));
    await renderScreen();
    expect(await screen.findByText("Couldn't load this order")).toBeOnTheScreen();

    ordered({ status: 'placed' });
    await userEvent.setup().press(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Waiting for the restaurant')).toBeOnTheScreen();
  });
});
