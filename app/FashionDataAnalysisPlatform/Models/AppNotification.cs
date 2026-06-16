namespace FashionDataAnalysisPlatform.Models
{
    public class AppNotification
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        /// <summary>Critical | Warning | Info | Success</summary>
        public string Severity { get; set; } = "Info";
        public string Module { get; set; } = string.Empty;
        public string ModuleUrl { get; set; } = string.Empty;
        public string TimeLabel { get; set; } = "Now";
    }
}
