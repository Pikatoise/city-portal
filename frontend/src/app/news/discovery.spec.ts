import { signal, RESPONSE_INIT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BehaviorSubject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Discovery } from './discovery';
import { PublicNewsApi, publicNewsRequests } from './public-news-api';
import { SiteMetadata } from '../site-metadata';
import { CityClock } from '../staff/city-clock';
import { PublicArticleSummaryResponse } from '../api/models/public-article-summary-response';
import { FeaturedNews } from './featured-news';

const story = (id: string): PublicArticleSummaryResponse => ({
  id,
  title: 'Public ' + id,
  authorDisplayName: 'Current author',
  summary: 'Summary',
  canonicalUrl: '/news/' + id + '/public',
  categoryId: 'category',
  tagIds: [],
  cover: null,
  firstPublishedAtUtc: '2026-11-16T12:00:00Z',
});

describe('Public discovery', () => {
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  let route: {
    snapshot: {
      data: Record<string, boolean>;
      queryParamMap: ReturnType<typeof convertToParamMap>;
      queryParams: Record<string, string>;
    };
    queryParamMap: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  };
  const api = { taxonomy: vi.fn(), list: vi.fn(), homepage: vi.fn(), weather: vi.fn() };
  const response: ResponseInit = {};
  beforeEach(() => {
    vi.resetAllMocks();
    params = new BehaviorSubject(
      convertToParamMap({
        query: 'музей',
        categoryId: 'category',
        tagId: 'tag',
        from: '2026-11-01',
        to: '2026-11-16',
        page: '2',
      }),
    );
    route = {
      snapshot: { data: { list: true }, queryParamMap: params.value, queryParams: { page: '2' } },
      queryParamMap: params,
    };
    api.taxonomy.mockResolvedValue([]);
    api.list.mockResolvedValue({ items: [story('one')], total: 41, page: 2, pageSize: 20 });
    api.homepage.mockResolvedValue({ featured: [], latest: [], categories: [] });
    api.weather.mockResolvedValue(null);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: ActivatedRoute, useValue: route },
        { provide: PublicNewsApi, useValue: api },
        { provide: SiteMetadata, useValue: { name: signal('Portal') } },
        { provide: CityClock, useValue: { format: (s: string) => s } },
        { provide: RESPONSE_INIT, useValue: response },
      ],
    });
  });
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });
  it('uses all URL filters, twenty-item pages and numbered links', async () => {
    const fixture = TestBed.createComponent(Discovery);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(api.list).toHaveBeenCalledWith({
      query: 'музей',
      categoryId: 'category',
      tagId: 'tag',
      from: '2026-11-01',
      to: '2026-11-16',
      page: 2,
      pageSize: 20,
    });
    expect(fixture.nativeElement.querySelectorAll('.pagination a').length).toBe(3);
    expect(fixture.nativeElement.querySelector('[aria-current="page"]').textContent.trim()).toBe(
      '2',
    );
    expect(fixture.nativeElement.textContent).toContain('Public one');
    expect(response.headers).toEqual({
      'Cache-Control': 'no-store',
      'Content-Type': 'text/html; charset=utf-8',
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    fixture.componentInstance.query = 'new';
    await fixture.componentInstance.search();
    expect(navigate).toHaveBeenCalledWith(['/news'], {
      queryParams: {
        query: 'new',
        categoryId: 'category',
        tagId: 'tag',
        from: '2026-11-01',
        to: '2026-11-16',
        page: null,
      },
    });
  });
  it('preserves encoded article addresses in cards and featured links', async () => {
    const canonicalUrl = '/news/article/%D1%82%D0%B5%D1%81%D1%82';
    api.list.mockResolvedValue({
      items: [{ ...story('one'), canonicalUrl }],
      total: 1,
      page: 1,
      pageSize: 20,
    });
    const list = TestBed.createComponent(Discovery);
    list.detectChanges();
    await list.whenStable();
    list.detectChanges();
    for (const link of list.nativeElement.querySelectorAll(
      'app-news-card h3 a, app-news-card a.image',
    ))
      expect(link.getAttribute('href')).toBe(canonicalUrl);
    expect(list.nativeElement.querySelectorAll('app-news-card h3 a')).toHaveLength(1);

    const featured = TestBed.createComponent(FeaturedNews);
    featured.componentRef.setInput('stories', [{ ...story('one'), canonicalUrl }]);
    featured.detectChanges();
    expect(featured.nativeElement.querySelector('h2 a').getAttribute('href')).toBe(canonicalUrl);
  });
  it('clears results on invalid query and offers a retry', async () => {
    const fixture = TestBed.createComponent(Discovery);
    fixture.detectChanges();
    await fixture.whenStable();
    api.list.mockRejectedValueOnce(new HttpErrorResponse({ status: 400 }));
    await fixture.componentInstance.load();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'Проверь запрос',
    );
    expect(fixture.nativeElement.textContent).not.toContain('Public one');
    expect(response.status).toBe(400);
  });
  it('shows the empty result without a pagination bar', async () => {
    api.list.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 20 });
    const fixture = TestBed.createComponent(Discovery);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Новостей по этому запросу нет');
    expect(fixture.nativeElement.querySelector('.pagination')).toBeNull();
  });
  it('ignores a slow older response after a newer query', async () => {
    const fixture = TestBed.createComponent(Discovery);
    fixture.detectChanges();
    await fixture.whenStable();
    let resolve!: (value: unknown) => void;
    api.list.mockImplementationOnce(() => new Promise((done) => (resolve = done)));
    const old = fixture.componentInstance.load();
    api.list.mockResolvedValue({ items: [story('new')], total: 1, page: 1, pageSize: 20 });
    await fixture.componentInstance.load();
    resolve({ items: [story('old')], total: 1, page: 1, pageSize: 20 });
    await old;
    expect(fixture.componentInstance.result()?.items[0].id).toBe('new');
  });
  it('renders five latest items and keeps category block order', async () => {
    route.snapshot.data = {};
    api.homepage.mockResolvedValue({
      featured: [story('featured')],
      latest: Array.from({ length: 5 }, (_, i) => story('latest' + i)),
      categories: [
        { category: { id: 'b', name: 'Second' }, articles: [story('b')] },
        { category: { id: 'a', name: 'First' }, articles: [] },
      ],
    });
    const fixture = TestBed.createComponent(Discovery);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('#news-latest app-news-card').length).toBe(5);
    expect(fixture.nativeElement.querySelectorAll('.recent app-news-card').length).toBe(3);
    expect(
      Array.from(fixture.nativeElement.querySelectorAll('.category-block h2')).map(
        (e: any) => e.textContent,
      ),
    ).toEqual(['Second', 'First']);
    expect(fixture.nativeElement.querySelector('.filters')).toBeNull();
  });
  it('interrupts category scrolling on wheel, touch and keys and cancels it on destruction', async () => {
    route.snapshot.data = {};
    const fixture = TestBed.createComponent(Discovery);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const frames = new Map<number, FrameRequestCallback>();
    let next = 0;
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((callback) => {
      frames.set(++next, callback);
      return next;
    });
    const cancel = vi.spyOn(window, 'cancelAnimationFrame').mockImplementation((id) => {
      frames.delete(id);
    });
    const scroll = vi.spyOn(window, 'scrollTo').mockImplementation(() => {});
    for (const type of ['wheel', 'touchstart', 'keydown']) {
      fixture.componentInstance.scroll('news-latest');
      const [id, callback] = [...frames.entries()][0];
      frames.delete(id);
      callback(performance.now() + 20);
      const calls = scroll.mock.calls.length;
      document.dispatchEvent(new Event(type));
      expect(frames.size).toBe(0);
      expect(scroll.mock.calls.length).toBe(calls);
    }
    fixture.componentInstance.scroll('news-latest');
    expect(frames.size).toBe(1);
    fixture.destroy();
    expect(frames.size).toBe(0);
    const calls = cancel.mock.calls.length;
    document.dispatchEvent(new Event('wheel'));
    expect(cancel.mock.calls.length).toBe(calls);
    vi.restoreAllMocks();
  });
});

describe('Featured controls', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
    vi.useRealTimers();
  });
  it('hides arrows for a single article and exposes one active slide', () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: CityClock, useValue: { format: (s: string) => s } },
      ],
    });
    const fixture = TestBed.createComponent(FeaturedNews);
    fixture.componentRef.setInput('stories', [story('single')]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.arrows')).toBeNull();
    expect(fixture.nativeElement.querySelectorAll('.slide:not([inert])').length).toBe(1);
    fixture.componentRef.setInput('stories', [story('a'), story('b')]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('.arrows button').length).toBe(2);
    vi.useFakeTimers();
    fixture.componentInstance.move(1);
    vi.advanceTimersByTime(380);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.slide:not([inert]) h2').textContent).toContain(
      'Public b',
    );
  });
  it('keeps image failures scoped to their media ID when slide elements are reused', () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: CityClock, useValue: { format: (s: string) => s } },
      ],
    });
    const fixture = TestBed.createComponent(FeaturedNews);
    fixture.componentRef.setInput('stories', [
      { ...story('broken'), cover: { mediaId: 'broken', alt: 'Broken' } },
    ]);
    fixture.detectChanges();
    fixture.componentInstance.failImage('broken');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.slide:not([inert]) img').hidden).toBe(true);
    expect(
      fixture.nativeElement.querySelector('.slide:not([inert]) .unavailable').textContent,
    ).toContain('Изображение недоступно');
    fixture.componentRef.setInput('stories', [
      { ...story('valid'), cover: { mediaId: 'valid', alt: 'Valid' } },
    ]);
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.slide:not([inert]) img').hidden).toBe(false);
    expect(fixture.nativeElement.querySelector('.slide:not([inert]) .unavailable')).toBeNull();
    expect(fixture.nativeElement.querySelector('.byline').textContent).toContain('Current author');
  });
});

describe('Public API isolation', () => {
  afterEach(() => TestBed.resetTestingModule());
  it('omits credentials and disables transfer/browser caching for generated public requests', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([publicNewsRequests])),
        provideHttpClientTesting(),
      ],
    });
    const api = TestBed.inject(PublicNewsApi);
    const pending = api.list({ query: 'city' });
    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne('/api/news/articles?query=city');
    expect(request.request.credentials).toBe('omit');
    expect(request.request.cache).toBe('no-store');
    expect(request.request.transferCache).toBe(false);
    request.flush({ items: [], total: 0, page: 1, pageSize: 20 });
    expect((await pending).items).toEqual([]);
    http.verify();
  });
});
