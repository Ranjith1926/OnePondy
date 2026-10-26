import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigation } from '@react-navigation/native';
import { useMutation } from '@tanstack/react-query';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  FormField,
  InlineBanner,
  ModalScreenTemplate,
  OPButton,
  OPIcon,
  OPText,
  useTheme,
  useThemedStyles,
  type IconName,
  type Theme,
} from '../../../design-system';
import { authApi } from '../api/authApi';
import { fieldError } from '../components/FieldError';
import { useAuthActions } from '../hooks/useAuthActions';
import { codeSchema } from '../schemas/authSchemas';
import { authErrorMessageKey } from '../services/errorMessages';

const points: readonly { key: 'data' | 'sessions' | 'records'; icon: IconName }[] = [
  { key: 'data', icon: 'user-round-x' },
  { key: 'sessions', icon: 'log-out' },
  { key: 'records', icon: 'file-lock' },
];

/** Spec §7 account deletion workflow: explain, re-confirm with a code, then delete. */
export function DeleteAccountScreen() {
  const { t } = useTranslation();
  const theme = useTheme();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { endSessionAfterDeletion } = useAuthActions();

  const requestCode = useMutation({ mutationFn: authApi.requestDeletionCode });
  const challenge = requestCode.data;
  const form = useForm({ resolver: zodResolver(codeSchema), defaultValues: { code: '' } });
  const confirm = useMutation({
    mutationFn: ({ challengeId, code }: { challengeId: string; code: string }) =>
      authApi.confirmDeletion(challengeId, code),
    onSuccess: endSessionAfterDeletion,
  });

  const submit = form.handleSubmit(({ code }) => {
    if (challenge) {
      confirm.mutate({ challengeId: challenge.challengeId, code });
    }
  });
  const error = confirm.error ?? requestCode.error;

  return (
    <ModalScreenTemplate
      title={t('deleteAccount.title')}
      closeLabel={t('common.close')}
      onClose={() => navigation.goBack()}
    >
      <View style={styles.body}>
        <OPText variant="bodyStrong">{t('deleteAccount.intro')}</OPText>
        {points.map((point) => (
          <View key={point.key} style={styles.point}>
            <OPIcon name={point.icon} size={20} color={theme.colors.text.secondary} />
            <OPText tone="secondary" style={styles.pointText}>
              {t(`deleteAccount.points.${point.key}`)}
            </OPText>
          </View>
        ))}

        {challenge ? (
          <>
            <OPText>
              {t('deleteAccount.codeIntro', { destination: challenge.maskedDestination })}
            </OPText>
            <Controller
              control={form.control}
              name="code"
              render={({ field }) => (
                <FormField
                  label={t('auth.codeLabel')}
                  value={field.value}
                  onChangeText={(text) => field.onChange(text.replace(/\D/g, '').slice(0, 6))}
                  onBlur={field.onBlur}
                  error={fieldError(t, form.formState.errors.code?.message)}
                  keyboardType="number-pad"
                  autoComplete="sms-otp"
                  textContentType="oneTimeCode"
                  maxLength={6}
                  autoFocus
                />
              )}
            />
          </>
        ) : null}

        {error ? <InlineBanner kind="error" message={t(authErrorMessageKey(error))} /> : null}

        {challenge ? (
          <OPButton
            label={t('deleteAccount.confirm')}
            onPress={submit}
            loading={confirm.isPending}
            variant="danger"
            icon="trash-2"
            fullWidth
            size="lg"
          />
        ) : (
          <OPButton
            label={t('deleteAccount.sendCode')}
            onPress={() => requestCode.mutate()}
            loading={requestCode.isPending}
            variant="secondary"
            fullWidth
            size="lg"
          />
        )}
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
    point: {
      flexDirection: 'row',
      alignItems: 'flex-start',
      gap: theme.spacing.sm,
    },
    pointText: {
      flex: 1,
    },
  });
