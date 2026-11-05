import { useNavigation } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  Alert,
  KeyboardAvoidingView,
  Linking,
  ScrollView,
  Share,
  StyleSheet,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { shareBaseUrl } from '../../../api/config';
import {
  ErrorState,
  FormField,
  InlineBanner,
  LoadingState,
  OPButton,
  OPChip,
  OPIconButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { useAppState } from '../../../state';
import { SignInPrompt } from '../../auth/components/SignInPrompt';
import { useGpsLocation } from '../../location/hooks/useGpsLocation';
import { useModuleStatus } from '../../modules/hooks/useAppConfig';
import {
  incidentCategories,
  type IncidentCategory,
  type PlaceChoice,
  type QuoteOption,
  type Ride,
} from '../api/ridesApi';
import {
  useCancelRide,
  useCurrentRide,
  useQuotes,
  useReportIncident,
  useRequestRide,
  useShareRide,
  useSos,
} from '../hooks/useRides';
import { rideErrorMessageKey } from '../rideErrors';
import { canCancel, canReport, hasDriver, isOpen, landmarks, rupees } from '../rideLogic';

const EMERGENCY_FALLBACK = '112';
const MAX_NOTE = 200;
const MAX_DETAILS = 1000;

/**
 * Rides, in one screen with three states: choosing a trip, choosing a fare, and following the ride. Which one is shown always comes from the
 * server (the current ride), so reopening the app in the middle of a trip lands on the trip. Behind the <c>rides</c> module: while it is not
 * available the screen says so and offers nothing, though a ride already under way can still be followed and ended.
 */
export function RideScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { auth } = useAppState();
  const modules = useModuleStatus();
  const current = useCurrentRide();

  const header = (
    <View style={styles.header}>
      <OPIconButton
        icon="arrow-left"
        accessibilityLabel={t('common.back')}
        onPress={() => navigation.goBack()}
      />
    </View>
  );

  if (auth.status !== 'authenticated') {
    return (
      <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
        {header}
        <SignInPrompt
          icon="car-taxi-front"
          title={t('rides.signInTitle')}
          message={t('rides.signInMessage')}
        />
      </SafeAreaView>
    );
  }

  let body;
  if (current.isLoading) {
    body = <LoadingState accessibilityLabel={t('rides.loading')} />;
  } else if (current.isError) {
    body = (
      <ErrorState
        title={t('rides.errorTitle')}
        message={t(rideErrorMessageKey(current.error))}
        retryLabel={t('common.retry')}
        onRetry={() => void current.refetch()}
        retrying={current.isFetching}
      />
    );
  } else if (current.data) {
    body = <Trip ride={current.data} connected={current.connected} />;
  } else if (modules.rides !== 'available') {
    body = (
      <View style={styles.inset}>
        <OPText variant="title" accessibilityRole="header">
          {t('rides.title')}
        </OPText>
        <OPText tone="secondary">{t('rides.unavailable')}</OPText>
      </View>
    );
  } else {
    body = <Plan />;
  }

  return (
    <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
      {header}
      <ScrollView contentContainerStyle={styles.content} keyboardShouldPersistTaps="handled">
        {body}
      </ScrollView>
    </SafeAreaView>
  );
}

/** Choosing the trip, then a fare. */
function Plan() {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const gps = useGpsLocation();
  const quotes = useQuotes();
  const request = useRequestRide();
  const [pickupId, setPickupId] = useState<string | null>(null);
  const [destinationId, setDestinationId] = useState<string | null>(null);
  const [note, setNote] = useState('');

  const pickup: PlaceChoice | null =
    pickupId === 'here' && gps.coordinates
      ? { ...gps.coordinates, label: t('rides.myLocation') }
      : (landmarks.find((place) => place.id === pickupId) ?? null);
  const destination = landmarks.find((place) => place.id === destinationId) ?? null;
  const ready = pickup !== null && destination !== null && pickup.label !== destination.label;

  const seeFares = () => {
    if (pickup && destination) {
      request.reset();
      quotes.mutate({ pickup, destination });
    }
  };

  const choose = (option: QuoteOption) =>
    request.mutate({ quoteId: option.quoteId, note: note.trim() || undefined });

  return (
    <View style={styles.inset}>
      <OPText variant="title" accessibilityRole="header">
        {t('rides.title')}
      </OPText>

      <View style={styles.field}>
        <OPText variant="label">{t('rides.pickup')}</OPText>
        <View style={styles.chips}>
          <OPChip
            label={gps.coordinates ? t('rides.myLocation') : t('rides.useMyLocation')}
            selected={pickupId === 'here'}
            onPress={() => {
              if (gps.coordinates) {
                setPickupId('here');
              } else {
                void gps.requestLocation();
                setPickupId('here');
              }
            }}
          />
          {landmarks.map((place) => (
            <OPChip
              key={place.id}
              label={place.label}
              selected={pickupId === place.id}
              onPress={() => setPickupId(place.id)}
            />
          ))}
        </View>
        {pickupId === 'here' &&
        !gps.coordinates &&
        (gps.status === 'denied' || gps.status === 'unavailable') ? (
          <OPText tone="secondary">{t('rides.locationUnavailable')}</OPText>
        ) : null}
      </View>

      <View style={styles.field}>
        <OPText variant="label">{t('rides.destination')}</OPText>
        <View style={styles.chips}>
          {landmarks.map((place) => (
            <OPChip
              key={place.id}
              label={place.label}
              selected={destinationId === place.id}
              onPress={() => setDestinationId(place.id)}
            />
          ))}
        </View>
      </View>

      <FormField
        label={t('rides.note')}
        hint={t('rides.noteHint')}
        value={note}
        onChangeText={setNote}
        maxLength={MAX_NOTE}
      />

      <OPButton
        label={t('rides.seeFares')}
        onPress={seeFares}
        loading={quotes.isPending}
        disabled={!ready}
        fullWidth
        testID="rides-see-fares"
      />

      {quotes.error ? (
        <InlineBanner kind="error" message={t(rideErrorMessageKey(quotes.error))} />
      ) : null}
      {request.error ? (
        <InlineBanner kind="error" message={t(rideErrorMessageKey(request.error))} />
      ) : null}

      {quotes.data ? (
        <View style={styles.field}>
          <OPText variant="label">{t('rides.fares')}</OPText>
          {quotes.data.options.map((option) => (
            <View key={option.quoteId} style={styles.card}>
              <OPText variant="bodyStrong">{t(`rides.vehicle.${option.vehicleClass}`)}</OPText>
              <OPText tone="secondary">
                {t('rides.distance', { km: option.distanceKm })} · {rupees(option.fare)}{' '}
                {t('rides.payDriver')}
              </OPText>
              <OPText tone="secondary">{option.cancellationTerms}</OPText>
              {option.available ? null : (
                <OPText tone="secondary">{t('rides.noDriversNear')}</OPText>
              )}
              <OPButton
                label={t('rides.request', {
                  vehicle: t(`rides.vehicle.${option.vehicleClass}`),
                  fare: rupees(option.fare),
                })}
                onPress={() => choose(option)}
                loading={request.isPending}
                disabled={!option.available}
                fullWidth
                testID={`rides-request-${option.vehicleClass}`}
              />
            </View>
          ))}
        </View>
      ) : null}
    </View>
  );
}

/** Following a ride: where it is, who is coming, and cancel, emergency, share and report. */
function Trip({ ride, connected }: { ride: Ride; connected: boolean }) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const gps = useGpsLocation();
  const cancel = useCancelRide();
  const sos = useSos();
  const share = useShareRide();
  const report = useReportIncident();
  const [reporting, setReporting] = useState(false);
  const [category, setCategory] = useState<IncidentCategory | null>(null);
  const [details, setDetails] = useState('');
  const [tried, setTried] = useState(false);

  const emergencyNumber = sos.data?.emergencyNumber ?? EMERGENCY_FALLBACK;
  const name = ride.driver?.firstName ?? '';

  const confirmCancel = () =>
    Alert.alert(t('rides.cancelTitle'), ride.cancellationTerms, [
      { text: t('rides.keep'), style: 'cancel' },
      {
        text: t('rides.cancelConfirm'),
        style: 'destructive',
        onPress: () => cancel.mutate(ride.id),
      },
    ]);

  const pressSos = () =>
    Alert.alert(t('rides.sosTitle'), t('rides.sosBody', { number: emergencyNumber }), [
      { text: t('rides.notNow'), style: 'cancel' },
      {
        text: t('rides.sosConfirm'),
        style: 'destructive',
        onPress: () => sos.mutate({ id: ride.id, position: gps.coordinates }),
      },
    ]);

  const shareTrip = () =>
    share.mutate(ride.id, {
      onSuccess: (link) =>
        void Share.share({
          message: t('rides.shareMessage', { url: `${shareBaseUrl}/trip/${link.token}` }),
        }),
    });

  const sendReport = () => {
    setTried(true);
    if (!category || details.trim() === '') {
      return;
    }

    report.mutate(
      { id: ride.id, category, details: details.trim() },
      {
        onSuccess: () => {
          setReporting(false);
          setDetails('');
          setCategory(null);
          setTried(false);
        },
      },
    );
  };

  return (
    <KeyboardAvoidingView behavior="padding">
      <View style={styles.inset}>
        <OPText variant="title" accessibilityRole="header" accessibilityLiveRegion="polite">
          {t(`rides.status.${ride.status}`, { name })}
        </OPText>
        {isOpen(ride.status) && !connected ? (
          <OPText tone="secondary">{t('rides.reconnecting')}</OPText>
        ) : null}

        <View style={styles.card}>
          <OPText>
            {ride.pickup.label} → {ride.destination.label}
          </OPText>
          <OPText tone="secondary">
            {t(`rides.vehicle.${ride.vehicleClass}`)} ·{' '}
            {t('rides.distance', { km: ride.distanceKm })} · {rupees(ride.fare)}{' '}
            {t('rides.payDriver')}
          </OPText>
          {ride.driver ? (
            <OPText>
              {t('rides.driver', { name: ride.driver.firstName, plate: ride.driver.vehicleNumber })}
            </OPText>
          ) : null}
          {ride.status === 'requested' ? (
            <OPText tone="secondary">{t('rides.searching')}</OPText>
          ) : null}
          {ride.status === 'cancelled' && ride.cancelledBy === 'driver' ? (
            <OPText tone="secondary">{t('rides.cancelledByDriver')}</OPText>
          ) : null}
        </View>

        {cancel.error ? (
          <InlineBanner kind="error" message={t(rideErrorMessageKey(cancel.error))} />
        ) : null}
        {sos.error ? (
          <InlineBanner kind="error" message={t(rideErrorMessageKey(sos.error))} />
        ) : null}
        {share.error ? (
          <InlineBanner kind="error" message={t(rideErrorMessageKey(share.error))} />
        ) : null}
        {report.error ? (
          <InlineBanner kind="error" message={t(rideErrorMessageKey(report.error))} />
        ) : null}
        {sos.data ? (
          <InlineBanner
            kind="success"
            message={t('rides.sosSent', { number: sos.data.emergencyNumber })}
          />
        ) : null}
        {report.isSuccess ? <InlineBanner kind="success" message={t('rides.reportSent')} /> : null}

        {hasDriver(ride.status) ? (
          <View style={styles.actions}>
            <OPButton
              label={t('rides.sos')}
              variant="danger"
              icon="triangle-alert"
              onPress={pressSos}
              loading={sos.isPending}
              fullWidth
              testID="rides-sos"
            />
            <OPButton
              label={t('rides.callEmergency', { number: emergencyNumber })}
              variant="secondary"
              icon="phone"
              onPress={() => void Linking.openURL(`tel:${emergencyNumber}`)}
              fullWidth
            />
            <OPButton
              label={t('rides.shareTrip')}
              variant="secondary"
              icon="share-2"
              onPress={shareTrip}
              loading={share.isPending}
              fullWidth
              testID="rides-share"
            />
          </View>
        ) : null}

        {canCancel(ride.status) ? (
          <OPButton
            label={t('rides.cancel')}
            variant="tertiary"
            onPress={confirmCancel}
            loading={cancel.isPending}
            fullWidth
            testID="rides-cancel"
          />
        ) : null}

        {canReport(ride) ? (
          reporting ? (
            <View style={styles.field}>
              <OPText variant="label">{t('rides.reportWhat')}</OPText>
              <View style={styles.chips}>
                {incidentCategories.map((option) => (
                  <OPChip
                    key={option}
                    label={t(`rides.category.${option}`)}
                    selected={category === option}
                    onPress={() => setCategory(option)}
                  />
                ))}
              </View>
              <FormField
                label={t('rides.reportDetails')}
                value={details}
                onChangeText={setDetails}
                maxLength={MAX_DETAILS}
                multiline
                error={
                  tried && (!category || details.trim() === '')
                    ? t('rides.reportRequired')
                    : undefined
                }
              />
              <OPButton
                label={t('rides.reportSend')}
                onPress={sendReport}
                loading={report.isPending}
                fullWidth
                testID="rides-report-send"
              />
            </View>
          ) : (
            <OPButton
              label={t('rides.report')}
              variant="tertiary"
              icon="flag"
              onPress={() => setReporting(true)}
              fullWidth
              testID="rides-report"
            />
          )
        ) : null}
      </View>
    </KeyboardAvoidingView>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    screen: { flex: 1, backgroundColor: theme.colors.background },
    header: { paddingHorizontal: theme.spacing.xxs, paddingVertical: theme.spacing.xs },
    content: { paddingBottom: theme.spacing.xxl },
    inset: { paddingHorizontal: theme.layout.screenPadding, gap: theme.spacing.md },
    field: { gap: theme.spacing.xs },
    chips: { flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.xs },
    card: {
      gap: theme.spacing.xs,
      padding: theme.spacing.md,
      borderRadius: theme.radii.md,
      backgroundColor: theme.colors.surface,
    },
    actions: { gap: theme.spacing.xs },
  });
