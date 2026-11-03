import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  FormField,
  InlineBanner,
  ModalSheet,
  OPButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { reviewErrorMessageKey } from '../reviewErrors';
import { StarRating } from './StarRating';

const maxTextLength = 1000;

interface ReviewFormSheetProps {
  visible: boolean;
  onClose: () => void;
  /** Present when changing an existing review. */
  initial?: { rating: number; text: string | null };
  submitting: boolean;
  error: unknown;
  onSubmit: (rating: number, text: string | undefined) => void;
}

/** Writing a review, or changing the one already written. A rating is required; the words are not. */
export function ReviewFormSheet({
  visible,
  onClose,
  initial,
  submitting,
  error,
  onSubmit,
}: ReviewFormSheetProps) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const [rating, setRating] = useState(initial?.rating ?? 0);
  const [text, setText] = useState(initial?.text ?? '');
  const [triedWithoutRating, setTriedWithoutRating] = useState(false);

  const submit = () => {
    if (rating === 0) {
      setTriedWithoutRating(true);
      return;
    }

    onSubmit(rating, text.trim() === '' ? undefined : text.trim());
  };

  return (
    <ModalSheet
      visible={visible}
      onClose={onClose}
      title={t('reviews.form.title')}
      closeLabel={t('common.close')}
    >
      <View style={styles.body}>
        {error ? <InlineBanner kind="error" message={t(reviewErrorMessageKey(error))} /> : null}
        <View style={styles.field}>
          <OPText variant="label">{t('reviews.form.rating')}</OPText>
          <StarRating
            value={rating}
            onChange={(value) => {
              setRating(value);
              setTriedWithoutRating(false);
            }}
          />
          {triedWithoutRating ? (
            <OPText variant="caption" tone="error" accessibilityLiveRegion="polite">
              {t('reviews.form.ratingRequired')}
            </OPText>
          ) : null}
        </View>
        <FormField
          label={t('reviews.form.text')}
          hint={t('reviews.form.textHint')}
          value={text}
          onChangeText={setText}
          maxLength={maxTextLength}
          multiline
          testID="review-text"
        />
        <OPButton
          label={
            submitting
              ? t('reviews.form.submitting')
              : initial
                ? t('reviews.form.save')
                : t('reviews.form.submit')
          }
          loading={submitting}
          onPress={submit}
          fullWidth
          testID="review-submit"
        />
      </View>
    </ModalSheet>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    body: {
      gap: theme.spacing.sm,
    },
    field: {
      gap: theme.spacing.xxs,
    },
  });
