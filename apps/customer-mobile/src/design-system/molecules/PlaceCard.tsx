import { Pressable, StyleSheet, View, type ImageSourcePropType } from 'react-native';

import { OPBadge } from '../atoms/OPBadge';
import type { IconName } from '../atoms/OPIcon';
import { OPImage } from '../atoms/OPImage';
import { OPText } from '../atoms/OPText';
import { useTheme, useThemedStyles } from '../theme/ThemeProvider';
import type { Theme } from '../theme/theme';

interface PlaceCardProps {
  title: string;
  subtitle: string;
  fallbackIcon: IconName;
  imageSource?: ImageSourcePropType;
  /** e.g. "Sample"; shown on the image and read out with the title. */
  badgeLabel?: string;
  variant?: 'carousel' | 'list';
  /** Omit to render a non-interactive card. */
  onPress?: () => void;
}

export function PlaceCard({
  title,
  subtitle,
  fallbackIcon,
  imageSource,
  badgeLabel,
  variant = 'carousel',
  onPress,
}: PlaceCardProps) {
  const theme = useTheme();
  const styles = useThemedStyles(createStyles);
  const label = [title, subtitle, badgeLabel].filter(Boolean).join(', ');
  const isList = variant === 'list';

  return (
    <Pressable
      accessible
      accessibilityRole={onPress ? 'button' : undefined}
      accessibilityLabel={label}
      disabled={!onPress}
      onPress={onPress}
      style={({ pressed }) => [
        styles.card,
        isList ? styles.listCard : styles.carouselCard,
        theme.elevation.card ? { boxShadow: theme.elevation.card } : null,
        pressed && onPress ? styles.pressed : null,
      ]}
    >
      <View style={isList ? styles.listImage : null}>
        <OPImage
          source={imageSource}
          fallbackIcon={fallbackIcon}
          aspectRatio={isList ? 1 : 4 / 3}
          radius="md"
        />
        {/* The list thumbnail is too small for an overlay badge, so list cards show it with the text. */}
        {badgeLabel && !isList ? (
          <View style={styles.badge}>
            <OPBadge label={badgeLabel} tone="neutral" />
          </View>
        ) : null}
      </View>
      <View style={isList ? styles.listText : styles.carouselText}>
        <OPText variant="subheading" numberOfLines={3}>
          {title}
        </OPText>
        <OPText variant="caption" tone="secondary" numberOfLines={3}>
          {subtitle}
        </OPText>
        {badgeLabel && isList ? <OPBadge label={badgeLabel} tone="neutral" /> : null}
      </View>
    </Pressable>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    card: {
      borderRadius: theme.radii.lg,
      borderWidth: 1,
      borderColor: theme.colors.border,
      backgroundColor: theme.colors.surface,
      padding: theme.spacing.xs,
    },
    carouselCard: {
      width: 232,
      gap: theme.spacing.xs,
    },
    listCard: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.sm,
    },
    pressed: {
      opacity: theme.motion.pressedOpacity,
    },
    listImage: {
      width: 88,
    },
    badge: {
      position: 'absolute',
      top: theme.spacing.xs,
      left: theme.spacing.xs,
    },
    carouselText: {
      gap: 2,
      paddingHorizontal: theme.spacing.xxs,
      paddingBottom: theme.spacing.xxs,
    },
    listText: {
      flex: 1,
      gap: 2,
      paddingRight: theme.spacing.xs,
    },
  });
