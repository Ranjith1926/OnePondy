import { zodResolver } from '@hookform/resolvers/zod';
import { Controller, useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { FormField, OPButton, useThemedStyles, type Theme } from '../../../design-system';
import { nameSchema } from '../schemas/authSchemas';
import { fieldError } from './FieldError';

interface NameFormProps {
  defaultName?: string;
  submitLabel: string;
  submitting: boolean;
  onSubmit: (displayName: string) => void;
}

export function NameForm({ defaultName = '', submitLabel, submitting, onSubmit }: NameFormProps) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const { control, handleSubmit, formState } = useForm({
    resolver: zodResolver(nameSchema),
    defaultValues: { displayName: defaultName },
  });
  const submit = handleSubmit(({ displayName }) => onSubmit(displayName));

  return (
    <View style={styles.form}>
      <Controller
        control={control}
        name="displayName"
        render={({ field }) => (
          <FormField
            label={t('auth.nameLabel')}
            value={field.value}
            onChangeText={field.onChange}
            onBlur={field.onBlur}
            error={fieldError(t, formState.errors.displayName?.message)}
            autoCapitalize="words"
            autoComplete="name"
            textContentType="name"
            returnKeyType="done"
            onSubmitEditing={() => void submit()}
            maxLength={120}
          />
        )}
      />
      <OPButton label={submitLabel} onPress={submit} loading={submitting} fullWidth size="lg" />
    </View>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    form: {
      gap: theme.spacing.md,
    },
  });
