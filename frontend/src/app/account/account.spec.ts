import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { vi } from 'vitest';
import { AccountApi, AccountProfile } from './account-api';
import { ConfirmEmail } from './confirm-email';
import { Login } from './login';
import { Profile } from './profile';
import { Register } from './register';

const account: AccountProfile = {
  id: 'account-1',
  email: 'person@example.test',
  displayName: 'Житель1',
  emailConfirmed: false,
  roles: ['RegularUser', 'Author'],
  version: 1,
  confirmationDelivery: { state: 'Loading', resendAfterSeconds: 60 },
};

const firstLinkId = '64ba148d-3a1a-4cab-9d80-f46bf3ed23c1';
const replacementLinkId = 'd62ff190-c283-45ee-b251-6375d6f2108e';

describe('Account workflows', () => {
  let http: HttpTestingController;
  let route: { fragment: BehaviorSubject<string | null> };
  beforeEach(() => {
    route = { fragment: new BehaviorSubject<string | null>(null) };
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: route },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function csrf(token = 'request-token'): void {
    http.expectOne('/api/auth/csrf').flush({ token });
  }

  async function settle<T>(fixture: ComponentFixture<T>): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function fill<T>(
    fixture: ComponentFixture<T>,
    selector: string,
    value: string,
  ): Promise<void> {
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
    await settle(fixture);
  }

  it('creates an account with the exact password and asks for separate login without claiming mail sent', async () => {
    const fixture = TestBed.createComponent(Register);
    fixture.detectChanges();
    await settle(fixture);
    await fill(fixture, '#register-email', 'person@example.test');
    await fill(fixture, '#display-name', 'Житель1');
    await fill(fixture, '#register-password', '!Exact123');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    csrf();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/register');
      expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('request-token');
      expect(request.request.headers.get('Content-Type')).toBe('application/json');
      expect(request.request.body).toEqual({
        email: 'person@example.test',
        displayName: 'Житель1',
        password: '!Exact123',
      });
      request.flush(account, { status: 201, statusText: 'Created' });
    });
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Аккаунт создан');
    expect(element.querySelector('a.primary-link')?.getAttribute('href')).toBe('/account/login');
    expect(element.textContent).not.toContain('Отправлено');
    expect(element.querySelector('input[type=password]')).toBeNull();
    http.expectNone('/api/auth/login');
  });

  it('shows duplicate address guidance without creating a session', async () => {
    const fixture = TestBed.createComponent(Register);
    fixture.detectChanges();
    await settle(fixture);
    await fill(fixture, '#register-email', 'person@example.test');
    await fill(fixture, '#display-name', 'Житель1');
    await fill(fixture, '#register-password', '!Exact123');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    csrf();
    await vi.waitFor(() =>
      http
        .expectOne('/api/auth/register')
        .flush({ code: 'duplicate_email' }, { status: 409, statusText: 'Conflict' }),
    );
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Этот Email уже зарегистрирован');
    expect(fixture.nativeElement.textContent).not.toContain('Аккаунт создан');
    http.expectNone('/api/auth/login');
  });

  it('fetches fresh CSRF protection across consecutive identity writes', async () => {
    const api = TestBed.inject(AccountApi);
    const login = api.login({
      email: 'person@example.test',
      password: '!Exact123',
      rememberMe: true,
    });
    csrf('anonymous-token');
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/login');
      expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('anonymous-token');
      expect(request.request.body.rememberMe).toBe(true);
      request.flush(account);
    });
    await login;
    const logout = api.logout();
    csrf('authenticated-token');
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/logout');
      expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('authenticated-token');
      request.flush(null, { status: 204, statusText: 'No Content' });
    });
    await logout;
  });

  it('renders server validation in Russian and retry wait information for public limits', async () => {
    const fixture = TestBed.createComponent(Register);
    fixture.detectChanges();
    await settle(fixture);
    await fill(fixture, '#register-email', 'person@example.test');
    await fill(fixture, '#display-name', 'Bad-name');
    await fill(fixture, '#register-password', '!Exact123');
    const submit = () =>
      fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    submit();
    csrf();
    await vi.waitFor(() =>
      http
        .expectOne('/api/auth/register')
        .flush(
          { code: 'validation_failed', errors: { DisplayName: ['Only letters and digits.'] } },
          { status: 400, statusText: 'Bad Request' },
        ),
    );
    await settle(fixture);
    expect(fixture.nativeElement.querySelector('#name-errors').textContent).toContain(
      'от 3 до 20 букв и цифр',
    );
    expect(fixture.nativeElement.querySelector('#display-name').getAttribute('aria-invalid')).toBe(
      'true',
    );
    expect(fixture.nativeElement.textContent).not.toContain('Only letters');
    submit();
    csrf();
    await vi.waitFor(() =>
      http
        .expectOne('/api/auth/register')
        .flush(
          { code: 'rate_limited', retryAfterSeconds: 47 },
          { status: 429, statusText: 'Too Many Requests' },
        ),
    );
    await settle(fixture);
    expect(fixture.nativeElement.querySelector('[role=alert]').textContent).toContain('47 сек.');
    expect(fixture.nativeElement.querySelector('button').disabled).toBe(false);
  });

  it.each([
    ['missing fragment', ''],
    ['malformed identifier', 'linkId=not-a-guid&token=secret'],
    ['empty identifier', 'linkId=00000000-0000-0000-0000-000000000000&token=secret'],
    ['missing token', `linkId=${firstLinkId}`],
    ['whitespace token', `linkId=${firstLinkId}&token=%20`],
    ['overlong token', `linkId=${firstLinkId}&token=${'x'.repeat(129)}`],
  ])('does not submit an invalid confirmation link with %s', (_case, fragment) => {
    route.fragment.next(fragment);
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Ссылка недействительна');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    http.expectNone('/api/auth/csrf');
    http.expectNone('/api/auth/confirm-email');
  });

  it('uses a replacement link on the same route and ignores an older in-flight confirmation', async () => {
    route.fragment.next(`linkId=${firstLinkId}&token=old-secret`);
    const replace = vi.spyOn(TestBed.inject(Location), 'replaceState');
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.detectChanges();
    const oldSubmission = fixture.componentInstance['confirm']();
    csrf();
    const oldRequest = await vi.waitFor(() => http.expectOne('/api/auth/confirm-email'));
    expect(oldRequest.request.body).toEqual({ linkId: firstLinkId, token: 'old-secret' });

    route.fragment.next(`linkId=${replacementLinkId}&token=new-secret`);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Нажмите кнопку');
    expect(fixture.nativeElement.querySelector('button')).not.toBeNull();
    expect(replace).toHaveBeenCalledTimes(2);
    http.expectNone('/api/auth/csrf');
    const newSubmission = fixture.componentInstance['confirm']();
    csrf('new-link-csrf');
    const newRequest = await vi.waitFor(() => http.expectOne('/api/auth/confirm-email'));
    expect(newRequest.request.body).toEqual({ linkId: replacementLinkId, token: 'new-secret' });

    oldRequest.flush({ status: 'AlreadyConfirmed' });
    await oldSubmission;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Подтверждаем Email');
    expect(fixture.nativeElement.textContent).not.toContain('Email уже подтверждён');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    newRequest.flush({ status: 'Confirmed' });
    await newSubmission;
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Email подтверждён');
    expect(fixture.nativeElement.textContent).not.toContain('new-secret');
    fixture.destroy();
  });

  it('removes the confirmation secret from history and requires an explicit POST action', async () => {
    route.fragment.next(`linkId=${firstLinkId}&token=bearer-secret`);
    const replace = vi.spyOn(TestBed.inject(Location), 'replaceState');
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.detectChanges();
    expect(replace).toHaveBeenCalledWith('/account/confirm-email');
    http.expectNone('/api/auth/csrf');
    expect(fixture.nativeElement.textContent).toContain('Нажмите кнопку');
    fixture.nativeElement.querySelector('button').click();
    csrf();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/confirm-email');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ linkId: firstLinkId, token: 'bearer-secret' });
      request.flush({ status: 'AlreadyConfirmed' });
    });
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Email уже подтверждён');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('bearer-secret');
  });

  it.each([
    ['expired_link', 'Срок действия ссылки истёк'],
    ['invalid_link', 'Ссылка недействительна'],
  ])('explains %s without offering confirmation resend', async (code, message) => {
    route.fragment.next(`linkId=${firstLinkId}&token=secret`);
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.detectChanges();
    fixture.nativeElement.querySelector('button').click();
    csrf();
    await vi.waitFor(() =>
      http
        .expectOne('/api/auth/confirm-email')
        .flush({ code }, { status: 400, statusText: 'Bad Request' }),
    );
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain(message);
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    http.expectNone('/api/account/email-confirmation');
  });

  it('allows a failed confirmation request to be retried without restoring the secret URL', async () => {
    route.fragment.next(`linkId=${firstLinkId}&token=secret`);
    const replace = vi.spyOn(TestBed.inject(Location), 'replaceState');
    const fixture = TestBed.createComponent(ConfirmEmail);
    fixture.detectChanges();
    fixture.nativeElement.querySelector('button').click();
    csrf();
    await vi.waitFor(() =>
      http
        .expectOne('/api/auth/confirm-email')
        .flush(null, { status: 503, statusText: 'Unavailable' }),
    );
    await settle(fixture);
    expect(fixture.nativeElement.querySelector('button').textContent.trim()).toBe('Повторить');
    fixture.nativeElement.querySelector('button').click();
    csrf('retry-token');
    await vi.waitFor(() =>
      http.expectOne('/api/auth/confirm-email').flush({ status: 'Confirmed' }),
    );
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Email подтверждён');
    expect(fixture.nativeElement.querySelector('button')).toBeNull();
    expect(replace).toHaveBeenCalledTimes(1);
  });

  it('shows profile loading, failed mail and later SMTP acceptance without exposing internal queue states', async () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();
    http.expectOne('/api/account').flush(account);
    await settle(fixture);
    expect(fixture.nativeElement.querySelector('.spinner')).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Пользователь, Автор');
    expect(fixture.nativeElement.textContent).toContain('60 сек.');
    const refresh = fixture.nativeElement.querySelector('.text-button') as HTMLButtonElement;
    refresh.click();
    http
      .expectOne('/api/account')
      .flush({ ...account, confirmationDelivery: { state: 'Error', resendAfterSeconds: 0 } });
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Ошибка, повторите письмо');
    expect(fixture.nativeElement.querySelector('.spinner')).toBeNull();
    refresh.click();
    http
      .expectOne('/api/account')
      .flush({ ...account, confirmationDelivery: { state: 'Sent', resendAfterSeconds: 0 } });
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Отправлено');
    expect(fixture.nativeElement.textContent).not.toContain('Ошибка, повторите письмо');
    fixture.destroy();
  });

  it('redirects an expired profile session to login', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();
    http.expectOne('/api/account').flush(null, { status: 401, statusText: 'Unauthorized' });
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith('/account/login');
    fixture.destroy();
  });

  it('hides confirmation resend after the account is confirmed', async () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();
    http.expectOne('/api/account').flush({ ...account, emailConfirmed: true });
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Подтверждён');
    expect(fixture.nativeElement.querySelector('.confirmation-card')).toBeNull();
    http.expectNone('/api/account/email-confirmation');
    fixture.destroy();
  });

  it('does not let an older profile response overwrite the latest requested mail status', async () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();
    http
      .expectOne('/api/account')
      .flush({ ...account, confirmationDelivery: { state: 'Error', resendAfterSeconds: 0 } });
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    (element.querySelector('.text-button') as HTMLButtonElement).click();
    const oldProfile = http.expectOne('/api/account');
    (element.querySelector('.confirmation-card .secondary-button') as HTMLButtonElement).click();
    csrf();
    await vi.waitFor(() =>
      http
        .expectOne('/api/account/email-confirmation')
        .flush(
          { state: 'Loading', resendAfterSeconds: 60 },
          { status: 202, statusText: 'Accepted' },
        ),
    );
    await settle(fixture);
    oldProfile.flush({
      ...account,
      confirmationDelivery: { state: 'Error', resendAfterSeconds: 0 },
    });
    await settle(fixture);
    expect(element.querySelector('.spinner')).not.toBeNull();
    expect(element.textContent).not.toContain('Ошибка, повторите письмо');
    expect(
      (element.querySelector('.confirmation-card .secondary-button') as HTMLButtonElement).disabled,
    ).toBe(true);
    fixture.destroy();
  });

  it('resends only from the profile and treats persisted work as loading until SMTP accepts it', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();
    http
      .expectOne('/api/account')
      .flush({ ...account, confirmationDelivery: { state: 'Error', resendAfterSeconds: 0 } });
    await settle(fixture);
    const element = fixture.nativeElement as HTMLElement;
    const resend = element.querySelector(
      '.confirmation-card .secondary-button',
    ) as HTMLButtonElement;
    expect(resend.disabled).toBe(false);
    resend.click();
    csrf();
    await vi.waitFor(() =>
      http
        .expectOne('/api/account/email-confirmation')
        .flush(
          { state: 'Loading', resendAfterSeconds: 60 },
          { status: 202, statusText: 'Accepted' },
        ),
    );
    await settle(fixture);
    expect(element.querySelector('.spinner')).not.toBeNull();
    expect(element.textContent).not.toContain('Ошибка, повторите письмо');
    expect(element.textContent).not.toContain('Отправлено');
    expect(resend.disabled).toBe(true);
    const logout = [...element.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === 'Выйти',
    )!;
    logout.click();
    csrf('session-token');
    await vi.waitFor(() =>
      http.expectOne('/api/auth/logout').flush(null, { status: 204, statusText: 'No Content' }),
    );
    await settle(fixture);
    expect(navigate).toHaveBeenCalledWith('/account/login');
    expect(element.querySelector('dd')).toBeNull();
    fixture.destroy();
  });

  it('keeps failed login feedback generic and clears the password', async () => {
    const fixture = TestBed.createComponent(Login);
    fixture.detectChanges();
    await settle(fixture);
    await fill(fixture, '#login-email', 'person@example.test');
    await fill(fixture, '#login-password', '!Wrong123');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    csrf();
    await vi.waitFor(() =>
      http
        .expectOne('/api/auth/login')
        .flush({ code: 'invalid_login' }, { status: 401, statusText: 'Unauthorized' }),
    );
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Неверный Email или пароль');
    expect(
      (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('#login-password')!
        .value,
    ).toBe('');
  });
});
