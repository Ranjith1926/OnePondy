import { codeSchema, emailSchema, nameSchema, phoneSchema } from './authSchemas';

describe('auth form schemas', () => {
  it.each([
    ['98765 43210', '9876543210'],
    ['98765-43210', '9876543210'],
    ['+91 98765 43210', '9876543210'],
    ['919876543210', '9876543210'],
    ['09876543210', '9876543210'],
    ['9198765432', '9198765432'],
  ])('reads %s as the mobile number %s', (typed, phone) => {
    expect(phoneSchema.parse({ phone: typed })).toEqual({ phone });
  });

  it.each(['12345', '5876543210', '98765432101', '+44 7700 900123'])(
    'rejects %s as a mobile number',
    (phone) => {
      const result = phoneSchema.safeParse({ phone });

      expect(result.success).toBe(false);
      expect(result.error?.issues[0]?.message).toBe('auth.validation.phone');
    },
  );

  it('normalises email addresses', () => {
    expect(emailSchema.parse({ email: '  Asha@Example.COM ' })).toEqual({
      email: 'asha@example.com',
    });
    expect(emailSchema.safeParse({ email: 'asha@' }).error?.issues[0]?.message).toBe(
      'auth.validation.email',
    );
  });

  it('accepts only six-digit codes', () => {
    expect(codeSchema.safeParse({ code: '123456' }).success).toBe(true);
    for (const code of ['12345', '1234567', '12a456']) {
      expect(codeSchema.safeParse({ code }).success).toBe(false);
    }
  });

  it('trims names and limits them to 120 characters', () => {
    expect(nameSchema.parse({ displayName: '  Asha  ' })).toEqual({ displayName: 'Asha' });
    expect(nameSchema.safeParse({ displayName: '   ' }).success).toBe(false);
    expect(nameSchema.safeParse({ displayName: 'a'.repeat(121) }).success).toBe(false);
  });
});
