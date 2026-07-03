using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace TermPoint.LicenseFulfillment;

/// <summary>
/// HTTP-triggered Azure Function that handles Paddle's <c>transaction.completed</c> webhook.
/// Generates a signed license file and emails it to the customer.
/// </summary>
public class PaddleWebhook
{
    private readonly ILogger<PaddleWebhook> _logger;
    private static readonly HttpClient Http = new();

    public PaddleWebhook(ILogger<PaddleWebhook> logger)
    {
        _logger = logger;
    }

    [Function("PaddleWebhook")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequest req)
    {
        _logger.LogInformation("Paddle webhook received.");

        var body = await new StreamReader(req.Body).ReadToEndAsync();
        var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var eventType = root.GetProperty("event_type").GetString();
        if (eventType != "transaction.completed")
        {
            _logger.LogInformation("Ignoring event type: {EventType}", eventType);
            return new OkResult();
        }

        var data = root.GetProperty("data");

        // Extract department name from custom_data
        var department = GetDepartmentFromCustomData(data);
        if (string.IsNullOrWhiteSpace(department))
        {
            _logger.LogError("No department name found in custom_data.");
            return new BadRequestObjectResult("Missing department in custom_data.");
        }

        // Get customer email via Paddle API
        var customerId = data.GetProperty("customer_id").GetString()!;
        var customerEmail = await GetCustomerEmailAsync(customerId);
        if (string.IsNullOrWhiteSpace(customerEmail))
        {
            _logger.LogError("Could not retrieve customer email for {CustomerId}.", customerId);
            return new StatusCodeResult(500);
        }

        // Generate the signed license file
        var privateKey = Environment.GetEnvironmentVariable("PRIVATE_KEY")!;
        var expiryYears = int.Parse(Environment.GetEnvironmentVariable("LICENSE_EXPIRY_YEARS") ?? "1");
        var licenseContent = LicenseGenerator.Generate(department, expiryYears, privateKey);

        _logger.LogInformation(
            "License generated for {Department}, customer {Email}.",
            department, customerEmail);

        // Email the license file to the customer
        var acsConnectionString = Environment.GetEnvironmentVariable("ACS_CONNECTION_STRING")!;
        var senderEmail = Environment.GetEnvironmentVariable("SENDER_EMAIL")!;

        await EmailSender.SendLicenseAsync(
            acsConnectionString,
            senderEmail,
            customerEmail,
            department,
            licenseContent);

        _logger.LogInformation("License emailed to {Email}.", customerEmail);

        return new OkResult();
    }

    /// <summary>
    /// Extracts the department name from the transaction's custom_data object.
    /// </summary>
    private static string? GetDepartmentFromCustomData(JsonElement data)
    {
        if (!data.TryGetProperty("custom_data", out var customData))
            return null;

        if (customData.ValueKind == JsonValueKind.Null)
            return null;

        if (!customData.TryGetProperty("department", out var dept))
            return null;

        return dept.GetString();
    }

    /// <summary>
    /// Calls the Paddle API to retrieve the customer's email address.
    /// </summary>
    private async Task<string?> GetCustomerEmailAsync(string customerId)
    {
        var apiKey = Environment.GetEnvironmentVariable("PADDLE_API_KEY");
        var apiBase = Environment.GetEnvironmentVariable("PADDLE_API_BASE")
                      ?? "https://api.paddle.com";

        var request = new HttpRequestMessage(HttpMethod.Get, $"{apiBase}/customers/{customerId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Paddle API returned {StatusCode} for customer {CustomerId}.",
                response.StatusCode, customerId);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").GetProperty("email").GetString();
    }
}
