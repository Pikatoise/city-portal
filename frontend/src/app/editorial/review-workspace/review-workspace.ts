import {
  Component,
  OnInit,
  inject,
  signal,
  computed,
  ElementRef,
  DestroyRef,
  Injector,
  afterNextRender,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AccountApi, AccountProfile } from '../../account/account-api';
import { CityClock } from '../../staff/city-clock';
import {
  NewsApi,
  ReviewResponse,
  ReviewSummaryResponse,
  ArticleHistoryResponse,
  TaxonomyResponse,
  newsError,
} from '../news-api';
import { articleState, reviewAction } from '../workflow-labels';

@Component({
  imports: [FormsModule, RouterLink],
  selector: 'app-review-workspace',
  styleUrl: './review-workspace.css',
  templateUrl: './review-workspace.html',
})
export class ReviewWorkspace implements OnInit {
  constructor() {
    const host = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const onError = (event: Event) => {
      const image = event.target;
      if (image instanceof HTMLImageElement && !image.hidden) {
        image.hidden = true;
        const placeholder = document.createElement('span');
        placeholder.textContent = 'Изображение недоступно';
        image.after(placeholder);
      }
    };
    host.addEventListener('error', onError, true);
    inject(DestroyRef).onDestroy(() => host.removeEventListener('error', onError, true));
  }
  private readonly api = inject(NewsApi);
  private readonly account = inject(AccountApi);
  private readonly injector = inject(Injector);
  private readonly previewTitle = viewChild<ElementRef<HTMLHeadingElement>>('previewTitle');
  protected readonly clock = inject(CityClock);
  readonly profile = signal<AccountProfile | null>(null);
  readonly allowed = computed(
    () =>
      !!this.profile()?.emailConfirmed &&
      this.profile()!.roles.some((role) => role === 'Editor' || role === 'Administrator'),
  );
  readonly rows = signal<ReviewSummaryResponse[]>([]);
  readonly taxonomy = signal<TaxonomyResponse[]>([]);
  taxonomyName(id: string | null | undefined): string {
    return this.taxonomy().find((item) => item.id === id)?.name ?? id ?? 'Не выбрана';
  }
  readonly selected = signal<ReviewResponse | null>(null);
  readonly history = signal<ArticleHistoryResponse[]>([]);
  readonly historyPage = signal(1);
  readonly historyTotal = signal(0);
  readonly snapshot = signal<ArticleHistoryResponse | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly page = signal(1);
  readonly total = signal(0);
  readonly mine = signal(false);
  readonly state = articleState;
  readonly actionName = reviewAction;
  reason = '';
  async ngOnInit(): Promise<void> {
    this.busy.set(true);
    try {
      this.profile.set(await this.account.profile());
      if (this.allowed()) {
        const [categories, tags] = await Promise.all([
          this.api.taxonomy('categories'),
          this.api.taxonomy('tags'),
        ]);
        this.taxonomy.set([...categories, ...tags]);
        await this.load();
      }
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  async load(page = 1): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const result = await this.api.reviews(this.mine(), page);
      this.rows.set(result.items);
      this.total.set(Number(result.total));
      this.page.set(page);
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  async open(id: string, focusPreview = true): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.snapshot.set(null);
    try {
      this.selected.set(await this.api.review(id));
      this.reason = '';
      if (focusPreview)
        afterNextRender(() => this.previewTitle()?.nativeElement.focus(), {
          injector: this.injector,
        });
      await this.loadHistory();
    } catch (error) {
      this.selected.set(null);
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  async loadHistory(page = 1): Promise<void> {
    const selected = this.selected();
    if (!selected) return;
    try {
      const result = await this.api.history(selected.article.id, page);
      this.history.set(result.items);
      this.historyTotal.set(Number(result.total));
      this.historyPage.set(page);
    } catch (error) {
      this.error.set(newsError(error));
    }
  }
  async act(id: string, version: number | string, operation: string): Promise<void> {
    if (this.busy()) return;
    if (
      ['reject', 'publish', 'takeover'].includes(operation) &&
      !window.confirm('Подтвердить действие со статьёй?')
    )
      return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await this.api.act(id, version, operation, this.reason);
      this.notice.set('Действие сохранено.');
      if (operation === 'release') {
        this.selected.set(null);
        this.history.set([]);
      } else if (operation !== 'publish' || this.selected()) {
        await this.open(id, operation === 'claim' || operation === 'takeover');
      }
      await this.load(this.page());
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  canRead(row: ReviewSummaryResponse): boolean {
    return (
      row.reviewerId === this.profile()?.id || !!this.profile()?.roles.includes('Administrator')
    );
  }
}
