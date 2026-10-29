import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  InlineBanner,
  ListItem,
  LoadingState,
  ModalSheet,
  OPButton,
  OPIconButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { formatDateTime } from '../../bookings/formatDateTime';
import { newIdempotencyKey } from '../../bookings/idempotencyKey';
import { useCreateBooking } from '../../bookings/hooks/useBookings';
import { authErrorMessageKey } from '../../auth/services/errorMessages';
import { useListingAvailability } from '../hooks/useListings';

interface BookSlotSheetProps {
  visible: boolean;
  listingId: string;
  businessName: string;
  onClose: () => void;
  onBooked: () => void;
}

const MAX_GUESTS = 20;

/** Picks a slot and a guest count, then holds inventory with the partner (spec §7, §10, Phase 6). */
export function BookSlotSheet({
  visible,
  listingId,
  businessName,
  onClose,
  onBooked,
}: BookSlotSheetProps) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const slots = useListingAvailability(visible ? listingId : undefined);
  const createBooking = useCreateBooking();
  const [slotId, setSlotId] = useState<string | null>(null);
  const [guestCount, setGuestCount] = useState(1);
  // A fresh idempotency key per mount; the parent remounts this component (by changing its `key`)
  // each time it opens the sheet, so a previous attempt's key, selection and guest count never leak in.
  const [idempotencyKey] = useState(newIdempotencyKey);

  const confirm = () => {
    if (!slotId) {
      return;
    }
    createBooking.mutate(
      { listingId, slotIds: [slotId], guestCount, idempotencyKey },
      { onSuccess: onBooked },
    );
  };

  const available = slots.data?.filter((slot) => slot.availableCount > 0) ?? [];
  const selectedSlot = available.find((slot) => slot.id === slotId);
  const maxGuests = selectedSlot ? Math.min(MAX_GUESTS, selectedSlot.availableCount) : MAX_GUESTS;

  return (
    <ModalSheet
      visible={visible}
      onClose={onClose}
      title={t('listings.book.title')}
      closeLabel={t('common.close')}
    >
      <View style={styles.body}>
        {createBooking.error ? (
          <InlineBanner kind="error" message={t(authErrorMessageKey(createBooking.error))} />
        ) : null}

        {slots.isPending ? (
          <LoadingState accessibilityLabel={t('listings.detail.loading')} />
        ) : available.length === 0 ? (
          <OPText tone="secondary">{t('listings.book.noSlots')}</OPText>
        ) : (
          <>
            {available.map((slot) => (
              <ListItem
                key={slot.id}
                leadingIcon="clock"
                title={formatDateTime(slot.startAt)}
                subtitle={t('listings.book.placesLeft', { count: slot.availableCount })}
                trailing={{ kind: 'radio', selected: slot.id === slotId }}
                onPress={() => {
                  setSlotId(slot.id);
                  setGuestCount((count) => Math.min(count, slot.availableCount));
                }}
                disabled={createBooking.isPending}
              />
            ))}

            {slotId ? (
              <View style={styles.guests}>
                <OPText variant="label">{t('listings.book.guests')}</OPText>
                <View style={styles.stepper}>
                  <OPIconButton
                    icon="minus"
                    variant="tonal"
                    accessibilityLabel={t('listings.book.guests')}
                    disabled={guestCount <= 1 || createBooking.isPending}
                    onPress={() => setGuestCount((count) => Math.max(1, count - 1))}
                  />
                  <OPText variant="title">{guestCount}</OPText>
                  <OPIconButton
                    icon="plus"
                    variant="tonal"
                    accessibilityLabel={t('listings.book.guests')}
                    disabled={guestCount >= maxGuests || createBooking.isPending}
                    onPress={() => setGuestCount((count) => Math.min(maxGuests, count + 1))}
                  />
                </View>
              </View>
            ) : null}

            <OPButton
              label={
                createBooking.isPending ? t('listings.book.requesting') : t('listings.book.confirm')
              }
              onPress={confirm}
              disabled={!slotId || createBooking.isPending}
              fullWidth
            />
          </>
        )}
      </View>
    </ModalSheet>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    body: {
      gap: theme.spacing.sm,
    },
    guests: {
      gap: theme.spacing.xs,
      alignItems: 'flex-start',
    },
    stepper: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.md,
    },
  });
