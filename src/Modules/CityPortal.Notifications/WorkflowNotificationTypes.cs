namespace CityPortal.Notifications;

public static class WorkflowNotificationTypes
{
    // No article titles, draft text, template names, or private decision reasons enter delivery payloads.
    private static readonly IReadOnlyDictionary<string, string> _messages = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["article.returned"] = "Статья возвращена на доработку. Обратная связь доступна в статье.",
        ["article.rejected"] = "Статья отклонена. Причина доступна в истории статьи.",
        ["article.approved"] = "Статья одобрена и готова к публикации.",
        ["article.published"] = "Статья опубликована.",
        ["suggestion.published"] = "Статья по вашему предложению опубликована.",
        ["article.corrected"] = "Редактор исправил опубликованную статью.",
        ["article.withdrawn"] = "Статья снята с публикации.",
        ["article.superseded"] = "Рабочая версия закрыта после редакторского исправления.",
        ["review.resubmitted"] = "На вашу проверку поступила исправленная версия статьи.",
        ["review.withdrawn"] = "Автор отозвал статью с проверки.",
        ["review.deleted"] = "Автор удалил статью из работы.",
        ["review.transferred"] = "Назначение на проверку статьи изменилось.",
        ["template.approved"] = "Шаблон изображения одобрен.",
        ["template.rejected"] = "Шаблон изображения отклонён. Причина доступна в его истории.",
        ["template.archived"] = "Шаблон изображения помещён в архив.",
        ["template.blocked"] = "Шаблон изображения заблокирован. В статьях вместо него отображается заглушка.",
        ["template.unblocked"] = "Блокировка шаблона изображения снята.",
        ["image.blocked"] = "Изображение в вашей статье заблокировано. Вместо него отображается заглушка.",
        ["image.unblocked"] = "Блокировка изображения в вашей статье снята."
    };

    public static IReadOnlyCollection<string> All => _messages.Keys.ToArray();

    public static bool Contains(string type) => _messages.ContainsKey(type);

    public static string Message(string type) => _messages[type];
}
