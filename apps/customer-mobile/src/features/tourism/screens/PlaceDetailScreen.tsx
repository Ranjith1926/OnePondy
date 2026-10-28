import { useNavigation, type StaticScreenProps } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Linking, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  ErrorState,
  HorizontalCarousel,
  InlineBanner,
  LoadingState,
  OPButton,
  OPIconButton,
  OPImage,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { useAppState } from '../../../state';
import { AddToItinerarySheet } from '../../itineraries/components/AddToItinerarySheet';
import { useIsSaved, useToggleSavedPlace } from '../../saved/hooks/useSavedPlaces';
import { categoryIcons, isKnownCategory } from '../categories';
import { resolveMediaUrl, type EntryFee, type OpeningPeriod } from '../api/placesApi';
import { usePlace } from '../hooks/usePlaces';

type Props = StaticScreenProps<{ idOrSlug: string }>;

const dayOrder: OpeningPeriod['day'][] = [
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
  'sunday',
];

function formatFee(fee: EntryFee): string {
  return fee.currency === 'INR' ? `₹${fee.amount}` : `${fee.amount} ${fee.currency}`;
}

/** Everything a visitor needs for one place, plus the entry points into saving and trip planning (spec §10, Phase 4). */
export function PlaceDetailScreen({ route }: Props) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { auth } = useAppState();
  const place = usePlace(route.params.idOrSlug);
  const isSaved = useIsSaved(place.data?.id);
  const toggleSaved = useToggleSavedPlace();
  const [addToTripVisible, setAddToTripVisible] = useState(false);
  const [confirmation, setConfirmation] = useState<string | null>(null);

  const requireSignIn = () => navigation.navigate('SignIn');

  const header = (
    <View style={styles.header}>
      <OPIconButton
        icon="arrow-left"
        accessibilityLabel={t('common.back')}
        onPress={() => navigation.goBack()}
      />
      <View style={styles.headerSpacer} />
      {place.data ? (
        <OPIconButton
          icon="heart"
          variant={isSaved ? 'tonal' : 'plain'}
          accessibilityLabel={isSaved ? t('placeDetail.saved') : t('placeDetail.save')}
          disabled={toggleSaved.isPending}
          onPress={() => {
            if (auth.status !== 'authenticated') {
              requireSignIn();
              return;
            }
            toggleSaved.mutate({ placeId: place.data!.id, save: !isSaved });
          }}
        />
      ) : null}
    </View>
  );

  if (place.isPending) {
    return (
      <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
        {header}
        <View style={styles.centered}>
          <LoadingState accessibilityLabel={t('placeDetail.loading')} />
        </View>
      </SafeAreaView>
    );
  }

  if (place.isError) {
    return (
      <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
        {header}
        <ErrorState
          title={t('placeDetail.error.title')}
          message={t('placeDetail.error.message')}
          retryLabel={t('common.retry')}
          onRetry={() => void place.refetch()}
          retrying={place.isFetching}
        />
      </SafeAreaView>
    );
  }

  const data = place.data;
  const categoryName = isKnownCategory(data.categoryCode)
    ? t(`categoryNames.${data.categoryCode}`)
    : data.categoryCode;
  const fallbackIcon = isKnownCategory(data.categoryCode)
    ? categoryIcons[data.categoryCode]
    : 'map-pin';
  const hasCoordinates = data.latitude !== null && data.longitude !== null;

  const openDirections = () => {
    if (!hasCoordinates) return;
    void Linking.openURL(
      `https://www.google.com/maps/search/?api=1&query=${data.latitude},${data.longitude}`,
    );
  };

  const onAddToTrip = () => {
    if (auth.status !== 'authenticated') {
      requireSignIn();
      return;
    }
    setAddToTripVisible(true);
  };

  return (
    <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
      {header}
      <ScrollView contentContainerStyle={styles.content}>
        {data.images.length > 0 ? (
          <HorizontalCarousel
            data={data.images}
            keyExtractor={(image) => image.urls.large}
            itemWidth={280}
            accessibilityLabel={data.name}
            renderItem={(image) => (
              <OPImage
                source={{ uri: resolveMediaUrl(image.urls.large) }}
                fallbackIcon={fallbackIcon}
                width={280}
                aspectRatio={4 / 3}
                radius="md"
                accessibilityLabel={image.altText}
              />
            )}
          />
        ) : (
          <View style={styles.inset}>
            <OPImage fallbackIcon={fallbackIcon} aspectRatio={16 / 9} radius="md" />
          </View>
        )}

        <View style={[styles.inset, styles.body]}>
          <OPText variant="title" accessibilityRole="header">
            {data.name}
          </OPText>
          <OPText tone="secondary">{categoryName}</OPText>

          {confirmation ? (
            <InlineBanner
              kind="success"
              message={confirmation}
              onDismiss={() => setConfirmation(null)}
            />
          ) : null}

          <View style={styles.actions}>
            <OPButton
              label={t('placeDetail.addToTrip')}
              icon="map"
              variant="secondary"
              onPress={onAddToTrip}
            />
            {hasCoordinates ? (
              <OPButton
                label={t('placeDetail.directions')}
                icon="navigation"
                variant="secondary"
                onPress={openDirections}
              />
            ) : null}
          </View>

          {data.description ? (
            <View style={styles.section}>
              <OPText variant="label">{t('placeDetail.about')}</OPText>
              <OPText>{data.description}</OPText>
            </View>
          ) : null}

          {data.visitorGuidance ? (
            <View style={styles.section}>
              <OPText variant="label">{t('placeDetail.visitorGuidance')}</OPText>
              <OPText>{data.visitorGuidance}</OPText>
            </View>
          ) : null}

          {data.openingHours ? (
            <View style={styles.section}>
              <OPText variant="label">{t('placeDetail.openingHours')}</OPText>
              {data.openingHours.alwaysOpen ? (
                <OPText>{t('placeDetail.alwaysOpen')}</OPText>
              ) : (
                dayOrder
                  .map((day) => data.openingHours!.periods.find((period) => period.day === day))
                  .filter((period): period is OpeningPeriod => period !== undefined)
                  .map((period) => (
                    <View key={period.day} style={styles.row}>
                      <OPText tone="secondary">{t(`days.${period.day}`)}</OPText>
                      <OPText>{`${period.opens} – ${period.closes}`}</OPText>
                    </View>
                  ))
              )}
              {data.openingHours.notes ? (
                <OPText tone="secondary">{data.openingHours.notes}</OPText>
              ) : null}
            </View>
          ) : null}

          {data.entryFees.length > 0 ? (
            <View style={styles.section}>
              <OPText variant="label">{t('placeDetail.entryFees')}</OPText>
              {data.entryFees.map((fee) => (
                <View key={fee.category} style={styles.row}>
                  <OPText tone="secondary">{t(`visitorCategories.${fee.category}`)}</OPText>
                  <OPText>{formatFee(fee)}</OPText>
                </View>
              ))}
            </View>
          ) : null}
        </View>
      </ScrollView>

      <AddToItinerarySheet
        visible={addToTripVisible}
        placeId={data.id}
        onClose={() => setAddToTripVisible(false)}
        onAdded={(title) => {
          setAddToTripVisible(false);
          setConfirmation(t('placeDetail.addedToTrip', { title }));
        }}
      />
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
      paddingHorizontal: theme.spacing.xxs,
      paddingVertical: theme.spacing.xs,
    },
    headerSpacer: {
      flex: 1,
    },
    centered: {
      flex: 1,
      justifyContent: 'center',
    },
    inset: {
      paddingHorizontal: theme.layout.screenPadding,
    },
    content: {
      paddingBottom: theme.spacing.xxl,
      gap: theme.spacing.md,
    },
    body: {
      gap: theme.spacing.md,
    },
    section: {
      gap: theme.spacing.xs,
    },
    actions: {
      flexDirection: 'row',
      flexWrap: 'wrap',
      gap: theme.spacing.sm,
    },
    row: {
      flexDirection: 'row',
      justifyContent: 'space-between',
    },
  });
