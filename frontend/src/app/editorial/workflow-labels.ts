export const articleState = (state: string): string =>
  ({
    Draft: 'Черновик',
    Deleted: 'Удалено автором',
    InReview: 'На проверке',
    Approved: 'Одобрено',
    FinalRejected: 'Окончательно отклонено',
    Published: 'Опубликовано',
    Superseded: 'Закрыто',
  })[state] ?? state;

export const reviewAction = (action: string): string =>
  ({
    Created: 'Создан черновик',
    Deleted: 'Статья удалена',
    submit: 'Отправлено на проверку',
    recall: 'Отозвано автором',
    claim: 'Назначен редактор',
    release: 'Возвращено в очередь',
    takeover: 'Проверка передана',
    return: 'Возвращено на доработку',
    reject: 'Окончательно отклонено',
    approve: 'Одобрено',
    publish: 'Опубликовано',
    delete: 'Статья удалена',
    'roles-released': 'Назначение снято после изменения ролей',
  })[action] ?? action;
