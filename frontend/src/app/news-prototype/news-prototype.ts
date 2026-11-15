import {
  AfterViewInit,
  Component,
  HostListener,
  OnDestroy,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { SiteMetadata } from '../site-metadata';
import { HomeTop } from './home-top';
import { ArticleReader } from './article-reader';
import { EditorialWorkspace } from './editorial-workspace';

@Component({
  selector: 'app-root',
  imports: [FormsModule, RouterLink, HomeTop, ArticleReader, EditorialWorkspace],
  templateUrl: './news-prototype.html',
  styleUrl: './news-prototype.css',
})
export class NewsPrototype implements AfterViewInit, OnDestroy {
  readonly site = inject(SiteMetadata);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly screen = signal('home');
  readonly readerId = signal(1);
  readonly message = signal('');
  readonly query = signal('');
  readonly category = signal('');
  readonly tag = signal('');
  readonly dateFrom = signal('');
  readonly dateTo = signal('');
  readonly featuredIndex = signal(0);
  readonly featuredHeight = signal(600);
  private featuredResize?: ResizeObserver;
  readonly featuredDrag = signal(0);
  readonly featuredSettling = signal(false);
  private featuredTimer?: ReturnType<typeof setTimeout>;
  private featuredPointer?: { id: number; x: number; y: number; dragged: boolean };
  private suppressFeaturedClickUntil = 0;
  readonly featuredCount = signal(5);
  readonly latestPage = signal(1);
  private scrollFrame = 0;
  private pageFrame = 0;
  private pageAnimation?: Animation;
  private pageKey = '';
  readonly articles = Array.from({ length: 46 }, (_, index) => ({
    id: index + 1,
    title: [
      'На набережной открыли новый прогулочный маршрут',
      'Библиотека приглашает на вечер городских историй',
      'Как изменится движение автобусов в выходные',
      'В сквере у вокзала высадили молодые липы',
      'Школьная команда выиграла городской турнир',
      'Музей откроет выставку старых фотографий',
    ][index % 6],
    summary: [
      'Два километра дорожек, места для отдыха и новый вид на реку.',
      'Встречи с краеведами и истории, которые хранят жители города.',
      'Собрали главное, чтобы было проще спланировать выходные.',
    ][index % 3],
    category: ['Город', 'Культура', 'Транспорт'][index % 3],
    tag: ['Благоустройство', 'Выходные', 'Маршруты'][index % 3],
    date: `2026-11-${String(13 - Math.floor(index / 4)).padStart(2, '0')}`,
    time: `${18 - (index % 4)}:30`,
    featured: index >= 3 && index < 8,
  }));
  readonly filtered = computed(() =>
    this.articles.filter(
      (article) =>
        (!this.query() ||
          `${article.title} ${article.summary}`
            .toLowerCase()
            .includes(this.query().toLowerCase())) &&
        (!this.category() || article.category === this.category()) &&
        (!this.tag() || article.tag === this.tag()) &&
        (!this.dateFrom() || article.date >= this.dateFrom()) &&
        (!this.dateTo() || article.date <= this.dateTo()),
    ),
  );
  readonly readerStory = computed(
    () => this.articles.find((story) => story.id === this.readerId()) ?? this.articles[0],
  );
  readonly latest = computed(() =>
    this.filtered().slice((this.latestPage() - 1) * 20, this.latestPage() * 20),
  );
  readonly featured = computed(() =>
    this.articles.filter((a) => a.featured).slice(0, this.featuredCount()),
  );
  readonly recentCards = this.articles.slice(0, 3);
  readonly featuredSlides = computed(() => {
    const stories = this.featured();
    return [-1, 0, 1].map(
      (delta) => stories[(this.featuredIndex() + delta + stories.length) % stories.length],
    );
  });
  formatDate(value: string): string {
    return new Date(value).toLocaleDateString('ru-RU', {
      day: 'numeric',
      month: 'long',
      timeZone: 'UTC',
    });
  }
  readonly homeLatest = this.articles.slice(3, 5);
  readonly categoryBlocks = ['Транспорт', 'Культура'].map((name) => ({
    name,
    articles: this.articles.filter((article) => article.category === name).slice(0, 5),
  }));
  readonly homeCategories = this.categoryBlocks.map((block) => block.name);
  moveFeatured(delta: number): void {
    if (this.featuredSettling() || this.featured().length < 2) return;
    const width = document.querySelector('.featured-viewport')?.clientWidth ?? 0;
    this.featuredSettling.set(true);
    this.featuredDrag.set(delta > 0 ? -width : width);
    this.featuredTimer = setTimeout(() => {
      this.featuredSettling.set(false);
      this.featuredIndex.update(
        (index) => (index + delta + this.featured().length) % this.featured().length,
      );
      this.featuredDrag.set(0);
    }, 380);
  }
  beginFeaturedDrag(event: PointerEvent): void {
    if (
      this.featured().length < 2 ||
      this.featuredSettling() ||
      !event.isPrimary ||
      event.button !== 0 ||
      (event.target as HTMLElement).closest('.featured-arrows')
    )
      return;
    this.featuredPointer = {
      id: event.pointerId,
      x: event.clientX,
      y: event.clientY,
      dragged: false,
    };
  }
  dragFeatured(event: PointerEvent): void {
    const pointer = this.featuredPointer;
    if (!pointer || pointer.id !== event.pointerId) return;
    const dx = event.clientX - pointer.x;
    const dy = event.clientY - pointer.y;
    if (!pointer.dragged && Math.abs(dy) > Math.abs(dx) && Math.abs(dy) > 8) {
      this.cancelFeaturedDrag();
      return;
    }
    if (Math.abs(dx) > 8) {
      pointer.dragged = true;
      (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
      const width = (event.currentTarget as HTMLElement).clientWidth;
      this.featuredDrag.set(Math.max(-width, Math.min(width, dx)));
    }
  }
  endFeaturedDrag(event: PointerEvent): void {
    const pointer = this.featuredPointer;
    if (!pointer || pointer.id !== event.pointerId) return;
    const dx = event.clientX - pointer.x;
    if (pointer.dragged) {
      this.suppressFeaturedClickUntil = performance.now() + 400;
      const threshold = Math.min(80, (event.currentTarget as HTMLElement).clientWidth * 0.15);
      if (Math.abs(dx) >= threshold) {
        this.featuredPointer = undefined;
        this.moveFeatured(dx < 0 ? 1 : -1);
        return;
      }
    }
    this.cancelFeaturedDrag();
  }
  cancelFeaturedDrag(): void {
    this.featuredPointer = undefined;
    if (this.featuredSettling()) return;
    if (this.featuredDrag() !== 0) {
      this.featuredSettling.set(true);
      this.featuredTimer = setTimeout(() => this.featuredSettling.set(false), 380);
    }
    this.featuredDrag.set(0);
  }
  openFeaturedArticle(event: MouseEvent, id: number): void {
    if (event.detail !== 0 && performance.now() < this.suppressFeaturedClickUntil) return;
    this.openArticle(id);
  }
  readonly latestPages = computed(() =>
    Array.from({ length: Math.ceil(this.filtered().length / 20) }, (_, i) => i + 1),
  );

  constructor() {
    this.route.queryParamMap.subscribe((params) => {
      const count = Number(params.get('featured') ?? 5);
      this.featuredCount.set(Number.isInteger(count) && count >= 1 && count <= 5 ? count : 5);
      this.featuredIndex.set(0);
      this.screen.set(
        ['editor', 'list', 'article'].includes(params.get('screen') ?? '')
          ? params.get('screen')!
          : 'home',
      );
      this.readerId.set(Number(params.get('id') ?? 1));
      this.category.set(params.get('category') ?? '');
      this.query.set(params.get('q') ?? '');
      this.latestPage.set(1);
      const pageKey = [
        this.screen(),
        this.screen() === 'article' ? this.readerId() : '',
        this.screen() === 'list' ? this.category() + ':' + this.query() : '',
      ].join(':');
      if (pageKey !== this.pageKey) {
        this.pageKey = pageKey;
        cancelAnimationFrame(this.pageFrame);
        this.pageAnimation?.cancel();
        this.pageFrame = requestAnimationFrame(() => {
          this.pageAnimation = document
            .getElementById('prototype-content')
            ?.animate([{ opacity: 0 }, { opacity: 1 }], { duration: 180, easing: 'ease-out' });
        });
      }
    });
  }

  ngAfterViewInit(): void {
    this.featuredResize = new ResizeObserver(() => this.fitFeatured());
    this.featuredResize.observe(document.body);
    this.fitFeatured();
  }

  ngOnDestroy(): void {
    cancelAnimationFrame(this.pageFrame);
    this.pageAnimation?.cancel();
    this.featuredResize?.disconnect();
    clearTimeout(this.featuredTimer);
    this.stopScroll();
  }
  searchNews(value: string): void {
    this.openList(this.screen() === 'list' ? this.category() : '', value);
  }
  @HostListener('window:resize')
  fitFeatured(): void {
    const height = (selector: string): number =>
      document.querySelector(selector)?.getBoundingClientRect().height ?? 0;
    this.featuredHeight.set(
      Math.max(
        0,
        window.innerHeight -
          height('.home-top') -
          height('.portal-tools') -
          height('.featured-section > .section-heading') -
          34,
      ),
    );
  }
  async filterNews(value: string): Promise<void> {
    this.stopScroll();
    if (this.screen() !== 'home') {
      await this.router.navigate([], {
        queryParams: { screen: 'home', category: null, q: null },
        queryParamsHandling: 'merge',
      });
    }
    this.scrollFrame = requestAnimationFrame(() => {
      const section = document.getElementById(value ? 'news-' + value : 'news-latest');
      if (!section) return;
      const offset = document.querySelector('.portal-tools')?.getBoundingClientRect().height ?? 0;
      const start = window.scrollY;
      const target = Math.max(
        0,
        Math.min(
          start + section.getBoundingClientRect().top - offset - 12,
          document.documentElement.scrollHeight - window.innerHeight,
        ),
      );
      const started = performance.now();
      const step = (now: number): void => {
        const progress = Math.min((now - started) / 650, 1);
        const eased = progress * progress * (3 - 2 * progress);
        window.scrollTo({ top: start + (target - start) * eased, behavior: 'instant' });
        if (progress < 1) this.scrollFrame = requestAnimationFrame(step);
      };
      this.scrollFrame = requestAnimationFrame(step);
    });
  }
  @HostListener('window:wheel')
  @HostListener('window:touchstart')
  @HostListener('window:keydown')
  stopScroll(): void {
    cancelAnimationFrame(this.scrollFrame);
  }
  openList(category = '', query = ''): void {
    this.stopScroll();
    this.tag.set('');
    this.dateFrom.set('');
    this.dateTo.set('');
    void this.router
      .navigate([], {
        queryParams: { screen: 'list', category: category || null, q: query || null },
        queryParamsHandling: 'merge',
      })
      .then(() => window.scrollTo({ top: 0 }));
  }
  openArticle(id: number): void {
    this.stopScroll();
    this.message.set('');
    void this.router
      .navigate([], {
        queryParams: { screen: 'article', id, category: null, q: null },
        queryParamsHandling: 'merge',
      })
      .then(() => window.scrollTo({ top: 0, behavior: 'instant' }));
  }
  openTag(tag: string): void {
    this.openList();
    this.tag.set(tag);
  }
}
