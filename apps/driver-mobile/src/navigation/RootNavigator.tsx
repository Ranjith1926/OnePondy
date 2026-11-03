import { createStaticNavigation } from '@react-navigation/native';
import { createNativeStackNavigator } from '@react-navigation/native-stack';

import { SignInScreen } from '../features/auth/screens/SignInScreen';
import { VerifyCodeScreen } from '../features/auth/screens/VerifyCodeScreen';
import { useDriverProfile } from '../features/driver/hooks/useDriver';
import { ApplyScreen } from '../features/driver/screens/ApplyScreen';
import { HomeScreen } from '../features/driver/screens/HomeScreen';
import { RideApplyScreen } from '../features/rides/screens/RideApplyScreen';
import { RideHomeScreen } from '../features/rides/screens/RideHomeScreen';
import { useSession } from '../session/SessionProvider';

/** Sign-in screens exist only while signed out, so signing in replaces them. */
function useIsSignedOut(): boolean {
  return useSession().status !== 'signedIn';
}

/** Ride mode (Phase 9) is separate from delivering food: anyone signed in can open it, and apply for it. */
function useIsSignedIn(): boolean {
  return useSession().status === 'signedIn';
}

/** A signed-in account that has not applied yet goes straight to the application. */
function useNeedsApplication(): boolean {
  const { status } = useSession();
  const configured = useDriverProfile().data?.configured;
  return status === 'signedIn' && configured === false;
}

/** Once an application exists, the driver's own screens show, whatever its status. */
function useHasApplied(): boolean {
  const { status } = useSession();
  const configured = useDriverProfile().data?.configured;
  return status === 'signedIn' && configured === true;
}

export const RootStack = createNativeStackNavigator({
  screenOptions: {
    headerShown: false,
  },
  groups: {
    SignedOut: {
      if: useIsSignedOut,
      screens: {
        SignIn: SignInScreen,
        VerifyCode: VerifyCodeScreen,
      },
    },
    NeedsApplication: {
      if: useNeedsApplication,
      screens: {
        Apply: ApplyScreen,
      },
    },
    Driver: {
      if: useHasApplied,
      screens: {
        Home: HomeScreen,
        EditDetails: ApplyScreen,
      },
    },
    // Last on purpose: the first screen of the first active group is where the app opens, and that is never ride mode.
    RideMode: {
      if: useIsSignedIn,
      screens: {
        RideHome: RideHomeScreen,
        RideApply: RideApplyScreen,
      },
    },
  },
});

export const Navigation = createStaticNavigation(RootStack);
