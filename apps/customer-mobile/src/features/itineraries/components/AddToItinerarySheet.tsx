import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  FormField,
  InlineBanner,
  ListItem,
  ModalSheet,
  OPButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { authErrorMessageKey } from '../../auth/services/errorMessages';
import { useAddStop, useCreateItinerary, useItineraries } from '../hooks/useItineraries';

interface AddToItinerarySheetProps {
  visible: boolean;
  placeId: string | null;
  onClose: () => void;
  /** Shown as a confirmation banner once the place is added. */
  onAdded: (itineraryTitle: string) => void;
}

/** Lets a signed-in customer add a place to an existing trip, or start a new one (spec §10, Phase 4). */
export function AddToItinerarySheet({
  visible,
  placeId,
  onClose,
  onAdded,
}: AddToItinerarySheetProps) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const itineraries = useItineraries(false);
  const createItinerary = useCreateItinerary();
  const [creating, setCreating] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const addStop = useAddStop(placeId ?? '');

  const close = () => {
    setCreating(false);
    setNewTitle('');
    onClose();
  };

  const addTo = (itineraryId: string, title: string) => {
    if (!placeId) return;
    addStop.mutate(
      { placeId },
      {
        onSuccess: () => {
          close();
          onAdded(title);
        },
      },
    );
  };

  const createAndAdd = () => {
    const title = newTitle.trim();
    if (!title || !placeId) return;
    createItinerary.mutate(
      { title },
      {
        onSuccess: (created) => addTo(created.id, created.title),
      },
    );
  };

  const busy = addStop.isPending || createItinerary.isPending;

  return (
    <ModalSheet
      visible={visible}
      onClose={close}
      title={t('itineraries.addToTitle')}
      closeLabel={t('common.close')}
    >
      <View style={styles.body}>
        {addStop.error ? (
          <InlineBanner kind="error" message={t(authErrorMessageKey(addStop.error))} />
        ) : null}
        {!creating ? (
          <>
            {itineraries.data
              ?.filter((i) => i.status !== 'archived')
              .map((itinerary) => (
                <ListItem
                  key={itinerary.id}
                  leadingIcon="map"
                  title={itinerary.title}
                  subtitle={t('itineraries.stopCount', { count: itinerary.stopCount })}
                  trailing={{ kind: 'chevron' }}
                  onPress={() => addTo(itinerary.id, itinerary.title)}
                  disabled={busy}
                />
              ))}
            {itineraries.data?.length === 0 ? (
              <OPText tone="secondary">{t('itineraries.noneYet')}</OPText>
            ) : null}
            <OPButton
              label={t('itineraries.newTrip')}
              icon="plus"
              variant="secondary"
              onPress={() => setCreating(true)}
              fullWidth
            />
          </>
        ) : (
          <>
            <FormField
              label={t('itineraries.titleLabel')}
              value={newTitle}
              onChangeText={setNewTitle}
              autoFocus
              maxLength={180}
              returnKeyType="done"
              onSubmitEditing={createAndAdd}
            />
            <OPButton
              label={t('itineraries.createAndAdd')}
              onPress={createAndAdd}
              loading={busy}
              disabled={!newTitle.trim()}
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
      paddingBottom: theme.spacing.md,
    },
  });
