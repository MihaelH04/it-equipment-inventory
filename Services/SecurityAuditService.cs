using ITEquipmentInventory.Data;
using ITEquipmentInventory.Models;
using Microsoft.EntityFrameworkCore;

namespace ITEquipmentInventory.Services;

public class SecurityAuditService
{
    private readonly AppDbContext _context;
    private readonly ILogger<SecurityAuditService> _logger;

    public SecurityAuditService(AppDbContext context, ILogger<SecurityAuditService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogAsync(
        string eventType,
        string? userName,
        int? appUserId,
        bool success,
        string? message,
        HttpContext? httpContext = null)
    {
        try
        {
            var log = new SecurityLog
            {
                EventType = SafeTrim(eventType, 80) ?? "Unknown",
                UserName = SafeTrim(userName, 80),
                AppUserId = appUserId,
                Success = success,
                IpAddress = SafeTrim(GetIpAddress(httpContext), 64),
                UserAgent = SafeTrim(httpContext?.Request.Headers.UserAgent.ToString(), 300),
                Path = SafeTrim(httpContext?.Request.Path.Value, 300),
                Message = SafeTrim(message, 500),
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.SecurityLogs.Add(log);
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sigurnosni zapis nije spremljen za događaj {EventType}.", eventType);
        }
    }

    private static string? GetIpAddress(HttpContext? httpContext)
    {
        if (httpContext == null)
            return null;

        return httpContext.Connection.RemoteIpAddress?.ToString();
    }

    private static string? SafeTrim(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
