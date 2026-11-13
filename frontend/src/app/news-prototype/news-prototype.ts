import {
  AfterViewInit,
  Component,
  ElementRef,
  HostListener,
  OnDestroy,
  ViewChild,
  computed,
  inject,
  isDevMode,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Editor, JSONContent } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { MediaFigure, initialDocument, renderDocument, safeUrl } from './document';
import { SiteMetadata } from '../site-metadata';
import { HomeTop } from './home-top';

@Component({
  selector: 'app-root',
  imports: [FormsModule, RouterLink, HomeTop],
  templateUrl: './news-prototype.html',
  styleUrl: './news-prototype.css',
})
export class NewsPrototype implements AfterViewInit, OnDestroy {
  readonly site = inject(SiteMetadata);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  readonly development = isDevMode();
  readonly variant = signal('A');
  readonly top = signal('A');
  readonly screen = signal('home');
  readonly tab = signal('text');
  readonly status = signal('Draft');
  readonly saveStatus = signal('Демо-черновик сохранён в памяти');
  readonly doc = signal<JSONContent>(structuredClone(initialDocument));
  readonly preview = computed(() => renderDocument(this.doc()));
  readonly json = computed(() => JSON.stringify({ schemaVersion: 1, body: this.doc() }, null, 2));
  readonly message = signal('');
  readonly query = signal('');
  readonly category = signal('');
  readonly tag = signal('');
  readonly dateFrom = signal('');
  readonly dateTo = signal('');
  readonly featuredIndex = signal(0);
  readonly featuredCount = signal(5);
  readonly latestPage = signal(1);
  title = 'У реки появилась новая прогулочная дорожка';
  summary =
    'На набережной открыли первый участок нового маршрута. Рассказываем, что здесь уже можно увидеть.';
  articleCategory = 'Город';
  tags = 'Набережная, Благоустройство';
  usesOthers = false;
  sourceName = '';
  sourceUrl = '';
  alt = 'Прогулочная дорожка вдоль реки с деревьями и скамейками';
  caption = 'Новый маршрут вдоль реки';
  seoTitle = '';
  seoDescription = '';
  linkUrl = 'https://example.org';
  snapshot: JSONContent | null = null;
  editor?: Editor;
  private saveTimer?: ReturnType<typeof setTimeout>;
  @ViewChild('editorHost') host!: ElementRef<HTMLElement>;
  readonly layoutNames = computed(() =>
    this.screen() === 'home'
      ? ({ A: 'Компактный портал', B: 'Городская газета', C: 'Город и поиск' } as Record<
          string,
          string
        >)
      : ({ A: 'Текст и настройки', B: 'Пошаговая форма', C: 'Текст и предпросмотр' } as Record<
          string,
          string
        >),
  );
  readonly articles = Array.from({ length: 46 }, (_, index) => ({
    id: index + 1,
    title: [
      'На набережной открыли новый прогулочный маршрут',
      'Библиотека приглашает на вечер городских историй',
      'Как изменится движение автобусов в выходные',
      'В сквере у вокзала высадили молодые липы',
      'Школьная команда выиграла городской турнир',
      'Музей откроет выставку старых фотографий',
    ][index % 6],
    summary: [
      'Два километра дорожек, места для отдыха и новый вид на реку.',
      'Встречи с краеведами и истории, которые хранят жители города.',
      'Собрали главное, чтобы было проще спланировать выходные.',
    ][index % 3],
    category: ['Город', 'Культура', 'Транспорт'][index % 3],
    tag: ['Благоустройство', 'Выходные', 'Маршруты'][index % 3],
    date: `2026-11-${String(13 - Math.floor(index / 4)).padStart(2, '0')}`,
    time: `${18 - (index % 4)}:30`,
    featured: index >= 3 && index < 8,
  }));
  readonly filtered = computed(() =>
    this.articles
      .slice(3)
      .filter(
        (article) =>
          (!this.query() ||
            `${article.title} ${article.summary}`
              .toLowerCase()
              .includes(this.query().toLowerCase())) &&
          (!this.category() || article.category === this.category()) &&
          (!this.tag() || article.tag === this.tag()) &&
          (!this.dateFrom() || article.date >= this.dateFrom()) &&
          (!this.dateTo() || article.date <= this.dateTo()),
      ),
  );
  readonly latest = computed(() =>
    this.filtered().slice((this.latestPage() - 1) * 20, this.latestPage() * 20),
  );
  readonly featured = computed(() =>
    this.articles.filter((a) => a.featured).slice(0, this.featuredCount()),
  );
  readonly recentCards = this.articles.slice(0, 3);
  moveFeatured(delta: number): void {
    this.featuredIndex.update(
      (index) => (index + delta + this.featured().length) % this.featured().length,
    );
  }
  readonly latestPages = computed(() =>
    Array.from({ length: Math.ceil(this.filtered().length / 20) }, (_, i) => i + 1),
  );

  constructor() {
    this.route.queryParamMap.subscribe((params) => {
      const count = Number(params.get('featured') ?? 5);
      this.featuredCount.set(Number.isInteger(count) && count >= 1 && count <= 5 ? count : 5);
      this.featuredIndex.set(0);
      this.screen.set(params.get('screen') === 'editor' ? 'editor' : 'home');
      this.variant.set(
        this.screen() === 'home'
          ? 'B'
          : ['A', 'B', 'C'].includes(params.get('variant') ?? '')
            ? params.get('variant')!
            : 'B',
      );
      this.top.set(['A', 'B', 'C'].includes(params.get('top') ?? '') ? params.get('top')! : 'A');
    });
  }

  ngAfterViewInit(): void {
    this.editor = new Editor({
      element: this.host.nativeElement,
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
            isAllowedUri: (url) => safeUrl(url),
          },
        }),
        MediaFigure,
      ],
      content: this.doc(),
      editorProps: {
        attributes: { 'aria-label': 'Текст статьи', role: 'textbox', 'aria-multiline': 'true' },
        // Conservative prototype: pasting rich HTML inserts only clipboard plain text.
        handlePaste: (view, event) => {
          const text = event.clipboardData?.getData('text/plain');
          if (text === undefined) return true;
          event.preventDefault();
          view.dispatch(view.state.tr.insertText(text));
          return true;
        },
        handleDrop: () => true,
      },
      onUpdate: ({ editor }) => {
        this.doc.set(editor.getJSON());
        this.changed();
      },
    });
  }

  ngOnDestroy(): void {
    clearTimeout(this.saveTimer);
    this.editor?.destroy();
  }
  switchVariant(delta: number): void {
    const keys = ['A', 'B', 'C'];
    const home = this.screen() === 'home';
    const next = keys[(keys.indexOf(home ? this.top() : this.variant()) + delta + 3) % 3];
    void this.router.navigate([], {
      queryParams: home ? { top: next, variant: 'B' } : { variant: next },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }
  searchNews(value: string): void {
    this.query.set(value);
    this.latestPage.set(1);
    document
      .getElementById('latest-heading')
      ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
  filterNews(value: string): void {
    this.category.set(value);
    this.latestPage.set(1);
    document
      .getElementById('latest-heading')
      ?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
  @HostListener('document:keydown', ['$event'])
  key(event: KeyboardEvent): void {
    const target = event.target as HTMLElement;
    if (target.closest('input, textarea, select, [contenteditable="true"]')) return;
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
      event.preventDefault();
      this.switchVariant(event.key === 'ArrowLeft' ? -1 : 1);
    }
  }
  changed(): void {
    clearTimeout(this.saveTimer);
    this.saveStatus.set(
      this.status() === 'Approved'
        ? 'Изменения отправятся на проверку при сохранении'
        : 'Есть несохранённые изменения',
    );
    if (this.status() === 'Draft') this.saveTimer = setTimeout(() => this.save(), 5000);
  }
  save(): void {
    clearTimeout(this.saveTimer);
    if (this.status() === 'Approved') {
      this.submit();
      return;
    }
    this.snapshot = structuredClone(this.doc());
    this.saveStatus.set('Сохранено в памяти · ' + new Date().toLocaleTimeString('ru-RU'));
  }
  restore(): void {
    if (!this.snapshot) this.save();
    this.editor?.commands.setContent(structuredClone(this.snapshot!), { emitUpdate: false });
    this.doc.set(this.editor!.getJSON());
    this.message.set(
      JSON.stringify(this.snapshot) === JSON.stringify(this.doc())
        ? 'Структура восстановлена без изменений.'
        : 'Структура изменилась при восстановлении.',
    );
  }
  format(command: string): void {
    const chain = this.editor?.chain().focus();
    if (!chain) return;
    if (command === 'bold') chain.toggleBold().run();
    if (command === 'italic') chain.toggleItalic().run();
    if (command === 'heading') chain.toggleHeading({ level: 2 }).run();
    if (command === 'bullet') chain.toggleBulletList().run();
    if (command === 'ordered') chain.toggleOrderedList().run();
    if (command === 'quote') chain.toggleBlockquote().run();
    if (command === 'paragraph') chain.setParagraph().run();
  }
  addLink(): void {
    if (!safeUrl(this.linkUrl)) {
      this.message.set('Для ссылки нужен полный адрес http:// или https://.');
      return;
    }
    this.editor
      ?.chain()
      .focus()
      .insertContent({
        type: 'text',
        text: 'Источник',
        marks: [{ type: 'link', attrs: { href: this.linkUrl } }],
      })
      .run();
  }
  addImage(): void {
    if (!this.alt.trim()) {
      this.message.set('Опиши изображение для экранного диктора.');
      return;
    }
    this.editor
      ?.chain()
      .focus()
      .insertContent({
        type: 'mediaFigure',
        attrs: { mediaId: 'demo-park', alt: this.alt, caption: this.caption },
      })
      .run();
    this.message.set('Добавлено демо-изображение из медиатеки.');
  }
  errors(): string[] {
    const errors: string[] = [];
    if (!this.title.trim() || Array.from(this.title.normalize('NFC')).length > 200)
      errors.push('Заголовок: от 1 до 200 символов.');
    if (!this.summary.trim() || Array.from(this.summary.normalize('NFC')).length > 500)
      errors.push('Краткое описание: от 1 до 500 символов.');
    if (!this.articleCategory) errors.push('Выбери категорию.');
    const text = this.editor?.getText() ?? '';
    if (!text.trim() || Array.from(text.normalize('NFC')).length > 100000)
      errors.push('Текст: от 1 до 100 000 символов.');
    if (this.tags.split(',').filter((t) => t.trim()).length > 10)
      errors.push('Допускается до 10 тегов.');
    if (this.usesOthers && (!this.sourceName.trim() || !safeUrl(this.sourceUrl)))
      errors.push('Укажи название и http(s)-адрес источника.');
    if (
      (this.doc().content ?? []).some((n) => n.type === 'mediaFigure' && !n.attrs?.['alt']?.trim())
    )
      errors.push('Для каждого изображения нужен альтернативный текст.');
    return errors;
  }
  submit(): void {
    const errors = this.errors();
    if (errors.length) {
      this.message.set(errors.join(' '));
      return;
    }
    this.saveStatus.set('Демо-версия отправлена в общую очередь');
    this.status.set('InReview');
    this.editor?.setEditable(false);
    clearTimeout(this.saveTimer);
    this.message.set('Статья на проверке. Редактирование заблокировано.');
  }
  scenario(value: string): void {
    clearTimeout(this.saveTimer);
    this.status.set(value);
    this.editor?.setEditable(value !== 'InReview');
    this.message.set('');
    this.saveStatus.set(
      value === 'Approved'
        ? 'Одобрено · автосохранение отключено'
        : value === 'InReview'
          ? 'Версия отправлена на проверку'
          : 'Черновик · автосохранение через 5 секунд',
    );
  }
  openArticle(title: string): void {
    this.message.set('Предпросмотр демо-новости: ' + title);
  }
}
