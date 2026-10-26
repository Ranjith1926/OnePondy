import { useFocusEffect, type StaticScreenProps } from '@react-navigation/native';
import { useMutation } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { BackHandler, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  InlineBanner,
  OPButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import type { SessionUser } from '../../../state';
import { authApi } from '../api/authApi';
import { NameForm } from '../components/NameForm';
import { useAuthActions } from '../hooks/useAuthActions';
import { authErrorMessageKey } from '../services/errorMessages';
import { sessionManager } from '../services/sessionManager';

type Props = StaticScreenProps<{ user: SessionUser }>;

/** First sign-in only: asks for a display name. Skipping or going back finishes sign-in as is. */
export function CompleteProfileScreen({ route }: Props) {
  const { t } = useTranslation();
  const styles = useThemedStyles(createStyles);
  const { finishSignIn } = useAuthActions();
  const { user } = route.params;

  const save = useMutation({
    mutationFn: (displayName: string) => authApi.updateProfile({ displayName }),
    onSuccess: async (profile) => {
      const updated = { ...user, displayName: profile.displayName };
      await sessionManager.updateUser(updated);
      finishSignIn(updated);
    },
  });

  const skip = useCallback(() => finishSignIn(user), [finishSignIn, user]);

  // The code was already used, so going back would lead nowhere useful.
  useFocusEffect(
    useCallback(() => {
      const subscription = BackHandler.addEventListener('hardwareBackPress', () => {
        skip();
        return true;
      });
      return () => subscription.remove();
    }, [skip]),
  );

  return (
    <SafeAreaView style={styles.screen}>
      <View style={styles.body}>
        <OPText variant="title" accessibilityRole="header">
          {t('auth.completeTitle')}
        </OPText>
        <OPText tone="secondary">{t('auth.completeIntro')}</OPText>
        <NameForm
          submitLabel={t('auth.continue')}
          submitting={save.isPending}
          onSubmit={(displayName) => save.mutate(displayName)}
        />
        {save.error ? (
          <InlineBanner kind="error" message={t(authErrorMessageKey(save.error))} />
        ) : null}
        <View style={styles.skip}>
          <OPButton label={t('auth.skip')} onPress={skip} variant="tertiary" />
        </View>
      </View>
    </SafeAreaView>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    screen: {
      flex: 1,
      backgroundColor: theme.colors.background,
    },
    body: {
      gap: theme.spacing.md,
      padding: theme.layout.screenPadding,
    },
    skip: {
      alignItems: 'center',
    },
  });
