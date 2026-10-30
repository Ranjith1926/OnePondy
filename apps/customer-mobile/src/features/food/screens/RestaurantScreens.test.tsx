import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, userEvent, within } from '@testing-library/react-native';
import type { ReactElement } from 'react';
import { Text } from 'react-native';

import { ApiError } from '../../../api/apiErrors';
import {
  aCart,
  aMenu,
  aRestaurant,
  coffeeId,
  dosaId,
  emptyCart,
  restaurantId,
} from '../../../test-utils/foodFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { guestState, signedInState } from '../../../test-utils/signedIn';
import { foodApi } from '../api/foodApi';
import type { CartState } from '../cart/cart';
import { useCart } from '../cart/CartContext';
import { RestaurantMenuScreen } from './RestaurantMenuScreen';
import { RestaurantsScreen } from './RestaurantsScreen';

const mockNavigate = jest.fn();
const mockGoBack = jest.fn();

jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ navigate: mockNavigate, goBack: mockGoBack }),
}));
jest.mock('../api/foodApi', () => ({ foodApi: { restaurants: jest.fn(), menu: jest.fn() } }));

const restaurants = foodApi.restaurants as jest.Mock;
const menu = foodApi.menu as jest.Mock;

/** Shows what is in the cart as plain text, so a test can read the cart the screen is changing. */
function CartProbe() {
  const { cart } = useCart();
  return (
    <>
      {cart.lines.map((line) => (
        <Text key={line.menuItemId} testID={`cart-${line.menuItemId}`}>
          {String(line.quantity)}
        </Text>
      ))}
    </>
  );
}

async function renderScreen(
  ui: ReactElement,
  state = signedInState,
  initialCart: CartState = emptyCart,
) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      {ui}
      <CartProbe />
    </QueryClientProvider>,
    { initialState: state, initialCart },
  );
}

const menuRoute = { key: 'k', name: 'RestaurantMenu', params: { id: restaurantId } } as never;

beforeEach(() => {
  restaurants.mockReset();
  menu.mockReset();
  mockNavigate.mockReset();
  mockGoBack.mockReset();
});

describe('<RestaurantsScreen />', () => {
  it('lists the open restaurants for the customer’s area, without needing an account', async () => {
    restaurants.mockResolvedValue([
      aRestaurant(),
      aRestaurant({ businessId: 'r2', name: 'Sea View' }),
    ]);

    await renderScreen(<RestaurantsScreen />, guestState);

    expect(await screen.findByText('Spice Route')).toBeOnTheScreen();
    expect(screen.getByText('Sea View')).toBeOnTheScreen();
    expect(screen.getAllByText('About 25 min · Smallest order ₹100')).toHaveLength(2);
    expect(restaurants).toHaveBeenCalledWith('puducherry');
  });

  it('opens a restaurant’s menu when it is tapped', async () => {
    restaurants.mockResolvedValue([aRestaurant()]);
    await renderScreen(<RestaurantsScreen />);

    await userEvent.setup().press(await screen.findByRole('button', { name: /Spice Route/ }));

    expect(mockNavigate).toHaveBeenCalledWith('RestaurantMenu', { id: restaurantId });
  });

  it('offers to change the area when nothing is open there', async () => {
    restaurants.mockResolvedValue([]);
    await renderScreen(<RestaurantsScreen />);

    expect(await screen.findByText('No restaurants open here right now')).toBeOnTheScreen();
    await userEvent.setup().press(screen.getAllByRole('button', { name: 'Change area' })[0]);

    expect(mockNavigate).toHaveBeenCalledWith('LocationSelector');
  });

  it('says so, and lets the customer retry, when restaurants cannot be loaded', async () => {
    restaurants.mockRejectedValueOnce(new Error('offline'));
    await renderScreen(<RestaurantsScreen />);
    expect(await screen.findByText("Couldn't load restaurants")).toBeOnTheScreen();

    restaurants.mockResolvedValueOnce([aRestaurant()]);
    await userEvent.setup().press(screen.getByRole('button', { name: 'Try again' }));

    expect(await screen.findByText('Spice Route')).toBeOnTheScreen();
  });

  it('shows a way to the cart while it has dishes', async () => {
    restaurants.mockResolvedValue([aRestaurant()]);
    await renderScreen(<RestaurantsScreen />, signedInState, aCart());

    await userEvent.setup().press(await screen.findByTestId('cart-bar'));

    expect(mockNavigate).toHaveBeenCalledWith('Checkout');
  });
});

describe('<RestaurantMenuScreen />', () => {
  it('shows the menu with each dish’s price, whether it is vegetarian, and the licence number', async () => {
    menu.mockResolvedValue(aMenu());

    await renderScreen(<RestaurantMenuScreen route={menuRoute} />, guestState);

    expect(await screen.findByText('Spice Route')).toBeOnTheScreen();
    expect(screen.getByText('Breakfast')).toBeOnTheScreen();
    expect(screen.getByText('Masala dosa')).toBeOnTheScreen();
    expect(screen.getByText('₹120')).toBeOnTheScreen();
    expect(screen.getByText('FSSAI licence: 12345678901234')).toBeOnTheScreen();
    expect(screen.getAllByText('Veg').length).toBeGreaterThan(0);
  });

  it('adds and removes dishes, and the cart bar follows', async () => {
    menu.mockResolvedValue(aMenu());
    const user = userEvent.setup();
    await renderScreen(<RestaurantMenuScreen route={menuRoute} />);
    await screen.findByText('Masala dosa');
    expect(screen.queryByTestId('cart-bar')).not.toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Add one Masala dosa' }));
    await user.press(screen.getByRole('button', { name: 'Add one Masala dosa' }));
    await user.press(screen.getByRole('button', { name: 'Add one Filter coffee' }));

    expect(screen.getByTestId('cart-bar')).toHaveTextContent('View cart · 3 items · ₹285');
    expect(screen.getByTestId(`cart-${dosaId}`)).toHaveTextContent('2');

    await user.press(screen.getByRole('button', { name: 'Remove one Filter coffee' }));

    expect(screen.queryByTestId(`cart-${coffeeId}`)).not.toBeOnTheScreen();
    expect(screen.getByTestId('cart-bar')).toHaveTextContent('View cart · 2 items · ₹240');
  });

  it('does not let a sold-out dish be added', async () => {
    menu.mockResolvedValue(aMenu());
    await renderScreen(<RestaurantMenuScreen route={menuRoute} />);

    const vada = await screen.findByTestId('dish-a0000000-0000-4000-8000-000000000003');

    expect(within(vada).getByText('Sold out')).toBeOnTheScreen();
    expect(within(vada).queryByRole('button', { name: /Add one/ })).not.toBeOnTheScreen();
  });

  it('asks before replacing a cart that holds another restaurant’s dishes', async () => {
    menu.mockResolvedValue(
      aMenu({ restaurant: aRestaurant({ businessId: 'other', name: 'Sea View' }) }),
    );
    const user = userEvent.setup();
    await renderScreen(<RestaurantMenuScreen route={menuRoute} />, signedInState, aCart());
    await screen.findByText('Sea View');

    await user.press(screen.getByRole('button', { name: 'Add one Masala dosa' }));

    expect(await screen.findByText('Start a new cart?')).toBeOnTheScreen();
    expect(screen.getByText(/Your cart has dishes from Spice Route/)).toBeOnTheScreen();
    // Nothing changed yet.
    expect(screen.getByTestId(`cart-${coffeeId}`)).toBeOnTheScreen();

    await user.press(screen.getByRole('button', { name: 'Start a new cart' }));

    expect(screen.queryByTestId(`cart-${coffeeId}`)).not.toBeOnTheScreen();
    expect(screen.getByTestId(`cart-${dosaId}`)).toHaveTextContent('1');
  });

  it('keeps the cart when the customer says so', async () => {
    menu.mockResolvedValue(
      aMenu({ restaurant: aRestaurant({ businessId: 'other', name: 'Sea View' }) }),
    );
    const user = userEvent.setup();
    await renderScreen(<RestaurantMenuScreen route={menuRoute} />, signedInState, aCart());
    await screen.findByText('Sea View');

    await user.press(screen.getByRole('button', { name: 'Add one Masala dosa' }));
    await user.press(await screen.findByRole('button', { name: 'Keep my cart' }));

    expect(screen.getByTestId(`cart-${coffeeId}`)).toBeOnTheScreen();
    expect(screen.getByTestId(`cart-${dosaId}`)).toHaveTextContent('2');
  });

  it('drops dishes from the cart that this menu no longer offers', async () => {
    // The vada was in the cart but is sold out now, and the coffee is gone altogether.
    const cart = aCart();
    cart.lines = [
      ...cart.lines.filter((line) => line.menuItemId === dosaId),
      {
        menuItemId: 'a0000000-0000-4000-8000-000000000003',
        name: 'Medu vada',
        isVeg: true,
        unitPrice: 60,
        quantity: 1,
      },
      { menuItemId: 'gone', name: 'Old dish', isVeg: true, unitPrice: 10, quantity: 1 },
    ];
    menu.mockResolvedValue(aMenu());

    await renderScreen(<RestaurantMenuScreen route={menuRoute} />, signedInState, cart);
    await screen.findByText('Masala dosa');

    expect(screen.getByTestId(`cart-${dosaId}`)).toBeOnTheScreen();
    expect(screen.queryByTestId('cart-a0000000-0000-4000-8000-000000000003')).not.toBeOnTheScreen();
    expect(screen.queryByTestId('cart-gone')).not.toBeOnTheScreen();
  });

  it('takes a guest to sign in rather than to checkout', async () => {
    menu.mockResolvedValue(aMenu());
    const user = userEvent.setup();
    await renderScreen(<RestaurantMenuScreen route={menuRoute} />, guestState);
    await screen.findByText('Masala dosa');

    await user.press(screen.getByRole('button', { name: 'Add one Masala dosa' }));
    await user.press(screen.getByTestId('cart-bar'));

    expect(mockNavigate).toHaveBeenCalledWith('SignIn');
  });

  it('says so when the menu cannot be loaded, for instance because the restaurant closed', async () => {
    menu.mockRejectedValue(new ApiError(404, 'restaurant_not_found'));

    await renderScreen(<RestaurantMenuScreen route={menuRoute} />);

    expect(await screen.findByText("Couldn't load this menu")).toBeOnTheScreen();
  });
});
