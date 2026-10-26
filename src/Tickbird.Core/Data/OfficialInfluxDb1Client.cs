using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using Newtonsoft.Json.Linq;
using OfficialInfluxDbClient = InfluxDB.Client.InfluxDBClient;

namespace Tickbird.Core.Data
{
    /// <summary>
    /// InfluxDB 1.8 client backed by InfluxData's official .NET client.
    /// Legacy InfluxQL administration uses the 1.x compatibility endpoint because
    /// the official package intentionally exposes only the v2-compatible APIs.
    /// </summary>
    public sealed class OfficialInfluxDb1Client : InfluxDbClient
    {
        private readonly OfficialInfluxDbClient influx;
        private readonly HttpClient influxQl;

        public OfficialInfluxDb1Client(InfluxDbConnection connection) : base(connection)
        {
            var allowUntrusted = InfluxDbClientFactory.AllowUntrustedSsl();
            var options = new InfluxDBClientOptions(connection.HttpConnectionString)
            {
                Org = "ignored",
                Bucket = connection.Database,
                VerifySsl = !allowUntrusted
            };
            if (!string.IsNullOrWhiteSpace(connection.Username)
                || !string.IsNullOrWhiteSpace(connection.Password))
            {
                options.Token = (connection.Username ?? string.Empty) + ":" + (connection.Password ?? string.Empty);
            }
            influx = new OfficialInfluxDbClient(options);

            var handler = new HttpClientHandler();
            if (allowUntrusted)
                handler.ServerCertificateCustomValidationCallback = delegate { return true; };
            influxQl = new HttpClient(handler)
            {
                BaseAddress = new Uri(connection.HttpConnectionString.TrimEnd('/') + "/"),
                // Each request sets its own limit: DefaultRequestTimeout, or the query timeout / Stop button for queries.
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
            if (!string.IsNullOrWhiteSpace(connection.Username) || !string.IsNullOrWhiteSpace(connection.Password))
            {
                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes((connection.Username ?? string.Empty) + ":" + (connection.Password ?? string.Empty)));
                influxQl.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            }
        }

        public override async Task<IEnumerable<string>> GetDatabaseNamesAsync() =>
            ColumnValues(await QueryInfluxQlAsync(null, "SHOW DATABASES"), "name").ToList();

        public override Task<InfluxDbApiResponse> CreateDatabaseAsync(string database) =>
            ExecuteCommandAsync(null, "CREATE DATABASE " + QuoteIdentifier(database));

        public override Task<InfluxDbApiResponse> DropDatabaseAsync(string database) =>
            ExecuteCommandAsync(null, "DROP DATABASE " + QuoteIdentifier(database));

        public override async Task<IEnumerable<InfluxDbRetentionPolicy>> GetRetentionPoliciesAsync(string database)
        {
            var result = new List<InfluxDbRetentionPolicy>();
            foreach (var series in await QueryInfluxQlAsync(database,
                         "SHOW RETENTION POLICIES ON " + QuoteIdentifier(database)))
            {
                foreach (var row in series.Values)
                {
                    result.Add(new InfluxDbRetentionPolicy
                    {
                        Database = database,
                        Name = GetString(series, row, "name"),
                        Duration = GetString(series, row, "duration"),
                        ShardGroupDuration = GetString(series, row, "shardGroupDuration"),
                        ReplicationCopies = GetInt(series, row, "replicaN"),
                        Default = GetBool(series, row, "default")
                    });
                }
            }
            return result;
        }

        public override async Task<InfluxDbApiResponse> CreateRetentionPolicyAsync(
            string database, string policyName, string duration, int replication, bool isDefault = false)
        {
            Require(database, "database"); Require(policyName, "policyName"); Require(duration, "duration");
            var query = "CREATE RETENTION POLICY " + QuoteIdentifier(policyName) + " ON "
                + QuoteIdentifier(database) + " DURATION " + duration + " REPLICATION " + Math.Max(1, replication)
                + (isDefault ? " DEFAULT" : string.Empty);
            return await ExecuteCommandAsync(database, query);
        }

        public override Task<InfluxDbApiResponse> AlterRetentionPolicyAsync(
            string database, string policyName, string duration, int replication, bool isDefault = false)
        {
            Require(database, "database"); Require(policyName, "policyName"); Require(duration, "duration");
            var query = "ALTER RETENTION POLICY " + QuoteIdentifier(policyName) + " ON "
                + QuoteIdentifier(database) + " DURATION " + duration + " REPLICATION " + Math.Max(1, replication)
                + (isDefault ? " DEFAULT" : string.Empty);
            return ExecuteCommandAsync(database, query);
        }

        public override Task<InfluxDbApiResponse> DropRetentionPolicyAsync(string database, string policyName) =>
            ExecuteCommandAsync(database, "DROP RETENTION POLICY " + QuoteIdentifier(policyName)
                + " ON " + QuoteIdentifier(database));

        public override async Task<IEnumerable<string>> GetMeasurementNamesAsync(string database) =>
            ColumnValues(await QueryInfluxQlAsync(database, "SHOW MEASUREMENTS"), "name").ToList();

        public override Task<InfluxDbApiResponse> DropMeasurementAsync(string database, string measurement) =>
            ExecuteCommandAsync(database, "DROP MEASUREMENT " + QuoteIdentifier(measurement));

        public override async Task<IEnumerable<string>> GetTagKeysAsync(string database, string measurement) =>
            ColumnValues(await QueryInfluxQlAsync(database,
                "SHOW TAG KEYS FROM " + QuoteIdentifier(measurement)), "tagKey").Distinct().ToList();

        public override async Task<IEnumerable<InfluxDbTagValue>> GetTagValuesAsync(
            string database, string measurement, string tag)
        {
            var values = ColumnValues(await QueryInfluxQlAsync(database,
                "SHOW TAG VALUES FROM " + QuoteIdentifier(measurement) + " WITH KEY = " + QuoteIdentifier(tag)), "value");
            return values.Select(value => new InfluxDbTagValue(tag, value)).ToList();
        }

        public override async Task<IEnumerable<InfluxDbFieldKey>> GetFieldKeysAsync(
            string database, string measurement)
        {
            var result = new List<InfluxDbFieldKey>();
            // Measurements in the tree span all policies, so their fields must too.
            var seen = new HashSet<(string Name, string Type)>();
            foreach (var policy in await GetRetentionPoliciesAsync(database))
            {
                var source = QuoteIdentifier(database) + "." + QuoteIdentifier(policy.Name)
                    + "." + QuoteIdentifier(measurement);
                foreach (var series in await QueryInfluxQlAsync(database, "SHOW FIELD KEYS FROM " + source))
                    foreach (var row in series.Values)
                    {
                        var name = GetString(series, row, "fieldKey");
                        var type = GetString(series, row, "fieldType");
                        if (seen.Add((name, type))) result.Add(new InfluxDbFieldKey(name, type));
                    }
            }
            return result;
        }

        /// <summary>
        /// Whole-database schema in a few queries: field keys from every retention policy
        /// (FROM db.rp./.*/) and tag keys for all measurements.
        /// </summary>
        public override async Task<IReadOnlyList<InfluxDbMeasurementSchema>> GetDatabaseSchemaAsync(string database)
        {
            Require(database, "database");
            var fieldSeries = new List<InfluxDbSeries>();
            List<string> policies;
            try { policies = (await GetRetentionPoliciesAsync(database)).Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(); }
            catch (Exception) { policies = new List<string>(); }
            if (policies.Count == 0) fieldSeries.AddRange(await QueryInfluxQlAsync(database, "SHOW FIELD KEYS"));
            foreach (var policy in policies)
                fieldSeries.AddRange(await QueryInfluxQlAsync(database,
                    "SHOW FIELD KEYS FROM " + QuoteIdentifier(database) + "." + QuoteIdentifier(policy) + "./.*/"));
            var tagSeries = await QueryInfluxQlAsync(database, "SHOW TAG KEYS");
            IEnumerable<string> measurements;
            try { measurements = await GetMeasurementNamesAsync(database); }
            catch (Exception) { measurements = Enumerable.Empty<string>(); }
            return InfluxDbMeasurementSchema.FromInfluxQl(fieldSeries, tagSeries, measurements);
        }

        public override async Task<IEnumerable<string>> GetSeriesNamesAsync(
            string database, string measurement = null)
        {
            var query = "SHOW SERIES" + (string.IsNullOrWhiteSpace(measurement)
                ? string.Empty : " FROM " + QuoteIdentifier(measurement));
            return ColumnValues(await QueryInfluxQlAsync(database, query), "key").ToList();
        }

        public override async Task<IEnumerable<InfluxDbFieldKey>> GetFieldKeysAsync(
            string database, string measurement, string retentionPolicy)
        {
            Require(retentionPolicy, "retentionPolicy");
            var source = QuoteIdentifier(database) + "." + QuoteIdentifier(retentionPolicy)
                + "." + QuoteIdentifier(measurement);
            var fields = new List<InfluxDbFieldKey>();
            foreach (var series in await QueryInfluxQlAsync(database, "SHOW FIELD KEYS FROM " + source))
                foreach (var row in series.Values)
                    fields.Add(new InfluxDbFieldKey(GetString(series, row, "fieldKey"),
                        GetString(series, row, "fieldType")));
            return fields.GroupBy(field => (field.Name, field.Type)).Select(group => group.First()).ToList();
        }

        public override Task<InfluxDbApiResponse> DropSeriesAsync(string database, string measurement = null) =>
            ExecuteCommandAsync(database, "DROP SERIES" + (string.IsNullOrWhiteSpace(measurement)
                ? string.Empty : " FROM " + QuoteIdentifier(measurement)));

        public override async Task<IEnumerable<InfluxDbRunningQuery>> GetRunningQueriesAsync()
        {
            var result = new List<InfluxDbRunningQuery>();
            foreach (var series in await QueryInfluxQlAsync("_internal", "SHOW QUERIES"))
                foreach (var row in series.Values)
                    result.Add(new InfluxDbRunningQuery(GetInt(series, row, "qid"),
                        GetString(series, row, "database"), GetString(series, row, "duration"),
                        GetString(series, row, "query")));
            return result;
        }

        public override Task<InfluxDbApiResponse> KillQueryAsync(int pid) =>
            ExecuteCommandAsync(null, "KILL QUERY " + pid.ToString(CultureInfo.InvariantCulture));

        public override Task<IEnumerable<InfluxDbSeries>> QueryAsync(string database, string query) =>
            QueryInfluxQlAsync(database, query);

        /// <summary>Cancelling closes the HTTP request; InfluxDB 1.x then stops the query on the server too.</summary>
        public override Task<IEnumerable<InfluxDbSeries>> QueryAsync(string database, string query, CancellationToken cancellationToken) =>
            QueryInfluxQlAsync(database, query, cancellationToken);

        public override async Task<IEnumerable<InfluxDbContinuousQuery>> GetContinousQueriesAsync(string database)
        {
            var result = new List<InfluxDbContinuousQuery>();
            foreach (var series in await QueryInfluxQlAsync(database, "SHOW CONTINUOUS QUERIES"))
            {
                if (!string.Equals(series.Name, database, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var row in series.Values)
                    result.Add(new InfluxDbContinuousQuery(GetString(series, row, "name"),
                        GetString(series, row, "query")));
            }
            return result;
        }

        public override Task<InfluxDbApiResponse> CreateContinuousQueryAsync(InfluxDbCqParams value)
        {
            if (value == null) throw new ArgumentNullException("cqParams");
            Require(value.Name, "cqParams.Name"); Require(value.Database, "cqParams.Database");
            Require(value.Destination, "cqParams.Destination"); Require(value.Source, "cqParams.Source");
            Require(value.Interval, "cqParams.Interval");
            var subQueries = value.SubQueries == null ? new List<string>() : value.SubQueries.ToList();
            if (subQueries.Count == 0) throw new ArgumentException("cqParams.SubQueries needs at least one query.");

            var resample = string.Empty;
            if (!string.IsNullOrWhiteSpace(value.ResampleEveryInterval))
                resample += " EVERY " + value.ResampleEveryInterval;
            if (!string.IsNullOrWhiteSpace(value.ResampleForInterval))
                resample += " FOR " + value.ResampleForInterval;
            if (resample.Length > 0) resample = " RESAMPLE" + resample;

            var group = "time(" + value.Interval + ")";
            if (value.Tags != null && value.Tags.Any())
                group += ", " + string.Join(", ", value.Tags.Select(QuoteIdentifier));
            var query = "CREATE CONTINUOUS QUERY " + QuoteIdentifier(value.Name) + " ON "
                + QuoteIdentifier(value.Database) + resample + " BEGIN SELECT " + string.Join(", ", subQueries)
                + " INTO " + QuoteIdentifier(value.Destination) + " FROM " + QuoteIdentifier(value.Source)
                + " GROUP BY " + group + " fill(" + value.FillType.ToString().ToLowerInvariant() + ") END";
            return ExecuteCommandAsync(value.Database, query);
        }

        public override Task<InfluxDbApiResponse> DropContinuousQueryAsync(string database, string cqName) =>
            ExecuteCommandAsync(database, "DROP CONTINUOUS QUERY " + QuoteIdentifier(cqName)
                + " ON " + QuoteIdentifier(database));

        public override Task<InfluxDbApiResponse> BackfillAsync(
            string database, InfluxDbBackfillParams value)
        {
            if (value == null) throw new ArgumentNullException("backfillParams");
            var filters = new List<string>
            {
                "time >= '" + value.FromTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "'",
                "time <= '" + value.ToTime.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + "'"
            };
            if (value.Filters != null) filters.AddRange(value.Filters.Where(filter => !string.IsNullOrWhiteSpace(filter)));
            var group = "time(" + value.Interval + ")";
            if (value.Tags != null && value.Tags.Any())
                group += ", " + string.Join(", ", value.Tags.Select(QuoteIdentifier));
            var query = "SELECT " + string.Join(", ", value.SubQueries) + " INTO " + QuoteIdentifier(value.Destination)
                + " FROM " + QuoteIdentifier(value.Source) + " WHERE " + string.Join(" AND ", filters)
                + " GROUP BY " + group + " fill(" + value.FillType.ToString().ToLowerInvariant() + ")";
            return ExecuteCommandAsync(database, query);
        }

        public override Task<InfluxDbApiResponse> WriteAsync(string database, string measurement,
            IDictionary<string, object> tags, IDictionary<string, object> fields, DateTime timeStamp,
            string retentionPolicy = null) =>
            WriteAsync(database, new InfluxDbPoint(measurement, tags, fields, timeStamp), retentionPolicy);

        public override Task<InfluxDbApiResponse> WriteAsync(
            string database, InfluxDbPoint point, string retentionPolicy = null) =>
            WriteAsync(database, new[] { point }, retentionPolicy);

        public override async Task<InfluxDbApiResponse> WriteAsync(
            string database, IEnumerable<InfluxDbPoint> points, string retentionPolicy = null)
        {
            Require(database, "database");
            if (points == null) throw new ArgumentNullException("points");
            var bucket = string.IsNullOrWhiteSpace(retentionPolicy)
                ? database : database + "/" + retentionPolicy;
            await influx.GetWriteApiAsync().WriteRecordsAsync(
                points.Select(ToLineProtocol), WritePrecision.Ns, bucket, "ignored", CancellationToken.None);
            return SuccessResponse();
        }

        public override async Task<InfluxDbPingResponse> PingAsync()
        {
            var watch = Stopwatch.StartNew();
            var success = await influx.PingAsync();
            var version = await influx.VersionAsync();
            watch.Stop();
            return new InfluxDbPingResponse(success, watch.Elapsed,
                string.IsNullOrWhiteSpace(version) ? "1.x" : version);
        }

        public override async Task<InfluxDbDiagnostics> GetDiagnosticsAsync()
        {
            var series = await QueryInfluxQlAsync(null, "SHOW DIAGNOSTICS");
            var diagnostics = new InfluxDbDiagnostics();
            foreach (var item in series)
            {
                var row = item.Values.FirstOrDefault();
                if (row == null) continue;
                if (item.Name == "build")
                {
                    diagnostics.Branch = GetString(item, row, "Branch");
                    diagnostics.BuildVersion = GetString(item, row, "Version");
                    diagnostics.Commit = GetString(item, row, "Commit");
                }
                else if (item.Name == "network") diagnostics.Hostname = GetString(item, row, "hostname");
                else if (item.Name == "runtime")
                {
                    diagnostics.GoArch = GetString(item, row, "GOARCH");
                    diagnostics.GoMaxProc = GetLong(item, row, "GOMAXPROCS");
                    diagnostics.GoOs = GetString(item, row, "GOOS");
                    diagnostics.GoVersion = GetString(item, row, "version");
                }
                else if (item.Name == "system")
                {
                    DateTime.TryParse(GetString(item, row, "currentTime"), null,
                        DateTimeStyles.RoundtripKind, out var current);
                    DateTime.TryParse(GetString(item, row, "started"), null,
                        DateTimeStyles.RoundtripKind, out var started);
                    diagnostics.CurrentTime = current;
                    diagnostics.Started = started;
                    diagnostics.PID = GetLong(item, row, "PID");
                    diagnostics.Uptime = InfluxDbDiagnostics.ParseGoDuration(GetString(item, row, "uptime"));
                }
            }
            return diagnostics;
        }

        public override async Task<InfluxDbStats> GetStatsAsync()
        {
            var groups = (await QueryInfluxQlAsync(null, "SHOW STATS"))
                .GroupBy(series => (series.Name ?? string.Empty).ToLowerInvariant())
                .ToDictionary(group => group.Key, group => (IEnumerable<InfluxDbSeries>)group.ToList());
            IEnumerable<InfluxDbSeries> Find(params string[] keys) =>
                keys.Select(key => groups.TryGetValue(key, out var value) ? value : null)
                    .FirstOrDefault(value => value != null);
            return new InfluxDbStats
            {
                CQ = Find("cq"), Database = Find("database"), Engine = Find("engine"),
                Httpd = Find("httpd"), QueryExecutor = Find("queryexecutor", "query_executor"),
                Runtime = Find("runtime"), Shard = Find("shard"), Subscriber = Find("subscriber"),
                Tsm1Cache = Find("tsm1_cache"), Tsm1Filestore = Find("tsm1_filestore"),
                Tsm1Wal = Find("tsm1_wal"), WAL = Find("wal"), Write = Find("write")
            };
        }

        public override async Task<IEnumerable<InfluxDbUser>> GetUsersAsync()
        {
            var result = new List<InfluxDbUser>();
            foreach (var series in await QueryInfluxQlAsync(null, "SHOW USERS"))
                foreach (var row in series.Values)
                    result.Add(new InfluxDbUser(GetString(series, row, "user"), GetBool(series, row, "admin")));
            return result;
        }

        public override Task<InfluxDbApiResponse> CreateUserAsync(string username, string password, bool isAdmin)
        {
            var query = "CREATE USER " + QuoteIdentifier(username) + " WITH PASSWORD " + QuoteLiteral(password);
            if (isAdmin) query += " WITH ALL PRIVILEGES";
            return ExecuteCommandAsync(null, query);
        }

        public override Task<InfluxDbApiResponse> DropUserAsync(string username) =>
            ExecuteCommandAsync(null, "DROP USER " + QuoteIdentifier(username));

        public override Task<InfluxDbApiResponse> SetPasswordAsync(string username, string password) =>
            ExecuteCommandAsync(null, "SET PASSWORD FOR " + QuoteIdentifier(username) + " = " + QuoteLiteral(password));

        public override async Task<IEnumerable<InfluxDbGrant>> GetPrivilegesAsync(string username)
        {
            var result = new List<InfluxDbGrant>();
            foreach (var series in await QueryInfluxQlAsync(null,
                         "SHOW GRANTS FOR " + QuoteIdentifier(username)))
                foreach (var row in series.Values)
                {
                    var privilege = ParsePrivilege(GetString(series, row, "privilege"));
                    result.Add(new InfluxDbGrant(GetString(series, row, "database"), privilege));
                }
            return result;
        }

        public override Task<InfluxDbApiResponse> GrantAdministratorAsync(string username) =>
            ExecuteCommandAsync(null, "GRANT ALL PRIVILEGES TO " + QuoteIdentifier(username));

        public override Task<InfluxDbApiResponse> RevokeAdministratorAsync(string username) =>
            ExecuteCommandAsync(null, "REVOKE ALL PRIVILEGES FROM " + QuoteIdentifier(username));

        public override Task<InfluxDbApiResponse> GrantPrivilegeAsync(
            string username, InfluxDbPrivileges privilege, string database) =>
            ExecuteCommandAsync(null, "GRANT " + PrivilegeText(privilege) + " ON "
                + QuoteIdentifier(database) + " TO " + QuoteIdentifier(username));

        public override Task<InfluxDbApiResponse> RevokePrivilegeAsync(
            string username, InfluxDbPrivileges privilege, string database) =>
            ExecuteCommandAsync(null, "REVOKE " + PrivilegeText(privilege) + " ON "
                + QuoteIdentifier(database) + " FROM " + QuoteIdentifier(username));

        /// <summary>Limit for requests that are not given their own cancellation (admin commands, schema reads).</summary>
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(100);

        private async Task<IEnumerable<InfluxDbSeries>> QueryInfluxQlAsync(string database, string query,
            CancellationToken cancellationToken = default)
        {
            Require(query, "query");
            var response = await SendInfluxQlAsync(database, query, cancellationToken);
            return ParseSeries(response.Body);
        }

        private async Task<InfluxDbApiResponse> ExecuteCommandAsync(string database, string query) =>
            await SendInfluxQlAsync(database, query);

        private async Task<InfluxDbApiResponse> SendInfluxQlAsync(string database, string query,
            CancellationToken cancellationToken = default)
        {
            using var defaultTimeout = cancellationToken.CanBeCanceled ? null : new CancellationTokenSource(DefaultRequestTimeout);
            var token = defaultTimeout?.Token ?? cancellationToken;
            var form = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("q", query)
            };
            if (!string.IsNullOrWhiteSpace(database))
                form.Add(new KeyValuePair<string, string>("db", database));
            using (var request = new HttpRequestMessage(HttpMethod.Post, "query"))
            {
                request.Content = new FormUrlEncodedContent(form);
                using (var response = await influxQl.SendAsync(request, token).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new HttpRequestException("InfluxDB 1.x request failed (" + (int)response.StatusCode
                            + " " + response.StatusCode + "): " + body);
                    ThrowIfInfluxError(body);
                    return new InfluxDbApiResponse(string.IsNullOrWhiteSpace(body) ? "{}" : body,
                        response.StatusCode, true);
                }
            }
        }

        private static IList<InfluxDbSeries> ParseSeries(string json)
        {
            var output = new List<InfluxDbSeries>();
            var root = JObject.Parse(json);
            foreach (var result in root["results"] as JArray ?? new JArray())
            {
                foreach (var series in result["series"] as JArray ?? new JArray())
                {
                    var columns = (series["columns"] as JArray ?? new JArray())
                        .Select(value => (string)value).ToList();
                    var values = (series["values"] as JArray ?? new JArray())
                        .Select(row => (IList<object>)((JArray)row).Select(ToValue).ToList()).ToList();
                    var tags = (series["tags"] as JObject)?.Properties()
                        .ToDictionary(property => property.Name, property => (string)property.Value)
                        ?? new Dictionary<string, string>();
                    output.Add(new InfluxDbSeries((string)series["name"], columns, tags, values));
                }
            }
            return output;
        }

        private static void ThrowIfInfluxError(string json)
        {
            var root = JObject.Parse(json);
            foreach (var result in root["results"] as JArray ?? new JArray())
            {
                var error = (string)result["error"];
                if (!string.IsNullOrWhiteSpace(error)) throw new InvalidOperationException(error);
            }
            var topError = (string)root["error"];
            if (!string.IsNullOrWhiteSpace(topError)) throw new InvalidOperationException(topError);
        }

        private static object ToValue(JToken token) =>
            token == null || token.Type == JTokenType.Null ? null
                : token is JValue value ? value.Value : token.ToString();

        private static IEnumerable<string> ColumnValues(IEnumerable<InfluxDbSeries> series, string column)
        {
            foreach (var item in series)
            {
                var index = item.GetColumnIndex(column);
                if (index < 0) continue;
                foreach (var row in item.Values)
                {
                    var value = Convert.ToString(row[index], CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(value)) yield return value;
                }
            }
        }

        private static string GetString(InfluxDbSeries series, IList<object> row, string column)
        {
            var index = series.GetColumnIndex(column);
            return index < 0 ? null : Convert.ToString(row[index], CultureInfo.InvariantCulture);
        }
        private static int GetInt(InfluxDbSeries series, IList<object> row, string column) =>
            int.TryParse(GetString(series, row, column), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
                ? value : 0;
        private static long GetLong(InfluxDbSeries series, IList<object> row, string column) =>
            long.TryParse(GetString(series, row, column), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
                ? value : 0;
        private static bool GetBool(InfluxDbSeries series, IList<object> row, string column) =>
            bool.TryParse(GetString(series, row, column), out var value) && value;

        private static string ToLineProtocol(InfluxDbPoint point)
        {
            if (point == null) throw new ArgumentNullException("point");
            if (point.Fields == null || point.Fields.Count == 0)
                throw new ArgumentException("An InfluxDB point must contain at least one field.", "point");
            var builder = new StringBuilder(EscapeKey(point.Measurement));
            if (point.Tags != null)
                foreach (var tag in point.Tags.OrderBy(item => item.Key))
                    builder.Append(',').Append(EscapeKey(tag.Key)).Append('=')
                        .Append(EscapeKey(Convert.ToString(tag.Value, CultureInfo.InvariantCulture)));
            builder.Append(' ').Append(string.Join(",", point.Fields.OrderBy(item => item.Key)
                .Select(field => EscapeKey(field.Key) + "=" + FormatField(field.Value))));
            var utc = point.TimeStamp.Kind == DateTimeKind.Utc ? point.TimeStamp : point.TimeStamp.ToUniversalTime();
            builder.Append(' ').Append(((utc.Ticks - DateTime.UnixEpoch.Ticks) * 100L)
                .ToString(CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static string FormatField(object value)
        {
            if (value == null) throw new ArgumentException("InfluxDB field values cannot be null.");
            if (value is string || value is char)
                return "\"" + Convert.ToString(value).Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            if (value is bool boolean) return boolean ? "true" : "false";
            if (value is byte || value is sbyte || value is short || value is ushort || value is int || value is long)
                return Convert.ToString(value, CultureInfo.InvariantCulture) + "i";
            if (value is uint || value is ulong)
                return Convert.ToString(value, CultureInfo.InvariantCulture) + "u";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string EscapeKey(string value) =>
            (value ?? string.Empty).Replace("\\", "\\\\").Replace(" ", "\\ ")
                .Replace(",", "\\,").Replace("=", "\\=");
        private static string QuoteIdentifier(string value)
        {
            Require(value, "identifier");
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
        private static string QuoteLiteral(string value) =>
            "'" + (value ?? string.Empty).Replace("\\", "\\\\").Replace("'", "\\'") + "'";
        private static string PrivilegeText(InfluxDbPrivileges privilege) =>
            privilege == InfluxDbPrivileges.Read ? "READ"
                : privilege == InfluxDbPrivileges.Write ? "WRITE"
                : privilege == InfluxDbPrivileges.All ? "ALL" : "NONE";
        private static InfluxDbPrivileges ParsePrivilege(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return InfluxDbPrivileges.None;
            if (value.IndexOf("ALL", StringComparison.OrdinalIgnoreCase) >= 0) return InfluxDbPrivileges.All;
            if (value.IndexOf("READ", StringComparison.OrdinalIgnoreCase) >= 0) return InfluxDbPrivileges.Read;
            if (value.IndexOf("WRITE", StringComparison.OrdinalIgnoreCase) >= 0) return InfluxDbPrivileges.Write;
            return InfluxDbPrivileges.None;
        }
        private static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(name);
        }
        private static InfluxDbApiResponse SuccessResponse() =>
            new InfluxDbApiResponse("{}", HttpStatusCode.NoContent, true);
    }
}
