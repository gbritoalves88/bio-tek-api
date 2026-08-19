namespace BioTek.Models.DTOs
{
    public class SubscribeRequest
    {
        public string UserId { get; set; }
        public string Token { get; set; }
        public string Device { get; set; }
        public string Platform { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class UnsubscribeRequest
    {
        public string UserId { get; set; }
        public string Token { get; set; }
    }

    public class NotificationMessage
    {
        public string Title { get; set; }
        public string Body { get; set; }
        public string Url { get; set; }
        public Dictionary<string, string> Data { get; set; }
    }

    public class ReadingReminderRequest
    {
        public int BookId { get; set; }
        public string BookTitle { get; set; }
        public string UserId { get; set; }
    }

    public class EnviarNotificacaoRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string? Url { get; set; }
        public Dictionary<string, string>? Data { get; set; }
    }
}