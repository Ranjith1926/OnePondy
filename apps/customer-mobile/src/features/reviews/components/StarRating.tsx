import { useTranslation } from 'react-i18next';
import { Pressable, StyleSheet, View } from 'react-native';

import { OPText, useThemedStyles, type Theme } from '../../../design-system';

const stars = [1, 2, 3, 4, 5] as const;

interface StarRatingProps {
  /** 0 means nothing chosen yet. */
  value: number;
  /** Omit to show a rating; pass it to let the person choose one. */
  onChange?: (value: number) => void;
}

/**
 * A rating from 1 to 5, shown or chosen. The glyphs are for the eye; a screen reader is told "4 stars", or for the chooser
 * offered five options of which one is selected.
 */
export function StarRating({ value, onChange }: StarRatingProps) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);

  if (!onChange) {
    return (
      <View
        accessible
        accessibilityLabel={t('reviews.stars', { count: value })}
        style={styles.row}
        testID="star-rating"
      >
        {stars.map((star) => (
          <OPText key={star} style={styles.star} importantForAccessibility="no">
            {star <= value ? '★' : '☆'}
          </OPText>
        ))}
      </View>
    );
  }

  return (
    <View accessibilityRole="radiogroup" style={styles.row}>
      {stars.map((star) => (
        <Pressable
          key={star}
          accessibilityRole="radio"
          accessibilityLabel={t('reviews.stars', { count: star })}
          accessibilityState={{ checked: value === star }}
          onPress={() => onChange(star)}
          style={styles.target}
          testID={`star-${star}`}
        >
          <OPText style={styles.inputStar}>{star <= value ? '★' : '☆'}</OPText>
        </Pressable>
      ))}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    row: {
      flexDirection: 'row',
      alignItems: 'center',
    },
    star: {
      color: theme.colors.brand.secondary,
      fontSize: 18,
      lineHeight: 22,
    },
    target: {
      minWidth: theme.layout.minTouchTarget,
      minHeight: theme.layout.minTouchTarget,
      alignItems: 'center',
      justifyContent: 'center',
    },
    inputStar: {
      color: theme.colors.brand.secondary,
      fontSize: 32,
      lineHeight: 38,
    },
  });
