import { useNavigation, type StaticScreenProps } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ScrollView, Share, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { apiBaseUrl } from '../../../api/config';
import {
  ErrorState,
  FormField,
  InlineBanner,
  ListItem,
  LoadingState,
  ModalSheet,
  OPBadge,
  OPButton,
  OPIconButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { categoryIcons, isKnownCategory } from '../../tourism/categories';
import type { ItineraryStop } from '../api/itinerariesApi';
import {
  useConfirmItinerary,
  useDeleteItinerary,
  useItinerary,
  useRemoveStop,
  useReorderStops,
  useSetItineraryArchived,
  useSharing,
  useUpdateItinerary,
} from '../hooks/useItineraries';

type Props = StaticScreenProps<{ id: string }>;

function groupByDay(stops: readonly ItineraryStop[]): [number, ItineraryStop[]][] {
  const days = new Map<number, ItineraryStop[]>();
  for (const stop of [...stops].sort(
    (a, b) => a.dayNumber - b.dayNumber || a.stopOrder - b.stopOrder,
  )) {
    const list = days.get(stop.dayNumber) ?? [];
    list.push(stop);
    days.set(stop.dayNumber, list);
  }
  return [...days.entries()].sort(([a], [b]) => a - b);
}

/** A trip's stops by day, with reordering, sharing and the AI-assisted review step (spec §7, §10 Phase 4). */
export function ItineraryDetailScreen({ route }: Props) {
  const { id } = route.params;
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const itinerary = useItinerary(id);
  const confirm = useConfirmItinerary(id);
  const removeStop = useRemoveStop(id);
  const reorderStops = useReorderStops(id);
  const setArchived = useSetItineraryArchived(id);
  const deleteItinerary = useDeleteItinerary();
  const updateItinerary = useUpdateItinerary(id);
  const sharing = useSharing(id);
  const [renaming, setRenaming] = useState(false);
  const [draftTitle, setDraftTitle] = useState('');
  const [confirmingDelete, setConfirmingDelete] = useState(false);

  const header = (
    <View style={styles.header}>
      <OPIconButton
        icon="arrow-left"
        accessibilityLabel={t('common.back')}
        onPress={() => navigation.goBack()}
      />
    </View>
  );

  if (itinerary.isPending) {
    return (
      <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
        {header}
        <View style={styles.centered}>
          <LoadingState accessibilityLabel={t('itineraries.loading')} />
        </View>
      </SafeAreaView>
    );
  }

  if (itinerary.isError) {
    return (
      <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
        {header}
        <ErrorState
          title={t('itineraries.error.title')}
          message={t('itineraries.error.message')}
          retryLabel={t('common.retry')}
          onRetry={() => void itinerary.refetch()}
          retrying={itinerary.isFetching}
        />
      </SafeAreaView>
    );
  }

  const data = itinerary.data;
  const days = groupByDay(data.stops);

  const moveStop = (dayNumber: number, index: number, direction: -1 | 1) => {
    const stops = days.find(([day]) => day === dayNumber)?.[1];
    if (!stops) return;
    const otherIndex = index + direction;
    if (otherIndex < 0 || otherIndex >= stops.length) return;
    const a = stops[index];
    const b = stops[otherIndex];
    reorderStops.mutate([
      { stopId: a.id, dayNumber: a.dayNumber, stopOrder: b.stopOrder },
      { stopId: b.id, dayNumber: b.dayNumber, stopOrder: a.stopOrder },
    ]);
  };

  const startRename = () => {
    setDraftTitle(data.title);
    setRenaming(true);
  };

  const saveRename = () => {
    const title = draftTitle.trim();
    if (!title) return;
    updateItinerary.mutate(
      { title, startDate: data.startDate, endDate: data.endDate },
      { onSuccess: () => setRenaming(false) },
    );
  };

  const shareTrip = () => {
    void Share.share({ message: `${apiBaseUrl}/api/v1/itineraries/shared/${data.shareToken}` });
  };

  return (
    <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
      {header}
      <ScrollView contentContainerStyle={styles.content}>
        <View style={[styles.inset, styles.titleBlock]}>
          <View style={styles.titleRow}>
            <OPText variant="title" accessibilityRole="header" style={styles.titleText}>
              {data.title}
            </OPText>
            <OPIconButton
              icon="pencil"
              accessibilityLabel={t('itineraries.rename')}
              onPress={startRename}
              variant="tonal"
              size="sm"
            />
          </View>
          <View style={styles.badges}>
            <OPBadge
              label={t(`itineraries.status.${data.status}`)}
              tone={
                data.status === 'archived'
                  ? 'neutral'
                  : data.status === 'draft'
                    ? 'accent'
                    : 'brand'
              }
            />
            {data.source === 'aiAssisted' ? (
              <OPBadge label={t('itineraries.aiAssistedLabel')} tone="info" />
            ) : null}
          </View>
          {data.startDate && data.endDate ? (
            <OPText tone="secondary">{`${data.startDate} – ${data.endDate}`}</OPText>
          ) : null}
        </View>

        {data.status === 'draft' ? (
          <View style={styles.inset}>
            <InlineBanner kind="info" message={t('itineraries.aiAssistedBanner')} />
            <View style={styles.saveButton}>
              <OPButton
                label={t('itineraries.saveTrip')}
                icon="save"
                onPress={() => confirm.mutate()}
                loading={confirm.isPending}
                fullWidth
              />
            </View>
          </View>
        ) : null}

        <View style={[styles.inset, styles.stops]}>
          {days.length === 0 ? (
            <OPText tone="secondary">{t('itineraries.noStops')}</OPText>
          ) : (
            days.map(([dayNumber, stops]) => (
              <View key={dayNumber} style={styles.day}>
                <OPText variant="label" tone="secondary">
                  {t('itineraries.dayLabel', { day: dayNumber })}
                </OPText>
                {stops.map((stop, index) => (
                  <ListItem
                    key={stop.id}
                    title={stop.place.name}
                    subtitle={
                      isKnownCategory(stop.place.categoryCode)
                        ? t(`categoryNames.${stop.place.categoryCode}`)
                        : stop.place.categoryCode
                    }
                    leadingIcon={
                      isKnownCategory(stop.place.categoryCode)
                        ? categoryIcons[stop.place.categoryCode]
                        : 'map-pin'
                    }
                    trailing={{
                      kind: 'custom',
                      node: (
                        <View style={styles.stopActions}>
                          <OPIconButton
                            icon="chevron-up"
                            size="sm"
                            accessibilityLabel={t('itineraries.moveUp')}
                            disabled={index === 0 || reorderStops.isPending}
                            onPress={() => moveStop(dayNumber, index, -1)}
                          />
                          <OPIconButton
                            icon="chevron-down"
                            size="sm"
                            accessibilityLabel={t('itineraries.moveDown')}
                            disabled={index === stops.length - 1 || reorderStops.isPending}
                            onPress={() => moveStop(dayNumber, index, 1)}
                          />
                          <OPIconButton
                            icon="trash-2"
                            size="sm"
                            accessibilityLabel={t('itineraries.removeStop')}
                            disabled={removeStop.isPending}
                            onPress={() => removeStop.mutate(stop.id)}
                          />
                        </View>
                      ),
                    }}
                  />
                ))}
              </View>
            ))
          )}
        </View>

        <View style={[styles.inset, styles.actions]}>
          <OPButton
            label={data.shareToken ? t('itineraries.share.copy') : t('itineraries.share.enable')}
            icon={data.shareToken ? 'share-2' : 'link'}
            variant="secondary"
            onPress={() => (data.shareToken ? shareTrip() : sharing.enable.mutate())}
            loading={sharing.enable.isPending}
          />
          {data.shareToken ? (
            <OPButton
              label={t('itineraries.share.disable')}
              variant="tertiary"
              onPress={() => sharing.disable.mutate()}
              loading={sharing.disable.isPending}
            />
          ) : null}
          <OPButton
            label={
              data.status === 'archived' ? t('itineraries.unarchive') : t('itineraries.archive')
            }
            icon={data.status === 'archived' ? 'archive-restore' : 'archive'}
            variant="secondary"
            onPress={() => setArchived.mutate(data.status !== 'archived')}
            loading={setArchived.isPending}
          />
          <OPButton
            label={t('itineraries.delete')}
            icon="trash-2"
            variant="danger"
            onPress={() => setConfirmingDelete(true)}
          />
        </View>
      </ScrollView>

      <ModalSheet
        visible={renaming}
        onClose={() => setRenaming(false)}
        title={t('itineraries.rename')}
        closeLabel={t('common.close')}
      >
        <View style={styles.renameBody}>
          <FormField
            label={t('itineraries.titleLabel')}
            value={draftTitle}
            onChangeText={setDraftTitle}
            autoFocus
            maxLength={180}
          />
          <OPButton
            label={t('editProfile.save')}
            onPress={saveRename}
            loading={updateItinerary.isPending}
            disabled={!draftTitle.trim()}
            fullWidth
          />
        </View>
      </ModalSheet>

      <ModalSheet
        visible={confirmingDelete}
        onClose={() => setConfirmingDelete(false)}
        title={t('itineraries.deleteConfirm.title')}
        closeLabel={t('common.close')}
      >
        <View style={styles.renameBody}>
          <OPText tone="secondary">{t('itineraries.deleteConfirm.message')}</OPText>
          <OPButton
            label={t('itineraries.deleteConfirm.confirm')}
            variant="danger"
            loading={deleteItinerary.isPending}
            onPress={() => deleteItinerary.mutate(id, { onSuccess: () => navigation.goBack() })}
            fullWidth
          />
          <OPButton
            label={t('itineraries.deleteConfirm.cancel')}
            variant="tertiary"
            onPress={() => setConfirmingDelete(false)}
            fullWidth
          />
        </View>
      </ModalSheet>
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
    centered: {
      flex: 1,
      justifyContent: 'center',
    },
    inset: {
      paddingHorizontal: theme.layout.screenPadding,
      gap: theme.spacing.sm,
    },
    content: {
      paddingBottom: theme.spacing.xxl,
      gap: theme.spacing.md,
    },
    titleBlock: {
      gap: theme.spacing.xs,
    },
    titleRow: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.sm,
    },
    titleText: {
      flex: 1,
    },
    badges: {
      flexDirection: 'row',
      gap: theme.spacing.xxs,
    },
    saveButton: {
      marginTop: theme.spacing.xs,
    },
    stops: {
      gap: theme.spacing.md,
    },
    day: {
      gap: theme.spacing.xxs,
    },
    stopActions: {
      flexDirection: 'row',
      gap: theme.spacing.xxs,
    },
    actions: {
      flexDirection: 'row',
      flexWrap: 'wrap',
      gap: theme.spacing.sm,
    },
    renameBody: {
      gap: theme.spacing.sm,
      paddingBottom: theme.spacing.md,
    },
  });
