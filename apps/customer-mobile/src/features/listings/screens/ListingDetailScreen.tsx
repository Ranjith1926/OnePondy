import { useNavigation, type StaticScreenProps } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  ErrorState,
  InlineBanner,
  LoadingState,
  OPButton,
  OPIconButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { useAppState } from '../../../state';
import { BookSlotSheet } from '../components/BookSlotSheet';
import { useListing } from '../hooks/useListings';
import { useIsListingSaved, useToggleSavedListing } from '../hooks/useSavedListings';

type Props = StaticScreenProps<{ id: string }>;

export function ListingDetailScreen({ route }: Props) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { auth } = useAppState();
  const listing = useListing(route.params.id);
  const isSaved = useIsListingSaved(listing.data?.id);
  const toggleSaved = useToggleSavedListing();
  const [bookVisible, setBookVisible] = useState(false);
  const [bookAttempt, setBookAttempt] = useState(0);
  const [confirmation, setConfirmation] = useState<string | null>(null);

  const header = (
    <View style={styles.header}>
      <OPIconButton
        icon="arrow-left"
        accessibilityLabel={t('common.back')}
        onPress={() => navigation.goBack()}
      />
      <View style={styles.headerSpacer} />
      {listing.data ? (
        <OPIconButton
          icon="heart"
          variant={isSaved ? 'tonal' : 'plain'}
          accessibilityLabel={isSaved ? t('listings.detail.saved') : t('listings.detail.save')}
          disabled={toggleSaved.isPending}
          onPress={() => {
            if (auth.status !== 'authenticated') {
              navigation.navigate('SignIn');
              return;
            }
            toggleSaved.mutate({ listingId: listing.data!.id, save: !isSaved });
          }}
        />
      ) : null}
    </View>
  );

  if (listing.isPending) {
    return (
      <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
        {header}
        <View style={styles.centered}>
          <LoadingState accessibilityLabel={t('listings.detail.loading')} />
        </View>
      </SafeAreaView>
    );
  }

  if (listing.isError) {
    return (
      <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
        {header}
        <ErrorState
          title={t('listings.detail.error.title')}
          message={t('listings.detail.error.message')}
          retryLabel={t('common.retry')}
          onRetry={() => void listing.refetch()}
          retrying={listing.isFetching}
        />
      </SafeAreaView>
    );
  }

  const data = listing.data;

  const onBook = () => {
    if (auth.status !== 'authenticated') {
      navigation.navigate('SignIn');
      return;
    }
    setBookAttempt((attempt) => attempt + 1);
    setBookVisible(true);
  };

  return (
    <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
      {header}
      <ScrollView contentContainerStyle={[styles.inset, styles.content]}>
        <OPText variant="title" accessibilityRole="header">
          {data.title}
        </OPText>
        <OPText tone="secondary">{data.businessName}</OPText>
        {data.basePrice !== null ? (
          <OPText variant="label">
            {t('listings.from', { currency: data.currency, price: data.basePrice })}
          </OPText>
        ) : null}

        {confirmation ? (
          <InlineBanner
            kind="success"
            message={confirmation}
            onDismiss={() => setConfirmation(null)}
          />
        ) : null}

        {data.description ? (
          <View style={styles.section}>
            <OPText variant="label">{t('listings.detail.about')}</OPText>
            <OPText>{data.description}</OPText>
          </View>
        ) : null}

        {data.businessAddress ? (
          <View style={styles.section}>
            <OPText variant="label">{t('listings.detail.address')}</OPText>
            <OPText>{data.businessAddress}</OPText>
          </View>
        ) : null}

        {data.isBookable && data.cancellationPolicy ? (
          <View style={styles.section}>
            <OPText variant="label">{t('listings.detail.cancellationPolicy')}</OPText>
            <OPText>
              {t('listings.detail.freeUntil', {
                hours: data.cancellationPolicy.freeCancellationHours,
                percent: data.cancellationPolicy.refundPercentage,
              })}
            </OPText>
            {data.cancellationPolicy.notes ? (
              <OPText tone="secondary">{data.cancellationPolicy.notes}</OPText>
            ) : null}
          </View>
        ) : null}

        {!data.isBookable ? <OPText tone="secondary">{t('listings.discoveryOnly')}</OPText> : null}
      </ScrollView>

      {data.isBookable ? (
        <View style={[styles.inset, styles.footer]}>
          <OPButton label={t('listings.detail.book')} onPress={onBook} fullWidth size="lg" />
        </View>
      ) : null}

      <BookSlotSheet
        key={bookAttempt}
        visible={bookVisible}
        listingId={data.id}
        businessName={data.businessName}
        onClose={() => setBookVisible(false)}
        onBooked={() => {
          setBookVisible(false);
          setConfirmation(t('listings.book.success', { business: data.businessName }));
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
      alignItems: 'center',
      justifyContent: 'center',
    },
    inset: {
      paddingHorizontal: theme.layout.screenPadding,
    },
    content: {
      gap: theme.spacing.sm,
      paddingBottom: theme.spacing.xl,
    },
    section: {
      marginTop: theme.spacing.md,
      gap: theme.spacing.xxs,
    },
    footer: {
      paddingVertical: theme.spacing.sm,
      borderTopWidth: StyleSheet.hairlineWidth,
      borderTopColor: theme.colors.border,
    },
  });
