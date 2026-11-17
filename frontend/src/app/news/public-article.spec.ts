import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { RESPONSE_INIT, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { Meta, Title } from '@angular/platform-browser';
import { EMPTY } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Api } from '../api/api';
import { PublicArticleResponse } from '../api/models/public-article-response';
import { SiteMetadata } from '../site-metadata';
import { CityClock } from '../staff/city-clock';
import { PublicArticle } from './public-article';
import { PublicNewsApi } from './public-news-api';

const publication: PublicArticleResponse = {
  id: 'article-id',
  canonicalUrl: '/news/published-title-15-11-2026-abcdef',
  authorDisplayName: 'Current author',
  firstPublishedAtUtc: '2026-11-15T10:00:00Z',
  updatedAtUtc: '2026-11-16T10:00:00Z',
  content: { title: 'Published title', summary: 'Public summary', seoTitle: 'Public SEO' },
  previewHtml: '<p>Public body</p>',
};

describe('Public article visibility', () => {
  let response: ResponseInit;
  beforeEach(() => {
    response = {};
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Api, useValue: { rootUrl: '' } },
        { provide: PublicNewsApi, useValue: { related: async () => [], taxonomy: async () => [] } },
        { provide: ActivatedRoute, useValue: { paramMap: EMPTY } },
        { provide: RESPONSE_INIT, useValue: response },
        { provide: SiteMetadata, useValue: { name: signal('Portal') } },
        { provide: CityClock, useValue: { format: (value: string) => `Local ${value}` } },
      ],
    });
  });
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
    vi.restoreAllMocks();
  });

  it('renders only the publication and clears its metadata when it becomes withdrawn', async () => {
    // The server DOM supports appendChild but not the convenience append API.
    vi.spyOn(document.head, 'append').mockImplementation(() => {
      throw new Error('Server DOM does not implement append');
    });
    const fixture = TestBed.createComponent(PublicArticle);
    fixture.detectChanges();
    const component = fixture.componentInstance;
    let loading = component.load('published-title-15-11-2026-abcdef');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url.includes('/api/news/articles/'))
      .flush(publication);
    await loading;
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Published title');
    expect(fixture.nativeElement.textContent).toContain('Local 2026-11-15');
    expect(TestBed.inject(Title).getTitle()).toBe('Public SEO');
    expect(document.querySelector('link[rel=canonical]')?.getAttribute('href')).toBe(
      publication.canonicalUrl,
    );
    loading = component.load('published-title-15-11-2026-abcdef');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url.includes('/api/news/articles/'))
      .flush({ code: 'article_withdrawn' }, { status: 404, statusText: 'Not Found' });
    await loading;
    fixture.detectChanges();
    expect(response.status).toBe(404);
    expect(response.headers).toEqual({ 'Cache-Control': 'no-store', 'Content-Type': 'text/html; charset=utf-8' });
    expect(fixture.nativeElement.textContent).toContain('Материал снят с публикации');
    expect(fixture.nativeElement.textContent).not.toContain('Published title');
    expect(fixture.nativeElement.textContent).not.toContain('Current author');
    expect(TestBed.inject(Title).getTitle()).toBe('Portal');
    expect(TestBed.inject(Meta).getTag('name="description"')).toBeNull();
    expect(document.querySelector('link[rel=canonical]')).toBeNull();
  });

  it('clears publication data and metadata if server rendering fails after the public response', async () => {
    const fixture = TestBed.createComponent(PublicArticle);
    vi.spyOn(document.head, 'appendChild').mockImplementationOnce(() => {
      throw new Error('Server DOM write failed');
    });
    const loading = fixture.componentInstance.load('published-title-15-11-2026-abcdef');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url.includes('/api/news/articles/'))
      .flush(publication);
    await loading;
    expect(response.status).toBe(503);
    expect(fixture.componentInstance.article()).toBeNull();
    expect(fixture.componentInstance.state()).toBe('failed');
    expect(TestBed.inject(Title).getTitle()).toBe('Portal');
    expect(TestBed.inject(Meta).getTag('property="og:title"')).toBeNull();
    expect(TestBed.inject(Meta).getTag('name="description"')).toBeNull();
    expect(document.querySelector('link[rel=canonical]')).toBeNull();
  });

  it('distinguishes unknown material from withdrawal and displays blocked body-image placeholders', async () => {
    const fixture = TestBed.createComponent(PublicArticle);
    fixture.detectChanges();
    const loading = fixture.componentInstance.load('unknown');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url.includes('/api/news/articles/'))
      .flush({ code: 'not_found' }, { status: 404, statusText: 'Not Found' });
    await loading;
    fixture.detectChanges();
    expect(response.status).toBe(404);
    expect(fixture.nativeElement.textContent).toContain('Материал не найден');
    fixture.componentInstance.article.set({
      ...publication,
      previewHtml: '<img src="/api/media/images/blocked/480" alt="Blocked">',
    });
    fixture.componentInstance.state.set('published');
    fixture.detectChanges();
    const image = fixture.nativeElement.querySelector('.body-copy img') as HTMLImageElement;
    image.dispatchEvent(new Event('error', { bubbles: false }));
    expect(image.hidden).toBe(true);
    expect(image.nextElementSibling?.textContent).toBe('Изображение недоступно');
  });
});
