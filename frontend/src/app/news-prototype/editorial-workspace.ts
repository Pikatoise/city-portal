import {
  AfterViewInit,
  Component,
  ElementRef,
  OnDestroy,
  ViewChild,
  computed,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Editor, JSONContent } from '@tiptap/core';
import StarterKit from '@tiptap/starter-kit';
import { MediaFigure, initialDocument, renderDocument, safeUrl } from './document';

type State =
  'Draft' | 'InReview' | 'Returned' | 'Rejected' | 'Approved' | 'Published' | 'Withdrawn';
type Placement = { id: string; alt: string; caption: string };
type Source = { name: string; url: string };
type Entry = {
  action: string;
  actor: string;
  time: string;
  note: string;
  body: JSONContent;
  content?: Content;
};
type Article = {
  id: number;
  title: string;
  summary: string;
  category: string;
  tags: string[];
  body: JSONContent;
  state: State;
  reviewer: string;
  previousReviewer: string;
  submitted: boolean;
  published: boolean;
  working: boolean;
  deleted: boolean;
  cover: Placement | null;
  gallery: Placement[];
  sources: Source[];
  usesOthers: boolean;
  seoTitle: string;
  seoDescription: string;
  history: Entry[];
};
type Content = Pick<
  Article,
  | 'title'
  | 'summary'
  | 'category'
  | 'tags'
  | 'body'
  | 'cover'
  | 'gallery'
  | 'sources'
  | 'usesOthers'
  | 'seoTitle'
  | 'seoDescription'
>;
function copyContent(a: Content): Content {
  const {
    title,
    summary,
    category,
    tags,
    body,
    cover,
    gallery,
    sources,
    usesOthers,
    seoTitle,
    seoDescription,
  } = a;
  return structuredClone({
    title,
    summary,
    category,
    tags,
    body,
    cover,
    gallery,
    sources,
    usesOthers,
    seoTitle,
    seoDescription,
  });
}

@Component({
  imports: [FormsModule],
  selector: 'app-editorial-workspace',
  styleUrl: './editorial-workspace.css',
  templateUrl: './editorial-workspace.html',
})
// In-memory review surface; no article or media API mutations.
export class EditorialWorkspace implements AfterViewInit, OnDestroy {
  @ViewChild('bodyHost') host!: ElementRef<HTMLElement>;
  @ViewChild('mediaDialog') mediaDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('decisionDialog') decisionDialog!: ElementRef<HTMLDialogElement>;
  readonly persona = signal<'author' | 'editor'>('author');
  readonly area = signal<'article' | 'articles' | 'queue'>('article');
  readonly tab = signal('text');
  readonly selected = signal(1);
  readonly revision = signal(0);
  readonly saveStatus = signal('Демо-версия сохранена в памяти');
  readonly notice = signal('');
  readonly problems = signal<string[]>([]);
  readonly dirty = signal(false);
  readonly picker = signal<'cover' | 'gallery' | 'body' | null>(null);
  readonly library = signal('own');
  readonly selectedMedia = signal('demo-park');
  readonly decision = signal('');
  readonly previewMobile = signal(false);
  readonly inspected = signal<Entry | null>(null);
  readonly inlineImage = signal<Placement | null>(null);
  readonly activeFormats = signal<string[]>([]);
  private readonly submittedCopies = new Map<number, Content>();
  private readonly savedCopies = new Map<number, Content>();
  private readonly publicCopies = new Map<number, Content>();
  private readonly editorBuffer = signal<Content | null>(null);
  readonly editingPublished = computed(() => this.persona() === 'editor' && !!this.editorBuffer());
  readonly doc = signal<JSONContent>(structuredClone(initialDocument));
  readonly rendered = computed(() => renderDocument(this.doc()));
  readonly textLength = computed(() => {
    const text = (n: JSONContent): string => (n.text ?? '') + (n.content ?? []).map(text).join('');
    return Array.from(text(this.doc()).normalize('NFC')).length;
  });
  readonly hasBodyText = computed(() => {
    const text = (n: JSONContent): string => (n.text ?? '') + (n.content ?? []).map(text).join('');
    return !!text(this.doc()).trim();
  });
  readonly labels: Record<State, string> = {
    Draft: 'Черновик',
    InReview: 'На проверке',
    Returned: 'На доработке',
    Rejected: 'Отклонено',
    Approved: 'Одобрено',
    Published: 'Опубликовано',
    Withdrawn: 'Снято с публикации',
  };
  readonly tabs = [
    { id: 'text', name: 'Текст' },
    { id: 'media', name: 'Изображения' },
    { id: 'settings', name: 'Параметры' },
    { id: 'preview', name: 'Предпросмотр' },
    { id: 'history', name: 'История' },
  ];
  readonly media = [
    {
      id: 'demo-park',
      name: 'Набережная у реки',
      type: 'own',
      detail: 'Моя загрузка · 1600 × 1067',
    },
    {
      id: 'demo-street',
      name: 'Улица в центре',
      type: 'own',
      detail: 'Моя загрузка · 1800 × 1200',
    },
    {
      id: 'demo-library',
      name: 'Городская библиотека',
      type: 'shared',
      detail: 'Общий шаблон · одобрен',
    },
  ];
  readonly articles: Article[] = [
    this.seed(1, 'Draft'),
    this.seed(2, 'InReview'),
    this.seed(3, 'Approved'),
    this.seed(4, 'Published'),
    this.seed(5, 'Returned'),
  ];
  readonly article = computed(() => {
    this.revision();
    return this.articles.find((a) => a.id === this.selected())!;
  });
  readonly content = computed(() => {
    this.revision();
    const a = this.article();
    if (this.persona() === 'author') return a;
    return this.editorBuffer() ?? this.submittedCopies.get(a.id) ?? a;
  });
  readonly canReadHistory = computed(() => {
    this.revision();
    return this.persona() === 'author' || this.article().reviewer === 'Илья Морозов';
  });
  readonly visibleArticles = computed(() => {
    this.revision();
    return this.articles.filter(
      (a) =>
        !a.deleted &&
        (this.persona() === 'author' ||
          a.state === 'InReview' ||
          a.state === 'Approved' ||
          (a.submitted && a.reviewer === 'Илья Морозов') ||
          a.published),
    );
  });
  readonly queue = computed(() => this.visibleArticles().filter((a) => a.state === 'InReview'));
  readonly editable = computed(() => {
    this.revision();
    const a = this.article();
    if (this.persona() === 'editor') return !!this.editorBuffer() && a.state !== 'Withdrawn';
    return (
      ['Draft', 'Returned', 'Approved'].includes(a.state) || (a.state === 'Withdrawn' && a.working)
    );
  });
  readonly checklist = computed(() => {
    this.revision();
    const a = this.article();
    return [
      { label: 'Заголовок и описание', ok: !!a.title.trim() && !!a.summary.trim(), tab: 'text' },
      { label: 'Текст статьи', ok: this.hasBodyText(), tab: 'text' },
      { label: 'Категория', ok: !!a.category, tab: 'settings' },
      {
        label: 'Описания изображений',
        ok: this.placements().every((p) => !!p.alt.trim()),
        tab: 'media',
      },
      {
        label: 'Источники',
        ok: !a.usesOthers || a.sources.some((s) => !!s.name.trim() && safeUrl(s.url)),
        tab: 'settings',
      },
    ];
  });
  query = '';
  stateFilter = '';
  mediaAlt = '';
  mediaCaption = '';
  linkUrl = '';
  linkOpen = false;
  feedback = '';
  changeNote = '';
  transferTo = 'Мария Соколова';
  private editor?: Editor;
  private saveTimer?: ReturnType<typeof setTimeout>;
  private seed(id: number, state: State): Article {
    const submitted = state !== 'Draft';
    const a: Article = {
      id,
      title: [
        'У реки появилась новая прогулочная дорожка',
        'Как изменятся автобусные маршруты',
        'Библиотека приглашает на вечер историй',
        'В городском сквере высадили молодые липы',
        'В музее готовят выставку фотографий',
      ][id - 1],
      summary: 'Новые места, полезные подробности и истории жителей Белогорска.',
      category: ['Город', 'Транспорт', 'Культура', 'Город', 'Культура'][id - 1],
      tags: ['Благоустройство'],
      body: structuredClone(initialDocument),
      state,
      reviewer: state === 'Returned' ? 'Илья Морозов' : '',
      previousReviewer: submitted ? 'Илья Морозов' : '',
      submitted,
      published: state === 'Published',
      working: !['Published', 'Rejected'].includes(state),
      deleted: false,
      cover: {
        id: 'demo-park',
        alt: 'Дорожка вдоль реки с деревьями и скамейками',
        caption: 'Новая прогулочная дорожка',
      },
      gallery: [],
      sources: [],
      usesOthers: false,
      seoTitle: '',
      seoDescription: '',
      history: submitted
        ? [
            {
              action: state === 'Returned' ? 'Возвращено на доработку' : 'Отправлено на проверку',
              actor: state === 'Returned' ? 'Илья Морозов' : 'Анна Лесная',
              time: '14 ноября, 12:30',
              note:
                state === 'Returned'
                  ? 'Уточните дату открытия и добавьте ссылку на первоисточник.'
                  : '',
              body: structuredClone(initialDocument),
            },
          ]
        : [],
    };
    if (submitted) this.submittedCopies.set(id, copyContent(a));
    this.savedCopies.set(id, copyContent(a));
    if (a.published) this.publicCopies.set(id, copyContent(a));
    a.history.forEach((entry) => (entry.content = copyContent(a)));
    return a;
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
          link: { openOnClick: false, autolink: false, linkOnPaste: false, isAllowedUri: safeUrl },
        }),
        MediaFigure,
      ],
      content: this.doc(),
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
        this.doc.set(editor.getJSON());
        this.content().body = structuredClone(this.doc());
        this.changed();
      },
      onSelectionUpdate: ({ editor }) => {
        const attrs = editor.isActive('mediaFigure') ? editor.getAttributes('mediaFigure') : null;
        this.inlineImage.set(
          attrs ? { id: attrs['mediaId'], alt: attrs['alt'], caption: attrs['caption'] } : null,
        );
      },
      onTransaction: ({ editor }) => {
        this.activeFormats.set(
          ['bold', 'italic', 'bulletList', 'orderedList', 'blockquote'].filter((name) =>
            editor.isActive(name),
          ),
        );
      },
    });
  }
  ngOnDestroy(): void {
    clearTimeout(this.saveTimer);
    this.editor?.destroy();
  }
  touch(): void {
    this.revision.update((n) => n + 1);
  }
  changed(): void {
    if (!this.editable()) return;
    this.touch();
    this.dirty.set(true);
    clearTimeout(this.saveTimer);
    this.saveStatus.set(
      this.article().state === 'Approved'
        ? 'Правки отправятся на проверку при сохранении'
        : 'Есть несохранённые изменения',
    );
    if (this.persona() === 'author' && this.article().state !== 'Approved')
      this.saveTimer = setTimeout(() => this.save(), 5000);
  }
  save(): void {
    if (!this.editable()) return;
    if (this.article().state === 'Approved' && this.persona() === 'author') {
      this.submit();
      return;
    }
    clearTimeout(this.saveTimer);
    this.savedCopies.set(this.article().id, copyContent(this.content()));
    this.dirty.set(false);
    this.saveStatus.set(
      'Сохранено в памяти · ' +
        new Date().toLocaleTimeString('ru-RU', { hour: '2-digit', minute: '2-digit' }),
    );
  }
  open(id: number): void {
    if (this.dirty()) {
      this.notice.set(
        'Сначала сохрани текущие правки. У одобренной версии сохранение сразу запускает проверку.',
      );
      return;
    }
    clearTimeout(this.saveTimer);
    this.selected.set(id);
    this.area.set('article');
    this.tab.set('text');
    const a = this.article();
    this.editorBuffer.set(
      this.persona() === 'editor' &&
        a.published &&
        !['Withdrawn', 'InReview', 'Approved'].includes(a.state)
        ? copyContent(this.publicCopies.get(a.id)!)
        : null,
    );
    this.doc.set(structuredClone(this.content().body));
    this.editor?.commands.setContent(this.doc(), { emitUpdate: false });
    this.editor?.setEditable(this.editable(), false);
    this.problems.set([]);
    this.decision.set('');
    this.inspected.set(null);
    this.saveStatus.set('Демо-версия сохранена в памяти');
  }
  discard(): void {
    clearTimeout(this.saveTimer);
    this.dirty.set(false);
    if (this.persona() === 'author')
      Object.assign(this.article(), copyContent(this.savedCopies.get(this.selected())!));
    this.touch();
    this.open(this.selected());
  }
  setPersona(value: 'author' | 'editor'): void {
    if (this.dirty()) {
      this.notice.set('Сохрани правки перед сменой демонстрационного режима.');
      return;
    }
    this.persona.set(value);
    this.editorBuffer.set(null);
    this.area.set(value === 'editor' ? 'queue' : 'articles');
    this.editor?.setEditable(this.editable(), false);
    this.decision.set('');
    this.picker.set(null);
  }
  startCorrection(): void {
    const a = this.article();
    if (this.persona() !== 'editor' || !a.published || a.state === 'Withdrawn') return;
    this.editorBuffer.set(copyContent(this.publicCopies.get(a.id)!));
    this.doc.set(structuredClone(this.content().body));
    this.editor?.commands.setContent(this.doc(), { emitUpdate: false });
    this.editor?.setEditable(true, false);
    this.tab.set('text');
  }
  newArticle(): void {
    if (this.dirty()) {
      this.notice.set('Сохрани текущие правки перед созданием статьи.');
      return;
    }
    const id = Math.max(...this.articles.map((a) => a.id)) + 1;
    const a = this.seed(id, 'Draft');
    Object.assign(a, {
      id,
      title: '',
      summary: '',
      category: '',
      tags: [],
      cover: null,
      body: { type: 'doc', content: [{ type: 'paragraph' }] },
    });
    this.articles.push(a);
    this.savedCopies.set(id, copyContent(a));
    this.touch();
    this.open(id);
  }
  rows(): Article[] {
    return (this.area() === 'queue' ? this.queue() : this.visibleArticles())
      .map((a) => {
        const snapshot =
          this.persona() === 'editor'
            ? a.published
              ? this.publicCopies.get(a.id)
              : this.submittedCopies.get(a.id)
            : null;
        return snapshot ? { ...a, ...snapshot } : a;
      })
      .filter(
        (a) =>
          (!this.stateFilter || a.state === this.stateFilter) &&
          a.title.toLocaleLowerCase('ru').includes(this.query.toLocaleLowerCase('ru')),
      );
  }
  format(command: string): void {
    if (!this.editable()) return;
    const chain = this.editor!.chain().focus();
    switch (command) {
      case 'bold':
        chain.toggleBold().run();
        break;
      case 'italic':
        chain.toggleItalic().run();
        break;
      case 'h2':
        chain.toggleHeading({ level: 2 }).run();
        break;
      case 'h3':
        chain.toggleHeading({ level: 3 }).run();
        break;
      case 'bullet':
        chain.toggleBulletList().run();
        break;
      case 'ordered':
        chain.toggleOrderedList().run();
        break;
      case 'quote':
        chain.toggleBlockquote().run();
        break;
      case 'undo':
        chain.undo().run();
        break;
      case 'redo':
        chain.redo().run();
        break;
      default:
        chain.setParagraph().run();
    }
  }
  active(command: string): boolean {
    return this.activeFormats().includes(
      (
        { bullet: 'bulletList', ordered: 'orderedList', quote: 'blockquote' } as Record<
          string,
          string
        >
      )[command] ?? command,
    );
  }
  updateInlineImage(): void {
    const image = this.inlineImage();
    if (this.editable() && image)
      this.editor?.commands.updateAttributes('mediaFigure', {
        alt: image.alt,
        caption: image.caption,
      });
  }
  addLink(): void {
    if (!this.editable()) return;
    if (!safeUrl(this.linkUrl)) {
      this.notice.set('Укажи полный адрес http:// или https://.');
      return;
    }
    if (this.editor!.state.selection.empty) {
      this.notice.set('Сначала выдели текст, к которому нужно добавить ссылку.');
      return;
    }
    this.editor!.chain().focus().setLink({ href: this.linkUrl }).run();
    this.linkOpen = false;
  }
  placements(): Placement[] {
    return [this.content().cover, ...this.content().gallery].filter((p): p is Placement => !!p);
  }
  openPicker(target: 'cover' | 'gallery' | 'body'): void {
    this.picker.set(target);
    this.mediaAlt = '';
    this.mediaCaption = '';
    this.problems.set([]);
    this.mediaDialog.nativeElement.showModal();
  }
  closePicker(): void {
    this.picker.set(null);
    this.mediaDialog.nativeElement.close();
  }
  decide(action: string): void {
    this.feedback = '';
    this.decision.set(action);
    this.decisionDialog.nativeElement.showModal();
  }
  closeDecision(): void {
    this.decision.set('');
    this.decisionDialog.nativeElement.close();
  }
  confirmDecision(): void {
    if (this.decision() === 'takeover') {
      this.claim();
      this.closeDecision();
    } else this.act(this.decision());
  }
  insertMedia(): void {
    if (!this.editable()) return;
    const p = { id: this.selectedMedia(), alt: this.mediaAlt, caption: this.mediaCaption };
    if (this.picker() === 'cover') this.content().cover = p;
    if (this.picker() === 'gallery') this.content().gallery.push(p);
    if (this.picker() === 'body')
      this.editor
        ?.chain()
        .focus()
        .insertContent({
          type: 'mediaFigure',
          attrs: { mediaId: p.id, alt: p.alt, caption: p.caption },
        })
        .run();
    this.closePicker();
    this.changed();
  }
  removeGallery(index: number): void {
    this.content().gallery.splice(index, 1);
    this.changed();
  }
  setTags(value: string): void {
    this.content().tags = value
      .split(',')
      .map((t) => t.trim())
      .filter(Boolean);
    this.changed();
  }
  addSource(): void {
    this.content().sources.push({ name: '', url: '' });
    this.changed();
  }
  removeSource(index: number): void {
    this.content().sources.splice(index, 1);
    this.changed();
  }
  validate(): string[] {
    const a = this.content();
    const errors: string[] = [];
    const length = (s: string) => Array.from(s.normalize('NFC')).length;
    if (!a.title.trim() || length(a.title) > 200)
      errors.push('Заголовок должен содержать от 1 до 200 символов.');
    if (!a.summary.trim() || length(a.summary) > 500)
      errors.push('Краткое описание должно содержать от 1 до 500 символов.');
    if (!this.hasBodyText() || this.textLength() > 100000)
      errors.push('Добавь текст статьи, не более 100 000 символов.');
    if (!a.category) errors.push('Выбери категорию статьи.');
    if (a.tags.length > 10) errors.push('Можно выбрать не более 10 тегов.');
    if (a.usesOthers && !a.sources.length)
      errors.push('Добавь хотя бы один источник чужих материалов.');
    if (a.sources.some((s) => !s.name.trim() || !safeUrl(s.url)))
      errors.push('Заполни название и http(s)-адрес каждого источника.');
    const refs = this.placements().map((p) => p.id);
    const walk = (n: JSONContent) => {
      if (n.type === 'mediaFigure') {
        refs.push(n.attrs?.['mediaId']);
        if (!n.attrs?.['alt']?.trim())
          errors.push('Добавь альтернативный текст изображения в статье.');
      }
      n.content?.forEach(walk);
    };
    walk(this.doc());
    if (this.placements().some((p) => !p.alt.trim()))
      errors.push('Добавь альтернативный текст обложки и галереи.');
    if (new Set(refs).size > 20) errors.push('В версии может быть не более 20 разных изображений.');
    if (
      this.persona() === 'author' &&
      this.article().state === 'Returned' &&
      !this.changeNote.trim()
    )
      errors.push('Кратко опиши исправления для редактора.');
    return errors;
  }
  record(action: string, note = '', snapshot: Content = this.content()): void {
    this.article().history.unshift({
      action,
      actor: this.persona() === 'author' ? 'Анна Лесная' : 'Илья Морозов',
      time: new Date().toLocaleString('ru-RU', {
        day: 'numeric',
        month: 'long',
        hour: '2-digit',
        minute: '2-digit',
      }),
      note,
      body: structuredClone(snapshot.body),
      content: copyContent(snapshot),
    });
    this.touch();
    this.notice.set(action + '. Действие выполнено в демо.');
  }
  submit(): void {
    if (!this.editable() || this.persona() !== 'author') return;
    const errors = this.validate();
    this.problems.set(errors);
    if (errors.length) return;
    const a = this.article();
    a.reviewer = a.previousReviewer;
    a.submitted = true;
    a.state = 'InReview';
    this.submittedCopies.set(a.id, copyContent(a));
    this.record('Отправлено на проверку', this.changeNote);
    this.finishTransition();
  }
  claim(): void {
    const a = this.article();
    if (this.persona() !== 'editor' || a.state !== 'InReview') return;
    a.reviewer = 'Илья Морозов';
    a.previousReviewer = a.reviewer;
    this.record('Проверка назначена Илье Морозову');
  }
  act(action: string): void {
    const a = this.article();
    const editor = this.persona() === 'editor';
    const reviewer = editor && a.reviewer === 'Илья Морозов' && a.state === 'InReview';
    if (['return', 'reject', 'release', 'transfer', 'approve'].includes(action) && !reviewer)
      return;
    if (['return', 'reject', 'withdraw', 'correct'].includes(action) && !this.feedback.trim()) {
      this.problems.set(['Укажи причину или замечания.']);
      return;
    }
    switch (action) {
      case 'return':
        a.state = 'Returned';
        this.record('Возвращено на доработку', this.feedback);
        break;
      case 'reject':
        a.state = 'Rejected';
        a.working = false;
        this.record('Окончательный отказ', this.feedback);
        break;
      case 'approve':
        a.state = 'Approved';
        this.record('Версия одобрена');
        break;
      case 'release':
        a.reviewer = '';
        a.previousReviewer = '';
        this.record('Проверка возвращена в общую очередь');
        break;
      case 'transfer':
        a.reviewer = this.transferTo;
        a.previousReviewer = this.transferTo;
        this.record('Проверка передана', this.transferTo);
        break;
      case 'recall':
        if (editor || a.state !== 'InReview') return;
        a.state = 'Draft';
        this.record('Автор отозвал статью для исправлений');
        break;
      case 'publish':
        if (a.state !== 'Approved' || this.dirty()) return;
        a.state = 'Published';
        a.published = true;
        a.working = false;
        this.publicCopies.set(a.id, copyContent(a));
        this.record('Версия опубликована');
        break;
      case 'withdraw':
        if (!editor || !a.published || a.state === 'Withdrawn') return;
        a.state = 'Withdrawn';
        a.working = true;
        this.record('Материал снят с публикации', this.feedback);
        break;
      case 'correct':
        if (!editor || !this.editable()) return;
        this.problems.set(this.validate());
        if (this.problems().length) return;
        if (a.working) {
          this.record('Рабочая версия закрыта как устаревшая', this.feedback, a);
        }
        Object.assign(a, copyContent(this.content()));
        a.state = 'Published';
        a.working = false;
        this.publicCopies.set(a.id, copyContent(a));
        this.record('Опубликованная статья исправлена редактором', this.feedback);
        break;
      case 'propose':
        if (editor || !a.published || a.working) return;
        a.state = 'Draft';
        a.working = true;
        this.record('Создана рабочая версия');
        break;
      case 'delete':
        if (editor || a.published) return;
        a.deleted = true;
        this.record('Статья удалена автором');
        this.area.set('articles');
        break;
      default:
        return;
    }
    this.finishTransition();
  }
  private finishTransition(): void {
    clearTimeout(this.saveTimer);
    this.savedCopies.set(this.selected(), copyContent(this.article()));
    this.touch();
    this.editor?.setEditable(this.editable(), false);
    this.dirty.set(false);
    this.closeDecision();
    this.feedback = '';
    this.changeNote = '';
    this.problems.set([]);
    this.saveStatus.set('Демо-версия сохранена в памяти');
  }
  historyBody(entry: Entry): string {
    return renderDocument(entry.body);
  }
}
