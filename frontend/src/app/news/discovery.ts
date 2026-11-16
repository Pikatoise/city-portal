import { DOCUMENT } from '@angular/common';
import {
  Component,
  afterNextRender,
  DestroyRef,
  PendingTasks,
  RESPONSE_INIT,
  computed,
  inject,
  signal,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Meta, Title } from '@angular/platform-browser';
import { PublicNewsApi } from './public-news-api';
import { NewsCard } from './news-card';
import { FeaturedNews } from './featured-news';
import { PublicHomepageResponse } from '../api/models/public-homepage-response';
import { PublicTaxonomyResponse } from '../api/models/public-taxonomy-response';
import { NewsPageOfPublicArticleSummaryResponse } from '../api/models/news-page-of-public-article-summary-response';
import { SiteMetadata } from '../site-metadata';

@Component({
  selector: 'app-discovery',
  imports: [FormsModule, RouterLink, NewsCard, FeaturedNews],
  templateUrl: './discovery.html',
  styleUrl: './discovery.css',
})
export class Discovery {
  private readonly api = inject(PublicNewsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly pending = inject(PendingTasks);
  private readonly response = inject(RESPONSE_INIT, { optional: true });
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly site = inject(SiteMetadata);
  private readonly document = inject(DOCUMENT);
  readonly isList = this.route.snapshot.data['list'] === true;
  readonly home = signal<PublicHomepageResponse | null>(null);
  readonly result = signal<NewsPageOfPublicArticleSummaryResponse | null>(null);
  readonly categories = signal<PublicTaxonomyResponse[]>([]);
  readonly tags = signal<PublicTaxonomyResponse[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly page = signal(1);
  readonly pages = computed(() => {
    const count = Math.ceil(Number(this.result()?.total ?? 0) / 20);
    const page = this.page();
    return Array.from(new Set([1, ...Array.from({ length: 5 }, (_, i) => page + i - 2), count]))
      .filter((n) => n >= 1 && n <= count)
      .sort((a, b) => a - b);
  });
  query = '';
  categoryId = '';
  tagId = '';
  from = '';
  to = '';
  private generation = 0;
  private scrollFrame = 0;
  constructor() {
    const destroy = inject(DestroyRef);
    afterNextRender(() => {
      const interrupt = () => cancelAnimationFrame(this.scrollFrame);
      const events = ['wheel', 'touchstart', 'keydown'] as const;
      for (const event of events)
        this.document.addEventListener(event, interrupt, { passive: true });
      destroy.onDestroy(() => {
        for (const event of events) this.document.removeEventListener(event, interrupt);
      });
    });
    const subscription = this.route.queryParamMap.subscribe(
      () => void this.pending.run(() => this.load()),
    );
    inject(DestroyRef).onDestroy(() => {
      subscription.unsubscribe();
      this.generation++;
      this.meta.removeTag('name="description"');
      this.document.querySelector('link[rel="canonical"]')?.remove();
      if (this.scrollFrame) cancelAnimationFrame(this.scrollFrame);
    });
  }
  async load(): Promise<void> {
    const generation = ++this.generation;
    this.loading.set(true);
    this.error.set('');
    this.home.set(null);
    this.result.set(null);
    if (this.response) {
      this.response.status = 200;
      this.response.headers = { 'Cache-Control': 'no-store' };
    }
    const params = this.route.snapshot.queryParamMap;
    this.query = params.get('query') ?? '';
    this.categoryId = params.get('categoryId') ?? '';
    this.tagId = params.get('tagId') ?? '';
    this.from = params.get('from') ?? '';
    this.to = params.get('to') ?? '';
    const page = Number(params.get('page') ?? 1);
    this.page.set(page);
    this.title.setTitle(this.isList ? 'Новости · ' + this.site.name() : this.site.name());
    this.meta.updateTag({
      name: 'description',
      content: this.isList
        ? 'Новости города: поиск по публикациям, категориям, тегам и датам.'
        : 'Последние новости города и выбор редакции.',
    });
    this.document.querySelector('link[rel="canonical"]')?.remove();
    try {
      const [categories, tags, data] = await Promise.all([
        this.api.taxonomy('categories'),
        this.isList ? this.api.taxonomy('tags') : Promise.resolve([]),
        this.isList
          ? this.api.list({
              query: this.query,
              categoryId: this.categoryId || undefined,
              tagId: this.tagId || undefined,
              from: this.from || undefined,
              to: this.to || undefined,
              page,
              pageSize: 20,
            })
          : this.api.homepage(),
      ]);
      if (generation !== this.generation) return;
      this.categories.set(categories);
      this.tags.set(tags);
      if (this.isList) this.result.set(data as NewsPageOfPublicArticleSummaryResponse);
      else this.home.set(data as PublicHomepageResponse);
      const link = this.document.createElement('link');
      link.rel = 'canonical';
      link.href = this.isList ? this.router.url : '/';
      this.document.head.appendChild(link);
    } catch (error) {
      if (generation !== this.generation) return;
      const invalid = error instanceof HttpErrorResponse && error.status === 400;
      this.error.set(
        invalid
          ? 'Проверь запрос, фильтры и номер страницы.'
          : 'Не удалось загрузить новости. Повтори запрос.',
      );
      if (this.response) this.response.status = invalid ? 400 : 503;
    } finally {
      if (generation === this.generation) this.loading.set(false);
    }
  }
  async search(page = 1): Promise<void> {
    await this.router.navigate(['/news'], {
      queryParams: {
        query: this.query || null,
        categoryId: this.categoryId || null,
        tagId: this.tagId || null,
        from: this.from || null,
        to: this.to || null,
        page: page === 1 ? null : page,
      },
    });
  }
  pageParams(page: number) {
    return { ...this.route.snapshot.queryParams, page };
  }
  categoryName(id: string | null): string {
    return this.categories().find((item) => item.id === id)?.name ?? '';
  }
  reset(): void {
    this.query = '';
    this.categoryId = '';
    this.tagId = '';
    this.from = '';
    this.to = '';
    void this.search();
  }
  scroll(id: string): void {
    const element = this.document.getElementById(id);
    if (!element) return;
    cancelAnimationFrame(this.scrollFrame);
    const start = window.scrollY;
    const target = element.getBoundingClientRect().top + start - 100;
    const began = performance.now();
    const step = (now: number) => {
      const progress = Math.min(1, (now - began) / 650);
      window.scrollTo(0, start + (target - start) * (1 - Math.pow(1 - progress, 3)));
      if (progress < 1) this.scrollFrame = requestAnimationFrame(step);
    };
    this.scrollFrame = requestAnimationFrame(step);
  }
}
