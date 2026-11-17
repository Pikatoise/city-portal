import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MediaApi } from './media-api';
import { MediaLibrary } from './media-library';

describe('Media contracts', () => {
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('sends the selected file with a fresh CSRF token as multipart data', async () => {
    const file = new File(['image bytes'], 'river.png', { type: 'image/png' });
    const promise = TestBed.inject(MediaApi).upload(file);
    http.expectOne('/api/auth/csrf').flush({ token: 'fresh-media-token' });
    await vi.waitFor(() => {
      const request = http.expectOne('/api/media/images');
      expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('fresh-media-token');
      expect(request.request.headers.has('Content-Type')).toBe(false);
      const payload = request.request.body as FormData;
      expect((payload.get('file') as File).name).toBe('river.png');
      request.flush(
        { id: 'image-1', state: 'Processing' },
        { status: 202, statusText: 'Accepted' },
      );
    });
    expect((await promise).state).toBe('Processing');
  });

  it('sends the removal request version and never converts resolution into rejection', async () => {
    const promise = TestBed.inject(MediaApi).resolve({
      id: 'request-1',
      templateId: 'template-1',
      ownerId: 'owner-1',
      createdAtUtc: '2026-11-15T00:00:00Z',
      resolvedAtUtc: null,
      version: 7,
    });
    http.expectOne('/api/auth/csrf').flush({ token: 'resolution-token' });
    await vi.waitFor(() => {
      const request = http.expectOne('/api/media/template-removal-requests/request-1/resolve');
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ version: 7 });
      expect(request.request.headers.get('X-CSRF-TOKEN')).toBe('resolution-token');
      request.flush(null, { status: 204, statusText: 'No Content' });
    });
    await promise;
  });

  it('does not request private libraries for an unconfirmed author', async () => {
    const fixture = TestBed.createComponent(MediaLibrary);
    fixture.detectChanges();
    http.expectOne('/api/system/info').flush({ name: 'CityPortal', timeZone: 'UTC' });
    http.expectOne('/api/account').flush({
      id: 'author',
      displayName: 'Author',
      email: 'author@example.test',
      emailConfirmed: false,
      roles: ['Author'],
      version: 1,
      confirmationDelivery: { state: 'Sent', resendAfterSeconds: 0 },
    });
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectNone((request) => request.url.startsWith('/api/media/'));
    expect(fixture.nativeElement.querySelector('input[type=file]')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('подтверждённым Email');
  });
});
