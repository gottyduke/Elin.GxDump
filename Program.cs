using System.Text;
using ClosedXML.Excel;
using Tenosynovitis;

const string dbFile = "./db_creature.hsp";
const string excelFile = "db_2_elin.xlsx";

Console.WriteLine($"parsing db file {dbFile}");

var db = File.ReadAllText(dbFile, Encoding.UTF8);
var parser = new DbParser();

var charas = parser.Parse(db);
var charasWithTalks = charas
    .Where(c => c.CharaTexts.Count > 0)
    .ToArray();

Console.WriteLine($"{charas.Count} charas parsed from db");
Console.WriteLine($"{charasWithTalks.Length} charas with flavor texts");
Console.WriteLine("making excel sheets to prevent tenosynovitis...");

string[] headers = [
    // 1       2          3
    "id", "name", "name_JP",
    //   4      5        6       7       8
    "calm", "fov", "aggro", "dead", "kill",
    //      9        10          11         12         13
    "calm_JP", "fov_JP", "aggro_JP", "dead_JP", "kill_JP",
];

using var workbook = new XLWorkbook();
var worksheet = workbook.Worksheets.Add("CharaText");

for (var i = 0; i < headers.Length; ++i) {
    worksheet.Cell(1, i + 1).Value = headers[i];
}

worksheet.Row(1).Style.Protection.Locked = true;
worksheet.SheetView.FreezeRows(1);

var rowCount = 3;
foreach (var chara in charas) {
    var row = worksheet.Row(rowCount);
    
    row.Cell(1).Value = chara.Id;
    row.Cell(2).Value = chara.Name[0];
    row.Cell(3).Value = chara.Name[1];

    foreach (var (flavor, (en, jp)) in DbParser.GxFlavors.Values) {
        if (chara.CharaTexts.TryGetValue(flavor, out var texts)) {
            row.Cell(en).Value = JoinTexts(texts, 1);
            row.Cell(jp).Value = JoinTexts(texts, 0);
        }
    }

    ++rowCount;
}

var totalColumns = headers.Length;
worksheet.Rows(1, rowCount).AdjustToContents(1, totalColumns);
worksheet.Columns(1, totalColumns).AdjustToContents(1, rowCount, minWidth: 5d, maxWidth: 50d);

workbook.SaveAs(excelFile);

Console.WriteLine($"saved {excelFile}");
Console.WriteLine("move your finger and close this");
Console.ReadKey();
return;

static string JoinTexts(List<string[]> texts, int index) => 
    string.Join(Environment.NewLine, texts.Select(t => t[index]));