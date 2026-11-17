import {
  Component,
  DestroyRef,
  ElementRef,
  OnInit,
  ViewChild,
  inject,
  signal,
  computed,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AccountApi, AccountProfile } from '../account/account-api';
import { CityClock } from '../staff/city-clock';
import {
  MediaApi,
  ImageResponse,
  TemplateResponse,
  RemovalRequestResponse,
  TemplateHistoryResponse,
  mediaError,
} from './media-api';

@Component({
  imports: [FormsModule, RouterLink],
  selector: 'app-media-library',
  styleUrl: './media-library.css',
  templateUrl: './media-library.html',
})
export class MediaLibrary implements OnInit {
  private readonly api = inject(MediaApi);
  private readonly account = inject(AccountApi);
  protected readonly clock = inject(CityClock);
  private readonly destroy = inject(DestroyRef);
  @ViewChild('actionDialog') actionDialog!: ElementRef<HTMLDialogElement>;
  readonly profile = signal<AccountProfile | null>(null);
  readonly author = computed(
    () => !!this.profile()?.emailConfirmed && !!this.profile()?.roles.includes('Author'),
  );
  readonly moderator = computed(
    () =>
      !!this.profile()?.emailConfirmed &&
      !!this.profile()?.roles.some((role) => ['Moderator', 'Administrator'].includes(role)),
  );
  readonly section = signal('uploads');
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly images = signal<ImageResponse[]>([]);
  readonly templates = signal<TemplateResponse[]>([]);
  readonly requests = signal<RemovalRequestResponse[]>([]);
  readonly history = signal<TemplateHistoryResponse[]>([]);
  readonly historyTitle = signal('');
  readonly historyId = signal('');
  readonly historyPage = signal(1);
  readonly historyTotal = signal(0);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly action = signal('');
  readonly target = signal<TemplateResponse | null>(null);
  readonly selectedImage = signal('');
  readonly replacements = signal<ImageResponse[]>([]);
  readonly failedPreviews = signal<string[]>([]);
  name = '';
  reason = '';
  replacement = '';
  private refreshTimer?: ReturnType<typeof setTimeout>;
  private requestSequence = 0;
  readonly actionLabels: Record<string, string> = {
    propose: 'Предложить общий шаблон',
    submit: 'Отправить на проверку',
    approve: 'Одобрить шаблон',
    reject: 'Отклонить предложение',
    archive: 'Архивировать шаблон',
    restore: 'Вернуть в библиотеку',
    block: 'Заблокировать изображение',
    unblock: 'Снять блокировку',
    removal: 'Запросить архивирование',
  };
  readonly historyLabels: Record<string, string> = {
    Created: 'Предложение создано',
    submit: 'Отправлено на проверку',
    approve: 'Одобрено',
    reject: 'Отклонено',
    archive: 'Архивировано',
    restore: 'Возвращено в библиотеку',
    block: 'Заблокировано',
    unblock: 'Разблокировано',
    RemovalRequested: 'Автор запросил архивирование',
    RemovalResolved: 'Архивировано по запросу автора',
  };

  constructor() {
    this.destroy.onDestroy(() => {
      clearTimeout(this.refreshTimer);
      this.requestSequence++;
    });
  }
  async ngOnInit(): Promise<void> {
    try {
      this.profile.set(await this.account.profile());
      if (!this.author() && !this.moderator()) {
        this.error.set(
          'Медиатека доступна авторам, а проверка шаблонов модераторам и администраторам с подтверждённым Email.',
        );
        return;
      }
      this.section.set(this.author() ? 'uploads' : 'queue');
      await this.load();
    } catch (error) {
      this.error.set(mediaError(error));
    }
  }
  async changeSection(value: string): Promise<void> {
    this.section.set(value);
    this.historyId.set('');
    await this.load(1);
  }
  async load(page = this.page()): Promise<void> {
    if (this.destroy.destroyed) return;
    const sequence = ++this.requestSequence;
    clearTimeout(this.refreshTimer);
    this.busy.set(true);
    this.error.set('');
    try {
      const section = this.section();
      const result =
        section === 'uploads'
          ? await this.api.images(page)
          : section === 'requests'
            ? await this.api.removals(page)
            : await this.api.templates(
                section === 'shared'
                  ? 'library'
                  : section === 'proposals'
                    ? 'mine'
                    : section === 'queue'
                      ? 'queue'
                      : 'moderation',
                page,
              );
      if (sequence !== this.requestSequence) return;
      this.failedPreviews.set([]);
      this.images.set(section === 'uploads' ? (result.items as ImageResponse[]) : []);
      this.templates.set(
        ['uploads', 'requests'].includes(section) ? [] : (result.items as TemplateResponse[]),
      );
      this.requests.set(section === 'requests' ? (result.items as RemovalRequestResponse[]) : []);
      this.total.set(Number(result.total));
      this.page.set(Number(result.page));
      if (section === 'uploads' && this.images().some((image) => image.state === 'Processing'))
        this.refreshTimer = setTimeout(() => void this.load(), 3000);
    } catch (error) {
      if (sequence === this.requestSequence) this.error.set(mediaError(error));
    } finally {
      if (sequence === this.requestSequence) this.busy.set(false);
    }
  }
  async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    input.value = '';
    if (file.size > 10 * 1024 * 1024) {
      this.error.set('Максимальный размер файла: 10 МиБ.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.upload(file);
      this.notice.set('Изображение принято. Оно появится в медиатеке после записи в хранилище.');
      await this.load(1);
    } catch (error) {
      this.error.set(mediaError(error));
      this.busy.set(false);
    }
  }
  preview(id: string): string {
    return `/api/media/images/${encodeURIComponent(id)}/480`;
  }
  previewFailed(id: string): void {
    this.failedPreviews.update((ids) => (ids.includes(id) ? ids : [...ids, id]));
  }
  async begin(action: string, target?: TemplateResponse, imageId = ''): Promise<void> {
    this.action.set(action);
    this.target.set(target ?? null);
    this.selectedImage.set(imageId);
    this.name = target?.name ?? '';
    this.reason = '';
    this.replacement = '';
    this.error.set('');
    this.actionDialog.nativeElement.showModal();
    if (action === 'submit' && target?.approval === 'Rejected') {
      try {
        this.replacements.set(
          (await this.api.images()).items.filter((image) => image.state === 'Ready'),
        );
      } catch (error) {
        this.error.set(mediaError(error));
      }
    }
  }
  close(): void {
    this.actionDialog.nativeElement.close();
    this.action.set('');
  }
  async submit(): Promise<void> {
    const nameLength = [...this.name.trim().normalize('NFC')].length;
    if (['propose', 'submit'].includes(this.action()) && (nameLength < 1 || nameLength > 100)) {
      this.error.set('Название шаблона должно содержать от 1 до 100 символов.');
      return;
    }
    if ([...this.reason.trim().normalize('NFC')].length > 2000) {
      this.error.set('Причина должна содержать не более 2000 символов.');
      return;
    }
    this.busy.set(true);
    this.error.set('');
    try {
      if (this.action() === 'propose') {
        const proposal = await this.api.propose(this.selectedImage(), this.name);
        this.target.set(proposal);
        this.action.set('submit');
        await this.api.act(proposal, 'submit');
      } else if (this.action() === 'removal') await this.api.requestRemoval(this.target()!.id);
      else
        await this.api.act(
          this.target()!,
          this.action(),
          this.reason,
          this.replacement || undefined,
          this.action() === 'submit' ? this.name : undefined,
        );
      this.notice.set('Действие сохранено.');
      this.close();
      await this.load();
    } catch (error) {
      this.error.set(mediaError(error));
      this.busy.set(false);
    }
  }
  async openHistory(template: TemplateResponse, page = 1): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const result = await this.api.history(template.id, page);
      this.history.set(result.items);
      this.historyId.set(template.id);
      this.historyTitle.set(template.name);
      this.historyPage.set(page);
      this.historyTotal.set(Number(result.total));
    } catch (error) {
      this.error.set(mediaError(error));
    } finally {
      this.busy.set(false);
    }
  }
  async moreHistory(page: number): Promise<void> {
    const template = this.templates().find((item) => item.id === this.historyId());
    if (template) await this.openHistory(template, page);
  }
  async resolve(request: RemovalRequestResponse): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.resolve(request);
      this.notice.set('Шаблон архивирован по запросу автора.');
      await this.load();
    } catch (error) {
      this.error.set(mediaError(error));
      this.busy.set(false);
    }
  }
  state(template: TemplateResponse): string {
    return [
      template.approval === 'Approved'
        ? 'Одобрено'
        : template.approval === 'Rejected'
          ? 'Отклонено'
          : template.submitted
            ? 'На проверке'
            : 'Не отправлено',
      ...(template.archived ? ['В архиве'] : []),
      ...(template.blocked ? ['Заблокировано'] : []),
    ].join(' · ');
  }
}
