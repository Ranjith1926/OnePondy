import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigation } from '@react-navigation/native';
import { useMutation } from '@tanstack/react-query';
import { useState } from 'react';
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
import { emailSchema, phoneSchema } from '../schemas/authSchemas';
import { authErrorMessageKey } from '../services/errorMessages';

/** Passwordless sign-in and sign-up in one flow (spec D1): mobile number first, email as an option. */
export function SignInScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const [channel, setChannel] = useState<OtpChannel>('sms');

  const phoneForm = useForm({ resolver: zodResolver(phoneSchema), defaultValues: { phone: '' } });
  const emailForm = useForm({ resolver: zodResolver(emailSchema), defaultValues: { email: '' } });
  const request = useMutation({
    mutationFn: (destination: string) => authApi.requestCode(channel, destination),
    onSuccess: (result, destination) =>
      navigation.navigate('VerifyCode', {
        challengeId: result.challengeId,
        channel,
        destination,
        maskedDestination: result.maskedDestination,
        resendAfterSeconds: result.resendAfterSeconds,
      }),
  });

  const submit =
    channel === 'sms'
      ? phoneForm.handleSubmit(({ phone }) => request.mutate(phone))
      : emailForm.handleSubmit(({ email }) => request.mutate(email));

  const switchChannel = () => {
    request.reset();
    setChannel(channel === 'sms' ? 'email' : 'sms');
  };

  return (
    <ModalScreenTemplate
      title={t('auth.signInTitle')}
      closeLabel={t('common.close')}
      onClose={() => navigation.goBack()}
    >
      <View style={styles.body}>
        <OPText tone="secondary">{t('auth.signInIntro')}</OPText>

        {channel === 'sms' ? (
          <Controller
            control={phoneForm.control}
            name="phone"
            render={({ field }) => (
              <FormField
                label={t('auth.phoneLabel')}
                hint={t('auth.phoneHint')}
                prefix="+91"
                value={field.value}
                onChangeText={field.onChange}
                onBlur={field.onBlur}
                error={fieldError(t, phoneForm.formState.errors.phone?.message)}
                keyboardType="phone-pad"
                autoComplete="tel"
                textContentType="telephoneNumber"
                maxLength={16}
                returnKeyType="send"
                onSubmitEditing={() => void submit()}
                autoFocus
              />
            )}
          />
        ) : (
          <Controller
            control={emailForm.control}
            name="email"
            render={({ field }) => (
              <FormField
                label={t('auth.emailLabel')}
                value={field.value}
                onChangeText={field.onChange}
                onBlur={field.onBlur}
                error={fieldError(t, emailForm.formState.errors.email?.message)}
                keyboardType="email-address"
                autoCapitalize="none"
                autoComplete="email"
                textContentType="emailAddress"
                returnKeyType="send"
                onSubmitEditing={() => void submit()}
                autoFocus
              />
            )}
          />
        )}

        {request.error ? (
          <InlineBanner kind="error" message={t(authErrorMessageKey(request.error))} />
        ) : null}

        <OPButton
          label={t('auth.sendCode')}
          onPress={submit}
          loading={request.isPending}
          fullWidth
          size="lg"
        />
        <OPText variant="caption" tone="secondary" align="center">
          {channel === 'sms' ? t('auth.smsNote') : t('auth.emailNote')}
        </OPText>
        <View style={styles.switch}>
          <OPButton
            label={channel === 'sms' ? t('auth.useEmail') : t('auth.usePhone')}
            onPress={switchChannel}
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
    switch: {
      alignItems: 'center',
    },
  });
