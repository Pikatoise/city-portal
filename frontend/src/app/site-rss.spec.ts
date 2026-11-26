import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, expect, it } from 'vitest';
import { SiteMetadata } from './site-metadata';

const rssLink = 'link[rel="alternate"][type="application/rss+xml"][href="/rss.xml"]';

afterEach(() => {
  document.head.querySelector(rssLink)?.remove();
  TestBed.resetTestingModule();
});

it('sets one RSS discovery link and removes it when the feed is disabled', async () => {
  TestBed.configureTestingModule({
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });
  const metadata = TestBed.inject(SiteMetadata);
  const http = TestBed.inject(HttpTestingController);
  for (const rssEnabled of [false, true, true, false]) {
    const reload = metadata.load();
    http.expectOne('/api/system/info').flush({
      name: 'CityPortal',
      stage: 1,
      city: 'Demo city',
      region: '',
      timeZone: 'UTC',
      rssEnabled,
    });
    await reload;
    expect(metadata.rssEnabled()).toBe(rssEnabled);
    expect(document.head.querySelectorAll(rssLink)).toHaveLength(rssEnabled ? 1 : 0);
  }
  http.verify();
});
