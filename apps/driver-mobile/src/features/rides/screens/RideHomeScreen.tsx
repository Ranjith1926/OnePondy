import { useNavigation } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Alert, Linking, StyleSheet, Switch, View } from 'react-native';

import { Screen } from '../../../components/Screen';
import {
  FormField,
  InlineBanner,
  OPBadge,
  OPButton,
  OPChip,
  OPText,
  useTheme,
  useThemedStyles,
  type BadgeTone,
  type Theme,
} from '../../../design-system';
import {
  incidentCategories,
  type ApprovalStatus,
  type DriverRide,
  type IncidentCategory,
  type RideOffer,
} from '../api/rideDriverApi';
import {
  useAcceptRide,
  useArriveRide,
  useCancelRideAsDriver,
  useCompleteRide,
  useGiveUpRide,
  useRideDriverProfile,
  useRideIncident,
  useRideLocation,
  useRideSos,
  useRides,
  useSetRideDuty,
  useStartRide,
} from '../hooks/useRideDriver';
import { rideErrorMessageKey } from '../services/errorMessages';
import { canEndWithReason, canGiveUp, nextStep, rupees } from '../services/rideRules';

const statusTone: Record<ApprovalStatus, BadgeTone> = {
  pending: 'info',
  approved: 'success',
  rejected: 'warning',
  suspended: 'error',
};

const EMERGENCY_FALLBACK = '112';

/**
 * Ride mode: the state of the application, the duty switch and, once approved and insured, the requests near the driver and the ride they hold.
 * The driver's position is collected only while on duty, and only the foreground app reports it.
 */
export function RideHomeScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const theme = useTheme();
  const query = useRideDriverProfile();
  const setDuty = useSetRideDuty();
  const profile = query.data;
  const eligible = profile?.eligible === true;
  const rides = useRides(profile?.approvalStatus === 'approved');
  const current = rides.data?.current ?? null;
  const location = useRideLocation(profile?.isOnDuty === true, current?.id ?? null);

  const back = { backLabel: t('common.back'), onBack: () => navigation.goBack() };

  if (!profile) {
    return (
      <Screen title={t('rides.title')} {...back}>
        {null}
      </Screen>
    );
  }

  if (!profile.configured || !profile.approvalStatus) {
    return (
      <Screen title={t('rides.title')} {...back}>
        <OPText tone="secondary">{t('rides.apply.intro')}</OPText>
        <OPButton
          label={t('rides.apply.start')}
          onPress={() => navigation.navigate('RideApply')}
          fullWidth
          testID="ride-start-apply"
        />
      </Screen>
    );
  }

  const status = profile.approvalStatus;
  // Off duty needs a free driver (the server refuses it otherwise), and on duty needs an approved, insured one.
  const dutyLocked =
    setDuty.isPending || (!profile.isOnDuty && !eligible) || (profile.isOnDuty && current !== null);

  return (
    <Screen title={t('rides.title')} {...back}>
      <View style={styles.badgeRow}>
        <OPBadge label={t(`rides.status.${status}`)} tone={statusTone[status]} />
      </View>

      {status === 'pending' ? (
        <>
          <InlineBanner kind="info" message={t('rides.pendingMessage')} />
          <OPButton
            label={t('rides.checkStatus')}
            onPress={() => void query.refetch()}
            loading={query.isFetching}
            variant="secondary"
            fullWidth
          />
        </>
      ) : null}
      {status === 'rejected' ? (
        <>
          <InlineBanner
            kind="warning"
            message={
              profile.reviewNote
                ? t('rides.rejectedMessage', { reason: profile.reviewNote })
                : t('rides.rejectedNoReason')
            }
          />
          <OPButton
            label={t('rides.updateDetails')}
            onPress={() => navigation.navigate('RideApply')}
            fullWidth
          />
        </>
      ) : null}
      {status === 'suspended' ? (
        <InlineBanner
          kind="error"
          message={t('rides.suspendedMessage', { reason: profile.reviewNote ?? '' })}
        />
      ) : null}

      {status === 'approved' ? (
        <>
          {profile.insuranceEnded ? (
            <>
              <InlineBanner kind="error" message={t('rides.insuranceEnded')} />
              <OPButton
                label={t('rides.updateDetails')}
                onPress={() => navigation.navigate('RideApply')}
                fullWidth
              />
            </>
          ) : null}

          <View style={styles.card}>
            <View style={styles.dutyRow}>
              <View style={styles.dutyText}>
                <OPText variant="subheading">{t('rides.dutyTitle')}</OPText>
                <OPText tone="secondary">
                  {profile.isOnDuty ? t('rides.dutyOn') : t('rides.dutyOff')}
                </OPText>
              </View>
              <Switch
                value={profile.isOnDuty}
                onValueChange={(onDuty) => setDuty.mutate({ onDuty, version: profile.version })}
                disabled={dutyLocked}
                accessibilityState={{ disabled: dutyLocked }}
                accessibilityLabel={t('rides.dutyLabel')}
                trackColor={{ true: theme.colors.brand.primary, false: theme.colors.borderStrong }}
              />
            </View>
            <OPText variant="caption" tone="secondary">
              {t('rides.dutyExplainer')}
            </OPText>
          </View>
          {setDuty.error ? (
            <InlineBanner kind="error" message={t(rideErrorMessageKey(setDuty.error))} />
          ) : null}
          {location.denied ? (
            <InlineBanner kind="warning" message={t('rides.locationDenied')} />
          ) : null}
          {profile.isOnDuty && !rides.connected ? (
            <OPText variant="caption" tone="secondary">
              {t('rides.reconnecting')}
            </OPText>
          ) : null}

          {current ? (
            <Trip ride={current} position={location.position} />
          ) : profile.isOnDuty ? (
            <Offers offers={rides.data?.offers ?? []} loading={rides.isLoading} />
          ) : null}
        </>
      ) : null}

      {status !== 'suspended' && status !== 'rejected' && current === null ? (
        <OPButton
          label={t('rides.editDetails')}
          onPress={() => navigation.navigate('RideApply')}
          variant="tertiary"
          fullWidth
        />
      ) : null}
    </Screen>
  );
}

/** The requests near the driver. They show the pickup's name and distance, never the rider or the exact point. */
function Offers({ offers, loading }: { offers: RideOffer[]; loading: boolean }) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const accept = useAcceptRide();

  return (
    <View style={styles.section}>
      <OPText variant="subheading">{t('rides.offersTitle')}</OPText>
      {accept.error ? (
        <InlineBanner kind="error" message={t(rideErrorMessageKey(accept.error))} />
      ) : null}
      {offers.length === 0 ? (
        <OPText tone="secondary">{loading ? t('rides.loading') : t('rides.noOffers')}</OPText>
      ) : (
        offers.map((offer) => (
          <View key={offer.id} style={styles.card}>
            <OPText variant="bodyStrong">
              {offer.pickupLabel} → {offer.destinationLabel}
            </OPText>
            <OPText tone="secondary">
              {t('rides.offerDetails', {
                away: offer.pickupDistanceKm,
                km: offer.distanceKm,
                fare: rupees(offer.fare),
              })}
            </OPText>
            <OPButton
              label={t('rides.accept')}
              onPress={() => accept.mutate(offer.id)}
              loading={accept.isPending && accept.variables === offer.id}
              fullWidth
              testID={`ride-accept-${offer.id}`}
            />
          </View>
        ))
      )}
    </View>
  );
}

/** The ride the driver holds: where to go, the next step, and the ways to end it or to get help. */
function Trip({
  ride,
  position,
}: {
  ride: DriverRide;
  position: { latitude: number; longitude: number } | null;
}) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const arrive = useArriveRide();
  const start = useStartRide();
  const complete = useCompleteRide();
  const giveUp = useGiveUpRide();
  const cancel = useCancelRideAsDriver();
  const sos = useRideSos();
  const incident = useRideIncident();
  const [ending, setEnding] = useState(false);
  const [reason, setReason] = useState('');
  const [reporting, setReporting] = useState(false);
  const [category, setCategory] = useState<IncidentCategory | null>(null);
  const [details, setDetails] = useState('');
  const [tried, setTried] = useState(false);

  const step = nextStep(ride.status);
  const number = sos.data?.emergencyNumber ?? EMERGENCY_FALLBACK;
  const failure =
    arrive.error ??
    start.error ??
    complete.error ??
    giveUp.error ??
    cancel.error ??
    sos.error ??
    incident.error;
  const busy = arrive.isPending || start.isPending || complete.isPending;

  const confirmComplete = () =>
    Alert.alert(t('rides.completeTitle'), t('rides.completeBody', { fare: rupees(ride.fare) }), [
      { text: t('rides.notNow'), style: 'cancel' },
      {
        text: t('rides.completeConfirm', { fare: rupees(ride.fare) }),
        onPress: () => complete.mutate({ id: ride.id, cash: ride.fare }),
      },
    ]);

  const pressSos = () =>
    Alert.alert(t('rides.sosTitle'), t('rides.sosBody', { number }), [
      { text: t('rides.notNow'), style: 'cancel' },
      {
        text: t('rides.sosConfirm'),
        style: 'destructive',
        onPress: () => sos.mutate({ id: ride.id, position }),
      },
    ]);

  const sendReport = () => {
    setTried(true);
    if (!category || details.trim() === '') {
      return;
    }

    incident.mutate(
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
    <View style={styles.section}>
      <OPText variant="subheading" accessibilityLiveRegion="polite">
        {t(`rides.trip.${ride.status}`, { name: ride.riderFirstName })}
      </OPText>
      <View style={styles.card}>
        <OPText variant="bodyStrong">{t('rides.pickupLine', { place: ride.pickup.label })}</OPText>
        {ride.pickupNote ? <OPText>{t('rides.noteLine', { note: ride.pickupNote })}</OPText> : null}
        <OPText>{t('rides.destinationLine', { place: ride.destination.label })}</OPText>
        <OPText tone="secondary">
          {t('rides.fareLine', { fare: rupees(ride.fare), km: ride.distanceKm })}
        </OPText>
      </View>

      {failure ? <InlineBanner kind="error" message={t(rideErrorMessageKey(failure))} /> : null}
      {sos.data ? (
        <InlineBanner
          kind="success"
          message={t('rides.sosSent', { number: sos.data.emergencyNumber })}
        />
      ) : null}
      {incident.isSuccess ? <InlineBanner kind="success" message={t('rides.reportSent')} /> : null}

      {step ? (
        <OPButton
          label={t(`rides.step.${step}`, { fare: rupees(ride.fare) })}
          onPress={() => {
            if (step === 'arrive') arrive.mutate(ride.id);
            else if (step === 'start') start.mutate(ride.id);
            else confirmComplete();
          }}
          loading={busy}
          fullWidth
          size="lg"
          testID={`ride-step-${step}`}
        />
      ) : null}

      {canGiveUp(ride.status) ? (
        <OPButton
          label={t('rides.giveUp')}
          variant="secondary"
          onPress={() => giveUp.mutate(ride.id)}
          loading={giveUp.isPending}
          fullWidth
          testID="ride-give-up"
        />
      ) : null}

      {canEndWithReason(ride.status) ? (
        ending ? (
          <View style={styles.section}>
            <FormField
              label={t('rides.endReason')}
              hint={t('rides.endReasonHint')}
              value={reason}
              onChangeText={setReason}
              maxLength={500}
              multiline
            />
            <OPButton
              label={t('rides.endConfirm')}
              variant="danger"
              onPress={() => reason.trim() && cancel.mutate({ id: ride.id, reason: reason.trim() })}
              disabled={reason.trim() === ''}
              loading={cancel.isPending}
              fullWidth
              testID="ride-end-confirm"
            />
          </View>
        ) : (
          <OPButton
            label={t('rides.endRide')}
            variant="tertiary"
            onPress={() => setEnding(true)}
            fullWidth
            testID="ride-end"
          />
        )
      ) : null}

      <OPButton
        label={t('rides.sos')}
        variant="danger"
        icon="triangle-alert"
        onPress={pressSos}
        loading={sos.isPending}
        fullWidth
        testID="ride-sos"
      />
      <OPButton
        label={t('rides.callEmergency', { number })}
        variant="secondary"
        icon="phone"
        onPress={() => void Linking.openURL(`tel:${number}`)}
        fullWidth
      />

      {reporting ? (
        <View style={styles.section}>
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
            maxLength={1000}
            multiline
            error={
              tried && (!category || details.trim() === '') ? t('rides.reportRequired') : undefined
            }
          />
          <OPButton
            label={t('rides.reportSend')}
            onPress={sendReport}
            loading={incident.isPending}
            fullWidth
            testID="ride-report-send"
          />
        </View>
      ) : (
        <OPButton
          label={t('rides.report')}
          variant="tertiary"
          icon="flag"
          onPress={() => setReporting(true)}
          fullWidth
          testID="ride-report"
        />
      )}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    badgeRow: { flexDirection: 'row' },
    section: { gap: theme.spacing.sm },
    chips: { flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.xs },
    card: {
      gap: theme.spacing.xs,
      padding: theme.spacing.md,
      borderRadius: theme.radii.lg,
      borderWidth: 1,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surface,
    },
    dutyRow: { flexDirection: 'row', alignItems: 'center', gap: theme.spacing.md },
    dutyText: { flex: 1, gap: theme.spacing.xxs },
  });
