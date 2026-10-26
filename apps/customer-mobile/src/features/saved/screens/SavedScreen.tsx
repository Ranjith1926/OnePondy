import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';

import { EmptyState, MainTabTemplate } from '../../../design-system';

// Saving places needs an account, which arrives in Phase 2.
export function SavedScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  return (
    <MainTabTemplate title={t('saved.title')}>
      <EmptyState
        icon="heart"
        title={t('saved.empty.title')}
        message={t('saved.empty.message')}
        actionLabel={t('saved.empty.action')}
        onAction={() => navigation.navigate('Main', { screen: 'Explore' })}
      />
    </MainTabTemplate>
  );
}
