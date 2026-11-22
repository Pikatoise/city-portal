import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PLATFORM_ID } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiWrite } from './api-write';
import { provideApiConfiguration } from './api/api-configuration';
import { logout } from './api/fn/auth/logout';
import { updateMySuggestion } from './api/fn/suggestions/update-my-suggestion';
import { uploadSuggestionImage } from './api/fn/suggestions/upload-suggestion-image';

describe('ApiWrite', () => {
  const root = 'https://cityportal.test';
  const tokenUrl = `${root}/api/auth/csrf`;
  const logoutUrl = `${root}/api/auth/logout`;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideApiConfiguration(root)],
    });
  });

  afterEach(() => http.verify());

  it('queues token requests at the configured root and preserves JSON and multipart writes', async () => {
    const write = TestBed.inject(ApiWrite);
    http = TestBed.inject(HttpTestingController);
    const file = new File(['image'], 'river.png', { type: 'image/png' });
    const body = {
      version: 4,
      details: { title: 'River', description: 'New footbridge', imageIds: [] },
    };

    const upload = write.invoke(uploadSuggestionImage, { body: { file } });
    const update = write.invoke(updateMySuggestion, { id: 'suggestion', body });
    const initialToken = http.expectOne({ method: 'GET', url: tokenUrl });
    http.expectNone(({ method }) => method !== 'GET');
    initialToken.flush({ token: 'upload-token' });
    const multipart = await vi.waitFor(() =>
      http.expectOne({ method: 'POST', url: `${root}/api/account/suggestions/uploads` }),
    );
    expect(multipart.request.headers.get('X-CSRF-TOKEN')).toBe('upload-token');
    expect(multipart.request.headers.has('Content-Type')).toBe(false);
    expect((multipart.request.body as FormData).get('file')).toBe(file);
    const nextToken = http.expectOne({ method: 'GET', url: tokenUrl });
    const updateUrl = `${root}/api/account/suggestions/suggestion`;
    http.expectNone(updateUrl);
    nextToken.flush({ token: 'update-token' });
    const json = await vi.waitFor(() => http.expectOne({ method: 'PUT', url: updateUrl }));
    expect(json.request.headers.get('X-CSRF-TOKEN')).toBe('update-token');
    expect(json.request.body).toEqual(body);
    json.flush({ id: 'suggestion', version: 5 });
    multipart.flush({ id: 'upload', state: 'Processing' }, { status: 202, statusText: 'Accepted' });
    await expect(update).resolves.toEqual({ id: 'suggestion', version: 5 });
    await expect(upload).resolves.toEqual({ id: 'upload', state: 'Processing' });
  });

  it('propagates a token failure, lets a queued caller proceed, and never retries a failed write', async () => {
    const write = TestBed.inject(ApiWrite);
    http = TestBed.inject(HttpTestingController);
    const first = expect(write.invoke(logout, {})).rejects.toMatchObject({ status: 503 });
    const next = write.invoke(logout, {}).catch((error: unknown) => error);
    http.expectOne(tokenUrl).flush(null, { status: 503, statusText: 'Unavailable' });
    await first;
    http.expectNone(logoutUrl);
    http.expectOne(tokenUrl).flush({ token: 'next-token' });
    const mutation = await vi.waitFor(() => http.expectOne({ method: 'POST', url: logoutUrl }));
    expect(mutation.request.headers.get('X-CSRF-TOKEN')).toBe('next-token');
    mutation.flush(
      { code: 'rate_limited' },
      { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '75' } },
    );
    const failure = await next;
    expect(failure).toBeInstanceOf(HttpErrorResponse);
    expect(failure).toMatchObject({ status: 429, error: { code: 'rate_limited' } });
    expect((failure as HttpErrorResponse).headers.get('Retry-After')).toBe('75');
    http.expectNone(tokenUrl);
    http.expectNone(logoutUrl);
  });

  it('rejects server-side writes before requesting a token', async () => {
    TestBed.overrideProvider(PLATFORM_ID, { useValue: 'server' });
    const write = TestBed.inject(ApiWrite);
    http = TestBed.inject(HttpTestingController);
    await expect(write.invoke(logout, {})).rejects.toThrow('API writes require a browser session.');
  });
});
