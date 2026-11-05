import { useTranslation } from 'react-i18next';
import { Linking, StyleSheet, View } from 'react-native';

import {
  EmptyState,
  InlineBanner,
  LoadingState,
  OPBadge,
  OPButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import type { JobDetail, JobOffer } from '../api/driverApi';
import { formatMoney } from '../../../format';
import { isAreaId } from '../areas';
import {
  useAcceptJob,
  useDeliverJob,
  useDriverCash,
  useJobs,
  usePickUpJob,
  useReleaseJob,
  useShareLocation,
} from '../hooks/useJobs';
import { driverErrorMessageKey } from '../services/errorMessages';

/**
 * The deliveries part of an approved driver's home: the delivery they hold, with the next step, or the open ones to take. The
 * server decides everything (who gets a delivery, which step is next); this only shows it and asks.
 */
export function JobsSection({ isOnDuty }: { isOnDuty: boolean }) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const jobs = useJobs(true);
  const accept = useAcceptJob();

  const current = jobs.data?.current ?? null;
  const open = jobs.data?.open ?? [];

  return (
    <View style={styles.section}>
      <CashHeld />
      {current ? (
        <ActiveJob job={current} />
      ) : (
        <>
          <OPText variant="heading" accessibilityRole="header">
            {t('jobs.openTitle')}
          </OPText>
          {jobs.isLoading ? (
            <LoadingState accessibilityLabel={t('jobs.openTitle')} rows={2} />
          ) : null}
          {jobs.isError && !jobs.data ? (
            <InlineBanner kind="error" message={t('jobs.loadError')} />
          ) : null}
          {accept.error ? (
            <InlineBanner kind="warning" message={t(driverErrorMessageKey(accept.error))} />
          ) : null}
          {open.map((offer) => (
            <OfferCard
              key={offer.id}
              offer={offer}
              onAccept={() => accept.mutate(offer.id)}
              busy={accept.isPending}
            />
          ))}
          {!jobs.isLoading && !jobs.isError && open.length === 0 ? (
            <EmptyState
              icon={isOnDuty ? 'bike' : 'moon'}
              title={isOnDuty ? t('jobs.emptyOnTitle') : t('jobs.emptyOffTitle')}
              message={isOnDuty ? t('jobs.emptyOnMessage') : t('jobs.emptyOffMessage')}
            />
          ) : null}
        </>
      )}
    </View>
  );
}

function OfferCard({
  offer,
  onAccept,
  busy,
}: {
  offer: JobOffer;
  onAccept: () => void;
  busy: boolean;
}) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  return (
    <View style={styles.card}>
      <OPText variant="subheading">
        {t('jobs.offerRoute', {
          restaurant: offer.restaurantName,
          area: isAreaId(offer.deliveryArea)
            ? t(`areas.${offer.deliveryArea}`)
            : offer.deliveryArea,
        })}
      </OPText>
      {offer.restaurantAddress ? <OPText tone="secondary">{offer.restaurantAddress}</OPText> : null}
      <OPText tone="secondary">{t('jobs.offerItems', { count: offer.itemCount })}</OPText>
      {offer.cashToCollect !== null ? (
        <View style={styles.cashRow}>
          <OPBadge label={t('jobs.cashBadge')} tone="warning" />
          <OPText variant="bodyStrong">
            {t('jobs.cashToCollect', { amount: formatMoney(offer.cashToCollect) })}
          </OPText>
        </View>
      ) : null}
      <OPButton label={t('jobs.accept')} onPress={onAccept} loading={busy} fullWidth />
    </View>
  );
}

/** The cash the driver is holding for OnePondy, so they know what to hand in. Nothing is shown while they hold none. */
function CashHeld() {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const cash = useDriverCash(true).data;
  if (!cash || cash.held <= 0) {
    return null;
  }
  return (
    <View style={styles.card}>
      <OPText variant="subheading">{t('jobs.cashHeld', { amount: formatMoney(cash.held) })}</OPText>
      <OPText variant="caption" tone="secondary">
        {t('jobs.cashHeldHint', { limit: formatMoney(cash.limit) })}
      </OPText>
    </View>
  );
}

/** Opens a place, given as words, in the driver's Maps app. Only the driver's own tap sends the address anywhere. */
function openInMaps(place: string): void {
  void Linking.openURL(
    `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(place)}`,
  );
}

function ActiveJob({ job }: { job: JobDetail }) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const pickUp = usePickUpJob();
  const deliver = useDeliverJob();
  const release = useReleaseJob();
  const { denied } = useShareLocation(job.id);

  const collecting = job.status === 'accepted';
  const deliveryAreaName = isAreaId(job.deliveryArea)
    ? t(`areas.${job.deliveryArea}`)
    : job.deliveryArea;
  const cashToCollect = job.cashToCollect;
  const error = pickUp.error ?? deliver.error ?? release.error;
  const busy = pickUp.isPending || deliver.isPending || release.isPending;

  return (
    <View style={styles.card}>
      <OPText variant="heading" accessibilityRole="header">
        {t('jobs.currentTitle')}
      </OPText>
      <OPText variant="label" tone="secondary">
        {collecting ? t('jobs.stepCollect') : t('jobs.stepDeliver')}
      </OPText>
      {cashToCollect !== null && collecting ? (
        <InlineBanner
          kind="info"
          message={t('jobs.cashNote', { amount: formatMoney(cashToCollect) })}
        />
      ) : null}

      {collecting ? (
        <>
          <OPText variant="subheading">
            {t('jobs.collectAt', { restaurant: job.restaurantName })}
          </OPText>
          {job.restaurantAddress ? (
            <OPText>{t('jobs.restaurantAddress', { address: job.restaurantAddress })}</OPText>
          ) : null}
          <OPButton
            label={t('jobs.openInMaps')}
            variant="secondary"
            onPress={() =>
              openInMaps([job.restaurantName, job.restaurantAddress].filter(Boolean).join(', '))
            }
          />
          {job.restaurantPhone ? (
            <OPText>{t('jobs.restaurantPhone', { phone: job.restaurantPhone })}</OPText>
          ) : null}
          <OPText variant="label">{t('jobs.bagTitle')}</OPText>
          {job.items.map((item) => (
            <OPText key={item.name}>
              {t('jobs.bagLine', { quantity: item.quantity, name: item.name })}
            </OPText>
          ))}
        </>
      ) : (
        <>
          <OPText variant="label">{t('jobs.deliverTo')}</OPText>
          <OPText variant="subheading">{job.deliveryAddress}</OPText>
          <OPText tone="secondary">{deliveryAreaName}</OPText>
          <OPButton
            label={t('jobs.openInMaps')}
            variant="secondary"
            onPress={() => openInMaps(`${job.deliveryAddress}, ${deliveryAreaName}`)}
          />
          {job.deliveryNote ? <OPText>{t('jobs.note', { note: job.deliveryNote })}</OPText> : null}
          <OPText>{t('jobs.customerPhone', { phone: job.contactPhone })}</OPText>
          {cashToCollect !== null ? (
            <View style={styles.cashDue}>
              <OPText variant="heading">
                {t('jobs.collectCash', { amount: formatMoney(cashToCollect) })}
              </OPText>
            </View>
          ) : null}
        </>
      )}

      {denied ? (
        <InlineBanner kind="warning" message={t('jobs.locationOff')} />
      ) : (
        <OPText variant="caption" tone="secondary">
          {t('jobs.locationNote')}
        </OPText>
      )}
      {error ? <InlineBanner kind="error" message={t(driverErrorMessageKey(error))} /> : null}

      {collecting ? (
        <>
          <OPButton
            label={t('jobs.pickedUp')}
            onPress={() => pickUp.mutate(job.id)}
            loading={pickUp.isPending}
            disabled={busy}
            fullWidth
            size="lg"
          />
          <OPButton
            label={t('jobs.release')}
            onPress={() => release.mutate(job.id)}
            loading={release.isPending}
            disabled={busy}
            variant="tertiary"
            accessibilityHint={t('jobs.releaseHint')}
          />
        </>
      ) : (
        <OPButton
          label={
            cashToCollect !== null
              ? t('jobs.deliveredCash', { amount: formatMoney(cashToCollect) })
              : t('jobs.delivered')
          }
          onPress={() => deliver.mutate({ id: job.id, cashCollected: cashToCollect ?? undefined })}
          loading={deliver.isPending}
          disabled={busy}
          fullWidth
          size="lg"
        />
      )}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    section: {
      gap: theme.spacing.md,
    },
    cashRow: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.xs,
    },
    cashDue: {
      padding: theme.spacing.sm,
      borderRadius: theme.radii.md,
      backgroundColor: theme.colors.warningContainer,
    },
    card: {
      gap: theme.spacing.xs,
      padding: theme.spacing.md,
      borderRadius: theme.radii.lg,
      borderWidth: 1,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surface,
    },
  });
