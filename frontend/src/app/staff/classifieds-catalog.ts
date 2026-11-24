import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AccountApi, accountProblem, errorMessage } from '../account/account-api';
import { ApiWrite } from '../api-write';
import { Api } from '../api/api';
import {
  createListingCatalogEntry,
  listAdminListingCatalog,
  saveListingCatalogEntry,
} from '../api/functions';
import { CatalogEntryResponse, ClassifiedsPageOfCatalogEntryResponse } from '../api/models';

type CatalogKind = 'categories' | 'districts';

@Component({
  imports: [FormsModule, RouterLink],
  selector: 'app-classifieds-catalog',
  styleUrl: './classifieds-catalog.css',
  templateUrl: './classifieds-catalog.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ClassifiedsCatalog implements OnInit {
  private readonly account = inject(AccountApi);
  private readonly api = inject(Api);
  private readonly write = inject(ApiWrite);

  protected readonly kind = signal<CatalogKind>('categories');
  protected readonly result = signal<ClassifiedsPageOfCatalogEntryResponse | null>(null);
  protected readonly page = signal(1);
  protected readonly editing = signal<CatalogEntryResponse | null>(null);
  protected readonly busy = signal(false);
  protected readonly allowed = signal(false);
  protected readonly signedOut = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected name = '';
  protected requiresPrice = false;
  protected archived = false;

  ngOnInit(): void {
    void this.initialize();
  }

  protected async initialize(): Promise<void> {
    await this.run(async () => {
      const profile = await this.account.profile();
      if (!profile.emailConfirmed || !profile.roles.includes('Administrator')) {
        this.error.set('Для управления каталогом нужен администратор с подтверждённым Email.');
        return;
      }
      this.allowed.set(true);
      await this.readPage('categories', 1);
    });
  }

  protected async select(kind: CatalogKind, page = 1): Promise<void> {
    await this.run(async () => {
      await this.readPage(kind, page);
      this.edit(null);
    });
  }

  protected edit(entry: CatalogEntryResponse | null): void {
    this.editing.set(entry);
    this.name = entry?.name ?? '';
    this.requiresPrice = entry?.requiresPrice ?? false;
    this.archived = entry?.archived ?? false;
    this.error.set('');
    this.notice.set('');
  }

  protected async save(): Promise<void> {
    const name = this.name.trim();
    if (!name) {
      this.error.set('Укажи название.');
      return;
    }
    await this.run(async () => {
      const kind = this.kind();
      const entry = this.editing();
      const fields = { name, requiresPrice: this.requiresPrice, archived: this.archived };
      if (entry) {
        await this.write.invoke(saveListingCatalogEntry, {
          kind,
          id: entry.id,
          body: { version: entry.version, entry: fields },
        });
      } else {
        await this.write.invoke(createListingCatalogEntry, { kind, body: fields });
      }
      this.edit(null);
      this.notice.set(entry ? 'Изменения сохранены.' : 'Запись добавлена.');
      await this.readPage(kind, this.page());
    });
  }

  private async readPage(kind: CatalogKind, page: number): Promise<void> {
    this.result.set(await this.api.invoke(listAdminListingCatalog, { kind, page }));
    this.kind.set(kind);
    this.page.set(page);
  }

  private async run(operation: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await operation();
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.signedOut.set(true);
        this.allowed.set(false);
      } else if (error instanceof HttpErrorResponse && error.status === 403) {
        this.allowed.set(false);
        this.error.set('Для управления каталогом нужен администратор с подтверждённым Email.');
      } else {
        const code = accountProblem(error).code;
        this.error.set(
          code === 'duplicate_classifieds_catalog_name'
            ? 'Запись с таким названием уже есть.'
            : code === 'classifieds_catalog_conflict'
              ? 'Запись изменилась. Скопируй свои правки, обнови список и открой запись заново.'
              : errorMessage(error),
        );
      }
    } finally {
      this.busy.set(false);
    }
  }
}
