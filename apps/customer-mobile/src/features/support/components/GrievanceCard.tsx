import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { OPText, useThemedStyles, type Theme } from '../../../design-system';
import { useGrievance } from '../hooks/useSupport';

/**
 * Who to write to with a complaint (the Consumer Protection (E-Commerce) Rules require a named grievance officer). Shown to everyone, signed
 * in or not. Until one is appointed it says so, rather than showing a blank or someone's placeholder.
 */
export function GrievanceCard() {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const grievance = useGrievance();

  // An extra on the page: while it loads, or if it cannot, the page around it still works.
  if (!grievance.data) {
    return null;
  }

  const officer = grievance.data;
  return (
    <View style={styles.section} testID="grievance">
      <OPText variant="label" accessibilityRole="header">
        {t('support.grievance.title')}
      </OPText>
      {officer.configured ? (
        <>
          <OPText selectable>{officer.name}</OPText>
          {officer.email ? (
            <OPText selectable>{t('support.grievance.email', { value: officer.email })}</OPText>
          ) : null}
          {officer.phone ? (
            <OPText selectable>{t('support.grievance.phone', { value: officer.phone })}</OPText>
          ) : null}
          {officer.address ? (
            <OPText selectable>{t('support.grievance.address', { value: officer.address })}</OPText>
          ) : null}
          <OPText tone="secondary">
            {t('support.grievance.promise', {
              hours: officer.acknowledgeWithinHours,
              days: officer.resolveWithinDays,
            })}
          </OPText>
        </>
      ) : (
        <OPText tone="secondary">{t('support.grievance.none')}</OPText>
      )}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    section: {
      gap: theme.spacing.xxs,
    },
  });
