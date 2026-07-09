namespace TextServices.Infrastructure.Http;

public static class UserAgents
{
    private const string Suffix = " (+https://github.com/dlcs/text-services)";

    public const string Builder = "TextServices-Builder/1.0" + Suffix;
    public const string Search = "TextServices-Search/1.0" + Suffix;
}
