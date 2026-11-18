import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { expect, it, vi } from 'vitest';
import { StaffSuggestionResponse } from '../api/models/staff-suggestion-response';
import { CityClock } from './city-clock';
import { SuggestionQueue } from './suggestion-queue';

it('clears remaining assigned contacts when refresh after a return is forbidden', async () => {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      { provide: CityClock, useValue: { format: (value: string) => value } },
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(SuggestionQueue);
  const panel = fixture.nativeElement as HTMLElement;
  const path = '/api/staff/suggestions';
  const kept: StaffSuggestionResponse = {
    id: 'kept',
    title: 'Remaining assignment',
    description: 'Private description',
    eventDate: null,
    location: null,
    contact: 'private@example.test',
    assigned: true,
    articleId: 'article',
    canReturn: true,
    published: false,
    expiresAtUtc: '2026-11-21T18:00:00Z',
    version: 2,
  };
  const returned = { ...kept, id: 'returned', title: 'First assignment', contact: null };
  fixture.detectChanges();
  http
    .expectOne(
      (request) => request.url === path + '/queue' && request.params.get('mine') === 'false',
    )
    .flush({ items: [], hasMore: false });
  const assignments = [...panel.querySelectorAll('button')].find(
    (button) => button.textContent?.trim() === 'Мои назначения',
  )!;
  await vi.waitFor(() => {
    fixture.detectChanges();
    expect(assignments.disabled).toBe(false);
  });
  assignments.click();
  http
    .expectOne(
      (request) => request.url === path + '/queue' && request.params.get('mine') === 'true',
    )
    .flush({ items: [returned, kept], hasMore: false });
  await vi.waitFor(() => {
    fixture.detectChanges();
    expect(panel.textContent).toContain('private@example.test');
  });

  panel.querySelector('article button')!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
  http.expectOne('/api/auth/csrf').flush({ token: 'csrf' });
  await vi.waitFor(() =>
    http
      .expectOne(path + '/returned/return')
      .flush(null, { status: 204, statusText: 'No Content' }),
  );
  await vi.waitFor(() =>
    http
      .expectOne((request) => request.url === path + '/queue')
      .flush(null, { status: 403, statusText: 'Forbidden' }),
  );
  await vi.waitFor(() => {
    fixture.detectChanges();
    expect(panel.textContent).toContain('нет доступа');
  });
  expect(panel.textContent).not.toContain('private@example.test');
  expect(panel.querySelector('article')).toBeNull();
  http.verify();
});
