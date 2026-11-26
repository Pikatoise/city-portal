import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Api } from '../api/api';
import { ApiWrite } from '../api-write';
import { decideContentReports, getReportReview, listReportedContent } from '../api/functions';
import { ReportedContentSummary, ReportReviewPreview } from '../api/models';
import { ReportKind, reportCategories } from '../reports/report-categories';
import { CityClock } from './city-clock';

@Component({
  selector: 'app-report-queue',
  imports: [FormsModule, RouterLink],
  templateUrl: './report-queue.html',
  styleUrl: './report-queue.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportQueue implements OnInit {
  private readonly api = inject(Api);
  private readonly write = inject(ApiWrite);
  protected readonly clock = inject(CityClock);
  protected readonly kind = inject(ActivatedRoute).snapshot.data['kind'] as ReportKind;
  protected readonly queueTitles: Record<ReportKind, string> = {
    articles: 'Жалобы на статьи',
    classifieds: 'Жалобы на объявления',
    comments: 'Жалобы на комментарии',
  };
  protected readonly hideLabel =
    this.kind === 'comments' ? 'Скрыть комментарий' : 'Скрыть публикацию';
  protected readonly hideNotice =
    this.kind === 'comments' ? 'Комментарий скрыт.' : 'Публикация скрыта.';
  protected readonly categories = reportCategories[this.kind];
  protected readonly items = signal<ReportedContentSummary[]>([]);
  protected readonly preview = signal<ReportReviewPreview | null>(null);
  protected readonly resolvedPage = signal(1);
  protected readonly decisionPage = signal(1);
  protected readonly history = signal(false);
  protected readonly page = signal(1);
  protected readonly hasNext = signal(false);
  protected readonly loaded = signal(false);
  protected readonly busy = signal(false);
  protected readonly notice = signal('');
  protected readonly error = signal('');
  protected reason = '';

  ngOnInit(): void {
    void this.load(1, false);
  }

  protected async load(page: number, history: boolean): Promise<void> {
    await this.run(async () => {
      this.close();
      await this.readPage(page, history);
    });
  }

  protected async open(id: string, resolvedPage = 1, decisionPage = 1): Promise<void> {
    await this.run(async () => {
      if (this.preview()?.id !== id) this.reason = '';
      this.preview.set(
        await this.api.invoke(getReportReview, { kind: this.kind, id, resolvedPage, decisionPage }),
      );
      this.resolvedPage.set(resolvedPage);
      this.decisionPage.set(decisionPage);
    });
  }

  protected close(): void {
    this.preview.set(null);
    this.reason = '';
  }

  protected async decide(outcome: 'hide' | 'dismiss'): Promise<void> {
    const item = this.preview();
    const throughSequence = item?.throughSequence;
    if (!item || throughSequence == null) return;
    const reason = this.reason.trim();
    if (outcome === 'hide' && (!item.canHide || !reason)) return;
    await this.run(async () => {
      await this.write.invoke(decideContentReports, {
        kind: this.kind,
        id: item.id,
        body: {
          outcome,
          throughSequence,
          targetVersion: outcome === 'hide' ? item.targetVersion : null,
          reason: outcome === 'hide' ? reason : null,
        },
      });
      this.close();
      this.notice.set(outcome === 'hide' ? this.hideNotice : 'Жалобы отклонены.');
      await this.readPage(1, this.history());
    });
  }

  private async readPage(page: number, history: boolean): Promise<void> {
    const result = await this.api.invoke(listReportedContent, { kind: this.kind, page, history });
    this.page.set(page);
    this.history.set(history);
    this.items.set(result.items);
    this.hasNext.set(result.hasNextPage);
    this.loaded.set(true);
  }

  private async run(action: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await action();
    } catch (error) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      if (status === 401 || status === 403) {
        this.close();
        this.items.set([]);
        this.loaded.set(false);
      }
      this.error.set(
        status === 409
          ? 'Публикация или жалобы изменились. Открой проверку заново.'
          : status === 401 || status === 403
            ? 'Очередь доступна модераторам и администраторам.'
            : 'Не удалось загрузить или обработать жалобы.',
      );
    } finally {
      this.busy.set(false);
    }
  }
}
