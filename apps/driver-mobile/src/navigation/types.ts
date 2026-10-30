import type { RootStack } from './RootNavigator';

type RootStackType = typeof RootStack;

// Types useNavigation() and nested navigation across the app (React Navigation static API).
declare module '@react-navigation/native' {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  interface RootNavigator extends RootStackType {}
}
