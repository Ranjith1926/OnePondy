import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';
import { StyleSheet, Switch, View } from 'react-native';

import { Screen } from '../../../components/Screen';
import {
  EmptyState,
  InlineBanner,
  OPBadge,
  OPButton,
  OPText,
  useTheme,
  useThemedStyles,
  type BadgeTone,
  type Theme,
} from '../../../design-system';
import { useSession } from '../../../session/SessionProvider';
import type { DriverApprovalStatus, DriverProfile } from '../api/driverApi';
import { useDriverProfile, useSetDuty } from '../hooks/useDriver';
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
 * Nothing about where the driver is gets collected here; location is for the length of a delivery only.
 */
export function HomeScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const theme = useTheme();
  const { user, signOut } = useSession();
  const query = useDriverProfile();
  const setDuty = useSetDuty();
  const profile = query.data;

  if (!profile?.configured || !profile.approvalStatus) {
    return null;
  }

  const status = profile.approvalStatus;
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
          <View style={styles.card}>
            <View style={styles.dutyRow}>
              <View style={styles.dutyText}>
                <OPText variant="subheading">{t('home.dutyTitle')}</OPText>
                <OPText tone="secondary">
                  {profile.isOnDuty && profile.onDutySince
                    ? t('home.dutyOn', { time: formatTime(profile.onDutySince) })
                    : t('home.dutyOff')}
                </OPText>
              </View>
              <Switch
                value={profile.isOnDuty}
                onValueChange={(onDuty) => setDuty.mutate({ onDuty, version: profile.version })}
                disabled={setDuty.isPending || (!profile.isOnDuty && !profile.canGoOnDuty)}
                accessibilityLabel={t('home.dutyLabel')}
                trackColor={{ true: theme.colors.brand.primary, false: theme.colors.borderStrong }}
              />
            </View>
            <OPText variant="caption" tone="secondary">
              {t('home.dutyExplainer')}
            </OPText>
          </View>
          {setDuty.error ? (
            <InlineBanner kind="error" message={t(driverErrorMessageKey(setDuty.error))} />
          ) : null}

          <OPText variant="heading" accessibilityRole="header">
            {t('home.jobsTitle')}
          </OPText>
          <EmptyState
            icon={profile.isOnDuty ? 'bike' : 'moon'}
            title={profile.isOnDuty ? t('home.noJobsOnTitle') : t('home.noJobsOffTitle')}
            message={profile.isOnDuty ? t('home.noJobsOnMessage') : t('home.noJobsOffMessage')}
          />
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
    dutyRow: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.md,
    },
    dutyText: {
      flex: 1,
    },
  });
