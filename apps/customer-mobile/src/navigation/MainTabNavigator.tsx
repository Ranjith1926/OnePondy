import { createBottomTabNavigator } from '@react-navigation/bottom-tabs';
import { Platform, Text } from 'react-native';

import { colorTokens, fontFamily, OPIcon, type IconName } from '../design-system';
import { BookingsScreen } from '../features/bookings/screens/BookingsScreen';
import { HomeScreen } from '../features/home/screens/HomeScreen';
import { ProfileScreen } from '../features/profile/screens/ProfileScreen';
import { SavedScreen } from '../features/saved/screens/SavedScreen';
import { ExploreScreen } from '../features/tourism/screens/ExploreScreen';
import i18n from '../i18n';

const TAB_COUNT = 5;

function tabIcon(name: IconName) {
  return function TabIcon({ color, size }: { color: string; size: number }) {
    return <OPIcon name={name} color={color} size={size} />;
  };
}

// Five labels share the width of a phone, so they grow with the system font up to 1.5× and then
// shrink to fit, as platform tab bars do. Screen readers still read the full label.
function TabLabel({ color, children }: { color: string; children: string }) {
  return (
    <Text
      numberOfLines={1}
      adjustsFontSizeToFit
      minimumFontScale={0.75}
      maxFontSizeMultiplier={1.5}
      style={{ color, fontFamily, fontSize: 12, lineHeight: 16, fontWeight: '600' }}
    >
      {children}
    </Text>
  );
}

const renderTabLabel = (props: { color: string; children: string }) => <TabLabel {...props} />;

type TabKey = 'home' | 'explore' | 'bookings' | 'saved' | 'profile';

// Options are functions so titles are translated when navigators re-render.
function tabOptions(key: TabKey, icon: IconName, position: number) {
  return () => {
    const title = i18n.t(`tabs.${key}`);
    return {
      title,
      tabBarIcon: tabIcon(icon),
      // React Navigation adds VoiceOver's "tab, n of 5" only for plain-text labels; Android
      // announces its native tab role instead.
      tabBarAccessibilityLabel:
        Platform.OS === 'ios'
          ? i18n.t('tabs.accessibilityLabel', { label: title, position, total: TAB_COUNT })
          : undefined,
    };
  };
}

// Five destinations only; services are reached from Home and Explore (spec §4).
export const MainTabs = createBottomTabNavigator({
  screenOptions: ({ theme }) => {
    const colors = colorTokens[theme.dark ? 'dark' : 'light'];
    return {
      headerShown: false,
      tabBarActiveTintColor: colors.brand.primary,
      tabBarInactiveTintColor: colors.text.secondary,
      tabBarStyle: {
        backgroundColor: colors.surface,
        borderTopColor: colors.border,
      },
      tabBarLabel: renderTabLabel,
    };
  },
  screens: {
    Home: { screen: HomeScreen, options: tabOptions('home', 'house', 1) },
    Explore: { screen: ExploreScreen, options: tabOptions('explore', 'compass', 2) },
    Bookings: { screen: BookingsScreen, options: tabOptions('bookings', 'calendar-check', 3) },
    Saved: { screen: SavedScreen, options: tabOptions('saved', 'heart', 4) },
    Profile: { screen: ProfileScreen, options: tabOptions('profile', 'user-round', 5) },
  },
});
