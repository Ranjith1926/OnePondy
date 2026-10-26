import * as SplashScreen from 'expo-splash-screen';

import '../i18n';
import { AppNavigation } from '../navigation/AppNavigation';
import { AppProviders } from './providers/AppProviders';
import { BannerHost } from './providers/BannerHost';

// Keep the native splash screen up until saved preferences are restored.
void SplashScreen.preventAutoHideAsync();

export default function App() {
  return (
    <AppProviders>
      <AppNavigation />
      <BannerHost />
    </AppProviders>
  );
}
