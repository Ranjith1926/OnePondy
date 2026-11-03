import { StackActions, useNavigation, type StaticScreenProps } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { KeyboardAvoidingView, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  FormField,
  InlineBanner,
  OPButton,
  OPChip,
  OPIconButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { useAppState } from '../../../state';
import { SignInPrompt } from '../../auth/components/SignInPrompt';
import { supportTopics, type SupportTopic } from '../api/supportApi';
import { useCreateTicket } from '../hooks/useSupport';
import { supportErrorMessageKey } from '../supportErrors';

/** A request can be started from a booking or an order (so it is about that one), or from Help (about nothing in particular). */
type Props = StaticScreenProps<{ bookingId?: string; foodOrderId?: string } | undefined>;

const MAX_SUBJECT = 120;
const MAX_MESSAGE = 2000;

export function NewRequestScreen({ route }: Props) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { auth } = useAppState();
  const create = useCreateTicket();
  const bookingId = route.params?.bookingId;
  const foodOrderId = route.params?.foodOrderId;
  const [topic, setTopic] = useState<SupportTopic>(
    bookingId ? 'booking' : foodOrderId ? 'foodOrder' : 'other',
  );
  const [subject, setSubject] = useState('');
  const [message, setMessage] = useState('');
  const [tried, setTried] = useState(false);

  const header = (
    <View style={styles.header}>
      <OPIconButton
        icon="arrow-left"
        accessibilityLabel={t('common.back')}
        onPress={() => navigation.goBack()}
      />
    </View>
  );

  if (auth.status !== 'authenticated') {
    return (
      <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
        {header}
        <SignInPrompt
          icon="life-buoy"
          title={t('support.signInTitle')}
          message={t('support.signInMessage')}
        />
      </SafeAreaView>
    );
  }

  const missingSubject = tried && subject.trim() === '';
  const missingMessage = tried && message.trim() === '';

  const submit = () => {
    setTried(true);
    if (subject.trim() === '' || message.trim() === '') {
      return;
    }

    create.mutate(
      {
        topic,
        subject: subject.trim(),
        message: message.trim(),
        ...(bookingId ? { bookingId } : {}),
        ...(foodOrderId ? { foodOrderId } : {}),
      },
      // The form is done with: back from the new request returns to where help was asked for, not to an empty form.
      {
        onSuccess: (ticket) =>
          navigation.dispatch(StackActions.replace('SupportTicket', { id: ticket.id })),
      },
    );
  };

  return (
    <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
      {header}
      <KeyboardAvoidingView behavior="padding" style={styles.screen}>
        <ScrollView
          contentContainerStyle={[styles.inset, styles.content]}
          keyboardShouldPersistTaps="handled"
        >
          <OPText variant="title" accessibilityRole="header">
            {t('support.form.title')}
          </OPText>

          {bookingId || foodOrderId ? (
            <OPText tone="secondary">
              {bookingId ? t('support.form.aboutBooking') : t('support.form.aboutOrder')}
            </OPText>
          ) : null}

          {create.error ? (
            <InlineBanner kind="error" message={t(supportErrorMessageKey(create.error))} />
          ) : null}

          <View style={styles.field}>
            <OPText variant="label">{t('support.form.topic')}</OPText>
            <View style={styles.chips}>
              {supportTopics.map((option) => (
                <OPChip
                  key={option}
                  label={t(`support.topic.${option}`)}
                  selected={topic === option}
                  onPress={() => setTopic(option)}
                />
              ))}
            </View>
          </View>

          <FormField
            label={t('support.form.subject')}
            hint={t('support.form.subjectHint')}
            value={subject}
            onChangeText={setSubject}
            maxLength={MAX_SUBJECT}
            error={missingSubject ? t('support.form.required') : undefined}
          />
          <FormField
            label={t('support.form.message')}
            hint={t('support.form.messageHint')}
            value={message}
            onChangeText={setMessage}
            maxLength={MAX_MESSAGE}
            multiline
            error={missingMessage ? t('support.form.required') : undefined}
          />

          <OPButton
            label={create.isPending ? t('support.form.submitting') : t('support.form.submit')}
            loading={create.isPending}
            onPress={submit}
            fullWidth
            testID="support-submit"
          />
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    screen: {
      flex: 1,
      backgroundColor: theme.colors.background,
    },
    header: {
      flexDirection: 'row',
      alignItems: 'center',
      paddingHorizontal: theme.spacing.xxs,
      paddingVertical: theme.spacing.xs,
    },
    inset: {
      paddingHorizontal: theme.layout.screenPadding,
    },
    content: {
      gap: theme.spacing.sm,
      paddingBottom: theme.spacing.xl,
    },
    field: {
      gap: theme.spacing.xxs,
    },
    chips: {
      flexDirection: 'row',
      flexWrap: 'wrap',
      gap: theme.spacing.xs,
    },
  });
