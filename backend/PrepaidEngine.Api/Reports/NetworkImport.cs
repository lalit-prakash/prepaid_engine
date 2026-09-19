using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Reports;

/// <summary>One line of a hierarchy file: the full path down to one DTR, each level with a code and a name.</summary>
public sealed record NetworkImportRow(
    string? ZoneCode, string? ZoneName, string? CircleCode, string? CircleName, string? DivisionCode, string? DivisionName,
    string? SubDivisionCode, string? SubDivisionName, string? SubstationCode, string? SubstationName,
    string? FeederCode, string? FeederName, string? DtrCode, string? DtrName);

/// <summary>One line of a consumer mapping file: which DTR a consumer is supplied from.</summary>
public sealed record ConsumerMappingRow(string? AccountNumber, string? DtrCode);

public sealed record ImportRowResult(int Row, bool Ok, string? Message);

/// <summary>Outcome of an import or a dry run. Nothing is saved when any row has an error, or when <see cref="DryRun"/> is true.</summary>
public sealed record ImportResult(
    bool DryRun, bool Saved, int RowsRead, int RowsWithErrors,
    Dictionary<string, int> Created, Dictionary<string, int> Renamed, int ConsumersMapped, int ConsumersRemapped, int ConsumersUnchanged,
    List<ImportRowResult> Errors);

/// <summary>
/// Loads the supply network from flat files, without inventing anything: hierarchy rows are matched to nodes by
/// code (new codes are created, a changed name renames the node, a code that already sits under a different
/// parent is an error), and consumers are mapped to DTRs by account number and DTR code. Each call is
/// all-or-nothing, so a bad file changes nothing, and a dry run reports exactly what a real run would do.
/// </summary>
public static class NetworkImport
{
    public const int MaxRows = 10_000;
    private static readonly string[] Levels = { "Zone", "Circle", "Division", "SubDivision", "Substation", "Feeder", "Dtr" };

    public static async Task<ImportResult> ImportHierarchyAsync(
        PrepaidEngineDbContext db, IReadOnlyList<NetworkImportRow> rows, bool dryRun, string actor, CancellationToken ct = default)
    {
        var errors = new List<ImportRowResult>();
        var created = Levels.ToDictionary(l => l, _ => 0);
        var renamed = Levels.ToDictionary(l => l, _ => 0);

        // Existing nodes for every code in the file, one query per level.
        string[] Codes(Func<NetworkImportRow, string?> pick) => rows.Select(pick).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.Trim()).Distinct().ToArray();
        var zoneCodes = Codes(r => r.ZoneCode); var circleCodes = Codes(r => r.CircleCode); var divisionCodes = Codes(r => r.DivisionCode);
        var subDivisionCodes = Codes(r => r.SubDivisionCode); var substationCodes = Codes(r => r.SubstationCode);
        var feederCodes = Codes(r => r.FeederCode); var dtrCodes = Codes(r => r.DtrCode);

        var zones = await db.Zones.Where(n => zoneCodes.Contains(n.Code)).ToDictionaryAsync(n => n.Code, ct);
        var circles = await db.Circles.Where(n => circleCodes.Contains(n.Code)).ToDictionaryAsync(n => n.Code, ct);
        var divisions = await db.Divisions.Where(n => divisionCodes.Contains(n.Code)).ToDictionaryAsync(n => n.Code, ct);
        var subDivisions = await db.SubDivisions.Where(n => subDivisionCodes.Contains(n.Code)).ToDictionaryAsync(n => n.Code, ct);
        var substations = await db.Substations.Where(n => substationCodes.Contains(n.Code)).ToDictionaryAsync(n => n.Code, ct);
        var feeders = await db.Feeders.Where(n => feederCodes.Contains(n.Code)).ToDictionaryAsync(n => n.Code, ct);
        var dtrs = await db.Dtrs.Where(n => dtrCodes.Contains(n.Code)).ToDictionaryAsync(n => n.Code, ct);

        // Names seen earlier in this file, so two rows cannot give one code two different names.
        var namesInFile = new Dictionary<(string Level, string Code), string>();
        var touched = new HashSet<(string Level, string Code)>();

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var rowNumber = i + 1;
            var problem = Validate(row);
            if (problem is not null) { errors.Add(new ImportRowResult(rowNumber, false, problem)); continue; }

            string Code(string? s) => s!.Trim();
            string Name(string? s) => s!.Trim();

            string? Check(string level, string code, string name)
            {
                if (namesInFile.TryGetValue((level, code), out var earlier) && earlier != name)
                    return $"{level} code {code} is given two different names in this file ('{earlier}' and '{name}').";
                namesInFile[(level, code)] = name;
                return null;
            }

            // Walk down the path, reusing existing nodes and creating missing ones. Any parent mismatch stops the row.
            string? error = null;
            Zone? zone = null; Circle? circle = null; Division? division = null; SubDivision? subDivision = null; Substation? substation = null; Feeder? feeder = null;

            var zc = Code(row.ZoneCode); var zn = Name(row.ZoneName);
            error = Check("Zone", zc, zn);
            if (error is null)
            {
                if (!zones.TryGetValue(zc, out zone)) { zone = new Zone(Guid.NewGuid(), zc, zn); zones[zc] = zone; db.Zones.Add(zone); Bump(created, "Zone"); }
                else if (zone.Name != zn && touched.Add(("Zone", zc))) { zone.Rename(zn); Bump(renamed, "Zone"); }
            }

            if (error is null)
            {
                var c = Code(row.CircleCode); var n = Name(row.CircleName);
                error = Check("Circle", c, n);
                if (error is null)
                {
                    if (!circles.TryGetValue(c, out circle)) { circle = new Circle(Guid.NewGuid(), zone!.Id, c, n); circles[c] = circle; db.Circles.Add(circle); Bump(created, "Circle"); }
                    else if (circle.ZoneId != zone!.Id) error = Elsewhere("Circle", c);
                    else if (circle.Name != n && touched.Add(("Circle", c))) { circle.Rename(n); Bump(renamed, "Circle"); }
                }
            }

            if (error is null)
            {
                var c = Code(row.DivisionCode); var n = Name(row.DivisionName);
                error = Check("Division", c, n);
                if (error is null)
                {
                    if (!divisions.TryGetValue(c, out division)) { division = new Division(Guid.NewGuid(), circle!.Id, c, n); divisions[c] = division; db.Divisions.Add(division); Bump(created, "Division"); }
                    else if (division.CircleId != circle!.Id) error = Elsewhere("Division", c);
                    else if (division.Name != n && touched.Add(("Division", c))) { division.Rename(n); Bump(renamed, "Division"); }
                }
            }

            if (error is null)
            {
                var c = Code(row.SubDivisionCode); var n = Name(row.SubDivisionName);
                error = Check("SubDivision", c, n);
                if (error is null)
                {
                    if (!subDivisions.TryGetValue(c, out subDivision)) { subDivision = new SubDivision(Guid.NewGuid(), division!.Id, c, n); subDivisions[c] = subDivision; db.SubDivisions.Add(subDivision); Bump(created, "SubDivision"); }
                    else if (subDivision.DivisionId != division!.Id) error = Elsewhere("Sub-division", c);
                    else if (subDivision.Name != n && touched.Add(("SubDivision", c))) { subDivision.Rename(n); Bump(renamed, "SubDivision"); }
                }
            }

            if (error is null)
            {
                var c = Code(row.SubstationCode); var n = Name(row.SubstationName);
                error = Check("Substation", c, n);
                if (error is null)
                {
                    if (!substations.TryGetValue(c, out substation)) { substation = new Substation(Guid.NewGuid(), subDivision!.Id, c, n); substations[c] = substation; db.Substations.Add(substation); Bump(created, "Substation"); }
                    else if (substation.SubDivisionId != subDivision!.Id) error = Elsewhere("Substation", c);
                    else if (substation.Name != n && touched.Add(("Substation", c))) { substation.Rename(n); Bump(renamed, "Substation"); }
                }
            }

            if (error is null)
            {
                var c = Code(row.FeederCode); var n = Name(row.FeederName);
                error = Check("Feeder", c, n);
                if (error is null)
                {
                    if (!feeders.TryGetValue(c, out feeder)) { feeder = new Feeder(Guid.NewGuid(), substation!.Id, c, n); feeders[c] = feeder; db.Feeders.Add(feeder); Bump(created, "Feeder"); }
                    else if (feeder.SubstationId != substation!.Id) error = Elsewhere("Feeder", c);
                    else if (feeder.Name != n && touched.Add(("Feeder", c))) { feeder.Rename(n); Bump(renamed, "Feeder"); }
                }
            }

            if (error is null)
            {
                var c = Code(row.DtrCode); var n = Name(row.DtrName);
                error = Check("Dtr", c, n);
                if (error is null)
                {
                    if (!dtrs.TryGetValue(c, out var dtr)) { dtr = new Dtr(Guid.NewGuid(), feeder!.Id, c, n); dtrs[c] = dtr; db.Dtrs.Add(dtr); Bump(created, "Dtr"); }
                    else if (dtr.FeederId != feeder!.Id) error = Elsewhere("DTR", c);
                    else if (dtr.Name != n && touched.Add(("Dtr", c))) { dtr.Rename(n); Bump(renamed, "Dtr"); }
                }
            }

            if (error is not null) errors.Add(new ImportRowResult(rowNumber, false, error));
        }

        var saved = false;
        if (errors.Count == 0 && !dryRun)
        {
            db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "NetworkHierarchy", "import", "HIERARCHY_IMPORTED", actor, DateTime.UtcNow, null, null,
                $"{rows.Count} rows; created {Summary(created)}; renamed {Summary(renamed)}."));
            await db.SaveChangesAsync(ct);
            saved = true;
        }
        else
        {
            db.ChangeTracker.Clear(); // a dry run or a rejected file changes nothing
        }

        return new ImportResult(dryRun, saved, rows.Count, errors.Count, created, renamed, 0, 0, 0, errors);
    }

    public static async Task<ImportResult> MapConsumersAsync(
        PrepaidEngineDbContext db, IReadOnlyList<ConsumerMappingRow> rows, bool dryRun, string actor, CancellationToken ct = default)
    {
        var errors = new List<ImportRowResult>();
        var accounts = rows.Select(r => r.AccountNumber?.Trim()).Where(a => !string.IsNullOrEmpty(a)).Distinct().ToArray();
        var codes = rows.Select(r => r.DtrCode?.Trim()).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToArray();
        var consumers = await db.Consumers.Where(c => accounts.Contains(c.AccountNumber)).ToDictionaryAsync(c => c.AccountNumber, ct);
        var dtrs = await db.Dtrs.Where(d => codes.Contains(d.Code)).ToDictionaryAsync(d => d.Code, ct);

        int mapped = 0, remapped = 0, unchanged = 0;
        var seen = new HashSet<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var account = rows[i].AccountNumber?.Trim();
            var code = rows[i].DtrCode?.Trim();
            if (string.IsNullOrEmpty(account) || string.IsNullOrEmpty(code)) { errors.Add(new ImportRowResult(i + 1, false, "Account number and DTR code are both required.")); continue; }
            if (!seen.Add(account)) { errors.Add(new ImportRowResult(i + 1, false, $"Account {account} appears more than once in this file.")); continue; }
            if (!consumers.TryGetValue(account, out var consumer)) { errors.Add(new ImportRowResult(i + 1, false, $"No consumer with account number {account}.")); continue; }
            if (!dtrs.TryGetValue(code, out var dtr)) { errors.Add(new ImportRowResult(i + 1, false, $"No DTR with code {code}. Import the hierarchy first.")); continue; }

            if (consumer.DtrId == dtr.Id) unchanged++;
            else
            {
                if (consumer.DtrId is null) mapped++; else remapped++;
                consumer.AssignDtr(dtr.Id);
            }
        }

        var saved = false;
        if (errors.Count == 0 && !dryRun)
        {
            db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "NetworkHierarchy", "consumer-mapping", "CONSUMERS_MAPPED_TO_DTR", actor, DateTime.UtcNow, null, null,
                $"{rows.Count} rows; {mapped} newly mapped, {remapped} moved to a different DTR, {unchanged} unchanged."));
            await db.SaveChangesAsync(ct);
            saved = true;
        }
        else
        {
            db.ChangeTracker.Clear();
        }

        return new ImportResult(dryRun, saved, rows.Count, errors.Count, new Dictionary<string, int>(), new Dictionary<string, int>(), mapped, remapped, unchanged, errors);
    }

    private static string? Validate(NetworkImportRow r)
    {
        var fields = new (string Label, string? Code, string? Name)[]
        {
            ("Zone", r.ZoneCode, r.ZoneName), ("Circle", r.CircleCode, r.CircleName), ("Division", r.DivisionCode, r.DivisionName),
            ("Sub-division", r.SubDivisionCode, r.SubDivisionName), ("Substation", r.SubstationCode, r.SubstationName),
            ("Feeder", r.FeederCode, r.FeederName), ("DTR", r.DtrCode, r.DtrName),
        };
        foreach (var (label, code, name) in fields)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) return $"{label} code and name are both required.";
            if (code.Trim().Length > 40) return $"{label} code is longer than 40 characters.";
            if (name.Trim().Length > 200) return $"{label} name is longer than 200 characters.";
        }
        return null;
    }

    private static string Elsewhere(string label, string code) => $"{label} code {code} already exists under a different parent.";

    private static void Bump(Dictionary<string, int> counts, string level) => counts[level]++;

    private static string Summary(Dictionary<string, int> counts)
        => string.Join(", ", counts.Where(kv => kv.Value > 0).Select(kv => $"{kv.Value} {kv.Key}")) is { Length: > 0 } s ? s : "none";
}
