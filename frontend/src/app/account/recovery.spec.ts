import { Location } from '@angular/common';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ForgotPassword } from './forgot-password';
import { ResetPassword } from './reset-password';
import { Profile } from './profile';

const linkId = '64ba148d-3a1a-4cab-9d80-f46bf3ed23c1';
const account = {
  id: linkId,
  email: 'person@example.test',
  displayName: 'Resident',
  emailConfirmed: true,
  roles: ['RegularUser'],
  version: 2,
  confirmationDelivery: { state: 'Sent', resendAfterSeconds: 0 },
};

describe('Recovery and profile editing', () => {
  let http: HttpTestingController;
  let fragment: BehaviorSubject<string | null>;
  beforeEach(() => {
    fragment = new BehaviorSubject<string | null>(null);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { fragment } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

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

  function csrf(): void {
    http.expectOne('/api/auth/csrf').flush({ token: 'fresh-token' });
  }

  it('requests recovery with fresh CSRF and shows a generic response without claiming delivery', async () => {
    const fixture = TestBed.createComponent(ForgotPassword);
    fixture.detectChanges();
    await settle(fixture);
    await fill(fixture, '#recovery-email', 'missing@example.test');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    csrf();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/forgot-password');
      expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('fresh-token');
      expect(request.request.body).toEqual({ email: 'missing@example.test' });
      request.flush(null, { status: 202, statusText: 'Accepted' });
    });
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Если аккаунт с таким Email существует');
    expect(fixture.nativeElement.textContent).not.toContain('Отправлено');
  });

  it('clears recovery secrets from history, waits for submit, and clears the password after success', async () => {
    fragment.next(`linkId=${linkId}&token=secret`);
    const location = vi.spyOn(TestBed.inject(Location), 'replaceState');
    const fixture = TestBed.createComponent(ResetPassword);
    fixture.detectChanges();
    await settle(fixture);
    expect(location).toHaveBeenCalledWith('/account/reset-password');
    http.expectNone('/api/auth/reset-password');
    await fill(fixture, '#reset-password', '!Exact123');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    csrf();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/reset-password');
      expect(request.request.body).toEqual({ linkId, token: 'secret', newPassword: '!Exact123' });
      expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('fresh-token');
      request.flush(null, { status: 204, statusText: 'No Content' });
    });
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Все сеансы завершены');
    expect(fixture.nativeElement.querySelector('input[type=password]')).toBeNull();
    http.expectNone('/api/auth/login');
  });

  it.each(['invalid_link', 'expired_link'])(
    'offers anonymous replacement after %s',
    async (code) => {
      fragment.next(`linkId=${linkId}&token=secret`);
      const fixture = TestBed.createComponent(ResetPassword);
      fixture.detectChanges();
      await settle(fixture);
      await fill(fixture, '#reset-password', '!Exact123');
      fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
      csrf();
      await vi.waitFor(() =>
        http
          .expectOne('/api/auth/reset-password')
          .flush({ code }, { status: 400, statusText: 'Bad Request' }),
      );
      await settle(fixture);
      expect(fixture.nativeElement.querySelector('form')).toBeNull();
      expect(fixture.nativeElement.querySelector('a').getAttribute('href')).toBe(
        '/account/forgot-password',
      );
      expect(fixture.nativeElement.textContent).toContain('Запросите новое письмо');
    },
  );

  it('rejects malformed recovery fragments without submitting a request', async () => {
    fragment.next('linkId=bad&token=secret');
    const fixture = TestBed.createComponent(ResetPassword);
    fixture.detectChanges();
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Ссылка недействительна');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    http.expectNone('/api/auth/csrf');
  });

  it('sends the profile version and reloads current data after an edit conflict', async () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();
    http.expectOne('/api/account').flush(account);
    await settle(fixture);
    await fill(fixture, '#edit-display-name', 'Changed');
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    csrf();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/account/display-name');
      expect(request.request.method).toBe('PATCH');
      expect(request.request.body).toEqual({ displayName: 'Changed', version: 2 });
      request.flush({ code: 'version_conflict' }, { status: 409, statusText: 'Conflict' });
    });
    await vi.waitFor(() =>
      http.expectOne('/api/account').flush({ ...account, displayName: 'Other', version: 3 }),
    );
    await settle(fixture);
    expect(fixture.nativeElement.querySelector('#edit-display-name').value).toBe('Other');
    expect(fixture.nativeElement.textContent).toContain('Профиль изменился');
    fixture.destroy();
  });

  it('requires the current password and presents separate login after changing it', async () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();
    http.expectOne('/api/account').flush(account);
    await settle(fixture);
    await fill(fixture, '#current-password', 'Current1!');
    await fill(fixture, '#new-password', 'Changed1!');
    fixture.nativeElement.querySelectorAll('form')[1].dispatchEvent(new Event('submit'));
    csrf();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/account/change-password');
      expect(request.request.body).toEqual({
        currentPassword: 'Current1!',
        newPassword: 'Changed1!',
        version: 2,
      });
      request.flush(null, { status: 204, statusText: 'No Content' });
    });
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Все сеансы завершены');
    expect(fixture.nativeElement.querySelector('input[type=password]')).toBeNull();
    expect(fixture.nativeElement.querySelector('a').getAttribute('href')).toBe('/account/login');
    fixture.destroy();
  });

  it('hides editing controls for an unconfirmed account', async () => {
    const fixture = TestBed.createComponent(Profile);
    fixture.detectChanges();
    http.expectOne('/api/account').flush({ ...account, emailConfirmed: false });
    await settle(fixture);
    expect(fixture.nativeElement.querySelector('#edit-display-name')).toBeNull();
    expect(fixture.nativeElement.querySelector('#new-password')).toBeNull();
    fixture.destroy();
  });
});
