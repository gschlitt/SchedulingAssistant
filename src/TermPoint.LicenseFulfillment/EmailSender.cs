using Azure;
using Azure.Communication.Email;

namespace TermPoint.LicenseFulfillment;

/// <summary>
/// Sends the license file to the customer via Azure Communication Services Email.
/// </summary>
public static class EmailSender
{
    /// <summary>
    /// Sends an email with the license file attached.
    /// </summary>
    /// <param name="connectionString">ACS Email connection string.</param>
    /// <param name="senderAddress">Verified sender email address.</param>
    /// <param name="recipientEmail">Customer email address.</param>
    /// <param name="department">Department name (for the email subject/body).</param>
    /// <param name="licenseFileContent">The complete termpoint.lic file content.</param>
    public static async Task SendLicenseAsync(
        string connectionString,
        string senderAddress,
        string recipientEmail,
        string department,
        string licenseFileContent)
    {
        var client = new EmailClient(connectionString);

        var content = new EmailContent("Your TermPoint License")
        {
            PlainText =
                $"Thank you for purchasing a TermPoint license for {department}.\n\n" +
                "Your license file (termpoint.lic) is attached to this email.\n\n" +
                "To activate your license, place the termpoint.lic file in the same folder " +
                "as your department's TermPoint database (.tpdb file). TermPoint will find " +
                "it automatically on the next launch.\n\n" +
                "If your department uses multiple database folders, place a copy of the " +
                "license file in each folder.\n\n" +
                "Questions? Contact us at admin@termpoint.ca.",
            Html =
                $"<p>Thank you for purchasing a TermPoint license for <strong>{department}</strong>.</p>" +
                "<p>Your license file (<code>termpoint.lic</code>) is attached to this email.</p>" +
                "<h3>How to install</h3>" +
                "<p>Place the <code>termpoint.lic</code> file in the same folder as your " +
                "department's TermPoint database (<code>.tpdb</code> file). TermPoint will find " +
                "it automatically on the next launch.</p>" +
                "<p>If your department uses multiple database folders, place a copy of the " +
                "license file in each folder.</p>" +
                "<p>Questions? Contact us at <a href=\"mailto:admin@termpoint.ca\">admin@termpoint.ca</a>.</p>"
        };

        var licenseBytes = System.Text.Encoding.UTF8.GetBytes(licenseFileContent);

        var message = new EmailMessage(
            senderAddress,
            new EmailRecipients(new List<EmailAddress> { new(recipientEmail) }),
            content);

        message.Attachments.Add(new EmailAttachment(
            "termpoint.lic",
            "text/plain",
            BinaryData.FromBytes(licenseBytes)));

        await client.SendAsync(WaitUntil.Completed, message);
    }
}
