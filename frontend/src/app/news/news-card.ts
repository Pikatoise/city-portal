import { Component, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { PublicArticleSummaryResponse } from '../api/models/public-article-summary-response';
import { CityClock } from '../staff/city-clock';

@Component({
  selector: 'app-news-card',
  imports: [RouterLink],
  template: ` <article>
    <a class="image" [routerLink]="router.parseUrl(story().canonicalUrl)" [attr.aria-label]="story().title">
      @if (story().cover; as cover) {
        @if (!failed()) {
          <img
            [src]="'/api/media/images/' + cover.mediaId + '/480'"
            [alt]="cover.alt"
            loading="lazy"
            (error)="failed.set(true)"
          />
        } @else {
          <span>Изображение недоступно</span>
        }
      } @else {
        <span>Без изображения</span>
      }
    </a>
    <div class="copy">
      @if (category()) {
        <a class="category" routerLink="/news" [queryParams]="{ categoryId: story().categoryId }">{{
          category()
        }}</a>
      }
      <h3>
        <a [routerLink]="router.parseUrl(story().canonicalUrl)">{{ story().title }}</a>
      </h3>
      <p>{{ story().summary }}</p>
      <div class="byline">
        <span class="category">{{ story().authorDisplayName }} · </span
        ><time [attr.datetime]="story().firstPublishedAtUtc">{{
          clock.format(story().firstPublishedAtUtc)
        }}</time>
      </div>
    </div>
  </article>`,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }
    article {
      display: grid;
      grid-template-columns: minmax(120px, 240px) minmax(0, 1fr);
      gap: 28px;
      padding: 24px 0;
      border-bottom: 1px solid var(--color-divider);
    }
    .image {
      display: flex;
      align-items: center;
      justify-content: center;
      aspect-ratio: 16/9;
      overflow: hidden;
      border-radius: 6px;
      background: var(--color-surface-muted);
      color: var(--color-muted);
      font-size: 12px;
      text-decoration: none;
    }
    img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .copy {
      align-self: center;
      min-width: 0;
    }
    h3 {
      font-size: 23px;
      line-height: 1.35;
      margin: 8px 0;
      font-weight: 600;
    }
    h3 a {
      color: inherit;
      text-decoration: none;
    }
    h3 a:hover {
      text-decoration: underline;
    }
    p,
    h3 {
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
      overflow-wrap: anywhere;
    }
    p {
      font-size: 14px;
      color: var(--color-muted);
      margin: 8px 0;
    }
    .category,
    time {
      font-size: 12px;
      color: var(--color-muted);
    }
    :host(.compact) article {
      grid-template-columns: minmax(0, 1fr) minmax(0, 1.25fr);
      gap: 16px;
      padding: 0 0 16px;
    }
    .byline {
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    :host(.compact) h3 {
      font-size: 19px;
    }
    :host(.compact) p {
      display: none;
    }
    @media (max-width: 600px) {
      article {
        gap: 16px;
        grid-template-columns: 110px minmax(0, 1fr);
      }
      h3 {
        font-size: 18px;
      }
      p {
        font-size: 13px;
      }
    }
  `,
})
export class NewsCard {
  protected readonly router = inject(Router);
  readonly story = input.required<PublicArticleSummaryResponse>();
  readonly category = input('');
  protected readonly clock = inject(CityClock);
  readonly failed = signal(false);
}
