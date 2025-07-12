using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.ObjectPool;

namespace Tenosynovitis;

internal partial class DbParser
{
    internal static readonly Dictionary<string, (string, (int, int))> GxFlavors = new() {
        ["FLAVOR_PASSIVE"] = ("calm", (4, 9)),
        ["FLAVOR_WELCOME"] = ("fov", (5, 10)),
        ["FLAVOR_ANGERED"] = ("aggro", (6, 11)),
        ["FLAVOR_DEATH"] = ("dead", (7, 12)),
        ["FLAVOR_KILL"] = ("kill", (8, 13)),
    };

    internal List<CharaData> Parse(string dbData)
    {
        return SplitCreatureBlocks(dbData)
            .Select(kv => ParseCreatureBlock(kv.Key, kv.Value))
            .ToList();
    }

    private static readonly ObjectPool<StringBuilder> _builders = new DefaultObjectPoolProvider().CreateStringBuilderPool();

    private static Dictionary<string, string> SplitCreatureBlocks(string dbData)
    {
        Dictionary<string, string> blocks = [];
        var matches = DbBlockRegex().Matches(dbData);

        for (var i = 0; i < matches.Count; i++) {
            var id = matches[i].Groups[1].Value.Trim().Replace("CREATURE_ID_", "");
            var start = matches[i].Index;
            var end = i < matches.Count - 1
                ? matches[i + 1].Index
                : dbData.Length;

            blocks[id] = dbData.Substring(start, end - start).Trim();
        }

        return blocks;
    }

    private CharaData ParseCreatureBlock(string id, string block)
    {
        var dbBlocks = ParseScriptBlocks(block);
        var chara = new CharaData {
            Id = id.ToLowerInvariant(),
        };

        foreach (var (mode, blockContent) in dbBlocks) {
            if (GxFlavors.TryGetValue(mode, out var flavor)) {
                var validText = blockContent
                    .Select(ParseText)
                    .FirstOrDefault(t => t.Count > 0);

                if (validText is not null) {
                    chara.CharaTexts[flavor.Item1] = validText;
                }
            } else if (mode is "SET") {
                var validName = blockContent
                    .Select(ParseText)
                    .FirstOrDefault(t => t.Count > 0)?[0];

                if (validName is not null) {
                    chara.Name = validName;
                }
            }
        }

        return chara;
    }

    private static Dictionary<string, List<string>> ParseScriptBlocks(string dbData)
    {
        Dictionary<string, List<string>> blocks = [];
        var lines = dbData.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)[1..^1];
        var braceDepth = 0;
        string? dbMode = null;
        List<string>? dbBlock = null;

        foreach (var line in lines) {
            var trimmedLine = line.Trim();
            var match = DbModeBlockRegex().Match(trimmedLine);
            if (match.Success) {
                dbMode = match.Groups[1].Value.Replace("DBMODE_", "").Trim();
                braceDepth = 1;
                dbBlock = [];
                continue;
            }

            if (dbBlock is null) {
                continue;
            }

            braceDepth += CountOccurrences(line, '{');
            braceDepth -= CountOccurrences(line, '}');

            if (!string.IsNullOrWhiteSpace(line) && braceDepth > 0) {
                dbBlock.Add(line);
            }

            if (braceDepth > 0) {
                continue;
            }

            if (dbMode is not null) {
                blocks[dbMode] = dbBlock;
            }

            dbMode = null;
            dbBlock = null;
        }

        return blocks;
    }

    public List<string[]> ParseText(string input)
    {
        input = input.Trim();
        input = OniiRegex().Replace(input, "#onii");
        input = ScriptLhsRegex().Replace(input, "");

        List<string> langCalls = [];
        var depth = 0;
        var start = 0;

        for (var i = 0; i < input.Length; i++) {
            switch (input[i]) {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    break;
            }

            if (depth != 0 || i >= input.Length - 1 || input[i] != ',') {
                continue;
            }

            langCalls.Add(input.Substring(start, i - start).Trim());
            start = i + 1;
        }

        if (start < input.Length) {
            langCalls.Add(input[start..].Trim());
        }

        List<string[]> result = [];
        foreach (var call in langCalls) {
            if (!call.StartsWith("lang(") || !call.EndsWith(')')) {
                continue;
            }

            var inner = call[5..^1].Trim();
            var paramDepth = 0;
            var commaPos = -1;

            for (var i = 0; i < inner.Length; i++) {
                if (inner[i] == '(') {
                    paramDepth++;
                } else if (inner[i] == ')') {
                    paramDepth--;
                } else if (paramDepth == 0 && inner[i] == ',') {
                    commaPos = i;
                    break;
                }
            }

            if (commaPos < 0) {
                continue;
            }

            var jpExp = inner[..commaPos].Trim();
            var enExp = inner[(commaPos + 1)..].Trim();

            var jp = ProcessExpression(jpExp);

            enExp = ConvertTalkRegex().Replace(enExp, "");
            enExp = enExp.Trim();
            if (enExp.EndsWith(')')) {
                enExp = enExp[..^1].Trim();
            }

            var en = ProcessExpression(enExp);

            result.Add([jp.Trim('\"'), en.Trim('\"')]);
        }

        return result;
    }

    private static string ProcessExpression(string exp)
    {
        var sb = _builders.Get();
        try {
            var parts = exp.Split('+');

            foreach (var part in parts) {
                var segment = part.Trim();
                if (segment is ['\"', _, '\"']) {
                    segment = segment.Substring(1, segment.Length - 2);
                }

                sb.Append(segment);
            }

            return sb.ToString();
        } finally {
            _builders.Return(sb);
        }
    }

    private static int CountOccurrences(ReadOnlySpan<char> text, char target)
    {
        var count = 0;
        foreach (var c in text) {
            if (c == target) {
                count++;
            }
        }
        return count;
    }

    [GeneratedRegex(@"if\s*\(\s*dbid\s*==\s*(\w+)\s*\)\s*\{")]
    private static partial Regex DbBlockRegex();

    [GeneratedRegex(@"dbmode == (.*)\) {")]
    private static partial Regex DbModeBlockRegex();

    [GeneratedRegex(@"_onii\(cdata\(CDATA_SEX,\s*CHARA_PLAYER\)\)")]
    private static partial Regex OniiRegex();

    [GeneratedRegex(@"^\s*txt\s*|^\s*cdatan\(CDATAN_NAME, rc\) = ", RegexOptions.Multiline)]
    private static partial Regex ScriptLhsRegex();

    [GeneratedRegex(@"^cnvtalk\s*\(")]
    private static partial Regex ConvertTalkRegex();
}