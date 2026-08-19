using System.Collections.Generic;
using System.Threading.Tasks;

namespace BioTek.Services.Interfaces
{
    public interface IFirebaseNotificationService
    {
        Task<bool> SendNotificationAsync(
            string token,
            string title,
            string body,
            string url = null,
            Dictionary<string, string> data = null);

        Task<bool> SendMulticastAsync(
            List<string> tokens,
            string title,
            string body,
            string url = null,
            Dictionary<string, string> data = null);

        Task<bool> SendReadingReminderAsync(string userId, string bookTitle, string bookId);
        Task<bool> ValidateTokenAsync(string token);
    }
}