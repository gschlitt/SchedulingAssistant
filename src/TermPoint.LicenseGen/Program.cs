using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TermPoint.LicenseGen;

/// <summary>
/// Local copy of the license payload — mirrors TermPoint.Licensing.LicensePayload.
/// Duplicated here so LicenseGen compiles without referencing the full TermPoint project.
/// </summary>
public class LicensePayload
{
    public string Department { get; set; } = string.Empty;
    public string? Institution { get; set; }
    public string Issued { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public int LicenseVersion { get; set; } = 1;
}

/// <summary>
/// CLI tool for generating TermPoint license key files.
///
/// Usage:
///   generate-keys                                  — create a new RSA keypair
///   generate --department "Name" --expiry 2027-07-01 --output termpoint.lic
///   generate --department "Name" --permanent --output termpoint.lic
/// </summary>
public class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0].ToLowerInvariant();
        return command switch
        {
            "generate-keys" => GenerateKeys(args),
            "generate" => GenerateLicense(args),
            _ => Error($"Unknown command: {args[0]}")
        };
    }

    /// <summary>
    /// Generates a new 2048-bit RSA keypair and writes the private and public keys to PEM files.
    /// </summary>
    private static int GenerateKeys(string[] args)
    {
        var privateOut = GetArg(args, "--private-out") ?? "termpoint-private.pem";
        var publicOut = GetArg(args, "--public-out") ?? "termpoint-public.pem";

        if (File.Exists(privateOut) || File.Exists(publicOut))
        {
            Console.Error.WriteLine($"Key file(s) already exist. Delete them first if you want to regenerate.");
            Console.Error.WriteLine($"  Private: {Path.GetFullPath(privateOut)}");
            Console.Error.WriteLine($"  Public:  {Path.GetFullPath(publicOut)}");
            return 1;
        }

        using var rsa = RSA.Create(2048);

        var privatePem = rsa.ExportRSAPrivateKeyPem();
        var publicPem = rsa.ExportRSAPublicKeyPem();

        File.WriteAllText(privateOut, privatePem);
        File.WriteAllText(publicOut, publicPem);

        Console.WriteLine($"Keypair generated:");
        Console.WriteLine($"  Private key: {Path.GetFullPath(privateOut)}");
        Console.WriteLine($"  Public key:  {Path.GetFullPath(publicOut)}");
        Console.WriteLine();
        Console.WriteLine("Keep the private key secure. Never commit it to the repo.");
        Console.WriteLine("The public key is safe to embed in the TermPoint binary.");

        return 0;
    }

    /// <summary>
    /// Signs a license payload with the private key and writes a <c>termpoint.lic</c> file.
    /// The file contains two base64-encoded sections separated by a blank line:
    /// the JSON payload and its RSA-SHA256 signature.
    /// </summary>
    private static int GenerateLicense(string[] args)
    {
        var department = GetArg(args, "--department");
        var institution = GetArg(args, "--institution");
        var expiryArg = GetArg(args, "--expiry");
        var permanent = HasFlag(args, "--permanent");
        var privateKeyPath = GetArg(args, "--key") ?? "termpoint-private.pem";
        var outputPath = GetArg(args, "--output") ?? "termpoint.lic";

        if (string.IsNullOrWhiteSpace(department))
            return Error("--department is required.");

        if (expiryArg == null && !permanent)
            return Error("Specify --expiry YYYY-MM-DD or --permanent.");

        if (expiryArg != null && permanent)
            return Error("Cannot specify both --expiry and --permanent.");

        if (expiryArg != null && !DateOnly.TryParse(expiryArg, out _))
            return Error($"Invalid date format: {expiryArg}. Use YYYY-MM-DD.");

        if (!File.Exists(privateKeyPath))
            return Error($"Private key not found: {Path.GetFullPath(privateKeyPath)}");

        // Build the payload
        var payload = new LicensePayload
        {
            Department = department,
            Institution = institution,
            Issued = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            Expiry = permanent ? null : expiryArg,
            LicenseVersion = 1
        };

        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        // Sign with the private key
        using var rsa = RSA.Create();
        var privatePem = File.ReadAllText(privateKeyPath);
        rsa.ImportFromPem(privatePem);

        var signature = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Write the .lic file: human-readable header, then base64 payload,
        // blank line, base64 signature. The validator skips lines starting with '#'.
        var licenseContent = new StringBuilder();
        licenseContent.AppendLine("# TermPoint License");
        licenseContent.AppendLine($"# Department: {payload.Department}");
        if (!string.IsNullOrWhiteSpace(payload.Institution))
            licenseContent.AppendLine($"# Institution: {payload.Institution}");
        licenseContent.AppendLine($"# Issued:     {payload.Issued}");
        licenseContent.AppendLine($"# Expires:    {payload.Expiry ?? "Never"}");
        licenseContent.AppendLine("#");
        licenseContent.AppendLine("# Do not edit below this line.");
        licenseContent.AppendLine();
        licenseContent.AppendLine(Convert.ToBase64String(payloadBytes));
        licenseContent.AppendLine();
        licenseContent.AppendLine(Convert.ToBase64String(signature));

        File.WriteAllText(outputPath, licenseContent.ToString());

        Console.WriteLine($"License generated: {Path.GetFullPath(outputPath)}");
        Console.WriteLine($"  Department:      {payload.Department}");
        if (!string.IsNullOrWhiteSpace(payload.Institution))
            Console.WriteLine($"  Institution:     {payload.Institution}");
        Console.WriteLine($"  Issued:          {payload.Issued}");
        Console.WriteLine($"  Expiry:          {payload.Expiry ?? "(permanent)"}");
        Console.WriteLine($"  License version: {payload.LicenseVersion}");

        return 0;
    }

    /// <summary>
    /// Returns the value of a named argument (e.g. --department "Name"), or null if not present.
    /// </summary>
    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    /// <summary>
    /// Returns true if a flag (e.g. --permanent) is present in the arguments.
    /// </summary>
    private static bool HasFlag(string[] args, string name)
    {
        return args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static int Error(string message)
    {
        Console.Error.WriteLine($"Error: {message}");
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("TermPoint License Generator");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  generate-keys    Generate a new RSA keypair");
        Console.WriteLine("    --private-out   Private key output path (default: termpoint-private.pem)");
        Console.WriteLine("    --public-out    Public key output path  (default: termpoint-public.pem)");
        Console.WriteLine();
        Console.WriteLine("  generate         Generate a signed license file");
        Console.WriteLine("    --department    Department name (required)");
        Console.WriteLine("    --institution   Institution name (optional)");
        Console.WriteLine("    --expiry        Expiry date, YYYY-MM-DD");
        Console.WriteLine("    --permanent     No expiry (mutually exclusive with --expiry)");
        Console.WriteLine("    --key           Path to private key (default: termpoint-private.pem)");
        Console.WriteLine("    --output        Output path (default: termpoint.lic)");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet run -- generate-keys");
        Console.WriteLine("  dotnet run -- generate --department \"UBC Geography\" --expiry 2027-07-01");
        Console.WriteLine("  dotnet run -- generate --department \"UBC Geography\" --permanent");
    }
}
