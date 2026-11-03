import { useNavigation } from '@react-navigation/native';
import { useState } from 'react';
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
import { vehicleClasses, type RideDriverProfile, type VehicleClass } from '../api/rideDriverApi';
import { useRideDriverProfile, useSaveRideDriver } from '../hooks/useRideDriver';
import { rideErrorMessageKey } from '../services/errorMessages';
import { isValidInsuranceDate, tidyNumber } from '../services/rideRules';

type Field =
  | 'fullName'
  | 'vehicleClass'
  | 'vehicleNumber'
  | 'licenceNumber'
  | 'permitNumber'
  | 'insurancePolicyNumber'
  | 'insuranceValidUntil';

/**
 * The application to carry passengers, and the same form for changing details later. Changing anything sends an approved driver back for review
 * and off duty, so the form says so first. Every number is required: passenger transport has no exemptions.
 */
export function RideApplyScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const profile = useRideDriverProfile().data;

  // The fields start from what is on file, so the form waits until that is known.
  if (!profile) {
    return (
      <Screen
        title={t('rides.apply.title')}
        backLabel={t('common.back')}
        onBack={() => navigation.goBack()}
      >
        {null}
      </Screen>
    );
  }

  return <RideApplyForm profile={profile} />;
}

function RideApplyForm({ profile }: { profile: RideDriverProfile }) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const editing = profile.configured;
  const save = useSaveRideDriver();
  const [values, setValues] = useState({
    fullName: profile.fullName ?? '',
    vehicleNumber: profile.vehicleNumber ?? '',
    licenceNumber: profile.licenceNumber ?? '',
    permitNumber: profile.permitNumber ?? '',
    insurancePolicyNumber: profile.insurancePolicyNumber ?? '',
    insuranceValidUntil: profile.insuranceValidUntil ?? '',
  });
  const [vehicleClass, setVehicleClass] = useState<VehicleClass | null>(
    profile.vehicleClass ?? null,
  );
  const [tried, setTried] = useState(false);

  const set = (field: keyof typeof values) => (text: string) =>
    setValues((previous) => ({ ...previous, [field]: text }));

  const problems: Partial<Record<Field, string>> = {};
  if (values.fullName.trim().length < 2) {
    problems.fullName = 'rides.apply.errors.name';
  }
  if (!vehicleClass) {
    problems.vehicleClass = 'rides.apply.errors.vehicleClass';
  }
  if (!/^[A-Z0-9]{4,12}$/.test(tidyNumber(values.vehicleNumber))) {
    problems.vehicleNumber = 'rides.apply.errors.vehicleNumber';
  }
  if (!/^[A-Z0-9]{8,20}$/.test(tidyNumber(values.licenceNumber))) {
    problems.licenceNumber = 'rides.apply.errors.licenceNumber';
  }
  if (!/^[A-Z0-9]{4,30}$/.test(tidyNumber(values.permitNumber))) {
    problems.permitNumber = 'rides.apply.errors.permitNumber';
  }
  if (!/^[A-Z0-9]{4,30}$/.test(tidyNumber(values.insurancePolicyNumber))) {
    problems.insurancePolicyNumber = 'rides.apply.errors.policyNumber';
  }
  if (!isValidInsuranceDate(values.insuranceValidUntil)) {
    problems.insuranceValidUntil = 'rides.apply.errors.insuranceDate';
  }

  const error = (field: Field) =>
    tried && problems[field] ? t(problems[field] as never) : undefined;

  const vehicleClassError = error('vehicleClass');

  const submit = () => {
    setTried(true);
    if (Object.keys(problems).length > 0 || !vehicleClass) {
      return;
    }

    save.mutate(
      {
        fullName: values.fullName.trim(),
        vehicleClass,
        vehicleNumber: tidyNumber(values.vehicleNumber),
        licenceNumber: tidyNumber(values.licenceNumber),
        permitNumber: tidyNumber(values.permitNumber),
        insurancePolicyNumber: tidyNumber(values.insurancePolicyNumber),
        insuranceValidUntil: values.insuranceValidUntil.trim(),
        version: editing ? profile.version : undefined,
      },
      { onSuccess: () => navigation.navigate('RideHome') },
    );
  };

  return (
    <Screen
      title={editing ? t('rides.apply.editTitle') : t('rides.apply.title')}
      backLabel={t('common.back')}
      onBack={() => navigation.goBack()}
    >
      <OPText tone="secondary">{t('rides.apply.intro')}</OPText>

      <FormField
        label={t('rides.apply.name')}
        value={values.fullName}
        onChangeText={set('fullName')}
        error={error('fullName')}
        maxLength={100}
      />

      <View style={styles.group}>
        <OPText variant="label">{t('rides.apply.vehicleClass')}</OPText>
        <View style={styles.chips}>
          {vehicleClasses.map((option) => (
            <OPChip
              key={option}
              label={t(`rides.vehicle.${option}`)}
              selected={vehicleClass === option}
              onPress={() => setVehicleClass(option)}
            />
          ))}
        </View>
        {vehicleClassError ? (
          <OPText variant="caption" tone="error" accessibilityLiveRegion="polite">
            {vehicleClassError}
          </OPText>
        ) : null}
      </View>

      <FormField
        label={t('rides.apply.vehicleNumber')}
        hint={t('rides.apply.vehicleNumberHint')}
        value={values.vehicleNumber}
        onChangeText={set('vehicleNumber')}
        error={error('vehicleNumber')}
        autoCapitalize="characters"
        autoCorrect={false}
        maxLength={20}
      />
      <FormField
        label={t('rides.apply.licenceNumber')}
        value={values.licenceNumber}
        onChangeText={set('licenceNumber')}
        error={error('licenceNumber')}
        autoCapitalize="characters"
        autoCorrect={false}
        maxLength={30}
      />
      <FormField
        label={t('rides.apply.permitNumber')}
        hint={t('rides.apply.permitHint')}
        value={values.permitNumber}
        onChangeText={set('permitNumber')}
        error={error('permitNumber')}
        autoCapitalize="characters"
        autoCorrect={false}
        maxLength={40}
      />
      <FormField
        label={t('rides.apply.policyNumber')}
        value={values.insurancePolicyNumber}
        onChangeText={set('insurancePolicyNumber')}
        error={error('insurancePolicyNumber')}
        autoCapitalize="characters"
        autoCorrect={false}
        maxLength={40}
      />
      <FormField
        label={t('rides.apply.insuranceDate')}
        hint={t('rides.apply.insuranceDateHint')}
        value={values.insuranceValidUntil}
        onChangeText={set('insuranceValidUntil')}
        error={error('insuranceValidUntil')}
        keyboardType="numbers-and-punctuation"
        autoCorrect={false}
        maxLength={10}
      />

      <OPText variant="caption" tone="secondary">
        {t('rides.apply.documentsNote')}
      </OPText>
      {editing && profile.approvalStatus === 'approved' ? (
        <InlineBanner kind="warning" message={t('rides.apply.reviewWarning')} />
      ) : null}
      {save.error ? (
        <InlineBanner kind="error" message={t(rideErrorMessageKey(save.error))} />
      ) : null}

      <OPButton
        label={editing ? t('rides.apply.submitEdit') : t('rides.apply.submit')}
        onPress={submit}
        loading={save.isPending}
        fullWidth
        size="lg"
        testID="ride-apply-submit"
      />
    </Screen>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    group: { gap: theme.spacing.xs },
    chips: { flexDirection: 'row', flexWrap: 'wrap', gap: theme.spacing.xs },
  });
