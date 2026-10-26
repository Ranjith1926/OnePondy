import { zodResolver } from '@hookform/resolvers/zod';
import { StackActions, useNavigation, type StaticScreenProps } from '@react-navigation/native';
import { useMutation } from '@tanstack/react-query';
import Constants from 'expo-constants';
import { useEffect, useState } from 'react';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  FormField,
  InlineBanner,
  ModalScreenTemplate,
  OPButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { authApi, type OtpChannel } from '../api/authApi';
import { fieldError } from '../components/FieldError';
import { useAuthActions } from '../hooks/useAuthActions';
import { codeSchema } from '../schemas/authSchemas';
import { authErrorMessageKey } from '../services/errorMessages';
import { sessionManager } from '../services/sessionManager';

type Props = StaticScreenProps<{
  challengeId: string;
  channel: OtpChannel;
  destination: string;
  maskedDestination: string;
  resendAfterSeconds: number;
}>;

export function VerifyCodeScreen({ route }: Props) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { finishSignIn } = useAuthActions();
  const { channel, destination, maskedDestination } = route.params;

  const [challengeId, setChallengeId] = useState(route.params.challengeId);
  const [resendAt, setResendAt] = useState(
    () => Date.now() + route.params.resendAfterSeconds * 1000,
  );
  const [now, setNow] = useState(() => Date.now());
  const secondsLeft = Math.max(0, Math.ceil((resendAt - now) / 1000));

  useEffect(() => {
    if (secondsLeft === 0) {
      return;
    }
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [secondsLeft]);

  const form = useForm({ resolver: zodResolver(codeSchema), defaultValues: { code: '' } });
  const verify = useMutation({
    mutationFn: (code: string) =>
      authApi.verifyCode(challengeId, code, Constants.deviceName ?? undefined),
    onSuccess: async (tokens) => {
      await sessionManager.start(tokens);
      if (tokens.isNewUser) {
        navigation.dispatch(StackActions.replace('CompleteProfile', { user: tokens.user }));
      } else {
        finishSignIn(tokens.user);
      }
    },
  });
  const resend = useMutation({
    mutationFn: () => authApi.requestCode(channel, destination),
    onSuccess: (result) => {
      setChallengeId(result.challengeId);
      setResendAt(Date.now() + result.resendAfterSeconds * 1000);
      setNow(Date.now());
      verify.reset();
      form.reset({ code: '' });
    },
  });

  const submit = form.handleSubmit(({ code }) => verify.mutate(code));
  const error = verify.error ?? resend.error;

  return (
    <ModalScreenTemplate
      title={t('auth.verifyTitle')}
      closeLabel={t('common.close')}
      onClose={() => navigation.goBack()}
    >
      <View style={styles.body}>
        <OPText tone="secondary">
          {t('auth.verifyIntro', { destination: maskedDestination })}
        </OPText>
        <Controller
          control={form.control}
          name="code"
          render={({ field }) => (
            <FormField
              label={t('auth.codeLabel')}
              value={field.value}
              onChangeText={(text) => {
                const digits = text.replace(/\D/g, '').slice(0, 6);
                field.onChange(digits);
                if (digits.length === 6 && !verify.isPending) {
                  verify.mutate(digits);
                }
              }}
              onBlur={field.onBlur}
              error={fieldError(t, form.formState.errors.code?.message)}
              keyboardType="number-pad"
              autoComplete="sms-otp"
              textContentType="oneTimeCode"
              maxLength={6}
              returnKeyType="done"
              onSubmitEditing={() => void submit()}
              autoFocus
            />
          )}
        />
        {error ? <InlineBanner kind="error" message={t(authErrorMessageKey(error))} /> : null}
        <OPButton
          label={t('auth.verify')}
          onPress={submit}
          loading={verify.isPending}
          fullWidth
          size="lg"
        />
        <View style={styles.links}>
          <OPButton
            label={
              secondsLeft > 0 ? t('auth.resendIn', { seconds: secondsLeft }) : t('auth.resend')
            }
            onPress={() => resend.mutate()}
            disabled={secondsLeft > 0}
            loading={resend.isPending}
            variant="tertiary"
          />
          <OPButton
            label={t('auth.changeDestination')}
            onPress={() => navigation.goBack()}
            variant="tertiary"
          />
        </View>
      </View>
    </ModalScreenTemplate>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    body: {
      gap: theme.spacing.md,
      paddingHorizontal: theme.layout.screenPadding,
    },
    links: {
      alignItems: 'center',
      gap: theme.spacing.xxs,
    },
  });
