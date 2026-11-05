import { useNavigation, type StaticScreenProps } from '@react-navigation/native';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { KeyboardAvoidingView, ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  ErrorState,
  FormField,
  InlineBanner,
  LoadingState,
  OPButton,
  OPIconButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { formatDateTime } from '../../bookings/formatDateTime';
import { useRefetchOnFocus } from '../../bookings/hooks/useRefetchOnFocus';
import { formatMoney } from '../../payments/formatMoney';
import type { LinkedSubject, TicketMessage } from '../api/supportApi';
import { useReplyToTicket, useTicket } from '../hooks/useSupport';
import { supportErrorMessageKey } from '../supportErrors';

type Props = StaticScreenProps<{ id: string }>;

const MAX_MESSAGE = 2000;

export function TicketScreen({ route }: Props) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const ticket = useTicket(route.params.id);
  useRefetchOnFocus(ticket.refetch);
  const reply = useReplyToTicket(route.params.id);
  const [text, setText] = useState('');
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

  if (ticket.isPending) {
    return (
      <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
        {header}
        <View style={styles.centered}>
          <LoadingState accessibilityLabel={t('support.ticket.loading')} />
        </View>
      </SafeAreaView>
    );
  }

  if (ticket.isError) {
    return (
      <SafeAreaView edges={['top', 'left', 'right']} style={styles.screen}>
        {header}
        <ErrorState
          title={t('support.ticket.error.title')}
          message={t('support.ticket.error.message')}
          retryLabel={t('common.retry')}
          onRetry={() => void ticket.refetch()}
          retrying={ticket.isFetching}
        />
      </SafeAreaView>
    );
  }

  const data = ticket.data;

  const send = () => {
    setTried(true);
    if (text.trim() === '') {
      return;
    }

    reply.mutate(text.trim(), {
      onSuccess: () => {
        setText('');
        setTried(false);
      },
    });
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
            {data.subject}
          </OPText>
          <OPText variant="caption" tone="secondary">
            {t('support.ticket.reference', { reference: data.reference })}
          </OPText>
          <OPText variant="label" accessibilityLiveRegion="polite">
            {t(`support.status.${data.status}`)}
          </OPText>

          {data.about ? <AboutCard about={data.about} /> : null}

          <View style={styles.thread} testID="thread">
            {data.messages.map((message) => (
              <MessageBubble key={message.id} message={message} />
            ))}
          </View>

          {data.canReply ? (
            <View style={styles.replyBox}>
              {data.status === 'resolved' ? (
                <OPText tone="secondary">{t('support.ticket.resolvedNote')}</OPText>
              ) : null}
              {reply.error ? (
                <InlineBanner kind="error" message={t(supportErrorMessageKey(reply.error))} />
              ) : null}
              <FormField
                label={t('support.ticket.replyLabel')}
                value={text}
                onChangeText={setText}
                maxLength={MAX_MESSAGE}
                multiline
                error={tried && text.trim() === '' ? t('support.form.required') : undefined}
              />
              <OPButton
                label={reply.isPending ? t('support.ticket.sending') : t('support.ticket.send')}
                loading={reply.isPending}
                onPress={send}
                fullWidth
                testID="support-send"
              />
            </View>
          ) : (
            <OPText tone="secondary">{t('support.ticket.cannotReply')}</OPText>
          )}
        </ScrollView>
      </KeyboardAvoidingView>
    </SafeAreaView>
  );
}

function AboutCard({ about }: { about: LinkedSubject }) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  return (
    <View style={styles.about} testID="about">
      <OPText variant="caption" tone="secondary">
        {about.kind === 'booking' ? t('support.about.booking') : t('support.about.food_order')}
      </OPText>
      <OPText variant="label">{about.title}</OPText>
      <OPText tone="secondary">
        {formatMoney(about.totalAmount, about.currency)} · {formatDateTime(about.when)}
      </OPText>
    </View>
  );
}

function MessageBubble({ message }: { message: TicketMessage }) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  return (
    <View
      style={[styles.bubble, message.fromSupport ? styles.fromSupport : styles.fromYou]}
      testID={message.fromSupport ? 'message-support' : 'message-you'}
    >
      <OPText variant="caption" tone="secondary">
        {message.fromSupport ? t('support.ticket.fromSupport') : t('support.ticket.fromYou')} ·{' '}
        {formatDateTime(message.createdAt)}
      </OPText>
      <OPText>{message.body}</OPText>
    </View>
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
    centered: {
      flex: 1,
      alignItems: 'center',
      justifyContent: 'center',
      paddingHorizontal: theme.layout.screenPadding,
    },
    inset: {
      paddingHorizontal: theme.layout.screenPadding,
    },
    content: {
      gap: theme.spacing.sm,
      paddingBottom: theme.spacing.xl,
    },
    about: {
      gap: theme.spacing.xxs,
      padding: theme.spacing.sm,
      borderRadius: theme.radii.md,
      backgroundColor: theme.colors.surfaceMuted,
    },
    thread: {
      gap: theme.spacing.xs,
    },
    bubble: {
      gap: theme.spacing.xxs,
      padding: theme.spacing.sm,
      borderRadius: theme.radii.md,
      maxWidth: '92%',
    },
    fromYou: {
      alignSelf: 'flex-end',
      backgroundColor: theme.colors.brand.primaryContainer,
    },
    fromSupport: {
      alignSelf: 'flex-start',
      backgroundColor: theme.colors.surfaceMuted,
    },
    replyBox: {
      gap: theme.spacing.xs,
      marginTop: theme.spacing.sm,
    },
  });
