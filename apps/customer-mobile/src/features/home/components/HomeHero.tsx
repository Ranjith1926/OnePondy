import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  OPButton,
  OPIcon,
  OPText,
  useTheme,
  useThemedStyles,
  type Theme,
} from '../../../design-system';

interface HomeHeroProps {
  onExplore: () => void;
}

export function HomeHero({ onExplore }: HomeHeroProps) {
  const { t } = useTranslation();
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);

  return (
    <View style={styles.hero}>
      <View style={styles.decoration}>
        <OPIcon name="sunrise" size={140} color={theme.colors.brand.onPrimary} />
      </View>
      <OPText variant="label" tone="onPrimary">
        {t('home.hero.eyebrow')}
      </OPText>
      <OPText variant="title" tone="onPrimary" accessibilityRole="header">
        {t('home.hero.title')}
      </OPText>
      <OPText tone="onPrimary" style={styles.body}>
        {t('home.hero.body')}
      </OPText>
      <View style={styles.action}>
        <OPButton
          label={t('home.hero.action')}
          onPress={onExplore}
          variant="secondary"
          icon="arrow-right"
          iconPosition="end"
        />
      </View>
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    hero: {
      overflow: 'hidden',
      gap: theme.spacing.xs,
      marginHorizontal: theme.layout.screenPadding,
      padding: theme.spacing.xl,
      borderRadius: theme.radii.xl,
      backgroundColor: theme.colors.brand.primary,
    },
    decoration: {
      position: 'absolute',
      right: -24,
      bottom: -28,
      opacity: 0.14,
    },
    body: {
      maxWidth: 320,
    },
    action: {
      alignItems: 'flex-start',
      marginTop: theme.spacing.sm,
    },
  });
