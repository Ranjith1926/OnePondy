import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  OPButton,
  OPIcon,
  OPText,
  useLargeText,
  useTheme,
  useThemedStyles,
  type IconName,
  type Theme,
} from '../../../design-system';
import { completeOnboarding, useAppDispatch } from '../../../state';

const steps: readonly { key: 'discover' | 'plan' | 'oneApp'; icon: IconName }[] = [
  { key: 'discover', icon: 'map' },
  { key: 'plan', icon: 'route' },
  { key: 'oneApp', icon: 'layout-grid' },
];

export function OnboardingScreen() {
  const { t } = useTranslation();
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const dispatch = useAppDispatch();
  // A smaller illustration leaves room for the text when the system font is large.
  const largeText = useLargeText();
  const [index, setIndex] = useState(0);
  const step = steps[index] ?? steps[0]!;
  const isLast = index === steps.length - 1;
  const finish = () => dispatch(completeOnboarding());

  return (
    <SafeAreaView style={styles.screen}>
      <View style={styles.topBar}>
        {!isLast ? (
          <OPButton label={t('onboarding.skip')} onPress={finish} variant="tertiary" size="sm" />
        ) : null}
      </View>
      <ScrollView contentContainerStyle={styles.content}>
        <View style={[styles.illustration, largeText ? styles.illustrationCompact : null]}>
          <OPIcon name={step.icon} size={largeText ? 44 : 64} color={theme.colors.brand.primary} />
        </View>
        <View accessibilityLiveRegion="polite" style={styles.copy}>
          <OPText variant="display" align="center" accessibilityRole="header">
            {t(`onboarding.steps.${step.key}.title`)}
          </OPText>
          <OPText tone="secondary" align="center">
            {t(`onboarding.steps.${step.key}.body`)}
          </OPText>
        </View>
      </ScrollView>
      <View style={styles.footer}>
        <View
          accessible
          accessibilityRole="text"
          accessibilityLabel={t('onboarding.progress', { current: index + 1, total: steps.length })}
          style={styles.dots}
        >
          {steps.map((item, dotIndex) => (
            <View
              key={item.key}
              style={[styles.dot, dotIndex === index ? styles.dotActive : null]}
            />
          ))}
        </View>
        <OPButton
          label={isLast ? t('onboarding.getStarted') : t('onboarding.next')}
          onPress={isLast ? finish : () => setIndex(index + 1)}
          icon={isLast ? undefined : 'arrow-right'}
          iconPosition="end"
          size="lg"
          fullWidth
        />
      </View>
    </SafeAreaView>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    screen: {
      flex: 1,
      backgroundColor: theme.colors.background,
    },
    topBar: {
      minHeight: theme.layout.minTouchTarget,
      alignItems: 'flex-end',
      justifyContent: 'center',
      paddingHorizontal: theme.spacing.sm,
    },
    content: {
      flexGrow: 1,
      alignItems: 'center',
      justifyContent: 'center',
      gap: theme.spacing.xl,
      paddingHorizontal: theme.spacing.xl,
      paddingVertical: theme.spacing.xl,
    },
    illustration: {
      width: 160,
      height: 160,
      borderRadius: 80,
      alignItems: 'center',
      justifyContent: 'center',
      backgroundColor: theme.colors.brand.primaryContainer,
    },
    illustrationCompact: {
      width: 104,
      height: 104,
      borderRadius: 52,
    },
    copy: {
      gap: theme.spacing.sm,
      maxWidth: 420,
    },
    footer: {
      gap: theme.spacing.lg,
      paddingHorizontal: theme.spacing.xl,
      paddingBottom: theme.spacing.lg,
    },
    dots: {
      flexDirection: 'row',
      alignSelf: 'center',
      gap: theme.spacing.xs,
    },
    dot: {
      width: 8,
      height: 8,
      borderRadius: 4,
      backgroundColor: theme.colors.borderStrong,
    },
    dotActive: {
      width: 24,
      backgroundColor: theme.colors.brand.primary,
    },
  });
