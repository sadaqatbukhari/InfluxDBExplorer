using Tickbird.Core.Data;

namespace Tickbird.Core.Navigation;

public enum NodeKind { None, Connection, Database, Measurement, Loading, Group }

/// <summary>Which toolbar/menu commands are enabled (port of AppForm.UpdateUIState).</summary>
public sealed record CommandAvailability
{
    public bool Disconnect { get; init; }
    public bool Refresh { get; init; }
    public bool NewQuery { get; init; }
    public bool ShowQueries { get; init; }
    public bool ShowPolicies { get; init; }
    public bool ShowUsers { get; init; }
    public bool ShowStats { get; init; }
    public bool ShowDiagnostics { get; init; }
    public bool CreateDatabase { get; init; }
    public bool ContinuousQueries { get; init; }
    public bool Backfill { get; init; }
    public bool DropDatabase { get; init; }
    public bool TagKeys { get; init; }
    public bool TagValues { get; init; }
    public bool FieldKeys { get; init; }
    public bool ShowSeries { get; init; }
    public bool DropSeries { get; init; }
    public bool DropMeasurement { get; init; }
    public bool RunQuery { get; init; }
    public bool CommentQuery { get; init; }
    public bool OpenSaveQuery { get; init; }

    public static CommandAvailability For(NodeKind kind, string? nodeName, InfluxDbServerVersion? serverVersion,
        bool requestTabSelected, bool queryTabSelected)
    {
        var v1 = serverVersion == null || serverVersion == InfluxDbServerVersion.InfluxDb1;
        var has = kind is NodeKind.Connection or NodeKind.Database or NodeKind.Measurement;
        var conn = kind == NodeKind.Connection;
        var db = kind == NodeKind.Database;
        var meas = kind == NodeKind.Measurement;
        return new CommandAvailability
        {
            Disconnect = has,
            Refresh = conn || db,
            NewQuery = db || meas,
            ShowQueries = conn && v1,
            ShowPolicies = conn && v1,
            ShowUsers = conn && v1,
            ShowStats = conn && v1,
            ShowDiagnostics = conn && v1,
            CreateDatabase = conn,
            ContinuousQueries = db && v1,
            Backfill = db && v1,
            DropDatabase = db && nodeName != "_internal",
            TagKeys = meas,
            TagValues = meas,
            FieldKeys = meas,
            ShowSeries = meas && v1,
            DropSeries = meas && v1,
            DropMeasurement = meas,
            RunQuery = requestTabSelected,
            CommentQuery = queryTabSelected,
            OpenSaveQuery = queryTabSelected,
        };
    }
}
