using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Apache.Arrow;
using InfluxDB3.Client.Config;
using InfluxDB3.Client.Write;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OfficialInfluxDb3Client = InfluxDB3.Client.InfluxDBClient;

namespace Tickbird.Core.Data
{
    /// <summary>
    /// HTTP API client for InfluxDB 3 Core and Enterprise.
    /// </summary>
    public sealed class InfluxDb3Client : InfluxDbClient
    {
        private readonly OfficialInfluxDb3Client influx;
        private readonly HttpClient adminHttp;

        public InfluxDb3Client(InfluxDbConnection connection) : base(connection)
        {
            var allowUntrusted = InfluxDbClientFactory.AllowUntrustedSsl();
            influx = new OfficialInfluxDb3Client(new ClientConfig
            {
                Host = connection.HttpConnectionString,
                Token = connection.Token,
                Database = connection.Database,
                DisableServerCertificateValidation = allowUntrusted,
                WriteOptions = new WriteOptions { UseV2Api = false }
            });

            var handler = new HttpClientHandler();
            if (allowUntrusted)
                handler.ServerCertificateCustomValidationCallback = delegate { return true; };

            adminHttp = new HttpClient(handler)
            {
                BaseAddress = new Uri(connection.HttpConnectionString.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(100)
            };
            if (!string.IsNullOrWhiteSpace(connection.Token))
                adminHttp.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", connection.Token);
            adminHttp.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public override async Task<IEnumerable<string>> GetDatabaseNamesAsync()
        {
            var json = await SendAsync(HttpMethod.Get, "api/v3/configure/database?format=json");
            var token = JToken.Parse(json);
            var databases = token as JArray ?? token["databases"] as JArray ?? new JArray();
            return databases
                .Select(item => item.Type == JTokenType.String
                    ? (string)item
                    : (string)item["iox::database"] ?? (string)item["name"])
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();
        }

        public override async Task<InfluxDbApiResponse> CreateDatabaseAsync(string database)
        {
            Require(database, "database");
            return await SendForResponseAsync(HttpMethod.Post, "api/v3/configure/database",
                new JObject { ["db"] = database }.ToString(Formatting.None));
        }

        public override async Task<InfluxDbApiResponse> DropDatabaseAsync(string database)
        {
            Require(database, "database");
            return await SendForResponseAsync(HttpMethod.Delete,
                "api/v3/configure/database?db=" + Uri.EscapeDataString(database));
        }

        public override async Task<IEnumerable<string>> GetMeasurementNamesAsync(string database)
        {
            var series = await QuerySqlAsync(database,
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'iox' ORDER BY table_name");
            return ColumnValues(series, "table_name");
        }

        public override async Task<InfluxDbApiResponse> DropMeasurementAsync(string database, string measurement)
        {
            Require(database, "database");
            Require(measurement, "measurement");
            var path = "api/v3/configure/table?db=" + Uri.EscapeDataString(database)
                + "&table=" + Uri.EscapeDataString(measurement);
            return await SendForResponseAsync(HttpMethod.Delete, path);
        }

        public override async Task<IEnumerable<string>> GetTagKeysAsync(string database, string measurement)
        {
            var columns = await GetSchemaColumnsAsync(database, measurement);
            return columns.Where(c => IsTagType(c.Type)).Select(c => c.Name).ToList();
        }

        public override async Task<IEnumerable<InfluxDbTagValue>> GetTagValuesAsync(
            string database, string measurement, string tag)
        {
            Require(tag, "tag");
            var series = await QuerySqlAsync(database, "SELECT DISTINCT " + QuoteIdentifier(tag)
                + " FROM " + QuoteIdentifier(measurement) + " ORDER BY " + QuoteIdentifier(tag));
            return ColumnValues(series, tag).Select(value => new InfluxDbTagValue(tag, value)).ToList();
        }

        public override async Task<IEnumerable<InfluxDbFieldKey>> GetFieldKeysAsync(
            string database, string measurement)
        {
            var columns = await GetSchemaColumnsAsync(database, measurement);
            return columns.Where(c => !IsTagType(c.Type) && !string.Equals(c.Name, "time", StringComparison.OrdinalIgnoreCase))
                .Select(c => new InfluxDbFieldKey(c.Name, c.Type)).ToList();
        }

        /// <summary>Whole-database schema from a single information_schema query.</summary>
        public override async Task<IReadOnlyList<InfluxDbMeasurementSchema>> GetDatabaseSchemaAsync(string database)
        {
            Require(database, "database");
            var series = await QuerySqlAsync(database,
                "SELECT table_name, column_name, data_type FROM information_schema.columns "
                + "WHERE table_schema = 'iox' ORDER BY table_name, ordinal_position");
            return InfluxDbMeasurementSchema.FromInformationSchema(series);
        }

        public override async Task<IEnumerable<InfluxDbSeries>> QueryAsync(string database, string query)
        {
            Require(database, "database");
            Require(query, "query");
            return await QuerySqlAsync(database, query);
        }

        public override async Task<IEnumerable<InfluxDbSeries>> QueryAsync(string database, string query, CancellationToken cancellationToken)
        {
            Require(database, "database");
            Require(query, "query");
            // WaitAsync returns as soon as Stop is pressed, even while the server has not sent the first batch.
            return await QuerySqlAsync(database, query, cancellationToken).WaitAsync(cancellationToken);
        }

        public override async Task<InfluxDbApiResponse> WriteAsync(string database, string measurement,
            IDictionary<string, object> tags, IDictionary<string, object> fields, DateTime timeStamp,
            string retentionPolicy = null)
        {
            return await WriteAsync(database, new InfluxDbPoint(measurement, tags, fields, timeStamp), retentionPolicy);
        }

        public override async Task<InfluxDbApiResponse> WriteAsync(
            string database, InfluxDbPoint point, string retentionPolicy = null)
        {
            return await WriteAsync(database, new[] { point }, retentionPolicy);
        }

        public override async Task<InfluxDbApiResponse> WriteAsync(
            string database, IEnumerable<InfluxDbPoint> points, string retentionPolicy = null)
        {
            Require(database, "database");
            if (points == null) throw new ArgumentNullException("points");
            await influx.WriteRecordsAsync(points.Select(ToLineProtocol), database, WritePrecision.Ns);
            return SuccessResponse();
        }

        public override async Task<InfluxDbPingResponse> PingAsync()
        {
            var watch = Stopwatch.StartNew();
            var version = await influx.GetServerVersion().ConfigureAwait(false);
            watch.Stop();
            return new InfluxDbPingResponse(true, watch.Elapsed,
                string.IsNullOrWhiteSpace(version) ? "3.x" : version);
        }

        public override Task<IEnumerable<InfluxDbRetentionPolicy>> GetRetentionPoliciesAsync(string database) =>
            Unsupported<IEnumerable<InfluxDbRetentionPolicy>>("Retention policies are replaced by database retention periods in InfluxDB 3.");
        public override Task<InfluxDbApiResponse> CreateRetentionPolicyAsync(string database, string policyName, string duration, int replication, bool isDefault = false) =>
            Unsupported<InfluxDbApiResponse>("InfluxDB 3 does not support InfluxDB 1.x retention policies.");
        public override Task<InfluxDbApiResponse> AlterRetentionPolicyAsync(string database, string policyName, string duration, int replication, bool isDefault = false) =>
            Unsupported<InfluxDbApiResponse>("InfluxDB 3 does not support InfluxDB 1.x retention policies.");
        public override Task<InfluxDbApiResponse> DropRetentionPolicyAsync(string database, string policyName) =>
            Unsupported<InfluxDbApiResponse>("InfluxDB 3 does not support InfluxDB 1.x retention policies.");
        public override Task<IEnumerable<string>> GetSeriesNamesAsync(string database, string measurement = null) =>
            Unsupported<IEnumerable<string>>("SHOW SERIES is an InfluxDB 1.x operation.");
        public override Task<InfluxDbApiResponse> DropSeriesAsync(string database, string measurement = null) =>
            Unsupported<InfluxDbApiResponse>("DROP SERIES is an InfluxDB 1.x operation.");
        public override Task<IEnumerable<InfluxDbRunningQuery>> GetRunningQueriesAsync() =>
            Unsupported<IEnumerable<InfluxDbRunningQuery>>("Use the InfluxDB 3 system.queries table from a SQL query.");
        public override Task<InfluxDbApiResponse> KillQueryAsync(int pid) =>
            Unsupported<InfluxDbApiResponse>("KILL QUERY is an InfluxDB 1.x operation.");
        public override Task<IEnumerable<InfluxDbContinuousQuery>> GetContinousQueriesAsync(string database) =>
            Unsupported<IEnumerable<InfluxDbContinuousQuery>>("Continuous queries are not supported by InfluxDB 3.");
        public override Task<InfluxDbApiResponse> CreateContinuousQueryAsync(InfluxDbCqParams cqParams) =>
            Unsupported<InfluxDbApiResponse>("Continuous queries are not supported by InfluxDB 3.");
        public override Task<InfluxDbApiResponse> DropContinuousQueryAsync(string database, string cqName) =>
            Unsupported<InfluxDbApiResponse>("Continuous queries are not supported by InfluxDB 3.");
        public override Task<InfluxDbApiResponse> BackfillAsync(string database, InfluxDbBackfillParams backfillParams) =>
            Unsupported<InfluxDbApiResponse>("The legacy InfluxQL backfill builder is not supported for InfluxDB 3.");
        public override Task<InfluxDbDiagnostics> GetDiagnosticsAsync() =>
            Unsupported<InfluxDbDiagnostics>("The InfluxDB 1.x diagnostics endpoint is not available in InfluxDB 3.");
        public override Task<InfluxDbStats> GetStatsAsync() =>
            Unsupported<InfluxDbStats>("The InfluxDB 1.x statistics endpoint is not available in InfluxDB 3.");
        public override Task<IEnumerable<InfluxDbUser>> GetUsersAsync() =>
            Unsupported<IEnumerable<InfluxDbUser>>("InfluxDB 3 uses token-based authentication.");
        public override Task<InfluxDbApiResponse> CreateUserAsync(string username, string password, bool isAdmin) =>
            Unsupported<InfluxDbApiResponse>("InfluxDB 3 uses token-based authentication.");
        public override Task<InfluxDbApiResponse> DropUserAsync(string username) =>
            Unsupported<InfluxDbApiResponse>("InfluxDB 3 uses token-based authentication.");
        public override Task<InfluxDbApiResponse> SetPasswordAsync(string username, string password) =>
            Unsupported<InfluxDbApiResponse>("InfluxDB 3 uses token-based authentication.");
        public override Task<IEnumerable<InfluxDbGrant>> GetPrivilegesAsync(string username) =>
            Unsupported<IEnumerable<InfluxDbGrant>>("InfluxDB 3 uses token-based authentication.");
        public override Task<InfluxDbApiResponse> GrantAdministratorAsync(string username) =>
            Unsupported<InfluxDbApiResponse>("InfluxDB 3 uses token-based authentication.");
        public override Task<InfluxDbApiResponse> RevokeAdministratorAsync(string username) =>
            Unsupported<InfluxDbApiResponse>("InfluxDB 3 uses token-based authentication.");
        public override Task<InfluxDbApiResponse> GrantPrivilegeAsync(string username, InfluxDbPrivileges privilege, string database) =>
            Unsupported<InfluxDbApiResponse>("InfluxDB 3 uses token-based authentication.");
        public override Task<InfluxDbApiResponse> RevokePrivilegeAsync(string username, InfluxDbPrivileges privilege, string database) =>
            Unsupported<InfluxDbApiResponse>("InfluxDB 3 uses token-based authentication.");

        private async Task<IEnumerable<InfluxDbSeries>> QuerySqlAsync(string database, string query,
            CancellationToken cancellationToken = default)
        {
            var columns = new List<string>();
            var values = new List<IList<object>>();
            await foreach (var batch in influx.QueryBatches(query, database: database).WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (columns.Count == 0)
                    columns.AddRange(batch.Schema.FieldsList.Select(field => field.Name));

                for (var rowIndex = 0; rowIndex < batch.Length; rowIndex++)
                {
                    var row = new List<object>();
                    for (var columnIndex = 0; columnIndex < batch.ColumnCount; columnIndex++)
                        row.Add(GetArrowValue(batch.Column(columnIndex), rowIndex));
                    values.Add(row);
                }
            }
            if (columns.Count == 0) return new List<InfluxDbSeries>();
            return new[] { new InfluxDbSeries(null, columns, new Dictionary<string, string>(), values) };
        }

        private async Task<IList<SchemaColumn>> GetSchemaColumnsAsync(string database, string measurement)
        {
            Require(measurement, "measurement");
            var query = "SELECT column_name, data_type FROM information_schema.columns "
                + "WHERE table_schema = 'iox' AND table_name = " + QuoteLiteral(measurement)
                + " ORDER BY ordinal_position";
            var series = await QuerySqlAsync(database, query);
            var result = new List<SchemaColumn>();
            foreach (var item in series)
            {
                var nameIndex = item.GetColumnIndex("column_name");
                var typeIndex = item.GetColumnIndex("data_type");
                foreach (var row in item.Values)
                    result.Add(new SchemaColumn(Convert.ToString(row[nameIndex]), Convert.ToString(row[typeIndex])));
            }
            return result;
        }

        private async Task<string> SendAsync(HttpMethod method, string path, string body = null, string mediaType = "application/json")
        {
            using (var request = new HttpRequestMessage(method, path))
            {
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, mediaType);
                using (var response = await adminHttp.SendAsync(request).ConfigureAwait(false))
                {
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) throw CreateHttpException(response.StatusCode, responseBody);
                    return responseBody;
                }
            }
        }

        private async Task<InfluxDbApiResponse> SendForResponseAsync(
            HttpMethod method, string path, string body = null, string mediaType = "application/json")
        {
            using (var request = new HttpRequestMessage(method, path))
            {
                if (body != null) request.Content = new StringContent(body, Encoding.UTF8, mediaType);
                using (var response = await adminHttp.SendAsync(request).ConfigureAwait(false))
                {
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) throw CreateHttpException(response.StatusCode, responseBody);
                    return new InfluxDbApiResponse(string.IsNullOrWhiteSpace(responseBody) ? "{}" : responseBody,
                        response.StatusCode, true);
                }
            }
        }

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

        private static object GetArrowValue(IArrowArray array, int index)
        {
            if (array == null || array.IsNull(index)) return null;

            var type = array.GetType();
            if (type.Name.StartsWith("DictionaryArray", StringComparison.Ordinal))
            {
                var indices = (IArrowArray)type.GetProperty("Indices").GetValue(array);
                var dictionary = (IArrowArray)type.GetProperty("Dictionary").GetValue(array);
                var dictionaryIndex = Convert.ToInt32(GetArrowValue(indices, index), CultureInfo.InvariantCulture);
                return GetArrowValue(dictionary, dictionaryIndex);
            }

            foreach (var methodName in new[] { "GetValue", "GetString", "GetTimestamp", "GetDateTime" })
            {
                var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(int) }, null);
                if (method != null) return method.Invoke(array, new object[] { index });
            }

            return array.ToString();
        }

        private static string ToLineProtocol(InfluxDbPoint point)
        {
            if (point == null) throw new ArgumentNullException("point");
            if (point.Fields == null || point.Fields.Count == 0)
                throw new ArgumentException("An InfluxDB point must contain at least one field.", "point");

            var builder = new StringBuilder(EscapeKey(point.Measurement));
            if (point.Tags != null)
                foreach (var tag in point.Tags.OrderBy(item => item.Key))
                    builder.Append(',').Append(EscapeKey(tag.Key)).Append('=').Append(EscapeKey(Convert.ToString(tag.Value, CultureInfo.InvariantCulture)));

            builder.Append(' ');
            builder.Append(string.Join(",", point.Fields.OrderBy(item => item.Key)
                .Select(field => EscapeKey(field.Key) + "=" + FormatField(field.Value))));
            var utc = point.TimeStamp.Kind == DateTimeKind.Utc ? point.TimeStamp : point.TimeStamp.ToUniversalTime();
            var nanoseconds = (utc.Ticks - DateTime.UnixEpoch.Ticks) * 100L;
            builder.Append(' ').Append(nanoseconds.ToString(CultureInfo.InvariantCulture));
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
            (value ?? string.Empty).Replace("\\", "\\\\").Replace(" ", "\\ ").Replace(",", "\\,").Replace("=", "\\=");
        private static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        private static string QuoteLiteral(string value) => "'" + value.Replace("'", "''") + "'";
        private static bool IsTagType(string type) =>
            type != null && type.IndexOf("Dictionary", StringComparison.OrdinalIgnoreCase) >= 0;
        private static void Require(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(name);
        }
        private static Task<T> Unsupported<T>(string message) =>
            Task.FromException<T>(new NotSupportedException(message));
        private static Exception CreateHttpException(HttpStatusCode statusCode, string body) =>
            new HttpRequestException("InfluxDB 3 request failed (" + (int)statusCode + " " + statusCode + "): "
                + (string.IsNullOrWhiteSpace(body) ? "No response body." : body));
        private static InfluxDbApiResponse SuccessResponse() =>
            new InfluxDbApiResponse("{}", HttpStatusCode.NoContent, true);

        private sealed class SchemaColumn
        {
            public string Name { get; private set; }
            public string Type { get; private set; }
            public SchemaColumn(string name, string type) { Name = name; Type = type; }
        }
    }
}
