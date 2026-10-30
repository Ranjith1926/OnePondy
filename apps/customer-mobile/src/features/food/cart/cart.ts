import type { OrderLineInput } from '../api/foodApi';

/** The server's own limits (OrderService, FoodOrderLine): the cart stops short of them so an order is never refused for size. */
export const MAX_PER_DISH = 20;
export const MAX_DISHES = 30;

export interface CartRestaurant {
  businessId: string;
  name: string;
  minOrderAmount: number;
  prepTimeMinutes: number;
  /** Area codes the restaurant delivers to; `puducherry` means the whole city. */
  deliveryAreas: string[];
}

/** A dish in the cart. The price is only for the estimate on the menu: the server prices the order. */
export interface CartLine {
  menuItemId: string;
  name: string;
  isVeg: boolean;
  unitPrice: number;
  quantity: number;
}

export interface CartState {
  restaurant: CartRestaurant | null;
  lines: CartLine[];
}

export const emptyCart: CartState = { restaurant: null, lines: [] };

export interface CartDish {
  id: string;
  name: string;
  isVeg: boolean;
  price: number;
}

export type CartAction =
  /** One more of a dish. From a different restaurant it starts a new cart (the screen asks first). */
  | { type: 'add'; restaurant: CartRestaurant; dish: CartDish }
  /** One fewer; at zero the dish leaves the cart, and an empty cart forgets its restaurant. */
  | { type: 'remove'; menuItemId: string }
  | { type: 'clear' }
  /** The menu was just read: dishes that are gone or sold out leave the cart, and prices and names follow the menu. */
  | { type: 'sync'; businessId: string; available: CartDish[] };

export function cartReducer(state: CartState, action: CartAction): CartState {
  switch (action.type) {
    case 'add': {
      const sameRestaurant = state.restaurant?.businessId === action.restaurant.businessId;
      const lines = sameRestaurant ? state.lines : [];
      const existing = lines.find((line) => line.menuItemId === action.dish.id);
      if (existing) {
        if (existing.quantity >= MAX_PER_DISH) {
          return state;
        }
        return {
          restaurant: action.restaurant,
          lines: lines.map((line) =>
            line === existing ? { ...line, quantity: line.quantity + 1 } : line,
          ),
        };
      }

      if (lines.length >= MAX_DISHES) {
        return state;
      }

      return {
        restaurant: action.restaurant,
        lines: [
          ...lines,
          {
            menuItemId: action.dish.id,
            name: action.dish.name,
            isVeg: action.dish.isVeg,
            unitPrice: action.dish.price,
            quantity: 1,
          },
        ],
      };
    }

    case 'remove': {
      const lines = state.lines
        .map((line) =>
          line.menuItemId === action.menuItemId ? { ...line, quantity: line.quantity - 1 } : line,
        )
        .filter((line) => line.quantity > 0);
      return lines.length === 0 ? emptyCart : { ...state, lines };
    }

    case 'clear':
      return emptyCart;

    case 'sync': {
      if (state.restaurant?.businessId !== action.businessId) {
        return state;
      }

      const byId = new Map(action.available.map((dish) => [dish.id, dish]));
      const lines = state.lines.flatMap((line) => {
        const dish = byId.get(line.menuItemId);
        return dish ? [{ ...line, name: dish.name, isVeg: dish.isVeg, unitPrice: dish.price }] : [];
      });
      return lines.length === 0 ? emptyCart : { ...state, lines };
    }
  }
}

export function cartItemCount(state: CartState): number {
  return state.lines.reduce((sum, line) => sum + line.quantity, 0);
}

/** What the dishes come to at the menu's prices. An estimate: tax, fees and the final price come from the server. */
export function cartEstimate(state: CartState): number {
  return state.lines.reduce((sum, line) => sum + line.unitPrice * line.quantity, 0);
}

export function quantityOf(state: CartState, menuItemId: string): number {
  return state.lines.find((line) => line.menuItemId === menuItemId)?.quantity ?? 0;
}

export function cartLineInputs(state: CartState): OrderLineInput[] {
  return state.lines.map((line) => ({ menuItemId: line.menuItemId, quantity: line.quantity }));
}

/** Whether adding this dish would start a new cart and throw away a different restaurant's dishes. */
export function wouldReplaceCart(state: CartState, businessId: string): boolean {
  return (
    state.restaurant !== null &&
    state.restaurant.businessId !== businessId &&
    state.lines.length > 0
  );
}
