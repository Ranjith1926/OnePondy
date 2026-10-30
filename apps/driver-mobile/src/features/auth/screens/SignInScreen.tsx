import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigation } from '@react-navigation/native';
import { useMutation } from '@tanstack/react-query';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';

import { Screen } from '../../../components/Screen';
import { FormField, InlineBanner, OPButton, OPText } from '../../../design-system';
import { useSession } from '../../../session/SessionProvider';
import { authApi } from '../api/authApi';
import { fieldError } from '../components/FieldError';
import { phoneSchema } from '../schemas/authSchemas';
import { authErrorMessageKey } from '../services/errorMessages';

/** Passwordless sign-in and sign-up in one flow (spec D1): the mobile number, then the texted code. */
export function SignInScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const { expired } = useSession();

  const form = useForm({ resolver: zodResolver(phoneSchema), defaultValues: { phone: '' } });
  const request = useMutation({
    mutationFn: (phone: string) => authApi.requestCode(phone),
    onSuccess: (result, phone) =>
      navigation.navigate('VerifyCode', {
        challengeId: result.challengeId,
        destination: phone,
        maskedDestination: result.maskedDestination,
        resendAfterSeconds: result.resendAfterSeconds,
      }),
  });

  const submit = form.handleSubmit(({ phone }) => request.mutate(phone));

  return (
    <Screen title={t('auth.signInTitle')}>
      <OPText tone="secondary">{t('auth.signInIntro')}</OPText>
      {expired ? <InlineBanner kind="warning" message={t('auth.sessionExpired')} /> : null}

      <Controller
        control={form.control}
        name="phone"
        render={({ field }) => (
          <FormField
            label={t('auth.phoneLabel')}
            hint={t('auth.phoneHint')}
            prefix="+91"
            value={field.value}
            onChangeText={field.onChange}
            onBlur={field.onBlur}
            error={fieldError(t, form.formState.errors.phone?.message)}
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
        {t('auth.smsNote')}
      </OPText>
    </Screen>
  );
}
