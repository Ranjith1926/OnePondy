import { useNavigation, type StaticScreenProps } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  EmptyState,
  ErrorState,
  ListItem,
  LoadingState,
  OPChip,
  OPIconButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import type { ListingType } from '../api/listingsApi';
import { useListings } from '../hooks/useListings';
import { listingTypeIcons, listingTypes } from '../listingTypes';

type Filter = ListingType | 'all';

type Props = StaticScreenProps<{ type?: ListingType }>;

// Reached from the Stays and Activities shortcuts on Home, so it is a pushed screen with its own
// back button rather than a tab (spec §10, Phase 6).
export function ListingsScreen({ route }: Props) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const [type, setType] = useState<Filter>(route.params?.type ?? 'all');
  const listings = useListings({ type: type === 'all' ? undefined : type, pageSize: 50 });

  return (
    <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
      <View style={styles.header}>
        <OPIconButton
          icon="arrow-left"
          accessibilityLabel={t('common.back')}
          onPress={() => navigation.goBack()}
        />
        <OPText variant="title" accessibilityRole="header">
          {t('listings.title')}
        </OPText>
      </View>
      <ScrollView contentContainerStyle={styles.content} showsVerticalScrollIndicator={false}>
        <ScrollView
          horizontal
          showsHorizontalScrollIndicator={false}
          accessibilityLabel={t('listings.categoriesLabel')}
          contentContainerStyle={styles.chips}
        >
          <OPChip
            label={t('listings.types.all')}
            selected={type === 'all'}
            onPress={() => setType('all')}
          />
          {listingTypes.map((id) => (
            <OPChip
              key={id}
              label={t(`listings.types.${id}`)}
              selected={type === id}
              onPress={() => setType(id)}
            />
          ))}
        </ScrollView>

        {listings.isPending ? (
          <View style={styles.list}>
            <LoadingState accessibilityLabel={t('listings.loading')} />
          </View>
        ) : listings.isError ? (
          <ErrorState
            title={t('listings.error.title')}
            message={t('listings.error.message')}
            retryLabel={t('common.retry')}
            onRetry={() => void listings.refetch()}
            retrying={listings.isFetching}
          />
        ) : listings.data.items.length > 0 ? (
          <View style={styles.list}>
            {listings.data.items.map((listing) => {
              const price =
                listing.basePrice === null
                  ? undefined
                  : t('listings.from', { currency: listing.currency, price: listing.basePrice });
              const subtitle = listing.isBookable
                ? [listing.businessName, price].filter(Boolean).join(' · ')
                : `${listing.businessName} · ${t('listings.discoveryOnly')}`;
              return (
                <ListItem
                  key={listing.id}
                  leadingIcon={listingTypeIcons[listing.listingType]}
                  title={listing.title}
                  subtitle={subtitle}
                  trailing={{ kind: 'chevron' }}
                  onPress={() => navigation.navigate('ListingDetail', { id: listing.id })}
                />
              );
            })}
          </View>
        ) : (
          <EmptyState
            icon="ticket"
            title={t('listings.empty.title')}
            message={t('listings.empty.message')}
          />
        )}
      </ScrollView>
    </SafeAreaView>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    screen: {
      flex: 1,
      backgroundColor: theme.colors.background,
    },
    header: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.xxs,
      paddingHorizontal: theme.spacing.xxs,
      paddingVertical: theme.spacing.xs,
    },
    content: {
      gap: theme.spacing.md,
      paddingBottom: theme.spacing.xxl,
    },
    chips: {
      gap: theme.spacing.xs,
      paddingHorizontal: theme.layout.screenPadding,
      paddingVertical: theme.spacing.xxs,
    },
    list: {
      gap: theme.spacing.sm,
      paddingHorizontal: theme.layout.screenPadding,
    },
  });
