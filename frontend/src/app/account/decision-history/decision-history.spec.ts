import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { expect, it } from 'vitest';
import { DecisionHistory } from './decision-history';

it('retries the requested page and resets staff history when the account route changes', async () => {
  const params = new BehaviorSubject(convertToParamMap({ id: 'first-account' }));
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      provideRouter([]),
      {
        provide: ActivatedRoute,
        useValue: { paramMap: params.asObservable(), snapshot: { data: { staff: true } } },
      },
    ],
  });
  const http = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(DecisionHistory);
  fixture.detectChanges();
  TestBed.tick();
  http.expectOne('/api/system/info').flush({ timeZone: 'UTC' });
  const path = '/api/staff/account-blocks';
  const decision = {
    id: 'decision',
    blocked: true,
    decidedAtUtc: '2026-11-25T09:00:00Z',
    reason: 'First account reason',
    actorId: 'staff',
    actorName: 'Moderator',
  };
  http
    .expectOne(`${path}/first-account/history?page=1`)
    .flush({ items: [decision], hasNextPage: true });
  await fixture.whenStable();
  fixture.detectChanges();
  const element: HTMLElement = fixture.nativeElement;
  expect(element.textContent).toContain('First account reason');
  element.querySelectorAll('nav button')[1].dispatchEvent(new MouseEvent('click'));
  TestBed.tick();
  const secondPage = `${path}/first-account/history?page=2`;
  http.expectOne(secondPage).flush({}, { status: 503, statusText: 'Unavailable' });
  await fixture.whenStable();
  fixture.detectChanges();
  expect(element.querySelector('[role=alert]')).not.toBeNull();
  element.querySelector('button')?.click();
  TestBed.tick();
  const pending = http.expectOne(secondPage);

  params.next(convertToParamMap({ id: 'second-account' }));
  TestBed.tick();
  http.expectOne(`${path}/second-account/history?page=1`).flush({
    items: [{ ...decision, id: 'other-decision', reason: 'Second account reason' }],
    hasNextPage: false,
  });
  pending.flush({ items: [{ ...decision, reason: 'Stale response' }], hasNextPage: false });
  await fixture.whenStable();
  fixture.detectChanges();
  expect(element.textContent).toContain('Second account reason');
  expect(element.textContent).not.toContain('First account reason');
  expect(element.textContent).not.toContain('Stale response');
  expect(element.textContent).toContain('Moderator');
  http.verify();
});
