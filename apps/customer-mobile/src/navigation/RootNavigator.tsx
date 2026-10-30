import { createStaticNavigation } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';

import { CompleteProfileScreen } from '../features/auth/screens/CompleteProfileScreen';
import { DeleteAccountScreen } from '../features/auth/screens/DeleteAccountScreen';
import { EditProfileScreen } from '../features/auth/screens/EditProfileScreen';
import { SignInScreen } from '../features/auth/screens/SignInScreen';
import { VerifyCodeScreen } from '../features/auth/screens/VerifyCodeScreen';
import { BookingDetailScreen } from '../features/bookings/screens/BookingDetailScreen';
import { ComponentGalleryScreen } from '../features/dev/screens/ComponentGalleryScreen';
import { CheckoutScreen } from '../features/food/screens/CheckoutScreen';
import { OrderDetailScreen } from '../features/food/screens/OrderDetailScreen';
import { RestaurantMenuScreen } from '../features/food/screens/RestaurantMenuScreen';
import { RestaurantsScreen } from '../features/food/screens/RestaurantsScreen';
import { ItineraryDetailScreen } from '../features/itineraries/screens/ItineraryDetailScreen';
import { ListingDetailScreen } from '../features/listings/screens/ListingDetailScreen';
import { ListingsScreen } from '../features/listings/screens/ListingsScreen';
import { LocationSelectorScreen } from '../features/location/screens/LocationSelectorScreen';
import { OnboardingScreen } from '../features/onboarding/screens/OnboardingScreen';
import { PlaceDetailScreen } from '../features/tourism/screens/PlaceDetailScreen';
import { SearchScreen } from '../features/tourism/screens/SearchScreen';
import { TripPlannerFormScreen } from '../features/trip-planner/screens/TripPlannerFormScreen';
import { useAppState } from '../state';
import { MainTabs } from './MainTabNavigator';

function useNeedsOnboarding(): boolean {
  return !useAppState().preferences.onboardingCompleted;
}

function useIsOnboarded(): boolean {
  return useAppState().preferences.onboardingCompleted;
}

/** Sign-in screens exist only for guests, so signing in closes them and returns to where the user was. */
function useIsGuest(): boolean {
  const { preferences, auth } = useAppState();
  return preferences.onboardingCompleted && auth.status !== 'authenticated';
}

/** Account screens exist only with a valid session, so they close when the session ends (spec §10, Phase 2). */
function useIsSignedIn(): boolean {
  const { preferences, auth } = useAppState();
  return preferences.onboardingCompleted && auth.status === 'authenticated';
}

function useIsDevelopmentBuild(): boolean {
  return __DEV__;
}

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
        PlaceDetail: PlaceDetailScreen,
        ItineraryDetail: ItineraryDetailScreen,
        Listings: ListingsScreen,
        ListingDetail: ListingDetailScreen,
        BookingDetail: BookingDetailScreen,
        Restaurants: RestaurantsScreen,
        RestaurantMenu: RestaurantMenuScreen,
        Checkout: CheckoutScreen,
        OrderDetail: OrderDetailScreen,
        TripPlanner: {
          screen: TripPlannerFormScreen,
          options: { presentation: 'modal' },
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
    Guest: {
      if: useIsGuest,
      screens: {
        SignIn: {
          screen: SignInScreen,
          options: { presentation: 'modal' },
        },
        VerifyCode: VerifyCodeScreen,
        CompleteProfile: {
          screen: CompleteProfileScreen,
          options: { gestureEnabled: false },
        },
      },
    },
    Account: {
      if: useIsSignedIn,
      screenOptions: { presentation: 'modal' },
      screens: {
        EditProfile: EditProfileScreen,
        DeleteAccount: DeleteAccountScreen,
      },
    },
  },
});

export const Navigation = createStaticNavigation(RootStack);
