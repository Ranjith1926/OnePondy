import { createStaticNavigation } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';

import { ComponentGalleryScreen } from '../features/dev/screens/ComponentGalleryScreen';
import { LocationSelectorScreen } from '../features/location/screens/LocationSelectorScreen';
import { OnboardingScreen } from '../features/onboarding/screens/OnboardingScreen';
import { SearchScreen } from '../features/tourism/screens/SearchScreen';
import { useAppState } from '../state';
import { MainTabs } from './MainTabNavigator';

function useNeedsOnboarding(): boolean {
  return !useAppState().preferences.onboardingCompleted;
}

function useIsOnboarded(): boolean {
  return useAppState().preferences.onboardingCompleted;
}

function useIsDevelopmentBuild(): boolean {
  return __DEV__;
}

// The auth navigator joins in Phase 2; guests keep using the public screens (spec §4).
export const RootStack = createNativeStackNavigator({
  screenOptions: {
    headerShown: false,
  },
  groups: {
    Onboarding: {
      if: useNeedsOnboarding,
      screens: {
        Onboarding: OnboardingScreen,
      },
    },
    App: {
      if: useIsOnboarded,
      screens: {
        Main: MainTabs,
        Search: {
          screen: SearchScreen,
          options: { animation: 'fade' },
        },
        LocationSelector: {
          screen: LocationSelectorScreen,
          options: { presentation: 'modal' },
        },
        ComponentGallery: {
          screen: ComponentGalleryScreen,
          if: useIsDevelopmentBuild,
        },
      },
    },
  },
});

export const Navigation = createStaticNavigation(RootStack);
