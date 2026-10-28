import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, View } from 'react-native';

import {
  OPBadge,
  OPIcon,
  OPText,
  useTheme,
  useThemedStyles,
  type Theme,
} from '../../../design-system';

interface TripPlannerCardProps {
  available: boolean;
  onPress: () => void;
}

/** Entry point for the AI trip planner (spec §7, Phase 5); explains its status until it is available. */
export function TripPlannerCard({ available, onPress }: TripPlannerCardProps) {
  const { t } = useTranslation();
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);

  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={
        available
          ? t('home.tripPlanner.title')
          : `${t('home.tripPlanner.title')}, ${t('common.comingSoon')}`
      }
      accessibilityHint={t('home.tripPlanner.body')}
      onPress={onPress}
      style={({ pressed }) => [styles.card, pressed ? styles.pressed : null]}
    >
      <View style={styles.iconTile}>
        <OPIcon name="sparkles" size={24} color={theme.colors.brand.onSecondaryContainer} />
      </View>
      <View style={styles.text}>
        <OPText variant="subheading">{t('home.tripPlanner.title')}</OPText>
        <OPText variant="caption" tone="secondary">
          {t('home.tripPlanner.body')}
        </OPText>
        {available ? null : <OPBadge label={t('common.comingSoon')} tone="accent" />}
      </View>
      <OPIcon name="chevron-right" size={20} color={theme.colors.text.secondary} />
    </Pressable>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    card: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.sm,
      marginHorizontal: theme.layout.screenPadding,
      padding: theme.spacing.md,
      borderRadius: theme.radii.lg,
      borderWidth: 1,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surface,
    },
    pressed: {
      backgroundColor: theme.colors.surfaceMuted,
    },
    iconTile: {
      width: 48,
      height: 48,
      alignItems: 'center',
      justifyContent: 'center',
      borderRadius: theme.radii.md,
      backgroundColor: theme.colors.brand.secondaryContainer,
    },
    text: {
      flex: 1,
      gap: theme.spacing.xxs,
    },
  });
