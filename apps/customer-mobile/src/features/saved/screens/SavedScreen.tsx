import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';

import { EmptyState, MainTabTemplate } from '../../../design-system';
import { useAppState } from '../../../state';
import { SignInPrompt } from '../../auth/components/SignInPrompt';

// Saved places need an account (spec §4 Guest access); saving itself arrives with places.
export function SavedScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const { auth } = useAppState();
  return (
    <MainTabTemplate title={t('saved.title')}>
      {auth.status === 'authenticated' ? (
        <EmptyState
          icon="heart"
          title={t('saved.empty.title')}
          message={t('saved.empty.message')}
          actionLabel={t('saved.empty.action')}
          onAction={() => navigation.navigate('Main', { screen: 'Explore' })}
        />
      ) : (
        <SignInPrompt
          icon="heart"
          title={t('saved.guest.title')}
          message={t('saved.guest.message')}
        />
      )}
    </MainTabTemplate>
  );
}
