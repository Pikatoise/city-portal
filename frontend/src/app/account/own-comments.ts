import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiWrite } from '../api-write';
import { Api } from '../api/api';
import { deleteOwnComment, listOwnComments } from '../api/functions';
import { OwnCommentPage } from '../api/models';
import { CityClock } from '../staff/city-clock';

@Component({
  selector: 'app-own-comments',
  imports: [RouterLink],
  templateUrl: './own-comments.html',
  styleUrl: './own-comments.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OwnComments implements OnInit {
  private readonly api = inject(Api);
  private readonly write = inject(ApiWrite);
  protected readonly clock = inject(CityClock);
  protected readonly result = signal<OwnCommentPage | null>(null);
  protected readonly busy = signal(false);
  protected readonly page = signal(1);
  protected readonly selection = signal<string | null>(null);
  protected readonly error = signal('');
  protected readonly signedOut = signal(false);

  ngOnInit(): void {
    void this.load(1);
  }

  protected load(page: number): Promise<void> {
    return this.run(() => this.read(page), 'Не удалось загрузить комментарии.');
  }

  protected remove(id: string): Promise<void> {
    return this.run(async () => {
      await this.write.invoke(deleteOwnComment, { id });
      this.selection.set(null);
      this.result.update((result) =>
        result ? { ...result, items: result.items.filter((item) => item.id !== id) } : null,
      );
      await this.read(this.page());
      if (this.page() > 1 && this.result()?.items.length === 0) await this.read(this.page() - 1);
    }, 'Не удалось удалить комментарий.');
  }

  private async read(page: number): Promise<void> {
    this.result.set(await this.api.invoke(listOwnComments, { page }));
    this.page.set(page);
    this.selection.set(null);
  }

  private async run(action: () => Promise<void>, fallback: string): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
    } catch (error) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      if (status === 401) {
        this.result.set(null);
        this.signedOut.set(true);
      } else {
        this.error.set(status === 403 ? 'Удаление недоступно для этого аккаунта.' : fallback);
      }
    } finally {
      this.busy.set(false);
    }
  }
}
