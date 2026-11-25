import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { accountProblem, errorMessage } from '../account/account-api';
import { ApiWrite } from '../api-write';
import { Api } from '../api/api';
import {
  blockAccount,
  blockListing,
  listAccountsForBlocking,
  listListingsForModeration,
  unblockAccount,
} from '../api/functions';
import { AccountBlockSummary, ListingModerationItem } from '../api/models';

type Target =
  { type: 'account'; item: AccountBlockSummary } | { type: 'listing'; item: ListingModerationItem };

@Component({
  selector: 'app-classifieds-moderation',
  imports: [FormsModule, RouterLink],
  templateUrl: './classifieds-moderation.html',
  styles: `
    .moderation-list {
      list-style: none;
      padding: 0;
    }
    .moderation-list li {
      border-bottom: 1px solid var(--color-divider);
      padding-block: 12px;
      overflow-wrap: anywhere;
    }
    .moderation-list button {
      margin-inline-start: 8px;
    }
  `,
})
export class ClassifiedsModeration implements OnInit {
  private readonly api = inject(Api);
  private readonly write = inject(ApiWrite);
  protected readonly listings = signal<ListingModerationItem[]>([]);
  protected readonly accounts = signal<AccountBlockSummary[]>([]);
  protected readonly listingPage = signal(1);
  protected readonly accountPage = signal(1);
  protected readonly moreListings = signal(false);
  protected readonly moreAccounts = signal(false);
  protected readonly busy = signal(false);
  protected readonly loaded = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly target = signal<Target | null>(null);
  protected reason = '';
  protected search = '';
  private query = '';

  ngOnInit(): void {
    void this.run(async () => {
      await Promise.all([this.readListings(1), this.readAccounts(1)]);
      this.loaded.set(true);
    });
  }

  protected loadListings(page: number): Promise<void> {
    return this.run(() => this.readListings(page));
  }

  protected loadAccounts(page: number): Promise<void> {
    return this.run(() => this.readAccounts(page));
  }

  protected findAccounts(): Promise<void> {
    this.query = this.search.trim();
    return this.loadAccounts(1);
  }

  protected select(target: Target | null): void {
    this.target.set(target);
    this.reason = '';
  }

  protected async submit(): Promise<void> {
    const target = this.target();
    if (!target || !this.reason.trim()) return;
    await this.run(async () => {
      const { id, version } = target.item;
      const reason = this.reason.trim();
      if (target.type === 'listing') {
        await this.write.invoke(blockListing, { id, body: { version, reason } });
        this.select(null);
        this.notice.set('Объявление скрыто.');
        await this.readListings(this.listingPage());
      } else {
        await this.write.invoke(blockAccount, { id, body: { version, reason } });
        this.select(null);
        this.notice.set('Аккаунт переведён в режим чтения.');
        await Promise.all([
          this.readAccounts(this.accountPage()),
          this.readListings(this.listingPage()),
        ]);
      }
    });
  }

  protected async unblock(item: AccountBlockSummary): Promise<void> {
    await this.run(async () => {
      await this.write.invoke(unblockAccount, { id: item.id, body: { version: item.version } });
      this.select(null);
      this.notice.set('Аккаунт разблокирован.');
      await this.readAccounts(this.accountPage());
    });
  }

  private async readListings(page: number): Promise<void> {
    const result = await this.api.invoke(listListingsForModeration, { page });
    this.listings.set(result.items);
    this.moreListings.set(result.hasNextPage);
    this.listingPage.set(page);
  }

  private async readAccounts(page: number): Promise<void> {
    const result = await this.api.invoke(listAccountsForBlocking, { page, query: this.query });
    this.accounts.set(result.items);
    this.moreAccounts.set(result.hasNextPage);
    this.accountPage.set(page);
  }

  private async run(task: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await task();
    } catch (error) {
      const code = accountProblem(error).code;
      this.error.set(
        code === 'account_block_forbidden'
          ? 'Этот аккаунт нельзя заблокировать или разблокировать.'
          : errorMessage(error),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
