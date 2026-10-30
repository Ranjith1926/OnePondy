import { aCart, coffeeId, dosaId, restaurantId } from '../../../test-utils/foodFixture';
import {
  cartEstimate,
  cartItemCount,
  cartLineInputs,
  cartReducer,
  emptyCart,
  MAX_DISHES,
  MAX_PER_DISH,
  quantityOf,
  wouldReplaceCart,
  type CartRestaurant,
} from './cart';

const spiceRoute: CartRestaurant = {
  businessId: restaurantId,
  name: 'Spice Route',
  minOrderAmount: 100,
  prepTimeMinutes: 25,
  deliveryAreas: ['whiteTown'],
};
const otherPlace: CartRestaurant = { ...spiceRoute, businessId: 'other', name: 'Sea View' };
const dosa = { id: dosaId, name: 'Masala dosa', isVeg: true, price: 120 };
const coffee = { id: coffeeId, name: 'Filter coffee', isVeg: true, price: 45 };

describe('cartReducer', () => {
  it('adds a dish, then counts more of the same dish together', () => {
    let cart = cartReducer(emptyCart, { type: 'add', restaurant: spiceRoute, dish: dosa });
    cart = cartReducer(cart, { type: 'add', restaurant: spiceRoute, dish: dosa });
    cart = cartReducer(cart, { type: 'add', restaurant: spiceRoute, dish: coffee });

    expect(cart.restaurant?.name).toBe('Spice Route');
    expect(cart.lines).toHaveLength(2);
    expect(quantityOf(cart, dosaId)).toBe(2);
    expect(cartItemCount(cart)).toBe(3);
    expect(cartEstimate(cart)).toBe(285);
  });

  it('starts a new cart when a dish comes from a different restaurant', () => {
    const cart = cartReducer(aCart(), { type: 'add', restaurant: otherPlace, dish: coffee });

    expect(cart.restaurant?.businessId).toBe('other');
    expect(cart.lines.map((line) => line.menuItemId)).toEqual([coffeeId]);
  });

  it('knows when adding would throw away another restaurant’s dishes', () => {
    expect(wouldReplaceCart(aCart(), 'other')).toBe(true);
    expect(wouldReplaceCart(aCart(), restaurantId)).toBe(false);
    expect(wouldReplaceCart(emptyCart, 'other')).toBe(false);
  });

  it('takes one away at a time, drops the dish at zero, and forgets the restaurant when empty', () => {
    let cart = cartReducer(aCart(), { type: 'remove', menuItemId: dosaId });
    expect(quantityOf(cart, dosaId)).toBe(1);

    cart = cartReducer(cart, { type: 'remove', menuItemId: dosaId });
    expect(quantityOf(cart, dosaId)).toBe(0);
    expect(cart.lines).toHaveLength(1);

    cart = cartReducer(cart, { type: 'remove', menuItemId: coffeeId });
    expect(cart).toEqual(emptyCart);
  });

  it('stops at the most the server accepts of one dish and of different dishes', () => {
    let cart = emptyCart;
    for (let i = 0; i < MAX_PER_DISH + 5; i++) {
      cart = cartReducer(cart, { type: 'add', restaurant: spiceRoute, dish: dosa });
    }
    expect(quantityOf(cart, dosaId)).toBe(MAX_PER_DISH);

    cart = emptyCart;
    for (let i = 0; i < MAX_DISHES + 3; i++) {
      cart = cartReducer(cart, {
        type: 'add',
        restaurant: spiceRoute,
        dish: { id: `dish-${i}`, name: `Dish ${i}`, isVeg: true, price: 10 },
      });
    }
    expect(cart.lines).toHaveLength(MAX_DISHES);
  });

  it('sends only dish ids and quantities, never prices', () => {
    expect(cartLineInputs(aCart())).toEqual([
      { menuItemId: dosaId, quantity: 2 },
      { menuItemId: coffeeId, quantity: 1 },
    ]);
  });

  describe('sync with a freshly read menu', () => {
    it('drops dishes that are gone or sold out and follows new prices', () => {
      const cart = cartReducer(aCart(), {
        type: 'sync',
        businessId: restaurantId,
        available: [{ ...dosa, price: 130 }],
      });

      expect(cart.lines).toEqual([
        { menuItemId: dosaId, name: 'Masala dosa', isVeg: true, unitPrice: 130, quantity: 2 },
      ]);
    });

    it('empties the cart when nothing in it is available any more', () => {
      expect(
        cartReducer(aCart(), { type: 'sync', businessId: restaurantId, available: [] }),
      ).toEqual(emptyCart);
    });

    it('leaves a cart from another restaurant alone', () => {
      const cart = aCart();
      expect(cartReducer(cart, { type: 'sync', businessId: 'other', available: [] })).toBe(cart);
    });
  });

  it('clears', () => {
    expect(cartReducer(aCart(), { type: 'clear' })).toEqual(emptyCart);
  });
});
