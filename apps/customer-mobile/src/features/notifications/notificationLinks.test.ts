import { bookingLinkFrom } from './notificationLinks';

const id = '3f0c2f64-7a5b-4a52-9d58-0d1d4f2d9b11';

describe('bookingLinkFrom', () => {
  it('reads a known kind with a booking id', () => {
    expect(bookingLinkFrom({ type: 'booking.accepted', bookingId: id })).toEqual({
      type: 'booking.accepted',
      bookingId: id,
    });
  });

  it.each([
    ['not an object', 'booking.accepted'],
    ['null', null],
    ['no booking id', { type: 'booking.accepted' }],
    ['a booking id that is not a uuid', { type: 'booking.accepted', bookingId: '../../admin' }],
    ['a numeric booking id', { type: 'booking.accepted', bookingId: 42 }],
    ['an unknown kind', { type: 'promo.sale', bookingId: id }],
    ['no kind', { bookingId: id }],
  ])('ignores data that is %s', (_name, data) => {
    expect(bookingLinkFrom(data)).toBeNull();
  });
});
