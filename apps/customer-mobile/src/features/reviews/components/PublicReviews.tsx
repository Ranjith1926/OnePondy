import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { OPText, useThemedStyles, type Theme } from '../../../design-system';
import { formatDateTime } from '../../bookings/formatDateTime';
import type { PublicReview, ReviewSubject } from '../api/reviewsApi';
import { usePublicReviews } from '../hooks/useReviews';
import { RatingLine } from './RatingLine';
import { StarRating } from './StarRating';

/** What other customers wrote about a listing or a restaurant: only published reviews, with the business's reply under each. */
export function PublicReviews({ subject }: { subject: ReviewSubject }) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const reviews = usePublicReviews(subject);

  // The section is an extra: while it loads, or if it cannot, the page around it still works, so it simply is not there.
  if (!reviews.data) {
    return null;
  }

  const { rating, items, totalCount } = reviews.data;
  return (
    <View style={styles.section} testID="public-reviews">
      <OPText variant="label" accessibilityRole="header">
        {t('reviews.title')}
      </OPText>
      {rating ? (
        <RatingLine rating={rating} />
      ) : (
        <OPText tone="secondary">
          {totalCount === 0 ? t('reviews.empty') : t('reviews.countOnly', { count: totalCount })}
        </OPText>
      )}
      {items.map((review) => (
        <ReviewCard key={review.id} review={review} />
      ))}
    </View>
  );
}

function ReviewCard({ review }: { review: PublicReview }) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  return (
    <View style={styles.card} testID="review-card">
      <StarRating value={review.rating} />
      <OPText tone="secondary" variant="caption">
        {t('reviews.by', { name: review.authorName, date: formatDateTime(review.publishedAt) })}
      </OPText>
      {review.text ? <OPText>{review.text}</OPText> : null}
      {review.reply ? (
        <View style={styles.reply}>
          <OPText variant="caption" tone="secondary">
            {t('reviews.replyFromBusiness')}
          </OPText>
          <OPText>{review.reply.text}</OPText>
        </View>
      ) : null}
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    section: {
      marginTop: theme.spacing.md,
      gap: theme.spacing.xs,
    },
    card: {
      gap: theme.spacing.xxs,
      paddingVertical: theme.spacing.xs,
      borderTopWidth: StyleSheet.hairlineWidth,
      borderTopColor: theme.colors.border,
    },
    reply: {
      marginTop: theme.spacing.xxs,
      marginLeft: theme.spacing.sm,
      paddingLeft: theme.spacing.xs,
      gap: theme.spacing.xxs,
      borderLeftWidth: 2,
      borderLeftColor: theme.colors.border,
    },
  });
