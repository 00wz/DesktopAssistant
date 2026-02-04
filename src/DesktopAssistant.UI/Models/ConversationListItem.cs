namespace DesktopAssistant.UI.Models;

/// <summary>
/// Элемент списка сохранённых диалогов
/// </summary>
public class ConversationListItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Форматированное время последнего обновления
    /// </summary>
    public string FormattedDate
    {
        get
        {
            var local = UpdatedAt.ToLocalTime();
            var now = DateTime.Now;
            var diff = now - local;

            if (diff.TotalMinutes < 1) return "только что";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} мин. назад";
            if (diff.TotalHours < 24 && local.Date == now.Date) return $"сегодня, {local:HH:mm}";
            if (diff.TotalHours < 48 && local.Date == now.Date.AddDays(-1)) return $"вчера, {local:HH:mm}";
            if (diff.TotalDays < 7) return local.ToString("dddd, HH:mm");
            
            return local.ToString("d MMMM, HH:mm");
        }
    }
}
