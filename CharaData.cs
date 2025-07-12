namespace Tenosynovitis;

internal record CharaData
{
    internal string Id { get; set; } = "";
    internal string[] Name { get; set; } = [];
    internal Dictionary<string, List<string[]>> CharaTexts { get; set; } = [];
}