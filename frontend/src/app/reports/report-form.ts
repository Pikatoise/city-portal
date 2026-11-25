import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiWrite } from '../api-write';
import { submitContentReport } from '../api/functions';
import { ReportKind, reportCategories } from './report-categories';

@Component({
  selector: 'app-report-form',
  imports: [FormsModule],
  templateUrl: './report-form.html',
  styleUrl: './report-form.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportForm {
  private readonly write = inject(ApiWrite);
  readonly kind = input.required<ReportKind>();
  readonly targetId = input.required<string>();
  protected readonly categories = computed(() => Object.entries(reportCategories[this.kind()]));
  protected readonly open = signal(false);
  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal('');
  protected category = '';
  protected text = '';

  protected cancel(): void {
    this.open.set(false);
    this.category = '';
    this.text = '';
    this.error.set('');
  }

  protected async submit(): Promise<void> {
    if (this.busy() || !this.category) return;

    this.busy.set(true);
    this.error.set('');
    try {
      await this.write.invoke(submitContentReport, {
        kind: this.kind(),
        id: this.targetId(),
        body: { category: this.category, text: this.text.trim() || null },
      });
      this.cancel();
      this.sent.set(true);
    } catch (error) {
      const status = error instanceof HttpErrorResponse ? error.status : 0;
      this.error.set(
        status === 429
          ? 'Достигнут лимит жалоб. Попробуй позже.'
          : status === 404
            ? 'Публикация больше недоступна.'
            : status === 403
              ? 'Отправка жалоб недоступна для этого аккаунта.'
              : 'Не удалось отправить жалобу. Попробуй ещё раз.',
      );
    } finally {
      this.busy.set(false);
    }
  }
}
