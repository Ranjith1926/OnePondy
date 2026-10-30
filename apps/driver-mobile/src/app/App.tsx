import * as SplashScreen from 'expo-splash-screen';

import '../i18n';
import { AppNavigation } from '../navigation/AppNavigation';
import { AppProviders } from './providers/AppProviders';

// Keep the native splash screen up until the saved session has been restored.
void SplashScreen.preventAutoHideAsync();

export default function App() {
  return (
    <AppProviders>
      <AppNavigation />
    </AppProviders>
  );
}
