import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent } from '@testing-library/react-native';
import { Text } from 'react-native';

import { ApiError } from '../../../api/apiErrors';
import {
  aCart,
  anOrder,
  aPrice,
  aQuote,
  dosaId,
  emptyCart,
  orderId,
  restaurantId,
} from '../../../test-utils/foodFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { guestState, signedInState } from '../../../test-utils/signedIn';
import { foodApi } from '../api/foodApi';
import { useCart } from '../cart/CartContext';
import { CheckoutScreen } from './CheckoutScreen';

const mockNavigate = jest.fn();
const mockGoBack = jest.fn();
const mockDispatch = jest.fn();

jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ navigate: mockNavigate, goBack: mockGoBack, dispatch: mockDispatch }),
  StackActions: {
    replace: (name: string, params: unknown) => ({ type: 'REPLACE', payload: { name, params } }),
  },
}));
jest.mock('../api/foodApi', () => ({ foodApi: { quote: jest.fn(), place: jest.fn() } }));

const quote = foodApi.quote as jest.Mock;
const place = foodApi.place as jest.Mock;

function CartSize() {
  const { cart } = useCart();
  return <Text testID="cart-size">{String(cart.lines.length)}</Text>;
}

async function renderScreen(state = signedInState, cart = aCart()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <CheckoutScreen />
      <CartSize />
    </QueryClientProvider>,
    { initialState: state, initialCart: cart },
  );
}

async function fillIn(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Delivery address'), '12 Rue Suffren, White Town');
  await user.type(screen.getByLabelText('Phone number'), '+919876500001');
}

beforeEach(() => {
  quote.mockReset();
  place.mockReset();
  mockNavigate.mockReset();
  mockGoBack.mockReset();
  mockDispatch.mockReset();
  quote.mockResolvedValue(aQuote());
});

describe('<CheckoutScreen />', () => {
  it('shows the server’s whole price breakdown before anything is placed', async () => {
    await renderScreen();

    expect(await screen.findByTestId('price-breakdown')).toBeOnTheScreen();
    expect(screen.getByText('2 × Masala dosa')).toBeOnTheScreen();
    expect(screen.getByText('₹240')).toBeOnTheScreen();
    expect(screen.getByText('GST')).toBeOnTheScreen();
    expect(screen.getByText('₹14.25')).toBeOnTheScreen();
    expect(screen.getByText('Delivery fee')).toBeOnTheScreen();
    expect(screen.getByText('Packaging')).toBeOnTheScreen();
    expect(screen.getByText('₹339.25')).toBeOnTheScreen();
    expect(screen.getByText(/only then are you charged/)).toBeOnTheScreen();
  });

  it('asks the server to price the cart by dish and quantity, never by price', async () => {
    await renderScreen();
    await screen.findByTestId('price-breakdown');

    expect(quote).toHaveBeenCalledWith({
      businessId: restaurantId,
      area: 'whiteTown',
      lines: [
        { menuItemId: dosaId, quantity: 2 },
        { menuItemId: 'a0000000-0000-4000-8000-000000000002', quantity: 1 },
      ],
    });
    expect(JSON.stringify(quote.mock.calls[0])).not.toMatch(/unitPrice|price/i);
  });

  it('needs an address and a phone number before the order can be placed', async () => {
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByTestId('price-breakdown');

    expect(screen.getByTestId('place-order')).toBeDisabled();
    await user.type(screen.getByLabelText('Delivery address'), '12 Rue Suffren');
    expect(screen.getByTestId('place-order')).toBeDisabled();
    await user.type(screen.getByLabelText('Phone number'), '+919876500001');

    expect(screen.getByTestId('place-order')).toBeEnabled();
  });

  it('places the order with the total the customer was shown, then opens it and empties the cart', async () => {
    place.mockResolvedValue(anOrder({ status: 'placed' }));
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByTestId('price-breakdown');
    await fillIn(user);

    await user.press(screen.getByTestId('place-order'));

    expect(place).toHaveBeenCalledTimes(1);
    expect(place.mock.calls[0][0]).toMatchObject({
      businessId: restaurantId,
      area: 'whiteTown',
      address: '12 Rue Suffren, White Town',
      contactPhone: '+919876500001',
      expectedTotal: aPrice.totalAmount,
    });
    expect(place.mock.calls[0][0].idempotencyKey).toEqual(expect.any(String));
    expect(mockDispatch).toHaveBeenCalledWith({
      type: 'REPLACE',
      payload: { name: 'OrderDetail', params: { id: orderId } },
    });
    expect(screen.getByTestId('cart-size')).toHaveTextContent('0');
  });

  it('uses the same idempotency key when the same order is tried again after a failure', async () => {
    place.mockRejectedValueOnce(new ApiError(503, 'http_503'));
    place.mockResolvedValueOnce(anOrder({ status: 'placed' }));
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByTestId('price-breakdown');
    await fillIn(user);

    await user.press(screen.getByTestId('place-order'));
    expect(await screen.findByText('Something went wrong. Please try again.')).toBeOnTheScreen();
    await user.press(screen.getByTestId('place-order'));

    expect(place).toHaveBeenCalledTimes(2);
    expect(place.mock.calls[1][0].idempotencyKey).toBe(place.mock.calls[0][0].idempotencyKey);
  });

  it('shows the new total and prices the cart again when the price changed meanwhile', async () => {
    place.mockRejectedValueOnce(new ApiError(409, 'order_price_changed'));
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByTestId('price-breakdown');
    await fillIn(user);

    // The menu changed: the server now says ₹349.25.
    quote.mockResolvedValue(aQuote({ price: { ...aPrice, totalAmount: 349.25 } }));
    await user.press(screen.getByTestId('place-order'));

    expect(
      await screen.findByText(/The price changed, and the total is now ₹349.25/),
    ).toBeOnTheScreen();
    expect(quote).toHaveBeenCalledTimes(2);
    expect(mockDispatch).not.toHaveBeenCalled();
    expect(screen.getByTestId('cart-size')).toHaveTextContent('2');
  });

  it('tells the customer how much more to add when the cart is under the smallest order', async () => {
    quote.mockResolvedValue(
      aQuote({ minOrderAmount: 300, price: { ...aPrice, subtotal: 240, totalAmount: 294.5 } }),
    );
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByTestId('price-breakdown');
    await fillIn(user);

    expect(
      screen.getByText("Spice Route's smallest order is ₹300. Add ₹60 more to order."),
    ).toBeOnTheScreen();
    expect(screen.getByTestId('place-order')).toBeDisabled();
  });

  it('explains a cart the server cannot price, and offers to go back to the menu', async () => {
    quote.mockRejectedValue(new ApiError(409, 'order_item_unavailable'));
    const user = userEvent.setup();
    await renderScreen();

    expect(await screen.findByText(/Some dishes aren't available any more/)).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Add more dishes' }));

    expect(mockGoBack).toHaveBeenCalled();
  });

  it('shows what the server said was wrong with the address or the phone number', async () => {
    place.mockRejectedValue(
      new ApiError(400, 'validation_failed', undefined, undefined, {
        contactPhone: ['Enter a phone number the driver can call.'],
      }),
    );
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByTestId('price-breakdown');
    await fillIn(user);

    await user.press(screen.getByTestId('place-order'));

    expect(await screen.findByText('Enter a phone number the driver can call.')).toBeOnTheScreen();
  });

  it('lets the customer choose among the areas the restaurant delivers to', async () => {
    const user = userEvent.setup();
    await renderScreen();
    await screen.findByTestId('price-breakdown');

    await user.press(screen.getByRole('button', { name: 'Tamil Quarter' }));

    expect(quote).toHaveBeenLastCalledWith(expect.objectContaining({ area: 'tamilQuarter' }));
  });

  it('asks a guest to sign in, and does not price anything', async () => {
    await renderScreen(guestState);

    expect(screen.getByText('Sign in to place your order')).toBeOnTheScreen();
    expect(quote).not.toHaveBeenCalled();
  });

  it('shows an empty cart as such', async () => {
    const user = userEvent.setup();
    await renderScreen(signedInState, emptyCart);

    expect(screen.getByText('Your cart is empty')).toBeOnTheScreen();
    await user.press(screen.getByRole('button', { name: 'Browse restaurants' }));

    expect(mockNavigate).toHaveBeenCalledWith('Restaurants');
    expect(quote).not.toHaveBeenCalled();
  });

  describe('paying', () => {
    const both = { online: true, cashOnDelivery: true, cashOnDeliveryMaxAmount: 2000 };

    it('offers only online payment, and says nothing about cash, when cash is not available', async () => {
      await renderScreen();
      await screen.findByTestId('price-breakdown');

      expect(screen.queryByText('Cash on delivery')).not.toBeOnTheScreen();
      expect(screen.queryByText('Pay online')).not.toBeOnTheScreen();
      expect(screen.getByText(/only then are you charged/)).toBeOnTheScreen();
    });

    it('lets the customer choose between paying online and cash on delivery, starting with online', async () => {
      quote.mockResolvedValue(aQuote({ paymentOptions: both }));
      place.mockResolvedValue(anOrder({ status: 'placed' }));
      const user = userEvent.setup();
      await renderScreen();
      await screen.findByTestId('price-breakdown');
      await fillIn(user);

      expect(screen.getByRole('button', { name: 'Pay online' })).toBeOnTheScreen();
      expect(screen.getByText(/only then are you charged/)).toBeOnTheScreen();

      await user.press(screen.getByTestId('place-order'));

      expect(place.mock.calls[0][0].paymentMethod).toBe('online');
    });

    it('places a cash on delivery order, telling the customer what they will pay the driver', async () => {
      quote.mockResolvedValue(aQuote({ paymentOptions: both }));
      place.mockResolvedValue(anOrder({ status: 'placed', paymentMethod: 'cashOnDelivery' }));
      const user = userEvent.setup();
      await renderScreen();
      await screen.findByTestId('price-breakdown');
      await fillIn(user);

      await user.press(screen.getByRole('button', { name: 'Cash on delivery' }));

      expect(
        screen.getByText(/You'll pay ₹339.25 in cash to the driver when your order arrives/),
      ).toBeOnTheScreen();
      expect(screen.queryByText(/only then are you charged/)).not.toBeOnTheScreen();

      await user.press(screen.getByTestId('place-order'));

      expect(place.mock.calls[0][0]).toMatchObject({
        paymentMethod: 'cashOnDelivery',
        expectedTotal: aPrice.totalAmount,
      });
    });

    it('offers cash alone when online payment is not available, and the order can still be placed', async () => {
      quote.mockResolvedValue(
        aQuote({
          paymentOptions: { online: false, cashOnDelivery: true, cashOnDeliveryMaxAmount: 2000 },
        }),
      );
      place.mockResolvedValue(anOrder({ status: 'placed', paymentMethod: 'cashOnDelivery' }));
      const user = userEvent.setup();
      await renderScreen();
      await screen.findByTestId('price-breakdown');
      await fillIn(user);

      expect(screen.queryByRole('button', { name: 'Pay online' })).not.toBeOnTheScreen();
      expect(screen.getByText(/in cash to the driver/)).toBeOnTheScreen();
      await user.press(screen.getByTestId('place-order'));

      expect(place.mock.calls[0][0].paymentMethod).toBe('cashOnDelivery');
    });

    it('cannot place an order that has no way to be paid for', async () => {
      quote.mockResolvedValue(
        aQuote({
          paymentOptions: { online: false, cashOnDelivery: false, cashOnDeliveryMaxAmount: 2000 },
        }),
      );
      const user = userEvent.setup();
      await renderScreen();
      await screen.findByTestId('price-breakdown');
      await fillIn(user);

      expect(screen.getByTestId('place-order')).toBeDisabled();
    });

    it('treats a different way of paying as a different attempt, so it never gets back an order made the other way', async () => {
      quote.mockResolvedValue(aQuote({ paymentOptions: both }));
      place.mockRejectedValue(new ApiError(503, 'http_503'));
      const user = userEvent.setup();
      await renderScreen();
      await screen.findByTestId('price-breakdown');
      await fillIn(user);

      await user.press(screen.getByTestId('place-order'));
      await screen.findByText('Something went wrong. Please try again.');
      await user.press(screen.getByRole('button', { name: 'Cash on delivery' }));
      await user.press(screen.getByTestId('place-order'));

      expect(place.mock.calls[1][0].paymentMethod).toBe('cashOnDelivery');
      expect(place.mock.calls[1][0].idempotencyKey).not.toBe(place.mock.calls[0][0].idempotencyKey);
    });

    it('says plainly when cash was refused by the server', async () => {
      quote.mockResolvedValue(aQuote({ paymentOptions: both }));
      place.mockRejectedValue(new ApiError(409, 'cash_on_delivery_unavailable'));
      const user = userEvent.setup();
      await renderScreen();
      await screen.findByTestId('price-breakdown');
      await fillIn(user);
      await user.press(screen.getByRole('button', { name: 'Cash on delivery' }));

      await user.press(screen.getByTestId('place-order'));

      expect(
        await screen.findByText(
          "Cash on delivery isn't available for this order. Pay online instead.",
        ),
      ).toBeOnTheScreen();
    });
  });
});
