import { HttpErrorResponse } from '@angular/common/http';
import {
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Editor, JSONContent } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { AccountApi, AccountProfile } from '../account/account-api';
import { MediaApi, mediaError as describeMediaError } from '../media/media-api';
import { CityClock } from '../staff/city-clock';
import {
  NewsApi,
  ArticleContent,
  ArticleImage,
  ArticleResponse,
  ArticleSummaryResponse,
  TaxonomyResponse,
  newsError,
  ArticleHistoryResponse,
} from './news-api';
import { articleState, reviewAction } from './workflow-labels';
import {
  ArticleMediaFigure,
  blankBody,
  renderArticleBody,
  safeArticleUrl,
} from './article-document';

type Draft = Required<Omit<ArticleContent, 'body' | 'schemaVersion'>> & {
  body: JSONContent;
  schemaVersion: number;
};
const draft = (content: ArticleContent = {}): Draft => ({
  title: content.title ?? '',
  summary: content.summary ?? '',
  body: content.body ?? blankBody(),
  schemaVersion: 1,
  categoryId: content.categoryId ?? null,
  tagIds: content.tagIds ?? [],
  cover: content.cover ?? null,
  gallery: content.gallery ?? [],
  sources: content.sources ?? [],
  usesOthers: content.usesOthers ?? false,
  seoTitle: content.seoTitle ?? '',
  seoDescription: content.seoDescription ?? '',
});

@Component({
  imports: [FormsModule, RouterLink],
  selector: 'app-author-workspace',
  styleUrl: './author-workspace.css',
  templateUrl: './author-workspace.html',
})
export class AuthorWorkspace implements OnInit, OnDestroy {
  readonly safeArticleUrl = safeArticleUrl;
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  constructor() {
    this.host.nativeElement.addEventListener('error', this.imageError, true);
  }
  private readonly api = inject(NewsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  readonly correctionMode = this.route.snapshot.data['correction'] === true;
  private readonly accountApi = inject(AccountApi);
  private readonly media = inject(MediaApi);
  protected readonly clock = inject(CityClock);
  readonly profile = signal<AccountProfile | null>(null);
  readonly allowed = computed(
    () =>
      !!this.profile()?.emailConfirmed &&
      (this.correctionMode
        ? !!this.profile()?.roles.some((role) => role === 'Editor' || role === 'Administrator')
        : !!this.profile()?.roles.includes('Author')),
  );
  readonly article = signal<ArticleResponse | null>(null);
  readonly editable = computed(() =>
    this.correctionMode
      ? this.article()?.publicationState === 'Published'
      : ['Draft', 'Approved'].includes(this.article()?.state ?? ''),
  );
  readonly history = signal<ArticleHistoryResponse[]>([]);
  readonly historyPage = signal(1);
  readonly historyTotal = signal(0);
  readonly historySnapshot = signal<ArticleHistoryResponse | null>(null);
  readonly stateName = articleState;
  readonly actionName = reviewAction;
  taxonomyName(id: string | null | undefined): string {
    return (
      [...this.categories(), ...this.tags()].find((item) => item.id === id)?.name ??
      id ??
      'Не выбрана'
    );
  }
  changeDescription = '';
  readonly form = signal<Draft>(draft());
  readonly rows = signal<ArticleSummaryResponse[]>([]);
  readonly categories = signal<TaxonomyResponse[]>([]);
  readonly tags = signal<TaxonomyResponse[]>([]);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly busy = signal(false);
  readonly saving = signal(false);
  readonly dirty = signal(false);
  readonly conflict = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly copyBuffer = signal('');
  readonly tab = signal('text');
  readonly preview = computed(() => renderArticleBody(this.form().body));
  readonly inline = signal<{ mediaId: string; alt: string; caption: string } | null>(null);
  readonly choices = signal<{ id: string; name: string; ready: boolean }[]>([]);
  readonly mediaPage = signal(1);
  readonly mediaTotal = signal(0);
  readonly mediaScope = signal('own');
  readonly pickerTarget = signal('cover');
  readonly mediaBusy = signal(false);
  readonly mediaNotice = signal('');
  readonly mediaError = signal('');
  query = '';
  linkUrl = '';
  editor?: Editor;
  @ViewChild('mediaDialog') mediaDialog!: ElementRef<HTMLDialogElement>;
  private timer?: ReturnType<typeof setTimeout>;
  private savePromise?: Promise<void>;
  private edits = 0;
  private changedAt = 0;
  private failed = false;
  private destroyed = false;
  readonly tabs = [
    { id: 'text', name: 'Текст' },
    { id: 'images', name: 'Изображения' },
    { id: 'parameters', name: 'Параметры' },
    { id: 'preview', name: 'Предпросмотр' },
    { id: 'history', name: 'История' },
  ];

  @ViewChild('bodyHost') set bodyHost(host: ElementRef<HTMLElement> | undefined) {
    this.editor?.destroy();
    this.editor = undefined;
    if (!host) return;
    host.nativeElement.addEventListener('error', this.imageError, true);
    this.editor = new Editor({
      editable: this.editable(),
      element: host.nativeElement,
      extensions: [
        StarterKit.configure({
          heading: { levels: [2, 3] },
          code: false,
          codeBlock: false,
          horizontalRule: false,
          strike: false,
          underline: false,
          link: {
            openOnClick: false,
            autolink: false,
            linkOnPaste: false,
            isAllowedUri: safeArticleUrl,
          },
        }),
        ArticleMediaFigure,
      ],
      content: this.form().body,
      editorProps: {
        attributes: { 'aria-label': 'Текст статьи', role: 'textbox', 'aria-multiline': 'true' },
        handlePaste: (view, event) => {
          event.preventDefault();
          view.dispatch(view.state.tr.insertText(event.clipboardData?.getData('text/plain') ?? ''));
          return true;
        },
        handleDrop: () => true,
      },
      onUpdate: ({ editor }) => {
        this.form.update((value) => ({ ...value, body: editor.getJSON() }));
        this.changed();
      },
      onSelectionUpdate: ({ editor }) => {
        const attrs = editor.isActive('mediaFigure') ? editor.getAttributes('mediaFigure') : null;
        this.inline.set(
          attrs
            ? {
                mediaId: attrs['mediaId'],
                alt: attrs['alt'] ?? '',
                caption: attrs['caption'] ?? '',
              }
            : null,
        );
      },
    });
  }
  @ViewChild('previewHost') set previewHost(host: ElementRef<HTMLElement> | undefined) {
    host?.nativeElement.addEventListener('error', this.imageError, true);
  }
  private imageError = (event: Event): void => {
    const image = event.target;
    if (image instanceof HTMLImageElement && !image.hidden) {
      image.hidden = true;
      const placeholder = document.createElement('span');
      placeholder.className = 'image-placeholder';
      placeholder.textContent = 'Изображение недоступно';
      image.after(placeholder);
    }
  };

  ngOnInit(): void {
    void this.initialize();
  }
  ngOnDestroy(): void {
    this.host.nativeElement.removeEventListener('error', this.imageError, true);
    this.destroyed = true;
    this.cancelTimer();
    this.editor?.destroy();
  }
  @HostListener('window:beforeunload', ['$event']) beforeUnload(event: BeforeUnloadEvent): void {
    if (this.dirty()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }
  canLeave(): boolean {
    return (
      !this.saving() &&
      (!this.dirty() || window.confirm('Есть несохранённые правки. Закрыть статью?'))
    );
  }
  private cancelTimer(): void {
    if (this.timer) clearTimeout(this.timer);
    this.timer = undefined;
  }
  private armSave(): void {
    this.cancelTimer();
    if (
      !this.destroyed &&
      !this.conflict() &&
      !this.failed &&
      !this.busy() &&
      this.dirty() &&
      this.article()?.state === 'Draft' &&
      !this.correctionMode
    )
      this.timer = setTimeout(
        () => void this.save(),
        Math.max(0, this.changedAt + 5000 - Date.now()),
      );
  }
  changed(): void {
    if (!this.editable()) return;
    this.edits++;
    this.changedAt = Date.now();
    this.failed = false;
    this.dirty.set(true);
    this.notice.set('');
    this.armSave();
  }
  async initialize(): Promise<void> {
    this.busy.set(true);
    try {
      this.profile.set(await this.accountApi.profile());
      if (this.allowed()) {
        const [categories, tags] = await Promise.all([
          this.api.taxonomy('categories'),
          this.api.taxonomy('tags'),
        ]);
        this.categories.set(categories);
        this.tags.set(tags);
        if (this.correctionMode)
          this.accept(await this.api.publication(this.route.snapshot.paramMap.get('id')!));
        else {
          await this.load();
          const articleId = this.route.snapshot.queryParamMap?.get('articleId');
          if (articleId) this.accept(await this.api.get(articleId));
        }
      } else
        this.error.set(
          this.correctionMode
            ? 'Для исправления публикаций нужны права редактора и подтверждённый Email.'
            : 'Для работы со статьями нужна роль автора и подтверждённый Email.',
        );
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  async load(page = 1): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const result = await this.api.list(page, this.query);
      this.rows.set(result.items);
      this.page.set(Number(result.page));
      this.total.set(Number(result.total));
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  private accept(article: ArticleResponse): void {
    this.cancelTimer();
    this.article.set(article);
    this.editor?.setEditable(this.editable(), false);
    this.history.set([]);
    this.historySnapshot.set(null);
    this.form.set(draft(article.content));
    this.dirty.set(false);
    this.conflict.set(false);
    this.failed = false;
    this.copyBuffer.set('');
    this.error.set('');
    this.notice.set('');
    this.tab.set('text');
    this.editor?.commands.setContent(this.form().body, { emitUpdate: false });
  }
  async open(id: string): Promise<void> {
    if (!this.canLeave()) return;
    this.busy.set(true);
    this.cancelTimer();
    try {
      this.accept(await this.api.get(id));
    } catch (error) {
      this.error.set(newsError(error));
      this.armSave();
    } finally {
      this.busy.set(false);
    }
  }
  async create(): Promise<void> {
    if (!this.canLeave()) return;
    this.busy.set(true);
    this.cancelTimer();
    try {
      this.accept(await this.api.create());
    } catch (error) {
      this.error.set(newsError(error));
      this.armSave();
    } finally {
      this.busy.set(false);
    }
  }
  async back(): Promise<void> {
    if (!this.canLeave()) return;
    this.cancelTimer();
    this.dirty.set(false);
    this.article.set(null);
    if (this.correctionMode) await this.router.navigateByUrl('/staff/publications');
    else await this.load();
  }
  save(): Promise<void> {
    this.cancelTimer();
    if (this.savePromise) return this.savePromise;
    const article = this.article();
    if (!article || !this.editable() || this.conflict()) return Promise.resolve();
    if (this.correctionMode && !this.changeDescription.trim()) {
      this.error.set('Укажи причину редакторского исправления.');
      return Promise.resolve();
    }
    if (
      this.correctionMode &&
      !window.confirm(
        'Опубликовать исправления? Рабочая версия автора будет закрыта с сохранением текста.',
      )
    )
      return Promise.resolve();
    this.saving.set(true);
    this.error.set('');
    this.failed = false;
    const generation = this.edits;
    const snapshot = structuredClone(this.form());
    this.savePromise = (
      this.correctionMode
        ? this.api.correct(article.id, article.version, snapshot, this.changeDescription)
        : this.api.save(article.id, article.version, snapshot)
    )
      .then((saved) => {
        this.article.set(saved);
        this.editor?.setEditable(this.editable(), false);
        if (this.edits !== generation && (saved.state !== 'Draft' || this.correctionMode)) {
          this.copyBuffer.set(JSON.stringify(this.form(), null, 2));
          this.conflict.set(true);
        }
        if (this.edits === generation) {
          this.form.set(draft(saved.content));
          this.dirty.set(false);
        }
        this.notice.set(
          this.edits === generation
            ? this.correctionMode
              ? 'Исправления опубликованы.'
              : 'Черновик сохранён.'
            : '',
        );
      })
      .catch((error: unknown) => {
        this.failed = true;
        this.conflict.set(error instanceof HttpErrorResponse && error.status === 409);
        this.error.set(newsError(error));
        if (this.conflict()) this.copyBuffer.set(JSON.stringify(this.form(), null, 2));
      })
      .finally(() => {
        this.saving.set(false);
        this.savePromise = undefined;
        this.armSave();
      });
    return this.savePromise;
  }
  async workflow(operation: string): Promise<void> {
    if (this.busy() || this.conflict()) return;
    if (operation === 'publish' && !window.confirm('Опубликовать одобренную версию?')) return;
    this.error.set('');
    if (this.dirty()) await this.save();
    const article = this.article();
    if (!article || this.dirty() || this.conflict() || this.error()) return;
    this.cancelTimer();
    this.busy.set(true);
    this.editor?.setEditable(false, false);
    try {
      this.accept(
        await this.api.act(article.id, article.version, operation, this.changeDescription),
      );
      this.changeDescription = '';
      this.notice.set('Действие сохранено.');
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
      this.editor?.setEditable(this.editable(), false);
    }
  }
  async openPublic(): Promise<void> {
    const article = this.article();
    if (!article || !this.canLeave()) return;
    try {
      if (article.publicAddress) await this.router.navigateByUrl(article.publicAddress);
    } catch (error) {
      this.error.set(newsError(error));
    }
  }
  async selectTab(id: string): Promise<void> {
    this.tab.set(id);
    if (id === 'history') await this.loadHistory();
  }
  async loadHistory(page = 1): Promise<void> {
    const article = this.article();
    if (!article) return;
    try {
      const result = await this.api.history(article.id, page);
      this.history.set(result.items);
      this.historyTotal.set(Number(result.total));
      this.historyPage.set(page);
    } catch (error) {
      this.error.set(newsError(error));
    }
  }
  async reloadCurrent(): Promise<void> {
    if (
      this.dirty() &&
      !window.confirm('Загрузить серверную версию? Сначала скопируй несохранённые правки ниже.')
    )
      return;
    const article = this.article();
    if (!article) return;
    const generation = this.edits;
    this.busy.set(true);
    try {
      const current = await (this.correctionMode
        ? this.api.publication(article.id)
        : this.api.get(article.id));
      if (generation !== this.edits) {
        this.copyBuffer.set(JSON.stringify(this.form(), null, 2));
        this.error.set(
          'Пока загружалась версия, появились новые правки. Они остались в форме. Скопируй их перед повторной загрузкой.',
        );
        return;
      }
      this.accept(current);
    } catch (error) {
      this.error.set(newsError(error));
    } finally {
      this.busy.set(false);
    }
  }
  async copyEdits(): Promise<void> {
    const text = JSON.stringify(this.form(), null, 2);
    this.copyBuffer.set(text);
    try {
      await navigator.clipboard.writeText(text);
      this.notice.set('Копия правок сохранена в буфере обмена.');
    } catch {
      this.notice.set('Выдели и скопируй правки из поля ниже.');
    }
  }
  async remove(): Promise<void> {
    const article = this.article();
    if (
      !article ||
      this.saving() ||
      !window.confirm('Удалить статью из работы? Восстановить её через личный кабинет нельзя.')
    )
      return;
    this.busy.set(true);
    this.cancelTimer();
    try {
      await this.api.delete(article.id, article.version);
      this.dirty.set(false);
      this.article.set(null);
      await this.load();
      this.notice.set('Статья удалена из работы.');
    } catch (error) {
      this.error.set(newsError(error));
      this.armSave();
    } finally {
      this.busy.set(false);
    }
  }
  toggleTag(id: string, checked: boolean): void {
    this.form.update((value) => ({
      ...value,
      tagIds: checked ? [...value.tagIds, id] : value.tagIds.filter((tag) => tag !== id),
    }));
    this.changed();
  }
  source(): void {
    this.form.update((value) => ({ ...value, sources: [...value.sources, { name: '', url: '' }] }));
    this.changed();
  }
  removeSource(index: number): void {
    this.form.update((value) => ({
      ...value,
      sources: value.sources.filter((_, i) => i !== index),
    }));
    this.changed();
  }
  removeImage(index: number): void {
    this.form.update((value) => ({
      ...value,
      gallery: value.gallery.filter((_, i) => i !== index),
    }));
    this.changed();
  }
  setInline(field: 'alt' | 'caption', value: string): void {
    const image = this.inline();
    if (image) {
      this.editor?.commands.updateAttributes('mediaFigure', { [field]: value });
      this.inline.set({ ...image, [field]: value });
    }
  }
  link(): void {
    if (!safeArticleUrl(this.linkUrl)) {
      this.error.set('Укажи абсолютную ссылку HTTP или HTTPS.');
      return;
    }
    this.editor?.chain().focus().setLink({ href: this.linkUrl }).run();
    this.linkUrl = '';
  }
  async pick(target: string): Promise<void> {
    this.pickerTarget.set(target);
    this.mediaNotice.set('');
    this.mediaDialog.nativeElement.showModal();
    await this.loadMedia('own', 1);
  }
  async loadMedia(scope = this.mediaScope(), page = 1): Promise<void> {
    this.mediaBusy.set(true);
    this.mediaError.set('');
    try {
      const result =
        scope === 'own'
          ? await this.media.images(page)
          : await this.media.templates('library', page);
      this.choices.set(
        result.items.map((item) =>
          'imageId' in item
            ? { id: item.imageId, name: item.name, ready: true }
            : { id: item.id, name: item.name, ready: item.state === 'Ready' },
        ),
      );
      this.mediaTotal.set(Number(result.total));
      this.mediaPage.set(page);
      this.mediaScope.set(scope);
    } catch (error) {
      this.mediaError.set(describeMediaError(error));
    } finally {
      this.mediaBusy.set(false);
    }
  }
  choose(id: string): void {
    const image: ArticleImage = { mediaId: id, alt: '', caption: '' };
    if (this.pickerTarget() === 'inline')
      this.editor?.chain().focus().insertContent({ type: 'mediaFigure', attrs: image }).run();
    else {
      this.form.update((value) =>
        this.pickerTarget() === 'cover'
          ? { ...value, cover: image }
          : { ...value, gallery: [...value.gallery, image] },
      );
      this.changed();
    }
    this.mediaDialog.nativeElement.close();
  }
  async upload(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.mediaBusy.set(true);
    this.mediaError.set('');
    try {
      await this.media.upload(file);
      this.mediaNotice.set('Файл принят. После обработки нажми «Обновить», чтобы выбрать его.');
      await this.loadMedia('own', 1);
    } catch (error) {
      this.mediaError.set(describeMediaError(error));
    } finally {
      this.mediaBusy.set(false);
      input.value = '';
    }
  }
}
