import { Node, mergeAttributes } from '@tiptap/core';
import type { JSONContent } from '@tiptap/core';

// Media is an ID reference, never an uploaded URL or pasted HTML image.
export const MediaFigure = Node.create({
  name: 'mediaFigure',
  group: 'block',
  atom: true,
  addAttributes: () => ({
    mediaId: { default: 'demo-park' },
    alt: { default: '' },
    caption: { default: '' },
  }),
  parseHTML: () => [],
  renderHTML: ({ HTMLAttributes }) => [
    'figure',
    mergeAttributes({ class: 'media-figure' }, { 'data-media-id': HTMLAttributes['mediaId'] }),
    ['div', { role: 'img', 'aria-label': HTMLAttributes['alt'], class: 'park-art' }, ''],
    ['figcaption', {}, HTMLAttributes['caption'] || HTMLAttributes['alt']],
  ],
});

export const initialDocument: JSONContent = {
  type: 'doc',
  content: [
    {
      type: 'paragraph',
      content: [
        {
          type: 'text',
          text: 'В Белогорске открыли новую прогулочную дорожку вдоль реки. Первые посетители уже оценили скамейки, освещение и спуски к воде.',
        },
      ],
    },
    {
      type: 'heading',
      attrs: { level: 2 },
      content: [{ type: 'text', text: 'Что изменилось на набережной' }],
    },
    {
      type: 'bulletList',
      content: ['Два километра пешеходных дорожек', 'Велопарковки и места для отдыха'].map(
        (text) => ({
          type: 'listItem',
          content: [{ type: 'paragraph', content: [{ type: 'text', text }] }],
        }),
      ),
    },
    {
      type: 'blockquote',
      content: [
        {
          type: 'paragraph',
          content: [
            {
              type: 'text',
              text: 'Теперь до центра можно дойти вдоль воды.',
              marks: [{ type: 'italic' }],
            },
          ],
        },
      ],
    },
  ],
};

export function safeUrl(value: string): boolean {
  try {
    return ['http:', 'https:'].includes(new URL(value).protocol);
  } catch {
    return false;
  }
}

// Independent allowlist renderer for a future .NET implementation to reproduce.
// It does not use Tiptap's HTML output or trust DOM/HTML in stored documents.
export function renderDocument(doc: JSONContent): string {
  const escape = (value: unknown) =>
    String(value ?? '').replace(
      /[&<>"']/g,
      (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]!,
    );
  const render = (node: JSONContent): string => {
    if (node.type === 'text') {
      let text = escape(node.text);
      for (const mark of node.marks ?? []) {
        if (mark.type === 'bold') text = `<strong>${text}</strong>`;
        if (mark.type === 'italic') text = `<em>${text}</em>`;
        if (mark.type === 'link' && safeUrl(mark.attrs?.['href']))
          text = `<a href="${escape(mark.attrs?.['href'])}" rel="noopener noreferrer">${text}</a>`;
      }
      return text;
    }
    if (node.type === 'mediaFigure')
      return `<figure><div class="park-art" role="img" aria-label="${escape(node.attrs?.['alt'])}"></div><figcaption>${escape(node.attrs?.['caption'])}</figcaption></figure>`;
    const tag = (
      {
        doc: 'div',
        paragraph: 'p',
        heading: node.attrs?.['level'] === 3 ? 'h3' : 'h2',
        bulletList: 'ul',
        orderedList: 'ol',
        listItem: 'li',
        blockquote: 'blockquote',
        hardBreak: 'br',
      } as Record<string, string>
    )[node.type ?? ''];
    if (!tag) return '';
    return tag === 'br' ? '<br>' : `<${tag}>${(node.content ?? []).map(render).join('')}</${tag}>`;
  };
  return render(doc);
}
