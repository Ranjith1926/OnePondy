import { zodResolver } from '@hookform/resolvers/zod';
import { useNavigation } from '@react-navigation/native';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import { Screen } from '../../../components/Screen';
import {
  FormField,
  InlineBanner,
  OPButton,
  OPChip,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { vehicleTypes } from '../api/driverApi';
import { driverFieldError } from '../components/fieldError';
import { useDriverProfile, useSaveDriver } from '../hooks/useDriver';
import { driverSchema, tidyIdentifier, type DriverForm } from '../schemas/driverSchema';
import { driverErrorMessageKey } from '../services/errorMessages';

/**
 * The application form, and the same form for changing details later. Changing the details of an approved driver sends them for
 * review again, so the form says so before they save.
 */
export function ApplyScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const profile = useDriverProfile().data;
  const editing = profile?.configured === true;
  const save = useSaveDriver();

  const form = useForm<DriverForm>({
    resolver: zodResolver(driverSchema),
    defaultValues: {
      fullName: profile?.fullName ?? '',
      vehicleType: profile?.vehicleType ?? undefined,
      vehicleNumber: profile?.vehicleNumber ?? '',
      licenceNumber: profile?.licenceNumber ?? '',
    },
  });
  const vehicleType = useWatch({ control: form.control, name: 'vehicleType' });
  const motorised = vehicleType !== undefined && vehicleType !== 'bicycle';

  const submit = form.handleSubmit((values) =>
    save.mutate(
      {
        fullName: values.fullName.trim(),
        vehicleType: values.vehicleType,
        vehicleNumber: motorised ? tidyIdentifier(values.vehicleNumber) : null,
        licenceNumber: motorised ? tidyIdentifier(values.licenceNumber) : null,
        version: editing ? profile.version : undefined,
      },
      // A first application replaces this screen by itself once the profile exists; an edit goes back.
      { onSuccess: () => editing && navigation.goBack() },
    ),
  );

  return (
    <Screen
      title={editing ? t('driver.editTitle') : t('driver.applyTitle')}
      backLabel={t('common.back')}
      onBack={editing ? () => navigation.goBack() : undefined}
    >
      {editing ? null : <OPText tone="secondary">{t('driver.applyIntro')}</OPText>}

      <Controller
        control={form.control}
        name="fullName"
        render={({ field }) => (
          <FormField
            label={t('driver.nameLabel')}
            hint={t('driver.nameHint')}
            value={field.value}
            onChangeText={field.onChange}
            onBlur={field.onBlur}
            error={driverFieldError(t, form.formState.errors.fullName?.message)}
            autoComplete="name"
            textContentType="name"
            maxLength={100}
            returnKeyType="next"
          />
        )}
      />

      <View style={styles.group}>
        <OPText variant="label">{t('driver.vehicleLabel')}</OPText>
        <View style={styles.chips}>
          {vehicleTypes.map((type) => (
            <OPChip
              key={type}
              label={t(`driver.vehicle.${type}`)}
              selected={vehicleType === type}
              onPress={() => form.setValue('vehicleType', type, { shouldValidate: true })}
            />
          ))}
        </View>
        {form.formState.errors.vehicleType ? (
          <OPText variant="caption" tone="error" accessibilityLiveRegion="polite">
            {driverFieldError(t, form.formState.errors.vehicleType.message)}
          </OPText>
        ) : null}
      </View>

      {motorised ? (
        <>
          <Controller
            control={form.control}
            name="vehicleNumber"
            render={({ field }) => (
              <FormField
                label={t('driver.vehicleNumberLabel')}
                hint={t('driver.vehicleNumberHint')}
                value={field.value}
                onChangeText={field.onChange}
                onBlur={field.onBlur}
                error={driverFieldError(t, form.formState.errors.vehicleNumber?.message)}
                autoCapitalize="characters"
                autoCorrect={false}
                maxLength={20}
              />
            )}
          />
          <Controller
            control={form.control}
            name="licenceNumber"
            render={({ field }) => (
              <FormField
                label={t('driver.licenceLabel')}
                hint={t('driver.licenceHint')}
                value={field.value}
                onChangeText={field.onChange}
                onBlur={field.onBlur}
                error={driverFieldError(t, form.formState.errors.licenceNumber?.message)}
                autoCapitalize="characters"
                autoCorrect={false}
                maxLength={30}
              />
            )}
          />
          <OPText variant="caption" tone="secondary">
            {t('driver.documentsNote')}
          </OPText>
        </>
      ) : null}

      {editing && profile.approvalStatus === 'approved' ? (
        <InlineBanner kind="warning" message={t('driver.reviewWarning')} />
      ) : null}
      {save.error ? (
        <InlineBanner kind="error" message={t(driverErrorMessageKey(save.error))} />
      ) : null}

      <OPButton
        label={editing ? t('driver.submitEdit') : t('driver.submitApply')}
        onPress={submit}
        loading={save.isPending}
        fullWidth
        size="lg"
      />
    </Screen>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    group: {
      gap: theme.spacing.xs,
    },
    chips: {
      flexDirection: 'row',
      flexWrap: 'wrap',
      gap: theme.spacing.xs,
    },
  });
