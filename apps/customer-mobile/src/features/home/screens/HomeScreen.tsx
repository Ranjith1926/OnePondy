import { useNavigation } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { MainTabTemplate, SearchBar, useThemedStyles, type Theme } from '../../../design-system';
import { ComingSoonSheet } from '../../modules/components/ComingSoonSheet';
import { useModuleStatus } from '../../modules/hooks/useAppConfig';
import type { ModuleKey } from '../../modules/moduleConfig';
import { CuratedCollectionsSection } from '../components/CuratedCollectionsSection';
import { HomeHeader } from '../components/HomeHeader';
import { HomeHero } from '../components/HomeHero';
import { PopularPlacesSection } from '../components/PopularPlacesSection';
import { ServiceGrid } from '../components/ServiceGrid';
import { TripPlannerCard } from '../components/TripPlannerCard';
import { TrustCard } from '../components/TrustCard';

export function HomeScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const [comingSoon, setComingSoon] = useState<ModuleKey | null>(null);
  const modules = useModuleStatus();
  const openExplore = () => navigation.navigate('Main', { screen: 'Explore' });

  return (
    <>
      <MainTabTemplate
        header={
          <HomeHeader
            onNotificationsPress={
              modules.notifications === 'hidden' ? undefined : () => setComingSoon('notifications')
            }
          />
        }
      >
        <View style={styles.search}>
          <SearchBar
            placeholder={t('home.searchPlaceholder')}
            accessibilityHint={t('home.searchHint')}
            onPress={() => navigation.navigate('Search')}
          />
        </View>
        <ServiceGrid onServicePress={setComingSoon} />
        <HomeHero onExplore={openExplore} />
        <PopularPlacesSection onSeeAll={openExplore} />
        <CuratedCollectionsSection />
        {modules.tripPlanner === 'hidden' ? null : (
          <TripPlannerCard
            available={modules.tripPlanner === 'available'}
            onPress={() =>
              modules.tripPlanner === 'available'
                ? navigation.navigate('TripPlanner')
                : setComingSoon('tripPlanner')
            }
          />
        )}
        <TrustCard />
      </MainTabTemplate>
      <ComingSoonSheet module={comingSoon} onClose={() => setComingSoon(null)} />
    </>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    search: {
      paddingHorizontal: theme.layout.screenPadding,
    },
  });
