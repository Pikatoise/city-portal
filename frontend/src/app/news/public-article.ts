import {
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  PendingTasks,
  RESPONSE_INIT,
  inject,
  signal,
} from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Meta, Title } from '@angular/platform-browser';
import { PublicArticleResponse } from '../api/models/public-article-response';
import { CityClock } from '../staff/city-clock';
import { SiteMetadata } from '../site-metadata';
import { PublicNewsApi } from './public-news-api';
import { NewsCard } from './news-card';
import { PublicArticleSummaryResponse } from '../api/models/public-article-summary-response';

import { ReportForm } from '../reports/report-form';
import { ArticleComments } from './article-comments';

@Component({
  selector: 'app-public-article',
  imports: [RouterLink, NewsCard, ReportForm, ArticleComments],
  templateUrl: './public-article.html',
  styleUrl: './public-article.css',
})
export class PublicArticle implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly news = inject(PublicNewsApi);
  readonly related = signal<PublicArticleSummaryResponse[]>([]);
  readonly relatedFailed = signal(false);
  private readonly response = inject(RESPONSE_INIT, { optional: true });
  private readonly pending = inject(PendingTasks);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly site = inject(SiteMetadata);
  private readonly document = inject(DOCUMENT);
  private readonly destroy = inject(DestroyRef);
  protected readonly clock = inject(CityClock);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private generation = 0;
  readonly article = signal<PublicArticleResponse | null>(null);
  readonly state = signal<'loading' | 'withdrawn' | 'missing' | 'failed' | 'published'>('loading');
  private readonly metadataSelectors = [
    'name="description"',
    'property="og:title"',
    'property="og:description"',
    'property="og:image"',
    'property="og:url"',
  ];
  ngOnInit(): void {
    this.host.nativeElement.addEventListener('error', this.unavailable, true);
    this.destroy.onDestroy(() =>
      this.host.nativeElement.removeEventListener('error', this.unavailable, true),
    );
    const subscription = this.route.paramMap.subscribe((params) => {
      const complete = this.pending.add();
      void this.load(params.get('address') ?? '').finally(complete);
    });
    this.destroy.onDestroy(() => {
      subscription.unsubscribe();
      this.generation++;
      this.clearMetadata();
    });
  }
  private clearMetadata(): void {
    this.title.setTitle(this.site.name());
    for (const selector of this.metadataSelectors) this.meta.removeTag(selector);
    this.document.querySelector('link[rel="canonical"]')?.remove();
  }
  async load(address: string): Promise<void> {
    const generation = ++this.generation;
    this.article.set(null);
    this.related.set([]);
    this.relatedFailed.set(false);
    this.state.set('loading');
    this.clearMetadata();
    if (this.response)
      this.response.headers = {
        'Cache-Control': 'no-store',
        'Content-Type': 'text/html; charset=utf-8',
      };
    let article: PublicArticleResponse;
    try {
      article = await this.news.article(address);
      if (generation !== this.generation) return;
      this.article.set(article);
      this.state.set('published');
      if (this.response) this.response.status = 200;
      const title = article.content.seoTitle || article.content.title || this.site.name();
      const description = article.content.seoDescription || article.content.summary || '';
      this.title.setTitle(title);
      this.meta.updateTag({ name: 'description', content: description });
      this.meta.updateTag({ property: 'og:title', content: title });
      this.meta.updateTag({ property: 'og:description', content: description });
      this.meta.updateTag({ property: 'og:url', content: article.canonicalUrl });
      if (article.content.cover)
        this.meta.updateTag({
          property: 'og:image',
          content: `/api/media/images/${article.content.cover.mediaId}/1280`,
        });
      const canonical = this.document.createElement('link');
      canonical.rel = 'canonical';
      canonical.href = article.canonicalUrl;
      this.document.head.appendChild(canonical);
    } catch (error) {
      if (generation !== this.generation) return;
      this.article.set(null);
      this.clearMetadata();
      const missing = error instanceof HttpErrorResponse && error.status === 404;
      this.state.set(
        missing ? (error.error?.code === 'article_withdrawn' ? 'withdrawn' : 'missing') : 'failed',
      );
      if (this.response) this.response.status = missing ? 404 : 503;
      return;
    }
    try {
      const related = await this.news.related(article.id);
      if (generation === this.generation) this.related.set(related);
    } catch {
      if (generation === this.generation) this.relatedFailed.set(true);
    }
  }
  unavailable = (event: Event): void => {
    const image = event.target;
    if (image instanceof HTMLImageElement && !image.hidden) {
      image.hidden = true;
      const placeholder = this.document.createElement('span');
      placeholder.className = 'image-placeholder';
      placeholder.textContent = 'Изображение недоступно';
      image.after(placeholder);
    }
  };
}
