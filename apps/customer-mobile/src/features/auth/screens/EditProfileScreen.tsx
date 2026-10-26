import { useNavigation } from '@react-navigation/native';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { StyleSheet, View } from 'react-native';

import {
  InlineBanner,
  ModalScreenTemplate,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { useAppDispatch, useAppState, userUpdated } from '../../../state';
import { authApi } from '../api/authApi';
import { NameForm } from '../components/NameForm';
import { profileQueryKey } from '../hooks/useProfile';
import { authErrorMessageKey } from '../services/errorMessages';
import { sessionManager } from '../services/sessionManager';

export function EditProfileScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const dispatch = useAppDispatch();
  const queryClient = useQueryClient();
  const { auth } = useAppState();

  const save = useMutation({
    mutationFn: (displayName: string) => authApi.updateProfile({ displayName }),
    onSuccess: async (profile) => {
      queryClient.setQueryData(profileQueryKey, profile);
      dispatch(userUpdated({ displayName: profile.displayName }));
      if (auth.user) {
        await sessionManager.updateUser({ ...auth.user, displayName: profile.displayName });
      }
      navigation.goBack();
    },
  });

  return (
    <ModalScreenTemplate
      title={t('editProfile.title')}
      closeLabel={t('common.close')}
      onClose={() => navigation.goBack()}
    >
      <View style={styles.body}>
        <NameForm
          defaultName={auth.user?.displayName ?? ''}
          submitLabel={t('editProfile.save')}
          submitting={save.isPending}
          onSubmit={(displayName) => save.mutate(displayName)}
        />
        {save.error ? (
          <InlineBanner kind="error" message={t(authErrorMessageKey(save.error))} />
        ) : null}
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
  });
