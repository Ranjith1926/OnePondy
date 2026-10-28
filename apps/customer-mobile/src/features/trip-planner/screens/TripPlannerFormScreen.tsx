import { StackActions, useNavigation } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  FormField,
  InlineBanner,
  ModalScreenTemplate,
  OPButton,
  OPChip,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { placeCategoryIds, type PlaceCategoryId } from '../../tourism/categories';
import { usePlanTrip } from '../hooks/usePlanTrip';
import { tripPlannerErrorMessageKey } from '../services/errorMessages';
import type { TripBudgetBand, TripPace } from '../api/tripPlannerApi';

const paceOptions: TripPace[] = ['relaxed', 'balanced', 'packed'];
const budgetOptions: TripBudgetBand[] = ['low', 'medium', 'high'];
const datePattern = /^\d{4}-\d{2}-\d{2}$/;

/** Dates, interests, pace, budget and mobility needs for the AI trip planner (spec §7 step 1). */
export function TripPlannerFormScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const planTrip = usePlanTrip();

  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [interests, setInterests] = useState<PlaceCategoryId[]>([]);
  const [pace, setPace] = useState<TripPace>('balanced');
  const [budgetBand, setBudgetBand] = useState<TripBudgetBand>('medium');
  const [mobilityNeeds, setMobilityNeeds] = useState('');
  const [dateError, setDateError] = useState<string | null>(null);

  const toggleInterest = (id: PlaceCategoryId) =>
    setInterests((current) =>
      current.includes(id) ? current.filter((i) => i !== id) : [...current, id],
    );

  const submit = () => {
    setDateError(null);
    if (!datePattern.test(startDate) || !datePattern.test(endDate)) {
      setDateError(t('tripPlanner.datePlaceholder'));
      return;
    }
    if (endDate < startDate) {
      setDateError(t('tripPlanner.datePlaceholder'));
      return;
    }

    planTrip.mutate(
      {
        startDate,
        endDate,
        interests,
        pace,
        budgetBand,
        mobilityNeeds: mobilityNeeds.trim() || undefined,
      },
      {
        onSuccess: (draft) =>
          navigation.dispatch(StackActions.replace('ItineraryDetail', { id: draft.id })),
      },
    );
  };

  return (
    <ModalScreenTemplate
      title={t('tripPlanner.title')}
      closeLabel={t('common.close')}
      onClose={() => navigation.goBack()}
    >
      <View style={styles.body}>
        <OPText tone="secondary">{t('tripPlanner.intro')}</OPText>

        {planTrip.error ? (
          <InlineBanner kind="error" message={t(tripPlannerErrorMessageKey(planTrip.error))} />
        ) : null}

        <View style={styles.dateRow}>
          <View style={styles.dateField}>
            <FormField
              label={t('tripPlanner.startDate')}
              value={startDate}
              onChangeText={setStartDate}
              placeholder={t('tripPlanner.datePlaceholder')}
              error={dateError ?? undefined}
            />
          </View>
          <View style={styles.dateField}>
            <FormField
              label={t('tripPlanner.endDate')}
              value={endDate}
              onChangeText={setEndDate}
              placeholder={t('tripPlanner.datePlaceholder')}
            />
          </View>
        </View>

        <View style={styles.section}>
          <OPText variant="label">{t('tripPlanner.interests')}</OPText>
          <View style={styles.chips}>
            {placeCategoryIds.map((id) => (
              <OPChip
                key={id}
                label={t(`categories.${id}`)}
                selected={interests.includes(id)}
                onPress={() => toggleInterest(id)}
              />
            ))}
          </View>
        </View>

        <View style={styles.section}>
          <OPText variant="label">{t('tripPlanner.pace.title')}</OPText>
          <View style={styles.chips}>
            {paceOptions.map((option) => (
              <OPChip
                key={option}
                label={t(`tripPlanner.pace.${option}`)}
                selected={pace === option}
                onPress={() => setPace(option)}
              />
            ))}
          </View>
        </View>

        <View style={styles.section}>
          <OPText variant="label">{t('tripPlanner.budgetBand.title')}</OPText>
          <View style={styles.chips}>
            {budgetOptions.map((option) => (
              <OPChip
                key={option}
                label={t(`tripPlanner.budgetBand.${option}`)}
                selected={budgetBand === option}
                onPress={() => setBudgetBand(option)}
              />
            ))}
          </View>
        </View>

        <FormField
          label={t('tripPlanner.mobilityNeeds')}
          value={mobilityNeeds}
          onChangeText={setMobilityNeeds}
          multiline
          maxLength={300}
        />

        <OPButton
          label={t('tripPlanner.submit')}
          onPress={submit}
          loading={planTrip.isPending}
          fullWidth
        />
      </View>
    </ModalScreenTemplate>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    body: {
      gap: theme.spacing.md,
    },
    dateRow: {
      flexDirection: 'row',
      gap: theme.spacing.sm,
    },
    dateField: {
      flex: 1,
    },
    section: {
      gap: theme.spacing.xs,
    },
    chips: {
      flexDirection: 'row',
      flexWrap: 'wrap',
      gap: theme.spacing.xs,
    },
  });
