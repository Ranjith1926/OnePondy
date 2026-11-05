import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, Switch, View } from 'react-native';

import { OPText, useTheme, useThemedStyles, type Theme } from '../../../design-system';

interface DutyPillProps {
  onDuty: boolean;
  /** True while the switch cannot be used: a change is in flight, or the server would refuse it. */
  locked: boolean;
  onChange: (onDuty: boolean) => void;
}

/**
 * The duty switch as the first thing on the home screen: a large pill that says plainly whether the driver is on or off duty, green
 * while on duty. The whole pill can be pressed, not only the small switch; screen readers still get one control, the switch.
 */
export function DutyPill({ onDuty, locked, onChange }: DutyPillProps) {
  const { t } = useTranslation();
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);

  return (
    <Pressable
      accessible={false}
      disabled={locked}
      onPress={() => onChange(!onDuty)}
      style={({ pressed }) => [
        styles.pill,
        onDuty ? styles.pillOn : styles.pillOff,
        locked ? styles.locked : null,
        pressed ? styles.pressed : null,
      ]}
    >
      <View style={styles.labelBox}>
        <OPText
          variant="heading"
          style={onDuty ? styles.labelOn : styles.labelOff}
          importantForAccessibility="no"
        >
          {onDuty ? t('home.dutyPillOn') : t('home.dutyPillOff')}
        </OPText>
      </View>
      <Switch
        value={onDuty}
        onValueChange={onChange}
        disabled={locked}
        accessibilityState={{ disabled: locked }}
        accessibilityLabel={t('home.dutyLabel')}
        trackColor={{ true: theme.colors.success, false: theme.colors.borderStrong }}
      />
    </Pressable>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    pill: {
      flexDirection: 'row',
      alignItems: 'center',
      justifyContent: 'space-between',
      gap: theme.spacing.md,
      minHeight: 64,
      paddingVertical: theme.spacing.xs,
      paddingLeft: theme.spacing.lg,
      paddingRight: theme.spacing.md,
      borderRadius: theme.radii.pill,
      borderWidth: 2,
    },
    pillOn: {
      borderColor: theme.colors.success,
      backgroundColor: theme.colors.successContainer,
    },
    pillOff: {
      borderColor: theme.colors.borderStrong,
      backgroundColor: theme.colors.surfaceMuted,
    },
    labelBox: { flex: 1 },
    labelOn: { color: theme.colors.onSuccessContainer, letterSpacing: 1 },
    labelOff: { color: theme.colors.text.secondary, letterSpacing: 1 },
    locked: { opacity: 0.7 },
    pressed: { opacity: 0.85 },
  });
