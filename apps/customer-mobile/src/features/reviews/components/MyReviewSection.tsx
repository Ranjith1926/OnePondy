import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  InlineBanner,
  ModalSheet,
  OPButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { formatDateTime } from '../../bookings/formatDateTime';
import type { MyReview } from '../api/reviewsApi';
import { useCreateReview, useDeleteReview, useEditReview, useMyReviews } from '../hooks/useReviews';
import { reviewErrorMessageKey } from '../reviewErrors';
import { ReviewFormSheet } from './ReviewFormSheet';
import { StarRating } from './StarRating';

export type ReviewTransaction = { kind: 'booking'; id: string } | { kind: 'foodOrder'; id: string };

interface MyReviewSectionProps {
  transaction: ReviewTransaction;
  /** A booking that is completed, or an order that is delivered. Nothing is shown (or asked of the server) otherwise. */
  eligible: boolean;
}

/**
 * The review of one finished booking or delivered order: an invitation to write it, or the review as it stands, with its
 * moderation status, the business's reply and the author's one change. The server decides everything (eligibility, the time
 * limit, the one change); this only shows what it says.
 */
export function MyReviewSection({ transaction, eligible }: MyReviewSectionProps) {
  const reviews = useMyReviews(eligible);
  if (!eligible || !reviews.data) {
    return null;
  }

  const existing = reviews.data.find((review) =>
    transaction.kind === 'booking'
      ? review.bookingId === transaction.id
      : review.foodOrderId === transaction.id,
  );
  return existing ? <ExistingReview review={existing} /> : <NewReview transaction={transaction} />;
}

function NewReview({ transaction }: { transaction: ReviewTransaction }) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const create = useCreateReview();
  const [open, setOpen] = useState(false);

  return (
    <View style={styles.section} testID="write-review">
      <OPText variant="label" accessibilityRole="header">
        {t('reviews.yourReview')}
      </OPText>
      <OPText tone="secondary">{t('reviews.writePrompt')}</OPText>
      <OPButton label={t('reviews.write')} variant="secondary" onPress={() => setOpen(true)} />
      <ReviewFormSheet
        key={String(open)}
        visible={open}
        onClose={() => setOpen(false)}
        submitting={create.isPending}
        error={create.error}
        onSubmit={(rating, text) =>
          create.mutate(
            {
              ...(transaction.kind === 'booking'
                ? { bookingId: transaction.id }
                : { foodOrderId: transaction.id }),
              rating,
              text,
            },
            { onSuccess: () => setOpen(false) },
          )
        }
      />
    </View>
  );
}

function ExistingReview({ review }: { review: MyReview }) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const edit = useEditReview(review.id);
  const remove = useDeleteReview(review.id);
  const [editing, setEditing] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const canDelete = review.status === 'pending' || review.status === 'published';

  return (
    <View style={styles.section} testID="my-review">
      <OPText variant="label" accessibilityRole="header">
        {t('reviews.yourReview')}
      </OPText>
      <StarRating value={review.rating} />
      {review.text ? <OPText>{review.text}</OPText> : null}
      <OPText tone="secondary">{t(`reviews.status.${review.status}`)}</OPText>

      {review.moderationReason ? (
        <InlineBanner
          kind="warning"
          message={t('reviews.reason', { reason: review.moderationReason })}
        />
      ) : null}

      {review.reply ? (
        <View style={styles.reply}>
          <OPText variant="caption" tone="secondary">
            {t('reviews.replyFromBusiness')} · {formatDateTime(review.reply.repliedAt)}
          </OPText>
          <OPText>{review.reply.text}</OPText>
        </View>
      ) : null}

      {review.canEdit ? <OPText tone="secondary">{t('reviews.canChangeOnce')}</OPText> : null}
      {remove.error ? (
        <InlineBanner kind="error" message={t(reviewErrorMessageKey(remove.error))} />
      ) : null}

      <View style={styles.actions}>
        {review.canEdit ? (
          <OPButton
            label={t('reviews.edit')}
            variant="secondary"
            onPress={() => setEditing(true)}
          />
        ) : null}
        {canDelete ? (
          <OPButton
            label={t('reviews.delete')}
            variant="tertiary"
            onPress={() => setConfirmingDelete(true)}
          />
        ) : null}
      </View>

      <ReviewFormSheet
        key={String(editing)}
        visible={editing}
        onClose={() => setEditing(false)}
        initial={{ rating: review.rating, text: review.text }}
        submitting={edit.isPending}
        error={edit.error}
        onSubmit={(rating, text) =>
          edit.mutate(
            { version: review.version, rating, text },
            { onSuccess: () => setEditing(false) },
          )
        }
      />

      <ModalSheet
        visible={confirmingDelete}
        onClose={() => setConfirmingDelete(false)}
        title={t('reviews.deleteConfirm.title')}
        closeLabel={t('common.close')}
      >
        <View style={styles.confirmBody}>
          <OPText tone="secondary">{t('reviews.deleteConfirm.message')}</OPText>
          <OPButton
            label={t('reviews.deleteConfirm.confirm')}
            variant="danger"
            loading={remove.isPending}
            onPress={() =>
              remove.mutate(undefined, { onSuccess: () => setConfirmingDelete(false) })
            }
            fullWidth
          />
          <OPButton
            label={t('reviews.deleteConfirm.keep')}
            variant="tertiary"
            onPress={() => setConfirmingDelete(false)}
            fullWidth
          />
        </View>
      </ModalSheet>
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    section: {
      marginTop: theme.spacing.md,
      gap: theme.spacing.xs,
    },
    reply: {
      marginLeft: theme.spacing.sm,
      paddingLeft: theme.spacing.xs,
      gap: theme.spacing.xxs,
      borderLeftWidth: 2,
      borderLeftColor: theme.colors.border,
    },
    actions: {
      flexDirection: 'row',
      flexWrap: 'wrap',
      gap: theme.spacing.xs,
    },
    confirmBody: {
      gap: theme.spacing.sm,
    },
  });
