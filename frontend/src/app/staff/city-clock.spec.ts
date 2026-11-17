import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, expect, it } from 'vitest';
import { Api } from '../api/api';
import { CityClock } from './city-clock';

afterEach(() => TestBed.resetTestingModule());

it('loads the configured city time zone from the absolute server API root', async () => {
  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: Api, useValue: { rootUrl: 'http://internal-api:5083' } },
    ],
  });
  const clock = TestBed.inject(CityClock);
  const http = TestBed.inject(HttpTestingController);
  http
    .expectOne('http://internal-api:5083/api/system/info')
    .flush({ timeZone: 'Asia/Yekaterinburg' });
  await Promise.resolve();
  expect(clock.timeZone()).toBe('Asia/Yekaterinburg');
  expect(clock.format('2026-11-16T10:00:00Z')).toContain('15:00');
  http.verify();
});
