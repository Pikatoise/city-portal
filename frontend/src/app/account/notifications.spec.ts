import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Notifications } from './notifications';

describe('Account notifications', () => {
  let http: HttpTestingController;
  const preference = { type: 'template.approved', account: true, email: true };
  const notification = {
    id: 'notification-1',
    type: 'template.approved',
    message: 'Шаблон изображения одобрен.',
    resourceId: 'template-1',
    createdAtUtc: '2026-11-15T10:00:00Z',
    read: false,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  async function open(items = [notification]) {
    const fixture = TestBed.createComponent(Notifications);
    fixture.detectChanges();
    http.expectOne('/api/system/info').flush({ timeZone: 'Asia/Yekaterinburg' });
    http
      .expectOne((request) => request.url === '/api/account/notifications')
      .flush({ items, total: items.length, page: 1, pageSize: 20 });
    http.expectOne('/api/account/notification-preferences').flush([preference]);
    await expect.poll(() => fixture.componentInstance.loaded()).toBe(true);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('renders the account notice and preserves independent channel switches with a fresh CSRF token', async () => {
    const fixture = await open();
    expect(fixture.nativeElement.textContent).toContain('Шаблон изображения одобрен.');
    expect(fixture.nativeElement.textContent).toContain('Новое');
    const switches = fixture.nativeElement.querySelectorAll('input[type=checkbox]');
    switches[0].click();
    fixture.detectChanges();
    const save = fixture.nativeElement.querySelector('.preferences button');
    expect(save.disabled).toBe(false);
    save.click();
    http.expectOne('/api/auth/csrf').flush({ token: 'preference-token' });
    await Promise.resolve();
    const request = http.expectOne('/api/account/notification-preferences');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual([
      { type: 'template.approved', account: false, email: true },
    ]);
    expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('preference-token');
    request.flush([{ type: 'template.approved', account: false, email: true }]);
    await expect.poll(() => fixture.componentInstance.busy()).toBe(false);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(save.disabled).toBe(true);
    expect(fixture.nativeElement.textContent).toContain('Настройки уведомлений сохранены.');
    expect(switches[1].checked).toBe(true);
  });

  it('keeps unsaved switches and exposes retry feedback after a failed save', async () => {
    const fixture = await open([]);
    fixture.nativeElement.querySelector('input[type=checkbox]').click();
    fixture.detectChanges();
    fixture.nativeElement.querySelector('.preferences button').click();
    http.expectOne('/api/auth/csrf').flush({ token: 'retry-token' });
    await Promise.resolve();
    http
      .expectOne('/api/account/notification-preferences')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await expect.poll(() => fixture.componentInstance.busy()).toBe(false);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role=alert]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('input[type=checkbox]').checked).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('Есть несохранённые изменения.');
    expect(fixture.nativeElement.querySelector('.preferences button').disabled).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('Уведомлений пока нет.');
  });

  it('marks read only after the server confirms the action and deletes through the own endpoint', async () => {
    const fixture = await open();
    fixture.nativeElement.querySelector('.notification-actions button').click();
    http.expectOne('/api/auth/csrf').flush({ token: 'read-token' });
    await Promise.resolve();
    const read = http.expectOne('/api/account/notifications/notification-1/read');
    expect(read.request.method).toBe('POST');
    read.flush(null, { status: 204, statusText: 'No Content' });
    await expect.poll(() => fixture.componentInstance.busy()).toBe(false);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.notification-state').textContent).toContain(
      'Прочитано',
    );
    fixture.nativeElement.querySelector('.notification-actions button').click();
    http.expectOne('/api/auth/csrf').flush({ token: 'delete-token' });
    await Promise.resolve();
    const remove = http.expectOne('/api/account/notifications/notification-1');
    expect(remove.request.method).toBe('DELETE');
    expect(remove.request.headers.get('X-CSRF-TOKEN')).toBe('delete-token');
    remove.flush(null, { status: 204, statusText: 'No Content' });
    await Promise.resolve();
    await Promise.resolve();
    await vi.waitFor(() =>
      http
        .expectOne((request) => request.url === '/api/account/notifications')
        .flush({ items: [], total: 0, page: 1, pageSize: 20 }),
    );
    await expect.poll(() => fixture.componentInstance.busy()).toBe(false);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Уведомлений пока нет.');
  });

  it('preserves unread state when the server rejects a read action', async () => {
    const fixture = await open();
    fixture.nativeElement.querySelector('.notification-actions button').click();
    http.expectOne('/api/auth/csrf').flush({ token: 'failure-token' });
    await Promise.resolve();
    http
      .expectOne('/api/account/notifications/notification-1/read')
      .flush({}, { status: 503, statusText: 'Unavailable' });
    await expect.poll(() => fixture.componentInstance.busy()).toBe(false);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.notification-state').textContent).toContain(
      'Новое',
    );
    expect(fixture.nativeElement.querySelector('[role=alert]')).not.toBeNull();
  });

  it('offers login and hides account data when the session is unavailable', async () => {
    const fixture = TestBed.createComponent(Notifications);
    fixture.detectChanges();
    http.expectOne('/api/system/info').flush({ timeZone: 'UTC' });
    http
      .expectOne((request) => request.url === '/api/account/notifications')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    http
      .expectOne('/api/account/notification-preferences')
      .flush({}, { status: 401, statusText: 'Unauthorized' });
    await expect.poll(() => fixture.componentInstance.signedOut()).toBe(true);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('a[href="/account/login"]')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('.notification-list')).toBeNull();
  });
});
