namespace TermPoint.Licensing;

/// <summary>
/// Embedded RSA public key used to verify <c>termpoint.lic</c> signatures.
/// The corresponding private key is kept offline and never shipped in the app.
/// </summary>
public static class LicenseKeys
{
    public const string PublicKeyPem =
        """
        -----BEGIN RSA PUBLIC KEY-----
        MIIBCgKCAQEAvZwilWpJ1JYajd8mL3p6wx3m0eYVDEzZVo0YELMZlOGCdkpe1sw4
        w7CEmc/36K2f4SREIyBcH8g3XWS5fSwMPP6t+f+6g2iZMAVzjcrc5ONyLsHsV+di
        w4XOrKQdUBbulpjC+h9+hsO7XtTlL5cxPr6FcgAtX3GWTpWSMO3Gu8O1u7GLrSrK
        LTiW8n/Lz0GrEhlr+X7jpFPxilK17Gh56x21sfTiIw8ldIToN9ritvGdbU08zVaQ
        5aRE9nFIwqGZQxos5UsnAdrkMN1SR0JKj+vikjh5dfO1wr1fYDKIum51MtDEfnCb
        Eb6ZaooP7pAUpvl85XgfJdMfsXE7yK4c7QIDAQAB
        -----END RSA PUBLIC KEY-----
        """;
}
