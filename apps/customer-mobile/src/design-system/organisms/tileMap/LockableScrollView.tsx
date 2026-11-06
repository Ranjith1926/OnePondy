import { createContext, useContext, useEffect, useState } from 'react';
import { ScrollView, type ScrollViewProps } from 'react-native';

type SetLocked = (locked: boolean) => void;

const ScrollLockContext = createContext<SetLocked>(() => undefined);

/**
 * A scroll view that stops scrolling while a finger is on a map inside it. On Android the page's own scrolling takes a drag away
 * from the map after a few pixels however the map answers in JavaScript, so the page has to be told to hold still instead.
 */
export function LockableScrollView({ scrollEnabled = true, ...rest }: ScrollViewProps) {
  const [locked, setLocked] = useState(false);
  return (
    <ScrollLockContext.Provider value={setLocked}>
      <ScrollView {...rest} scrollEnabled={scrollEnabled && !locked} />
    </ScrollLockContext.Provider>
  );
}

/** Lets a map hold the page still while it is being dragged. Does nothing outside a <see cref="LockableScrollView"/>. */
export function useScrollLock(active: boolean): SetLocked {
  const setLocked = useContext(ScrollLockContext);
  // Never leave the page stuck if the map goes away mid-gesture.
  useEffect(() => () => setLocked(false), [setLocked]);
  return active ? setLocked : () => undefined;
}
