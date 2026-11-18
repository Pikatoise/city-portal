import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { errorMessage } from '../account/account-api';
import { getCsrf } from '../api/fn/auth/get-csrf';
import { claimSuggestion } from '../api/fn/staff-suggestions/claim-suggestion';
import { deleteSuggestion } from '../api/fn/staff-suggestions/delete-suggestion';
import { listAuthorSuggestions } from '../api/fn/staff-suggestions/list-author-suggestions';
import { listModerationSuggestions } from '../api/fn/staff-suggestions/list-moderation-suggestions';
import { returnSuggestion } from '../api/fn/staff-suggestions/return-suggestion';
import { StaffSuggestionResponse } from '../api/models/staff-suggestion-response';
import { CityClock } from './city-clock';

@Component({
  selector: 'app-suggestion-queue',
  imports: [FormsModule, RouterLink],
  templateUrl: './suggestion-queue.html',
  styleUrl: './suggestion-queue.css',
})
export class SuggestionQueue implements OnInit {
  private readonly http = inject(HttpClient);
  protected readonly clock = inject(CityClock);
  protected readonly moderation = inject(ActivatedRoute).snapshot.data['moderation'] === true;
  protected readonly mine = signal(false);
  protected readonly page = signal(1);
  protected readonly hasMore = signal(false);
  protected readonly items = signal<StaffSuggestionResponse[]>([]);
  protected readonly deleting = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly loaded = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected reason = '';

  ngOnInit(): void {
    void this.load();
  }

  protected async load(page = 1, mine = this.mine()): Promise<void> {
    await this.run(async () => {
      await this.readPage(page, mine);
      this.cancelDelete();
    });
  }

  protected prepareDelete(id: string): void {
    this.deleting.set(id);
    this.reason = '';
    this.error.set('');
    this.notice.set('');
  }

  protected cancelDelete(): void {
    this.deleting.set(null);
    this.reason = '';
  }

  protected async change(item: StaffSuggestionResponse): Promise<void> {
    const reason = this.reason.trim();
    if (this.moderation && !reason) {
      this.error.set('Укажите причину удаления.');
      return;
    }

    await this.run(async () => {
      const { body: csrf } = await firstValueFrom(getCsrf(this.http, ''));
      const headers = { 'X-CSRF-TOKEN': csrf.token, id: item.id };
      if (this.moderation) {
        await firstValueFrom(
          deleteSuggestion(this.http, '', { ...headers, body: { version: item.version, reason } }),
        );
      } else {
        const change = this.mine() ? returnSuggestion : claimSuggestion;
        await firstValueFrom(
          change(this.http, '', { ...headers, body: { version: item.version } }),
        );
      }
      this.notice.set(
        this.moderation
          ? 'Предложение удалено.'
          : this.mine()
            ? 'Предложение возвращено в очередь.'
            : 'Черновик создан. Он доступен в разделе «Мои назначения».',
      );
      this.items.update((items) => items.filter((entry) => entry.id !== item.id));
      this.cancelDelete();
      try {
        await this.readPage(this.page(), this.mine());
        if (!this.items().length && this.page() > 1) {
          await this.readPage(this.page() - 1, this.mine());
        }
      } catch (error) {
        if (error instanceof HttpErrorResponse && (error.status === 401 || error.status === 403)) {
          throw error;
        }
        this.error.set('Не удалось обновить список. Нажмите «Обновить».');
      }
    });
  }

  private async readPage(page: number, mine: boolean): Promise<void> {
    const { body } = await firstValueFrom(
      this.moderation
        ? listModerationSuggestions(this.http, '', { page })
        : listAuthorSuggestions(this.http, '', { page, mine }),
    );
    this.items.set(body.items);
    this.mine.set(mine);
    this.page.set(page);
    this.hasMore.set(body.hasMore);
    this.loaded.set(true);
  }

  private async run(operation: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await operation();
    } catch (error) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      if (status === 401 || status === 403) {
        this.items.set([]);
        this.loaded.set(false);
        this.cancelDelete();
      }
      this.error.set(
        status === 401
          ? 'Войдите в аккаунт, чтобы открыть предложения.'
          : status === 403
            ? 'У вашего аккаунта нет доступа к этому разделу.'
            : status === 404 || status === 409
              ? 'Предложение уже изменилось или недоступно. Обновите список.'
              : errorMessage(error),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
