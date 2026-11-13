import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
  TestRequest,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Setup } from './setup';
import { InstallationState } from './installation-state';

describe('First-run setup', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  async function create(
    installed = false,
    canInstall = !installed,
  ): Promise<ComponentFixture<Setup>> {
    const fixture = TestBed.createComponent(Setup);
    fixture.detectChanges();
    http.expectOne('/api/installation/status').flush({ installed, canInstall });
    await fixture.whenStable();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toContain('Проверяем состояние установки');
    });
    return fixture;
  }

  async function fill(fixture: ComponentFixture<Setup>): Promise<void> {
    const fields: Record<string, string> = {
      name: 'TestPortal',
      city: 'TestCity',
      region: 'TestRegion',
      timeZone: 'Asia/Yekaterinburg',
      email: 'admin@example.test',
      displayName: 'Admin',
      password: 'InstallPass!123',
      smtpHost: 'smtp.example.test',
      smtpPort: '465',
      senderAddress: 'sender@example.test',
      smtpPassword: 'private-smtp-password',
    };
    for (const [name, value] of Object.entries(fields)) {
      const input = fixture.nativeElement.querySelector(`[name="${name}"]`) as HTMLInputElement;
      input.value = value;
      input.dispatchEvent(new Event('input', { bubbles: true }));
    }
    const security = fixture.nativeElement.querySelector('select') as HTMLSelectElement;
    security.value = 'Tls';
    security.dispatchEvent(new Event('change', { bubbles: true }));
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function pending(path: string): Promise<TestRequest> {
    let request: TestRequest | undefined;
    await vi.waitFor(() => {
      request = http.expectOne(path);
    });
    if (!request) throw new Error('Expected HTTP request was not created.');
    return request;
  }
  it('Setup_InPublicMode_HidesInstallationForm', async () => {
    const fixture = await create(false, false);
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Мастер установки отключён');
    http.expectNone('/api/installation/complete');
  });
  it('Setup_WhenInstalled_HidesCredentialsAndCompletionForm', async () => {
    const fixture = await create(true);
    expect(fixture.nativeElement.textContent).toContain('Мастер установки закрыт');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
  });

  it('Setup_OnSuccess_UsesFreshCsrfWithoutInstallationKeyAndUpdatesBranding', async () => {
    const fixture = await create();
    await fill(fixture);
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    http.expectOne('/api/auth/csrf').flush({ token: 'fresh-csrf' });
    await Promise.resolve();
    const request = await pending('/api/installation/complete');
    expect(request.request.headers.has('X-Installation-Key')).toBe(false);
    expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('fresh-csrf');
    expect(fixture.nativeElement.querySelector('#setup-key')).toBeNull();
    expect(request.request.body).toMatchObject({
      name: 'TestPortal',
      smtpPort: 465,
      useTls: true,
      implicitTls: true,
      smtpPassword: 'private-smtp-password',
    });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await Promise.resolve();
    (await pending('/api/system/info')).flush({
      name: 'TestPortal',
      stage: 1,
      city: 'TestCity',
      region: 'TestRegion',
      timeZone: 'Asia/Yekaterinburg',
    });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Сайт настроен');
    expect(document.title).toBe('TestPortal');
    expect(TestBed.inject(InstallationState).installed()).toBe(true);
  });

  it('Setup_OnSmtpFailure_RetainsInputsAndAllowsRetryWithFreshCsrf', async () => {
    const fixture = await create();
    await fill(fixture);
    for (let attempt = 0; attempt < 2; attempt++) {
      fixture.nativeElement
        .querySelector('form')
        .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
      http.expectOne('/api/auth/csrf').flush({ token: `csrf-${attempt}` });
      await Promise.resolve();
      const request = await pending('/api/installation/complete');
      expect(request.request.headers.get('X-CSRF-TOKEN')).toBe(`csrf-${attempt}`);
      request.flush({ code: 'smtp_test_failed' }, { status: 503, statusText: 'Unavailable' });
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();
      });
      expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
        'SMTP-сервер',
      );
      expect(fixture.nativeElement.querySelector('[name="name"]').value).toBe('TestPortal');
      expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(false);
    }
  });

  it('Setup_WhenAnotherInstallerWins_ClosesWizardAndClearsCredentials', async () => {
    const fixture = await create();
    await fill(fixture);
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    http.expectOne('/api/auth/csrf').flush({ token: 'fresh-csrf' });
    await Promise.resolve();
    (await pending('/api/installation/complete')).flush(
      { code: 'already_installed' },
      { status: 409, statusText: 'Conflict' },
    );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('form')).toBeNull();
    });
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Повторная установка запрещена',
    );
  });
});
