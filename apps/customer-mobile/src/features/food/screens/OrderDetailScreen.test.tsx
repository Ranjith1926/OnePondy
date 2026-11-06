import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent } from '@testing-library/react-native';
import { Linking } from 'react-native';

import { ApiError } from '../../../api/apiErrors';
import { anOrder, orderId } from '../../../test-utils/foodFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { signedInState } from '../../../test-utils/signedIn';
import { reviewsApi } from '../../reviews/api/reviewsApi';
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

jest.mock('../../reviews/api/reviewsApi', () => ({ reviewsApi: { mine: jest.fn() } }));

const order = foodApi.order as jest.Mock;
const mine = reviewsApi.mine as jest.Mock;
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
  mine.mockReset();
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

  describe('the delivery', () => {
    it('says it is looking for a driver while nobody has taken the order', async () => {
      ordered({
        status: 'readyForPickup',
        delivery: {
          status: 'open',
          driverFirstName: null,
          vehicleType: null,
          locationUpdatedAt: null,
        },
      });
      await renderScreen();

      expect(
        await screen.findByText('Looking for a driver to collect your order.'),
      ).toBeOnTheScreen();
    });

    it('names the driver and vehicle on the way to the restaurant', async () => {
      ordered({
        status: 'readyForPickup',
        delivery: {
          status: 'accepted',
          driverFirstName: 'Ravi',
          vehicleType: 'scooter',
          locationUpdatedAt: null,
        },
      });
      await renderScreen();

      expect(
        await screen.findByText('Ravi is on the way to the restaurant to collect your order.'),
      ).toBeOnTheScreen();
      expect(screen.getByText('On a scooter.')).toBeOnTheScreen();
    });

    it('says the driver has the order once it is on its way', async () => {
      ordered({
        status: 'outForDelivery',
        delivery: {
          status: 'pickedUp',
          driverFirstName: 'Meena',
          vehicleType: 'bicycle',
          locationUpdatedAt: '2026-10-30T10:20:00Z',
        },
      });
      await renderScreen();

      expect(
        await screen.findByText('Meena has your order and is on the way to you.'),
      ).toBeOnTheScreen();
      expect(screen.getByText('On a bicycle.')).toBeOnTheScreen();
    });

    it('lets the customer open the drivers position in Maps while the driver is bringing the order', async () => {
      const openURL = jest.spyOn(Linking, 'openURL').mockResolvedValue();
      ordered({
        status: 'outForDelivery',
        delivery: {
          status: 'pickedUp',
          driverFirstName: 'Meena',
          vehicleType: 'scooter',
          locationUpdatedAt: '2026-10-30T10:20:00Z',
          position: { latitude: 11.9341, longitude: 79.8302 },
        },
      });
      await renderScreen();

      await userEvent
        .setup()
        .press(await screen.findByRole('button', { name: 'See where your order is' }));

      expect(openURL).toHaveBeenCalledWith(
        'https://www.google.com/maps/search/?api=1&query=11.9341,79.8302',
      );
      expect(screen.getByText(/Driver's position updated at/)).toBeOnTheScreen();
      expect(screen.getByLabelText('Map showing where your order is now')).toBeOnTheScreen();
      expect(screen.getByTestId('map-marker-driver')).toBeOnTheScreen();
      openURL.mockRestore();
    });

    it('offers no map before the driver has collected the order, or when no position was reported', async () => {
      ordered({
        status: 'readyForPickup',
        delivery: {
          status: 'accepted',
          driverFirstName: 'Ravi',
          vehicleType: 'scooter',
          locationUpdatedAt: '2026-10-30T10:15:00Z',
          position: { latitude: 11.9, longitude: 79.8 },
        },
      });
      await renderScreen();
      expect(
        await screen.findByText('Ravi is on the way to the restaurant to collect your order.'),
      ).toBeOnTheScreen();
      expect(
        screen.queryByRole('button', { name: 'See where your order is' }),
      ).not.toBeOnTheScreen();
      expect(screen.queryByTestId('tile-map')).not.toBeOnTheScreen();
    });

    it('offers no map when the driver has not reported a position yet', async () => {
      ordered({
        status: 'outForDelivery',
        delivery: {
          status: 'pickedUp',
          driverFirstName: 'Meena',
          vehicleType: 'scooter',
          locationUpdatedAt: null,
          position: null,
        },
      });
      await renderScreen();

      expect(
        await screen.findByText('Meena has your order and is on the way to you.'),
      ).toBeOnTheScreen();
      expect(
        screen.queryByRole('button', { name: 'See where your order is' }),
      ).not.toBeOnTheScreen();
      expect(screen.queryByTestId('tile-map')).not.toBeOnTheScreen();
    });

    it('shows nothing about a delivery before the order is ready or after it arrives', async () => {
      ordered({ status: 'preparing', delivery: null });
      await renderScreen();
      expect(await screen.findByText('Being prepared', { exact: false })).toBeOnTheScreen();
      expect(screen.queryByText(/Looking for a driver/)).not.toBeOnTheScreen();
    });
  });

  describe('cash on delivery', () => {
    it('tells the customer what they will pay the driver, never that they have paid', async () => {
      ordered({ status: 'confirmed', paymentMethod: 'cashOnDelivery' });
      await renderScreen();

      expect(
        await screen.findByText(
          "Confirmed. You'll pay ₹339.25 in cash to the driver when it arrives.",
        ),
      ).toBeOnTheScreen();
      expect(
        screen.getByText('Cash on delivery: you pay ₹339.25 to the driver.'),
      ).toBeOnTheScreen();
      expect(screen.queryByText(/Paid and confirmed/)).not.toBeOnTheScreen();
    });

    it('does not say "you\'ll pay once it does" while the restaurant has yet to answer', async () => {
      ordered({ status: 'placed', paymentMethod: 'cashOnDelivery' });
      await renderScreen();

      expect(
        await screen.findByText(
          "The restaurant has a few minutes to accept. You'll pay ₹339.25 in cash to the driver when it arrives.",
        ),
      ).toBeOnTheScreen();
      expect(screen.queryByText(/pay once it does/)).not.toBeOnTheScreen();
    });

    it('shows no payment to make: nothing to pay online and no pay button', async () => {
      ordered({ status: 'confirmed', paymentMethod: 'cashOnDelivery' });
      await renderScreen();
      await screen.findByText(/in cash to the driver/);

      expect(screen.queryByRole('button', { name: /Pay/ })).not.toBeOnTheScreen();
    });

    it('says how much was paid in cash once it is delivered', async () => {
      ordered({
        status: 'delivered',
        paymentMethod: 'cashOnDelivery',
        cashCollected: 339.25,
        canCancel: false,
      });
      await renderScreen();

      expect(
        await screen.findByText('Delivered. You paid ₹339.25 in cash. Enjoy your meal.'),
      ).toBeOnTheScreen();
      expect(screen.getByText('Paid in cash to the driver: ₹339.25.')).toBeOnTheScreen();
    });

    it('cancelling a cash order does not promise a refund, because nothing was paid', async () => {
      ordered({ status: 'confirmed', paymentMethod: 'cashOnDelivery' });
      const user = userEvent.setup();
      await renderScreen();

      await user.press(await screen.findByRole('button', { name: 'Cancel order' }));

      expect(screen.queryByText(/full refund/)).not.toBeOnTheScreen();
    });
  });

  describe('reviews', () => {
    it('invites a review once the order is delivered', async () => {
      ordered({ status: 'delivered', canCancel: false });
      mine.mockResolvedValue([]);
      await renderScreen();

      expect(await screen.findByRole('button', { name: 'Write a review' })).toBeOnTheScreen();
    });

    it('offers no review, and does not ask for any, before the order is delivered', async () => {
      ordered({ status: 'outForDelivery', canCancel: false });
      await renderScreen();

      expect(await screen.findByText('Spice Route')).toBeOnTheScreen();
      expect(screen.queryByTestId('write-review')).not.toBeOnTheScreen();
      expect(mine).not.toHaveBeenCalled();
    });
  });
});
