import {
  createContext,
  useContext,
  useMemo,
  useReducer,
  type Dispatch,
  type ReactNode,
} from 'react';

import { cartReducer, emptyCart, type CartAction, type CartState } from './cart';

interface CartContextValue {
  cart: CartState;
  dispatch: Dispatch<CartAction>;
}

const CartContext = createContext<CartContextValue | null>(null);

interface CartProviderProps {
  children: ReactNode;
  /** For tests: start with dishes already in the cart. */
  initialCart?: CartState;
}

/** The customer's food cart, kept in memory for as long as the app is open. It holds dish ids and quantities, nothing personal. */
export function CartProvider({ children, initialCart = emptyCart }: CartProviderProps) {
  const [cart, dispatch] = useReducer(cartReducer, initialCart);
  const value = useMemo(() => ({ cart, dispatch }), [cart]);
  return <CartContext.Provider value={value}>{children}</CartContext.Provider>;
}

export function useCart(): CartContextValue {
  const value = useContext(CartContext);
  if (!value) {
    throw new Error('useCart must be used inside <CartProvider>.');
  }
  return value;
}
