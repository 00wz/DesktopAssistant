namespace DesktopAssistant.UI.Models;

/// <summary>
/// An item in the saved conversations list.
/// </summary>
public class ConversationListItem
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Formatted time of the last update.
    /// </summary>
    public string FormattedDate
    {
        get
        {
            var local = UpdatedAt.ToLocalTime();
            var now = DateTime.Now;
            var diff = now - local;

            if (diff.TotalMinutes < 1) return "just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min. ago";
            if (diff.TotalHours < 24 && local.Date == now.Date) return $"today, {local:HH:mm}";
            if (diff.TotalHours < 48 && local.Date == now.Date.AddDays(-1)) return $"yesterday, {local:HH:mm}";
            if (diff.TotalDays < 7) return local.ToString("dddd, HH:mm");

            return local.ToString("d MMMM, HH:mm");
        }
    }
}
