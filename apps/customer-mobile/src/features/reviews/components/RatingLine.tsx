import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { OPText, useThemedStyles, type Theme } from '../../../design-system';
import type { RatingSummary } from '../api/reviewsApi';
import { StarRating } from './StarRating';

/** "★★★★☆ 4.0 · 12 reviews", or nothing while there are too few published reviews for an average (the server leaves it out). */
export function RatingLine({ rating }: { rating: RatingSummary | null | undefined }) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  if (!rating) {
    return null;
  }

  return (
    <View style={styles.row} testID="rating-line">
      <StarRating value={Math.round(rating.average)} />
      <OPText
        tone="secondary"
        accessibilityLabel={t('reviews.ratingSummary', {
          average: rating.average.toFixed(1),
          count: rating.count,
        })}
      >
        {t('reviews.ratingShort', { average: rating.average.toFixed(1), count: rating.count })}
      </OPText>
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    row: {
      flexDirection: 'row',
      alignItems: 'center',
      gap: theme.spacing.xs,
    },
  });
