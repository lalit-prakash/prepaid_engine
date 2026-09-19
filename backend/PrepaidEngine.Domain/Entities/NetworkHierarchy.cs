namespace PrepaidEngine.Domain.Entities;

// The utility's supply network, top to bottom: Zone > Circle > Division > Sub-division > Substation >
// Feeder > Distribution transformer (DTR). A consumer hangs off one DTR (Consumer.DtrId), so its whole
// path is known from that single link and every report can show or filter by any level.

/// <summary>The levels of the network hierarchy, in order from the top.</summary>
public enum NetworkLevel
{
    Zone = 0,
    Circle = 1,
    Division = 2,
    SubDivision = 3,
    Substation = 4,
    Feeder = 5,
    Dtr = 6,
}

public abstract class NetworkNodeBase
{
    public Guid Id { get; protected set; }

    /// <summary>Short unique code used by the utility for this node (for example a feeder number).</summary>
    public string Code { get; protected set; } = string.Empty;

    public string Name { get; protected set; } = string.Empty;

    /// <summary>Changes the display name; the code, which identifies the node, never changes.</summary>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A name is required.", nameof(name));
        Name = name.Trim();
    }

    protected static void Require(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ArgumentException("A code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A name is required.", nameof(name));
    }
}

public class Zone : NetworkNodeBase
{
    public Zone(Guid id, string code, string name) { Require(code, name); Id = id; Code = code; Name = name; }
    private Zone() { }
}

public class Circle : NetworkNodeBase
{
    public Guid ZoneId { get; private set; }
    public Zone Zone { get; private set; } = null!;
    public Circle(Guid id, Guid zoneId, string code, string name) { Require(code, name); Id = id; ZoneId = zoneId; Code = code; Name = name; }
    private Circle() { }
}

public class Division : NetworkNodeBase
{
    public Guid CircleId { get; private set; }
    public Circle Circle { get; private set; } = null!;
    public Division(Guid id, Guid circleId, string code, string name) { Require(code, name); Id = id; CircleId = circleId; Code = code; Name = name; }
    private Division() { }
}

public class SubDivision : NetworkNodeBase
{
    public Guid DivisionId { get; private set; }
    public Division Division { get; private set; } = null!;
    public SubDivision(Guid id, Guid divisionId, string code, string name) { Require(code, name); Id = id; DivisionId = divisionId; Code = code; Name = name; }
    private SubDivision() { }
}

public class Substation : NetworkNodeBase
{
    public Guid SubDivisionId { get; private set; }
    public SubDivision SubDivision { get; private set; } = null!;
    public Substation(Guid id, Guid subDivisionId, string code, string name) { Require(code, name); Id = id; SubDivisionId = subDivisionId; Code = code; Name = name; }
    private Substation() { }
}

public class Feeder : NetworkNodeBase
{
    public Guid SubstationId { get; private set; }
    public Substation Substation { get; private set; } = null!;
    public Feeder(Guid id, Guid substationId, string code, string name) { Require(code, name); Id = id; SubstationId = substationId; Code = code; Name = name; }
    private Feeder() { }
}

/// <summary>Distribution transformer: the last level, and the node consumers are attached to.</summary>
public class Dtr : NetworkNodeBase
{
    public Guid FeederId { get; private set; }
    public Feeder Feeder { get; private set; } = null!;
    public Dtr(Guid id, Guid feederId, string code, string name) { Require(code, name); Id = id; FeederId = feederId; Code = code; Name = name; }
    private Dtr() { }
}
