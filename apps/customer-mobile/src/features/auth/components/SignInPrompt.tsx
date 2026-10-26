import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';

import { EmptyState, type IconName } from '../../../design-system';

interface SignInPromptProps {
  icon: IconName;
  title: string;
  message: string;
}

/** Shown to guests where an action needs an account (spec §4 Guest access). */
export function SignInPrompt({ icon, title, message }: SignInPromptProps) {
  const { t } = useTranslation();
  const navigation = useNavigation();
  return (
    <EmptyState
      icon={icon}
      title={title}
      message={message}
      actionLabel={t('guestPrompt.action')}
      onAction={() => navigation.navigate('SignIn')}
    />
  );
}
