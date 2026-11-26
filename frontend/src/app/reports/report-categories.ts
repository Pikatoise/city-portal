export type ReportKind = 'articles' | 'classifieds' | 'comments';

const common = {
  spam: 'Спам',
  fraud: 'Мошенничество',
  insults: 'Оскорбления',
  'personal-data': 'Чужие персональные данные',
  other: 'Другое',
};

export const reportCategories: Record<ReportKind, Record<string, string>> = {
  articles: {
    spam: common.spam,
    fraud: common.fraud,
    'inaccurate-information': 'Недостоверные сведения',
    'misleading-headline': 'Вводящий в заблуждение заголовок',
    copyright: 'Нарушение авторских прав',
    'personal-data': common['personal-data'],
    insults: common.insults,
    'hate-speech': 'Язык вражды',
    'inappropriate-media': 'Неуместные фото или видео',
    other: common.other,
  },
  comments: {
    spam: common.spam,
    insults: common.insults,
    'hate-speech': 'Язык вражды',
    'personal-data': common['personal-data'],
    other: common.other,
  },
  classifieds: {
    spam: common.spam,
    fraud: common.fraud,
    'prohibited-item': 'Запрещённый товар или услуга',
    'wrong-category': 'Неверная категория',
    'inaccurate-description': 'Недостоверное описание',
    'incorrect-price': 'Неверная цена',
    'inappropriate-photos': 'Неуместные фото',
    insults: common.insults,
    'personal-data': common['personal-data'],
    other: common.other,
  },
};
