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
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Meta, Title } from '@angular/platform-browser';
import { firstValueFrom } from 'rxjs';
import { Api } from '../api/api';
import { PublicArticleResponse } from '../api/models/public-article-response';
import { CityClock } from '../staff/city-clock';
import { SiteMetadata } from '../site-metadata';

@Component({
  selector: 'app-public-article',
  imports: [RouterLink],
  templateUrl: './public-article.html',
  styleUrl: './public-article.css',
})
export class PublicArticle implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly api = inject(Api);
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
      void this.load(params.get('id') ?? '', params.get('slug') ?? '').finally(complete);
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
  async load(id: string, slug: string): Promise<void> {
    const generation = ++this.generation;
    this.article.set(null);
    this.state.set('loading');
    this.clearMetadata();
    if (this.response) this.response.headers = { 'Cache-Control': 'no-store' };
    try {
      const article = await firstValueFrom(
        this.http.get<PublicArticleResponse>(
          `${this.api.rootUrl}/api/news/articles/${encodeURIComponent(id)}`,
          {
            params: { slug },
            transferCache: false,
            credentials: 'omit',
            cache: 'no-store',
          },
        ),
      );
      if (generation !== this.generation) return;
      if (article.requestedSlugIsAlias) {
        if (this.response) {
          this.response.status = 307;
          this.response.headers = { 'Cache-Control': 'no-store', Location: article.canonicalUrl };
        } else await this.router.navigateByUrl(article.canonicalUrl, { replaceUrl: true });
        return;
      }
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
