import { screen, userEvent } from '@testing-library/react-native';

import { bookingId } from '../../../test-utils/bookingFixture';
import { orderId } from '../../../test-utils/foodFixture';
import { renderWithProviders } from '../../../test-utils/render';
import { useModuleStatus } from '../../modules/hooks/useAppConfig';
import { GetHelpButton } from './GetHelpButton';

const mockNavigate = jest.fn();

jest.mock('@react-navigation/native', () => ({
  useNavigation: () => ({ navigate: mockNavigate }),
}));
jest.mock('../../modules/hooks/useAppConfig', () => ({ useModuleStatus: jest.fn() }));

const moduleStatus = useModuleStatus as jest.Mock;

beforeEach(() => {
  mockNavigate.mockReset();
  moduleStatus.mockReturnValue({ support: 'available' });
});

describe('<GetHelpButton />', () => {
  it('starts a request about the booking it is on', async () => {
    await renderWithProviders(
      <GetHelpButton about={{ bookingId }} label="Get help with this booking" />,
    );

    await userEvent
      .setup()
      .press(screen.getByRole('button', { name: 'Get help with this booking' }));

    expect(mockNavigate).toHaveBeenCalledWith('NewSupportRequest', { bookingId });
  });

  it('starts a request about the order it is on', async () => {
    await renderWithProviders(
      <GetHelpButton about={{ foodOrderId: orderId }} label="Get help with this order" />,
    );

    await userEvent.setup().press(screen.getByRole('button', { name: 'Get help with this order' }));

    expect(mockNavigate).toHaveBeenCalledWith('NewSupportRequest', { foodOrderId: orderId });
  });

  it('is not shown while support requests are not open', async () => {
    moduleStatus.mockReturnValue({ support: 'comingSoon' });

    await renderWithProviders(
      <GetHelpButton about={{ bookingId }} label="Get help with this booking" />,
    );

    expect(
      screen.queryByRole('button', { name: 'Get help with this booking' }),
    ).not.toBeOnTheScreen();
  });
});
