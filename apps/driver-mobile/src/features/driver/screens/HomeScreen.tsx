import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { Screen } from '../../../components/Screen';
import {
  InlineBanner,
  OPBadge,
  OPButton,
  OPText,
  useThemedStyles,
  type BadgeTone,
  type Theme,
} from '../../../design-system';
import { useSession } from '../../../session/SessionProvider';
import type { DriverApprovalStatus, DriverProfile } from '../api/driverApi';
import { DutyPill } from '../components/DutyPill';
import { HomeMap } from '../../maps/components/HomeMap';
import { JobsSection } from '../components/JobsSection';
import { useDriverProfile, useSetDuty } from '../hooks/useDriver';
import { useJobs } from '../hooks/useJobs';
import { driverErrorMessageKey } from '../services/errorMessages';

const statusTone: Record<DriverApprovalStatus, BadgeTone> = {
  pending: 'info',
  approved: 'success',
  rejected: 'warning',
  suspended: 'error',
};

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}

/**
 * The driver's home: the state of their application, and once approved the duty switch and where deliveries will appear.
 * Where the driver is can be drawn on a map here, on this phone only; it goes to OnePondy only for the length of a delivery.
 */
export function HomeScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { user, signOut } = useSession();
  const query = useDriverProfile();
  const setDuty = useSetDuty();
  const profile = query.data;
  // The same query the deliveries list uses, so while a delivery is held the duty switch can say why it cannot be turned off.
  const jobs = useJobs(profile?.approvalStatus === 'approved').data;
  const holdsJob = jobs?.current != null;

  if (!profile?.configured || !profile.approvalStatus) {
    return null;
  }

  const status = profile.approvalStatus;
  // Off duty needs a free driver (the server refuses it otherwise), and on duty needs an approved one.
  const dutyLocked =
    setDuty.isPending ||
    (!profile.isOnDuty && !profile.canGoOnDuty) ||
    (profile.isOnDuty && holdsJob);
  const firstName = (profile.fullName ?? user?.displayName ?? '').split(' ')[0];

  return (
    <Screen title={t('home.greeting', { name: firstName })}>
      <View style={styles.badgeRow}>
        <OPBadge label={t(`home.status.${status}`)} tone={statusTone[status]} />
      </View>

      {status === 'pending' ? (
        <>
          <InlineBanner kind="info" message={t('home.pendingMessage')} />
          <OPButton
            label={t('home.checkStatus')}
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
                ? t('home.rejectedMessage', { reason: profile.reviewNote })
                : t('home.rejectedMessageNoReason')
            }
          />
          <OPButton
            label={t('home.updateDetails')}
            onPress={() => navigation.navigate('EditDetails')}
            fullWidth
          />
        </>
      ) : null}

      {status === 'suspended' ? (
        <InlineBanner
          kind="error"
          message={t('home.suspendedMessage', { reason: profile.reviewNote ?? '' })}
        />
      ) : null}

      {status === 'approved' ? (
        <>
          <DutyPill
            onDuty={profile.isOnDuty}
            locked={dutyLocked}
            onChange={(onDuty) => setDuty.mutate({ onDuty, version: profile.version })}
          />
          <View style={styles.dutyNotes}>
            <OPText tone="secondary">
              {profile.isOnDuty && profile.onDutySince
                ? t('home.dutyOn', { time: formatTime(profile.onDutySince) })
                : t('home.dutyOff')}
            </OPText>
            <OPText variant="caption" tone="secondary">
              {t('home.dutyExplainer')}
            </OPText>
          </View>
          {setDuty.error ? (
            <InlineBanner kind="error" message={t(driverErrorMessageKey(setDuty.error))} />
          ) : null}

          {/* Once the deliveries are known: a driver holding one has the delivery's own map instead. */}
          {jobs && !holdsJob ? <HomeMap /> : null}

          <JobsSection isOnDuty={profile.isOnDuty} />
        </>
      ) : null}

      <VehicleSummary profile={profile} />

      {status !== 'suspended' && status !== 'rejected' ? (
        <OPButton
          label={t('home.editDetails')}
          onPress={() => navigation.navigate('EditDetails')}
          variant="secondary"
          fullWidth
        />
      ) : null}
      <OPButton
        label={t('home.rideMode')}
        onPress={() => navigation.navigate('RideHome')}
        variant="secondary"
        fullWidth
        testID="open-ride-mode"
      />
      <OPButton label={t('home.signOut')} onPress={() => void signOut()} variant="tertiary" />
    </Screen>
  );
}

function VehicleSummary({ profile }: { profile: DriverProfile }) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  if (!profile.vehicleType) {
    return null;
  }
  const vehicle = t(`driver.vehicle.${profile.vehicleType}`);
  return (
    <View style={styles.card}>
      <OPText variant="label" tone="secondary">
        {t('home.yourVehicle')}
      </OPText>
      <OPText>
        {profile.vehicleNumber
          ? t('home.vehicleSummary', { vehicle, number: profile.vehicleNumber })
          : vehicle}
      </OPText>
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    badgeRow: {
      flexDirection: 'row',
    },
    card: {
      gap: theme.spacing.xs,
      padding: theme.spacing.md,
      borderRadius: theme.radii.lg,
      borderWidth: 1,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surface,
    },
    dutyNotes: {
      gap: theme.spacing.xxs,
      paddingHorizontal: theme.spacing.xs,
    },
  });
