import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Accounts } from './accounts';
import { EmailDeliveries } from './email-deliveries';

const actorId = '64ba148d-3a1a-4cab-9d80-f46bf3ed23c1';
const targetId = 'd62ff190-c283-45ee-b251-6375d6f2108e';
const target = {
  id: targetId,
  email: 'writer@example.test',
  displayName: 'Writer',
  emailConfirmed: true,
  roles: ['RegularUser', 'Author'],
  version: 2,
};
const profile = {
  ...target,
  id: actorId,
  roles: ['Administrator', 'RegularUser'],
  confirmationDelivery: { state: 'Sent', resendAfterSeconds: 0 },
};
const delivery = {
  id: targetId,
  accountId: targetId,
  purpose: 'confirmation',
  state: 'Failed',
  attemptCount: 6,
  retryCount: 6,
  createdAtUtc: '2026-11-12T00:00:00Z',
  expiresAtUtc: '2026-11-13T00:00:00Z',
  version: 8,
};

describe('Administration workflows', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  function cityInfo(): void {
    http
      .expectOne('/api/system/info')
      .flush({ name: 'CityPortal', stage: 1, timeZone: 'Asia/Yekaterinburg' });
  }

  async function settle<T>(fixture: ComponentFixture<T>): Promise<void> {
    await fixture.whenStable();
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toContain('Загружаем…');
    });
  }

  function roleCheckbox(fixture: ComponentFixture<Accounts>, label: string): HTMLInputElement {
    const labels = [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll('label.checkbox-label'),
    ];
    const match = labels.find((item) => item.textContent?.trim() === label);
    expect(match).toBeDefined();
    return match!.querySelector('input')!;
  }

  async function accounts(): Promise<ComponentFixture<Accounts>> {
    const fixture = TestBed.createComponent(Accounts);
    fixture.detectChanges();
    cityInfo();
    http.expectOne('/api/account').flush(profile);
    await vi.waitFor(() =>
      http
        .expectOne((request) => request.urlWithParams.startsWith('/api/admin/accounts?'))
        .flush({ items: [target], totalCount: '1', page: '1', pageSize: '20' }),
    );
    await settle(fixture);
    return fixture;
  }

  async function open(fixture: ComponentFixture<Accounts>, account = target): Promise<void> {
    fixture.nativeElement.querySelector('tbody button').click();
    http.expectOne('/api/admin/accounts/' + targetId).flush(account);
    http
      .expectOne((request) => request.urlWithParams.includes('/role-history?'))
      .flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
    await settle(fixture);
  }

  it('does not load administrative data for a regular or unconfirmed account', async () => {
    const fixture = TestBed.createComponent(Accounts);
    fixture.detectChanges();
    cityInfo();
    http.expectOne('/api/account').flush({ ...profile, emailConfirmed: false });
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Доступ разрешён только администратору');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    http.expectNone((request) => request.url.startsWith('/api/admin'));
  });

  it('normalizes pagination and submits account filters from the search form', async () => {
    const fixture = await accounts();
    const input = fixture.nativeElement.querySelector('#account-search') as HTMLInputElement;
    input.value = 'writer';
    input.dispatchEvent(new Event('input'));
    const role = fixture.nativeElement.querySelector('#role-filter') as HTMLSelectElement;
    role.value = 'Author';
    role.dispatchEvent(new Event('change'));
    await settle(fixture);
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit'));
    const request = http.expectOne((request) =>
      request.urlWithParams.startsWith('/api/admin/accounts?'),
    );
    expect(request.request.urlWithParams).toContain('query=writer');
    expect(request.request.urlWithParams).toContain('role=Author');
    expect(request.request.urlWithParams).toContain('page=1');
    request.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Найдено: 0');
  });

  it('writes the desired roles with the current version and fresh CSRF then refreshes history', async () => {
    const fixture = await accounts();
    await open(fixture);
    const editor = roleCheckbox(fixture, 'Редактор');
    editor.checked = true;
    editor.dispatchEvent(new Event('change'));
    await settle(fixture);
    fixture.nativeElement.querySelectorAll('form')[1].dispatchEvent(new Event('submit'));
    http.expectOne('/api/auth/csrf').flush({ token: 'fresh' });
    await vi.waitFor(() => {
      const write = http.expectOne('/api/admin/accounts/' + targetId + '/roles');
      expect(write.request.method).toBe('PUT');
      expect(write.request.headers.get('X-CSRF-TOKEN')).toBe('fresh');
      expect(write.request.body).toEqual({
        roles: ['RegularUser', 'Author', 'Editor'],
        version: 2,
      });
      write.flush({ ...target, roles: ['RegularUser', 'Author', 'Editor'], version: 3 });
    });
    await vi.waitFor(() => {
      http.expectOne('/api/admin/accounts/' + targetId).flush({ ...target, version: 3 });
      http
        .expectOne((request) => request.urlWithParams.includes('/role-history?'))
        .flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
    });
    await vi.waitFor(() =>
      http
        .expectOne((request) => request.urlWithParams.startsWith('/api/admin/accounts?'))
        .flush({ items: [target], totalCount: 1, page: 1, pageSize: 20 }),
    );
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Роли сохранены');
  });

  it('reloads the selected account after a version conflict without overwriting it', async () => {
    const fixture = await accounts();
    await open(fixture);
    fixture.nativeElement.querySelectorAll('form')[1].dispatchEvent(new Event('submit'));
    http.expectOne('/api/auth/csrf').flush({ token: 'fresh' });
    await vi.waitFor(() =>
      http
        .expectOne('/api/admin/accounts/' + targetId + '/roles')
        .flush({ code: 'version_conflict' }, { status: 409, statusText: 'Conflict' }),
    );
    await vi.waitFor(() => {
      http
        .expectOne('/api/admin/accounts/' + targetId)
        .flush({ ...target, roles: ['RegularUser', 'Editor'], version: 3 });
      http
        .expectOne((request) => request.urlWithParams.includes('/role-history?'))
        .flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
    });
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Данные изменились');
    expect(roleCheckbox(fixture, 'Редактор').checked).toBe(true);
    expect(roleCheckbox(fixture, 'Автор').checked).toBe(false);
  });

  it('keeps personal access and stops admin reads after self-removal', async () => {
    const fixture = await accounts();
    await open(fixture, { ...target, id: actorId, roles: ['Administrator', 'RegularUser'] });
    const administrator = roleCheckbox(fixture, 'Администратор');
    administrator.checked = false;
    administrator.dispatchEvent(new Event('change'));
    await settle(fixture);
    fixture.nativeElement.querySelectorAll('form')[1].dispatchEvent(new Event('submit'));
    http.expectOne('/api/auth/csrf').flush({ token: 'fresh' });
    await vi.waitFor(() =>
      http
        .expectOne('/api/admin/accounts/' + actorId + '/roles')
        .flush({ ...target, id: actorId, roles: ['RegularUser'], version: 3 }),
    );
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Права администратора сняты');
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/account"]')).not.toBeNull();
  });

  it.each([false, true])(
    'replays only a versioned failed delivery and retains the result after refresh; rejected=%s',
    async (rejected) => {
      const fixture = TestBed.createComponent(EmailDeliveries);
      fixture.detectChanges();
      cityInfo();
      http
        .expectOne((request) => request.urlWithParams.startsWith('/api/admin/email-deliveries?'))
        .flush({ items: [delivery], totalCount: 1, page: 1, pageSize: 20 });
      await settle(fixture);
      expect(fixture.nativeElement.textContent).toContain('05:00');
      fixture.nativeElement.querySelector('tbody button').click();
      http.expectOne('/api/auth/csrf').flush({ token: 'fresh' });
      await vi.waitFor(() => {
        const request = http.expectOne('/api/admin/email-deliveries/' + targetId + '/replay');
        expect(request.request.body).toEqual({ version: 8 });
        expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('fresh');
        if (rejected)
          request.flush({ code: 'invalid_link' }, { status: 400, statusText: 'Bad Request' });
        else request.flush(null, { status: 202, statusText: 'Accepted' });
      });
      await vi.waitFor(() =>
        http
          .expectOne((request) => request.urlWithParams.startsWith('/api/admin/email-deliveries?'))
          .flush({
            items: [{ ...delivery, state: rejected ? 'Failed' : 'Queued' }],
            totalCount: 1,
            page: 1,
            pageSize: 20,
          }),
      );
      await settle(fixture);
      await vi.waitFor(() => {
        fixture.detectChanges();
        expect(fixture.nativeElement.textContent).toContain(
          rejected ? 'Ссылка больше не действует' : 'Повторная отправка поставлена в очередь',
        );
      });
      if (!rejected) expect(fixture.nativeElement.querySelector('tbody button')).toBeNull();
    },
  );

  it('shows denied Email inspection without exposing cached delivery data', async () => {
    const fixture = TestBed.createComponent(EmailDeliveries);
    fixture.detectChanges();
    cityInfo();
    http
      .expectOne((request) => request.urlWithParams.startsWith('/api/admin/email-deliveries?'))
      .flush({}, { status: 403, statusText: 'Forbidden' });
    await settle(fixture);
    expect(fixture.nativeElement.textContent).toContain('Доступ разрешён только администратору');
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
  });
});
