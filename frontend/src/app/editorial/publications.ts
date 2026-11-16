import { Component, computed, inject, signal, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { AccountApi, AccountProfile } from '../account/account-api';
import { NewsApi, ArticleSummaryResponse, newsError } from './news-api';
import { HomepageSelectionResponse } from '../api/models/homepage-selection-response';
import { TaxonomyResponse } from '../api/models/taxonomy-response';

@Component({
  selector: 'app-publications',
  imports: [RouterLink, FormsModule],
  templateUrl: './publications.html',
  styleUrl: './publications.css',
})
export class Publications implements OnInit {
  private readonly api = inject(NewsApi);
  private readonly accounts = inject(AccountApi);
  private readonly router = inject(Router);
  readonly profile = signal<AccountProfile | null>(null);
  readonly allowed = computed(
    () =>
      !!this.profile()?.emailConfirmed &&
      !!this.profile()?.roles.some((role) => role === 'Editor' || role === 'Administrator'),
  );
  readonly rows = signal<ArticleSummaryResponse[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly withdrawing = signal<ArticleSummaryResponse | null>(null);
  reason = '';
  readonly selection = signal<HomepageSelectionResponse | null>(null);
  readonly categories = signal<TaxonomyResponse[]>([]);
  firstCategoryId = '';
  secondCategoryId = '';
  async configureHomepage(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const [selection, categories] = await Promise.all([
        this.api.homepage(),
        this.api.taxonomy('categories'),
      ]);
      this.selection.set(selection);
      this.categories.set(categories.filter((item) => !item.archived));
      this.firstCategoryId = selection.firstCategoryId ?? '';
      this.secondCategoryId = selection.secondCategoryId ?? '';
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  async saveHomepage(): Promise<void> {
    const selection = this.selection();
    if (!selection || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      this.selection.set(
        await this.api.saveHomepage(selection.version, this.firstCategoryId, this.secondCategoryId),
      );
      this.notice.set('Категории главной сохранены.');
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  async feature(row: ArticleSummaryResponse): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.feature(row.id, row.version, !row.featured);
      await this.load(this.page());
      this.notice.set(
        row.featured ? 'Статья убрана из выбора редакции.' : 'Статья добавлена в выбор редакции.',
      );
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  async openPublic(id: string): Promise<void> {
    try {
      const article = await this.api.publicArticle(id);
      await this.router.navigateByUrl(article.canonicalUrl);
    } catch (error) {
      this.error.set(newsError(error));
    }
  }
  async ngOnInit(): Promise<void> {
    try {
      this.profile.set(await this.accounts.profile());
      if (this.allowed()) await this.load();
    } catch (error) {
      this.error.set(newsError(error));
    }
  }
  async load(page = 1): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const result = await this.api.publications(page);
      this.rows.set(result.items);
      this.total.set(Number(result.total));
      this.page.set(page);
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  async withdraw(): Promise<void> {
    const row = this.withdrawing();
    if (!row || this.busy() || !this.reason.trim()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.act(row.id, row.version, 'withdraw', this.reason);
      this.withdrawing.set(null);
      this.reason = '';
      this.notice.set('Материал снят с публикации. Исправления подготовит автор.');
      await this.load(this.page());
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
