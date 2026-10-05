using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace BattlePvp.EditorData
{
    public sealed class ExcelRow
    {
        private readonly IReadOnlyList<string> cells;

        public ExcelRow(int rowNumber, IReadOnlyList<string> cells)
        {
            RowNumber = rowNumber;
            this.cells = cells;
        }

        public int RowNumber { get; }
        public IReadOnlyList<string> Cells => cells;

        public string GetCell(int columnIndex)
        {
            return columnIndex >= 0 && columnIndex < cells.Count
                ? cells[columnIndex]?.Trim() ?? string.Empty
                : string.Empty;
        }

        public bool IsEmpty => cells.All(string.IsNullOrWhiteSpace);
    }

    public sealed class ExcelSheet
    {
        public ExcelSheet(string name, IReadOnlyList<ExcelRow> rows)
        {
            Name = name;
            Rows = rows;
        }

        public string Name { get; }
        public IReadOnlyList<ExcelRow> Rows { get; }
    }

    public sealed class OpenXmlWorkbookReader : IDisposable
    {
        private static readonly XNamespace SpreadsheetNamespace =
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace DocumentRelationshipNamespace =
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelationshipNamespace =
            "http://schemas.openxmlformats.org/package/2006/relationships";

        private readonly FileStream stream;
        private readonly ZipArchive archive;
        private readonly IReadOnlyList<string> sharedStrings;
        private readonly Dictionary<string, string> sheetParts;

        public OpenXmlWorkbookReader(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Excel workbook path is empty.",
                    nameof(path));
            }

            stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            try
            {
                archive = new ZipArchive(
                    stream,
                    ZipArchiveMode.Read,
                    false);
                sharedStrings = ReadSharedStrings();
                sheetParts = ReadSheetParts();
                ValidateTableMetadata();
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        public IReadOnlyCollection<string> SheetNames => sheetParts.Keys;

        public ExcelSheet ReadSheet(string sheetName)
        {
            string actualName = sheetParts.Keys.FirstOrDefault(candidate =>
                string.Equals(
                    candidate,
                    sheetName,
                    StringComparison.OrdinalIgnoreCase));
            if (actualName == null)
            {
                throw new InvalidDataException(
                    $"Required sheet '{sheetName}' was not found. " +
                    $"Available sheets: {string.Join(", ", SheetNames)}");
            }

            XDocument document = ReadXml(sheetParts[actualName]);
            var rows = new List<ExcelRow>();
            foreach (XElement rowElement in document
                         .Descendants(SpreadsheetNamespace + "row"))
            {
                int rowNumber = ReadPositiveInteger(
                    rowElement.Attribute("r")?.Value,
                    rows.Count + 1);
                var values = new SortedDictionary<int, string>();
                foreach (XElement cell in rowElement.Elements(
                             SpreadsheetNamespace + "c"))
                {
                    string reference = cell.Attribute("r")?.Value;
                    int columnIndex = GetColumnIndex(reference);
                    values[columnIndex] = ReadCellValue(
                        actualName,
                        reference,
                        cell);
                }

                int columnCount = values.Count == 0
                    ? 0
                    : values.Keys.Max() + 1;
                var cells = Enumerable
                    .Repeat(string.Empty, columnCount)
                    .ToArray();
                foreach (KeyValuePair<int, string> value in values)
                {
                    cells[value.Key] = value.Value;
                }

                rows.Add(new ExcelRow(rowNumber, cells));
            }

            return new ExcelSheet(actualName, rows);
        }

        public void Dispose()
        {
            archive.Dispose();
            stream.Dispose();
        }

        private IReadOnlyList<string> ReadSharedStrings()
        {
            ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return Array.Empty<string>();
            }

            XDocument document = ReadXml(entry);
            return document
                .Descendants(SpreadsheetNamespace + "si")
                .Select(item => string.Concat(
                    item.Descendants(SpreadsheetNamespace + "t")
                        .Select(text => text.Value)))
                .ToList();
        }

        private Dictionary<string, string> ReadSheetParts()
        {
            XDocument workbook = ReadXml("xl/workbook.xml");
            XDocument relationships =
                ReadXml("xl/_rels/workbook.xml.rels");
            var targets = relationships
                .Descendants(PackageRelationshipNamespace + "Relationship")
                .ToDictionary(
                    relation => relation.Attribute("Id")?.Value ??
                        string.Empty,
                    relation => relation.Attribute("Target")?.Value ??
                        string.Empty,
                    StringComparer.Ordinal);
            var result = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (XElement sheet in workbook.Descendants(
                         SpreadsheetNamespace + "sheet"))
            {
                string name = sheet.Attribute("name")?.Value;
                string relationshipId = sheet.Attribute(
                    DocumentRelationshipNamespace + "id")?.Value;
                if (string.IsNullOrWhiteSpace(name) ||
                    string.IsNullOrWhiteSpace(relationshipId) ||
                    !targets.TryGetValue(
                        relationshipId,
                        out string target))
                {
                    throw new InvalidDataException(
                        "Workbook contains an invalid worksheet reference.");
                }

                result.Add(
                    name,
                    ResolvePartPath("xl/workbook.xml", target));
            }

            return result;
        }

        private void ValidateTableMetadata()
        {
            var tableIds = new HashSet<int>();
            var tableNames = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var tableDisplayNames = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> sheetPart
                     in sheetParts)
            {
                XDocument sheet = ReadXml(sheetPart.Value);
                List<XElement> tableParts = sheet
                    .Descendants(SpreadsheetNamespace + "tablePart")
                    .ToList();
                if (tableParts.Count == 0)
                {
                    continue;
                }

                string relationshipPart =
                    GetRelationshipPartPath(sheetPart.Value);
                Dictionary<string, string> relationshipTargets =
                    ReadRelationshipTargets(relationshipPart);
                foreach (XElement tablePart in tableParts)
                {
                    string relationshipId = tablePart.Attribute(
                        DocumentRelationshipNamespace + "id")?.Value;
                    if (string.IsNullOrWhiteSpace(relationshipId) ||
                        !relationshipTargets.TryGetValue(
                            relationshipId,
                            out string target))
                    {
                        throw new InvalidDataException(
                            $"{sheetPart.Key} contains an invalid " +
                            "table relationship.");
                    }

                    string tablePath = ResolvePartPath(
                        sheetPart.Value,
                        target);
                    ValidateTable(
                        sheetPart.Key,
                        sheet,
                        tablePath,
                        tableIds,
                        tableNames,
                        tableDisplayNames);
                }
            }
        }

        private void ValidateTable(
            string sheetName,
            XDocument sheet,
            string tablePath,
            HashSet<int> tableIds,
            HashSet<string> tableNames,
            HashSet<string> tableDisplayNames)
        {
            XDocument tableDocument = ReadXml(tablePath);
            XElement table = tableDocument.Root;
            if (table == null ||
                table.Name != SpreadsheetNamespace + "table")
            {
                throw new InvalidDataException(
                    $"Excel table part is invalid: {tablePath}");
            }

            int tableId = ReadPositiveInteger(
                table.Attribute("id")?.Value,
                0);
            string tableName = table.Attribute("name")?.Value;
            string displayName =
                table.Attribute("displayName")?.Value;
            if (tableId <= 0 || !tableIds.Add(tableId))
            {
                throw new InvalidDataException(
                    $"Excel table id is missing or duplicated: {tablePath}");
            }

            if (string.IsNullOrWhiteSpace(tableName) ||
                !tableNames.Add(tableName))
            {
                throw new InvalidDataException(
                    $"Excel table name is missing or duplicated: " +
                    $"{tablePath}");
            }

            if (string.IsNullOrWhiteSpace(displayName) ||
                !tableDisplayNames.Add(displayName))
            {
                throw new InvalidDataException(
                    $"Excel table display name is missing or duplicated: " +
                    $"{tablePath}");
            }

            string rangeReference = table.Attribute("ref")?.Value;
            ParseRangeReference(
                rangeReference,
                out int firstColumn,
                out int firstRow,
                out int lastColumn,
                out int lastRow);
            if (lastColumn < firstColumn || lastRow < firstRow)
            {
                throw new InvalidDataException(
                    $"{sheetName} table '{displayName}' has an invalid " +
                    $"range '{rangeReference}'.");
            }

            XElement tableColumns = table.Element(
                SpreadsheetNamespace + "tableColumns");
            List<XElement> columns = tableColumns?
                .Elements(SpreadsheetNamespace + "tableColumn")
                .ToList() ?? new List<XElement>();
            int declaredColumnCount = ReadPositiveInteger(
                tableColumns?.Attribute("count")?.Value,
                0);
            int rangeColumnCount = lastColumn - firstColumn + 1;
            if (columns.Count == 0 ||
                declaredColumnCount != columns.Count ||
                rangeColumnCount != columns.Count)
            {
                throw new InvalidDataException(
                    $"{sheetName} table '{displayName}' range has " +
                    $"{rangeColumnCount} columns, but its metadata has " +
                    $"{columns.Count}.");
            }

            int headerRowCount = ReadNonNegativeInteger(
                table.Attribute("headerRowCount")?.Value,
                1);
            if (headerRowCount == 0)
            {
                return;
            }

            XElement headerRow = sheet
                .Descendants(SpreadsheetNamespace + "row")
                .FirstOrDefault(row => ReadPositiveInteger(
                    row.Attribute("r")?.Value,
                    0) == firstRow);
            var headerCells = new Dictionary<int, XElement>();
            if (headerRow != null)
            {
                foreach (XElement cell in headerRow.Elements(
                             SpreadsheetNamespace + "c"))
                {
                    string reference = cell.Attribute("r")?.Value;
                    headerCells[GetColumnIndex(reference)] = cell;
                }
            }

            for (int index = 0; index < columns.Count; index++)
            {
                int columnIndex = firstColumn + index;
                string expected =
                    columns[index].Attribute("name")?.Value ??
                    string.Empty;
                string actual = string.Empty;
                if (headerCells.TryGetValue(
                        columnIndex,
                        out XElement cell))
                {
                    actual = ReadCellValue(
                        sheetName,
                        cell.Attribute("r")?.Value,
                        cell);
                }

                if (!string.Equals(
                        expected,
                        actual,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"{sheetName} table '{displayName}' header " +
                        $"{index + 1} differs from its table metadata. " +
                        $"Cell='{actual}', TableColumn='{expected}'.");
                }
            }
        }

        private Dictionary<string, string> ReadRelationshipTargets(
            string relationshipPart)
        {
            XDocument relationships = ReadXml(relationshipPart);
            return relationships
                .Descendants(
                    PackageRelationshipNamespace + "Relationship")
                .ToDictionary(
                    relation => relation.Attribute("Id")?.Value ??
                        string.Empty,
                    relation => relation.Attribute("Target")?.Value ??
                        string.Empty,
                    StringComparer.Ordinal);
        }

        private string ReadCellValue(
            string sheetName,
            string reference,
            XElement cell)
        {
            if (cell.Element(SpreadsheetNamespace + "f") != null)
            {
                throw new InvalidDataException(
                    $"{sheetName}!{reference} contains a formula. " +
                    "Game data cells must contain fixed values.");
            }

            string type = cell.Attribute("t")?.Value;
            if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
            {
                return string.Concat(
                    cell.Descendants(SpreadsheetNamespace + "t")
                        .Select(text => text.Value));
            }

            string raw = cell.Element(SpreadsheetNamespace + "v")?.Value ??
                string.Empty;
            if (string.Equals(type, "s", StringComparison.Ordinal))
            {
                if (!int.TryParse(
                        raw,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int index) ||
                    index < 0 ||
                    index >= sharedStrings.Count)
                {
                    throw new InvalidDataException(
                        $"{sheetName}!{reference} has an invalid shared string.");
                }

                return sharedStrings[index];
            }

            if (string.Equals(type, "b", StringComparison.Ordinal))
            {
                return raw == "1" ? "TRUE" : "FALSE";
            }

            if (string.Equals(type, "e", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{sheetName}!{reference} contains Excel error '{raw}'.");
            }

            return raw;
        }

        private XDocument ReadXml(string partPath)
        {
            ZipArchiveEntry entry = archive.GetEntry(partPath);
            if (entry == null)
            {
                throw new InvalidDataException(
                    $"Excel part is missing: {partPath}");
            }

            return ReadXml(entry);
        }

        private static XDocument ReadXml(ZipArchiveEntry entry)
        {
            using Stream entryStream = entry.Open();
            return XDocument.Load(entryStream, LoadOptions.None);
        }

        private static int GetColumnIndex(string cellReference)
        {
            if (string.IsNullOrWhiteSpace(cellReference))
            {
                throw new InvalidDataException(
                    "Worksheet contains a cell without a reference.");
            }

            int result = 0;
            int letterCount = 0;
            foreach (char value in cellReference)
            {
                if (!char.IsLetter(value))
                {
                    break;
                }

                result = result * 26 +
                    (char.ToUpperInvariant(value) - 'A' + 1);
                letterCount++;
            }

            if (letterCount == 0)
            {
                throw new InvalidDataException(
                    $"Invalid cell reference: {cellReference}");
            }

            return result - 1;
        }

        private static void ParseRangeReference(
            string rangeReference,
            out int firstColumn,
            out int firstRow,
            out int lastColumn,
            out int lastRow)
        {
            string[] references = (rangeReference ?? string.Empty)
                .Split(':');
            if (references.Length == 0 ||
                references.Length > 2)
            {
                throw new InvalidDataException(
                    $"Invalid Excel table range: {rangeReference}");
            }

            ParseCellReference(
                references[0],
                out firstColumn,
                out firstRow);
            ParseCellReference(
                references.Length == 2
                    ? references[1]
                    : references[0],
                out lastColumn,
                out lastRow);
        }

        private static void ParseCellReference(
            string cellReference,
            out int columnIndex,
            out int rowNumber)
        {
            string normalized =
                (cellReference ?? string.Empty).Replace("$", string.Empty);
            int rowStart = 0;
            while (rowStart < normalized.Length &&
                   char.IsLetter(normalized[rowStart]))
            {
                rowStart++;
            }

            if (rowStart == 0 ||
                !int.TryParse(
                    normalized.Substring(rowStart),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out rowNumber) ||
                rowNumber <= 0)
            {
                throw new InvalidDataException(
                    $"Invalid cell reference: {cellReference}");
            }

            columnIndex = GetColumnIndex(normalized);
        }

        private static int ReadPositiveInteger(
            string value,
            int fallback)
        {
            return int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int result) &&
                result > 0
                ? result
                : fallback;
        }

        private static int ReadNonNegativeInteger(
            string value,
            int fallback)
        {
            return int.TryParse(
                    value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int result) &&
                result >= 0
                ? result
                : fallback;
        }

        private static string GetRelationshipPartPath(string sourcePart)
        {
            int separatorIndex = sourcePart.LastIndexOf('/');
            if (separatorIndex < 0 ||
                separatorIndex >= sourcePart.Length - 1)
            {
                throw new InvalidDataException(
                    $"Invalid Excel part path: {sourcePart}");
            }

            string directory =
                sourcePart.Substring(0, separatorIndex);
            string fileName = sourcePart.Substring(separatorIndex + 1);
            return $"{directory}/_rels/{fileName}.rels";
        }

        private static string ResolvePartPath(
            string sourcePart,
            string target)
        {
            string normalizedTarget = target.Replace('\\', '/');
            if (normalizedTarget.StartsWith("/", StringComparison.Ordinal))
            {
                return normalizedTarget.TrimStart('/');
            }

            string sourceDirectory = sourcePart.Substring(
                0,
                sourcePart.LastIndexOf('/', sourcePart.Length - 1));
            var parts = new List<string>();
            foreach (string part in
                     $"{sourceDirectory}/{normalizedTarget}".Split('/'))
            {
                if (part == "..")
                {
                    if (parts.Count == 0)
                    {
                        throw new InvalidDataException(
                            $"Invalid Excel relationship target: {target}");
                    }

                    parts.RemoveAt(parts.Count - 1);
                }
                else if (part != "." && part.Length > 0)
                {
                    parts.Add(part);
                }
            }

            return string.Join("/", parts);
        }
    }
}
