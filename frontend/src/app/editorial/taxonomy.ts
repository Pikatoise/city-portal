import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AccountApi } from '../account/account-api';
import { NewsApi, TaxonomyResponse, newsError } from './news-api';

@Component({
  imports: [FormsModule, RouterLink],
  selector: 'app-taxonomy',
  styleUrl: './taxonomy.css',
  templateUrl: './taxonomy.html',
})
export class Taxonomy implements OnInit {
  private readonly api = inject(NewsApi);
  private readonly account = inject(AccountApi);
  readonly allowed = signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly entries = signal<TaxonomyResponse[]>([]);
  readonly kind = signal('categories');
  name = '';
  readonly editing = signal<TaxonomyResponse | null>(null);
  ngOnInit(): void {
    void this.initialize();
  }
  async initialize(): Promise<void> {
    try {
      const profile = await this.account.profile();
      this.allowed.set(
        profile.emailConfirmed &&
          profile.roles.some((role) => ['Editor', 'Administrator'].includes(role)),
      );
      if (this.allowed()) await this.load('categories');
      else
        this.error.set(
          'Для управления категориями и тегами нужна роль редактора или администратора и подтверждённый Email.',
        );
    } catch (error) {
      this.error.set(newsError(error));
    }
  }
  async load(kind: string): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.editing.set(null);
    this.name = '';
    try {
      this.entries.set(await this.api.taxonomy(kind));
      this.kind.set(kind);
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  edit(entry: TaxonomyResponse): void {
    this.editing.set(entry);
    this.name = entry.name;
  }
  async save(): Promise<void> {
    if (
      ![...this.name.trim().normalize('NFC')].length ||
      [...this.name.trim().normalize('NFC')].length > 100
    ) {
      this.error.set('Укажи название от 1 до 100 символов.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      const entry = this.editing();
      if (entry) await this.api.updateTaxonomy(entry, this.name, entry.archived);
      else await this.api.createTaxonomy(this.kind(), this.name);
      await this.load(this.kind());
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  async archive(entry: TaxonomyResponse): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.updateTaxonomy(entry, entry.name, true);
      await this.load(this.kind());
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
