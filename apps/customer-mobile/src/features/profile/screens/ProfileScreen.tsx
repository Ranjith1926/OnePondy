import { useNavigation } from '@react-navigation/native';
import { useMutation } from '@tanstack/react-query';
import Constants from 'expo-constants';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';

import { ListItem, ListSection, MainTabTemplate } from '../../../design-system';
import { languageOptions } from '../../../i18n';
import { useAppState } from '../../../state';
import { useAuthActions } from '../../auth/hooks/useAuthActions';
import { formatPhone, useProfile } from '../../auth/hooks/useProfile';
import { useSelectedArea } from '../../location/hooks/useSelectedArea';
import { ComingSoonSheet } from '../../modules/components/ComingSoonSheet';
import type { ModuleKey } from '../../modules/moduleConfig';
import { AppearanceSheet } from '../components/AppearanceSheet';
import { LanguageSheet } from '../components/LanguageSheet';
import { ProfileSummary } from '../components/ProfileSummary';

type OpenSheet = 'appearance' | 'language' | null;

export function ProfileScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const { preferences, auth } = useAppState();
  const area = useSelectedArea();
  const profile = useProfile();
  const { signOut } = useAuthActions();
  const signOutRequest = useMutation({ mutationFn: signOut });
  const [sheet, setSheet] = useState<OpenSheet>(null);
  const [comingSoon, setComingSoon] = useState<ModuleKey | null>(null);
  const languageName =
    languageOptions.find((option) => option.code === preferences.language)?.nativeName ?? '';
  const version = Constants.expoConfig?.version;

  const contact = profile.data?.phoneNumber
    ? formatPhone(profile.data.phoneNumber)
    : (profile.data?.email ?? undefined);
  const account =
    auth.status === 'authenticated' && auth.user
      ? { displayName: auth.user.displayName, contact }
      : undefined;

  return (
    <>
      <MainTabTemplate title={t('profile.title')}>
        <ProfileSummary account={account} onSignIn={() => navigation.navigate('SignIn')} />
        {account ? (
          <ListSection title={t('profile.sections.account')}>
            <ListItem
              leadingIcon="user-round-pen"
              title={t('profile.editProfile')}
              subtitle={account.displayName ? undefined : t('profile.addName')}
              trailing={{ kind: 'chevron' }}
              onPress={() => navigation.navigate('EditProfile')}
            />
            <ListItem
              leadingIcon="log-out"
              title={t('profile.signOut')}
              onPress={() => signOutRequest.mutate()}
              disabled={signOutRequest.isPending}
            />
            <ListItem
              leadingIcon="trash-2"
              title={t('profile.deleteAccount')}
              trailing={{ kind: 'chevron' }}
              onPress={() => navigation.navigate('DeleteAccount')}
              destructive
            />
          </ListSection>
        ) : null}
        <ListSection title={t('profile.sections.preferences')}>
          <ListItem
            leadingIcon="languages"
            title={t('profile.language')}
            trailing={{ kind: 'value', text: languageName }}
            onPress={() => setSheet('language')}
          />
          <ListItem
            leadingIcon="sun-moon"
            title={t('profile.appearance')}
            trailing={{ kind: 'value', text: t(`appearance.${preferences.theme}`) }}
            onPress={() => setSheet('appearance')}
          />
          <ListItem
            leadingIcon="map-pin"
            title={t('profile.area')}
            trailing={{ kind: 'value', text: area.name }}
            onPress={() => navigation.navigate('LocationSelector')}
          />
        </ListSection>
        <ListSection title={t('profile.sections.support')}>
          <ListItem
            leadingIcon="life-buoy"
            title={t('profile.help')}
            trailing={{ kind: 'chevron' }}
            onPress={() => setComingSoon('support')}
          />
        </ListSection>
        {version ? (
          <ListSection title={t('profile.sections.about')}>
            <ListItem
              leadingIcon="info"
              title={t('profile.version')}
              trailing={{ kind: 'value', text: version }}
            />
          </ListSection>
        ) : null}
        {__DEV__ ? (
          <ListSection title={t('profile.sections.developer')}>
            <ListItem
              leadingIcon="layout-grid"
              title={t('profile.componentGallery')}
              trailing={{ kind: 'chevron' }}
              onPress={() => navigation.navigate('ComponentGallery')}
            />
          </ListSection>
        ) : null}
      </MainTabTemplate>
      <AppearanceSheet visible={sheet === 'appearance'} onClose={() => setSheet(null)} />
      <LanguageSheet visible={sheet === 'language'} onClose={() => setSheet(null)} />
      <ComingSoonSheet module={comingSoon} onClose={() => setComingSoon(null)} />
    </>
  );
}
