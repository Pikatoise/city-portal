import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  afterNextRender,
  inject,
  input,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiWrite } from '../api-write';
import { Api } from '../api/api';
import {
  createArticleComment,
  deleteOwnComment,
  hidePublicComment,
  listArticleComments,
} from '../api/functions';
import { CommentPage } from '../api/models';
import { ReportForm } from '../reports/report-form';
import { CityClock } from '../staff/city-clock';

@Component({
  selector: 'app-article-comments',
  imports: [FormsModule, ReportForm],
  templateUrl: './article-comments.html',
  styleUrl: './article-comments.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ArticleComments implements OnInit {
  readonly articleId = input.required<string>();
  private readonly api = inject(Api);
  private readonly write = inject(ApiWrite);
  protected readonly clock = inject(CityClock);
  protected readonly result = signal<CommentPage | null>(null);
  protected readonly cursors = signal<(string | null)[]>([null]);
  protected readonly busy = signal(false);
  protected readonly interactive = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly action = signal<{ id: string; type: 'delete' | 'hide' } | null>(null);
  protected body = '';
  protected reason = '';

  constructor() {
    afterNextRender(() => this.interactive.set(true));
  }

  ngOnInit(): void {
    void this.load(this.cursors());
  }

  protected load(cursors: (string | null)[]): Promise<void> {
    return this.run(() => this.readPage(cursors), 'Не удалось загрузить комментарии.');
  }

  protected post(): Promise<void> {
    const body = this.body.trim();
    if (!body || body.length > 500) return Promise.resolve();
    return this.run(async () => {
      await this.write.invoke(createArticleComment, {
        articleId: this.articleId(),
        body: { body },
      });
      this.body = '';
      this.notice.set('Комментарий опубликован.');
      await this.readPage([null]);
    }, 'Не удалось опубликовать комментарий.');
  }

  protected startAction(id: string, type: 'delete' | 'hide'): void {
    this.reason = '';
    this.action.set({ id, type });
  }

  protected applyAction(): Promise<void> {
    const action = this.action();
    if (!action) return Promise.resolve();
    const reason = this.reason.trim();
    if (action.type === 'hide' && !reason) return Promise.resolve();
    return this.run(async () => {
      if (action.type === 'hide') {
        await this.write.invoke(hidePublicComment, { id: action.id, body: { reason } });
      } else {
        await this.write.invoke(deleteOwnComment, { id: action.id });
      }
      this.action.set(null);
      this.notice.set(action.type === 'hide' ? 'Комментарий скрыт.' : 'Комментарий удалён.');
      this.result.update((result) =>
        result ? { ...result, items: result.items.filter((item) => item.id !== action.id) } : null,
      );
      await this.readPage(this.cursors());
      if (this.cursors().length > 1 && this.result()?.items.length === 0)
        await this.readPage(this.cursors().slice(0, -1));
    }, 'Не удалось изменить комментарий.');
  }

  private async readPage(cursors: (string | null)[]): Promise<void> {
    try {
      this.result.set(
        await this.api.invoke(listArticleComments, {
          articleId: this.articleId(),
          cursor: cursors.at(-1) ?? undefined,
        }),
      );
      this.cursors.set(cursors);
      this.action.set(null);
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) this.result.set(null);
      throw error;
    }
  }

  private async run(action: () => Promise<void>, fallback: string): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await action();
    } catch (error) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      this.error.set(
        status === 429
          ? 'Слишком много попыток. Попробуй позже.'
          : status === 403
            ? 'Действие недоступно для этого аккаунта.'
            : status === 404
              ? 'Статья или комментарий больше недоступны.'
              : this.notice()
                ? 'Не удалось обновить список комментариев.'
                : fallback,
      );
    } finally {
      this.busy.set(false);
    }
  }
}
