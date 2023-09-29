using Microsoft.AspNetCore.Identity.UI.Services;

namespace WebApp;

public class MockEmailSender : IEmailSender
{
    public static Dictionary<int, HashSet<(string email, string subject, string htmlMessage)>> EmailDictionary { get; set; } = new();

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        var currentHour = DateTime.Now.Hour;

        if (EmailDictionary.TryGetValue(currentHour, out var existingHashSet))
        {
            existingHashSet.Add((email, subject, htmlMessage));
        }
        else
        {
            var newSingleItemHashset = new HashSet<(string email, string subject, string htmlMessage)>() { (email, subject, htmlMessage) };
            EmailDictionary.Add(currentHour, newSingleItemHashset);
        }

        return Task.CompletedTask;
    }
}