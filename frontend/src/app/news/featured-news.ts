import { Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { PublicArticleSummaryResponse } from '../api/models/public-article-summary-response';
import { CityClock } from '../staff/city-clock';

@Component({
  selector: 'app-featured-news',
  imports: [RouterLink],
  template: ` <article
    class="lead"
    [class.draggable]="stories().length > 1"
    (pointerdown)="begin($event)"
    (pointermove)="drag($event)"
    (pointerup)="end($event)"
    (pointercancel)="cancel()"
    (lostpointercapture)="cancel()"
    (dragstart)="$event.preventDefault()"
  >
    <div class="viewport">
      <div
        class="track"
        [class.settling]="settling()"
        [style.transform]="'translateX(calc(-100% + ' + offset() + 'px))'"
      >
        @for (story of slides(); track $index) {
          <div
            class="slide"
            [attr.aria-hidden]="$index !== 1"
            [attr.inert]="$index !== 1 ? '' : null"
          >
            @if (story.cover; as cover) {
              <img
                [src]="'/api/media/images/' + cover.mediaId + '/1280'"
                [alt]="cover.alt"
                [hidden]="failedImages().has(cover.mediaId)"
                (error)="failImage(cover.mediaId)"
              />
              @if (failedImages().has(cover.mediaId)) {
                <span class="unavailable">Изображение недоступно</span>
              }
            }
            <div class="copy">
              <p>Выбор редакции</p>
              <h2>
                <a [routerLink]="router.parseUrl(story.canonicalUrl)" (click)="open($event)">{{ story.title }}</a>
              </h2>
              <p class="summary">{{ story.summary }}</p>
              <span class="byline"
                >{{ story.authorDisplayName }} ·
                <time [attr.datetime]="story.firstPublishedAtUtc">{{
                  clock.format(story.firstPublishedAtUtc)
                }}</time></span
              >
            </div>
          </div>
        }
      </div>
    </div>
    @if (stories().length > 1) {
      <nav aria-label="Выбор редакции" class="arrows">
        <button type="button" (click)="move(-1, $event)" aria-label="Предыдущая избранная новость">
          ←</button
        ><span aria-live="polite">{{ index() + 1 }} / {{ stories().length }}</span
        ><button type="button" (click)="move(1, $event)" aria-label="Следующая избранная новость">
          →
        </button>
      </nav>
    }
  </article>`,
  styles: `
    :host {
      display: block;
      min-width: 0;
      height: 100%;
    }
    .lead {
      height: 100%;
      min-height: 430px;
      position: relative;
      border-radius: 6px;
      overflow: hidden;
      background: var(--color-action);
    }
    .draggable {
      touch-action: pan-y;
      user-select: none;
      cursor: grab;
    }
    .viewport {
      overflow: hidden;
      height: 100%;
    }
    .track {
      display: flex;
      height: 100%;
    }
    .track.settling {
      transition: transform 380ms cubic-bezier(0.22, 0.61, 0.36, 1);
    }
    .slide {
      position: relative;
      flex: 0 0 100%;
      min-width: 0;
      min-height: 430px;
    }
    .slide img {
      position: absolute;
      inset: 0;
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .copy {
      position: absolute;
      inset: auto 0 0;
      padding: 72px 32px 64px;
      background: linear-gradient(transparent, #18382dec 30%);
      color: var(--color-surface);
    }
    h2 {
      font-family: var(--font-feature);
      font-size: clamp(28px, 3.3vw, 44px);
      font-weight: 400;
      line-height: 1.13;
      margin: 12px 0;
    }
    h2 a {
      color: inherit;
      text-decoration: none;
    }
    h2 a:hover {
      text-decoration: underline;
    }
    h2,
    .summary {
      display: -webkit-box;
      -webkit-line-clamp: 2;
      -webkit-box-orient: vertical;
      overflow: hidden;
      overflow-wrap: anywhere;
    }
    .copy p,
    .copy time {
      font-size: 13px;
      color: #e3e9dd;
    }
    .arrows {
      position: absolute;
      bottom: 16px;
      right: 24px;
      display: flex;
      align-items: center;
      gap: 12px;
      background: #fff6;
      border-radius: 24px;
      padding: 4px 8px;
      color: var(--color-ink);
    }
    .arrows button {
      border: 0;
      background: transparent;
      color: inherit;
      width: 32px;
      height: 32px;
      padding: 0;
    }
    .arrows span {
      font-size: 12px;
    }
    .byline {
      display: block;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      font-size: 12px;
    }
    .unavailable {
      display: block;
      padding: 20px;
      color: var(--color-surface);
      font-size: 13px;
    }
    @media (max-width: 600px) {
      .copy {
        padding: 48px 20px 64px;
      }
      .lead,
      .slide {
        min-height: 440px;
      }
    }
  `,
})
export class FeaturedNews {
  protected readonly router = inject(Router);
  readonly failedImages = signal(new Set<string>());
  readonly stories = input.required<PublicArticleSummaryResponse[]>();
  protected readonly clock = inject(CityClock);
  readonly index = signal(0);
  readonly offset = signal(0);
  readonly settling = signal(false);
  readonly slides = computed(() => {
    const stories = this.stories();
    return stories.length
      ? [-1, 0, 1].map((delta) => stories[(this.index() + delta + stories.length) % stories.length])
      : [];
  });
  private pointer?: { id: number; x: number; y: number; dragged: boolean };
  private width = 0;
  private suppressUntil = 0;
  private timer?: ReturnType<typeof setTimeout>;
  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.timer));
  }
  move(delta: number, event?: MouseEvent): void {
    if (this.settling() || this.stories().length < 2) return;
    if (event) this.width = (event.currentTarget as HTMLElement).closest('.lead')?.clientWidth ?? 0;
    this.settling.set(true);
    this.offset.set(delta > 0 ? -this.width : this.width);
    this.timer = setTimeout(() => {
      this.index.update((index) => (index + delta + this.stories().length) % this.stories().length);
      this.settling.set(false);
      this.offset.set(0);
    }, 380);
  }
  begin(event: PointerEvent): void {
    if (
      !event.isPrimary ||
      event.button !== 0 ||
      this.stories().length < 2 ||
      this.settling() ||
      (event.target as HTMLElement).closest('.arrows')
    )
      return;
    this.width = (event.currentTarget as HTMLElement).clientWidth;
    this.pointer = { id: event.pointerId, x: event.clientX, y: event.clientY, dragged: false };
  }
  drag(event: PointerEvent): void {
    const p = this.pointer;
    if (!p || p.id !== event.pointerId) return;
    const dx = event.clientX - p.x,
      dy = event.clientY - p.y;
    if (!p.dragged && Math.abs(dy) > Math.abs(dx) && Math.abs(dy) > 8) {
      this.cancel();
      return;
    }
    if (Math.abs(dx) > 8) {
      p.dragged = true;
      (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
      this.offset.set(Math.max(-this.width, Math.min(this.width, dx)));
    }
  }
  end(event: PointerEvent): void {
    const p = this.pointer;
    if (!p || p.id !== event.pointerId) return;
    if (p.dragged) {
      this.suppressUntil = performance.now() + 400;
      const dx = event.clientX - p.x;
      if (Math.abs(dx) >= Math.min(80, this.width * 0.15)) {
        this.pointer = undefined;
        this.move(dx < 0 ? 1 : -1);
        return;
      }
    }
    this.cancel();
  }
  cancel(): void {
    this.pointer = undefined;
    if (!this.settling()) this.offset.set(0);
  }
  open(event: MouseEvent): void {
    if (event.detail && performance.now() < this.suppressUntil) event.preventDefault();
  }
  failImage(id: string): void {
    this.failedImages.update((ids) => new Set([...ids, id]));
  }
}
