import { screen, userEvent } from '@testing-library/react-native';

import { renderWithProviders } from '../../../test-utils/render';
import { DutyPill } from './DutyPill';

describe('<DutyPill />', () => {
  it('says plainly that the driver is off duty, and goes on duty when the pill is pressed', async () => {
    const onChange = jest.fn();
    await renderWithProviders(<DutyPill onDuty={false} locked={false} onChange={onChange} />);

    expect(screen.getByText('OFF DUTY')).toBeOnTheScreen();
    await userEvent.setup().press(screen.getByText('OFF DUTY'));

    expect(onChange).toHaveBeenCalledWith(true);
  });

  it('says on duty, and goes off duty when the pill is pressed', async () => {
    const onChange = jest.fn();
    await renderWithProviders(<DutyPill onDuty locked={false} onChange={onChange} />);

    expect(screen.getByText('ON DUTY')).toBeOnTheScreen();
    await userEvent.setup().press(screen.getByText('ON DUTY'));

    expect(onChange).toHaveBeenCalledWith(false);
  });

  it('gives screen readers one control, the switch, with its state', async () => {
    await renderWithProviders(<DutyPill onDuty locked={false} onChange={() => {}} />);

    const duty = screen.getByRole('switch', { name: 'On duty' });
    expect(duty).toBeChecked();
    expect(screen.getAllByRole('switch')).toHaveLength(1);
  });

  it('does nothing while it is locked, and says so to assistive technology', async () => {
    const onChange = jest.fn();
    await renderWithProviders(<DutyPill onDuty locked onChange={onChange} />);

    await userEvent.setup().press(screen.getByText('ON DUTY'));

    expect(onChange).not.toHaveBeenCalled();
    expect(screen.getByRole('switch', { name: 'On duty' })).toBeDisabled();
  });
});
