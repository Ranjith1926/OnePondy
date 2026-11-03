import { useNavigation } from '@react-navigation/native';
import { useTranslation } from 'react-i18next';
import { ScrollView, StyleSheet, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import {
  ErrorState,
  ListItem,
  LoadingState,
  OPButton,
  OPIconButton,
  OPText,
  useThemedStyles,
  type Theme,
} from '../../../design-system';
import { useAppState } from '../../../state';
import { formatDateTime } from '../../bookings/formatDateTime';
import { useRefetchOnFocus } from '../../bookings/hooks/useRefetchOnFocus';
import { useModuleStatus } from '../../modules/hooks/useAppConfig';
import { GrievanceCard } from '../components/GrievanceCard';
import { useMyTickets } from '../hooks/useSupport';

/**
 * Help and support: the customer's own requests and a way to start one, and, always, who the grievance officer is. Requests open only while
 * the support module is available; the grievance officer is shown regardless, because a person with a complaint should never find a blank.
 */
export function HelpScreen() {
  const { t } = useTranslation();
  const navigation = useNavigation();
  const styles = useThemedStyles(createStyles);
  const { auth } = useAppState();
  const modules = useModuleStatus();
  const signedIn = auth.status === 'authenticated';
  const open = modules.support === 'available';
  const tickets = useMyTickets(open);
  useRefetchOnFocus(tickets.refetch, signedIn && open);

  return (
    <SafeAreaView edges={['top', 'left', 'right', 'bottom']} style={styles.screen}>
      <View style={styles.header}>
        <OPIconButton
          icon="arrow-left"
          accessibilityLabel={t('common.back')}
          onPress={() => navigation.goBack()}
        />
      </View>
      <ScrollView contentContainerStyle={[styles.inset, styles.content]}>
        <OPText variant="title" accessibilityRole="header">
          {t('support.title')}
        </OPText>

        <View style={styles.section}>
          <OPText variant="label" accessibilityRole="header">
            {t('support.yourRequests')}
          </OPText>
          {!open ? (
            <OPText tone="secondary">{t('support.notOpen')}</OPText>
          ) : !signedIn ? (
            <>
              <OPText tone="secondary">{t('support.signInPrompt')}</OPText>
              <OPButton
                label={t('guestPrompt.action')}
                variant="secondary"
                onPress={() => navigation.navigate('SignIn')}
              />
            </>
          ) : tickets.isPending ? (
            <LoadingState accessibilityLabel={t('support.loading')} />
          ) : tickets.isError ? (
            <ErrorState
              title={t('support.error.title')}
              message={t('support.error.message')}
              retryLabel={t('common.retry')}
              onRetry={() => void tickets.refetch()}
              retrying={tickets.isFetching}
            />
          ) : (
            <>
              <OPButton
                label={t('support.newRequest')}
                onPress={() => navigation.navigate('NewSupportRequest')}
              />
              {tickets.data.length === 0 ? (
                <OPText tone="secondary">{t('support.empty')}</OPText>
              ) : (
                tickets.data.map((ticket) => (
                  <ListItem
                    key={ticket.id}
                    title={ticket.subject}
                    subtitle={`${t(`support.status.${ticket.status}`)} · ${formatDateTime(ticket.updatedAt)}`}
                    trailing={{ kind: 'chevron' }}
                    onPress={() => navigation.navigate('SupportTicket', { id: ticket.id })}
                  />
                ))
              )}
            </>
          )}
        </View>

        <GrievanceCard />
      </ScrollView>
    </SafeAreaView>
  );
}

const createStyles = (theme: Theme) =>
  StyleSheet.create({
    screen: {
      flex: 1,
      backgroundColor: theme.colors.background,
    },
    header: {
      flexDirection: 'row',
      alignItems: 'center',
      paddingHorizontal: theme.spacing.xxs,
      paddingVertical: theme.spacing.xs,
    },
    inset: {
      paddingHorizontal: theme.layout.screenPadding,
    },
    content: {
      gap: theme.spacing.sm,
      paddingBottom: theme.spacing.xl,
    },
    section: {
      marginTop: theme.spacing.md,
      gap: theme.spacing.xs,
    },
  });
